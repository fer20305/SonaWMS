using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sona.Wms;

namespace Sona.Les;

/// <summary>Un bloque del buscador SONA (CLAVE: valor).</summary>
public sealed class Registro
{
    public Dictionary<string, string> Campos { get; } = new();
    string? G(params string[] ks) => ks.Select(k => Campos.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    public string? Contenedor => G("CONTENEDOR", "CONT", "PALLET");
    public string? Articulo => G("CODIGO_ARTICULO", "ARTICULO", "COD_ARTICULO");
    public string? Descripcion => G("DESCRIPCION_ARTICULO", "DESCRIPCION");
    public string? Cliente => G("CLIENTE", "EMPRESA");
    public double? Cantidad => Buscador.Numero(G("CANTIDAD", "CANT", "SALDO"));
    public string? Posicion => G("POS", "POSICION", "UBICACION");
    public double? M3 => Buscador.Numero(G("M3", "M3_TOTAL", "VOLUMEN", "VOLUMEN_M3", "MTS3", "CUBICAJE"));
    public double? Bultos => Buscador.Numero(G("BULTOS", "CAJAS"));
    /// <summary>Disponibilidad / calidad del contenedor si el buscador la informa (el nombre del campo varía).</summary>
    public string? Estado => G("ESTADO", "DISPONIBILIDAD", "CALIDAD", "ESTADO_CALIDAD", "DISPONIBLE")
        ?? Campos.Where(kv => Regex.IsMatch(kv.Key, "DISPON|CALIDAD|ESTADO|STATUS") && !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Value).FirstOrDefault();
}

public sealed record Foto(string Nombre, string Url);

/// <summary>Ficha del artículo en LES (tradebox › General › Artículos): habilitado, cliente y stock según LES.</summary>
public sealed record FichaLes(long Id, string Codigo, string? Descripcion, bool? Habilitado, int? IdCliente, string? Cliente, string? Marca, string? Proyecto,
    double? StockFisico, double? StockDisponible, double? StockNoDisponible, double? StockReservado, double? StockPedido, bool? PublicadoPedidos);

/// <summary>Buscador SONA (les.les-ejemplo.com/Api/Buscador): detalle de contenedores y artículos.</summary>
public sealed class Buscador(Configuracion cfg, HttpClient http)
{
    public static double? Numero(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = Regex.Replace(s.Trim(), @"\s", "");
        if (t.Contains(',') && t.Contains('.')) t = t.LastIndexOf(',') > t.LastIndexOf('.') ? t.Replace(".", "").Replace(",", ".") : t.Replace(",", "");
        else if (t.Contains(',')) t = t.Replace(",", ".");
        else if (Regex.IsMatch(t, @"^-?\d{1,3}(\.\d{3})+$")) t = t.Replace(".", "");
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    static readonly Regex ReLinea = new(@"^\s*([A-Za-zÁÉÍÓÚÑáéíóúñ0-9_ .()/#-]{1,60}?)\s*:\s?(.*)$");

    public static List<Registro> LeerResultado(string txt)
    {
        var regs = new List<Registro>();
        var actual = new Registro();
        void Cerrar() { if (actual.Campos.Count > 0) regs.Add(actual); actual = new Registro(); }
        foreach (var linea in txt.Replace("\r", "").Split('\n'))
        {
            if (Regex.IsMatch(linea, @"^\s*/{2,}-*\s*$")) { Cerrar(); continue; }
            var m = ReLinea.Match(linea);
            if (m.Success) actual.Campos[m.Groups[1].Value.Trim().ToUpperInvariant()] = m.Groups[2].Value.Trim();
        }
        Cerrar();
        return regs;
    }

    public async Task<List<Registro>> Buscar(string storeProcedure, string valor)
    {
        var url = cfg.Les.Buscador.TrimEnd('/') + "/GetInfoBusqueda?storeProcedure=" + Uri.EscapeDataString(storeProcedure) + "&valor=" + Uri.EscapeDataString(valor);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/json");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var r = await http.SendAsync(req, cts.Token);
        var cuerpo = await r.Content.ReadAsStringAsync(cts.Token);
        if ((int)r.StatusCode >= 400) throw new Exception($"Buscador SONA respondió {(int)r.StatusCode}");
        var txt = cuerpo;
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            var data = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("Data", out var d) ? d : doc.RootElement;
            static string Uno(JsonElement e) => e.ValueKind switch
            {
                JsonValueKind.String => e.GetString() ?? "",
                JsonValueKind.Object when e.TryGetProperty("ResultadoTXT", out var t) => t.GetString() ?? "",
                JsonValueKind.Object => string.Join("\n", e.EnumerateObject().Select(p => $"{p.Name}: {p.Value}")),
                _ => "",
            };
            txt = data.ValueKind == JsonValueKind.Array ? string.Join("\n//------\n", data.EnumerateArray().Select(Uno)) : Uno(data);
        }
        catch (JsonException) { /* texto plano */ }
        return LeerResultado(txt);
    }

