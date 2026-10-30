using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Sona;

/// <summary>Una fila de la base: columna → valor (las columnas JSON y booleanas ya convertidas).</summary>
public sealed class Fila : Dictionary<string, object?>
{
    public Fila() : base(StringComparer.OrdinalIgnoreCase) { }
    public string? S(string k) => TryGetValue(k, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
    public long? L(string k) => TryGetValue(k, out var v) && v is not null && v is not string { Length: 0 } ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : null;
    public int? I(string k) => L(k) is long l ? (int)l : null;
    public double? D(string k) => TryGetValue(k, out var v) && v is not null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : null;
    public bool B(string k) => TryGetValue(k, out var v) && v switch { bool b => b, long l => l != 0, string s => s is "1" or "true", _ => false };
}

/// <summary>
/// Base propia en SQLite (un solo archivo en la carpeta de datos: no hay nada que instalar, ni en Windows ni en Docker).
/// </summary>
public sealed class Db
{
    readonly string _cadena;
    public string Archivo { get; }

    static readonly HashSet<string> ColumnasJson = new(StringComparer.OrdinalIgnoreCase) { "alias", "fotos", "detalle", "posiciones", "cambio", "valor", "movimientos", "candidatos" };
    static readonly HashSet<string> ColumnasBool = new(StringComparer.OrdinalIgnoreCase) { "habilitada", "activo", "disponible", "clave_guardada", "contada", "desde_panel" };

    public Db(string carpeta)
    {
        Archivo = Path.Combine(carpeta, "sona.db");
        _cadena = new SqliteConnectionStringBuilder { DataSource = Archivo, Cache = SqliteCacheMode.Shared, Pooling = true, DefaultTimeout = 30 }.ToString();
    }

