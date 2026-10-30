namespace Sona;

/// <summary>
/// Chat entre el panel y los operarios. Cada operario tiene una conversación con "el panel" (la ven todos los analistas
/// y administradores); la conversación "*" son avisos del panel a todos los celulares.
/// Lo leído se guarda por lector: cada operario por su lado y el panel como uno solo ("PANEL").
/// </summary>
public sealed class Mensajes(Db db, Eventos ev)
{
    public const string Todos = "*";
    const string LectorPanel = "PANEL";

    public const string Esquema = """
CREATE TABLE IF NOT EXISTS mensajes (
  id INTEGER PRIMARY KEY AUTOINCREMENT, conversacion TEXT NOT NULL, de TEXT NOT NULL, desde_panel INTEGER NOT NULL,
  texto TEXT NOT NULL, hora TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS mensajes_conv ON mensajes(conversacion, id);
CREATE TABLE IF NOT EXISTS mensajes_leidos (lector TEXT NOT NULL, conversacion TEXT NOT NULL, ultimo_id INTEGER NOT NULL, PRIMARY KEY (lector, conversacion));
""";

    /// <summary>Manda un mensaje. Un operario siempre escribe en su conversación; el panel elige a quién (o "*" = todos).</summary>
    public Fila Enviar(string usuario, bool esPanel, string? para, string texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length == 0) throw new ErrorUsuario("El mensaje está vacío.");
        if (t.Length > 1000) t = t[..1000];
        string conv;
        bool desdePanel;
        if (esPanel && !string.IsNullOrWhiteSpace(para) && !para.Equals("PANEL", StringComparison.OrdinalIgnoreCase))
        {
            conv = para.Trim().ToUpperInvariant();
            if (conv != Todos && db.Q1("SELECT 1 AS x FROM usuarios WHERE usuario=$1", conv) == null) throw new ErrorUsuario($"No existe el usuario {conv}.", 404);
            desdePanel = true;
        }
        else { conv = usuario.ToUpperInvariant(); desdePanel = false; }
        var id = Convert.ToInt64(db.Escalar("INSERT INTO mensajes (conversacion, de, desde_panel, texto, hora) VALUES ($1,$2,$3,$4,$5) RETURNING id",
            conv, usuario.ToUpperInvariant(), desdePanel, t, Db.Ahora()));
        // lo que escribe uno mismo queda leído para su lado
        MarcarLeido(desdePanel ? LectorPanel : conv, conv, id);
        var m = Uno(id)!;
        ev.Emitir(new()
        {
            ["tipo"] = "mensaje", ["id"] = id, ["conversacion"] = conv, ["de"] = m.S("de"), ["nombre"] = m.S("nombre"),
            ["desde_panel"] = desdePanel, ["texto"] = t,
        }, (u, rol) => Seguridad.RolesPanel.Contains(rol) || conv == Todos || u == conv);
        return m;
    }

    Fila? Uno(long id) => db.Q1("SELECT m.*, u.nombre FROM mensajes m LEFT JOIN usuarios u ON u.usuario = m.de WHERE m.id=$1", id);

    /// <summary>Mensajes de una conversación (para un operario incluye los avisos a todos). Los últimos 300.</summary>
    public List<Fila> Conversacion(string conv, bool incluirTodos, long? desde = null) => db.Q("""
        SELECT * FROM (
          SELECT m.*, u.nombre FROM mensajes m LEFT JOIN usuarios u ON u.usuario = m.de
          WHERE (m.conversacion = $1 OR ($2 AND m.conversacion = '*')) AND ($3 IS NULL OR m.id > $3)
          ORDER BY m.id DESC LIMIT 300) ORDER BY id
        """, conv.ToUpperInvariant(), incluirTodos, desde);

    public void MarcarLeido(string lector, string conv, long? hasta = null)
    {
        var ultimo = hasta ?? Convert.ToInt64(db.Escalar("SELECT COALESCE(max(id),0) FROM mensajes WHERE conversacion=$1", conv) ?? 0L);
        db.Ejecutar("""
            INSERT INTO mensajes_leidos (lector, conversacion, ultimo_id) VALUES ($1,$2,$3)
            ON CONFLICT(lector, conversacion) DO UPDATE SET ultimo_id = max(mensajes_leidos.ultimo_id, excluded.ultimo_id)
            """, lector, conv, ultimo);
    }

    /// <summary>El operario abrió el chat: quedan leídos su conversación y los avisos a todos.</summary>
    public void LeyoOperario(string usuario)
    {
        var u = usuario.ToUpperInvariant();
        MarcarLeido(u, u); MarcarLeido(u, Todos);
    }

    public void LeyoPanel(string conv) => MarcarLeido(LectorPanel, conv.ToUpperInvariant());

    /// <summary>Cuántos mensajes del panel no leyó el operario.</summary>
    public long SinLeerOperario(string usuario)
    {
        var u = usuario.ToUpperInvariant();
        return db.Contar("""
            SELECT count(*) FROM mensajes m
            WHERE m.desde_panel = 1 AND m.conversacion IN ($1, '*')
              AND m.id > COALESCE((SELECT ultimo_id FROM mensajes_leidos l WHERE l.lector = $1 AND l.conversacion = m.conversacion), 0)
            """, u);
    }

    /// <summary>Para el panel: cada usuario con su último mensaje y lo que el panel no leyó. Primero "A todos" y los que tienen mensajes.</summary>
    public List<Fila> Conversaciones() => db.Q("""
        SELECT c.conversacion, u.nombre, u.rol, u.activo,
               (SELECT texto FROM mensajes WHERE conversacion = c.conversacion ORDER BY id DESC LIMIT 1) AS ultimo,
               (SELECT hora FROM mensajes WHERE conversacion = c.conversacion ORDER BY id DESC LIMIT 1) AS hora,
               (SELECT count(*) FROM mensajes m WHERE m.conversacion = c.conversacion AND m.desde_panel = 0
                  AND m.id > COALESCE((SELECT ultimo_id FROM mensajes_leidos l WHERE l.lector = 'PANEL' AND l.conversacion = c.conversacion), 0)) AS sin_leer
        FROM (SELECT '*' AS conversacion UNION SELECT usuario FROM usuarios WHERE activo = 1 UNION SELECT DISTINCT conversacion FROM mensajes) c
        LEFT JOIN usuarios u ON u.usuario = c.conversacion
        ORDER BY c.conversacion <> '*', sin_leer > 0 DESC, hora IS NULL, hora DESC, c.conversacion
        """);

    public long SinLeerPanel() => db.Contar("""
        SELECT count(*) FROM mensajes m WHERE m.desde_panel = 0
          AND m.id > COALESCE((SELECT ultimo_id FROM mensajes_leidos l WHERE l.lector = 'PANEL' AND l.conversacion = m.conversacion), 0)
        """);
}
