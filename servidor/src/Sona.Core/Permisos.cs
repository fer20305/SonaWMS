using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Sona;

/// <summary>
/// Permisos por usuario, al estilo de los grupos de Active Directory: un catálogo de permisos (cada función de la app,
/// cada transacción del panel y los paneles web), perfiles editables con un conjunto de permisos (los roles de siempre son
/// perfiles de sistema) y, por usuario, uno o más perfiles más permisos agregados o quitados puntualmente.
/// La app, el panel y los paneles web muestran solo lo permitido y el servidor lo controla en cada operación.
/// </summary>
public sealed class Permisos(Db db)
{
    public sealed record Permiso(string Codigo, string Nombre, string Grupo);

    public static readonly Permiso[] Catalogo =
    {
        // ---- operaciones (app y panel)
        new("consultar", "Consultar posición, contenedor o artículo", "Operaciones"),
        new("transferir", "Transferir", "Operaciones"),
        new("guardar", "Guardar recepciones", "Operaciones"),
        new("reubicar_cajas", "Reubicar cajas", "Operaciones"),
        new("cambiar_estado", "Cambiar estado (disponible / no disponible)", "Operaciones"),
        new("ajustar", "Ajustar contenedor (sumar / restar)", "Operaciones"),
        new("crear_contenedor", "Crear contenedor", "Operaciones"),
        new("enviar_nn", "Enviar a NN", "Operaciones"),
        new("eliminar", "Eliminar contenedor", "Operaciones"),
        new("autorizar", "Autorizar movimientos a zona de otro cliente", "Operaciones"),
        new("conteo", "Conteo", "Operaciones"),
        new("ingreso", "Módulo de ingreso (LES + WMS)", "Operaciones"),
        // ---- pedidos
        new("picking", "Picking", "Pedidos"),
        new("control_bultos", "Control de bultos (medidas)", "Pedidos"),
        new("control_pedido", "Control de pedido", "Pedidos"),
        new("consultar_pedido", "Consultar pedido", "Pedidos"),
        new("produccion_ver", "Panel de producción: ver", "Pedidos"),
        new("produccion_asignar", "Panel de producción: asignar pedidos", "Pedidos"),
        // ---- otros
        new("mensajes", "Mensajes", "Otros"),
        new("mis_movimientos", "Mis movimientos", "Otros"),
        new("reconocer_foto", "Reconocer artículo por foto (IA)", "Otros"),
        new("supervisor_web", "Panel web de supervisión (solo mira)", "Otros"),
        // ---- panel de escritorio
        new("panel", "Entrar al panel de escritorio", "Panel"),
        new("ZMON", "ZMON · Monitor del depósito", "Panel"),
        new("ZMOV", "ZMOV · Movimientos", "Panel"),
        new("ZPRO", "ZPRO · Productividad", "Panel"),
        new("ZMSG", "ZMSG · Mensajes con operarios", "Panel"),
        new("ZCAT", "ZCAT · Catálogo de fotos", "Panel"),
        new("ZART", "ZART · Artículos por empresa (maestro de LES)", "Panel"),
        new("ZPOS", "ZPOS · Fichas de posiciones", "Panel"),
        new("ZZON", "ZZON · Zonas / Layout objetivo", "Panel"),
        new("ZMAP", "ZMAP · Mapa de pasillo", "Panel"),
        new("ZCNT", "ZCNT · Tareas de conteo", "Panel"),
        new("ZDIF", "ZDIF · Conteos y diferencias", "Panel"),
        new("ZUSR", "ZUSR · Usuarios, perfiles y permisos", "Panel"),
        new("ZPAR", "ZPAR · Parámetros", "Panel"),
        new("ZEMP", "ZEMP · Empresas", "Panel"),
        new("ZAUD", "ZAUD · Auditoría", "Panel"),
        new("ZCFG", "ZCFG · Configuración de plataformas", "Panel"),
        new("ZEST", "ZEST · Estado y conexiones", "Panel"),
        new("ZIA", "ZIA · Reconocimiento por foto", "Panel"),
        new("ZLOG", "ZLOG · Registro del servidor", "Panel"),
        new("ZAPP", "ZAPP · App de los celulares", "Panel"),
        new("ZIMP", "ZIMP · Impresoras de etiquetas de bultos", "Panel"),
    };

    static readonly string[] PanelTodo = Catalogo.Where(p => p.Grupo == "Panel").Select(p => p.Codigo).ToArray();

