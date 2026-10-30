using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sona.Dominio;
using Sona.Les;
using Sona.Wms;

namespace Sona;

public sealed class InfoContenedor
{
    public string Contenedor { get; set; } = "";
    public string? Articulo { get; set; }
    public string? Descripcion { get; set; }
    public string? Cliente { get; set; }
    public int? Empresa { get; set; }
    public string? EmpresaNombre { get; set; }
    public double? Cantidad { get; set; }
    public double? Bultos { get; set; }
    public double? M3 { get; set; }
    public string? Posicion { get; set; }
    public bool? Disponible { get; set; }
    /// <summary>Motivo de "no disponible" (ROTURA, CUARENTENA…) o el texto de estado que informó la fuente.</summary>
    public string? EstadoTexto { get; set; }
    public Dictionary<string, string> Campos { get; set; } = new();
    public string? Foto { get; set; }
    /// <summary>Todas las fotos del artículo (la primera es <see cref="Foto"/>).</summary>
    public List<string> Fotos { get; set; } = new();
    public bool Encontrado { get; set; }
    /// <summary>Si el artículo está habilitado en LES (null = no se sabe).</summary>
    public bool? ArticuloHabilitado { get; set; }
}

public sealed record PosicionResuelta(string Cara, int Columna, int? Nivel, long? UbicCtrl, string? Codigo)
{
    public string Legible => Posicion.Legible(Cara, Columna, Nivel);
}

public class Ocupacion
{
    public string Codigo { get; set; } = "";
    public string Cara { get; set; } = "";
    public int Columna { get; set; }
    public int Nivel { get; set; }
    public int Capacidad { get; set; }
    public int Ocupados { get; set; }
    public int Libres { get; set; }
    public int Inhabilitados { get; set; }
    public List<string> Contenedores { get; set; } = new();
    public string? Area { get; set; }
    /// <summary>Aviso si SONA no pudo leer todos los contenedores que el WMS dice tener en el nivel.</summary>
    public string? Aviso { get; set; }
    /// <summary>Estado (disponibilidad/calidad) por contenedor si la pantalla "Ver un hueco" lo informa.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<string, string> EstadosWms { get; set; } = new();
}

public sealed class NivelContenido : Ocupacion
{
    public Fila? Ficha { get; set; }
    public List<InfoContenedor> Detalle { get; set; } = new();
}

public sealed record FotoUrl(string Nombre, string Url);
public sealed record ContenedorArticulo(string? Contenedor, double? Cantidad, string? Posicion, Dictionary<string, string> Campos)
{
    /// <summary>Disponibilidad según el WMS (Datos de Ubicaciones del handheld); null = no se pudo consultar.</summary>
    public bool? Disponible { get; set; }
    public string? EstadoTexto { get; set; }
}
public sealed class ArticuloInfo
{
    public string Articulo { get; set; } = "";
    public string? Descripcion { get; set; }
    public string? Cliente { get; set; }
    public int? Empresa { get; set; }
    public string? EmpresaNombre { get; set; }
    public List<FotoUrl> Fotos { get; set; } = new();
    public List<ContenedorArticulo> Contenedores { get; set; } = new();
    public double Total { get; set; }
    /// <summary>Ficha en LES (habilitado, stock según LES). null si LES no respondió o no lo tiene.</summary>
    public FichaLes? Les { get; set; }
}

public sealed record Carga(List<InfoContenedor> Contenedores, string? Articulo, int? Empresa, double? M3, bool Poca, string? Origen);
public sealed record Sugerencia(string Codigo, string Legible, long? UbicCtrl, int Nivel, int Regla, string Motivo, int? Libres, int? Capacidad);

public sealed class Validacion
{
    public bool Ok { get; set; }
    public List<string> Bloqueos { get; set; } = new();
    public string? Autorizacion { get; set; }
    public List<string> Avisos { get; set; } = new();
    /// <summary>Faltan huecos según SONA: el operario puede mandar igual ("Transferir igual") y el WMS decide.</summary>
    public List<string> Forzables { get; set; } = new();
    public PosicionResuelta? Destino { get; set; }
    public Ocupacion? Ocupacion { get; set; }
}

/// <summary>
/// Reglas del depósito: sesiones del WMS por usuario, empresa automática, datos de contenedores,
/// consulta de posiciones, sugerencia y validación de destino (Layout Objetivo).
/// </summary>
public sealed class Servicios
{
    readonly Configuracion _cfg;
    readonly Db _db;
    readonly Buscador _buscador;
    readonly FotosLes _les;
    readonly ILogger _log;

    public Servicios(Configuracion cfg, Db db, Buscador buscador, FotosLes les, ILogger<Servicios> log)
    { _cfg = cfg; _db = db; _buscador = buscador; _les = les; _log = log; }

    public Db Db => _db;
    public Buscador Buscador => _buscador;
    public FotosLes Les => _les;

    // ================================================================ sesiones del WMS
    readonly ConcurrentDictionary<string, SesionHandheld> _sesiones = new();

