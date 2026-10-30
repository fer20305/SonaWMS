using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sona.Les;

/// <summary>
/// Control de bulto: los bultos (medidas) de una solicitud en LES, igual que la página bultos.bultos-ejemplo.com.ar
/// (Api/Bultos/GetBultos y PostGuardarBultos). LES genera las etiquetas de medida al guardar.
/// </summary>
public sealed class BultosLes(Configuracion cfg, HttpClient http)
{
    public sealed record Bulto(long NroContenedor, double Alto, double Ancho, double Profundidad, string? Origen);
    public sealed record Pedido(string NroSolicitud, long? FkPedido, string? Cliente, string? Proyecto, bool Controlado, string? UsuarioControl, string? FechaControl, List<Bulto> Bultos);

    public static readonly Dictionary<string, string> Origenes = new() { ["PUESTO1"] = "PUESTO 1: CD Sona", ["PUESTO2"] = "PUESTO 2: CQ Fondo", ["PUESTO3"] = "PUESTO 3: CQ Frente", ["PUESTO4"] = "PUESTO 4: GAPP" };

    string Base
    {
        get
        {
            var u = new Uri(cfg.Les.Url);
            // la API de bultos de LES va por https (la página oficial la llama así)
            var esquema = u.Host.EndsWith("les-ejemplo.com", StringComparison.OrdinalIgnoreCase) ? "https" : u.Scheme;
            return $"{esquema}://{u.Authority}{u.AbsolutePath.TrimEnd('/')}/Api/Bultos/";
        }
    }

    static JsonNode? Datos(JsonNode? r) => r?["Data"] ?? r;

    public async Task<Pedido> Buscar(string nro)
    {
        using var r = await http.GetAsync(Base + "GetBultos?NroSolicitud=" + Uri.EscapeDataString(nro.Trim()));
        var t = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new ErrorUsuario($"LES respondió {(int)r.StatusCode} al buscar la solicitud {nro}.", 502);
        JsonNode? j;
        try { j = JsonNode.Parse(t); } catch { throw new ErrorUsuario("LES no devolvió los bultos (¿pidió login?).", 502); }
        var d = Datos(j);
        if (d == null || d["NroSolicitud"] == null) throw new ErrorUsuario($"La solicitud {nro} no existe en LES.", 404);
        string? S(string k) => d[k] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
        double N(JsonNode? n) => n is JsonValue v ? (v.TryGetValue<double>(out var x) ? x : double.TryParse(v.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out x) ? x : 0) : 0;
        var bultos = (d["ListBultos"] as JsonArray ?? new()).OfType<JsonObject>().Select(b => new Bulto((long)N(b["NroContenedor"]), N(b["Alto"]), N(b["Ancho"]), N(b["Profundidad"]),
            b["Origen"]?.ToString())).ToList();
        var fecha = S("FechaControl");
        return new Pedido(S("NroSolicitud") ?? nro, d["FkPedido"] is JsonValue fp && fp.TryGetValue<long>(out var f) ? f : null, S("Cliente"), S("Proyecto"),
            d["CheckControlado"] is JsonValue cc && cc.TryGetValue<bool>(out var c) && c, S("UsuarioControl"), fecha is { } fc && !fc.StartsWith("0001") ? fc : null, bultos);
    }

    /// <summary>Número de contenedor para un bulto nuevo, con la misma regla que la página oficial (3.000.000 a 4.999.999 según fecha y hora).</summary>
    public static long NuevoNumero(IEnumerable<long> existentes)
    {
        const long min = 3_000_000, rango = 2_000_000;
        var ya = existentes.ToHashSet();
        var a = DateTime.Now;
        var ts = long.Parse($"{a.Month:00}{a.Day:00}{a.Hour:00}{a.Minute:00}{a.Second:00}{a.Millisecond / 10:00}");
        var n = min + ts % rango;
        while (ya.Contains(n)) n = min + (n - min + Random.Shared.Next(1, 1000)) % rango;
        return n;
    }

    /// <summary>Guarda TODOS los bultos de la solicitud (los que ya estaban y los nuevos), como hace "Generar Etiquetas".</summary>
    public async Task<string> Guardar(string nro, List<Bulto> bultos)
    {
        var cuerpo = new
        {
            NroSolicitud = nro.Trim(),
            ListBultos = bultos.Select(b => new { b.NroContenedor, Alto = Math.Round(b.Alto, 2), Ancho = Math.Round(b.Ancho, 2), Profundidad = Math.Round(b.Profundidad, 2), b.Origen }).ToList(),
        };
        using var r = await http.PostAsJsonAsync(Base + "PostGuardarBultos", cuerpo, new JsonSerializerOptions { PropertyNamingPolicy = null });
        var t = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new ErrorUsuario($"LES respondió {(int)r.StatusCode} al guardar los bultos.", 502);
        var d = Datos(JsonNode.Parse(t));
        var msj = d?["Message"]?.ToString() ?? "";
        if (!msj.Equals("Success", StringComparison.OrdinalIgnoreCase)) throw new ErrorUsuario("LES no guardó los bultos: " + (msj.Length > 0 ? msj : t[..Math.Min(200, t.Length)]), 502);
        return msj;
    }
}
