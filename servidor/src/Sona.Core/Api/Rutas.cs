using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sona.Dominio;
using Sona.Wms;

namespace Sona.Api;

// ------------------------------------------------------------------ cuerpos de las peticiones
public sealed record LoginReq(string Usuario, string Clave, string? Dispositivo, string? Modelo, string? Version, bool? Panel, bool? Web = null);
public sealed record CargaReq(List<string> Contenedores, string? Origen, int? Cuantas, string? Destino, int? Nivel, bool? Multi, bool? Forzar = null);
public sealed record MovimientosReq(List<MovimientoEntrada> Movimientos);
public sealed record PermisosUsuarioReq(List<string>? Perfiles, List<string>? Mas, List<string>? Menos);
public sealed record PerfilReq(string Nombre, string? Descripcion, List<string>? Permisos);
public sealed record MotivoReq(string Motivo);
public sealed record ResolverReq(bool Aprobar, string? Nota);
public sealed record RangoReq(string Cara, int Desde, int Hasta, List<int>? Niveles);
public sealed record BultoNuevo(double Alto, double Ancho, double Profundidad);
public sealed record BultoCambio(long NroContenedor, double Alto, double Ancho, double Profundidad);
public sealed record BultosReq(List<BultoNuevo>? Nuevos, string? Origen, List<long>? Quitar, List<BultoCambio>? Cambios = null, bool? Imprimir = null);
public sealed record ImprimirBultosReq(List<long>? Bultos, string? Origen);
public sealed record ImprimirEtiquetaReq(string? Formato, int? Copias, string? Impresora);
public sealed record EtiquetasConfigReq(string? ImpresoraZebra, string? ImpresoraA4, int? AnchoMm, int? AltoMm);
public sealed record ImpresionBultosReq(bool? ImprimirAlGuardar, Dictionary<string, string>? Impresoras, string? Impresora, int? Copias, bool? SoloNuevos, int? AnchoMm, int? AltoMm);
public sealed record PickIniciarReq(int Empresa = 0, string? Modo = null);
public sealed record ReconteoReq(List<long>? Lineas, string? Tipo, string? Asignado_a, string? Modo);
public sealed record ContenedoresReq(List<string>? Contenedores);
public sealed record CrearContenedorReq(string? Articulo, double? Cantidad, string? Posicion, int? Nivel, string? Prefijo, string? Contenedor);
public sealed record TareaReq(string Nombre, List<string>? Posiciones, string? Asignado_a, RangoReq? Rango, string? Modo, string? Articulo);
public sealed record ArticuloConteoReq(string Articulo, string? Modo);
public sealed record LineasReq(long? Tarea_id, string Posicion, List<string>? Esperados, List<LineaConteo> Lineas);
public sealed record RevisionReq(string Revision);
public sealed record ZonaReq(string Tipo, int? Empresa, string Rol_zona, string Mezcla);
public sealed record EmpresaReq(string Nombre, List<string>? Alias);
public sealed record UsuarioReq(string? Nombre, string Rol, bool? Activo);
public sealed record CarasReq(List<string>? Caras);
public sealed record MensajeReq(string? Para, string? Texto);
public sealed record LeidoReq(string? Con);
public sealed record IaConfirmarReq(string FotoId, string? Articulo);
public sealed record IaIndexarReq(bool? Catalogo, bool? Rehacer, bool? Detener);

public static class Rutas
{
    public const string Version = "2.20.1";

    static Sesion S(HttpContext c) => (Sesion)c.Items["sesion"]!;
    static bool EsPanel(HttpContext c) => Seguridad.RolesPanel.Contains(S(c).Rol);
    static void SoloPanel(HttpContext c) { if (!EsPanel(c) && !Tiene(c, "panel")) throw new ErrorUsuario("Solo analistas y administradores.", 403); }
    /// <summary>Ver (no cambiar): supervisores, analistas y administradores.</summary>
    static bool PuedeVer(HttpContext c) => Seguridad.RolesLectura.Contains(S(c).Rol) || Tiene(c, "panel") || Tiene(c, "supervisor_web");
    static void SoloLectura(HttpContext c) { if (!PuedeVer(c)) throw new ErrorUsuario("Solo supervisores, analistas y administradores.", 403); }
    static void SoloAdmin(HttpContext c) { if (S(c).Rol != "admin") throw new ErrorUsuario("Solo administradores.", 403); }
    static Permisos Perm(HttpContext c) => c.RequestServices.GetRequiredService<Permisos>();
    /// <summary>Exige un permiso del usuario (ZUSR › permisos).</summary>
    static void Exigir(HttpContext c, string permiso) => Perm(c).Exigir(S(c).Usuario, permiso);
    static bool Tiene(HttpContext c, string permiso) => Perm(c).Puede(S(c).Usuario, permiso);
    static int? Num(string? s) => int.TryParse(s, out var n) ? n : null;
    static string? Q(HttpContext c, string k) => c.Request.Query.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : null;

    static readonly string[] Libres = { "/api/login", "/api/estado", "/api/app/version" };

    /// <summary>Autenticación y manejo de errores para todo /api.</summary>
    public static void Middleware(WebApplication app, Configuracion cfg, Db db)
    {
        app.Use(async (ctx, next) =>
        {
            var ruta = ctx.Request.Path.Value ?? "";
            try
            {
                if (ruta.StartsWith("/api/") && !Libres.Contains(ruta) && !ruta.StartsWith("/api/imagen") && !ruta.StartsWith("/api/fotos/"))
                {
                    var h = ctx.Request.Headers.Authorization.ToString();
                    var t = h.StartsWith("Bearer ") ? h[7..] : ctx.Request.Query["token"].ToString();
                    var s = string.IsNullOrEmpty(t) ? null : Seguridad.Verificar(t, cfg.Secreto);
                    if (s == null) { ctx.Response.StatusCode = 401; await ctx.Response.WriteAsJsonAsync(new { error = "Sesión vencida: ingresá de nuevo." }); return; }
                    var u = db.Q1("SELECT rol, activo FROM usuarios WHERE usuario=$1", s.Usuario);
                    if (u == null || !u.B("activo")) { ctx.Response.StatusCode = 401; await ctx.Response.WriteAsJsonAsync(new { error = "Usuario inactivo." }); return; }
                    ctx.Items["sesion"] = s with { Rol = u.S("rol")! };   // el rol vale el de la base
                    // el supervisor solo mira (panel web): no puede registrar ni cambiar nada
                    if (u.S("rol") == "supervisor" && !HttpMethods.IsGet(ctx.Request.Method) && !ruta.StartsWith("/api/mensajes"))
                    { ctx.Response.StatusCode = 403; await ctx.Response.WriteAsJsonAsync(new { error = "El usuario supervisor solo puede consultar." }); return; }
                }
                await next();
            }
            catch (Exception e) when (!ctx.Response.HasStarted)
            {
                var (codigo, mensaje) = e switch
                {
                    ErrorUsuario eu => (eu.Codigo, eu.Message),
                    WmsError => (502, e.Message),
                    ArgumentException or JsonException or BadHttpRequestException => (400, e.Message),
                    _ => (500, e.Message),
                };
                if (codigo >= 500) app.Logger.LogError(e, "Error en {Ruta}", ruta);
                ctx.Response.StatusCode = codigo;
                await ctx.Response.WriteAsJsonAsync(new { error = mensaje });
            }
        });
    }