    // ================================================================ estado real del contenedor en el WMS
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Hora, DatosContenedorWms Datos)> _estadoWms = new();

    /// <summary>
    /// Disponible / bloqueado según el WMS, con la pantalla "Datos de Ubicaciones" del handheld (la que se ve en "Ver un hueco" al elegir la
    /// profundidad). Se guarda 2 minutos para no repetir consultas y se actualiza el espejo de contenedores.
    /// </summary>
    public async Task<DatosContenedorWms?> EstadoWms(string usuario, string contenedor, bool forzar = false)
    {
        var c = contenedor.Trim().ToUpperInvariant();
        if (!forzar && _estadoWms.TryGetValue(c, out var ya) && DateTime.UtcNow - ya.Hora < TimeSpan.FromMinutes(2)) return ya.Datos;
        try
        {
            var d = await SesionDe(usuario).DatosContenedor(c);
            _estadoWms[c] = (DateTime.UtcNow, d);
            if (d.Existe && d.Bloqueado is bool b)
                _db.Ejecutar("UPDATE contenedores SET disponible=$2, motivo=$3 WHERE codigo=$1", c, !b, b ? d.Motivo ?? "BLOQUEADO" : null);
            return d;
        }
        catch (Exception e) { _log.LogWarning("No se pudo leer el estado de {C} en el WMS: {E}", c, e.Message); return null; }
    }

    /// <summary>Aplica el estado del WMS a la ficha del contenedor (si el WMS lo informa).</summary>
    public static void AplicarEstadoWms(InfoContenedor info, DatosContenedorWms? d)
    {
        if (d is not { Existe: true, Bloqueado: bool b }) return;
        info.Disponible = !b;
        info.EstadoTexto = b ? "NO DISPONIBLE · " + (d.Motivo ?? "BLOQUEADO") : "DISPONIBLE";
        info.Campos["Estado (WMS)"] = b ? "Bloqueado" + (d.Motivo != null ? " · " + d.Motivo : "") : "Disponible";
        if (d.Vencimiento is { Length: > 0 } vto && !vto.StartsWith("00/")) info.Campos["Vencimiento"] = vto;
        if (d.UltimaAccion != null) info.Campos["Última acción (WMS)"] = d.UltimaAccion;
    }

    public SesionHandheld SesionDe(string usuario)
    {
        var u = usuario.ToUpperInvariant();
        if (_sesiones.TryGetValue(u, out var s)) return s;
        var clave = _db.Q1("SELECT clave_cifrada FROM usuarios WHERE usuario=$1", u)?.S("clave_cifrada");
        if (string.IsNullOrEmpty(clave)) throw new WmsError($"El usuario {u} no tiene la clave del WMS guardada: que ingrese de nuevo desde la app.");
        return _sesiones.GetOrAdd(u, _ => new SesionHandheld(_cfg, u, Seguridad.Descifrar(clave, _cfg.Secreto)));
    }

    public SesionHandheld NuevaSesion(string usuario, string clave) => new(_cfg, usuario, clave);
    public void RegistrarSesion(SesionHandheld s) => _sesiones[s.Usuario] = s;
    public void OlvidarSesion(string usuario) => _sesiones.TryRemove(usuario.ToUpperInvariant(), out _);
    public void OlvidarWeb() { _webs.Clear(); _etms?.Olvidar(); }
    public void OlvidarSesiones() => _sesiones.Clear();
    public List<object> SesionesActivas() => _sesiones.Values.Select(s => (object)new { usuario = s.Usuario, red = s.Red }).ToList();

    static int EmpresaConsulta(SesionHandheld s) => s.Empresas.Count > 0 ? s.Empresas[0].Codigo : 6;

    // ================================================================ empresas
    List<(int Codigo, string Nombre, List<string> Alias)>? _empresas;
    public void OlvidarEmpresas() => _empresas = null;

    List<(int Codigo, string Nombre, List<string> Alias)> Empresas() => _empresas ??= _db.Q("SELECT codigo, nombre, alias FROM empresas")
        .Select(f => ((int)f.L("codigo")!, f.S("nombre") ?? "", (f["alias"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToList() ?? new List<string>())).ToList();

    public static string Normalizar(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch) && ch < 128) sb.Append(char.ToUpperInvariant(ch));
        return sb.ToString();
    }

    /// <summary>La empresa nunca se elige a mano: sale del cliente del contenedor/artículo (nombre, alias o código).</summary>
    public int? ResolverEmpresa(string? cliente)
    {
        if (string.IsNullOrWhiteSpace(cliente)) return null;
        var lista = Empresas();
        if (int.TryParse(cliente.Trim(), out var n) && lista.Any(e => e.Codigo == n)) return n;
        var t = Normalizar(cliente);
        foreach (var e in lista) if (Normalizar(e.Nombre) == t || e.Alias.Any(a => Normalizar(a) == t)) return e.Codigo;
        foreach (var e in lista)
        {
            var nn = Normalizar(e.Nombre);
            if (nn.Length >= 4 && (t.Contains(nn) || nn.Contains(t))) return e.Codigo;
        }
        return null;
    }

    public string? NombreEmpresa(int? codigo) => codigo == null ? null : Empresas().FirstOrDefault(e => e.Codigo == codigo).Nombre;

    // ================================================================ artículos y fotos
    public static string UrlImagen(string u) => "/api/imagen?u=" + Uri.EscapeDataString(u);

    /// <summary>Fotos del artículo: primero las propias del catálogo de SONA (subidas a mano), después tradebox / e-TMS.</summary>
    public async Task<List<Foto>> FotosArticulo(string codigo, int? empresa = null)
    {
        var propias = _db.Q("SELECT archivo FROM articulo_fotos WHERE articulo=$1 ORDER BY id", codigo.Trim().ToUpperInvariant())
            .Select(f => new Foto("catálogo SONA", "catalogo:" + f.S("archivo"))).ToList();
        var externas = await FotosExternas(codigo, empresa);
        return propias.Concat(externas).ToList();
    }

    async Task<List<Foto>> FotosExternas(string codigo, int? empresa)
    {
        var a = _db.Q1("SELECT fotos, actualizado, empresa FROM articulos WHERE codigo=$1", codigo);
        empresa ??= a?.I("empresa");
        var guardadas = (a?["fotos"] as JsonArray)?.Select(x => new Foto(x!["nombre"]?.GetValue<string>() ?? "", x["url"]?.GetValue<string>() ?? "")).ToList();
        var vieja = a?.S("actualizado") is string act && DateTime.TryParse(act, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d) && DateTime.UtcNow - d > TimeSpan.FromDays(7);
        if (guardadas != null && !vieja)
        {
            if (guardadas.Count > 0) return guardadas;
            var e = await FotosEtms(codigo, empresa);
            if (e.Count > 0) GuardarFotos(codigo, e);
            return e;
        }
        try
        {
            var fotos = await _les.Fotos(codigo);
            if (fotos.Count == 0) fotos = await FotosEtms(codigo, empresa);   // sin foto en tradebox: la del e-TMS
            GuardarFotos(codigo, fotos);
            return fotos;
        }
        catch (Exception e)
        {
            _log.LogWarning("No se pudieron traer las fotos de {Codigo}: {Error}", codigo, e.Message);
            if (guardadas is { Count: > 0 }) return guardadas;
            var et = await FotosEtms(codigo, empresa);
            if (et.Count > 0) GuardarFotos(codigo, et);
            return et;
        }
    }

    void GuardarFotos(string codigo, List<Foto> fotos)
    {
        var json = new JsonArray(fotos.Select(f => (JsonNode)new JsonObject { ["nombre"] = f.Nombre, ["url"] = f.Url }).ToArray());
        _db.Ejecutar("INSERT INTO articulos (codigo, fotos, actualizado) VALUES ($1,$2,$3) ON CONFLICT(codigo) DO UPDATE SET fotos=excluded.fotos, actualizado=excluded.actualizado",
            codigo, json, Db.Ahora());
    }

    readonly ConcurrentDictionary<string, DateTime> _etmsProbado = new();

    /// <summary>Usuario para consultas automáticas al e-TMS: el configurado en ZCFG o, si no hay, un administrador con clave del WMS guardada.</summary>
    (string Usuario, string Clave)? CredencialesServicio()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.Etms.Usuario) && _cfg.ClaveEtms.Length > 0) return (_cfg.Etms.Usuario, _cfg.ClaveEtms);
        foreach (var adm in _cfg.Admins.Concat(_db.Q("SELECT usuario FROM usuarios WHERE rol='admin' AND activo=1").Select(f => f.S("usuario")!)))
        {
            var cl = _db.Q1("SELECT clave_cifrada FROM usuarios WHERE usuario=$1", adm.ToUpperInvariant())?.S("clave_cifrada");
            if (!string.IsNullOrEmpty(cl)) return (adm.ToUpperInvariant(), Seguridad.Descifrar(cl, _cfg.Secreto));
        }
        return null;
    }

    /// <summary>Foto del artículo en el e-TMS (como mucho una vez cada 12 h por artículo si no tiene).</summary>
    async Task<List<Foto>> FotosEtms(string codigo, int? empresa)
    {
        if (empresa == null) return new();
        if (_etmsProbado.TryGetValue(codigo, out var t) && DateTime.UtcNow - t < TimeSpan.FromHours(12)) return new();
        _etmsProbado[codigo] = DateTime.UtcNow;
        if (CredencialesServicio() is not { } cr) return new();
        try { return await EtmsCliente.Fotos(cr.Usuario, cr.Clave, empresa.Value, codigo, _les.ArchivoCache); }
        catch (Exception e) { _log.LogWarning("e-TMS: no se pudo traer la foto de {Codigo}: {E}", codigo, e.Message); return new(); }
    }

    // ================================================================ contenedores
    readonly ConcurrentDictionary<string, (DateTime Hora, InfoContenedor Info)> _cacheInfo = new();
    public void OlvidarContenedor(string c) => _cacheInfo.TryRemove(c.ToUpperInvariant(), out _);
    /// <summary>Cambiaron las fotos propias de un artículo: se descarta la caché de contenedores para que se vean ya.</summary>
    public void OlvidarFotos(string articulo) => _cacheInfo.Clear();

    /// <summary>Datos del contenedor: buscador SONA + espejo propio. Caché de 60 s.</summary>
    public async Task<InfoContenedor> InfoContenedor(string codigo, bool conFoto = true)
    {
        var c = codigo.Trim().ToUpperInvariant();
        if (_cacheInfo.TryGetValue(c, out var ca) && DateTime.UtcNow - ca.Hora < TimeSpan.FromSeconds(60) && (!conFoto || ca.Info.Foto != null || ca.Info.Articulo == null)) return ca.Info;
        List<Registro> regs = new();
        try { regs = await _buscador.Contenedor(c); } catch (Exception e) { _log.LogWarning("Buscador SONA no respondió ({C}): {E}", c, e.Message); }
        // solo cuenta si el buscador devuelve ESE contenedor (con un código de artículo puede devolver otras filas)
        var r = regs.FirstOrDefault(x => string.Equals(x.Contenedor, c, StringComparison.OrdinalIgnoreCase)) ?? regs.FirstOrDefault(x => string.IsNullOrEmpty(x.Contenedor));
        var espejo = _db.Q1("SELECT * FROM contenedores WHERE codigo=$1", c);
        var articulo = r?.Articulo ?? espejo?.S("articulo");
        var empresa = ResolverEmpresa(r?.Cliente) ?? espejo?.I("empresa");
        var info = new InfoContenedor
        {
            Contenedor = c, Articulo = articulo, Descripcion = r?.Descripcion, Cliente = r?.Cliente, Empresa = empresa, EmpresaNombre = NombreEmpresa(empresa),
            Cantidad = r?.Cantidad ?? espejo?.D("cantidad"), Bultos = r?.Bultos, M3 = r != null ? r.M3 : espejo?.D("m3"),
            Posicion = r?.Posicion ?? espejo?.S("posicion"),
            Campos = r?.Campos ?? new(), Encontrado = r != null,
        };
        // Disponibilidad: lo que diga el buscador; si no dice nada, lo último que cargó SONA en el WMS para ese contenedor.
        var (disp, texto) = LeerDisponibilidad(r?.Estado);
        if (disp == null && espejo != null && espejo.GetValueOrDefault("disponible") is bool dsp) { disp = dsp; texto = dsp ? "DISPONIBLE" : espejo.S("motivo") ?? "NO DISPONIBLE"; }
        // Si ninguna fuente dice que está NO disponible, se toma como disponible (así lo muestra el WMS)
        if (disp == null && r != null) { disp = true; texto = "DISPONIBLE"; }
        info.Disponible = disp; info.EstadoTexto = texto;
        if (articulo != null && conFoto)
        {
            var fotos = await FotosArticulo(articulo, empresa);
            info.Fotos = fotos.Select(f => UrlImagen(f.Url)).ToList();
            info.Foto = info.Fotos.FirstOrDefault();
        }
        if (r != null)
        {
            _db.Ejecutar("""
                INSERT INTO contenedores (codigo, articulo, empresa, cantidad, bultos, m3, posicion, actualizado) VALUES ($1,$2,$3,$4,$5,$6,$7,$8)
                ON CONFLICT(codigo) DO UPDATE SET articulo=excluded.articulo, empresa=COALESCE(excluded.empresa, contenedores.empresa),
                  cantidad=excluded.cantidad, bultos=excluded.bultos, m3=COALESCE(excluded.m3, contenedores.m3),
                  posicion=COALESCE(excluded.posicion, contenedores.posicion), actualizado=excluded.actualizado
                """, c, info.Articulo, info.Empresa, info.Cantidad, info.Bultos, info.M3, Posicion.Normalizar(info.Posicion), Db.Ahora());
            if (articulo != null)
                _db.Ejecutar("""
                    INSERT INTO articulos (codigo, descripcion, empresa, empresa_nombre, actualizado) VALUES ($1,$2,$3,$4,$5)
                    ON CONFLICT(codigo) DO UPDATE SET descripcion=COALESCE(excluded.descripcion, articulos.descripcion),
                      empresa=COALESCE(excluded.empresa, articulos.empresa), empresa_nombre=COALESCE(excluded.empresa_nombre, articulos.empresa_nombre)
                    """, articulo, info.Descripcion, info.Empresa, info.EmpresaNombre, "2000-01-01T00:00:00.000Z");
        }
        _cacheInfo[c] = (DateTime.UtcNow, info);
        return info;
    }

    static readonly System.Text.RegularExpressions.Regex ReNoDisp = new("NO ?DISP|ROTUR|CUARENT|NO CONFORME|SCRAP|OBSOLET|INCOMPLET|RESERVAD|BLOQUE|VENCID|DEPO.*EXTERNO", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Interpreta el estado que viene de una fuente: (disponible?, texto para mostrar).</summary>
    public static (bool? Disponible, string? Texto) LeerDisponibilidad(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado)) return (null, null);
        var e = estado.Trim();
        if (ReNoDisp.IsMatch(e)) return (false, e.ToUpperInvariant());
        if (System.Text.RegularExpressions.Regex.IsMatch(e, @"^(DISP|OK|S|SI|SÍ|LIBRE|HABILITAD)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return (true, "DISPONIBLE");
        if (System.Text.RegularExpressions.Regex.IsMatch(e, @"^(N|NO)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return (false, "NO DISPONIBLE");
        return (null, e.ToUpperInvariant());
    }

    /// <summary>Dónde está cada contenedor de un artículo (buscador SONA).</summary>
    public async Task<ArticuloInfo> InfoArticulo(string codigo)
    {
        var cod = codigo.Trim().ToUpperInvariant();
        var regs = await _buscador.Articulo(cod);
        var les = await LeerFichaLes(cod);
        var cliente = regs.Select(r => r.Cliente).FirstOrDefault(x => x != null) ?? les?.Cliente;
        var empresa = ResolverEmpresa(cliente) ?? les?.IdCliente;
        var fotos = (regs.Count > 0 || les != null) ? await FotosArticulo(cod, empresa) : new();
        if (les != null && empresa != null)
            _db.Ejecutar("""
                INSERT INTO articulos (codigo, descripcion, empresa, empresa_nombre, actualizado) VALUES ($1,$2,$3,$4,$5)
                ON CONFLICT(codigo) DO UPDATE SET descripcion=COALESCE(articulos.descripcion, excluded.descripcion), empresa=COALESCE(articulos.empresa, excluded.empresa),
                  empresa_nombre=COALESCE(articulos.empresa_nombre, excluded.empresa_nombre)
                """, cod, les.Descripcion, empresa, NombreEmpresa(empresa) ?? les.Cliente, "2000-01-01T00:00:00.000Z");
        return new ArticuloInfo
        {
            Articulo = cod, Descripcion = regs.Select(r => r.Descripcion).FirstOrDefault(x => x != null) ?? les?.Descripcion, Cliente = cliente,
            Empresa = empresa, EmpresaNombre = NombreEmpresa(empresa) ?? les?.Cliente,
            Fotos = fotos.Select(f => new FotoUrl(f.Nombre, UrlImagen(f.Url))).ToList(),
            Contenedores = regs.Select(r => new ContenedorArticulo(r.Contenedor, r.Cantidad, r.Posicion, r.Campos)).ToList(),
            Total = regs.Sum(r => r.Cantidad ?? 0),
            Les = les,
        };
    }

    /// <summary>Artículos cuya descripción en LES contiene lo escrito (con cliente, habilitado y stock según LES).</summary>
    public async Task<List<object>> BuscarPorDescripcion(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < 3) return new();
        try
        {
            return (await _les.BuscarPorDescripcion(texto)).Select(f => (object)new
            {
                articulo = f.Codigo, descripcion = f.Descripcion, cliente = f.Cliente, empresa = ResolverEmpresa(f.Cliente) ?? f.IdCliente,
                empresaNombre = NombreEmpresa(ResolverEmpresa(f.Cliente) ?? f.IdCliente) ?? f.Cliente, habilitado = f.Habilitado,
                stock = f.StockFisico, disponible = f.StockDisponible, marca = f.Marca, proyecto = f.Proyecto,
            }).ToList();
        }
        catch (Exception e) { _log.LogWarning("No se pudo buscar por descripción en LES: {E}", e.Message); return new(); }
    }

    static string NormBusqueda(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToUpperInvariant(ch));
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    readonly ConcurrentDictionary<string, (DateTime Hora, List<FichaLes> Lista)> _cacheDescripcion = new();

    /// <summary>
    /// Buscador "a medida que escribís" (como el filtro por descripción del LES): primero lo que SONA ya conoce (instantáneo, con el stock
    /// que tiene registrado) y después lo que encuentra el LES por descripción (con caché de 10 minutos y sin esperar más de 6 segundos).
    /// Busca por código o por descripción; con varias palabras exige todas, en cualquier orden.
    /// </summary>
    public async Task<object> SugerirArticulos(string q, int limite = 25)
    {
        var t = NormBusqueda(q ?? "");
        if (t.Length < 2) return new { q, items = Array.Empty<object>(), errorLes = (string?)null };
        var palabras = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var larga = palabras.OrderByDescending(w => w.Length).First();
        var items = new List<Dictionary<string, object?>>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _db.Q("""
            SELECT a.codigo, a.descripcion, a.empresa, a.empresa_nombre,
                   (SELECT count(*) FROM contenedores c WHERE c.articulo = a.codigo) AS conts,
                   (SELECT sum(cantidad) FROM contenedores c WHERE c.articulo = a.codigo) AS cant
            FROM articulos a WHERE upper(a.codigo) LIKE $1 OR upper(a.descripcion) LIKE $2 LIMIT 400
            """, t.Replace(" ", "") + "%", "%" + larga + "%"))
        {
            var cod = f.S("codigo")!; var desc = f.S("descripcion") ?? "";
            var texto = NormBusqueda(cod + " " + desc);
            if (!palabras.All(w => texto.Contains(w)) && !NormBusqueda(cod).StartsWith(t.Replace(" ", ""))) continue;
            if (!vistos.Add(cod)) continue;
            items.Add(new() { ["articulo"] = cod, ["descripcion"] = desc, ["empresa"] = f.L("empresa"), ["empresaNombre"] = f.S("empresa_nombre"),
                ["contenedores"] = f.L("conts") ?? 0, ["stock"] = f.D("cant"), ["origen"] = "sona" });
        }
        // los que empiezan con lo escrito, primero; después los que tienen stock
        items = items.OrderByDescending(x => NormBusqueda((string)x["articulo"]!).StartsWith(palabras[0]) || NormBusqueda((string?)x["descripcion"] ?? "").StartsWith(palabras[0]))
            .ThenByDescending(x => (long)(x["contenedores"] ?? 0L)).Take(limite).ToList();

        string? errorLes = null;
        if (t.Length >= 3 && items.Count < limite)
        {
            List<FichaLes>? les = null;
            if (_cacheDescripcion.TryGetValue(t, out var c) && DateTime.UtcNow - c.Hora < TimeSpan.FromMinutes(10)) les = c.Lista;
            else
            {
                var tarea = _les.BuscarPorDescripcion(t, 40);
                _ = tarea.ContinueWith(x => { if (x.IsCompletedSuccessfully) _cacheDescripcion[t] = (DateTime.UtcNow, x.Result); }, TaskScheduler.Default);   // si tarda, queda para la próxima tecla
                var fin = await Task.WhenAny(tarea, Task.Delay(TimeSpan.FromSeconds(6)));
                if (fin == tarea)
                {
                    try { les = await tarea; _cacheDescripcion[t] = (DateTime.UtcNow, les); }
                    catch (Exception e) { errorLes = "El LES no respondió la búsqueda por descripción: " + e.Message; _log.LogWarning("Buscador: {E}", e.Message); }
                }
                else errorLes = "El LES tarda en responder: seguí escribiendo o tocá Buscar.";
                if (_cacheDescripcion.Count > 500) _cacheDescripcion.Clear();
            }
            foreach (var f in les ?? new())
            {
                if (items.Count >= limite) break;
                if (!vistos.Add(f.Codigo)) continue;
                var emp = ResolverEmpresa(f.Cliente) ?? f.IdCliente;
                items.Add(new() { ["articulo"] = f.Codigo, ["descripcion"] = f.Descripcion, ["empresa"] = emp, ["empresaNombre"] = NombreEmpresa(emp) ?? f.Cliente,
                    ["contenedores"] = null, ["stock"] = f.StockFisico, ["habilitado"] = f.Habilitado, ["origen"] = "les" });
            }
        }
        return new { q, items, errorLes };
    }

    /// <summary>Ficha de LES sin romper la consulta si LES no responde.</summary>
    public async Task<FichaLes?> LeerFichaLes(string articulo)
    {
        if (string.IsNullOrWhiteSpace(articulo)) return null;
        try { return await _les.Ficha(articulo); }
        catch (Exception e) { _log.LogWarning("LES: no se pudo leer la ficha de {A}: {E}", articulo, e.Message); return null; }
    }

    // ================================================================ posiciones
    (DateTime Hora, List<string> Caras)? _caras;
    public void OlvidarCaras() => _caras = null;
    public List<string> CarasConocidas()
    {
        if (_caras is { } c && DateTime.UtcNow - c.Hora < TimeSpan.FromMinutes(5)) return c.Caras;
        var l = _db.Q("SELECT cara FROM zonas UNION SELECT DISTINCT cara FROM posiciones").Select(f => f.S("cara")!).ToList();
        _caras = (DateTime.UtcNow, l);
        return l;
    }

    /// <summary>Lo que escanea el operario como posición: Ubic.Ctrl (etiqueta) o código (FI1502, FI-15-02, IE_PK00403).</summary>
    public async Task<PosicionResuelta?> ResolverPosicion(string texto, int? nivel, string usuario)
    {
        var t = texto.Trim().ToUpperInvariant();
        if (Posicion.EsUbicCtrl(t))
        {
            var ubic = long.Parse(t);
            var f = _db.Q1("SELECT cara, columna FROM posiciones WHERE ubic_ctrl=$1 LIMIT 1", ubic);
            if (f != null) return Armar(f.S("cara")!, f.I("columna")!.Value, nivel, ubic);
            var s = SesionDe(usuario);
            var v = await s.VerHuecoPorUbic(EmpresaConsulta(s), ubic, nivel ?? 1);
            if (v == null || v.Cara.Length == 0) return null;
            GuardarFicha(v, ubic);
            return Armar(v.Cara, v.Columna, nivel, ubic);
        }
        var p = Posicion.Interpretar(t, CarasConocidas());
        if (p == null) return null;
        var n = nivel ?? p.Nivel;
        var u = _db.Q1("SELECT ubic_ctrl FROM posiciones WHERE cara=$1 AND columna=$2 AND ubic_ctrl IS NOT NULL LIMIT 1", p.Cara, p.Columna)?.L("ubic_ctrl");
        return Armar(p.Cara, p.Columna, n, u);
    }

    static PosicionResuelta Armar(string cara, int columna, int? nivel, long? ubic) =>
        new(cara, columna, nivel, ubic, nivel != null ? Posicion.Codigo(cara, columna, nivel.Value) : null);

    void GuardarFicha(VistaHueco v, long? ubic = null)
    {
        if (v.Cara.Length == 0 || v.Nivel == 0) return;
        var ocupados = v.Huecos.Count(h => h.Contenedor != null);
        _db.Ejecutar("""
            INSERT INTO posiciones (codigo, cara, columna, nivel, ubic_ctrl, capacidad, estado_wms, actualizado) VALUES ($1,$2,$3,$4,$5,$6,$7,$8)
            ON CONFLICT(codigo) DO UPDATE SET ubic_ctrl=COALESCE(excluded.ubic_ctrl, posiciones.ubic_ctrl),
              capacidad=COALESCE(posiciones.capacidad, excluded.capacidad), estado_wms=excluded.estado_wms, actualizado=excluded.actualizado
            """, Posicion.Codigo(v.Cara, v.Columna, v.Nivel), v.Cara, v.Columna, v.Nivel, ubic, (v.Total ?? v.Huecos.Count) > 0 ? (v.Total ?? v.Huecos.Count) : null,
            ocupados > 0 ? "Ocupado" : "Libre", Db.Ahora());
        if (ubic != null) _db.Ejecutar("UPDATE posiciones SET ubic_ctrl=$3 WHERE cara=$1 AND columna=$2 AND ubic_ctrl IS NULL", v.Cara, v.Columna, ubic);
    }

    /// <summary>Mira el hueco en vivo en el WMS ("Ver un hueco") y actualiza el espejo.</summary>
    public async Task<Ocupacion?> Ocupacion(string usuario, string cara, int columna, int nivel)
    {
        var s = SesionDe(usuario);
        var v = await s.VerHueco(EmpresaConsulta(s), cara, columna, nivel);
        if (v == null) return null;
        int Leidos(VistaHueco x) => x.Huecos.Where(h => h.Contenedor != null).Select(h => h.Contenedor).Distinct().Count();
        // Si faltan contenedores (la grilla del handheld es de a 5), se lee de nuevo como el operario: Chequeos y ajustes › 8 con el Ubic.Ctrl
        if (v.Esperados is int esp0 && Leidos(v) < esp0)
        {
            var ubic = _db.Q1("SELECT ubic_ctrl FROM posiciones WHERE cara=$1 AND columna=$2 AND ubic_ctrl IS NOT NULL LIMIT 1", cara, columna)?.L("ubic_ctrl");
            if (ubic != null)
                try
                {
                    var v2 = await s.VerHuecoPorUbic(EmpresaConsulta(s), ubic.Value, nivel);
                    if (v2 != null && Leidos(v2) > Leidos(v)) v = v2;
                }
                catch (Exception e) { _log.LogWarning("Ver un hueco por Ubic.Ctrl {U}: {E}", ubic, e.Message); }
        }
        GuardarFicha(v);
        var codigo = Posicion.Codigo(cara, columna, nivel);
        var cont = v.Huecos.Where(h => h.Contenedor != null).Select(h => h.Contenedor!.ToUpperInvariant()).Distinct().ToList();
        // Último recurso: los que el buscador SONA ubica en esta posición (por los artículos que ya se vieron en ella)
        if (v.Esperados is int esp && cont.Count < esp && cont.Count > 0)
        {
            try
            {
                var arts = new HashSet<string>();
                foreach (var c in cont) if ((await InfoContenedor(c, false)).Articulo is { } art0) arts.Add(art0);
                foreach (var a in arts.Take(6))
                {
                    foreach (var r in (await InfoArticulo(a)).Contenedores)
                        if (r.Contenedor is { } rc && Posicion.Normalizar(r.Posicion) == codigo && !cont.Contains(rc.ToUpperInvariant()) && cont.Count < esp)
                            cont.Add(rc.ToUpperInvariant());
                    if (cont.Count >= esp) break;
                }
            }
            catch (Exception e) { _log.LogWarning("No se pudo completar {Codigo} con el buscador: {E}", codigo, e.Message); }
        }
        foreach (var h in v.Huecos.Where(h => h.Contenedor != null))
            _db.Ejecutar("INSERT INTO contenedores (codigo, posicion, profundidad, actualizado) VALUES ($1,$2,$3,$4) ON CONFLICT(codigo) DO UPDATE SET posicion=excluded.posicion, profundidad=excluded.profundidad, actualizado=excluded.actualizado",
                h.Contenedor!.ToUpperInvariant(), codigo, h.Profundidad, Db.Ahora());
        var estados = v.Huecos.Where(h => h.Contenedor != null && h.Estado != null).GroupBy(h => h.Contenedor!.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Estado!);
        foreach (var (c, e) in estados)
        {
            var (dsp, txt) = LeerDisponibilidad(e);
            if (dsp is bool dd) _db.Ejecutar("UPDATE contenedores SET disponible=$2, motivo=$3 WHERE codigo=$1", c, dd, dd ? null : txt);
        }
        var ficha = _db.Q1("SELECT capacidad, capacidad_wms FROM posiciones WHERE codigo=$1", codigo);
        var inhab = v.InhabilitadosWms ?? v.Huecos.Count(h => h.Inhabilitado && h.Contenedor == null);
        // Profundidad: la que informa el WMS web (Ubicaciones › cantidad de ubicaciones). La pantalla "Ver un hueco" del handheld
        // puede mostrar solo una parte de los huecos, así que se usa la mayor de las dos.
        var capacidad = Math.Max(v.Total ?? v.Huecos.Count, ficha?.I("capacidad_wms") ?? 0);
        if (capacidad == 0) capacidad = ficha?.I("capacidad") ?? 0;
        return new Ocupacion
        {
            Codigo = codigo, Cara = cara, Columna = columna, Nivel = nivel, Capacidad = capacidad, Ocupados = cont.Count, Inhabilitados = inhab,
            Libres = Math.Max(0, capacidad - cont.Count - inhab), Contenedores = cont, Area = v.Area, EstadosWms = estados,
            Aviso = v.Esperados is int espF && espF > cont.Count
                ? $"El WMS informa {espF} contenedores en {codigo} y SONA leyó {cont.Count}. Si transferís la posición completa, SONA vuelve a mirar el origen al terminar y mueve los que queden."
                : null,
        };
    }

    /// <summary>
    /// Todo lo que hay en una posición; sin nivel recorre TODOS los niveles (como la opción 8 del handheld).
    /// Se prueban los niveles conocidos y además 1..niveles_max, porque la base puede conocer solo los que ya se consultaron.
    /// </summary>
    public async Task<List<NivelContenido>> ContenidoPosicion(string usuario, string cara, int columna, int? nivel)
    {
        var maxNiv = Math.Max(1, _db.Parametro("niveles_max", 8));
        var conocidos = _db.Q("SELECT nivel FROM posiciones WHERE cara=$1 AND columna=$2 ORDER BY nivel", cara, columna).Select(f => f.I("nivel")!.Value).ToHashSet();
        var niveles = nivel != null ? new List<int> { nivel.Value } : conocidos.Union(Enumerable.Range(1, maxNiv)).OrderBy(n => n).ToList();
        var lista = new List<NivelContenido>();
        foreach (var n in niveles)
        {
            Ocupacion? o;
            try { o = await Ocupacion(usuario, cara, columna, n); }
            catch (Exception e) when (nivel == null) { _log.LogWarning("No se pudo leer {Cara}{Col}-{N}: {E}", cara, columna, n, e.Message); continue; }
            if (o == null) continue;
            // nivel que no existe en el WMS (sin huecos ni contenedores y que la base no conoce): no se muestra
            if (nivel == null && o.Capacidad == 0 && o.Ocupados == 0 && !conocidos.Contains(n)) continue;
            var nc = new NivelContenido
            {
                Codigo = o.Codigo, Cara = o.Cara, Columna = o.Columna, Nivel = o.Nivel, Capacidad = o.Capacidad, Ocupados = o.Ocupados,
                Libres = o.Libres, Inhabilitados = o.Inhabilitados, Contenedores = o.Contenedores, Area = o.Area, Aviso = o.Aviso,
                Ficha = _db.Q1("SELECT * FROM pos_efectiva WHERE codigo=$1", o.Codigo),
            };
            foreach (var c in o.Contenedores)
            {
                var info = await InfoContenedor(c);
                if (o.EstadosWms.TryGetValue(c.ToUpperInvariant(), out var est))
                {
                    var (dsp, txt) = LeerDisponibilidad(est);
                    if (dsp != null || info.Disponible == null) { info.Disponible = dsp ?? info.Disponible; info.EstadoTexto = txt; }
                }
                // estado real en el WMS (Datos de Ubicaciones), con tope para no demorar posiciones muy cargadas
                if (nc.Detalle.Count < 40) AplicarEstadoWms(info, await EstadoWms(usuario, c));
                nc.Detalle.Add(info);
            }
            lista.Add(nc);
        }
        return lista;
    }

    /// <summary>
    /// Estado en el WMS de cada contenedor del artículo (Datos de Ubicaciones del handheld). Se consulta de a uno, con un tope de tiempo
    /// para que la consulta no se haga eterna en artículos con muchos contenedores (los que no llegan quedan "sin consultar").
    /// </summary>
    public async Task EstadosArticulo(string usuario, ArticuloInfo art)
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        foreach (var c in art.Contenedores.Where(x => x.Contenedor != null).Take(80))
        {
            if (reloj.Elapsed > TimeSpan.FromSeconds(12) && !_estadoWms.ContainsKey(c.Contenedor!.ToUpperInvariant())) { c.EstadoTexto = "sin consultar"; continue; }
            var d = await EstadoWms(usuario, c.Contenedor!);
            if (d is { Existe: true, Bloqueado: bool b }) { c.Disponible = !b; c.EstadoTexto = b ? d.Motivo ?? "BLOQUEADO" : "DISPONIBLE"; }
            else if (d is { Existe: false }) c.EstadoTexto = "no figura en el WMS";
        }
    }

    /// <summary>Consulta general (app: Consultar). Detecta Ubic.Ctrl, posición, contenedor o artículo.</summary>
    public async Task<object> Consulta(string usuario, string texto, int? nivel)
    {
        var t = texto.Trim().ToUpperInvariant();
        if (t.Length == 0) throw new ArgumentException("Escaneá o escribí algo para consultar.");
        if (Posicion.EsUbicCtrl(t) || Posicion.Interpretar(t, CarasConocidas()) != null)
        {
            var p = await ResolverPosicion(t, nivel, usuario);
            if (p != null && (Posicion.EsUbicCtrl(t) || CarasConocidas().Contains(p.Cara)))
                return new { tipo = "posicion", posicion = p, niveles = await ContenidoPosicion(usuario, p.Cara, p.Columna, p.Nivel) };
        }
        var c = await InfoContenedor(t);
        // Estado real en el WMS: "Datos de Ubicaciones" del handheld (lo que se ve en "Ver un hueco" al elegir la profundidad)
        var dw = c.Encontrado ? await EstadoWms(usuario, t, forzar: true) : null;
        AplicarEstadoWms(c, dw);
        if (dw is not { Existe: true, Bloqueado: not null } && c.Encontrado && Posicion.Normalizar(c.Posicion) is { } pc && Posicion.Interpretar(pc, CarasConocidas()) is { Nivel: { } nv } pp)
        {
            try
            {
                var o = await Ocupacion(usuario, pp.Cara, pp.Columna, nv);
                if (o != null && o.EstadosWms.TryGetValue(t, out var est))
                {
                    var (dsp, txt) = LeerDisponibilidad(est);
                    if (dsp != null) { c.Disponible = dsp; c.EstadoTexto = txt; }
                }
            }
            catch (Exception e) { _log.LogWarning("No se pudo ver el hueco de {C} en el WMS: {E}", t, e.Message); }
        }
        // Historial que conoce SONA: también para contenedores que ya no están en stock (pickeados, despachados, ajustados)
        var movs = _db.Q("SELECT id, tipo, origen, destino, cantidad, usuario, hora_servidor, estado, respuesta_wms, comentario FROM movimientos WHERE contenedor=$1 ORDER BY hora_servidor DESC LIMIT 200", t);
        var conteos = _db.Q("SELECT tarea_id, posicion, cantidad_contada, cantidad_wms, novedad, usuario, hora, revision FROM conteo_lineas WHERE contenedor=$1 ORDER BY hora DESC LIMIT 100", t);
        var espejo = _db.Q1("SELECT posicion, cantidad, actualizado FROM contenedores WHERE codigo=$1", t);
        // si no es un contenedor en stock, primero se busca como artículo (igual que SONA Escáner)
        if (!c.Encontrado)
        {
            var art = await InfoArticulo(t);
            // con stock en el buscador, o aunque no tenga stock si LES lo conoce (habilitado o no)
            if (art.Contenedores.Count > 0 || art.Les != null)
            {
                await EstadosArticulo(usuario, art);
                return new { tipo = "articulo", articulo = art };
            }
        }
        else if (c.Articulo != null) c.ArticuloHabilitado = (await LeerFichaLes(c.Articulo))?.Habilitado;
        if (c.Encontrado || movs.Count > 0 || conteos.Count > 0 || espejo != null)
        {
            string? aviso = null;
            if (!c.Encontrado)
            {
                var ult = espejo?.S("actualizado") is string act && DateTime.TryParse(act, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var fa)
                    ? fa.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : null;
                aviso = "No figura en el stock actual (buscador SONA): puede haber sido pickeado/despachado, ajustado a 0 o cambiado de código." +
                        (espejo?.S("posicion") is { } up ? $" Último dato conocido: en {up}" + (ult != null ? $" ({ult})" : "") + "." : "");
            }
            return new { tipo = "contenedor", contenedor = c, enStock = c.Encontrado, aviso, movimientos = movs, conteos };
        }
        // por descripción (LES): "clipstrip venus", "exhibidor going"
        var porDesc = await BuscarPorDescripcion(texto);
        if (porDesc.Count > 0) return new { tipo = "descripcion", texto = texto.Trim(), articulos = porDesc };
        return new { tipo = "nada", mensaje = $"No se encontró \"{t}\" como posición, contenedor, artículo ni descripción." };
    }

    /// <summary>Guarda en la base la ficha de una posición tal como la informa el WMS (Ubicaciones).</summary>
    public static void GuardarFilaLayout(Db _db, FilaLayout f)
    {
        if (f.Cara.Length == 0 || f.Columna == 0 || f.Nivel == 0) return;
        _db.Ejecutar("""
            INSERT INTO posiciones (codigo, cara, columna, nivel, ubic_ctrl, capacidad, capacidad_wms, ancho_cm, alto_cm, prof_cm, estado_wms, articulo_wms, empresa_wms, mezcla_wms, actualizado)
            VALUES ($1,$2,$3,$4,$5,$6,$6,$7,$8,$9,$10,$11,$12,$13,$14)
            ON CONFLICT(codigo) DO UPDATE SET ubic_ctrl=COALESCE(excluded.ubic_ctrl, posiciones.ubic_ctrl), capacidad=COALESCE(excluded.capacidad, posiciones.capacidad),
              capacidad_wms=COALESCE(excluded.capacidad_wms, posiciones.capacidad_wms),
              ancho_cm=excluded.ancho_cm, alto_cm=excluded.alto_cm, prof_cm=excluded.prof_cm, estado_wms=excluded.estado_wms,
              articulo_wms=excluded.articulo_wms, empresa_wms=excluded.empresa_wms, mezcla_wms=excluded.mezcla_wms, actualizado=excluded.actualizado
            """, Posicion.Codigo(f.Cara, f.Columna, f.Nivel), f.Cara, f.Columna, f.Nivel, f.UbicCtrl, f.Capacidad, f.Ancho, f.Alto, f.Prof,
            f.Estado.Length > 0 ? f.Estado : null, f.Articulo, f.Empresa, f.Mezcla, Db.Ahora());
    }

    readonly ConcurrentDictionary<string, DateTime> _carasRefrescadas = new();

    /// <summary>Trae del WMS web la ficha de las posiciones de una cara (mezcla, capacidad, Ubic.Ctrl). Una vez cada 30 min por cara.</summary>
    public async Task RefrescarCara(string usuario, string cara, TimeSpan? maximo = null)
    {
        var c = cara.ToUpperInvariant();
        if (_carasRefrescadas.TryGetValue(c, out var h) && DateTime.UtcNow - h < (maximo ?? TimeSpan.FromMinutes(30))) return;
        _carasRefrescadas[c] = DateTime.UtcNow;
        var u = usuario.ToUpperInvariant();
        var clave = _db.Q1("SELECT clave_cifrada FROM usuarios WHERE usuario=$1", u)?.S("clave_cifrada");
        if (string.IsNullOrEmpty(clave)) return;
        var web = new SesionWeb(_cfg, u, Seguridad.Descifrar(clave, _cfg.Secreto));
        foreach (var f in await web.LayoutDeCara(c)) GuardarFilaLayout(_db, f);
        OlvidarCaras();
    }

    // ================================================================ historia (WMS web) e ingresos/egresos (e-TMS)
    readonly ConcurrentDictionary<string, SesionWeb> _webs = new();
    Etms? _etms;
    public Etms EtmsCliente => _etms ??= new Etms(_cfg)
    {
        // si el e-TMS numera los clientes distinto que el WMS, se lo busca por nombre y alias (ZEMP)
        NombresDe = e => Empresas().Where(x => x.Codigo == e).SelectMany(x => new[] { x.Nombre }.Concat(x.Alias)).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
    };

    (string Usuario, string Clave) Credenciales(string usuario)
    {
        var u = usuario.ToUpperInvariant();
        var clave = _db.Q1("SELECT clave_cifrada FROM usuarios WHERE usuario=$1", u)?.S("clave_cifrada");
        if (string.IsNullOrEmpty(clave)) throw new ErrorUsuario("No tengo guardada tu clave del WMS: salí e ingresá de nuevo.");
        return (u, Seguridad.Descifrar(clave, _cfg.Secreto));
    }

    /// <summary>Historia del WMS (ingresos, guardados, reubicaciones, picking, egresos) de un contenedor o de un artículo.</summary>
    public async Task<TablaReporte> HistoriaWms(string usuario, string? contenedor, string? articulo, int dias = 365)
    {
        var (u, c) = Credenciales(usuario);
        var web = _webs.GetOrAdd(u + "|" + c.GetHashCode(), _ => new SesionWeb(_cfg, u, c));   // si cambia la clave, sesión nueva
        var hasta = DateTime.Today;
        return await web.Historia(contenedor?.Trim().ToUpperInvariant(), articulo?.Trim().ToUpperInvariant(), hasta.AddDays(-Math.Clamp(dias, 1, 3650)), hasta);
    }

    /// <summary>Ingresos y egresos del artículo en el e-TMS (a qué PDV salió, remito, estado).</summary>
    public async Task<Etms.Resultado> IngresosEgresos(string usuario, string articulo, int? empresa)
    {
        var art = articulo.Trim().ToUpperInvariant();
        empresa ??= _db.Q1("SELECT empresa FROM articulos WHERE codigo=$1", art)?.I("empresa");
        if (empresa == null)
        {
            var a = await InfoArticulo(art);
            empresa = a.Empresa;
        }
        if (empresa == null) throw new ErrorUsuario($"No sé de qué empresa es {art}: consultalo primero en el buscador.");
        // 1) las propias de "Mis credenciales" · 2) el usuario general (ZCFG) · 3) las del WMS de quien consulta
        var propia = _db.Q1("SELECT usuario_ext, clave FROM credenciales WHERE usuario=$1 AND sistema='etms'", usuario.ToUpperInvariant());
        var (u, c) = propia != null && !string.IsNullOrEmpty(propia.S("usuario_ext")) && !string.IsNullOrEmpty(propia.S("clave"))
            ? (propia.S("usuario_ext")!, Seguridad.Descifrar(propia.S("clave")!, _cfg.Secreto))
            : string.IsNullOrWhiteSpace(_cfg.Etms.Usuario) ? Credenciales(usuario) : (_cfg.Etms.Usuario, _cfg.ClaveEtms);
        var r = await EtmsCliente.IngresosEgresos(u, c, empresa.Value, art);
        return r with { Foto = r.Foto != null ? UrlImagen(r.Foto) : null };
    }

    /// <summary>Ubic.Ctrl y nivel de una posición escrita como la informa el buscador ("IC-006-04", "IC0604").</summary>
    public (long? Ubic, int? Nivel) UbicacionDe(string? posicion)
    {
        var cod = Posicion.Normalizar(posicion);
        if (cod == null) return (null, null);
        var p = Posicion.Interpretar(cod, CarasConocidas());
        if (p == null) return (null, null);
        var ubic = _db.Q1("SELECT ubic_ctrl FROM posiciones WHERE cara=$1 AND columna=$2 AND ubic_ctrl IS NOT NULL LIMIT 1", p.Cara, p.Columna)?.L("ubic_ctrl");
        return (ubic, p.Nivel);
    }

    // ================================================================ reglas de destino
    public async Task<Carga> ArmarCarga(IEnumerable<string> codigos, string? origen = null)
    {
        var conts = new List<InfoContenedor>();
        foreach (var c in codigos) conts.Add(await InfoContenedor(c, false));
        var arts = conts.Select(c => c.Articulo).Where(a => a != null).Distinct().ToList();
        var emps = conts.Select(c => c.Empresa).Where(e => e != null).Distinct().ToList();
        var conM3 = conts.Where(c => c.M3 != null).ToList();
        double? m3 = conM3.Count > 0 ? conM3.Sum(c => c.M3!.Value) : null;
        var umbral = _db.Parametro("poca_cantidad_m3", 0.1);
        return new Carga(conts, arts.Count == 1 ? arts[0] : null, emps.Count == 1 ? emps[0] : null, m3, m3 != null && m3 <= umbral, origen);
    }

    static readonly Dictionary<int, string> NombresRegla = new()
    {
        [1] = "Mismo artículo con lugar", [2] = "Poca cantidad: junto al mismo artículo o empresa", [3] = "Posición vacía en el núcleo del cliente",
        [4] = "Desborde del cliente", [5] = "Zona flexible",
    };

    bool RotacionAlta(string? articulo)
    {
        if (articulo == null) return false;
        var dias = _db.Parametro("dias_rotacion", 90);
        return _db.Contar("SELECT count(*) FROM movimientos WHERE articulo=$1 AND hora_servidor > $2", articulo, Db.Iso(DateTime.UtcNow.AddDays(-dias))) >= 10;
    }

    /// <summary>
    /// Sugerencia de destino: 1) mismo artículo con lugar · 2) poca cantidad: mismo artículo o empresa ·
    /// 3) vacía en el núcleo del cliente (nivel según rotación) · 4) desborde · 5) flex. Nunca llenas, inhabilitadas ni de otro cliente.
    /// </summary>
    public async Task<List<Sugerencia>> SugerirDestino(string usuario, Carga carga, int cuantas = 5)
    {
        var necesarios = Math.Max(1, carga.Contenedores.Count);
        var candidatos = new List<(Fila P, int Regla)>();
        var vistos = new HashSet<string>();
        void Agregar(IEnumerable<Fila> filas, int regla)
        {
            foreach (var p in filas) { var c = p.S("codigo")!; if (c == carga.Origen || !vistos.Add(c)) continue; candidatos.Add((p, regla)); }
        }
        const string Base = "SELECT pe.* FROM pos_efectiva pe WHERE pe.habilitada=1 AND pe.tipo IN ('reserva','picking') AND pe.ubic_ctrl IS NOT NULL";
        const string Permitida = "(pe.empresa IS NULL OR pe.empresa = $1)";

        if (carga.Articulo != null)
        {
            var posArticulo = new List<string>();
            try { posArticulo = (await _buscador.Articulo(carga.Articulo)).Select(r => Posicion.Normalizar(r.Posicion)).Where(x => x != null).Distinct().ToList()!; } catch { }
            Agregar(_db.Q($"""
                {Base} AND {Permitida} AND (pe.codigo IN (SELECT value FROM json_each($3)) OR pe.articulo_wms = $2
                  OR EXISTS (SELECT 1 FROM contenedores c WHERE c.posicion = pe.codigo AND c.articulo = $2))
                ORDER BY pe.nivel, pe.cara, pe.columna LIMIT 15
                """, carga.Empresa, carga.Articulo, posArticulo), 1);
        }
        if (carga.Poca && carga.Empresa != null)
            Agregar(_db.Q($"""
                {Base} AND {Permitida} AND pe.estado_wms = 'Ocupado'
                  AND (pe.empresa_wms = $1 OR EXISTS (SELECT 1 FROM contenedores c WHERE c.posicion = pe.codigo AND c.empresa = $1))
                ORDER BY (pe.mezcla = 'monoproducto'), pe.nivel DESC LIMIT 15
                """, carga.Empresa), 2);
        if (carga.Empresa != null)
        {
            var bajos = _db.Parametro("niveles_rotacion_alta", new List<int> { 1, 2 });
            var alta = RotacionAlta(carga.Articulo);
            var orden = alta ? "(pe.nivel IN (SELECT value FROM json_each($2))) DESC, pe.nivel" : "(pe.nivel IN (SELECT value FROM json_each($2))), pe.nivel DESC";
            Agregar(_db.Q($"{Base} AND pe.empresa = $1 AND pe.rol_zona = 'nucleo' AND COALESCE(pe.estado_wms,'Libre') = 'Libre' ORDER BY {orden}, pe.cara, pe.columna LIMIT 20",
                carga.Empresa, System.Text.Json.JsonSerializer.Serialize(bajos)), 3);
            Agregar(_db.Q($"{Base} AND pe.empresa = $1 AND pe.rol_zona = 'desborde' AND COALESCE(pe.estado_wms,'Libre') = 'Libre' ORDER BY pe.nivel DESC, pe.cara, pe.columna LIMIT 15", carga.Empresa), 4);
        }
        Agregar(_db.Q($"{Base} AND pe.empresa IS NULL AND pe.rol_zona = 'flex' AND pe.tipo = 'reserva' AND COALESCE(pe.estado_wms,'Libre') = 'Libre' ORDER BY pe.nivel DESC, pe.cara, pe.columna LIMIT 15"), 5);

        var lista = new List<Sugerencia>();
        var consultas = 0;
        foreach (var (p, regla) in candidatos)
        {
            if (lista.Count >= cuantas || consultas >= 12) break;
            if (p.S("mezcla") == "monoproducto" && regla > 2 && p.S("estado_wms") == "Ocupado") continue;
            int? libres = null; int? capacidad = p.I("capacidad");
            try
            {
                consultas++;
                var o = await Ocupacion(usuario, p.S("cara")!, p.I("columna")!.Value, p.I("nivel")!.Value);
                if (o == null) continue;
                libres = o.Libres; capacidad = o.Capacidad;
                if (o.Libres < necesarios) continue;
                if (regla == 1 && p.S("mezcla") == "monoproducto" && await HayAjeno(o.Contenedores, carga.Articulo)) continue;
            }
            catch (Exception e) { _log.LogWarning("No se pudo confirmar {P} en vivo: {E}", p.S("codigo"), e.Message); }
            lista.Add(new Sugerencia(p.S("codigo")!, Posicion.Legible(p.S("cara")!, p.I("columna")!.Value, p.I("nivel")), p.L("ubic_ctrl"), p.I("nivel")!.Value,
                regla, NombresRegla[regla], libres, capacidad));
        }
        return lista;
    }

    async Task<bool> HayAjeno(IEnumerable<string> contenedores, string? articulo)
    {
        if (articulo == null) return false;
        foreach (var c in contenedores)
        {
            var i = await InfoContenedor(c, false);
            if (i.Articulo != null && i.Articulo != articulo) return true;
        }
        return false;
    }

    /// <summary>
    /// Valida el destino: bloquea lleno / inhabilitado / misma posición / otro depósito / mezcla no permitida;
    /// pide autorización si es zona de otro cliente; avisa NN/proyectos o posición sin ficha.
    /// </summary>
    public async Task<Validacion> ValidarDestino(string usuario, Carga carga, string destinoTexto, int? nivel, bool multi = false)
    {
        var v = new Validacion();
        var d = await ResolverPosicion(destinoTexto, nivel, usuario);
        if (d == null) { v.Bloqueos.Add($"No se reconoce la posición \"{destinoTexto}\"."); return v; }
        v.Destino = d;
        if (d.Nivel == null) { v.Bloqueos.Add("Falta el nivel de destino."); return v; }
        if (d.UbicCtrl == null) v.Avisos.Add("La posición no tiene Ubic.Ctrl en la base: escaneá la etiqueta o sincronizá el layout.");
        var codigo = d.Codigo!;
        if (carga.Origen != null && carga.Origen == codigo) v.Bloqueos.Add("El destino es la misma posición de origen.");
        if (carga.Contenedores.Any(c => c.Posicion != null && Posicion.Normalizar(c.Posicion) == codigo)) v.Bloqueos.Add("El contenedor ya está en esa posición.");

        var ficha = _db.Q1("SELECT pe.*, p.mezcla AS mezcla_propia FROM pos_efectiva pe JOIN posiciones p ON p.codigo = pe.codigo WHERE pe.codigo=$1", codigo);
        if (ficha == null) v.Avisos.Add("La posición no tiene ficha configurada: se toma como libre.");
        if (ficha != null && !ficha.B("habilitada")) v.Bloqueos.Add("Posición inhabilitada" + (ficha.S("motivo_inhab") is { } mi ? ": " + mi : "") + ".");
        if (ficha?.S("tipo") == "otro_deposito") v.Bloqueos.Add("Esa posición es de otro depósito: se mueve con \"Transferir contenedor\" del WMS.");
        if (ficha?.S("tipo") is "nn" or "proyectos") v.Avisos.Add($"Destino en zona {(ficha.S("tipo") == "nn" ? "NN" : "de proyectos")}.");

        // profundidad y mezcla al día: la ficha de la cara en el WMS web (si se amplió la profundidad hace un rato, se ve)
        try { await RefrescarCara(usuario, d.Cara, TimeSpan.FromMinutes(2)); }
        catch (Exception e) { _log.LogWarning("No se pudo leer la ficha de {Cara} en el WMS web: {E}", d.Cara, e.Message); }
        try
        {
            var o = await Ocupacion(usuario, d.Cara, d.Columna, d.Nivel.Value);
            v.Ocupacion = o;
            if (o != null)
            {
                var n = Math.Max(1, carga.Contenedores.Count);
                if (o.Capacidad == 0)
                    v.Avisos.Add($"El WMS no informa la profundidad de {d.Legible}: se transfiere y el WMS decide.");
                else if (o.Libres < n)
                    v.Bloqueos.Add((multi
                        ? $"Hacen falta {n} huecos y {d.Legible} tiene {o.Libres} libre(s)"
                        : o.Libres == 0 ? $"{d.Legible} está llena" : $"{d.Legible} tiene {o.Libres} lugar(es) libre(s) y se mueven {n}")
                        + $" según el WMS (profundidad {o.Capacidad}, ocupados {o.Ocupados}). Ampliá la profundidad en el WMS y volvé a controlar.");
                else if (multi) v.Avisos.Add($"Entran: {n} contenedor(es) y hay {o.Libres} hueco(s) libre(s).");
                var mezcla = multi ? "libre" : ficha?.S("mezcla") ?? "libre";   // multicontenedor: se agrupa a propósito, no se controla mezcla
                if (mezcla != "libre" && o.Contenedores.Count > 0 && ficha?.L("mezcla_wms") == null && ficha?.S("mezcla_propia") == null)
                {
                    // No sabemos qué dice el WMS de esta posición: se trae la ficha de la cara antes de bloquear por mezcla
                    try { await RefrescarCara(usuario, d.Cara); ficha = _db.Q1("SELECT pe.*, p.mezcla AS mezcla_propia FROM pos_efectiva pe JOIN posiciones p ON p.codigo = pe.codigo WHERE pe.codigo=$1", codigo) ?? ficha; mezcla = ficha?.S("mezcla") ?? mezcla; }
                    catch (Exception e) { _log.LogWarning("No se pudo leer la ficha de {Cara} en el WMS: {E}", d.Cara, e.Message); }
                }
                if (ficha?.L("mezcla_wms") == 1 && ficha.S("mezcla_propia") == null) v.Avisos.Add("La posición admite multiproducto en el WMS.");
                if (mezcla != "libre" && o.Contenedores.Count > 0)
                {
                    var presentes = new List<InfoContenedor>();
                    foreach (var c in o.Contenedores.Take(10)) presentes.Add(await InfoContenedor(c, false));
                    var otraEmpresa = presentes.FirstOrDefault(p => p.Empresa != null && carga.Empresa != null && p.Empresa != carga.Empresa);
                    var otroArticulo = presentes.FirstOrDefault(p => p.Articulo != null && carga.Articulo != null && p.Articulo != carga.Articulo);
                    var origenRegla = ficha?.S("mezcla_propia") != null ? "ficha de la posición (ZPOS)" : "zona " + d.Cara + " (ZZON)";
                    if (otraEmpresa != null)
                        v.Bloqueos.Add($"En el destino hay mercadería de {otraEmpresa.EmpresaNombre ?? "otra empresa"} ({otraEmpresa.Contenedor}) y la {origenRegla} no admite mezclar clientes.");
                    else if (mezcla == "monoproducto" && otroArticulo != null)
                    {
                        if (carga.Poca) v.Avisos.Add($"Posición monoproducto con {otroArticulo.Articulo}: se permite por ser poca cantidad de la misma empresa.");
                        else v.Bloqueos.Add($"La {origenRegla} está como monoproducto y en el destino ya hay {otroArticulo.Articulo} ({otroArticulo.Contenedor}). Si la posición admite multiproducto, cambiala en ZPOS/ZZON o sincronizá el layout del WMS.");
                    }
                }
            }
        }
        catch (Exception e) { v.Avisos.Add($"No se pudo confirmar el lugar libre en el WMS ({e.Message})."); }

        if (multi)
        {
            var empresas = carga.Contenedores.Select(c => c.EmpresaNombre ?? c.Empresa?.ToString() ?? "?").Distinct().ToList();
            if (empresas.Count > 1) v.Avisos.Add($"Multicontenedor con {empresas.Count} empresas ({string.Join(", ", empresas)}): se cargan en el WMS cada uno con su empresa.");
            v.Ok = v.Bloqueos.Count == 0 && v.Forzables.Count == 0;
            return v;
        }
        var empZona = ficha?.I("empresa");
        if (empZona != null && carga.Empresa != null && empZona != carga.Empresa)
            v.Autorizacion = $"Destino {d.Legible} es zona de {NombreEmpresa(empZona) ?? "empresa " + empZona}; el material es de {carga.Contenedores.FirstOrDefault()?.EmpresaNombre ?? "otra empresa"}.";
        if (carga.Empresa == null && carga.Contenedores.Count > 0) v.Avisos.Add("No se pudo determinar la empresa del material.");
        v.Ok = v.Bloqueos.Count == 0 && v.Forzables.Count == 0 && v.Autorizacion == null;
        return v;
    }
}