    public Task<List<Registro>> Contenedor(string c) => Buscar("BOT_BusquedaContenedor", c);
    public Task<List<Registro>> Articulo(string a) => Buscar("BOT_BusquedaArticulo", a);
}

/// <summary>Fotos de artículos de les.tradebox (con usuario y clave de tradebox) y caché de imágenes en disco.</summary>
public sealed class FotosLes
{
    readonly Configuracion _cfg;
    readonly Navegador _nav = new(30);
    readonly SemaphoreSlim _cola = new(1, 1);
    readonly HttpClient _http;
    bool _logueado;

    public FotosLes(Configuracion cfg, HttpClient http) { _cfg = cfg; _http = http; }

    static bool EsLogin(string t) => Regex.IsMatch(t, @"Account/Log(On|In)", RegexOptions.IgnoreCase) || (t.Contains("name=\"pass\"", StringComparison.OrdinalIgnoreCase) && t.Contains("name=\"user\"", StringComparison.OrdinalIgnoreCase));

    async Task Login()
    {
        var clave = _cfg.ClaveLes;
        if (string.IsNullOrEmpty(_cfg.Les.Usuario) || string.IsNullOrEmpty(clave)) throw new Exception("Falta usuario y clave de tradebox (LES) en la configuración.");
        _nav.Vaciar();
        var acc = _cfg.Les.Account.TrimEnd('/') + "/";
        var r = await _nav.Post(new Uri(acc + "Account/LogOn"), new() { ["returnUrl"] = "", ["user"] = _cfg.Les.Usuario, ["pass"] = clave }, acc + "Account/LogOn");
        if (EsLogin(r.Texto) && r.Texto.Contains("Incorrect", StringComparison.OrdinalIgnoreCase)) throw new Exception("tradebox no aceptó el usuario y la clave.");
        _logueado = true;
    }

    public async Task<List<Foto>> Fotos(string codigo)
    {
        await _cola.WaitAsync();
        try { return await FotosAhora(codigo, false); } finally { _cola.Release(); }
    }

    public void Olvidar() => _logueado = false;

