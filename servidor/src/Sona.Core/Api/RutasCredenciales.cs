using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Sona.Api;

public sealed record CredencialReq(string? Usuario, string? Clave);

/// <summary>Mis credenciales: cada usuario carga las suyas (solo las propias; nunca se devuelven las claves).</summary>
public static class RutasCredenciales
{
    static Sesion S(HttpContext c) => (Sesion)c.Items["sesion"]!;

    public static void Mapear(WebApplication app, MisCredenciales cred)
    {
        app.MapGet("/api/yo/credenciales", (HttpContext c) => Results.Json(cred.Listar(S(c).Usuario)));
        app.MapPut("/api/yo/credenciales/{sistema}", async (HttpContext c, string sistema, CredencialReq b) =>
            Results.Json(await cred.Guardar(S(c).Usuario, sistema, b.Usuario ?? "", b.Clave ?? "")));
        app.MapDelete("/api/yo/credenciales/{sistema}", (HttpContext c, string sistema) => Results.Json(cred.Borrar(S(c).Usuario, sistema)));
        app.MapPost("/api/yo/credenciales/{sistema}/probar", async (HttpContext c, string sistema) => Results.Json(await cred.ProbarGuardadas(S(c).Usuario, sistema)));
    }
}
