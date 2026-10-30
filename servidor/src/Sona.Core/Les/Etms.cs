using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Sona.Wms;

namespace Sona.Les;

/// <summary>
/// e-TMS (www.tms-ejemplo.com.ar): "Ingresos Egresos" de un artículo (POP): cada ingreso al depósito y cada egreso a un punto de venta,
/// con remito, solicitud, PDV y estado. Se entra como un usuario más, se elige el cliente y se baja el Excel del artículo.
/// </summary>
public sealed class Etms
{
    readonly Configuracion _cfg;
    readonly SemaphoreSlim _uso = new(1, 1);
    CookieContainer _cookies = new();
    HttpClient _http;
    string? _usuario;
    readonly Dictionary<int, string> _paginaCliente = new();

    public Etms(Configuracion cfg) { _cfg = cfg; _http = Crear(); }

    HttpClient Crear()
    {
        var h = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true, AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All };
        var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SONA-Deposito/2.4");
        return c;
    }

    string Base => _cfg.Etms.Url.EndsWith('/') ? _cfg.Etms.Url : _cfg.Etms.Url + "/";

    public void Olvidar() { _usuario = null; _paginaCliente.Clear(); _http.Dispose(); _cookies = new(); _http = Crear(); }

    public sealed record Movimiento(string Fecha, string Tipo, double Entrada, double Salida, string Referencia, string? Remito, string? Solicitud,
        string? CodigoPdv, string? Pdv, string? Estado, string? Cliente);
    public sealed record Resultado(string Articulo, string? Descripcion, string? Foto, int Empresa, double Ingresado, double Egresado, double Saldo,
        List<Movimiento> Movimientos);

    static Dictionary<string, string> Campos(string html) => new(Gx.Leer(html).Valores);

    async Task<string> Texto(HttpResponseMessage r) { var t = await r.Content.ReadAsStringAsync(); return t; }

    /// <summary>Prueba un usuario y clave (Mis credenciales).</summary>
    public Task Probar(string usuario, string clave) => Login(usuario, clave);

    async Task Login(string usuario, string clave)
    {
        if (_usuario == usuario) return;
        Olvidar();
        var ini = await Texto(await _http.GetAsync(Base + "Login.aspx"));
        var v = Campos(ini);
        v["__EVENTTARGET"] = ""; v["__EVENTARGUMENT"] = "";
        v["ctl00$Contenido$txtUsuario"] = usuario;
        v["ctl00$Contenido$txtPassword"] = clave;
        v["ctl00$Contenido$btnLogin"] = "Ingresar";
        using var r = await _http.PostAsync(Base + "Login.aspx", new FormUrlEncodedContent(v));
        var t = await Texto(r);
        if ((r.RequestMessage?.RequestUri?.AbsolutePath ?? "").Contains("Login", StringComparison.OrdinalIgnoreCase) || !t.Contains("Logout", StringComparison.OrdinalIgnoreCase))
            throw new ErrorUsuario("El e-TMS no aceptó el usuario y la clave (cargalos en Mis credenciales o en ZCFG › e-TMS).");
        _usuario = usuario;
    }

    /// <summary>Nombres con los que se busca el cliente en el e-TMS si su número no coincide con el del WMS (nombre de la empresa y alias).</summary>
    public Func<int, IEnumerable<string>>? NombresDe { get; set; }

    static string Norm(string s) => Regex.Replace(s.Normalize(System.Text.NormalizationForm.FormD), @"[^A-Za-z0-9]", "").ToUpperInvariant();

    /// <summary>
    /// Página "Ingresos Egresos" del cliente. Se elige el cliente en el selector del e-TMS: por número (el mismo del WMS) y, si el e-TMS
    /// usa otro número, por nombre (empresa y alias de ZEMP). Así no falla con "no tiene el cliente habilitado" cuando sí lo tiene.
    /// </summary>
    async Task<string> PaginaCliente(int empresa)
    {
        if (_paginaCliente.TryGetValue(empresa, out var u)) return u;
        var dash = await Texto(await _http.GetAsync(Base + "Cliente/MicroSite/MainDashboard.aspx"));
        var f = Gx.Leer(dash);
        var v = new Dictionary<string, string>(f.Valores);
        const string ddl = "ctl00$Contenido$SelectorCliente1$DdlClientes";
        var opciones = f.Selects.FirstOrDefault(x => x.Key.EndsWith("DdlClientes", StringComparison.OrdinalIgnoreCase)).Value ?? new();
        var nombres = (NombresDe?.Invoke(empresa) ?? Enumerable.Empty<string>()).Select(Norm).Where(x => x.Length >= 3).ToList();
        var elegida = opciones.FirstOrDefault(o => o.Valor.Trim() == empresa.ToString(CultureInfo.InvariantCulture))
                      ?? opciones.FirstOrDefault(o => nombres.Contains(Norm(o.Texto)))
                      ?? opciones.FirstOrDefault(o => nombres.Any(n => Norm(o.Texto).Contains(n) || (Norm(o.Texto).Length >= 4 && n.Contains(Norm(o.Texto)))));
        string? valor = elegida?.Valor ?? (opciones.Count == 0 ? empresa.ToString(CultureInfo.InvariantCulture) : null);
        if (valor == null)
            throw new ErrorUsuario($"El e-TMS no tiene el cliente {empresa} ({string.Join(" / ", NombresDe?.Invoke(empresa) ?? Array.Empty<string>())}) para este usuario. " +
                                   $"Clientes que ve: {string.Join(", ", opciones.Take(15).Select(o => o.Texto))}. Cargá el nombre como alias en ZEMP o configurá otro usuario del e-TMS en ZCFG.");
        string t;
        var directo = Regex.Match(dash, @"IngresoEgresoPOP\.aspx\?IdCliente=([^""'&\s]+)", RegexOptions.IgnoreCase);
        if (elegida?.Elegida == true && directo.Success) t = dash;
        else
        {
            v["__EVENTTARGET"] = ddl; v["__EVENTARGUMENT"] = ""; v["__LASTFOCUS"] = "";
            v[ddl] = valor;
            t = await Texto(await _http.PostAsync(Base + "Cliente/MicroSite/MainDashboard.aspx", new FormUrlEncodedContent(v)));
        }
        var m = Regex.Match(t, @"IngresoEgresoPOP\.aspx\?IdCliente=([^""'&\s]+)", RegexOptions.IgnoreCase);
        if (!m.Success) throw new ErrorUsuario($"El e-TMS no muestra \"Ingresos Egresos\" para el cliente {elegida?.Texto ?? empresa.ToString()} con este usuario.");
        u = Base + "Cliente/MicroSite/IngresoEgresoPOP.aspx?IdCliente=" + WebUtility.HtmlDecode(m.Groups[1].Value);
        _paginaCliente[empresa] = u;
        return u;
    }

    static readonly Regex ReFila = new(@"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase);
    static readonly Regex ReCelda = new(@"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);

    public async Task<Resultado> IngresosEgresos(string usuario, string clave, int empresa, string articulo)
    {
        var art = articulo.Trim().ToUpperInvariant();
        await _uso.WaitAsync();
        try
        {
            await Login(usuario, clave);
            var url = await PaginaCliente(empresa);
            var pag = await Texto(await _http.GetAsync(url));
            if (pag.Contains("txtPassword", StringComparison.OrdinalIgnoreCase)) { _usuario = null; await Login(usuario, clave); pag = await Texto(await _http.GetAsync(url)); }
            var (link, desc, foto) = await Buscar(url, pag, art);
            if (link == null) throw new ErrorUsuario($"El e-TMS no tiene el artículo {art} para el cliente {empresa}.");
            var bytes = await _http.GetByteArrayAsync(new Uri(new Uri(Base), link));
            var movs = LeerExcel(bytes);
            var ing = movs.Sum(m => m.Entrada); var egr = movs.Sum(m => m.Salida);
            return new Resultado(art, desc, foto, empresa, ing, egr, ing - egr, movs);
        }
        finally { _uso.Release(); }
    }

    /// <summary>Busca el artículo en "Ingresos Egresos": link del Excel, descripción y foto (URL absoluta).</summary>
    async Task<(string? Link, string? Desc, string? Foto)> Buscar(string url, string pag, string art)
    {
        var v = Campos(pag);
        v["__EVENTTARGET"] = "ctl00$Contenido$btnBuscar"; v["__EVENTARGUMENT"] = "";
        v["ctl00$Contenido$txtCodigo"] = art; v["ctl00$Contenido$txtDescripcion"] = "";
        var res = await Texto(await _http.PostAsync(url, new FormUrlEncodedContent(v)));
        // filas: [foto] [código] [descripción] [marca] [Descargar Excel]
        foreach (Match f in ReFila.Matches(res))
        {
            var celdas = ReCelda.Matches(f.Groups[1].Value).Select(c => c.Groups[1].Value).ToList();
            if (celdas.Count < 5 || !Gx.Texto(celdas[1]).Equals(art, StringComparison.OrdinalIgnoreCase)) continue;
            var l = Regex.Match(celdas[^1], @"href=""([^""]*IngresoEgresoReporteExcel\.ashx[^""]*)""", RegexOptions.IgnoreCase);
            var img = Regex.Match(celdas[0], @"(?:data-img|src)=""([^""]+)""", RegexOptions.IgnoreCase);
            var foto = img.Success && !img.Groups[1].Value.Contains("imagenError", StringComparison.OrdinalIgnoreCase)
                ? new Uri(new Uri(Base), WebUtility.HtmlDecode(img.Groups[1].Value)).ToString() : null;
            return (l.Success ? WebUtility.HtmlDecode(l.Groups[1].Value) : null, Gx.Texto(celdas[2]), foto);
        }
        return (null, null, null);
    }

    /// <summary>
    /// Foto del artículo en el e-TMS (para los que no tienen foto en tradebox). La baja con la sesión del e-TMS
    /// y la deja en la caché de imágenes de SONA (<paramref name="archivoCache"/>), así se ve por /api/imagen.
    /// </summary>
    public async Task<List<Foto>> Fotos(string usuario, string clave, int empresa, string articulo, Func<string, string> archivoCache)
    {
        var art = articulo.Trim().ToUpperInvariant();
        await _uso.WaitAsync();
        try
        {
            await Login(usuario, clave);
            var url = await PaginaCliente(empresa);
            var pag = await Texto(await _http.GetAsync(url));
            if (pag.Contains("txtPassword", StringComparison.OrdinalIgnoreCase)) { _usuario = null; await Login(usuario, clave); pag = await Texto(await _http.GetAsync(url)); }
            var (_, _, foto) = await Buscar(url, pag, art);
            if (foto == null) return new();
            using var r = await _http.GetAsync(foto.Replace(" ", "%20"));
            if (!r.IsSuccessStatusCode) return new();
            var bytes = await r.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 200) return new();
            await File.WriteAllBytesAsync(archivoCache(foto), bytes);
            return new() { new Foto("e-TMS", foto) };
        }
        finally { _uso.Release(); }
    }

    /// <summary>Excel del e-TMS: Cliente, Fecha, Codigo, Descripcion, Referencia, Movimiento, Comprobante, Entrada, Salida, Saldo, NroSolicitud, Codigo04, PDV, Estado.</summary>
    public static List<Movimiento> LeerExcel(byte[] bytes)
    {
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheets.First();
        var usado = ws.RangeUsed();
        if (usado == null) return new();
        var cab = usado.FirstRow().Cells().Select(c => c.GetString().Trim()).ToList();
        int Col(string n) => cab.FindIndex(c => c.Equals(n, StringComparison.OrdinalIgnoreCase));
        string? S(IXLRangeRow f, string n) { var i = Col(n); if (i < 0) return null; var t = f.Cell(i + 1).GetString().Trim(); return t.Length == 0 ? null : t; }
        double D(IXLRangeRow f, string n) { var i = Col(n); if (i < 0) return 0; var c = f.Cell(i + 1); return c.TryGetValue<double>(out var d) ? d : double.TryParse(c.GetString().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0; }
        var lista = new List<Movimiento>();
        foreach (var f in usado.RowsUsed().Skip(1))
        {
            var ic = Col("Fecha");
            var fecha = ic >= 0 && f.Cell(ic + 1).DataType == XLDataType.DateTime ? f.Cell(ic + 1).GetDateTime().ToString("dd/MM/yyyy") : S(f, "Fecha") ?? "";
            var fm = Regex.Match(fecha, @"^(\d{1,2})/(\d{1,2})/(\d{4})");
            if (fm.Success) fecha = $"{int.Parse(fm.Groups[1].Value):00}/{int.Parse(fm.Groups[2].Value):00}/{fm.Groups[3].Value}";
            var refe = S(f, "Referencia") ?? "";
            var rem = Regex.Match(refe, @"Rem:\s*(\d+)");
            var comp = S(f, "Comprobante");
            lista.Add(new Movimiento(fecha, (S(f, "Movimiento") ?? "").ToUpperInvariant(), D(f, "Entrada"), D(f, "Salida"), refe,
                rem.Success ? rem.Groups[1].Value : comp is { Length: > 0 } and not "0" ? comp : null,
                S(f, "NroSolicitud"), S(f, "Codigo04"), S(f, "PDV"), S(f, "Estado"), S(f, "Cliente")));
        }
        return lista.OrderByDescending(m => DateTime.TryParseExact(m.Fecha, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue).ToList();
    }
}