    /// <summary>Una página del maestro de artículos de tradebox (para armar el catálogo de reconocimiento por foto). Devuelve los códigos.</summary>
    public async Task<List<string>> ListarArticulos(int pagina, int tam = 200)
    {
        await _cola.WaitAsync();
        try
        {
            for (var intento = 0; intento < 2; intento++)
            {
                if (!_logueado) await Login();
                var url = _cfg.Les.Url.TrimEnd('/') + "/";
                var r = await _nav.Post(new Uri(url + "GenericSearchRemote/GenericListRemoteData?action=read"), new()
                {
                    ["className"] = "Articulo", ["filtrosFijos"] = "", ["view"] = "false", ["page"] = pagina.ToString(), ["start"] = ((pagina - 1) * tam).ToString(),
                    ["limit"] = tam.ToString(), ["sort"] = "[{\"property\":\"IDArticulo\",\"direction\":\"DESC\"}]",
                }, url + "GenericSearchRemote/GenericListRemote?className=Articulo");
                if (EsLogin(r.Texto)) { _logueado = false; continue; }
                return Regex.Matches(r.Texto, "\"CodArticulo\"\\s*:\\s*" + Str).Select(m => Desc(m.Groups[1].Value).Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
            }
            throw new Exception("tradebox pidió login de nuevo");
        }
        finally { _cola.Release(); }
    }

    /// <summary>Una página del maestro de artículos de tradebox con todos los datos (cliente, habilitado, stock). Devuelve también el total.</summary>
    public async Task<(List<FichaLes> Filas, int? Total)> ListarFichas(int pagina, int tam = 200)
    {
        await _cola.WaitAsync();
        try
        {
            for (var intento = 0; intento < 2; intento++)
            {
                if (!_logueado) await Login();
                var url = _cfg.Les.Url.TrimEnd('/') + "/";
                var r = await _nav.Post(new Uri(url + "GenericSearchRemote/GenericListRemoteData?action=read"), new()
                {
                    ["className"] = "Articulo", ["filtrosFijos"] = "", ["view"] = "false", ["page"] = pagina.ToString(), ["start"] = ((pagina - 1) * tam).ToString(),
                    ["limit"] = tam.ToString(), ["sort"] = "[{\"property\":\"IDArticulo\",\"direction\":\"DESC\"}]",
                }, url + "GenericSearchRemote/GenericListRemote?className=Articulo");
                if (EsLogin(r.Texto)) { _logueado = false; continue; }
                using var doc = JsonDocument.Parse(r.Texto);
                var filas = new List<FichaLes>();
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    foreach (var a in data.EnumerateArray()) if (Fila(a) is { } f) filas.Add(f);
                int? total = doc.RootElement.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32()
                    : doc.RootElement.TryGetProperty("totalCount", out var t2) && t2.ValueKind == JsonValueKind.Number ? t2.GetInt32() : null;
                return (filas, total);
            }
            throw new Exception("tradebox pidió login de nuevo");
        }
        finally { _cola.Release(); }
    }

    /// <summary>Búsqueda del artículo en el listado de LES (filtro por código). Devuelve el JSON {"data":[...]}.</summary>
    async Task<string> BuscarJson(string codigo, bool reintento = false, string campo = "CodArticulo", int limite = 25)
    {
        if (!_logueado) await Login();
        var url = _cfg.Les.Url.TrimEnd('/') + "/";
        var filtro = JsonSerializer.Serialize(new[] { new { field = campo, type = "string", value = codigo } });
        var r = await _nav.Post(new Uri(url + "GenericSearchRemote/GenericListRemoteData?action=read"), new()
        {
            ["className"] = "Articulo", ["filtrosFijos"] = "", ["view"] = "false", ["page"] = "1", ["start"] = "0", ["limit"] = limite.ToString(),
            ["sort"] = "[{\"property\":\"IDArticulo\",\"direction\":\"DESC\"}]", ["filter"] = filtro,
            ["filter[0][field]"] = campo, ["filter[0][data][type]"] = "string", ["filter[0][data][value]"] = codigo,
        }, url + "GenericSearchRemote/GenericListRemote?className=Articulo");
        if (EsLogin(r.Texto))
        {
            if (reintento) throw new Exception("LES pidió login de nuevo");
            _logueado = false;
            return await BuscarJson(codigo, true, campo, limite);
        }
        return r.Texto;
    }

    /// <summary>Artículos de LES cuya descripción contiene el texto (como el filtro de la columna Descripción del listado de artículos).</summary>
    public async Task<List<FichaLes>> BuscarPorDescripcion(string texto, int limite = 60)
    {
        var t = texto.Trim();
        if (t.Length < 3) return new();
        // LES filtra la descripción con LIKE ("%" sirve de comodín): primero las palabras en el orden escrito
        // ("CLIPSTRIP%VENUS"); si hay varias palabras se suma la búsqueda por la más larga (por si vienen en otro orden).
        // SONA exige que estén todas las palabras escritas.
        var palabras = Norm(t).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var consultas = new List<string> { string.Join("%", palabras) };
        if (palabras.Length > 1) consultas.Add(palabras.OrderByDescending(w => w.Length).First());
        var lista = new List<FichaLes>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in consultas)
        {
            if (lista.Count >= limite) break;
            string json;
            await _cola.WaitAsync();
            try { json = await BuscarJson(q, false, "Descripcion", limite); }
            finally { _cola.Release(); }
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) continue;
            foreach (var a in data.EnumerateArray())
                if (Fila(a) is { } f && f.Descripcion != null && palabras.All(w => Norm(f.Descripcion).Contains(w)) && vistos.Add(f.Codigo))
                    lista.Add(f);
        }
        return lista;
    }

