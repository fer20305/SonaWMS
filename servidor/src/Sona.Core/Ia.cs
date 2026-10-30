using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Sona;

/// <summary>
/// Reconocimiento de artículos por foto, propio y sin costo por consulta.
/// Usa un modelo de visión ya entrenado (DINOv2 small, formato ONNX, ~90 MB, se baja una sola vez) que convierte cada imagen en una
/// "huella" de 384 números. Se calcula la huella de las fotos de tradebox de cada artículo y, al consultar, se buscan las más parecidas.
/// Cada vez que un administrador confirma cuál era, esa foto real también queda en el catálogo (aprende con el uso).
/// Todo corre en la PC del servidor.
/// </summary>
public sealed class Reconocedor : IDisposable
{
    public const string UrlModelo = "https://huggingface.co/onnx-community/dinov2-small/resolve/main/onnx/model.onnx";
    const int Lado = 224;
    static readonly float[] Media = { 0.485f, 0.456f, 0.406f }, Desvio = { 0.229f, 0.224f, 0.225f };

    public const string Esquema = """
CREATE TABLE IF NOT EXISTS ia_vectores (
  id INTEGER PRIMARY KEY AUTOINCREMENT, articulo TEXT NOT NULL, origen TEXT NOT NULL, fuente TEXT, vector BLOB NOT NULL, creado TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ia_vectores_art ON ia_vectores(articulo);
CREATE TABLE IF NOT EXISTS ia_articulos (articulo TEXT PRIMARY KEY, intentado TEXT, fotos INTEGER);
CREATE TABLE IF NOT EXISTS ia_consultas (
  foto TEXT PRIMARY KEY, usuario TEXT, creado TEXT NOT NULL, candidatos TEXT, estado TEXT NOT NULL DEFAULT 'pendiente',
  articulo TEXT, resuelto_por TEXT, resuelto TEXT);
CREATE INDEX IF NOT EXISTS ia_consultas_estado ON ia_consultas(estado, creado);
""";

    readonly Configuracion _cfg;
    readonly Db _db;
    readonly Servicios _sv;
    readonly Eventos _ev;
    readonly HttpClient _http;
    readonly ILogger _log;
    readonly SemaphoreSlim _cargar = new(1, 1);
    InferenceSession? _sesion;
    List<(string Articulo, float[] V)>? _indice;
    CancellationTokenSource? _trabajo;

    public Reconocedor(Configuracion cfg, Db db, Servicios sv, Eventos ev, ILogger<Reconocedor> log)
    {
        _cfg = cfg; _db = db; _sv = sv; _ev = ev; _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
    }

    string Carpeta { get { var d = Path.Combine(_cfg.Carpeta, "ia"); Directory.CreateDirectory(d); return d; } }
    string ArchivoModelo => Path.Combine(Carpeta, "dinov2-small.onnx");
    string CarpetaFotos { get { var d = Path.Combine(Carpeta, "fotos"); Directory.CreateDirectory(d); return d; } }

    public bool ModeloBajado => File.Exists(ArchivoModelo);
    public bool Indexando => _trabajo is { IsCancellationRequested: false };
    public string? Progreso { get; private set; }
    public string? UltimoError { get; private set; }

    public object Estado() => new
    {
        modelo = File.Exists(ArchivoModelo), cargado = _sesion != null,
        articulos = _db.Contar("SELECT count(DISTINCT articulo) FROM ia_vectores"),
        vectores = _db.Contar("SELECT count(*) FROM ia_vectores"),
        confirmadas = _db.Contar("SELECT count(*) FROM ia_vectores WHERE origen='confirmada'"),
        sinIdentificar = _db.Contar("SELECT count(*) FROM ia_consultas WHERE estado IN ('pendiente','ninguno')"),
        conocidos = _db.Contar("SELECT count(*) FROM articulos"),
        pendientes = _db.Contar("SELECT count(*) FROM articulos a WHERE NOT EXISTS (SELECT 1 FROM ia_articulos i WHERE i.articulo = a.codigo)"),
        sinFoto = _db.Contar("SELECT count(*) FROM ia_articulos WHERE fotos = 0"),
        indexando = Indexando, progreso = Progreso, ultimoError = UltimoError,
    };

