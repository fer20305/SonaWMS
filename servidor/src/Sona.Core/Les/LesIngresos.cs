using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sona.Les;

/// <summary>
/// "Ingresos Pendientes" del LES (les.les-ejemplo.com/IngresoPendiente): la precarga de cada recepción, la descarga
/// (pallets, depósito, fotos, remito firmado), fotos POP / de contenedor, carátulas y el pase a "Pend. validar".
/// Cada usuario de SONA entra con SU usuario del LES (lo carga una vez y queda cifrado).
/// </summary>
public sealed class LesIngresos
{
    readonly string _base;
    readonly string _usuario, _clave;
    readonly CookieContainer _cookies = new();
    readonly HttpClient _http;
    /// <summary>Para el login: formulario común, como el navegador (sin "X-Requested-With"), con las mismas cookies.</summary>
    readonly HttpClient _form;
    readonly SemaphoreSlim _login = new(1, 1);
    bool _logueado;

    public string Usuario => _usuario;
    public string Base => _base;
    /// <summary>Captura (diagnóstico): pedido, lo enviado (claves tapadas), estado y respuesta.</summary>
    public Action<string, string, int, string>? Traza { get; set; }

    async Task Trazar(HttpResponseMessage r, string texto)
    {
        var t = Traza; if (t == null) return;
        try
        {
            var req = r.RequestMessage;
            var env = "";
            if (req?.Content is FormUrlEncodedContent f) env = Captura.TaparClaves(await f.ReadAsStringAsync());
            else if (req?.Content is MultipartFormDataContent m)
            {
                var partes = new List<string>();
                foreach (var p in m)
                    partes.Add(p.Headers.ContentDisposition?.Name?.Trim('"') + (p is StringContent sc ? "=" + await sc.ReadAsStringAsync() : $" (archivo {p.Headers.ContentLength} bytes)"));
                env = "multipart: " + string.Join(" | ", partes);
            }
            t($"{req?.Method} {req?.RequestUri}", env, (int)r.StatusCode, texto);
        }
        catch { }
    }

    public LesIngresos(Configuracion cfg, string usuario, string clave)
    {
        var b = string.IsNullOrWhiteSpace(cfg.Les.Url) ? "https://les.les-ejemplo.com/" : cfg.Les.Url.Trim();
        // el LES real trabaja en https (por http redirige y se pierde el POST)
        b = Regex.Replace(b, @"^http://les\.les-ejemplo\.com", "https://les.les-ejemplo.com", RegexOptions.IgnoreCase);
        _base = (b.EndsWith('/') ? b : b + "/") + "IngresoPendiente/";
        _usuario = usuario; _clave = clave;
        var h = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true, AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All };
        _http = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SONA-Deposito/1.0");
        _http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        _form = new HttpClient(h, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
        _form.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SONA-Deposito/1.0");
    }

    /// <summary>Último intento de login (para el diagnóstico): adónde terminó y qué dijo el LES, sin claves.</summary>
    public string? UltimoLogin { get; private set; }

    static bool EsLogin(string t) => t.Contains("name=\"Username\"", StringComparison.OrdinalIgnoreCase) && t.Contains("name=\"Password\"", StringComparison.OrdinalIgnoreCase);

