using System.Diagnostics;
using System.Text.Json.Nodes;
using Sona.Wms;

namespace Sona.Api;

/// <summary>Edición de la configuración de plataformas y prueba de conexión a cada una.</summary>
public static class ConfigEditor
{
    static string? T(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static void Aplicar(Configuracion cfg, JsonNode b)
    {
        if (b["puerto"] is JsonValue p && p.TryGetValue<int>(out var puerto) && puerto is > 0 and < 65536) cfg.Puerto = puerto;
        if (b["sincronizarLayoutHoras"] is JsonValue h && h.TryGetValue<int>(out var horas)) cfg.SincronizarLayoutHoras = Math.Max(0, horas);
        if (b["admins"] is JsonArray adm) cfg.Admins = adm.Select(x => x!.GetValue<string>().Trim().ToUpperInvariant()).Where(x => x.Length > 0).ToList();
        if (b["wms"] is JsonObject w)
        {
            if (T(w, "local") is { } l) cfg.Wms.Local = Barra(l);
            if (T(w, "externo") is { } e) cfg.Wms.Externo = Barra(e);
            if (w["timeoutSegundos"] is JsonValue ts && ts.TryGetValue<int>(out var t)) cfg.Wms.TimeoutSegundos = Math.Clamp(t, 5, 120);
        }
        if (b["les"] is JsonObject les)
        {
            if (T(les, "url") is { } u) cfg.Les.Url = Barra(u);
            if (T(les, "account") is { } a) cfg.Les.Account = Barra(a);
            if (T(les, "buscador") is { } bu) cfg.Les.Buscador = bu.Trim().TrimEnd('/');
            if (T(les, "usuario") is { } us) cfg.Les.Usuario = us.Trim();
            if (T(les, "clave") is { Length: > 0 } cl) cfg.PonerClaveLes(cl);
        }
        if (b["etms"] is JsonObject et)
        {
            if (T(et, "url") is { Length: > 0 } eu) cfg.Etms.Url = Barra(eu);
            if (T(et, "usuario") is { } eus) cfg.Etms.Usuario = eus.Trim();
            if (T(et, "clave") is { Length: > 0 } ecl) cfg.PonerClaveEtms(ecl);
        }
        if (b["produccion"] is JsonObject pr) AplicarProduccion(cfg, pr);
        if (b["servidor"] is JsonObject s)
        {
            if (T(s, "direccionLocal") is { } dl) cfg.Servidor.DireccionLocal = dl.Trim().TrimEnd('/');
            if (T(s, "direccionExterna") is { } de) cfg.Servidor.DireccionExterna = de.Trim().TrimEnd('/');
        }
    }

    /// <summary>Usuario del WMS, impresora y copias del panel de producción.</summary>
    public static void AplicarProduccion(Configuracion cfg, JsonNode pr)
    {
        if (T(pr, "usuario") is { } pu) { cfg.Produccion.Usuario = pu.Trim().ToUpperInvariant(); if (pu.Trim().Length == 0) cfg.Produccion.Clave = ""; }
        if (T(pr, "clave") is { Length: > 0 } pc) cfg.PonerClaveProduccion(pc);
        if (T(pr, "impresora") is { } imp) cfg.Produccion.Impresora = imp.Trim();
        if (pr["imprimirAlAsignar"] is JsonValue ia && ia.TryGetValue<bool>(out var iab)) cfg.Produccion.ImprimirAlAsignar = iab;
        if (pr["copias"] is JsonValue cp && cp.TryGetValue<int>(out var c)) cfg.Produccion.Copias = Math.Clamp(c, 1, 5);
        if (pr["prioridadMayorPrimero"] is JsonValue pm && pm.TryGetValue<bool>(out var pmb)) cfg.Produccion.PrioridadMayorPrimero = pmb;
    }

    static string Barra(string u) { u = u.Trim(); return u.Length == 0 || u.EndsWith('/') ? u : u + "/"; }

    public sealed record Prueba(string Plataforma, string Direccion, bool Ok, string Detalle, long Ms);

    /// <summary>Prueba cada plataforma configurada: WMS local, WMS externo, buscador SONA y tradebox.</summary>
    public static async Task<List<Prueba>> Probar(Configuracion cfg, Servicios sv)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        async Task<Prueba> Url(string nombre, string url, Func<string, bool>? valida = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return new Prueba(nombre, url, false, "Sin configurar", 0);
            var sw = Stopwatch.StartNew();
            try
            {
                using var r = await http.GetAsync(url);
                var t = await r.Content.ReadAsStringAsync();
                var ok = (int)r.StatusCode < 500 && (valida == null || valida(t));
                return new Prueba(nombre, url, ok, $"HTTP {(int)r.StatusCode}" + (ok ? "" : " (respuesta inesperada)"), sw.ElapsedMilliseconds);
            }
            catch (Exception e) { return new Prueba(nombre, url, false, e.Message, sw.ElapsedMilliseconds); }
        }
        var lista = await Task.WhenAll(
            Url("WMS (red local)", (cfg.Wms.Local.EndsWith('/') ? cfg.Wms.Local : cfg.Wms.Local + "/") + "hdis.aspx", Gx.EsLogin),
            Url("WMS (IP externa)", (cfg.Wms.Externo.EndsWith('/') ? cfg.Wms.Externo : cfg.Wms.Externo + "/") + "hdis.aspx", Gx.EsLogin),
            Url("Buscador SONA", cfg.Les.Buscador.TrimEnd('/') + "/GetInfoBusqueda?storeProcedure=BOT_BusquedaContenedor&valor=PRUEBA"),
            Url("LES tradebox", cfg.Les.Url),
            Url("Cuentas tradebox", cfg.Les.Account.TrimEnd('/') + "/Account/LogOn"),
            Url("e-TMS", (cfg.Etms.Url.EndsWith('/') ? cfg.Etms.Url : cfg.Etms.Url + "/") + "Login.aspx"));
        var res = lista.ToList();
        if (!string.IsNullOrEmpty(cfg.Les.Usuario))
        {
            var sw = Stopwatch.StartNew();
            try { sv.Les.Olvidar(); await sv.Les.Fotos("PRUEBA-SONA"); res.Add(new Prueba("Usuario tradebox (fotos)", cfg.Les.Usuario, true, "Ingreso correcto", sw.ElapsedMilliseconds)); }
            catch (Exception e) { res.Add(new Prueba("Usuario tradebox (fotos)", cfg.Les.Usuario, false, e.Message, sw.ElapsedMilliseconds)); }
        }
        return res;
    }
}