    public static string Ahora() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    public static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    /// <summary>Comienzo del día local (el de la PC / TZ del contenedor) expresado en UTC ISO.</summary>
    public static string InicioDia(DateTime local) => Iso(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), TimeZoneInfo.Local));
    public static string Hoy() => InicioDia(DateTime.Now);

    SqliteConnection Abrir()
    {
        var c = new SqliteConnection(_cadena);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=10000; PRAGMA foreign_keys=OFF;";
        cmd.ExecuteNonQuery();
        return c;
    }

    static void Parametros(SqliteCommand cmd, object?[] ps)
    {
        for (var i = 0; i < ps.Length; i++)
        {
            var v = ps[i] switch
            {
                null => DBNull.Value,
                bool b => b ? 1 : 0,
                DateTime d => Iso(d),
                JsonNode n => n.ToJsonString(),
                IEnumerable<string> l when ps[i] is not string => JsonSerializer.Serialize(l),
                var o => o,
            };
            cmd.Parameters.AddWithValue("$" + (i + 1), v);
        }
    }

    public List<Fila> Q(string sql, params object?[] ps)
    {
        using var c = Abrir();
        return Q(c, null, sql, ps);
    }

    public static List<Fila> Q(SqliteConnection c, SqliteTransaction? t, string sql, params object?[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        Parametros(cmd, ps);
        using var r = cmd.ExecuteReader();
        var lista = new List<Fila>();
        while (r.Read())
        {
            var f = new Fila();
            for (var i = 0; i < r.FieldCount; i++)
            {
                var n = r.GetName(i);
                object? v = r.IsDBNull(i) ? null : r.GetValue(i);
                if (v is string s && ColumnasJson.Contains(n)) { try { v = JsonNode.Parse(s); } catch { } }
                else if (v is long l && ColumnasBool.Contains(n)) v = l != 0;
                f[n] = v;
            }
            lista.Add(f);
        }
        return lista;
    }

    public Fila? Q1(string sql, params object?[] ps) => Q(sql, ps).FirstOrDefault();

    public int Ejecutar(string sql, params object?[] ps)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        Parametros(cmd, ps);
        return cmd.ExecuteNonQuery();
    }

    public static int Ejecutar(SqliteConnection c, SqliteTransaction? t, string sql, params object?[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        Parametros(cmd, ps);
        return cmd.ExecuteNonQuery();
    }

    public object? Escalar(string sql, params object?[] ps)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        Parametros(cmd, ps);
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    public long Contar(string sql, params object?[] ps) => Convert.ToInt64(Escalar(sql, ps) ?? 0L);

    public T Transaccion<T>(Func<SqliteConnection, SqliteTransaction, T> fn)
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        var r = fn(c, t);
        t.Commit();
        return r;
    }

    // ------------------------------------------------------------------ parámetros y auditoría
    public T Parametro<T>(string clave, T porDefecto)
    {
        var v = Escalar("SELECT valor FROM parametros WHERE clave=$1", clave) as string;
        if (v is null) return porDefecto;
        try { return JsonSerializer.Deserialize<T>(v) ?? porDefecto; } catch { return porDefecto; }
    }

    public void Auditar(string tabla, string clave, object cambio, string usuario) =>
        Ejecutar("INSERT INTO auditoria (tabla, clave, cambio, usuario, hora) VALUES ($1,$2,$3,$4,$5)", tabla, clave, JsonSerializer.Serialize(cambio), usuario, Ahora());

    // ------------------------------------------------------------------ esquema
    public void Iniciar()
    {
        using var c = Abrir();
        using (var cmd = c.CreateCommand()) { cmd.CommandText = "PRAGMA journal_mode=WAL;"; cmd.ExecuteNonQuery(); }
        void Correr(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        Correr(TablaMovimientos);
        Correr(TablaUsuarios);
        Correr(Esquema);
        Correr(Mensajes.Esquema);
        Correr(Reconocedor.Esquema);
        Correr(Picking.Esquema);
        Correr(Produccion.Esquema);
        Correr(Ingresos.Esquema);
        Correr(MisCredenciales.Esquema);
        Correr(Permisos.Esquema);
        Correr(CatalogoArticulos.Esquema);
        Correr("CREATE TABLE IF NOT EXISTS articulo_fotos (id INTEGER PRIMARY KEY AUTOINCREMENT, articulo TEXT NOT NULL, archivo TEXT NOT NULL, subido_por TEXT, fecha TEXT NOT NULL);" +
               "CREATE INDEX IF NOT EXISTS articulo_fotos_art ON articulo_fotos(articulo);");
        Migrar(c, Correr);
        Correr(IndicesMovimientos);
        Correr(Vistas);
        Sembrar(c);
    }


    /// <summary>Cambios de esquema sobre bases que ya existían (se aplican una sola vez y no borran datos).</summary>
    static void Migrar(SqliteConnection c, Action<string> correr)
    {
        bool TieneColumna(string tabla, string col) => Q(c, null, $"PRAGMA table_info({tabla})").Any(f => string.Equals(f.S("name"), col, StringComparison.OrdinalIgnoreCase));
        // 2.1: "mezcla" que informa el WMS en Ubicaciones (S = admite multiproducto)
        if (!TieneColumna("posiciones", "mezcla_wms")) correr("ALTER TABLE posiciones ADD COLUMN mezcla_wms INTEGER;");
        if (!TieneColumna("posiciones", "capacidad_wms")) correr("ALTER TABLE posiciones ADD COLUMN capacidad_wms INTEGER;");
        // 2.2: modos de conteo (ciego / confirmación / por artículo)
        if (!TieneColumna("conteo_tareas", "modo")) correr("ALTER TABLE conteo_tareas ADD COLUMN modo TEXT NOT NULL DEFAULT 'ciego';");
        if (!TieneColumna("conteo_tareas", "articulo")) correr("ALTER TABLE conteo_tareas ADD COLUMN articulo TEXT;");
        // 2.1: nuevo tipo de movimiento "reubicar_cajas"; 2.11: "alta" (crear contenedor); 2.12: "picking", "medicion" y "control_pedido"
        // (lo que hacen los pickers también queda como movimiento). SQLite no deja cambiar un CHECK: se rearma la tabla copiando todo
        var sql = Q(c, null, "SELECT sql FROM sqlite_master WHERE type='table' AND name='movimientos'").FirstOrDefault()?.S("sql") ?? "";
        if (sql.Length > 0 && (!sql.Contains("reubicar_cajas") || !sql.Contains("'alta'") || !sql.Contains("'control_pedido'")))
        {
            using var t = c.BeginTransaction();
            Ejecutar(c, t, "ALTER TABLE movimientos RENAME TO movimientos_v20;");
            Ejecutar(c, t, TablaMovimientos);
            Ejecutar(c, t, """
                INSERT INTO movimientos (id, grupo, tipo, contenedor, articulo, cantidad, empresa, origen, destino, destino_ubic, nivel, estado_nuevo, usuario,
                  dispositivo, hora_celular, hora_servidor, estado, respuesta_wms, intentos, proximo_intento, aplicado_en, comentario, fotos, detalle)
                SELECT id, grupo, tipo, contenedor, articulo, cantidad, empresa, origen, destino, destino_ubic, nivel, estado_nuevo, usuario,
                  dispositivo, hora_celular, hora_servidor, estado, respuesta_wms, intentos, proximo_intento, aplicado_en, comentario, fotos, detalle
                FROM movimientos_v20;
                """);
            Ejecutar(c, t, "DROP TABLE movimientos_v20;");
            t.Commit();
        }
        // 2.12: rol "supervisor" (panel web de solo lectura)
        var sqlU = Q(c, null, "SELECT sql FROM sqlite_master WHERE type='table' AND name='usuarios'").FirstOrDefault()?.S("sql") ?? "";
        // 2.13: rol "produccion" (panel de producción: asigna pedidos a los pickers)
        if (sqlU.Length > 0 && (!sqlU.Contains("'supervisor'") || !sqlU.Contains("'produccion'")))
        {
            using var t = c.BeginTransaction();
            Ejecutar(c, t, "ALTER TABLE usuarios RENAME TO usuarios_v211;");
            Ejecutar(c, t, TablaUsuarios);
            Ejecutar(c, t, "INSERT INTO usuarios (usuario, nombre, rol, activo, clave_cifrada, creado, ultimo_acceso) SELECT usuario, nombre, rol, activo, clave_cifrada, creado, ultimo_acceso FROM usuarios_v211;");
            Ejecutar(c, t, "DROP TABLE usuarios_v211;");
            t.Commit();
        }
        // 2.17: permisos por usuario (perfiles + agregados / quitados)
        if (!TieneColumna("usuarios", "perfiles")) correr("ALTER TABLE usuarios ADD COLUMN perfiles TEXT;");
        if (!TieneColumna("usuarios", "permisos_mas")) correr("ALTER TABLE usuarios ADD COLUMN permisos_mas TEXT;");
        if (!TieneColumna("usuarios", "permisos_menos")) correr("ALTER TABLE usuarios ADD COLUMN permisos_menos TEXT;");
        // 2.16: prioridad elegida al asignar
        if (!TieneColumna("produccion_caratulas", "prioridad")) correr("ALTER TABLE produccion_caratulas ADD COLUMN prioridad INTEGER;");
        // 2.15: usuario del LES (Ingresos pendientes) de cada usuario, cifrado
        if (!TieneColumna("usuarios", "les_usuario")) correr("ALTER TABLE usuarios ADD COLUMN les_usuario TEXT;");
        if (!TieneColumna("usuarios", "les_clave")) correr("ALTER TABLE usuarios ADD COLUMN les_clave TEXT;");
    }

    void Sembrar(SqliteConnection c)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Sona.Core.Recursos.semilla.json")
                      ?? Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.EndsWith("semilla.json")).Select(n => Assembly.GetExecutingAssembly().GetManifestResourceStream(n)).First()!;
        using var doc = JsonDocument.Parse(s);
        using var t = c.BeginTransaction();
        foreach (var e in doc.RootElement.GetProperty("empresas").EnumerateArray())
        {
            var codigo = e.GetProperty("codigo").GetInt32();
            var alias = e.TryGetProperty("alias", out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>();
            var ya = Q(c, t, "SELECT alias FROM empresas WHERE codigo=$1", codigo).FirstOrDefault();
            if (ya is null) Ejecutar(c, t, "INSERT INTO empresas (codigo, nombre, alias) VALUES ($1,$2,$3)", codigo, e.GetProperty("nombre").GetString(), alias);
            else
            {
                var actuales = (ya["alias"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToList() ?? new();
                Ejecutar(c, t, "UPDATE empresas SET alias=$2 WHERE codigo=$1", codigo, actuales.Union(alias).ToList());
            }
        }
        foreach (var z in doc.RootElement.GetProperty("zonas").EnumerateArray())
        {
            Ejecutar(c, t, "INSERT OR IGNORE INTO zonas (cara, tipo, empresa, rol_zona, mezcla) VALUES ($1,$2,$3,$4,$5)",
                z.GetProperty("cara").GetString(), z.GetProperty("tipo").GetString(),
                z.GetProperty("empresa").ValueKind == JsonValueKind.Number ? z.GetProperty("empresa").GetInt32() : null,
                z.GetProperty("rol_zona").GetString(), z.GetProperty("mezcla").GetString());
        }
        t.Commit();
    }

    const string TablaMovimientos = """
CREATE TABLE IF NOT EXISTS movimientos (
  id TEXT PRIMARY KEY, grupo TEXT,
  tipo TEXT NOT NULL CHECK (tipo IN ('transferencia','guardado','cambio_estado','conteo','ajuste','anulacion','reubicar_cajas','alta','picking','medicion','control_pedido')),
  contenedor TEXT, articulo TEXT, cantidad REAL, empresa INTEGER, origen TEXT, destino TEXT, destino_ubic INTEGER, nivel INTEGER,
  estado_nuevo TEXT, usuario TEXT NOT NULL, dispositivo TEXT, hora_celular TEXT, hora_servidor TEXT NOT NULL,
  estado TEXT NOT NULL DEFAULT 'pendiente_wms'
    CHECK (estado IN ('pendiente_wms','aplicado','error','esperando_autorizacion','rechazado','anulado','registrado')),
  respuesta_wms TEXT, intentos INTEGER NOT NULL DEFAULT 0, proximo_intento TEXT, aplicado_en TEXT, comentario TEXT,
  fotos TEXT NOT NULL DEFAULT '[]', detalle TEXT);
""";

    const string TablaUsuarios = """
CREATE TABLE IF NOT EXISTS usuarios (
  usuario TEXT PRIMARY KEY, nombre TEXT,
  rol TEXT NOT NULL DEFAULT 'maquinista' CHECK (rol IN ('maquinista','conteo','picker','supervisor','produccion','analista','admin')),
  activo INTEGER NOT NULL DEFAULT 1, clave_cifrada TEXT, creado TEXT, ultimo_acceso TEXT, les_usuario TEXT, les_clave TEXT);
""";

    const string IndicesMovimientos = """
CREATE INDEX IF NOT EXISTS mov_hora ON movimientos(hora_servidor);
CREATE INDEX IF NOT EXISTS mov_usuario ON movimientos(usuario, hora_servidor);
CREATE INDEX IF NOT EXISTS mov_contenedor ON movimientos(contenedor);
CREATE INDEX IF NOT EXISTS mov_estado ON movimientos(estado);
CREATE INDEX IF NOT EXISTS mov_grupo ON movimientos(grupo);
""";

    const string Vistas = """
DROP VIEW IF EXISTS pos_efectiva;
CREATE VIEW pos_efectiva AS
SELECT p.codigo, p.cara, p.columna, p.nivel, p.ubic_ctrl, p.capacidad, p.habilitada, p.motivo_inhab,
       p.estado_wms, p.articulo_wms, p.empresa_wms, p.mezcla_wms, p.ancho_cm, p.alto_cm, p.prof_cm,
       COALESCE(p.tipo, z.tipo, 'reserva') AS tipo, COALESCE(p.empresa, z.empresa) AS empresa,
       COALESCE(p.rol_zona, z.rol_zona, 'flex') AS rol_zona,
       -- la mezcla propia de la posición manda; si no tiene, si el WMS dice que admite mezcla se respeta (multiproducto); si no, la de la zona
       CASE WHEN p.mezcla IS NOT NULL THEN p.mezcla WHEN p.mezcla_wms = 1 THEN 'libre' ELSE COALESCE(z.mezcla, 'libre') END AS mezcla
FROM posiciones p LEFT JOIN zonas z ON z.cara = p.cara;
""";

    const string Esquema = """
CREATE TABLE IF NOT EXISTS empresas (codigo INTEGER PRIMARY KEY, nombre TEXT NOT NULL, alias TEXT NOT NULL DEFAULT '[]');
CREATE TABLE IF NOT EXISTS dispositivos (id TEXT PRIMARY KEY, usuario TEXT, modelo TEXT, version_app TEXT, ultimo_contacto TEXT);
CREATE TABLE IF NOT EXISTS zonas (
  cara TEXT PRIMARY KEY,
  tipo TEXT NOT NULL DEFAULT 'reserva' CHECK (tipo IN ('reserva','picking','proyectos','nn','otro_deposito')),
  empresa INTEGER,
  rol_zona TEXT NOT NULL DEFAULT 'flex' CHECK (rol_zona IN ('nucleo','desborde','flex')),
  mezcla TEXT NOT NULL DEFAULT 'monoproducto' CHECK (mezcla IN ('monoproducto','mismo_cliente','libre')));
CREATE TABLE IF NOT EXISTS posiciones (
  codigo TEXT PRIMARY KEY, cara TEXT NOT NULL, columna INTEGER NOT NULL, nivel INTEGER NOT NULL, ubic_ctrl INTEGER,
  tipo TEXT, empresa INTEGER, rol_zona TEXT, mezcla TEXT, capacidad INTEGER,
  ancho_cm INTEGER, alto_cm INTEGER, prof_cm INTEGER,
  habilitada INTEGER NOT NULL DEFAULT 1, motivo_inhab TEXT,
  estado_wms TEXT, articulo_wms TEXT, empresa_wms INTEGER, actualizado TEXT, actualizado_por TEXT,
  UNIQUE (cara, columna, nivel));
CREATE INDEX IF NOT EXISTS posiciones_ubic ON posiciones(ubic_ctrl);
CREATE INDEX IF NOT EXISTS posiciones_cara ON posiciones(cara, columna);
CREATE TABLE IF NOT EXISTS articulos (codigo TEXT PRIMARY KEY, descripcion TEXT, empresa INTEGER, empresa_nombre TEXT, id_les INTEGER, fotos TEXT, actualizado TEXT);
CREATE TABLE IF NOT EXISTS contenedores (
  codigo TEXT PRIMARY KEY, articulo TEXT, empresa INTEGER, posicion TEXT, profundidad TEXT,
  cantidad REAL, bultos REAL, m3 REAL, disponible INTEGER, motivo TEXT, actualizado TEXT);
CREATE INDEX IF NOT EXISTS contenedores_posicion ON contenedores(posicion);
CREATE INDEX IF NOT EXISTS contenedores_articulo ON contenedores(articulo);
CREATE TABLE IF NOT EXISTS autorizaciones (
  id INTEGER PRIMARY KEY AUTOINCREMENT, grupo TEXT NOT NULL, motivo TEXT NOT NULL, solicitado_por TEXT NOT NULL,
  estado TEXT NOT NULL DEFAULT 'pendiente' CHECK (estado IN ('pendiente','aprobada','rechazada','cancelada')),
  resuelto_por TEXT, resuelto_en TEXT, creado TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS conteo_tareas (
  id INTEGER PRIMARY KEY AUTOINCREMENT, nombre TEXT NOT NULL, posiciones TEXT NOT NULL, asignado_a TEXT,
  estado TEXT NOT NULL DEFAULT 'abierta' CHECK (estado IN ('abierta','en_curso','cerrada')), creado_por TEXT NOT NULL, creado TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS conteo_lineas (
  id INTEGER PRIMARY KEY AUTOINCREMENT, tarea_id INTEGER, posicion TEXT NOT NULL, contenedor TEXT, articulo TEXT,
  cantidad_contada REAL, cantidad_wms REAL,
  novedad TEXT CHECK (novedad IN ('ok','falta','sobra','ajeno','diferencia')),
  usuario TEXT NOT NULL, hora TEXT NOT NULL,
  revision TEXT NOT NULL DEFAULT 'pendiente' CHECK (revision IN ('pendiente','aprobada','recontar','rechazada')), revisado_por TEXT);
CREATE TABLE IF NOT EXISTS parametros (clave TEXT PRIMARY KEY, valor TEXT NOT NULL);
INSERT OR IGNORE INTO parametros (clave, valor) VALUES
  ('poca_cantidad_m3', '0.1'), ('niveles_max', '8'), ('empresas_guardado', '[6]'),
  ('niveles_rotacion_alta', '[1,2]'), ('dias_rotacion', '90'), ('fotos_obligatorias', '["ROTURA","NO CONFORME"]');
INSERT OR IGNORE INTO parametros (clave, valor) VALUES ('telegram_token', '""'), ('telegram_chat', '""');
CREATE TABLE IF NOT EXISTS control_pedidos (
  id INTEGER PRIMARY KEY AUTOINCREMENT, nro TEXT NOT NULL, usuario TEXT NOT NULL, cantidad REAL, bultos INTEGER, completo INTEGER NOT NULL DEFAULT 0,
  observacion TEXT, fotos TEXT NOT NULL DEFAULT '[]', detalle TEXT, telegram TEXT, hora TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS control_pedidos_hora ON control_pedidos(hora);
CREATE INDEX IF NOT EXISTS control_pedidos_nro ON control_pedidos(nro);
CREATE TABLE IF NOT EXISTS auditoria (id INTEGER PRIMARY KEY AUTOINCREMENT, tabla TEXT NOT NULL, clave TEXT NOT NULL, cambio TEXT NOT NULL, usuario TEXT NOT NULL, hora TEXT NOT NULL);
""";
}
