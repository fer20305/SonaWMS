using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sona.Wms;

namespace Sona.Les;

/// <summary>
/// Ingresos pendientes de LES (les.les-ejemplo.com/IngresoPendiente), capturado el 19/10:
///   POST Login (Username, Password) → 302 Index
///   POST ImprimirEtiquetaRapida (codigo, cantidad, prefijo PP/PC/Bulto) →
///   {"success":true,"etiquetas":[{"Codigo":"AXION2116","Cliente":"Piquillín Dulces","Descripcion":"...","NroContenedor":"PP-201350"}],"warning":null}
/// SONA solo usa el número de contenedor que genera LES (la etiqueta se imprime en la terminal).
/// </summary>
public sealed class IngresosLes(Configuracion cfg)
{
    public sealed record Etiqueta(string Contenedor, string Articulo, string? Descripcion, string? Cliente, string? Aviso);

    readonly ConcurrentDictionary<string, (Navegador Nav, SemaphoreSlim Uso, bool Logueado)> _ses = new();

    string Base
    {
        get
        {
            var u = new Uri(cfg.Les.Url);
            var esquema = u.Host.EndsWith("les-ejemplo.com", StringComparison.OrdinalIgnoreCase) ? "https" : u.Scheme;
            return $"{esquema}://{u.Authority}{u.AbsolutePath.TrimEnd('/')}/IngresoPendiente/";
        }
    }

    static bool EsLogin(Respuesta r) => r.Url.AbsolutePath.Contains("Login", StringComparison.OrdinalIgnoreCase) ||
        (Regex.IsMatch(r.Texto, @"name=""?Username""?", RegexOptions.IgnoreCase) && Regex.IsMatch(r.Texto, @"name=""?Password""?", RegexOptions.IgnoreCase));

    /// <summary>
    /// Crea un contenedor nuevo en LES para el artículo y devuelve su número. Se entra con el usuario del WMS del operario
    /// (así lo hace el depósito); si no lo acepta, con el usuario de tradebox de la configuración.
    /// </summary>
    public async Task<Etiqueta> CrearContenedor(string usuario, string clave, string articulo, string prefijo)
    {
        var p = (prefijo ?? "PP").Trim().ToUpperInvariant() switch { "PC" => "PC", "BULTO" => "Bulto", _ => "PP" };
        var s = _ses.GetOrAdd(usuario.ToUpperInvariant(), _ => (new Navegador(30), new SemaphoreSlim(1, 1), false));
        await s.Uso.WaitAsync();
        try
        {
            for (var intento = 0; intento < 2; intento++)
            {
                if (!s.Logueado || intento > 0)
                {
                    await Login(s.Nav, usuario, clave);
                    _ses[usuario.ToUpperInvariant()] = s = (s.Nav, s.Uso, true);
                }
                var r = await s.Nav.Post(new Uri(Base + "ImprimirEtiquetaRapida"),
                    new() { ["codigo"] = articulo.Trim().ToUpperInvariant(), ["cantidad"] = "1", ["prefijo"] = p }, Base + "Index", ajax: true);
                if (EsLogin(r)) { s = (s.Nav, s.Uso, false); continue; }
                JsonNode? j;
                try { j = JsonNode.Parse(r.Texto); } catch { throw new ErrorUsuario($"LES no devolvió el contenedor (respuesta {r.Estado}).", 502); }
                if (j?["success"]?.GetValue<bool>() != true)
                    throw new ErrorUsuario("LES no creó el contenedor: " + (j?["message"]?.ToString() ?? "sin detalle"), 502);
                var e = j["etiquetas"]?.AsArray().FirstOrDefault() ?? throw new ErrorUsuario("LES no devolvió la etiqueta del contenedor.", 502);
                var cont = e["NroContenedor"]?.ToString();
                if (string.IsNullOrWhiteSpace(cont)) throw new ErrorUsuario("LES no devolvió el número de contenedor.", 502);
                return new Etiqueta(cont.Trim().ToUpperInvariant(), e["Codigo"]?.ToString() ?? articulo, e["Descripcion"]?.ToString(), e["Cliente"]?.ToString(),
                    j["warning"] is JsonValue w ? w.ToString() : null);
            }
            throw new ErrorUsuario("LES (Ingresos pendientes) no aceptó el ingreso.", 502);
        }
        finally { s.Uso.Release(); }
    }

    async Task Login(Navegador nav, string usuario, string clave)
    {
        var intentos = new List<(string U, string C)> { (usuario, clave) };
        if (!string.IsNullOrEmpty(cfg.Les.Usuario) && !string.IsNullOrEmpty(cfg.ClaveLes) && !cfg.Les.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase))
            intentos.Add((cfg.Les.Usuario, cfg.ClaveLes));
        foreach (var (u, c) in intentos)
        {
            nav.Vaciar();
            await nav.Get(Base + "Index");
            var r = await nav.Post(new Uri(Base + "Login"), new() { ["Username"] = u.ToUpperInvariant(), ["Password"] = c }, Base + "Index");
            if (!EsLogin(r)) return;
        }
        throw new ErrorUsuario("LES (Ingresos pendientes) no aceptó el usuario y la clave.", 502);
    }
}
