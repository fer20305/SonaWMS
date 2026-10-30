using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sona.Panel;

public sealed class ErrorApi(string mensaje, int codigo) : Exception(mensaje) { public int Codigo { get; } = codigo; }

/// <summary>
/// El panel habla con el servidor por la misma API que los celulares (sea el servidor integrado en esta PC o uno en Docker).
/// Si la sesión vence, vuelve a ingresar solo con el usuario guardado.
/// </summary>
public sealed class ApiCliente
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    public string Base { get; private set; } = "";
    public string Token { get; private set; } = "";
    public string Usuario { get; private set; } = "";
    public string Rol { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    /// <summary>Permisos del usuario (ZUSR). null = servidor viejo sin permisos: no se oculta nada.</summary>
    public HashSet<string>? Permisos { get; private set; }
    public bool Puede(string permiso) => Rol == "admin" || Permisos == null || Permisos.Contains(permiso);
    string _clave = "";

    public event Action<JsonElement>? Evento;
    public event Action<bool>? Conexion;
    public bool EnVivo { get; private set; }
    CancellationTokenSource? _ws;

    public void Servidor(string url) => Base = url.TrimEnd('/');

    public async Task Ingresar(string usuario, string clave)
    {
        var r = await _http.PostAsJsonAsync(Base + "/api/login", new { usuario, clave, panel = true });
        var j = await Leer(r);
        Token = j!["token"]!.GetValue<string>();
        Usuario = j["usuario"]!["usuario"]!.GetValue<string>();
        Rol = j["usuario"]!["rol"]!.GetValue<string>();
        Nombre = j["usuario"]!["nombre"]?.GetValue<string>() ?? "";
        Permisos = (j["permisos"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _clave = clave;
    }

    static async Task<JsonNode?> Leer(HttpResponseMessage r)
    {
        var t = await r.Content.ReadAsStringAsync();
        JsonNode? j = null;
        try { j = t.Length > 0 ? JsonNode.Parse(t) : null; } catch { }
        if (!r.IsSuccessStatusCode) throw new ErrorApi(j?["error"]?.GetValue<string>() ?? $"Error {(int)r.StatusCode}", (int)r.StatusCode);
        return j;
    }

    async Task<JsonNode?> Enviar(HttpMethod metodo, string ruta, object? cuerpo, bool reintento = true)
    {
        using var m = new HttpRequestMessage(metodo, Base + ruta);
        m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        if (cuerpo != null) m.Content = new StringContent(cuerpo is JsonNode n ? n.ToJsonString() : JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
        HttpResponseMessage r;
        try { r = await _http.SendAsync(m); }
        catch (HttpRequestException e) { throw new ErrorApi("Sin conexión con el servidor: " + e.Message, -1); }
        if ((int)r.StatusCode == 401 && reintento && _clave.Length > 0)
        {
            try { await Ingresar(Usuario, _clave); } catch { throw new ErrorApi("Sesión vencida", 401); }
            return await Enviar(metodo, ruta, cuerpo, false);
        }
        return await Leer(r);
    }

    public Task<JsonNode?> Get(string ruta) => Enviar(HttpMethod.Get, ruta, null);
    public Task<JsonNode?> Post(string ruta, object? cuerpo = null) => Enviar(HttpMethod.Post, ruta, cuerpo ?? new { });
    public Task<JsonNode?> Put(string ruta, object cuerpo) => Enviar(HttpMethod.Put, ruta, cuerpo);
    public Task<JsonNode?> Delete(string ruta) => Enviar(HttpMethod.Delete, ruta, null);

    public async Task<byte[]> Bytes(string ruta)
    {
        using var m = new HttpRequestMessage(HttpMethod.Get, ruta.StartsWith("http") ? ruta : Base + ruta);
        m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        using var r = await _http.SendAsync(m);
        if (!r.IsSuccessStatusCode) throw new ErrorApi($"Error {(int)r.StatusCode}", (int)r.StatusCode);
        return await r.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Sube archivos (multipart) a una ruta de la API.</summary>
    public async Task<JsonNode?> Subir(string ruta, IEnumerable<string> archivos)
    {
        using var form = new MultipartFormDataContent();
        foreach (var a in archivos)
        {
            var c = new ByteArrayContent(await System.IO.File.ReadAllBytesAsync(a));
            c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(a.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
            form.Add(c, "foto", System.IO.Path.GetFileName(a));
        }
        using var m = new HttpRequestMessage(HttpMethod.Post, Base + ruta) { Content = form };
        m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        using var r = await _http.SendAsync(m);
        return await Leer(r);
    }

    /// <summary>Sube un archivo grande con campos de formulario (ej. el APK de la app con su versión y notas).</summary>
    public async Task<JsonNode?> SubirArchivo(string ruta, string archivo, string campo, Dictionary<string, string> campos)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (k, v) in campos) form.Add(new StringContent(v), k);
        var c = new StreamContent(System.IO.File.OpenRead(archivo));
        c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.android.package-archive");
        form.Add(c, campo, System.IO.Path.GetFileName(archivo));
        using var m = new HttpRequestMessage(HttpMethod.Post, Base + ruta) { Content = form };
        m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var r = await _http.SendAsync(m, cts.Token);
        return await Leer(r);
    }

    // ------------------------------------------------------------------ eventos en vivo (WebSocket)
    public void Conectar()
    {
        _ws?.Cancel();
        _ws = new CancellationTokenSource();
        var ct = _ws.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    var url = Base.Replace("http://", "ws://").Replace("https://", "wss://") + "/api/eventos?token=" + Uri.EscapeDataString(Token);
                    await ws.ConnectAsync(new Uri(url), ct);
                    EnVivo = true; Conexion?.Invoke(true);
                    var buf = new byte[64 * 1024];
                    var sb = new StringBuilder();
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var r = await ws.ReceiveAsync(buf, ct);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                        if (!r.EndOfMessage) continue;
                        try { using var d = JsonDocument.Parse(sb.ToString()); Evento?.Invoke(d.RootElement.Clone()); } catch { }
                        sb.Clear();
                    }
                }
                catch { }
                EnVivo = false; Conexion?.Invoke(false);
                try { await Task.Delay(3000, ct); await Get("/api/yo"); } catch { }
            }
        }, ct);
    }

    public void Desconectar() { _ws?.Cancel(); Token = ""; _clave = ""; }
}
