using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Sona.Wms;

namespace Sona.Api;

public sealed record AsignarReq(int Empresa, string Tipo, string Op, string Picker, bool? Imprimir, string? Prioridad = null);
public sealed record PrioridadReq(int Empresa, string Tipo, string Op, string Prioridad);
public sealed record ElegirOpReq(int Empresa, string? Tipo, string Op, string? Modo);
public sealed record OpReq(int Empresa, string Tipo, string Op);

/// <summary>API del panel de producción (/produccion) y la lista "Mis OP" de la app del picker.</summary>
public static class RutasProduccion
{
    static Sesion S(HttpContext c) => (Sesion)c.Items["sesion"]!;
    static void Ver(HttpContext c) => c.RequestServices.GetRequiredService<Permisos>().Exigir(S(c).Usuario, "produccion_ver");
    static void Asignan(HttpContext c) => c.RequestServices.GetRequiredService<Permisos>().Exigir(S(c).Usuario, "produccion_asignar");
    static string? Q(HttpContext c, string k) => c.Request.Query.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : null;

    static async Task<IResult> Wms(Func<Task<object>> fn)
    {
        try { return Results.Json(await fn()); }
        catch (WmsError e) { throw new ErrorUsuario("WMS: " + e.Message, 502); }
    }

    public static void Mapear(WebApplication app, Configuracion cfg, Produccion prod)
    {
        app.MapGet("/api/produccion/tablero", async (HttpContext c) => { Ver(c); return await Wms(() => prod.Tablero(S(c).Usuario, Q(c, "refrescar") == "1")); });

        app.MapPost("/api/produccion/asignar", async (HttpContext c, AsignarReq b) =>
        { Asignan(c); return await Wms(async () => await prod.Asignar(S(c).Usuario, b.Empresa, b.Tipo, b.Op, b.Picker, b.Imprimir, b.Prioridad)); });

        app.MapPost("/api/produccion/prioridad", async (HttpContext c, PrioridadReq b) =>
        { Asignan(c); return await Wms(() => prod.CambiarPrioridad(S(c).Usuario, b.Empresa, b.Tipo, b.Op, b.Prioridad)); });

        app.MapPost("/api/produccion/reasignar", async (HttpContext c, AsignarReq b) =>
        { Asignan(c); return await Wms(async () => await prod.Reasignar(S(c).Usuario, b.Empresa, b.Tipo, b.Op, b.Picker, b.Imprimir ?? false)); });

        app.MapPost("/api/produccion/reimprimir", async (HttpContext c, OpReq b) =>
        { Asignan(c); return await Wms(async () => await prod.Reimprimir(S(c).Usuario, b.Empresa, b.Tipo, b.Op)); });

        // el PDF se abre en otra pestaña: el token viaja en la URL (?token=)
        app.MapGet("/api/produccion/caratula", async (HttpContext c) =>
        {
            Ver(c);
            try
            {
                var (datos, nombre) = await prod.Pdf(S(c).Usuario, int.Parse(Q(c, "empresa") ?? "0"), Q(c, "tipo") ?? "", Q(c, "op") ?? "", Q(c, "original") == "1");
                c.Response.Headers.ContentDisposition = $"inline; filename=\"{nombre}\"";
                return Results.File(datos, "application/pdf");
            }
            catch (WmsError e) { throw new ErrorUsuario("WMS: " + e.Message, 502); }
        });

        // imprimir desde ESTA computadora (diálogo del navegador): ops = "empresa|tipo|op,empresa|tipo|op"
        app.MapGet("/api/produccion/caratulas/imprimir.html", async (HttpContext c) =>
        {
            Asignan(c);
            var ops = (Q(c, "ops") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Split('|')).Where(x => x.Length == 3 && int.TryParse(x[0], out _)).Select(x => (int.Parse(x[0]), x[1], x[2])).Take(60).ToList();
            if (ops.Count == 0) throw new ErrorUsuario("Faltan las OPs a imprimir.");
            var (paginas, avisos) = await prod.ImagenesParaImprimir(S(c).Usuario, ops);
            var titulo = ops.Count == 1 ? $"Carátula OP {ops[0].Item3}" : $"Carátulas de {ops.Count} OPs";
            return Results.Content(PaginaImpresion.HojasA4(titulo, paginas, avisos), "text/html; charset=utf-8");
        });

        app.MapGet("/api/produccion/historial", (HttpContext c) =>
        {
            Ver(c);
            var desde = DateTime.TryParse(Q(c, "desde"), out var d) ? d.Date : DateTime.Today;
            var hasta = DateTime.TryParse(Q(c, "hasta"), out var h) ? h.Date : desde;
            return Results.Json(prod.Historial(desde, hasta));
        });

        // flujo de documentos (como en SAP): OP, solicitud, remito o bulto → toda la cadena del pedido
        app.MapGet("/api/produccion/flujo", async (HttpContext c) =>
        {
            Ver(c);
            return Results.Json(await c.RequestServices.GetRequiredService<FlujoDocumentos>().Armar(S(c).Usuario, Q(c, "q") ?? ""));
        });
        app.MapGet("/api/produccion/config", (HttpContext c) => { Ver(c); return Results.Json(prod.Config()); });
        app.MapPut("/api/produccion/config", async (HttpContext c) =>
        {
            Asignan(c);
            var b = await JsonNode.ParseAsync(c.Request.Body) ?? throw new ErrorUsuario("Cuerpo vacío.");
            // el usuario y la clave del WMS los cambian analistas y administradores; la impresora, cualquiera que asigne
            if ((b["usuario"] != null || b["clave"] != null) && S(c).Rol is not ("analista" or "admin"))
                throw new ErrorUsuario("El usuario del WMS del panel lo cambian analistas y administradores.", 403);
            ConfigEditor.AplicarProduccion(cfg, b);
            cfg.Guardar();
            if (b["usuario"] != null || b["clave"] != null) prod.Olvidar();
            sv(c).Db.Auditar("config", "produccion", new { impresora = cfg.Produccion.Impresora, cfg.Produccion.ImprimirAlAsignar, cfg.Produccion.Copias, usuario = cfg.Produccion.Usuario }, S(c).Usuario);
            return Results.Json(prod.Config());
        });
        app.MapPost("/api/produccion/probar-impresora", (HttpContext c) =>
        {
            Asignan(c);
            prod.ProbarImpresora();
            return Results.Json(new { ok = true });
        });

        // app del picker: las OPs que tiene asignadas en el WMS, con su avance
        // el picker elige una OP de su lista: SONA la pone primera en el WMS y arranca el picking
        app.MapPost("/api/picking/elegir", async (HttpContext c, ElegirOpReq b) =>
        {
            c.RequestServices.GetRequiredService<Permisos>().Exigir(S(c).Usuario, "picking");
            var u = S(c).Usuario;
            var (ok, aviso, o) = await prod.PonerPrimera(u, b.Op, b.Tipo);
            var picking = c.RequestServices.GetRequiredService<Picking>();
            var nota = ok ? $"Elegiste la OP {b.Op}." : $"No se pudo poner primera la OP {b.Op} ({aviso}). El WMS te da la que corresponde según su orden.";
            try { return Results.Json(await picking.Iniciar(u, o?.Empresa ?? b.Empresa, b.Modo, nota)); }
            catch (WmsError e) { throw new ErrorUsuario("WMS: " + e.Message, 502); }
        });
        app.MapGet("/api/picking/mis-ops", async (HttpContext c) => { var r = await prod.MisOps(S(c).Usuario); return Results.Json(r); });
    }

    static Servicios sv(HttpContext c) => (Servicios)c.RequestServices.GetService(typeof(Servicios))!;
}