    public static void Mapear(WebApplication app, Configuracion cfg, Db db, Servicios sv, Movimientos mov, Conteo conteo, SincronizadorLayout layout, Respaldo respaldo, Eventos ev, Mensajes mensajes, Reconocedor ia, Picking picking)
    {
        var carpetaFotos = Path.Combine(cfg.Carpeta, "fotos");
        Directory.CreateDirectory(carpetaFotos);

        // ================================================================ sesión
        app.MapPost("/api/login", async (LoginReq b) =>
        {
            if (string.IsNullOrWhiteSpace(b.Usuario) || string.IsNullOrEmpty(b.Clave)) throw new ErrorUsuario("Usuario y clave obligatorios.");
            var usuario = b.Usuario.Trim().ToUpperInvariant();
            var existente = db.Q1("SELECT * FROM usuarios WHERE usuario=$1", usuario);
            if (existente != null && !existente.B("activo")) throw new ErrorUsuario("Usuario inactivo en SONA Depósito.", 403);
            var s = sv.NuevaSesion(usuario, b.Clave);
            var sinWms = false;
            try { await s.Login(); }
            catch (WmsError e) when (!e.Message.Contains("llegar"))
            { throw new ErrorUsuario(e.Message, 401); }
            catch (Exception e)
            {
                // WMS caído: si ya ingresó antes con esa clave, entra igual
                var guardada = existente?.S("clave_cifrada");
                if (guardada != null && Seguridad.DescifrarSiHace(guardada, cfg.Secreto) == b.Clave) sinWms = true;
                else throw new ErrorUsuario("No se pudo validar con el WMS: " + e.Message, 503);
            }
            // los administradores de la configuración (ZCFG › admins) siempre entran como administrador, aunque antes hayan ingresado con otro rol
            var esAdminCfg = cfg.Admins.Any(a => a.Equals(usuario, StringComparison.OrdinalIgnoreCase));
            var rol = esAdminCfg ? "admin" : existente?.S("rol") ?? "maquinista";
            if (esAdminCfg && existente != null && existente.S("rol") != "admin") db.Ejecutar("UPDATE usuarios SET rol='admin' WHERE usuario=$1", usuario);
            // base sin ningún administrador que haya entrado (instalación nueva o base cambiada): el primero que entra al panel queda como administrador
            if (rol != "admin" && b.Panel == true
                && db.Q1("SELECT usuario FROM usuarios WHERE rol='admin' AND activo=1 AND clave_cifrada IS NOT NULL AND usuario<>$1 LIMIT 1", usuario) == null)
            {
                rol = "admin";
                if (existente != null) db.Ejecutar("UPDATE usuarios SET rol='admin', perfiles=NULL WHERE usuario=$1", usuario);
                db.Auditar("usuarios", usuario, new { rol = "admin", motivo = "primer administrador (no había ninguno)" }, usuario);
            }
            db.Ejecutar("""
                INSERT INTO usuarios (usuario, rol, clave_cifrada, creado, ultimo_acceso) VALUES ($1,$2,$3,$4,$4)
                ON CONFLICT(usuario) DO UPDATE SET clave_cifrada=excluded.clave_cifrada, ultimo_acceso=excluded.ultimo_acceso
                """, usuario, rol, Seguridad.Cifrar(b.Clave, cfg.Secreto), Db.Ahora());
            if (!sinWms) sv.RegistrarSesion(s); else sv.OlvidarSesion(usuario);
            var permisos = app.Services.GetRequiredService<Permisos>();
            permisos.Olvidar(usuario);
            var mios = permisos.De(usuario);
            if (b.Panel == true && !mios.Contains("panel")) throw new ErrorUsuario("Tu usuario no tiene permiso para entrar al panel. Pedíselo a un administrador (ZUSR › permisos).", 403);
            if (b.Web == true && !new[] { "supervisor_web", "produccion_ver", "ingreso" }.Any(mios.Contains)) throw new ErrorUsuario("Tu usuario no tiene permiso para los paneles web (pedíselo a un administrador en ZUSR).", 403);
            if (!string.IsNullOrEmpty(b.Dispositivo))
                db.Ejecutar("""
                    INSERT INTO dispositivos (id, usuario, modelo, version_app, ultimo_contacto) VALUES ($1,$2,$3,$4,$5)
                    ON CONFLICT(id) DO UPDATE SET usuario=excluded.usuario, modelo=excluded.modelo, version_app=excluded.version_app, ultimo_contacto=excluded.ultimo_contacto
                    """, b.Dispositivo, usuario, b.Modelo, b.Version, Db.Ahora());
            var u = db.Q1("SELECT usuario, nombre, rol FROM usuarios WHERE usuario=$1", usuario)!;
            u["permisos"] = mios.OrderBy(x => x).ToList();
            return Results.Json(new { token = Seguridad.Token(new Sesion(usuario, rol, b.Dispositivo), cfg.Secreto), usuario = u, red = s.Red, sinWms, version = Version, permisos = u["permisos"] });
        });

        app.MapGet("/api/yo", (HttpContext c) =>
        {
            var s = S(c);
            if (s.Dispositivo != null) db.Ejecutar("UPDATE dispositivos SET ultimo_contacto=$2, version_app=COALESCE($3, version_app) WHERE id=$1", s.Dispositivo, Db.Ahora(), Q(c, "version"));
            var u = db.Q1("SELECT usuario, nombre, rol FROM usuarios WHERE usuario=$1", s.Usuario)!;
            u["version"] = Version;
            u["permisos"] = Perm(c).De(s.Usuario).OrderBy(x => x).ToList();
            return u;
        });

        app.MapGet("/api/estado", () => new
        {
            version = Version, hora = Db.Ahora(), @base = "ok", pendientesWms = db.Contar("SELECT count(*) FROM movimientos WHERE estado='pendiente_wms'"),
            sesionesWms = sv.SesionesActivas(), conectados = ev.Conectados, layout = layout.Ultima,
            servidor = new { local = cfg.Servidor.DireccionLocal, externa = cfg.Servidor.DireccionExterna },
        });

        // ================================================================ consultas
        app.MapGet("/api/consulta/{codigo}", async (HttpContext c, string codigo) =>
        {
            // un número de solicitud (10395-421) es un pedido: datos, bultos y artículos con foto
            if (Pedidos.PareceSolicitud(codigo)) return Results.Json(await app.Services.GetRequiredService<Pedidos>().Consultar(codigo));
            return Results.Json(await sv.Consulta(S(c).Usuario, codigo, Num(Q(c, "nivel"))));
        });
        app.MapGet("/api/contenedor/{cont}", async (string cont) =>
        {
            var info = await sv.InfoContenedor(cont);
            var movs = db.Q("SELECT * FROM movimientos WHERE contenedor=$1 ORDER BY hora_servidor DESC LIMIT 50", cont.ToUpperInvariant());
            var j = JsonSerializer.SerializeToNode(info, Motor.Json)!.AsObject();
            j["movimientos"] = JsonSerializer.SerializeToNode(movs, Motor.Json);
            return Results.Json(j);
        });
        app.MapGet("/api/articulo/{a}", async (string a) => Results.Json(await sv.InfoArticulo(a)));
        // ================================================================ actualización de la app de los celulares
        app.MapGet("/api/app/version", () => Results.Json(app.Services.GetRequiredService<AppMovil>().Version()));
        app.MapGet("/api/app/descargar", (HttpContext c) =>
        {
            var a = app.Services.GetRequiredService<AppMovil>().Ultima() ?? throw new ErrorUsuario("No hay APK publicado en el servidor.", 404);
            return Results.File(a.Archivo, "application/vnd.android.package-archive", $"SONA_Deposito_{a.Version}.apk", enableRangeProcessing: true);
        });
        app.MapPost("/api/app/subir", async Task<IResult> (HttpContext c) =>
        {
            SoloPanel(c);
            var lim = c.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (lim != null && !lim.IsReadOnly) lim.MaxRequestBodySize = 300L * 1024 * 1024;
            var form = await c.Request.ReadFormAsync();
            var f = form.Files.FirstOrDefault() ?? throw new ErrorUsuario("Falta el archivo APK.");
            await using var st = f.OpenReadStream();
            var r = await app.Services.GetRequiredService<AppMovil>().Subir(st, f.FileName, form["version"], form["notas"], form["obligatoria"] == "true", S(c).Usuario);
            db.Auditar("app", "subir", new { archivo = f.FileName, version = form["version"].ToString(), obligatoria = form["obligatoria"].ToString() }, S(c).Usuario);
            return Results.Json(r);
        }).DisableAntiforgery();
        app.MapGet("/api/app/equipos", (HttpContext c) =>
        {
            SoloLectura(c);
            return Results.Json(db.Q("""
                SELECT d.id, d.usuario, u.nombre, u.rol, d.modelo, d.version_app, d.ultimo_contacto
                FROM dispositivos d LEFT JOIN usuarios u ON u.usuario = d.usuario
                WHERE d.version_app IS NOT NULL AND d.version_app <> 'web' AND d.id NOT LIKE 'WEB-%' AND d.id NOT LIKE 'PANEL%'
                ORDER BY d.ultimo_contacto DESC LIMIT 300
                """));
        });

        // maestro de artículos de LES por empresa (ZART)
        app.MapGet("/api/articulos/catalogo", (HttpContext c) =>
        {
            var cat = c.RequestServices.GetRequiredService<CatalogoArticulos>();
            return Results.Json(cat.Buscar(int.TryParse(Q(c, "empresa"), out var e) ? e : null, Q(c, "q"), Q(c, "habilitados") is "1" or "true", Q(c, "conStock") is "1" or "true",
                int.TryParse(Q(c, "limite"), out var l) ? l : 1000));
        });
        app.MapPost("/api/articulos/catalogo/actualizar", (HttpContext c) =>
        {
            var cat = c.RequestServices.GetRequiredService<CatalogoArticulos>();
            cat.Actualizar();
            return Results.Json(cat.Situacion());
        });
        app.MapGet("/api/articulos/sugerir", async Task<IResult> (HttpContext c) => Results.Json(await sv.SugerirArticulos(Q(c, "q") ?? "")));
        app.MapGet("/api/articulos/buscar", async (HttpContext c) => Results.Json(await sv.BuscarPorDescripcion(Q(c, "q") ?? "")));
        app.MapGet("/api/articulo/{a}/fotos", async (string a) => (await sv.FotosArticulo(a)).Select(f => new { nombre = f.Nombre, url = Servicios.UrlImagen(f.Url) }));
        app.MapGet("/api/imagen", async (string? u) =>
        {
            if (u == null || !(u.StartsWith("http", StringComparison.OrdinalIgnoreCase) || u.StartsWith("catalogo:"))) return Results.BadRequest(new { error = "URL inválida" });
            var img = await sv.Les.ImagenCacheada(u);
            return img == null ? Results.NotFound(new { error = "Sin imagen" }) : Results.File(img.Value.Archivo, img.Value.Tipo);
        });
        app.MapGet("/api/posicion/{codigo}", async (HttpContext c, string codigo) =>
        {
            var p = await sv.ResolverPosicion(codigo, Num(Q(c, "nivel")), S(c).Usuario) ?? throw new ErrorUsuario("Posición no reconocida.", 404);
            return Results.Json(new { posicion = p, niveles = await sv.ContenidoPosicion(S(c).Usuario, p.Cara, p.Columna, p.Nivel) });
        });

        // ================================================================ destino
        app.MapPost("/api/sugerencia", async (HttpContext c, CargaReq b) =>
        {
            if (b.Contenedores is not { Count: > 0 }) throw new ErrorUsuario("Faltan contenedores.");
            var carga = await sv.ArmarCarga(b.Contenedores, b.Origen);
            var sug = await sv.SugerirDestino(S(c).Usuario, carga, Math.Clamp(b.Cuantas ?? 5, 1, 20));
            return Results.Json(new { carga = new { articulo = carga.Articulo, empresa = carga.Empresa, m3 = carga.M3, poca = carga.Poca, cantidad = carga.Contenedores.Count }, sugerencias = sug });
        });
        app.MapPost("/api/validar", async (HttpContext c, CargaReq b) =>
        {
            if (b.Contenedores is not { Count: > 0 } || string.IsNullOrWhiteSpace(b.Destino)) throw new ErrorUsuario("Faltan contenedores o destino.");
            var carga = await sv.ArmarCarga(b.Contenedores, b.Origen);
            return Results.Json(await sv.ValidarDestino(S(c).Usuario, carga, b.Destino, b.Nivel, b.Multi == true));
        });

        // Datos de varios contenedores a la vez (panel: transferir un lote a NN)
        app.MapPost("/api/contenedores/info", async (HttpContext c, ContenedoresReq b) =>
        {
            SoloPanel(c);
            var lista = new List<object>();
            foreach (var k in (b.Contenedores ?? new()).Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().Take(500))
            {
                var i = await sv.InfoContenedor(k, false);
                lista.Add(new { contenedor = k, encontrado = i.Encontrado, articulo = i.Articulo, descripcion = i.Descripcion, empresaNombre = i.EmpresaNombre,
                    cantidad = i.Cantidad, posicion = i.Posicion, disponible = i.Disponible });
            }
            return Results.Json(lista);
        });

        // ================================================================ movimientos
        app.MapPost("/api/movimientos", async (HttpContext c, MovimientosReq b) =>
            Results.Json(new { resultados = await mov.Registrar(S(c).Usuario, S(c).Dispositivo, b.Movimientos ?? new(), S(c).Rol, Perm(c).De(S(c).Usuario)) }));

        const string SqlMov = """
            SELECT m.*, e.nombre AS empresa_nombre, a.descripcion, u.nombre AS usuario_nombre
            FROM movimientos m LEFT JOIN empresas e ON e.codigo = m.empresa
            LEFT JOIN articulos a ON a.codigo = m.articulo LEFT JOIN usuarios u ON u.usuario = m.usuario
            """;
        (string Where, List<object?> P) Filtro(HttpContext c)
        {
            var w = new List<string>(); var p = new List<object?>();
            void Add(string cond, object? v) { p.Add(v); w.Add(cond.Replace("?", "$" + p.Count)); }
            if (!PuedeVer(c)) Add("m.usuario = ?", S(c).Usuario);
            else if (Q(c, "usuario") is { } us) Add("m.usuario = ?", us.ToUpperInvariant());
            if (Q(c, "desde") is { } de && DateTime.TryParse(de, out var dd)) Add("m.hora_servidor >= ?", Db.InicioDia(dd));
            if (Q(c, "hasta") is { } ha && DateTime.TryParse(ha, out var dh)) Add("m.hora_servidor < ?", Db.InicioDia(dh.AddDays(1)));
            if (Q(c, "tipo") is { } ti) Add("m.tipo = ?", ti);
            if (Q(c, "estado") is { } es) Add("m.estado = ?", es);
            if (Num(Q(c, "empresa")) is int em) Add("m.empresa = ?", em);
            if (Q(c, "texto") is { } tx)
            {
                p.Add("%" + tx + "%"); var n = "$" + p.Count;
                w.Add($"(m.contenedor LIKE {n} OR m.articulo LIKE {n} OR m.origen LIKE {n} OR m.destino LIKE {n} OR a.descripcion LIKE {n})");
            }
            return (w.Count > 0 ? "WHERE " + string.Join(" AND ", w) : "", p);
        }
        app.MapGet("/api/movimientos", (HttpContext c) =>
        {
            var (where, p) = Filtro(c);
            var limite = Math.Min(Num(Q(c, "limite")) ?? 500, 5000);
            var desde = Num(Q(c, "desdeFila")) ?? 0;
            var filas = db.Q($"{SqlMov} {where} ORDER BY m.hora_servidor DESC LIMIT {limite} OFFSET {desde}", p.ToArray());
            var total = db.Contar($"SELECT count(*) FROM movimientos m LEFT JOIN articulos a ON a.codigo = m.articulo {where}", p.ToArray());
            return Results.Json(new { filas, total });
        });
        app.MapGet("/api/movimientos/export.xlsx", (HttpContext c) =>
        {
            var (where, p) = Filtro(c);
            var filas = db.Q($"{SqlMov} {where} ORDER BY m.hora_servidor DESC LIMIT 100000", p.ToArray());
            return Results.File(Excel.Movimientos(filas), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"movimientos_{DateTime.Now:yyyy-MM-dd}.xlsx");
        });
        app.MapGet("/api/movimientos/{id}", (HttpContext c, string id) =>
        {
            var m = db.Q1($"{SqlMov} WHERE m.id=$1", id) ?? throw new ErrorUsuario("No existe.", 404);
            if (!PuedeVer(c) && m.S("usuario") != S(c).Usuario) throw new ErrorUsuario("No autorizado.", 403);
            return m;
        });
        app.MapPost("/api/movimientos/{id}/anular", (HttpContext c, string id, MotivoReq b) =>
        {
            SoloPanel(c);
            if (string.IsNullOrWhiteSpace(b.Motivo) || b.Motivo.Trim().Length < 3) throw new ErrorUsuario("Escribí el motivo.");
            var mensaje = mov.Anular(id, S(c).Usuario, b.Motivo);
            db.Auditar("movimientos", id, new { anular = b.Motivo }, S(c).Usuario);
            return new { mensaje };
        });
        app.MapPost("/api/movimientos/{id}/reintentar", (HttpContext c, string id) => { SoloPanel(c); mov.Reintentar(id); return new { ok = true }; });

        // ================================================================ autorizaciones
        app.MapGet("/api/autorizaciones", (HttpContext c) =>
        {
            var lista = db.Q("""
                SELECT a.*, (SELECT json_group_array(json_object('id', m.id, 'contenedor', m.contenedor, 'articulo', m.articulo, 'destino', m.destino, 'origen', m.origen, 'estado', m.estado))
                             FROM movimientos m WHERE m.grupo = a.grupo) AS movimientos
                FROM autorizaciones a WHERE ($1 IS NULL OR a.estado = $1) AND ($2 IS NULL OR a.solicitado_por = $2)
                ORDER BY a.estado = 'pendiente' DESC, a.creado DESC LIMIT 300
                """, Q(c, "estado"), PuedeVer(c) ? null : S(c).Usuario);
            return Results.Json(lista);
        });
        app.MapGet("/api/autorizaciones/{id:long}", (long id) => db.Q1("SELECT * FROM autorizaciones WHERE id=$1", id));
        app.MapPost("/api/autorizaciones/{id:long}", (HttpContext c, long id, ResolverReq b) => { Exigir(c, "autorizar"); mov.ResolverAutorizacion(id, b.Aprobar, S(c).Usuario, b.Nota); return new { ok = true }; });
        app.MapPost("/api/autorizaciones/{id:long}/cancelar", (HttpContext c, long id) => new { ok = mov.CancelarAutorizacion(id, S(c).Usuario, EsPanel(c)) });

        // ================================================================ guardado
        app.MapGet("/api/guardado/pendientes", async (HttpContext c) =>
        {
            var s = sv.SesionDe(S(c).Usuario);
            var lista = new List<object>();
            foreach (var e in db.Parametro("empresas_guardado", new List<int> { 6 }))
            {
                try
                {
                    foreach (var p in await s.PendientesGuardado(e))
                    {
                        var i = await sv.InfoContenedor(p.Contenedor);
                        lista.Add(new { contenedor = p.Contenedor, recepcion = p.Recepcion, guardada = p.Guardada, empresa = e, empresaNombre = sv.NombreEmpresa(e),
                            articulo = i.Articulo, descripcion = i.Descripcion, cantidad = i.Cantidad, m3 = i.M3, foto = i.Foto, fotos = i.Fotos });
                    }
                }
                catch (Exception ex) { lista.Add(new { error = $"Empresa {e}: {ex.Message}", empresa = e }); }
            }
            return Results.Json(lista);
        });

        // ================================================================ fotos de movimientos
        app.MapPost("/api/fotos", async (HttpRequest req) =>
        {
            var form = await req.ReadFormAsync();
            var archivo = form.Files.FirstOrDefault() ?? throw new ErrorUsuario("Falta la foto.");
            var id = Guid.NewGuid() + (archivo.ContentType.Contains("png") ? ".png" : ".jpg");
            await using (var fs = File.Create(Path.Combine(carpetaFotos, id))) await archivo.CopyToAsync(fs);
            return new { id };
        }).DisableAntiforgery();
        app.MapGet("/api/fotos/{id}", (string id) =>
        {
            var f = Path.Combine(carpetaFotos, Path.GetFileName(id));
            return File.Exists(f) ? Results.File(f, id.EndsWith(".png") ? "image/png" : "image/jpeg") : Results.NotFound(new { error = "No existe" });
        });

        // ================================================================ historia del WMS e ingresos/egresos del e-TMS
        app.MapGet("/api/historia", async (HttpContext c) =>
        {
            var cont = Q(c, "contenedor"); var art = Q(c, "articulo");
            if (cont == null && art == null) throw new ErrorUsuario("Falta el contenedor o el artículo.");
            var dias = int.TryParse(Q(c, "dias"), out var d) ? d : 365;
            try { return Results.Json(await sv.HistoriaWms(S(c).Usuario, cont, art, dias)); }
            catch (WmsError e) { throw new ErrorUsuario("WMS: " + e.Message, 502); }
        });
        app.MapGet("/api/ingresos-egresos/{articulo}", async (HttpContext c, string articulo) =>
            Results.Json(await sv.IngresosEgresos(S(c).Usuario, articulo, int.TryParse(Q(c, "empresa"), out var e) ? e : null)));

        // ================================================================ conteo
        app.MapGet("/api/conteo/tareas", (HttpContext c) => Results.Json(conteo.TareasDe(S(c).Usuario, S(c).Rol, Q(c, "abiertas") is "1" or "true")));
        app.MapPost("/api/conteo/tareas", async (HttpContext c, TareaReq b) =>
        {
            SoloPanel(c);
            List<string> lista;
            if (b.Modo == "articulo")
            {
                if (string.IsNullOrWhiteSpace(b.Articulo)) throw new ErrorUsuario("Falta el artículo.");
                lista = await conteo.PosicionesDeArticulo(b.Articulo);
            }
            else lista = b.Posiciones is { Count: > 0 } ? b.Posiciones : b.Rango != null ? conteo.PosicionesDeRango(b.Rango.Cara, b.Rango.Desde, b.Rango.Hasta, b.Rango.Niveles) : new List<string>();
            return Results.Json(conteo.CrearTarea(b.Nombre, lista, b.Asignado_a, S(c).Usuario, b.Modo, b.Articulo));
        });
        // Desde el celular: "contar un artículo" arma una tarea por artículo para quien la pide
        app.MapPost("/api/conteo/articulo", async (HttpContext c, ArticuloConteoReq b) =>
        {
            SoloPanel(c);   // las tareas las crea el analista
            if (string.IsNullOrWhiteSpace(b.Articulo)) throw new ErrorUsuario("Falta el artículo.");
            var art = b.Articulo.Trim().ToUpperInvariant();
            var lista = await conteo.PosicionesDeArticulo(art);
            return Results.Json(conteo.CrearTarea($"Artículo {art}", lista, S(c).Usuario, S(c).Usuario, "articulo", art));
        });
        app.MapPost("/api/conteo/reconteo", async (HttpContext c, ReconteoReq b) =>
        {
            SoloPanel(c);
            return Results.Json(await conteo.Reconteo(b.Lineas ?? new(), b.Tipo ?? "posicion", b.Asignado_a, b.Modo, S(c).Usuario));
        });
        app.MapGet("/api/conteo/tareas/{id:long}", (long id) => Results.Json(conteo.Tarea(id)));
        app.MapPost("/api/conteo/tareas/{id:long}/cerrar", (HttpContext c, long id) => { SoloPanel(c); conteo.Cerrar(id); return new { ok = true }; });
        app.MapGet("/api/conteo/posicion/{codigo}", async (HttpContext c, string codigo) =>
            Results.Json(await conteo.PosicionParaContar(S(c).Usuario, codigo, long.TryParse(Q(c, "tarea"), out var t) ? (long?)t : null, Q(c, "modo"))));
        app.MapPost("/api/conteo/lineas", async (HttpContext c, LineasReq b) =>
        {
            Exigir(c, "conteo");
            return Results.Json(await conteo.Cargar(S(c).Usuario, b.Tarea_id, b.Posicion, b.Lineas ?? new(), b.Esperados ?? new()));
        });
        app.MapGet("/api/conteo/diferencias", (HttpContext c) => { SoloPanel(c); return Results.Json(conteo.Diferencias(Q(c, "tarea") == "suelto" ? -1 : Num(Q(c, "tarea")), Q(c, "revision"), Q(c, "todas") is "1" or "true")); });
        app.MapPost("/api/conteo/lineas/{id:long}/revision", (HttpContext c, long id, RevisionReq b) =>
        {
            SoloPanel(c);
            if (b.Revision is not ("aprobada" or "recontar" or "rechazada")) throw new ErrorUsuario("Revisión inválida.");
            conteo.Revisar(id, b.Revision, S(c).Usuario);
            return new { ok = true };
        });

        // ================================================================ posiciones y zonas
        app.MapGet("/api/posiciones", (HttpContext c) => Results.Json(db.Q("""
            SELECT pe.*, e.nombre AS empresa_nombre, (SELECT count(*) FROM contenedores c WHERE c.posicion = pe.codigo) AS contenedores
            FROM pos_efectiva pe LEFT JOIN empresas e ON e.codigo = pe.empresa
            WHERE ($1 IS NULL OR pe.cara = $1) AND ($2 IS NULL OR pe.columna = $2) AND ($3 IS NULL OR pe.codigo LIKE $3)
            ORDER BY pe.cara, pe.columna, pe.nivel LIMIT 5000
            """, Q(c, "cara")?.ToUpperInvariant(), Num(Q(c, "columna")), Q(c, "texto") is { } t ? "%" + t + "%" : null)));
        app.MapGet("/api/posiciones/{codigo}", (string codigo) =>
        {
            var c = codigo.ToUpperInvariant();
            return Results.Json(new
            {
                ficha = db.Q1("SELECT * FROM posiciones WHERE codigo=$1", c), efectiva = db.Q1("SELECT * FROM pos_efectiva WHERE codigo=$1", c),
                contenedores = db.Q("SELECT * FROM contenedores WHERE posicion=$1 ORDER BY profundidad", c),
                movimientos = db.Q("SELECT * FROM movimientos WHERE origen=$1 OR destino=$1 ORDER BY hora_servidor DESC LIMIT 50", c),
            });
        });
        app.MapPut("/api/posiciones", async (HttpContext c) =>
        {
            SoloPanel(c);
            var b = await JsonNode.ParseAsync(c.Request.Body) ?? throw new ErrorUsuario("Cuerpo vacío.");
            var codigos = b["codigos"]?.AsArray().Select(x => x!.GetValue<string>().ToUpperInvariant()).ToList() ?? new();
            var cambios = b["cambios"]?.AsObject() ?? new JsonObject();
            if (codigos.Count == 0) throw new ErrorUsuario("No hay posiciones elegidas.");
            var permitidos = new HashSet<string> { "tipo", "empresa", "rol_zona", "mezcla", "capacidad", "habilitada", "motivo_inhab" };
            var sets = new List<string>(); var p = new List<object?> { codigos };
            foreach (var (k, v) in cambios)
            {
                if (!permitidos.Contains(k)) continue;
                object? val = v switch { null => null, JsonValue jv when jv.TryGetValue<bool>(out var bo) => bo, JsonValue jv when jv.TryGetValue<long>(out var l) => l, _ => v.ToString() };
                p.Add(val); sets.Add($"{k}=${p.Count}");
            }
            if (sets.Count == 0) return Results.Json(new { cambiadas = 0 });
            p.Add(S(c).Usuario); p.Add(Db.Ahora());
            var n = db.Ejecutar($"UPDATE posiciones SET {string.Join(", ", sets)}, actualizado_por=${p.Count - 1}, actualizado=${p.Count} WHERE codigo IN (SELECT value FROM json_each($1))", p.ToArray());
            db.Auditar("posiciones", string.Join(",", codigos.Take(20)) + (codigos.Count > 20 ? "…" : ""), cambios, S(c).Usuario);
            sv.OlvidarCaras();
            ev.Emitir(new() { ["tipo"] = "posiciones", ["cambiadas"] = n }, (u, rol) => Seguridad.RolesPanel.Contains(rol));
            return Results.Json(new { cambiadas = n });
        });
        app.MapGet("/api/zonas", () => Results.Json(db.Q("""
            SELECT z.*, e.nombre AS empresa_nombre, (SELECT count(*) FROM posiciones p WHERE p.cara = z.cara) AS posiciones
            FROM zonas z LEFT JOIN empresas e ON e.codigo = z.empresa ORDER BY z.cara
            """)));
        app.MapPut("/api/zonas/{cara}", (HttpContext c, string cara, ZonaReq b) =>
        {
            SoloPanel(c);
            db.Ejecutar("INSERT INTO zonas (cara, tipo, empresa, rol_zona, mezcla) VALUES ($1,$2,$3,$4,$5) ON CONFLICT(cara) DO UPDATE SET tipo=excluded.tipo, empresa=excluded.empresa, rol_zona=excluded.rol_zona, mezcla=excluded.mezcla",
                cara.ToUpperInvariant(), b.Tipo, b.Empresa, b.Rol_zona, b.Mezcla);
            db.Auditar("zonas", cara.ToUpperInvariant(), b, S(c).Usuario);
            sv.OlvidarCaras();
            return new { ok = true };
        });
        app.MapDelete("/api/zonas/{cara}", (HttpContext c, string cara) => { SoloAdmin(c); db.Ejecutar("DELETE FROM zonas WHERE cara=$1", cara.ToUpperInvariant()); return new { ok = true }; });
        app.MapPost("/api/layout/sincronizar", async (HttpContext c) =>
        {
            SoloPanel(c);
            CarasReq? b = null;
            try { b = await c.Request.ReadFromJsonAsync<CarasReq>(Motor.Json); } catch { }
            _ = layout.Sincronizar(S(c).Usuario, b?.Caras);
            return new { ok = true, mensaje = "Sincronización iniciada: el avance se ve en el monitor." };
        });
        app.MapGet("/api/layout/estado", () => Results.Json(layout.Ultima));
        app.MapGet("/api/empresas", () => Results.Json(db.Q("SELECT * FROM empresas ORDER BY nombre")));
        app.MapPut("/api/empresas/{codigo:int}", (HttpContext c, int codigo, EmpresaReq b) =>
        {
            SoloAdmin(c);
            db.Ejecutar("INSERT INTO empresas (codigo, nombre, alias) VALUES ($1,$2,$3) ON CONFLICT(codigo) DO UPDATE SET nombre=excluded.nombre, alias=excluded.alias", codigo, b.Nombre, b.Alias ?? new());
            sv.OlvidarEmpresas();
            return new { ok = true };
        });

        // ================================================================ usuarios, parámetros, monitor, configuración
        app.MapGet("/api/usuarios", (HttpContext c) =>
        {
            SoloLectura(c);
            return Results.Json(db.Q("""
                SELECT u.usuario, u.nombre, u.rol, u.activo, u.creado, u.ultimo_acceso, (u.clave_cifrada IS NOT NULL) AS clave_guardada, u.perfiles, u.permisos_mas, u.permisos_menos,
                       (SELECT max(ultimo_contacto) FROM dispositivos d WHERE d.usuario = u.usuario) AS ultimo_contacto
                FROM usuarios u ORDER BY u.usuario
                """));
        });
        app.MapPut("/api/usuarios/{usuario}", (HttpContext c, string usuario, UsuarioReq b) =>
        {
            SoloAdmin(c);
            var u = usuario.ToUpperInvariant();
            if (b.Rol is not ("maquinista" or "conteo" or "picker" or "supervisor" or "produccion" or "analista" or "admin")) throw new ErrorUsuario("Rol inválido.");
            db.Ejecutar("INSERT INTO usuarios (usuario, nombre, rol, activo, creado) VALUES ($1,$2,$3,$4,$5) ON CONFLICT(usuario) DO UPDATE SET nombre=excluded.nombre, rol=excluded.rol, activo=excluded.activo",
                u, b.Nombre, b.Rol, b.Activo ?? true, Db.Ahora());
            if (b.Activo == false) sv.OlvidarSesion(u);
            // si el usuario tenía solo el perfil de su rol, el perfil sigue al rol nuevo
            var pf = db.Q1("SELECT perfiles FROM usuarios WHERE usuario=$1", u)?.S("perfiles");
            var lista = string.IsNullOrEmpty(pf) ? new List<string>() : (JsonNode.Parse(pf) as JsonArray)?.Select(x => x!.ToString()).ToList() ?? new();
            if (lista.Count <= 1) db.Ejecutar("UPDATE usuarios SET perfiles=$2 WHERE usuario=$1", u, new JsonArray((JsonNode)b.Rol).ToJsonString());
            Perm(c).Olvidar(u);
            db.Auditar("usuarios", u, b, S(c).Usuario);
            return new { ok = true };
        });
        // ================================================================ permisos (perfiles + por usuario)
        app.MapGet("/api/permisos", (HttpContext c) => { SoloLectura(c); return Results.Json(Perm(c).Datos()); });
        app.MapGet("/api/usuarios/{usuario}/permisos", (HttpContext c, string usuario) => { SoloLectura(c); return Results.Json(Perm(c).DeUsuario(usuario)); });
        app.MapPut("/api/usuarios/{usuario}/permisos", (HttpContext c, string usuario, PermisosUsuarioReq b) =>
        {
            SoloAdmin(c);
            Perm(c).GuardarUsuario(usuario, b.Perfiles, b.Mas, b.Menos, S(c).Usuario);
            return Results.Json(Perm(c).DeUsuario(usuario));
        });
        app.MapPut("/api/perfiles/{codigo}", (HttpContext c, string codigo, PerfilReq b) =>
        {
            SoloAdmin(c);
            Perm(c).GuardarPerfil(codigo, b.Nombre, b.Descripcion, b.Permisos ?? new(), S(c).Usuario);
            return Results.Json(Perm(c).Datos());
        });
        app.MapDelete("/api/perfiles/{codigo}", (HttpContext c, string codigo) => { SoloAdmin(c); Perm(c).BorrarPerfil(codigo, S(c).Usuario); return Results.Json(Perm(c).Datos()); });

        app.MapGet("/api/parametros", () => Results.Json(db.Q("SELECT * FROM parametros ORDER BY clave")));
        app.MapPut("/api/parametros/{clave}", async (HttpContext c, string clave) =>
        {
            SoloAdmin(c);
            var b = await JsonNode.ParseAsync(c.Request.Body);
            var valor = b?["valor"] ?? throw new ErrorUsuario("Falta valor.");
            db.Ejecutar("INSERT INTO parametros (clave, valor) VALUES ($1,$2) ON CONFLICT(clave) DO UPDATE SET valor=excluded.valor", clave, valor.ToJsonString());
            db.Auditar("parametros", clave, valor, S(c).Usuario);
            return new { ok = true };
        });
        // Productividad de los operarios en un rango de días: movimientos de SONA, conteos, picking, medición de bultos y control de pedidos
        app.MapGet("/api/productividad", (HttpContext c) =>
        {
            SoloLectura(c);
            var desde = DateTime.TryParse(Q(c, "desde"), out var dd) ? dd.Date : DateTime.Today;
            var hasta = DateTime.TryParse(Q(c, "hasta"), out var dh) ? dh.Date : desde;
            if (hasta < desde) (desde, hasta) = (hasta, desde);
            return Results.Json(Rendimiento.Calcular(db, desde, hasta));
        });
        // Detalle de lo que hizo un operario (todas las fuentes juntas, en orden): para ZPRO y el panel web
        app.MapGet("/api/productividad/actividad", (HttpContext c) =>
        {
            SoloLectura(c);
            var desde = DateTime.TryParse(Q(c, "desde"), out var dd) ? dd.Date : DateTime.Today;
            var hasta = DateTime.TryParse(Q(c, "hasta"), out var dh) ? dh.Date : desde;
            var u = Q(c, "usuario")?.ToUpperInvariant() ?? throw new ErrorUsuario("Falta el usuario.");
            return Results.Json(Rendimiento.Actividad(db, u, desde, hasta));
        });
        // semáforo de ocupación: cada posición en verde (libre), amarillo (con lugar), rojo (llena), gris (inhabilitada / sin dato)
        app.MapGet("/api/monitor/ocupacion", (HttpContext c) =>
        {
            SoloLectura(c);
            var filas = db.Q("""
                SELECT pe.codigo, pe.cara, pe.columna, pe.nivel, pe.capacidad, pe.habilitada, pe.estado_wms,
                       (SELECT count(*) FROM contenedores c WHERE c.posicion = pe.codigo) AS cont
                FROM pos_efectiva pe ORDER BY pe.cara, pe.columna, pe.nivel
                """);
            static string Estado(Fila f)
            {
                if (!f.B("habilitada")) return "x";
                var cont = f.L("cont") ?? 0; var cap = f.L("capacidad") ?? 0;
                var wms = (f.S("estado_wms") ?? "").Trim().ToUpperInvariant();
                if (cap > 0) return cont == 0 && !wms.StartsWith("OCUP") ? "v" : cont >= cap ? "r" : "a";
                if (cont > 0 || wms.StartsWith("OCUP")) return "a";
                return wms.Length > 0 ? "v" : "s";   // sin capacidad ni estado del WMS: sin dato
            }
            var caras = filas.GroupBy(f => f.S("cara")!).Select(g =>
            {
                var pos = g.Select(f => new object[] { (int)(f.L("columna") ?? 0), (int)(f.L("nivel") ?? 0), Estado(f), f.S("codigo")!, f.L("cont") ?? 0, f.L("capacidad") ?? 0 }).ToList();
                return new
                {
                    cara = g.Key, total = pos.Count,
                    libres = pos.Count(x => (string)x[2] == "v"), parciales = pos.Count(x => (string)x[2] == "a"), llenas = pos.Count(x => (string)x[2] == "r"), inhabilitadas = pos.Count(x => (string)x[2] == "x"),
                    pos,
                };
            }).ToList();
            return Results.Json(new
            {
                caras,
                total = filas.Count,
                libres = caras.Sum(c => c.libres), parciales = caras.Sum(c => c.parciales), llenas = caras.Sum(c => c.llenas), inhabilitadas = caras.Sum(c => c.inhabilitadas),
            });
        });
        app.MapGet("/api/monitor", (HttpContext c) =>
        {
            SoloLectura(c);
            var hoy = Db.Hoy();
            return Results.Json(new
            {
                totales = db.Q1("""
                    SELECT sum(hora_servidor >= $1) AS hoy, sum(estado='pendiente_wms') AS pendientes, sum(estado='error') AS errores,
                           sum(estado='esperando_autorizacion') AS esperando FROM movimientos
                    """, hoy),
                operarios = db.Q("""
                    SELECT m.usuario, u.nombre, u.rol, count(*) AS hoy, sum(m.estado='pendiente_wms') AS pendientes, sum(m.estado='error') AS errores, max(m.hora_servidor) AS ultimo
                    FROM movimientos m LEFT JOIN usuarios u ON u.usuario = m.usuario
                    WHERE m.hora_servidor >= $1 GROUP BY m.usuario ORDER BY max(m.hora_servidor) DESC
                    """, hoy),
                porHora = db.Q("SELECT hora_servidor FROM movimientos WHERE hora_servidor >= $1", hoy)
                    .GroupBy(f => DateTime.Parse(f.S("hora_servidor")!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime().Hour)
                    .Select(g => new { hora = g.Key, n = g.Count() }).OrderBy(x => x.hora),
                dispositivos = db.Q("SELECT * FROM dispositivos ORDER BY ultimo_contacto DESC LIMIT 100"),
                autorizacionesPendientes = db.Contar("SELECT count(*) FROM autorizaciones WHERE estado='pendiente'"),
                ultimos = db.Q($"{SqlMov} ORDER BY m.hora_servidor DESC LIMIT 30"),
                sesionesWms = sv.SesionesActivas(), conectados = ev.Conectados, layout = layout.Ultima, version = Version,
            });
        });
        app.MapGet("/api/auditoria", (HttpContext c) => { SoloPanel(c); return Results.Json(db.Q("SELECT * FROM auditoria ORDER BY id DESC LIMIT 1000")); });
        app.MapPost("/api/respaldo", (HttpContext c) => { SoloAdmin(c); return new { archivo = respaldo.Hacer() }; });

        app.MapGet("/api/config", (HttpContext c) =>
        {
            SoloPanel(c);
            return Results.Json(new
            {
                puerto = cfg.Puerto, wms = cfg.Wms, servidor = cfg.Servidor, admins = cfg.Admins, sincronizarLayoutHoras = cfg.SincronizarLayoutHoras,
                les = new { url = cfg.Les.Url, account = cfg.Les.Account, buscador = cfg.Les.Buscador, usuario = cfg.Les.Usuario, claveCargada = !string.IsNullOrEmpty(cfg.Les.Clave) },
                etms = new { url = cfg.Etms.Url, usuario = cfg.Etms.Usuario, claveCargada = !string.IsNullOrEmpty(cfg.Etms.Clave) },
                carpetaDatos = cfg.Carpeta,
            });
        });
        app.MapPut("/api/config", async (HttpContext c) =>
        {
            SoloAdmin(c);
            var b = await JsonNode.ParseAsync(c.Request.Body) ?? throw new ErrorUsuario("Cuerpo vacío.");
            var puertoAntes = cfg.Puerto;
            ConfigEditor.Aplicar(cfg, b);
            cfg.Guardar();
            sv.OlvidarSesiones();
            sv.OlvidarWeb();
            sv.Les.Olvidar();
            db.Auditar("config", "config.json", new { cambiado = true }, S(c).Usuario);
            return new { ok = true, reiniciar = puertoAntes != cfg.Puerto };
        });
        app.MapPost("/api/config/probar", async (HttpContext c) => { SoloPanel(c); return Results.Json(await ConfigEditor.Probar(cfg, sv)); });

        // ================================================================ mensajes (chat panel ↔ operarios)
        app.MapGet("/api/mensajes", (HttpContext c) =>
        {
            var con = Q(c, "con");
            var desde = long.TryParse(Q(c, "desde"), out var d) ? d : (long?)null;
            if (con != null && EsPanel(c)) return Results.Json(mensajes.Conversacion(con, false, desde));
            return Results.Json(mensajes.Conversacion(S(c).Usuario, true, desde));
        });
        app.MapPost("/api/mensajes", (HttpContext c, MensajeReq b) => Results.Json(mensajes.Enviar(S(c).Usuario, EsPanel(c), b.Para, b.Texto ?? "")));
        app.MapPost("/api/mensajes/leido", (HttpContext c, LeidoReq b) =>
        {
            if (!string.IsNullOrWhiteSpace(b.Con) && EsPanel(c)) mensajes.LeyoPanel(b.Con); else mensajes.LeyoOperario(S(c).Usuario);
            return new { ok = true };
        });
        app.MapGet("/api/mensajes/sin-leer", (HttpContext c) => new { operario = mensajes.SinLeerOperario(S(c).Usuario), panel = EsPanel(c) ? mensajes.SinLeerPanel() : 0 });
        app.MapGet("/api/mensajes/conversaciones", (HttpContext c) => { SoloPanel(c); return Results.Json(mensajes.Conversaciones()); });

        // ================================================================ reconocimiento por foto (solo administradores)
        app.MapGet("/api/ia/estado", (HttpContext c) => { SoloPanel(c); return Results.Json(ia.Estado()); });
        app.MapPost("/api/ia/reconocer", async (HttpContext c) =>
        {
            Exigir(c, "reconocer_foto");
            var form = await c.Request.ReadFormAsync();
            var archivo = form.Files.FirstOrDefault() ?? throw new ErrorUsuario("Falta la foto.");
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            return Results.Json(await ia.Reconocer(ms.ToArray(), S(c).Usuario));
        }).DisableAntiforgery();
        app.MapPost("/api/ia/confirmar", async (HttpContext c, IaConfirmarReq b) => { SoloAdmin(c); await ia.Confirmar(b.FotoId, b.Articulo ?? "", S(c).Usuario); return new { ok = true }; });
        app.MapPost("/api/ia/ninguno", (HttpContext c, IaConfirmarReq b) => { SoloAdmin(c); ia.Ninguno(b.FotoId, S(c).Usuario); return new { ok = true }; });
        app.MapPost("/api/ia/descartar", (HttpContext c, IaConfirmarReq b) => { SoloAdmin(c); ia.Descartar(b.FotoId, S(c).Usuario); return new { ok = true }; });
        app.MapGet("/api/ia/historial", (HttpContext c) => { SoloPanel(c); return Results.Json(ia.Historial(Q(c, "estado") ?? "pendientes")); });
        app.MapGet("/api/fotos/ia/{id}", (string id) => ia.ArchivoFoto(id) is { } f ? Results.File(f, "image/jpeg") : Results.NotFound(new { error = "No existe" }));
        app.MapPost("/api/ia/indexar", (HttpContext c, IaIndexarReq b) =>
        {
            SoloAdmin(c);
            if (b.Detener == true) ia.Detener(); else ia.Indexar(b.Catalogo == true, b.Rehacer == true);
            return new { ok = true };
        });

        // ================================================================ catálogo de fotos propias (por código de artículo, nunca por semejanza)
        app.MapGet("/api/catalogo/{articulo}", async (string articulo) =>
        {
            var art = articulo.Trim().ToUpperInvariant();
            var propias = db.Q("SELECT id, archivo, subido_por, fecha FROM articulo_fotos WHERE articulo=$1 ORDER BY id", art)
                .Select(f => new { id = f.L("id"), url = Servicios.UrlImagen("catalogo:" + f.S("archivo")), subidoPor = f.S("subido_por"), fecha = f.S("fecha") }).ToList();
            var todas = await sv.FotosArticulo(art);
            return Results.Json(new { articulo = art, propias, externas = todas.Where(f => !f.Url.StartsWith("catalogo:")).Select(f => Servicios.UrlImagen(f.Url)) });
        });
        app.MapPost("/api/catalogo/{articulo}/fotos", async (HttpContext c, string articulo) =>
        {
            if (S(c).Rol is not ("analista" or "admin" or "picker")) throw new ErrorUsuario("No tenés permiso para cargar fotos.", 403);
            var art = articulo.Trim().ToUpperInvariant();
            var form = await c.Request.ReadFormAsync();
            if (form.Files.Count == 0) throw new ErrorUsuario("Falta la foto.");
            var dir = Path.Combine(cfg.Carpeta, "catalogo"); Directory.CreateDirectory(dir);
            var ids = new List<long>();
            foreach (var f in form.Files)
            {
                var nombre = Guid.NewGuid().ToString("N") + ".jpg";
                await using (var fs = File.Create(Path.Combine(dir, nombre))) await f.CopyToAsync(fs);
                ids.Add(Convert.ToInt64(db.Escalar("INSERT INTO articulo_fotos (articulo, archivo, subido_por, fecha) VALUES ($1,$2,$3,$4) RETURNING id", art, nombre, S(c).Usuario, Db.Ahora())));
            }
            sv.OlvidarFotos(art);
            db.Auditar("catalogo", art, new { fotos = ids.Count }, S(c).Usuario);
            return Results.Json(new { ok = true, ids });
        }).DisableAntiforgery();
        app.MapDelete("/api/catalogo/fotos/{id:long}", (HttpContext c, long id) =>
        {
            SoloPanel(c);
            var f = db.Q1("SELECT * FROM articulo_fotos WHERE id=$1", id) ?? throw new ErrorUsuario("No existe.", 404);
            db.Ejecutar("DELETE FROM articulo_fotos WHERE id=$1", id);
            try { File.Delete(Path.Combine(cfg.Carpeta, "catalogo", f.S("archivo")!)); } catch { }
            sv.OlvidarFotos(f.S("articulo")!);
            db.Auditar("catalogo", f.S("articulo")!, new { borrada = id }, S(c).Usuario);
            return new { ok = true };
        });

        // ================================================================ picking (7-PREPARACIÓN del WMS)
        void SoloPicking(HttpContext c) => Exigir(c, "picking");
        app.MapGet("/api/picking/empresas", async (HttpContext c) => { SoloPicking(c); return Results.Json(await picking.Empresas(S(c).Usuario)); });
        app.MapPost("/api/picking/iniciar", async (HttpContext c, PickIniciarReq b) =>
        {
            SoloPicking(c);
            try { return Results.Json(await picking.Iniciar(S(c).Usuario, b.Empresa, b.Modo)); }
            catch (WmsError e) { throw new ErrorUsuario("WMS: " + e.Message, 502); }
        });
        app.MapGet("/api/picking/actual", async (HttpContext c) => { SoloPicking(c); return Results.Json(await picking.Actual(S(c).Usuario) ?? new { }); });
        app.MapPost("/api/picking/accion", async (HttpContext c, PickAccion b) =>
        {
            SoloPicking(c);
            try { return Results.Json(await picking.Accion(S(c).Usuario, b)); }
            catch (WmsError e) { throw new ErrorUsuario("WMS: " + e.Message, 502); }
        });
        // faltantes que informan los pickers (panel de inventario hoy, panel de producción a futuro)
        app.MapGet("/api/faltantes", (HttpContext c) => { if (!Seguridad.RolesLectura.Contains(S(c).Rol) && S(c).Rol != "produccion") throw new ErrorUsuario("No autorizado.", 403); return Results.Json(picking.Faltantes(Q(c, "estado"))); });
        app.MapPost("/api/faltantes/{id:long}/atender", (HttpContext c, long id, MotivoReq b) => { if (!EsPanel(c) && S(c).Rol != "produccion") throw new ErrorUsuario("Solo producción, analistas y administradores.", 403); picking.AtenderFaltante(id, S(c).Usuario, b.Motivo); return new { ok = true }; });
        // ---------------------------------------------------------------- control de bulto (medidas en LES, como bultos.bultos-ejemplo.com.ar)
        var bultosLes = app.Services.GetRequiredService<Sona.Les.BultosLes>();
        // impresión de las etiquetas de medida en la impresora del puesto (PC del servidor)
        string? ImpresoraBultos(string? origen) => origen != null && cfg.Bultos.Impresoras.TryGetValue(origen, out var i) && !string.IsNullOrWhiteSpace(i) ? i
            : string.IsNullOrWhiteSpace(cfg.Bultos.Impresora) ? null : cfg.Bultos.Impresora;
        object ImprimirBultos(Sona.Les.BultosLes.Pedido p, IEnumerable<long> cuales, string? origen, string usuario)
        {
            var todos = p.Bultos.OrderBy(x => x.NroContenedor).ToList();
            var lista = cuales.ToHashSet();
            var pngs = todos.Select((b, i) => (b, i)).Where(x => lista.Contains(x.b.NroContenedor)).Select(x => EtiquetaBulto.Png(new EtiquetaBulto.Datos(
                p.NroSolicitud, x.b.NroContenedor, x.i + 1, todos.Count, x.b.Alto, x.b.Ancho, x.b.Profundidad, p.Cliente, p.Proyecto,
                Sona.Les.BultosLes.Origenes.GetValueOrDefault(origen ?? x.b.Origen ?? "", origen ?? x.b.Origen ?? ""), usuario, DateTime.Now), cfg.Bultos.AnchoMm, cfg.Bultos.AltoMm)).ToList();
            var imp = ImpresoraBultos(origen);
            var nombreImp = imp ?? Caratula.ImpresoraPredeterminada() ?? "predeterminada";
            if (pngs.Count == 0) return new { ok = true, cantidad = 0, impresora = nombreImp, error = (string?)null };
            try
            {
                Caratula.ImprimirImagenes(pngs, imp, cfg.Bultos.Copias, $"SONA etiquetas solicitud {p.NroSolicitud}");
                return new { ok = true, cantidad = pngs.Count, impresora = nombreImp, error = (string?)null };
            }
            catch (Exception e) { return new { ok = false, cantidad = 0, impresora = nombreImp, error = e.Message }; }
        }
        bool PuedeConfigImpresion(HttpContext c) => S(c).Rol == "admin" || Tiene(c, "ZIMP");
        app.MapGet("/api/bultos/impresion", (HttpContext c) => Results.Json(new
        {
            imprimirAlGuardar = cfg.Bultos.ImprimirAlGuardar, impresoras = cfg.Bultos.Impresoras, impresora = cfg.Bultos.Impresora, copias = cfg.Bultos.Copias,
            soloNuevos = cfg.Bultos.SoloNuevos, anchoMm = cfg.Bultos.AnchoMm, altoMm = cfg.Bultos.AltoMm,
            origenes = Sona.Les.BultosLes.Origenes.Select(o => new { codigo = o.Key, nombre = o.Value, impresora = ImpresoraBultos(o.Key) ?? Caratula.ImpresoraPredeterminada() }),
            instaladas = Caratula.Impresoras(), predeterminada = Caratula.ImpresoraPredeterminada(), windows = OperatingSystem.IsWindows(), puedeEditar = PuedeConfigImpresion(c),
        }));
        app.MapPut("/api/bultos/impresion", (HttpContext c, ImpresionBultosReq b) =>
        {
            if (!PuedeConfigImpresion(c)) throw new ErrorUsuario("Tu usuario no tiene permiso para configurar las impresoras (ZIMP).", 403);
            var bc = cfg.Bultos;
            if (b.ImprimirAlGuardar is bool ia) bc.ImprimirAlGuardar = ia;
            if (b.Impresoras != null) bc.Impresoras = b.Impresoras.Where(x => Sona.Les.BultosLes.Origenes.ContainsKey(x.Key) && !string.IsNullOrWhiteSpace(x.Value)).ToDictionary(x => x.Key, x => x.Value.Trim());
            if (b.Impresora != null) bc.Impresora = b.Impresora.Trim();
            if (b.Copias is int cp) bc.Copias = Math.Clamp(cp, 1, 5);
            if (b.SoloNuevos is bool sn) bc.SoloNuevos = sn;
            if (b.AnchoMm is int an) bc.AnchoMm = Math.Clamp(an, 30, 210);
            if (b.AltoMm is int al) bc.AltoMm = Math.Clamp(al, 20, 300);
            cfg.Guardar();
            db.Auditar("config", "bultos_impresion", new { bc.ImprimirAlGuardar, bc.Impresoras, bc.Impresora, bc.Copias, bc.SoloNuevos, bc.AnchoMm, bc.AltoMm }, S(c).Usuario);
            return new { ok = true };
        });
        app.MapPost("/api/bultos/impresion/probar", (HttpContext c, ImprimirBultosReq b) =>
        {
            if (!PuedeConfigImpresion(c)) throw new ErrorUsuario("Tu usuario no tiene permiso para configurar las impresoras (ZIMP).", 403);
            var p = new Sona.Les.BultosLes.Pedido("PRUEBA-1", null, "SONA", "Prueba de impresora", true, null, null,
                new() { new(3_123_456, 50, 40, 30, b.Origen ?? "PUESTO1") });
            return Results.Json(ImprimirBultos(p, new[] { 3_123_456L }, b.Origen ?? "PUESTO1", S(c).Usuario));
        });
        // vista previa de la etiqueta (PNG)
        app.MapGet("/api/bultos/{nro}/etiqueta/{bulto:long}", async (HttpContext c, string nro, long bulto) =>
        {
            Exigir(c, "control_bultos");
            var p = nro == "PRUEBA-1" ? new Sona.Les.BultosLes.Pedido("PRUEBA-1", null, "SONA", "Prueba de impresora", true, null, null, new() { new(bulto, 50, 40, 30, "PUESTO1") })
                : await bultosLes.Buscar(nro);
            var todos = p.Bultos.OrderBy(x => x.NroContenedor).ToList();
            var i = todos.FindIndex(x => x.NroContenedor == bulto);
            if (i < 0) throw new ErrorUsuario("Ese bulto no está en la solicitud.", 404);
            var b = todos[i];
            return Results.File(EtiquetaBulto.Png(new EtiquetaBulto.Datos(p.NroSolicitud, b.NroContenedor, i + 1, todos.Count, b.Alto, b.Ancho, b.Profundidad, p.Cliente, p.Proyecto,
                Sona.Les.BultosLes.Origenes.GetValueOrDefault(b.Origen ?? "", b.Origen ?? ""), S(c).Usuario, DateTime.Now), cfg.Bultos.AnchoMm, cfg.Bultos.AltoMm), "image/png");
        });
        // reimprimir etiquetas de bultos ya guardados
        app.MapPost("/api/bultos/{nro}/imprimir", async (HttpContext c, string nro, ImprimirBultosReq b) =>
        {
            Exigir(c, "control_bultos");
            var p = await bultosLes.Buscar(nro);
            var cuales = b.Bultos is { Count: > 0 } l ? l : p.Bultos.Select(x => x.NroContenedor).ToList();
            var r = ImprimirBultos(p, cuales, b.Origen, S(c).Usuario);
            db.Auditar("bultos", p.NroSolicitud, new { reimpresion = cuales, b.Origen }, S(c).Usuario);
            return Results.Json(r);
        });
        app.MapGet("/api/bultos/{nro}", async (HttpContext c, string nro) =>
        {
            Exigir(c, "control_bultos");
            var p = await bultosLes.Buscar(nro);
            return Results.Json(new { pedido = p, origenes = Sona.Les.BultosLes.Origenes.Select(o => new { codigo = o.Key, nombre = o.Value }) });
        });
        app.MapPost("/api/bultos/{nro}", async (HttpContext c, string nro, BultosReq b) =>
        {
            Exigir(c, "control_bultos");
            var p = await bultosLes.Buscar(nro);
            if (!p.Controlado) throw new ErrorUsuario("El pedido debe ser controlado: no se pueden generar etiquetas de medida hasta que esté controlado.");
            var origen = b.Origen is { Length: > 0 } o && Sona.Les.BultosLes.Origenes.ContainsKey(o) ? o : "PUESTO1";
            var nuevos = b.Nuevos ?? new();
            var cambios = (b.Cambios ?? new()).GroupBy(x => x.NroContenedor).ToDictionary(g => g.Key, g => g.Last());
            static bool MedidaOk(double a, double b, double c) => a is > 0 and <= 1000 && b is > 0 and <= 1000 && c is > 0 and <= 1000;
            if (nuevos.Any(n => !MedidaOk(n.Alto, n.Ancho, n.Profundidad)) || cambios.Values.Any(n => !MedidaOk(n.Alto, n.Ancho, n.Profundidad)))
                throw new ErrorUsuario("Las medidas van en cm, mayores a 0 y hasta 1000.");
            var quitar = (b.Quitar ?? new()).ToHashSet();
            var noExisten = cambios.Keys.Concat(quitar).Where(k => p.Bultos.All(x => x.NroContenedor != k)).ToList();
            if (noExisten.Count > 0) throw new ErrorUsuario($"El bulto {noExisten[0]} ya no está en la solicitud (¿lo cambió otro?). Volvé a buscarla.");
            var antes = p.Bultos.Where(x => cambios.ContainsKey(x.NroContenedor) || quitar.Contains(x.NroContenedor)).ToList();
            var lista = p.Bultos.Where(x => !quitar.Contains(x.NroContenedor))
                .Select(x => cambios.TryGetValue(x.NroContenedor, out var cb) ? x with { Alto = cb.Alto, Ancho = cb.Ancho, Profundidad = cb.Profundidad, Origen = origen } : x with { Origen = origen }).ToList();
            foreach (var n in nuevos)
                lista.Add(new Sona.Les.BultosLes.Bulto(Sona.Les.BultosLes.NuevoNumero(lista.Select(x => x.NroContenedor)), n.Alto, n.Ancho, n.Profundidad, origen));
            await bultosLes.Guardar(p.NroSolicitud, lista);
            var horaB = Db.Ahora();
            var resumen = string.Join(", ", new[] { nuevos.Count > 0 ? $"{nuevos.Count} nuevo(s)" : null, cambios.Count > 0 ? $"{cambios.Count} modificado(s)" : null, quitar.Count > 0 ? $"{quitar.Count} borrado(s)" : null }.Where(x => x != null));
            db.Ejecutar("INSERT INTO picking_eventos (usuario, op, tipo, cantidad, detalle, hora) VALUES ($1,$2,'bultos',$3,$4,$5)",
                S(c).Usuario, p.NroSolicitud, nuevos.Count, $"{resumen} · {lista.Count} bulto(s) en total · {Sona.Les.BultosLes.Origenes[origen]}", horaB);
            if (cambios.Count > 0 || quitar.Count > 0)
                db.Auditar("bultos", p.NroSolicitud, new
                {
                    antes = antes.Select(x => new { x.NroContenedor, x.Alto, x.Ancho, x.Profundidad }),
                    modificados = cambios.Values, borrados = quitar,
                }, S(c).Usuario);
            // la medición queda también como movimiento (ZMOV, monitor, productividad)
            db.Ejecutar("""
                INSERT INTO movimientos (id, grupo, tipo, contenedor, cantidad, usuario, hora_servidor, estado, comentario, detalle, aplicado_en)
                VALUES ($1,$2,'medicion',$3,$4,$5,$6,'registrado',$7,$8,$6)
                """, Guid.NewGuid().ToString(), "SOL-" + p.NroSolicitud, p.NroSolicitud, nuevos.Count, S(c).Usuario, horaB,
                $"Control de bulto: {resumen}, {lista.Count} en total · {Sona.Les.BultosLes.Origenes[origen]}",
                new JsonObject
                {
                    ["medidas"] = new JsonArray(nuevos.Select(x => (JsonNode)new JsonObject { ["alto"] = x.Alto, ["ancho"] = x.Ancho, ["profundidad"] = x.Profundidad }).ToArray()),
                    ["modificados"] = new JsonArray(cambios.Values.Select(x => (JsonNode)new JsonObject { ["bulto"] = x.NroContenedor, ["alto"] = x.Alto, ["ancho"] = x.Ancho, ["profundidad"] = x.Profundidad }).ToArray()),
                    ["borrados"] = new JsonArray(quitar.Select(x => (JsonNode)x).ToArray()), ["origen"] = origen,
                });
            ev.Emitir(new() { ["tipo"] = "picking", ["usuario"] = S(c).Usuario, ["evento"] = "bultos", ["op"] = p.NroSolicitud }, (u, rol) => Seguridad.RolesLectura.Contains(rol));
            var final = await bultosLes.Buscar(nro);
            object? impresion = null;
            if (b.Imprimir ?? cfg.Bultos.ImprimirAlGuardar)
            {
                // los bultos nuevos son los que no estaban antes; los modificados, los de "cambios"
                var previos = p.Bultos.Select(x => x.NroContenedor).ToHashSet();
                var aImprimir = cfg.Bultos.SoloNuevos
                    ? final.Bultos.Where(x => !previos.Contains(x.NroContenedor) || cambios.ContainsKey(x.NroContenedor)).Select(x => x.NroContenedor)
                    : final.Bultos.Select(x => x.NroContenedor);
                impresion = ImprimirBultos(final, aImprimir, origen, S(c).Usuario);
            }
            return Results.Json(new { pedido = final, guardados = lista.Count, nuevos = nuevos.Count, modificados = cambios.Count, borrados = quitar.Count, impresion });
        });

        // ================================================================ crear contenedor (LES + carga inicial) y NN
        app.MapPost("/api/contenedores/crear", async (HttpContext c, CrearContenedorReq b) =>
        {
            Exigir(c, "crear_contenedor");
            if (string.IsNullOrWhiteSpace(b.Posicion)) throw new ErrorUsuario("Falta la posición.");
            var altas = app.Services.GetRequiredService<Altas>();
            return Results.Json(await altas.Crear(S(c).Usuario, b.Articulo ?? "", b.Cantidad ?? 0, b.Posicion, b.Nivel, b.Prefijo, b.Contenedor));
        });
        // Posición NN (material perdido): por defecto Ubic.Ctrl 5422 nivel 1; se cambia con los parámetros nn_ubic y nn_nivel
        app.MapGet("/api/nn", () => new { destino = db.Parametro("nn_ubic", 5422L).ToString(), nivel = db.Parametro("nn_nivel", 1) });

        app.MapPost("/api/picking/salir", (HttpContext c) => { picking.Salir(S(c).Usuario); return new { ok = true }; });
        app.MapGet("/api/picking/eventos", (HttpContext c) =>
        {
            SoloLectura(c);
            var desde = DateTime.TryParse(Q(c, "desde"), out var dd) ? dd.Date : DateTime.Today;
            var hasta = DateTime.TryParse(Q(c, "hasta"), out var dh) ? dh.Date : desde;
            var u = Q(c, "usuario")?.ToUpperInvariant();
            return Results.Json(db.Q("SELECT * FROM picking_eventos WHERE hora >= $1 AND hora < $2 AND ($3 IS NULL OR usuario = $3) ORDER BY hora DESC LIMIT 5000",
                Db.InicioDia(desde), Db.InicioDia(hasta.AddDays(1)), u));
        });

        // ================================================================ 2.12: pausa del picking, pedidos, control de pedido, reubicar cajas, etiquetas
        app.MapPost("/api/picking/pausar", (HttpContext c) => { SoloPicking(c); return Results.Json(picking.Pausar(S(c).Usuario)); });
        app.MapPost("/api/picking/retomar", async (HttpContext c) =>
        {
            SoloPicking(c);
            try { return Results.Json(await picking.Retomar(S(c).Usuario) ?? new { }); }
            catch (WmsError e) { throw new ErrorUsuario("WMS: " + e.Message, 502); }
        });
        app.MapGet("/api/picking/encurso", (HttpContext c) => Results.Json(picking.EnCurso(S(c).Usuario) ?? new { }));

        var pedidos = app.Services.GetRequiredService<Pedidos>();
        // Consultar pedido / solicitud: datos, bultos, controles y artículos con sus fotos (pickers, analistas, administradores y supervisores)
        app.MapGet("/api/pedido/{nro}", async (HttpContext c, string nro) =>
        {
            Exigir(c, "consultar_pedido");
            return Results.Json(await pedidos.Consultar(nro));
        });
        app.MapPost("/api/pedido/{nro}/control", async (HttpContext c, string nro, ControlEntrada b) =>
        {
            Exigir(c, "control_pedido");
            return Results.Json(await pedidos.Controlar(S(c).Usuario, nro, b, carpetaFotos));
        });
        app.MapGet("/api/control-pedidos", (HttpContext c) =>
        {
            SoloLectura(c);
            var desde = DateTime.TryParse(Q(c, "desde"), out var dd) ? dd.Date : DateTime.Today;
            var hasta = DateTime.TryParse(Q(c, "hasta"), out var dh) ? dh.Date : desde;
            return Results.Json(db.Q("""
                SELECT k.*, u.nombre AS usuario_nombre FROM control_pedidos k LEFT JOIN usuarios u ON u.usuario = k.usuario
                WHERE k.hora >= $1 AND k.hora < $2 AND ($3 IS NULL OR k.usuario = $3) ORDER BY k.hora DESC LIMIT 5000
                """, Db.InicioDia(desde), Db.InicioDia(hasta.AddDays(1)), Q(c, "usuario")?.ToUpperInvariant()));
        });

        // Reubicar cajas: contenedores a los que se pueden pasar las cajas (mismo artículo y empresa; primero los de la misma posición)
        app.MapGet("/api/cajas/destinos/{cont}", async (HttpContext c, string cont) =>
        {
            var k = cont.Trim().ToUpperInvariant();
            var o = await sv.InfoContenedor(k, false);
            if (!o.Encontrado || o.Articulo == null) return Results.Json(new { origen = k, destinos = Array.Empty<object>() });
            var art = await sv.InfoArticulo(o.Articulo);
            var posO = Posicion.Normalizar(o.Posicion);
            var destinos = art.Contenedores.Where(x => x.Contenedor != null && !x.Contenedor.Equals(k, StringComparison.OrdinalIgnoreCase))
                .Select(x => new { contenedor = x.Contenedor!.ToUpperInvariant(), posicion = x.Posicion, cantidad = x.Cantidad, articulo = o.Articulo, descripcion = art.Descripcion,
                    mismaPosicion = posO != null && Posicion.Normalizar(x.Posicion) == posO, foto = art.Fotos.FirstOrDefault()?.Url })
                .OrderByDescending(x => x.mismaPosicion).ThenBy(x => x.posicion).Take(40).ToList();
            return Results.Json(new { origen = k, articulo = o.Articulo, posicion = o.Posicion, destinos });
        });

        // ---------------------------------------------------------------- reimprimir la etiqueta del contenedor (QR): Zebra o A4
        async Task<EtiquetaContenedor.Datos> DatosEtiqueta(string cont, string usuario)
        {
            var k = cont.Trim().ToUpperInvariant();
            var i = await sv.InfoContenedor(k, false);
            var alta = db.Q1("SELECT m.articulo, m.cantidad, m.destino, m.hora_servidor, a.descripcion, e.nombre AS empresa FROM movimientos m LEFT JOIN articulos a ON a.codigo = m.articulo LEFT JOIN empresas e ON e.codigo = m.empresa WHERE m.contenedor=$1 AND m.tipo='alta' ORDER BY m.hora_servidor DESC LIMIT 1", k);
            if (!i.Encontrado && alta == null) throw new ErrorUsuario($"El contenedor {k} no figura en stock (buscador SONA).", 404);
            return new EtiquetaContenedor.Datos(k, i.Articulo ?? alta?.S("articulo"), i.Descripcion ?? alta?.S("descripcion"), i.EmpresaNombre ?? i.Cliente ?? alta?.S("empresa"),
                i.Encontrado ? i.Cantidad : alta?.D("cantidad"), i.Posicion ?? alta?.S("destino"), DateTime.Now, usuario);
        }
        byte[] PngEtiqueta(EtiquetaContenedor.Datos d, string? formato) =>
            formato == "a4" ? EtiquetaContenedor.A4(d) : EtiquetaContenedor.Zebra(d, cfg.Etiquetas.AnchoMm, cfg.Etiquetas.AltoMm);
        static string Formato(string? f) => (f ?? "").Trim().ToLowerInvariant() == "a4" ? "a4" : "zebra";
        app.MapGet("/api/contenedores/{cont}/etiqueta.png", async (HttpContext c, string cont) =>
            Results.File(PngEtiqueta(await DatosEtiqueta(cont, S(c).Usuario), Formato(Q(c, "formato"))), "image/png"));
        // para imprimir desde el navegador de esta PC (abre el diálogo de impresión)
        app.MapGet("/api/contenedores/{cont}/etiqueta.html", async (HttpContext c, string cont) =>
        {
            var f = Formato(Q(c, "formato"));
            var d = await DatosEtiqueta(cont, S(c).Usuario);
            db.Auditar("etiquetas", d.Contenedor, new { reimpresion = f, donde = "navegador" }, S(c).Usuario);
            var png = PngEtiqueta(d, f);
            var titulo = $"Etiqueta {d.Contenedor}" + (f == "a4" ? " (A4)" : $" ({cfg.Etiquetas.AnchoMm} x {cfg.Etiquetas.AltoMm} mm)");
            return Results.Content(f == "a4" ? PaginaImpresion.HojasA4(titulo, new[] { png }) : PaginaImpresion.Etiquetas(titulo, new[] { png }, cfg.Etiquetas.AnchoMm, cfg.Etiquetas.AltoMm), "text/html; charset=utf-8");
        });
        // en la impresora de la PC del servidor (Zebra o A4 según ZIMP)
        app.MapPost("/api/contenedores/{cont}/imprimir", async (HttpContext c, string cont, ImprimirEtiquetaReq b) =>
        {
            var f = Formato(b.Formato);
            var d = await DatosEtiqueta(cont, S(c).Usuario);
            var imp = !string.IsNullOrWhiteSpace(b.Impresora) ? b.Impresora!.Trim() : f == "a4" ? cfg.Etiquetas.ImpresoraA4 : cfg.Etiquetas.ImpresoraZebra;
            var nombre = string.IsNullOrWhiteSpace(imp) ? Caratula.ImpresoraPredeterminada() ?? "predeterminada" : imp;
            try
            {
                Caratula.ImprimirImagenes(new() { PngEtiqueta(d, f) }, string.IsNullOrWhiteSpace(imp) ? null : imp, b.Copias ?? 1, $"SONA etiqueta {d.Contenedor} ({f})",
                    f == "a4" ? null : (cfg.Etiquetas.AnchoMm, cfg.Etiquetas.AltoMm));
                db.Auditar("etiquetas", d.Contenedor, new { reimpresion = f, impresora = nombre, copias = b.Copias ?? 1 }, S(c).Usuario);
                return Results.Json(new { ok = true, impresora = nombre, formato = f });
            }
            catch (Exception e) { return Results.Json(new { ok = false, impresora = nombre, formato = f, error = e.Message }); }
        });
        app.MapGet("/api/impresion/etiquetas", (HttpContext c) => Results.Json(new
        {
            impresoraZebra = cfg.Etiquetas.ImpresoraZebra, impresoraA4 = cfg.Etiquetas.ImpresoraA4, anchoMm = cfg.Etiquetas.AnchoMm, altoMm = cfg.Etiquetas.AltoMm,
            instaladas = Caratula.Impresoras(), predeterminada = Caratula.ImpresoraPredeterminada(), windows = OperatingSystem.IsWindows(),
            puedeEditar = S(c).Rol == "admin" || Tiene(c, "ZIMP"),
        }));
        app.MapPut("/api/impresion/etiquetas", (HttpContext c, EtiquetasConfigReq b) =>
        {
            if (S(c).Rol != "admin" && !Tiene(c, "ZIMP")) throw new ErrorUsuario("Tu usuario no tiene permiso para configurar las impresoras (ZIMP).", 403);
            if (b.ImpresoraZebra != null) cfg.Etiquetas.ImpresoraZebra = b.ImpresoraZebra.Trim();
            if (b.ImpresoraA4 != null) cfg.Etiquetas.ImpresoraA4 = b.ImpresoraA4.Trim();
            if (b.AnchoMm is int an) cfg.Etiquetas.AnchoMm = Math.Clamp(an, 30, 210);
            if (b.AltoMm is int al) cfg.Etiquetas.AltoMm = Math.Clamp(al, 20, 300);
            cfg.Etiquetas.MedidaRevisada = true;
            cfg.Guardar();
            db.Auditar("config", "etiquetas_contenedor", new { cfg.Etiquetas.ImpresoraZebra, cfg.Etiquetas.ImpresoraA4, cfg.Etiquetas.AnchoMm, cfg.Etiquetas.AltoMm }, S(c).Usuario);
            return new { ok = true };
        });

        // Impresora virtual: la etiqueta del contenedor (10 x 8 cm, con QR) para ver, imprimir o guardar en PDF desde el celular
        app.MapGet("/api/etiquetas/{cont}", async (HttpContext c, string cont) =>
        {
            var k = cont.Trim().ToUpperInvariant();
            var alta = db.Q1("SELECT m.articulo, m.cantidad, m.destino, m.hora_servidor, m.usuario, m.detalle, a.descripcion, e.nombre AS empresa FROM movimientos m LEFT JOIN articulos a ON a.codigo = m.articulo LEFT JOIN empresas e ON e.codigo = m.empresa WHERE m.contenedor=$1 AND m.tipo='alta' ORDER BY m.hora_servidor DESC LIMIT 1", k);
            InfoContenedor? info = null;
            if (alta == null) info = await sv.InfoContenedor(k, false);
            var html = Etiqueta.Html(k, alta?.S("articulo") ?? info?.Articulo, alta?.S("descripcion") ?? (alta?["detalle"] as JsonObject)?["descripcion"]?.ToString() ?? info?.Descripcion,
                alta?.S("empresa") ?? info?.EmpresaNombre, alta?.D("cantidad") ?? info?.Cantidad, alta?.S("destino") ?? info?.Posicion,
                alta?.S("hora_servidor") is { } h ? DateTime.Parse(h, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime() : DateTime.Now);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        // ================================================================ tiempo real
        app.Map("/api/eventos", async (HttpContext c) =>
        {
            if (!c.WebSockets.IsWebSocketRequest) { c.Response.StatusCode = 400; return; }
            using var ws = await c.WebSockets.AcceptWebSocketAsync();
            // al detener el servicio se cortan las conexiones en vivo enseguida (si no, Windows espera y corta el servicio a la fuerza)
            var vida = c.RequestServices.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
            using var corte = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted, vida.ApplicationStopping);
            await ev.Atender(ws, S(c).Usuario, S(c).Rol, corte.Token);
        });
    }
}