    /// <summary>Los roles de siempre como perfiles de sistema (con lo que cada rol podía hacer hasta ahora).</summary>
    public static readonly Dictionary<string, (string Nombre, string[] Permisos)> PerfilesSistema = new()
    {
        ["maquinista"] = ("Maquinista", new[] { "consultar", "transferir", "guardar", "reubicar_cajas", "cambiar_estado", "mensajes", "mis_movimientos" }),
        ["conteo"] = ("Asistente de conteo", new[] { "consultar", "conteo", "cambiar_estado", "mensajes", "mis_movimientos" }),
        ["picker"] = ("Picker", new[] { "consultar", "picking", "control_bultos", "control_pedido", "consultar_pedido", "mensajes", "mis_movimientos" }),
        ["supervisor"] = ("Supervisor (solo ve)", new[] { "consultar", "consultar_pedido", "mensajes", "supervisor_web", "produccion_ver" }),
        ["produccion"] = ("Producción (asigna pedidos)", new[] { "consultar", "consultar_pedido", "mensajes", "produccion_ver", "produccion_asignar" }),
        ["analista"] = ("Analista", new[] { "consultar", "transferir", "guardar", "reubicar_cajas", "cambiar_estado", "ajustar", "crear_contenedor", "enviar_nn", "autorizar",
            "conteo", "ingreso", "picking", "consultar_pedido", "produccion_ver", "produccion_asignar", "mensajes", "mis_movimientos", "supervisor_web" }
            .Concat(PanelTodo).ToArray()),
        ["admin"] = ("Administrador", Catalogo.Select(p => p.Codigo).ToArray()),
    };

    public const string Esquema = """
CREATE TABLE IF NOT EXISTS perfiles (codigo TEXT PRIMARY KEY, nombre TEXT NOT NULL, descripcion TEXT, permisos TEXT NOT NULL DEFAULT '[]', sistema INTEGER NOT NULL DEFAULT 0, actualizado TEXT);
""";

    readonly ConcurrentDictionary<string, HashSet<string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Crea los perfiles de sistema que falten (no pisa los que ya se editaron).</summary>
    public void Sembrar()
    {
        foreach (var (cod, (nombre, perms)) in PerfilesSistema)
            db.Ejecutar("INSERT INTO perfiles (codigo, nombre, permisos, sistema, actualizado) VALUES ($1,$2,$3,1,$4) ON CONFLICT(codigo) DO NOTHING",
                cod, nombre, new JsonArray(perms.Select(p => (JsonNode)p).ToArray()).ToJsonString(), Db.Ahora());
        // el administrador siempre tiene todo (aunque se agreguen permisos nuevos)
        db.Ejecutar("UPDATE perfiles SET permisos=$1 WHERE codigo='admin'", new JsonArray(Catalogo.Select(p => (JsonNode)p.Codigo).ToArray()).ToJsonString());
    }

    public void Olvidar(string? usuario = null) { if (usuario == null) _cache.Clear(); else _cache.TryRemove(usuario, out _); }

