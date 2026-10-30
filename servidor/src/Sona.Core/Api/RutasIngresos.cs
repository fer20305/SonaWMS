using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Sona.Api;

public sealed record MiLesReq(string Usuario, string Clave);
public sealed record EtiquetasLesReq(string Codigo, string Prefijo, int Cantidad);
public sealed record PrefijoReq(string? Prefijo);
public sealed record CodigoReq(string Codigo);

/// <summary>API del módulo de Ingreso (/ingresos): analistas y administradores.</summary>
public static class RutasIngresos
{
    static Sesion S(HttpContext c) => (Sesion)c.Items["sesion"]!;
    static string U(HttpContext c)
    {
        c.RequestServices.GetRequiredService<Permisos>().Exigir(S(c).Usuario, "ingreso");
        return S(c).Usuario;
    }

    public static void Mapear(WebApplication app, Ingresos ing)
    {
        // usuario del LES de cada uno (una sola vez; sirve para la PC y el celular)
        app.MapGet("/api/yo/les", (HttpContext c) => Results.Json(ing.MiLes(U(c))));
        app.MapPut("/api/yo/les", async (HttpContext c, MiLesReq b) => Results.Json(await ing.GuardarMiLes(U(c), b.Usuario, b.Clave)));

        app.MapGet("/api/ingresos", async Task<IResult> (HttpContext c) => Results.Json(await ing.Bandeja(U(c))));
        app.MapGet("/api/ingresos/{id:long}", async (HttpContext c, long id) => Results.Json(await ing.Ficha(U(c), id)));
        app.MapPut("/api/ingresos/{id:long}/descarga", (HttpContext c, long id, Ingresos.DescargaReq b) => Results.Json(ing.GuardarDescarga(U(c), id, b)));
        app.MapPost("/api/ingresos/{id:long}/descarga/confirmar", async (HttpContext c, long id) => Results.Json(await ing.ConfirmarDescarga(U(c), id)));

        app.MapPost("/api/ingresos/{id:long}/fotos", async (HttpContext c, long id) =>
        {
            var u = U(c);
            var form = await c.Request.ReadFormAsync();
            var f = form.Files.FirstOrDefault() ?? throw new ErrorUsuario("Falta la foto.");
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            return Results.Json(await ing.Foto(u, id, form["tipo"].ToString(), string.IsNullOrWhiteSpace(form["codigo"]) ? null : form["codigo"].ToString(), ms.ToArray(), f.FileName));
        }).DisableAntiforgery();
        app.MapGet("/api/ingresos/fotos/{fid:long}", (HttpContext c, long fid) => { U(c); var (d, t) = ing.ArchivoFoto(fid); return Results.File(d, t); });
        app.MapGet("/api/ingresos/imagen-les", async Task<IResult> (HttpContext c) =>
        {
            var url = c.Request.Query["url"].ToString();
            if (string.IsNullOrEmpty(url)) return Results.NotFound();
            var r = await ing.ImagenLes(U(c), url);
            return r is { } x ? Results.File(x.Datos, x.Tipo) : Results.NotFound();
        });

        app.MapPut("/api/ingresos/{id:long}/contenedores", (HttpContext c, long id, List<Ingresos.ContenedorReq> b) => Results.Json(ing.GuardarContenedores(U(c), id, b)));
        app.MapPost("/api/ingresos/{id:long}/contenedores/sona", async (HttpContext c, long id, PrefijoReq b) => Results.Json(await ing.GenerarContenedoresSona(U(c), id, b.Prefijo ?? "PP")));
        app.MapPost("/api/ingresos/{id:long}/contenedores/les", async (HttpContext c, long id, EtiquetasLesReq b) => Results.Json(await ing.GenerarEtiquetasLes(U(c), id, b.Codigo, b.Prefijo, b.Cantidad)));

        app.MapPost("/api/ingresos/{id:long}/articulos/verificar", async (HttpContext c, long id) => Results.Json(await ing.VerificarArticulos(U(c), id)));
        app.MapPost("/api/ingresos/{id:long}/articulos/configurar", async (HttpContext c, long id, CodigoReq b) => Results.Json(await ing.ConfigurarArticulo(U(c), id, b.Codigo)));
        app.MapGet("/api/ingresos/{id:long}/proveedores", async (HttpContext c, long id) => Results.Json(await ing.Proveedores(U(c), id)));
        app.MapPost("/api/ingresos/{id:long}/recepcion", async (HttpContext c, long id, Ingresos.RecepcionReq b) => Results.Json(await ing.CrearRecepcion(U(c), id, b)));
        app.MapGet("/api/ingresos/{id:long}/recepcion.pdf", (HttpContext c, long id) =>
        {
            U(c);
            var (d, n) = ing.Comprobante(id);
            c.Response.Headers.ContentDisposition = $"inline; filename=\"{n}\"";
            return Results.File(d, "application/pdf");
        });
        app.MapPost("/api/ingresos/{id:long}/recibir", async (HttpContext c, long id) => Results.Json(await ing.RecibirContenedores(U(c), id)));

        app.MapGet("/api/ingresos/{id:long}/caratulas.pdf", async (HttpContext c, long id) =>
        {
            var d = await ing.CaratulasPdf(U(c), id);
            c.Response.Headers.ContentDisposition = $"inline; filename=\"caratulas_ingreso_{id}.pdf\"";
            return Results.File(d, "application/pdf");
        });
        app.MapGet("/api/ingresos/{id:long}/caratulas/imprimir.html", async (HttpContext c, long id) =>
            Results.Content(PaginaImpresion.HojasA4($"Carátulas del ingreso {id}", await ing.ImagenesCaratulas(U(c), id)), "text/html; charset=utf-8"));
        app.MapPost("/api/ingresos/{id:long}/caratulas/imprimir", async (HttpContext c, long id) => Results.Json(await ing.ImprimirCaratulas(U(c), id)));
        // diagnóstico (solo lectura) + capturas de las operaciones reales, en un zip
        app.MapPost("/api/ingresos/diagnostico", async Task<IResult> (HttpContext c) =>
        {
            var (d, n, copia) = await ing.Diagnostico(U(c));
            c.Response.Headers.ContentDisposition = $"attachment; filename=\"{n}\"";
            if (copia != null) c.Response.Headers["X-Sona-Copia"] = Uri.EscapeDataString(copia);
            return Results.File(d, "application/zip");
        });
        app.MapGet("/api/ingresos/capturas.zip", (HttpContext c) =>
        {
            U(c);
            var (d, n, _) = Captura.Zip(c.RequestServices.GetRequiredService<Configuracion>());
            c.Response.Headers.ContentDisposition = $"attachment; filename=\"{n}\"";
            return Results.File(d, "application/zip");
        });
        app.MapPost("/api/ingresos/{id:long}/cerrar", async (HttpContext c, long id) => Results.Json(await ing.Cerrar(U(c), id)));
    }
}