    static string Norm(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToUpperInvariant(ch));
        return Regex.Replace(sb.ToString(), @"\s+", " ");
    }

    async Task<List<Foto>> FotosAhora(string codigo, bool reintento)
    {
        var json = await BuscarJson(codigo);
        var id = IdArticulo(json, codigo);
        if (id == null) return new();
        var ficha = await _nav.Get(_cfg.Les.Url.TrimEnd('/') + "/General/LoadArticulo?idArticulo=" + id);
        return FotosDeFicha(ficha.Texto);
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Hora, FichaLes? Ficha)> _fichas = new();

    /// <summary>Ficha del artículo en LES (exista o no stock). null si LES no lo tiene. Caché de 10 minutos.</summary>
    public async Task<FichaLes?> Ficha(string codigo)
    {
        var c = codigo.Trim().ToUpperInvariant();
        if (_fichas.TryGetValue(c, out var f) && DateTime.UtcNow - f.Hora < TimeSpan.FromMinutes(10)) return f.Ficha;
        await _cola.WaitAsync();
        FichaLes? ficha;
        try { ficha = LeerFicha(await BuscarJson(c), c); }
        finally { _cola.Release(); }
        _fichas[c] = (DateTime.UtcNow, ficha);
        return ficha;
    }

    public static FichaLes? LeerFicha(string json, string codigo)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in data.EnumerateArray())
        {
            string? S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            double? D(string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            bool? B(string k) => a.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
            if (!string.Equals(S(a, "CodArticulo")?.Trim(), codigo, StringComparison.OrdinalIgnoreCase)) continue;
            return Fila(a);
        }
        return null;
    }

    /// <summary>Un renglón del listado de artículos de LES.</summary>
    static FichaLes? Fila(JsonElement a)
    {
        {
            string? S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            double? D(string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            bool? B(string k) => a.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
            var codigo = S(a, "CodArticulo")?.Trim();
            if (string.IsNullOrEmpty(codigo)) return null;
            int? idc = null; string? cli = null;
            if (a.TryGetProperty("FkCliente", out var fc) && fc.ValueKind == JsonValueKind.Object)
            {
                if (fc.TryGetProperty("IDCliente", out var ic) && ic.ValueKind == JsonValueKind.Number) idc = ic.GetInt32();
                cli = S(fc, "Nombre");
            }
            var id = a.TryGetProperty("IDArticulo", out var ida) && ida.ValueKind == JsonValueKind.Number ? ida.GetInt64() : 0;
            return new FichaLes(id, codigo.ToUpperInvariant(), S(a, "Descripcion"), B("Habilitado"), idc, cli, S(a, "Marca"), S(a, "Proyecto"),
                D("StockFisico"), D("StockDisponible"), D("StockNoDisponible"), D("StockReservado"), D("StockPedido"), B("PublicadoPedidosPOP"));
        }
        return null;
    }

    const string Str = "\"((?:\\\\.|[^\"\\\\])*)\"";
    static string Desc(string s) => Regex.Unescape(s);

    public static long? IdArticulo(string json, string codigo)
    {
        var ids = Regex.Matches(json, "\"IDArticulo\"\\s*:\\s*(\\d+)");
        for (var i = 0; i < ids.Count; i++)
        {
            var desde = ids[i].Index + ids[i].Length;
            var fin = i + 1 < ids.Count ? ids[i + 1].Index : json.Length;
            var trozo = json[desde..fin];
            var cod = Regex.Match(trozo, "\"CodArticulo\"\\s*:\\s*" + Str);
            if (cod.Success && Desc(cod.Groups[1].Value).Trim().Equals(codigo.Trim(), StringComparison.OrdinalIgnoreCase)) return long.Parse(ids[i].Groups[1].Value);
        }
        return null;
    }

    public static List<Foto> FotosDeFicha(string html)
    {
        var b = Regex.Match(html, @"ImagenesStore\.loadData\((\[[\s\S]*?\])\)");
        if (!b.Success) return new();
        return Regex.Matches(b.Groups[1].Value, "\\{\\s*\"name\"\\s*:\\s*" + Str + "\\s*,\\s*\"url\"\\s*:\\s*" + Str + "\\s*\\}")
            .Select(m => new Foto(Desc(m.Groups[1].Value), Desc(m.Groups[2].Value))).Where(f => f.Url.Length > 0).ToList();
    }

    /// <summary>Baja una imagen una sola vez y la guarda en datos/imagenes.</summary>
    /// <summary>Archivo de la caché de imágenes para una URL (lo usa también el e-TMS para dejar sus fotos).</summary>
    public string ArchivoCache(string url)
    {
        var dir = Path.Combine(_cfg.Carpeta, "imagenes");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant());
    }

    public async Task<(string Archivo, string Tipo)?> ImagenCacheada(string url)
    {
        if (url.StartsWith("catalogo:"))
        {
            var local = Path.Combine(_cfg.Carpeta, "catalogo", Path.GetFileName(url["catalogo:".Length..]));
            return File.Exists(local) ? (local, "image/jpeg") : null;
        }
        var archivo = ArchivoCache(url);
        var tipo = Regex.IsMatch(url, @"\.png(\?|$)", RegexOptions.IgnoreCase) ? "image/png" : "image/jpeg";
        if (File.Exists(archivo)) return (archivo, tipo);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url.Replace(" ", "%20"));
            req.Headers.TryAddWithoutValidation("Referer", _cfg.Les.Url);
            using var r = await _http.SendAsync(req);
            if (!r.IsSuccessStatusCode) return null;
            await File.WriteAllBytesAsync(archivo, await r.Content.ReadAsByteArrayAsync());
            return (archivo, tipo);
        }
        catch { return null; }
    }
}