    static List<string> Lista(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return (JsonNode.Parse(json) as JsonArray)?.Select(x => x!.ToString()).ToList() ?? new(); } catch { return new(); }
    }

    /// <summary>Permisos efectivos: los de sus perfiles (si no tiene perfiles asignados, el de su rol) + agregados − quitados.</summary>
    public HashSet<string> De(string usuario)
    {
        if (_cache.TryGetValue(usuario, out var c)) return c;
        var u = db.Q1("SELECT rol, perfiles, permisos_mas, permisos_menos FROM usuarios WHERE usuario=$1", usuario.ToUpperInvariant());
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (u != null)
        {
            var perfiles = Lista(u.S("perfiles"));
            if (perfiles.Count == 0 && u.S("rol") is { } r) perfiles.Add(r);
            if (u.S("rol") == "admin" && !perfiles.Contains("admin")) perfiles.Add("admin");   // el rol administrador nunca pierde todo
            foreach (var p in perfiles)
                foreach (var x in Lista(db.Q1("SELECT permisos FROM perfiles WHERE codigo=$1", p)?.S("permisos"))) set.Add(x);
            foreach (var x in Lista(u.S("permisos_mas"))) set.Add(x);
            if (u.S("rol") != "admin") foreach (var x in Lista(u.S("permisos_menos"))) set.Remove(x);
        }
        _cache[usuario] = set;
        return set;
    }

    public bool Puede(string usuario, string permiso) => De(usuario).Contains(permiso);

    public void Exigir(string usuario, string permiso)
    {
        if (!Puede(usuario, permiso))
            throw new ErrorUsuario($"Tu usuario no tiene permiso para: {Catalogo.FirstOrDefault(p => p.Codigo == permiso)?.Nombre ?? permiso}. Pedíselo a un administrador (ZUSR).", 403);
    }

    // ================================================================ administración

    public object Datos() => new
    {
        catalogo = Catalogo.Select(p => new { codigo = p.Codigo, nombre = p.Nombre, grupo = p.Grupo }),
        perfiles = db.Q("SELECT codigo, nombre, descripcion, permisos, sistema FROM perfiles ORDER BY sistema DESC, nombre")
            .Select(f => new { codigo = f.S("codigo"), nombre = f.S("nombre"), descripcion = f.S("descripcion"), permisos = Lista(f.S("permisos")), sistema = f.B("sistema") }),
    };

    public object DeUsuario(string usuario)
    {
        var u = db.Q1("SELECT usuario, nombre, rol, perfiles, permisos_mas, permisos_menos FROM usuarios WHERE usuario=$1", usuario.ToUpperInvariant())
                ?? throw new ErrorUsuario("No existe el usuario.", 404);
        var perfiles = Lista(u.S("perfiles"));
        return new
        {
            usuario = u.S("usuario"), nombre = u.S("nombre"), rol = u.S("rol"),
            perfiles = perfiles.Count > 0 ? perfiles : new List<string> { u.S("rol") ?? "maquinista" },
            mas = Lista(u.S("permisos_mas")), menos = Lista(u.S("permisos_menos")), efectivos = De(usuario).OrderBy(x => x).ToList(),
        };
    }

    static string Json(IEnumerable<string>? l) => new JsonArray((l ?? Enumerable.Empty<string>()).Distinct().Select(x => (JsonNode)x).ToArray()).ToJsonString();

    public void GuardarUsuario(string usuario, List<string>? perfiles, List<string>? mas, List<string>? menos, string por)
    {
        var u = usuario.ToUpperInvariant();
        var validos = Catalogo.Select(p => p.Codigo).ToHashSet();
        var perfilesOk = db.Q("SELECT codigo FROM perfiles").Select(f => f.S("codigo")!).ToHashSet();
        if (perfiles != null && perfiles.Any(p => !perfilesOk.Contains(p))) throw new ErrorUsuario("Perfil inexistente.");
        if ((mas ?? new()).Concat(menos ?? new()).Any(p => !validos.Contains(p))) throw new ErrorUsuario("Permiso inexistente.");
        if (db.Q1("SELECT 1 AS x FROM usuarios WHERE usuario=$1", u) == null) throw new ErrorUsuario("No existe el usuario.", 404);
        // el rol (perfil principal) sigue el primer perfil de sistema, para lo que todavía mira el rol (menú del celular viejo, roles del WMS)
        var principal = perfiles?.FirstOrDefault(p => PerfilesSistema.ContainsKey(p));
        db.Ejecutar("UPDATE usuarios SET perfiles=$2, permisos_mas=$3, permisos_menos=$4, rol=COALESCE($5, rol) WHERE usuario=$1",
            u, Json(perfiles), Json(mas), Json(menos), principal);
        db.Auditar("permisos", u, new { perfiles, mas, menos }, por);
        Olvidar(u);
    }

    public void GuardarPerfil(string codigo, string nombre, string? descripcion, List<string> permisos, string por)
    {
        var c = codigo.Trim().ToLowerInvariant();
        if (c.Length == 0 || nombre.Trim().Length == 0) throw new ErrorUsuario("Falta el código o el nombre del perfil.");
        if (c == "admin") throw new ErrorUsuario("El perfil Administrador tiene siempre todos los permisos.");
        var validos = Catalogo.Select(p => p.Codigo).ToHashSet();
        if (permisos.Any(p => !validos.Contains(p))) throw new ErrorUsuario("Permiso inexistente.");
        db.Ejecutar("""
            INSERT INTO perfiles (codigo, nombre, descripcion, permisos, sistema, actualizado) VALUES ($1,$2,$3,$4,0,$5)
            ON CONFLICT(codigo) DO UPDATE SET nombre=excluded.nombre, descripcion=excluded.descripcion, permisos=excluded.permisos, actualizado=excluded.actualizado
            """, c, nombre.Trim(), descripcion, Json(permisos), Db.Ahora());
        db.Auditar("perfiles", c, new { nombre, permisos }, por);
        Olvidar();
    }

    public void BorrarPerfil(string codigo, string por)
    {
        var c = codigo.ToLowerInvariant();
        if (db.Q1("SELECT sistema FROM perfiles WHERE codigo=$1", c)?.B("sistema") == true) throw new ErrorUsuario("Los perfiles de sistema no se borran (se pueden editar).");
        var usan = db.Q("SELECT usuario FROM usuarios WHERE perfiles LIKE $1", $"%\"{c}\"%").Select(f => f.S("usuario")).ToList();
        if (usan.Count > 0) throw new ErrorUsuario("Lo usan: " + string.Join(", ", usan.Take(10)) + ". Sacáselo primero.");
        db.Ejecutar("DELETE FROM perfiles WHERE codigo=$1", c);
        db.Auditar("perfiles", c, new { borrado = true }, por);
        Olvidar();
    }
}
