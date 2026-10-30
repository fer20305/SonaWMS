using System.Net;

namespace Sona.Wms;

public sealed record Respuesta(Uri Url, int Estado, string Texto);

/// <summary>Navegador mínimo para el WMS: guarda cookies de sesión y sigue redirecciones (303 → GET) como el handheld.</summary>
public sealed class Navegador : IDisposable
{
    CookieContainer _cookies = new();
    HttpClient _http;
    readonly TimeSpan _timeout;

    /// <summary>Captura (diagnóstico): cada pedido con su respuesta (método + datos enviados con las claves tapadas).</summary>
    public Action<string, string, Respuesta>? Traza { get; set; }

    public Navegador(int timeoutSegundos = 30)
    {
        _timeout = TimeSpan.FromSeconds(timeoutSegundos);
        _http = Crear();
    }

    HttpClient Crear()
    {
        var h = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true, AllowAutoRedirect = true, MaxAutomaticRedirections = 10, AutomaticDecompression = DecompressionMethods.All };
        var c = new HttpClient(h) { Timeout = _timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android) SONA-Deposito/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json,*/*");
        return c;
    }

    /// <summary>Borra las cookies (nueva sesión).</summary>
    public void Vaciar()
    {
        _http.Dispose();
        _cookies = new CookieContainer();
        _http = Crear();
    }

    public Task<Respuesta> Get(string url, string? referer = null) => Pedir(new HttpRequestMessage(HttpMethod.Get, url), referer);
    public Task<Respuesta> Get(Uri url, string? referer = null) => Pedir(new HttpRequestMessage(HttpMethod.Get, url), referer);

    public Task<Respuesta> Post(Uri url, Dictionary<string, string> campos, string? referer = null, bool ajax = false)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(campos) };
        m.Headers.TryAddWithoutValidation("Origin", url.GetLeftPart(UriPartial.Authority));
        if (ajax) m.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        return Pedir(m, referer);
    }

    /// <summary>Descarga binaria (PDF de la simulación de faltantes, "Ver OP").</summary>
    public async Task<(byte[] Datos, string? Tipo, Uri Url)> Bajar(Uri url, string? referer = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Get, url);
        if (referer != null) m.Headers.TryAddWithoutValidation("Referer", Ascii(referer));
        using var r = await _http.SendAsync(m);
        return (await r.Content.ReadAsByteArrayAsync(), r.Content.Headers.ContentType?.MediaType, r.RequestMessage?.RequestUri ?? url);
    }

    async Task<Respuesta> Pedir(HttpRequestMessage m, string? referer)
    {
        if (referer != null) m.Headers.TryAddWithoutValidation("Referer", Ascii(referer));
        var traza = Traza;
        var enviado = "";
        if (traza != null && m.Content != null)
            try { enviado = Captura.TaparClaves(await m.Content.ReadAsStringAsync()); } catch { }
        using var r = await _http.SendAsync(m);
        var texto = await r.Content.ReadAsStringAsync();
        var res = new Respuesta(r.RequestMessage?.RequestUri ?? m.RequestUri!, (int)r.StatusCode, texto);
        if (traza != null) try { traza(m.Method.Method + " " + m.RequestUri, enviado, res); } catch { }
        return res;
    }

    /// <summary>
    /// Los headers HTTP solo admiten ASCII. Uri.ToString() devuelve la URL "des-escapada" (ej. hpck1.aspx?...,COLLARÍN AYUDÍN,...)
    /// y .NET corta el pedido con "Request headers must contain only ASCII characters". Se re-escapa todo lo que no sea ASCII.
    /// </summary>
    public static string Ascii(string s)
    {
        if (s.All(ch => ch < 128 && ch >= 32)) return s;
        try { if (Uri.TryCreate(s, UriKind.Absolute, out var u)) { var a = u.AbsoluteUri; if (a.All(ch => ch < 128 && ch >= 32)) return a; } } catch { }
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if (ch >= 32 && ch < 128) sb.Append(ch);
            else if (ch >= 128) sb.Append(Uri.EscapeDataString(ch.ToString()));
        }
        return sb.ToString();
    }

    public void Dispose() => _http.Dispose();
}