    // ------------------------------------------------------------------ modelo
    async Task<InferenceSession> Sesion()
    {
        if (_sesion != null) return _sesion;
        await _cargar.WaitAsync();
        try
        {
            if (_sesion != null) return _sesion;
            if (!File.Exists(ArchivoModelo))
            {
                Progreso = "Bajando el modelo de reconocimiento (≈90 MB, una sola vez)…";
                _log.LogInformation("Bajando modelo de IA desde {Url}", UrlModelo);
                var tmp = ArchivoModelo + ".bajando";
                using (var r = await _http.GetAsync(UrlModelo, HttpCompletionOption.ResponseHeadersRead))
                {
                    r.EnsureSuccessStatusCode();
                    await using var o = File.Create(tmp);
                    await (await r.Content.ReadAsStreamAsync()).CopyToAsync(o);
                }
                if (new FileInfo(tmp).Length < 10_000_000) { File.Delete(tmp); throw new Exception("El modelo bajado está incompleto."); }
                File.Move(tmp, ArchivoModelo, true);
            }
            var op = new SessionOptions { IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2), GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            _sesion = new InferenceSession(ArchivoModelo, op);
            Progreso = null;
            return _sesion;
        }
        catch (Exception e) { UltimoError = "Modelo: " + e.Message; Progreso = null; throw new ErrorUsuario("No se pudo cargar el modelo de reconocimiento: " + e.Message, 503); }
        finally { _cargar.Release(); }
    }