    public async Task Login()
    {
        await _login.WaitAsync();
        try
        {
            // como el navegador (capturado el 19/10): GET Index → POST Login (Username, Password) → 302 a Index.
            // El usuario se prueba tal cual lo cargó y, si no entra, en mayúsculas (así lo usa el WMS).
            var variantes = new[] { _usuario.Trim(), _usuario.Trim().ToUpperInvariant() }.Distinct().ToList();
            string? mensaje = null;
            foreach (var u in variantes)
            {
                try { using var ix = await _form.GetAsync(_base + "Index"); var ti = await ix.Content.ReadAsStringAsync(); await Trazar(ix, ti); }
                catch (HttpRequestException e) { throw new ErrorUsuario("No se pudo conectar con el LES: " + e.Message, 502); }
                using var req = new HttpRequestMessage(HttpMethod.Post, _base + "Login")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = u, ["Password"] = _clave }),
                };
                req.Headers.Referrer = new Uri(_base + "Index");
                using var r = await _form.SendAsync(req);
                var t = await r.Content.ReadAsStringAsync();
                await Trazar(r, t);
                var fin = r.RequestMessage?.RequestUri;
                UltimoLogin = $"{(int)r.StatusCode} → {fin?.AbsolutePath}";
                if ((int)r.StatusCode >= 500) throw new ErrorUsuario($"El LES respondió {(int)r.StatusCode} al ingresar (problema del LES, probá en un rato).", 502);
                if (Regex.IsMatch(fin?.AbsolutePath ?? "", @"(Change|Cambi|Expir|Reset)\w*Pass|Password/(Change|Cambi)", RegexOptions.IgnoreCase))
                    throw new ErrorUsuario("El LES pide cambiar la clave de este usuario: entrá una vez a les.les-ejemplo.com, cambiala y cargá la nueva en \"Mis credenciales\".", 428);
                var enLogin = EsLogin(t) || fin?.AbsolutePath.EndsWith("/Login", StringComparison.OrdinalIgnoreCase) == true || (int)r.StatusCode == 401;
                if (!enLogin && (int)r.StatusCode < 400) { _logueado = true; return; }
                var msj = Regex.Match(t, @"(?:validation-summary-errors|text-danger|alert|error)[^>]*>\s*(?:<[^>]+>\s*)*([^<]{4,160})", RegexOptions.IgnoreCase);
                if (msj.Success) mensaje = WebUtility.HtmlDecode(msj.Groups[1].Value.Trim());
            }
            throw new ErrorUsuario("El LES no aceptó el usuario y la clave de Ingresos" + (mensaje != null ? ": " + mensaje : ".") +
                " Probá entrar con esos mismos datos en les.les-ejemplo.com; si ahí entra, avisá (queda la captura del intento en capturas).", 428);
        }
        finally { _login.Release(); }
    }

    async Task<string> Texto(Func<Task<HttpResponseMessage>> pedir)
    {
        if (!_logueado) await Login();
        var r = await pedir();
        var t = await r.Content.ReadAsStringAsync();
        await Trazar(r, t);
        if (EsLogin(t) || r.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logueado = false;
            await Login();
            r = await pedir();
            t = await r.Content.ReadAsStringAsync();
            await Trazar(r, t);
        }
        if ((int)r.StatusCode >= 400) throw new ErrorUsuario($"El LES respondió {(int)r.StatusCode}: {Regex.Replace(t, "<[^>]+>", " ").Trim()[..Math.Min(200, Regex.Replace(t, "<[^>]+>", " ").Trim().Length)]}", 502);
        return t;
    }

    static JsonNode Json(string t)
    {
        try { return JsonNode.Parse(t) ?? new JsonObject(); }
        catch (JsonException) { throw new ErrorUsuario("El LES no devolvió datos (¿se cerró la sesión?).", 502); }
    }

    static void Exito(JsonNode n, string accion)
    {
        if (n is JsonObject o && o["success"] is JsonValue v && v.TryGetValue<bool>(out var ok) && !ok)
            throw new ErrorUsuario($"El LES rechazó {accion}: {o["message"]?.ToString() ?? "sin detalle"}", 502);
    }

    public async Task<JsonNode> Get(string accion, Dictionary<string, string>? q = null)
    {
        var url = _base + accion + (q == null || q.Count == 0 ? "" : "?" + string.Join("&", q.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value))));
        return Json(await Texto(() => _http.GetAsync(url)));
    }

    async Task<JsonNode> PostForm(string accion, Dictionary<string, string> campos) =>
        Json(await Texto(() => _http.PostAsync(_base + accion, new FormUrlEncodedContent(campos))));

    async Task<JsonNode> PostMultipart(string accion, Func<MultipartFormDataContent> armar) =>
        Json(await Texto(() => _http.PostAsync(_base + accion, armar())));

    static ByteArrayContent Archivo(byte[] datos, string nombre)
    {
        var c = new ByteArrayContent(datos);
        c.Headers.ContentType = new MediaTypeHeaderValue(nombre.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : nombre.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "image/jpeg");
        return c;
    }

    // ================================================================ lectura

    /// <summary>Texto crudo (diagnóstico: la página Index y sus scripts).</summary>
    public Task<string> Crudo(string url) => Texto(() => _http.GetAsync(url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : new Uri(new Uri(_base), url).ToString()));

    public Task<JsonNode> Lista() => Get("GetAll", new() { ["_"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() });
    public Task<JsonNode> Sla() => Get("GetSlaConfig");
    public async Task<JsonNode> Detalle(long id) { var n = await Get("GetIngresoDetail", new() { ["id"] = id.ToString() }); Exito(n, "el detalle"); return n; }
    public Task<JsonNode> FotosPallets(long id) => Get("GetPalletPhotos", new() { ["idIngreso"] = id.ToString() });
    public Task<JsonNode> FotosPop(long id, int clienteId, IEnumerable<string> codigos) =>
        Get("CheckFotosPop", new() { ["idIngreso"] = id.ToString(), ["clienteId"] = clienteId.ToString(), ["codigos"] = string.Join(",", codigos) });
    public Task<JsonNode> FotosContenedor(long id, IEnumerable<string> codigos) =>
        Get("CheckFotosContenedor", new() { ["idIngreso"] = id.ToString(), ["codigos"] = string.Join(",", codigos) });

    /// <summary>Carátula "Pendiente de ingreso" de cada pallet (PDF).</summary>
    public async Task<byte[]> CaratulaPdf(long id)
    {
        if (!_logueado) await Login();
        var r = await _http.GetAsync(_base + "ExportPdf?id=" + id);
        var b = await r.Content.ReadAsByteArrayAsync();
        if (b.Length > 4 && b[0] == '%' && b[1] == 'P') return b;
        _logueado = false; await Login();
        b = await (await _http.GetAsync(_base + "ExportPdf?id=" + id)).Content.ReadAsByteArrayAsync();
        if (b.Length > 4 && b[0] == '%' && b[1] == 'P') return b;
        throw new ErrorUsuario("El LES no devolvió el PDF de las carátulas.", 502);
    }

    public async Task<byte[]?> Imagen(string urlRelativa)
    {
        if (!_logueado) await Login();
        var u = urlRelativa.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? urlRelativa : new Uri(new Uri(_base), urlRelativa).ToString();
        var r = await _http.GetAsync(u);
        return r.IsSuccessStatusCode && (r.Content.Headers.ContentType?.MediaType ?? "").StartsWith("image") ? await r.Content.ReadAsByteArrayAsync() : null;
    }

    // ================================================================ escritura

    public async Task<JsonNode> SubirFotoPallet(long id, byte[] foto, string nombre)
    {
        var n = await PostMultipart("UploadPalletPhoto", () => new MultipartFormDataContent { { new StringContent(id.ToString()), "idIngreso" }, { Archivo(foto, nombre), "foto", nombre } });
        Exito(n, "la foto del pallet"); return n;
    }

    public async Task<JsonNode> SubirFotoPop(int clienteId, string codigo, byte[] foto, string nombre)
    {
        var n = await PostMultipart("UploadFotoPop", () => new MultipartFormDataContent { { new StringContent(clienteId.ToString()), "clienteId" }, { new StringContent(codigo), "codigo" }, { Archivo(foto, nombre), "foto", nombre } });
        Exito(n, "la foto del POP"); return n;
    }

    public async Task<JsonNode> SubirFotoContenedor(long id, string codigo, byte[] foto, string nombre)
    {
        var n = await PostMultipart("UploadFotoArticulo", () => new MultipartFormDataContent { { new StringContent(id.ToString()), "idIngreso" }, { new StringContent(codigo), "codigo" }, { Archivo(foto, nombre), "foto", nombre } });
        Exito(n, "la foto del contenedor"); return n;
    }

    /// <summary>"Confirmar" de la descarga: pallets reales, depósito, observación, a desglosar, artículos y el remito firmado.</summary>
    public async Task<JsonNode> ConfirmarDescarga(JsonObject modelo, byte[]? remito, string? nombreRemito)
    {
        var n = await PostMultipart("DownloadPallets", () =>
        {
            var m = new MultipartFormDataContent { { new StringContent(modelo.ToJsonString()), "ingresoJson" } };
            if (remito != null) m.Add(Archivo(remito, nombreRemito ?? "remito.jpg"), "remitoFirmado", nombreRemito ?? "remito.jpg");
            return m;
        });
        Exito(n, "la descarga"); return n;
    }

    /// <summary>Genera etiquetas (y sus contenedores) en el LES: prefijo PC (A4), PP o Bultos (10x8).</summary>
    public async Task<JsonNode> GenerarEtiquetas(long ingresoId, int clienteId, long articuloId, string codigo, double cantidadArticulo, string descripcion, string prefijo, int cantidad)
    {
        var n = await PostForm("GenerarEtiqueta", new()
        {
            ["articuloId"] = articuloId.ToString(), ["ingresoId"] = ingresoId.ToString(), ["clienteId"] = clienteId.ToString(), ["codigo"] = codigo,
            ["cantidadArticulo"] = cantidadArticulo.ToString(System.Globalization.CultureInfo.InvariantCulture), ["descripcion"] = descripcion,
            ["prefijo"] = prefijo, ["cantidad"] = cantidad.ToString(),
        });
        Exito(n, "la generación de etiquetas"); return n;
    }

    /// <summary>"Procesar" (Pend. ingreso WMS → Pend. validar).</summary>
    public async Task<JsonNode> ConfirmarIngresoWms(long id)
    {
        var n = await PostForm("ConfirmarIngresoWms", new() { ["id"] = id.ToString() });
        Exito(n, "el pase a Pend. validar"); return n;
    }
}