    /// <summary>Huella de una imagen: se achica el lado corto a 256, se recorta el centro a 224×224 y se normaliza.</summary>
    public async Task<float[]> Huella(byte[] imagen)
    {
        var s = await Sesion();
        var entrada = new DenseTensor<float>(new[] { 1, 3, Lado, Lado });
        using (var original = SKBitmap.Decode(imagen) ?? throw new ErrorUsuario("No se pudo leer la imagen."))
        {
            var escala = 256f / Math.Min(original.Width, original.Height);
            var w = Math.Max(Lado, (int)Math.Round(original.Width * escala));
            var h = Math.Max(Lado, (int)Math.Round(original.Height * escala));
            using var chico = original.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.Medium) ?? throw new ErrorUsuario("No se pudo achicar la imagen.");
            int x0 = (w - Lado) / 2, y0 = (h - Lado) / 2;
            for (var y = 0; y < Lado; y++)
                for (var x = 0; x < Lado; x++)
                {
                    var c = chico.GetPixel(x0 + x, y0 + y);
                    entrada[0, 0, y, x] = (c.Red / 255f - Media[0]) / Desvio[0];
                    entrada[0, 1, y, x] = (c.Green / 255f - Media[1]) / Desvio[1];
                    entrada[0, 2, y, x] = (c.Blue / 255f - Media[2]) / Desvio[2];
                }
        }
        var nombre = s.InputMetadata.Keys.First();
        using var res = s.Run(new[] { NamedOnnxValue.CreateFromTensor(nombre, entrada) });
        var salida = res.FirstOrDefault(r => r.Name == "pooler_output") ?? res.First();
        var t = salida.AsTensor<float>();
        float[] v;
        if (t.Dimensions.Length == 3) { v = new float[t.Dimensions[2]]; for (var i = 0; i < v.Length; i++) v[i] = t[0, 0, i]; }   // token CLS
        else v = t.ToArray();
        var n = (float)Math.Sqrt(v.Sum(x => x * x));
        if (n > 0) for (var i = 0; i < v.Length; i++) v[i] /= n;
        return v;
    }

    static byte[] ABytes(float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); return b; }
    static float[] AVector(byte[] b) { var v = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, v, 0, b.Length); return v; }

    List<(string Articulo, float[] V)> Indice() => _indice ??= _db.Q("SELECT articulo, vector FROM ia_vectores")
        .Where(f => f["vector"] is byte[]).Select(f => (f.S("articulo")!, AVector((byte[])f["vector"]!))).ToList();

    void Agregar(string articulo, string origen, string? fuente, float[] v)
    {
        _db.Ejecutar("INSERT INTO ia_vectores (articulo, origen, fuente, vector, creado) VALUES ($1,$2,$3,$4,$5)", articulo, origen, fuente, ABytes(v), Db.Ahora());
        _indice?.Add((articulo, v));
    }

    // ------------------------------------------------------------------ consulta
    public sealed record Candidato(string Articulo, double Similitud, string? Descripcion, string? Empresa, string? Foto, int Muestras);

    /// <summary>Guarda la foto, calcula su huella y devuelve los artículos más parecidos (mejor coincidencia por artículo).</summary>
    public async Task<object> Reconocer(byte[] foto, string usuario, int cuantos = 5)
    {
        var reloj = Stopwatch.StartNew();
        var v = await Huella(foto);
        var id = Guid.NewGuid().ToString("N") + ".jpg";
        await File.WriteAllBytesAsync(Path.Combine(CarpetaFotos, id), foto);
        var idx = Indice();
        if (idx.Count == 0)
        {
            Registrar(id, usuario, new List<Candidato>());
            return new { fotoId = id, candidatos = Array.Empty<Candidato>(), mensaje = "El catálogo todavía está vacío: indexá los artículos desde el panel (ZIA). La foto queda guardada en \"Sin identificar\"." };
        }
        var mejores = idx.Select(e => (e.Articulo, Sim: (double)Producto(v, e.V)))
            .GroupBy(e => e.Articulo).Select(g => (Articulo: g.Key, Sim: g.Max(x => x.Sim), N: g.Count()))
            .OrderByDescending(x => x.Sim).Take(cuantos).ToList();
        var lista = new List<Candidato>();
        foreach (var m in mejores)
        {
            var a = _db.Q1("SELECT descripcion, empresa_nombre FROM articulos WHERE codigo=$1", m.Articulo);
            string? f = null;
            try { var fotos = await _sv.FotosArticulo(m.Articulo); f = fotos.Count > 0 ? Servicios.UrlImagen(fotos[0].Url) : null; } catch { }
            lista.Add(new Candidato(m.Articulo, Math.Round(m.Sim, 3), a?.S("descripcion"), a?.S("empresa_nombre"), f, m.N));
        }
        Registrar(id, usuario, lista);
        return new { fotoId = id, candidatos = lista, milisegundos = reloj.ElapsedMilliseconds };
    }

    void Registrar(string foto, string usuario, List<Candidato> lista) =>
        _db.Ejecutar("INSERT INTO ia_consultas (foto, usuario, creado, candidatos) VALUES ($1,$2,$3,$4)", foto, usuario, Db.Ahora(),
            new System.Text.Json.Nodes.JsonArray(lista.Select(c => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                { ["articulo"] = c.Articulo, ["similitud"] = c.Similitud, ["descripcion"] = c.Descripcion }).ToArray()));

    public static string UrlFoto(string id) => "/api/fotos/ia/" + id;
    public string? ArchivoFoto(string id) { var f = Path.Combine(CarpetaFotos, Path.GetFileName(id)); return File.Exists(f) ? f : null; }

    /// <summary>Historial de fotos consultadas. <paramref name="filtro"/>: pendientes (sin identificar), confirmadas, descartadas o todas.</summary>
    public List<Fila> Historial(string filtro, int limite = 300)
    {
        var where = filtro switch
        {
            "confirmadas" => "WHERE estado='confirmada'",
            "descartadas" => "WHERE estado='descartada'",
            "todas" => "",
            _ => "WHERE estado IN ('pendiente','ninguno')",
        };
        var filas = _db.Q($"SELECT * FROM ia_consultas {where} ORDER BY creado DESC LIMIT {Math.Clamp(limite, 1, 2000)}");
        foreach (var f in filas)
        {
            f["url"] = UrlFoto(f.S("foto")!);
            var mejor = (f.GetValueOrDefault("candidatos") as System.Text.Json.Nodes.JsonArray)?.FirstOrDefault();
            f["sugerido"] = mejor?["articulo"]?.ToString();
            f["similitud"] = mejor?["similitud"]?.GetValue<double>();
        }
        return filas;
    }

    void Resolver(string fotoId, string estado, string? articulo, string usuario)
    {
        var id = Path.GetFileName(fotoId);
        // si ya se había confirmado (con otro artículo), se saca esa huella para no enseñarle mal
        if (_db.Ejecutar("DELETE FROM ia_vectores WHERE origen='confirmada' AND fuente=$1", id) > 0) _indice = null;
        var n = _db.Ejecutar("UPDATE ia_consultas SET estado=$2, articulo=$3, resuelto_por=$4, resuelto=$5 WHERE foto=$1", id, estado, articulo, usuario, Db.Ahora());
        if (n == 0) _db.Ejecutar("INSERT INTO ia_consultas (foto, usuario, creado, estado, articulo, resuelto_por, resuelto) VALUES ($1,$2,$3,$4,$5,$2,$3)", id, usuario, Db.Ahora(), estado, articulo);
    }

    /// <summary>Ninguno de los sugeridos era: la foto queda en "sin identificar" para asignarla después.</summary>
    public void Ninguno(string fotoId, string usuario) => Resolver(fotoId, "ninguno", null, usuario);

    /// <summary>Foto que no sirve (borrosa, no es un producto): no se usa para aprender.</summary>
    public void Descartar(string fotoId, string usuario) => Resolver(fotoId, "descartada", null, usuario);

    static float Producto(float[] a, float[] b) { float s = 0; for (var i = 0; i < a.Length && i < b.Length; i++) s += a[i] * b[i]; return s; }

    /// <summary>El administrador confirmó qué artículo era: la foto real se suma al catálogo.</summary>
    public async Task Confirmar(string fotoId, string articulo, string usuario)
    {
        var archivo = Path.Combine(CarpetaFotos, Path.GetFileName(fotoId));
        if (!File.Exists(archivo)) throw new ErrorUsuario("No existe la foto.", 404);
        var art = articulo.Trim().ToUpperInvariant();
        if (art.Length == 0) throw new ErrorUsuario("Falta el código de artículo.");
        var huella = await Huella(await File.ReadAllBytesAsync(archivo));
        Resolver(fotoId, "confirmada", art, usuario);
        Agregar(art, "confirmada", Path.GetFileName(fotoId), huella);
        _db.Auditar("ia", art, new { foto = fotoId }, usuario);
    }

    // ------------------------------------------------------------------ catálogo
    /// <summary>Calcula las huellas de las fotos de tradebox de los artículos que todavía no se procesaron (o de todos si <paramref name="rehacer"/>).</summary>
    public void Indexar(bool traerCatalogo, bool rehacer = false)
    {
        if (Indexando) return;
        var cts = new CancellationTokenSource();
        _trabajo = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                UltimoError = null;
                await Sesion();
                if (traerCatalogo)
                {
                    var nuevos = 0;
                    for (var pag = 1; pag <= 500 && !cts.IsCancellationRequested; pag++)
                    {
                        Progreso = $"Trayendo el maestro de artículos de tradebox (página {pag}, {nuevos} nuevos)…";
                        var cods = await _sv.Les.ListarArticulos(pag);
                        if (cods.Count == 0) break;
                        foreach (var c in cods) nuevos += _db.Ejecutar("INSERT OR IGNORE INTO articulos (codigo) VALUES ($1)", c);
                    }
                }
                if (rehacer) { _db.Ejecutar("DELETE FROM ia_articulos"); }
                var lista = _db.Q("SELECT codigo FROM articulos a WHERE NOT EXISTS (SELECT 1 FROM ia_articulos i WHERE i.articulo = a.codigo) ORDER BY codigo").Select(f => f.S("codigo")!).ToList();
                var hechos = 0;
                foreach (var art in lista)
                {
                    if (cts.IsCancellationRequested) break;
                    hechos++;
                    Progreso = $"Procesando fotos: {hechos} de {lista.Count} artículos ({art})";
                    var n = 0;
                    try
                    {
                        var fotos = await _sv.FotosArticulo(art);
                        _db.Ejecutar("DELETE FROM ia_vectores WHERE articulo=$1 AND origen='catalogo'", art);
                        _indice = null;
                        foreach (var f in fotos.Take(4))
                        {
                            var img = await _sv.Les.ImagenCacheada(f.Url);
                            if (img == null) continue;
                            try { Agregar(art, "catalogo", f.Url, await Huella(await File.ReadAllBytesAsync(img.Value.Archivo))); n++; } catch (ErrorUsuario) { }
                        }
                    }
                    catch (Exception e) { _log.LogWarning("IA: no se pudo procesar {Art}: {E}", art, e.Message); }
                    _db.Ejecutar("INSERT INTO ia_articulos (articulo, intentado, fotos) VALUES ($1,$2,$3) ON CONFLICT(articulo) DO UPDATE SET intentado=excluded.intentado, fotos=excluded.fotos", art, Db.Ahora(), n);
                    if (hechos % 25 == 0) _ev.Emitir(new() { ["tipo"] = "ia", ["progreso"] = Progreso }, (u, rol) => Seguridad.RolesPanel.Contains(rol));
                }
                Progreso = null;
                _indice = null;
                _log.LogInformation("IA: catálogo actualizado ({N} artículos procesados)", hechos);
            }
            catch (Exception e) { UltimoError = e.Message; Progreso = null; _log.LogWarning("IA: {E}", e.Message); }
            finally { _trabajo = null; _ev.Emitir(new() { ["tipo"] = "ia", ["progreso"] = null }, (u, rol) => Seguridad.RolesPanel.Contains(rol)); }
        });
    }

    public void Detener() => _trabajo?.Cancel();

    public void Dispose() { _sesion?.Dispose(); _http.Dispose(); }
}

/// <summary>Cada 6 horas suma al catálogo los artículos nuevos (solo si el modelo ya está bajado).</summary>
public sealed class IaFondo(Reconocedor ia) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { if (ia.ModeloBajado) ia.Indexar(false); } catch { }
            try { await Task.Delay(TimeSpan.FromHours(6), ct); } catch (OperationCanceledException) { break; }
        }
    }
}
