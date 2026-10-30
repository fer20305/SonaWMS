using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Sona.Dominio;
using Sona.Wms;

namespace Sona;

/// <summary>
/// Picking del WMS desde la app (7-PREPARACIÓN › 9 consolidado / 5 por unidades). SONA no inventa nada: lee la pantalla del WMS,
/// la muestra con la foto del artículo, controla lo que escanea el picker ANTES de mandarlo (posición, nivel, artículo, contenedor)
/// y deja registrado cada paso para la productividad y la trazabilidad.
/// </summary>
public sealed class Picking(Servicios sv, Eventos ev, Mensajes mensajes)
{
    public const string Esquema = """
CREATE TABLE IF NOT EXISTS picking_eventos (
  id INTEGER PRIMARY KEY AUTOINCREMENT, usuario TEXT NOT NULL, empresa INTEGER, modo TEXT, op TEXT, tipo TEXT NOT NULL,
  articulo TEXT, cantidad REAL, posicion TEXT, contenedor TEXT, contenedor_picking TEXT, detalle TEXT, hora TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS picking_eventos_hora ON picking_eventos(hora);
CREATE INDEX IF NOT EXISTS picking_eventos_usuario ON picking_eventos(usuario, hora);
CREATE INDEX IF NOT EXISTS picking_eventos_op ON picking_eventos(op);
CREATE TABLE IF NOT EXISTS op_items (
  op TEXT NOT NULL, empresa INTEGER, orden INTEGER, articulo TEXT NOT NULL, descripcion TEXT, ubicacion TEXT, cantidad REAL,
  contenedor TEXT, destino TEXT, modo TEXT, usuario TEXT, visto TEXT NOT NULL, PRIMARY KEY (op, empresa, articulo, orden));
CREATE INDEX IF NOT EXISTS op_items_op ON op_items(op);
CREATE TABLE IF NOT EXISTS faltantes (
  id INTEGER PRIMARY KEY AUTOINCREMENT, hora TEXT NOT NULL, usuario TEXT NOT NULL, op TEXT, empresa INTEGER, articulo TEXT, descripcion TEXT,
  cantidad REAL, posicion TEXT, ubic_ctrl INTEGER, contenedor TEXT, comentario TEXT, estado TEXT NOT NULL DEFAULT 'abierto',
  atendido_por TEXT, atendido_en TEXT, nota TEXT);
CREATE INDEX IF NOT EXISTS faltantes_estado ON faltantes(estado, hora);
""";

    readonly Db _db = sv.Db;
    sealed class Ctx { public int Empresa; public string Modo = "consolidado"; public PasoPicking? Ir; public DateTime? Pausa; public string Usuario = ""; }
    readonly ConcurrentDictionary<string, Ctx> _ctx = new();

    public static int OpcionDe(string? modo) => modo == "unidades" ? 5 : 9;

    void Registrar(string usuario, Ctx c, string tipo, string? op, string? articulo = null, double? cantidad = null, string? posicion = null,
        string? contenedor = null, string? contPicking = null, string? detalle = null)
    {
        var hora = Db.Ahora();
        _db.Ejecutar("""
            INSERT INTO picking_eventos (usuario, empresa, modo, op, tipo, articulo, cantidad, posicion, contenedor, contenedor_picking, detalle, hora)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)
            """, usuario, c.Empresa, c.Modo, op, tipo, articulo, cantidad, posicion, contenedor, contPicking, detalle, hora);
        // Cada línea pickeada queda también como movimiento (se ve en ZMOV, en el monitor y en la productividad)
        if (tipo == "pick")
        {
            var id = Guid.NewGuid().ToString();
            _db.Ejecutar("""
                INSERT INTO movimientos (id, grupo, tipo, contenedor, articulo, cantidad, empresa, origen, destino, usuario, hora_servidor, estado, comentario, detalle, aplicado_en)
                VALUES ($1,$2,'picking',$3,$4,$5,$6,$7,$8,$9,$10,'registrado',$11,$12,$10)
                """, id, "OP-" + (op ?? "?"), contenedor ?? contPicking, articulo, cantidad, c.Empresa, Posicion.Normalizar(posicion) ?? posicion, contPicking, usuario, hora,
                $"Picking {(c.Modo == "unidades" ? "por unidades" : "consolidado")} · OP {op}", new System.Text.Json.Nodes.JsonObject { ["op"] = op, ["modo"] = c.Modo, ["contenedor_picking"] = contPicking });
            ev.Emitir(new() { ["tipo"] = "movimiento", ["id"] = id, ["estado"] = "registrado", ["usuario"] = usuario }, (u, rol) => Seguridad.RolesLectura.Contains(rol));
        }
        ev.Emitir(new() { ["tipo"] = "picking", ["usuario"] = usuario, ["evento"] = tipo, ["op"] = op }, (u, rol) => Seguridad.RolesLectura.Contains(rol));
    }

    /// <summary>
    /// Guarda los renglones de la OP tal como los muestra el WMS (lista hpck0), para "Consultar pedido" por número de OP:
    /// qué artículos lleva, cuánto pide cada uno y de dónde sale. La cantidad pedida es la mayor que se vio (el WMS muestra lo pendiente).
    /// </summary>
    void GuardarOp(PasoPicking p, Ctx c)
    {
        if (p.Pantalla != "lista" || string.IsNullOrEmpty(p.Op) || p.Items.Count == 0) return;
        foreach (var it in p.Items)
            _db.Ejecutar("""
                INSERT INTO op_items (op, empresa, orden, articulo, descripcion, ubicacion, cantidad, contenedor, destino, modo, usuario, visto)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)
                ON CONFLICT(op, empresa, articulo, orden) DO UPDATE SET cantidad=max(COALESCE(op_items.cantidad,0), COALESCE(excluded.cantidad,0)),
                  descripcion=COALESCE(excluded.descripcion, op_items.descripcion), ubicacion=COALESCE(excluded.ubicacion, op_items.ubicacion),
                  destino=COALESCE(excluded.destino, op_items.destino), contenedor=COALESCE(excluded.contenedor, op_items.contenedor)
                """, p.Op, c.Empresa, it.Orden, it.Articulo, it.Descripcion, it.Ubicacion, it.Cantidad, it.Contenedor, p.Destino, c.Modo, c.Usuario, Db.Ahora());
    }

    async Task<object> Respuesta(PasoPicking p, Ctx c, string? error = null)
    {
        try { GuardarOp(p, c); } catch { /* no es crítico */ }
        if (p.Articulo != null)
        {
            try
            {
                var fotos = await sv.FotosArticulo(p.Articulo.Trim().ToUpperInvariant(), c.Empresa);
                p.Fotos = fotos.Select(f => Servicios.UrlImagen(f.Url)).ToList();
                p.Foto = p.Fotos.FirstOrDefault();
            }
            catch { }
            if (p.UbicCtrl == null && p.Cara != null && p.Columna != null)
                p.UbicCtrl = _db.Q1("SELECT ubic_ctrl FROM posiciones WHERE cara=$1 AND columna=$2 AND ubic_ctrl IS NOT NULL LIMIT 1", p.Cara, p.Columna)?.L("ubic_ctrl");
        }
        _avisos.TryRemove(c.Usuario, out var aviso);
        return new { paso = p, empresa = c.Empresa, empresaNombre = sv.NombreEmpresa(c.Empresa), modo = c.Modo, error, aviso,
            avance = p.Op != null && p.Pantalla is "lista" or "ir" or "tomar" ? Avance(p.Op) : null, postergadas = Postergadas(c.Usuario) };
    }

    readonly ConcurrentDictionary<string, string> _avisos = new();

    public sealed record AvanceOp(string Op, int Lineas, int Hechas, double Unidades, double Pickeadas, int Porcentaje)
    {
        /// <summary>Artículos con faltante abierto en la OP (la OP está en stand by esperando reposición).</summary>
        public List<string> Faltantes { get; init; } = new();
    }

    /// <summary>Avance de una OP según lo que SONA vio y registró: líneas de la OP (op_items) y lo pickeado (picking_eventos).</summary>
    public AvanceOp? Avance(string op)
    {
        var tot = _db.Q1("SELECT count(*) AS lineas, COALESCE(sum(cantidad),0) AS unidades FROM op_items WHERE op=$1", op);
        var hecho = _db.Q1("SELECT count(DISTINCT articulo) AS hechas, COALESCE(sum(cantidad),0) AS pick FROM picking_eventos WHERE op=$1 AND tipo='pick'", op);
        var lineas = (int)(tot?.L("lineas") ?? 0); var unidades = tot?.D("unidades") ?? 0;
        var hechas = (int)(hecho?.L("hechas") ?? 0); var pick = hecho?.D("pick") ?? 0;
        if (lineas == 0 && hechas == 0) return null;
        lineas = Math.Max(lineas, hechas);
        // por unidades si se conocen; si no, por líneas
        var pct = unidades > 0 ? (int)Math.Round(Math.Min(1, pick / unidades) * 100) : lineas > 0 ? (int)Math.Round(100.0 * hechas / lineas) : 0;
        return new AvanceOp(op, lineas, hechas, unidades, pick, pct);
    }

    /// <summary>OPs que el picker postergó (últimos 7 días) y que todavía no terminaron, con su avance.</summary>
    public List<AvanceOp> Postergadas(string usuario)
    {
        var ops = _db.Q("""
            SELECT op, max(hora) AS hora FROM picking_eventos
            WHERE usuario=$1 AND tipo='postergar' AND op IS NOT NULL AND hora >= $2
              AND op NOT IN (SELECT op FROM picking_eventos WHERE tipo='fin' AND op IS NOT NULL)
            GROUP BY op ORDER BY hora DESC LIMIT 20
            """, usuario, Db.Iso(DateTime.UtcNow.AddDays(-7)));
        return ops.Select(f =>
        {
            var op = f.S("op")!;
            var a = Avance(op) ?? new AvanceOp(op, 0, 0, 0, 0, 0);
            return a with { Faltantes = _db.Q("SELECT DISTINCT articulo FROM faltantes WHERE op=$1 AND estado='abierto' AND articulo IS NOT NULL", op).Select(x => x.S("articulo")!).ToList() };
        }).ToList();
    }

    public async Task<List<object>> Empresas(string usuario) =>
        (await sv.SesionDe(usuario).EmpresasWms()).Select(e => (object)new { codigo = e.Codigo, nombre = e.Nombre }).ToList();

    readonly ConcurrentDictionary<string, int> _ultima = new();

    /// <summary>Arranca el picking. La empresa la busca SONA sola: prueba primero la última donde el usuario tuvo OP
    /// y después el resto, y se queda con la primera donde el WMS le asigna una orden de preparación.</summary>
    public async Task<object> Iniciar(string usuario, int empresa, string? modo, string? aviso = null)
    {
        var m = modo == "unidades" ? "unidades" : "consolidado";
        var ses = sv.SesionDe(usuario);
        var candidatas = new List<int>();
        if (empresa > 0) candidatas.Add(empresa);
        else
        {
            if (_ultima.TryGetValue(usuario, out var u)) candidatas.Add(u);
            foreach (var e in await ses.EmpresasWms()) if (!candidatas.Contains(e.Codigo)) candidatas.Add(e.Codigo);
        }
        PasoPicking? p = null; Ctx? c = null;
        foreach (var emp in candidatas)
        {
            c = new Ctx { Empresa = emp, Modo = m, Usuario = usuario };
            p = await ses.PickingIniciar(emp, OpcionDe(m));
            if (p.Pantalla != "menu") break;
        }
        if (p == null || c == null) throw new WmsError("El WMS no devolvió empresas para tu usuario.");
        _ctx[usuario] = c;
        if (p.Pantalla != "menu") _ultima[usuario] = c.Empresa;
        if (p.Pantalla is "lista" or "ir") Registrar(usuario, c, "inicio", p.Op);
        if (aviso != null && p.Pantalla != "menu") _avisos[usuario] = aviso;
        return await Respuesta(p, c, p.Pantalla == "menu" ? "El WMS no tiene órdenes de preparación asignadas para vos" + (empresa > 0 ? " en esa empresa." : " en ninguna empresa.") : null);
    }

    public async Task<object?> Actual(string usuario)
    {
        if (!_ctx.TryGetValue(usuario, out var c)) return null;
        var p = await sv.SesionDe(usuario).PickingActual();
        return p == null ? null : await Respuesta(p, c);
    }

    /// <summary>
    /// Pausa: el picker deja la OP como está para hacer otra cosa (una consulta, una medición). SONA no le manda nada al WMS
    /// (la OP sigue asignada y la pantalla del WMS queda donde estaba); solo se anota para no contar ese rato como trabajo de picking.
    /// </summary>
    public object Pausar(string usuario)
    {
        if (!_ctx.TryGetValue(usuario, out var c)) return new { ok = true, enCurso = false };
        var paso = sv.SesionDe(usuario).UltimoPaso;
        c.Pausa = DateTime.UtcNow;
        Registrar(usuario, c, "pausa", paso?.Op, paso?.Articulo, detalle: paso?.Pantalla);
        return new { ok = true, enCurso = true, op = paso?.Op };
    }

    /// <summary>Retoma la OP en pausa: vuelve a leer la pantalla del WMS donde quedó (si el WMS quedó en una pregunta, vuelve al último paso conocido).</summary>
    public async Task<object?> Retomar(string usuario)
    {
        if (!_ctx.TryGetValue(usuario, out var c)) return null;
        var s = sv.SesionDe(usuario);
        var p = await s.PickingActual();
        if (p == null) return null;
        if (p.Pantalla is "abandonar" or "otra" && s.HayPasoBueno) p = await s.PickingRetomar();
        if (c.Pausa != null)
        {
            var min = (DateTime.UtcNow - c.Pausa.Value).TotalMinutes;
            Registrar(usuario, c, "retoma", p.Op, p.Articulo, Math.Round(min, 1), detalle: $"pausa de {min:0.#} min");
            c.Pausa = null;
        }
        return await Respuesta(p, c);
    }

    /// <summary>OP y paso en curso (para el cartel "picking en pausa" del menú).</summary>
    public object? EnCurso(string usuario)
    {
        if (!_ctx.TryGetValue(usuario, out var c)) return null;
        var p = sv.SesionDe(usuario).UltimoPaso;
        return new { op = p?.Op, pantalla = p?.Pantalla, articulo = p?.Articulo, empresaNombre = sv.NombreEmpresa(c.Empresa), pausa = c.Pausa != null };
    }

    static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

    public async Task<object> Accion(string usuario, PickAccion a)
    {
        if (!_ctx.TryGetValue(usuario, out var c)) throw new ErrorUsuario("No hay un picking en curso: elegí la opción de nuevo.");
        var s = sv.SesionDe(usuario);
        var actual = s.UltimoPaso ?? await s.PickingActual() ?? throw new ErrorUsuario("No hay un picking en curso.");
        async Task<object> Rechazo(string msj, string? detalle = null)
        {
            Registrar(usuario, c, "error_lectura", actual.Op, actual.Articulo, null, actual.Posicion, actual.ContenedorOrigen, actual.ContenedorPicking, detalle ?? msj);
            return await Respuesta(actual, c, msj);
        }
        PasoPicking p;
        switch (actual.Pantalla, a.Accion)
        {
            case ("lista", "postergar"):
            {
                var (pp, fin) = await Postergar(usuario, c, s, actual, null);
                if (fin != null) return fin;
                p = pp;
                break;
            }
            case ("lista", _):
                var opcion = a.Accion switch { "cerrar" => "5", "pendiente" => "7", "volver" or "salir" => "99", _ => (a.Item ?? 1).ToString() };
                p = await s.PickingOpcion(opcion);
                if (a.Accion is "cerrar") Registrar(usuario, c, a.Accion, actual.Op, contPicking: actual.ContenedorPicking);
                break;

            case ("ir", "cancelar"): p = await s.PickingEnviar(new(), "INTERRUMPIR"); break;
            // el artículo no está: queda el faltante, se avisa al panel (inventario / producción) y la OP se POSTERGA (queda en stand by)
            // con su avance; el picker pasa a la siguiente orden
            case ("ir" or "tomar" or "lista", "faltante"):
            {
                var info = actual.Pantalla == "tomar" ? (c.Ir ?? actual) : actual;
                var id = Faltante(usuario, c, info, a.Comentario);
                Registrar(usuario, c, "faltante", info.Op ?? actual.Op, info.Articulo ?? actual.Articulo, info.Cantidad, info.Posicion, info.ContenedorOrigen, info.ContenedorPicking, a.Comentario);
                // se vuelve a la lista de la OP (tomar → ir → lista) para postergarla como en el handheld
                p = actual;
                for (var i = 0; i < 3 && p.Pantalla is "tomar" or "ir"; i++)
                    p = await s.PickingEnviar(new(), p.Pantalla == "tomar" ? "VOLVER" : "BUTTON2");
                if (p.Pantalla != "lista")
                {
                    _avisos[usuario] = $"Faltante de {info.Articulo} informado al panel (N° {id}). No se pudo postergar sola la OP: postergala desde la lista.";
                    break;
                }
                var (pp, fin) = await Postergar(usuario, c, s, p, $"Faltante de {info.Articulo} informado al panel (N° {id}). ");
                if (fin != null) return fin;
                p = pp;
                break;
            }
            // el WMS avisó (lote / vencimiento / FIFO-FEFO) pero deja seguir: el picker toca "Continuar igual"
            case ("ir", "continuar_aviso"):
                if (!actual.PuedeContinuar) return await Rechazo("El WMS no está mostrando un aviso para continuar.");
                p = await s.PickingEnviar(new(), "E'CONTINUAR'.");
                Registrar(usuario, c, "aviso_aceptado", actual.Op, actual.Articulo, null, actual.Posicion, actual.ContenedorOrigen, actual.ContenedorPicking, actual.Mensaje);
                break;
            case ("ir", "volver"): p = await s.PickingEnviar(new(), "BUTTON2"); break;
            case ("ir", _):
            {
                // controles de SONA antes de mandar: posición (Ubic.Ctrl o código), nivel, artículo y contenedor
                var ub = N(a.Ubicacion);
                if (ub.Length == 0) return await Rechazo("Escaneá la etiqueta de la posición.");
                string ubicWms;
                if (Regex.IsMatch(ub, @"^\d+$"))
                {
                    if (actual.UbicCtrl != null && long.Parse(ub) != actual.UbicCtrl) return await Rechazo($"Esa no es la posición: tenés que ir a {actual.Posicion} (Ubic.Ctrl {actual.UbicCtrl}).", $"leyó ubic {ub}");
                    ubicWms = ub;
                }
                else
                {
                    var pos = Posicion.Interpretar(ub, sv.CarasConocidas());
                    if (pos == null || pos.Cara != actual.Cara || pos.Columna != actual.Columna) return await Rechazo($"Esa no es la posición: tenés que ir a {actual.Posicion}.", $"leyó {ub}");
                    if (actual.UbicCtrl == null) return await Rechazo("Escaneá la etiqueta (Ubic.Ctrl) de la posición.");
                    ubicWms = actual.UbicCtrl.Value.ToString();
                }
                if (!int.TryParse(a.Nivel, out var niv)) return await Rechazo("Tipeá el nivel.");
                if (actual.Nivel != null && niv != actual.Nivel) return await Rechazo($"Nivel equivocado: es el nivel {actual.Nivel}.", $"tipeó nivel {niv}");
                // el picker ya no escanea el artículo: confirma en la app que es el de la OP (si no viene, se usa el de la OP)
                var art = N(a.Articulo);
                if (art.Length == 0) art = N(actual.Articulo);
                if (art.Length == 0) return await Rechazo("Confirmá el artículo.");
                if (art != N(actual.Articulo) && !Regex.IsMatch(art, @"^\d{8,14}$"))   // un EAN lo valida el WMS
                    return await Rechazo($"Artículo equivocado: leíste {art} y es {actual.Articulo}.", $"leyó {art}");
                var cont = N(a.Contenedor);
                if (cont.Length > 0 && actual.ContenedorOrigen != null && cont != N(actual.ContenedorOrigen))
                    return await Rechazo($"Contenedor equivocado: es {actual.ContenedorOrigen}.", $"leyó {cont}");
                c.Ir = actual;
                p = await s.PickingEnviar(new() { ["_NUEVAUBIC"] = ubicWms, ["_NIVEL"] = niv.ToString(), ["_ARTCOD"] = art, ["_CONTENEDORAPICKEAR"] = cont });
                break;
            }

            case ("tomar", "reiniciar"): p = await s.PickingEnviar(new(), "REINICIAR"); break;
            case ("tomar", "volver"): p = await s.PickingEnviar(new(), "VOLVER"); break;
            case ("tomar", _):
            {
                if (a.Cantidad is not > 0) return await Rechazo("Poné la cantidad que tomaste.");
                if (actual.Cantidad != null && a.Cantidad > actual.Cantidad) return await Rechazo($"No podés tomar más de lo pedido ({actual.Cantidad:0.##}).", $"quiso tomar {a.Cantidad}");
                var cp = N(a.Contenedor);
                if (cp.Length == 0) return await Rechazo("Escaneá la etiqueta del contenedor de picking.");
                p = await s.PickingEnviar(new() { ["_CANTPCK"] = a.Cantidad.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), ["_PLTEXTERNO"] = cp });
                if (p.Pantalla != "tomar")
                    Registrar(usuario, c, "pick", actual.Op ?? c.Ir?.Op, actual.Articulo, a.Cantidad, c.Ir?.Posicion, c.Ir?.ContenedorOrigen, cp);
                break;
            }

            case ("etiquetar", _):
                p = await s.PickingEnviar(new());
                Registrar(usuario, c, "isla", actual.Op, cantidad: actual.TotalUnidades, contPicking: actual.ContenedorPicking, detalle: actual.Isla);
                break;
            case ("fin", _):
                Registrar(usuario, c, "fin", actual.Op, cantidad: actual.TotalUnidades, detalle: actual.Isla);
                p = await s.PickingEnviar(new());
                break;
            // El WMS pregunta "¿Abandona el Pedido?": seguir = volver al último paso de la OP sin abandonar
            case ("abandonar" or "otra", "seguir"): p = await s.PickingRetomar(); break;
            case ("menu", "siguiente"):
                p = await s.PickingEnviar(new() { ["_OPCION"] = OpcionDe(c.Modo).ToString() });
                if (p.Pantalla == "menu") return await Iniciar(usuario, 0, c.Modo);   // en esta empresa no hay más: busca en las otras
                if (p.Pantalla is "lista" or "ir") Registrar(usuario, c, "inicio", p.Op);
                break;
            default:
                p = await s.PickingEnviar(a.Valores ?? new(), a.Boton);
                break;
        }
        // aviso del WMS que deja continuar (lote / vencimiento): no es un error, la app muestra el cartel y el botón "Continuar igual"
        if (p.Pantalla == "ir" && p.PuedeContinuar)
        {
            Registrar(usuario, c, "aviso_wms", p.Op ?? actual.Op, actual.Articulo, null, actual.Posicion, actual.ContenedorOrigen, a.Contenedor, p.Mensaje);
            return await Respuesta(p, c);
        }
        // si el WMS se quedó en la misma pantalla con un mensaje, es un rechazo del WMS
        if (p.Pantalla == actual.Pantalla && p.Mensaje != null && actual.Pantalla is "ir" or "tomar")
            Registrar(usuario, c, "rechazo_wms", p.Op ?? actual.Op, actual.Articulo, a.Cantidad, actual.Posicion, actual.ContenedorOrigen, a.Contenedor, p.Mensaje);
        return await Respuesta(p, c, p.Pantalla == actual.Pantalla && p.Mensaje != null ? p.Mensaje : null);
    }

    /// <summary>
    /// Postergar (opción 6 de la lista del handheld): la OP queda en stand by con su avance y se pasa a la siguiente orden asignada.
    /// Devuelve el paso nuevo, o la respuesta completa si hubo que volver a entrar al picking.
    /// </summary>
    async Task<(PasoPicking Paso, object? Respuesta)> Postergar(string usuario, Ctx c, SesionHandheld s, PasoPicking lista, string? prefijo)
    {
        var av = lista.Op != null ? Avance(lista.Op) : null;
        var p = await s.PickingOpcion("6");
        Registrar(usuario, c, "postergar", lista.Op, contPicking: lista.ContenedorPicking, cantidad: av?.Porcentaje, detalle: (prefijo != null ? "por faltante · " : "") + (av != null ? $"avance {av.Porcentaje}% ({av.Hechas} de {av.Lineas} líneas)" : ""));
        _avisos[usuario] = (prefijo ?? "") + $"OP {lista.Op} postergada" + (av != null ? $" con {av.Porcentaje}% de avance ({av.Hechas} de {av.Lineas} líneas)." : ".");
        if (p.Pantalla == "menu") p = await s.PickingEnviar(new() { ["_OPCION"] = OpcionDe(c.Modo).ToString() });   // la siguiente OP
        if (p.Pantalla is "menu" or "menu_rf" or "otra")
            return (p, await Iniciar(usuario, p.Pantalla == "menu" ? 0 : c.Empresa, c.Modo));   // no hay más en esta empresa / volvió al menú del WMS
        if (p.Pantalla is "lista" or "ir") Registrar(usuario, c, "inicio", p.Op);
        return (p, null);
    }

    /// <summary>Guarda el faltante y lo manda como mensaje al panel (chat del operario) y como evento "faltante" (panel de inventario y de producción).</summary>
    long Faltante(string usuario, Ctx c, PasoPicking p, string? comentario)
    {
        var id = Convert.ToInt64(_db.Escalar("""
            INSERT INTO faltantes (hora, usuario, op, empresa, articulo, descripcion, cantidad, posicion, ubic_ctrl, contenedor, comentario)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11) RETURNING id
            """, Db.Ahora(), usuario, p.Op, c.Empresa, p.Articulo, p.Descripcion, p.Cantidad, p.Posicion, p.UbicCtrl, p.ContenedorOrigen, comentario));
        var texto = $"⚠ FALTANTE en picking · OP {p.Op} ({sv.NombreEmpresa(c.Empresa) ?? "empresa " + c.Empresa})\n" +
                    $"{p.Articulo} {p.Descripcion}\nPide {p.Cantidad:0.##} {p.Unidad ?? "u."} en {p.Posicion}" + (p.ContenedorOrigen != null ? $" (cont. {p.ContenedorOrigen})" : "") +
                    (string.IsNullOrWhiteSpace(comentario) ? "" : $"\nComentario: {comentario.Trim()}");
        try { mensajes.Enviar(usuario, false, null, texto); } catch { /* el faltante ya quedó guardado */ }
        ev.Emitir(new() { ["tipo"] = "faltante", ["id"] = id, ["usuario"] = usuario, ["op"] = p.Op, ["articulo"] = p.Articulo, ["descripcion"] = p.Descripcion,
            ["cantidad"] = p.Cantidad, ["posicion"] = p.Posicion, ["contenedor"] = p.ContenedorOrigen, ["empresa"] = sv.NombreEmpresa(c.Empresa) },
            (u, rol) => Seguridad.RolesLectura.Contains(rol) || rol == "produccion");
        return id;
    }

    public List<Fila> Faltantes(string? estado) => _db.Q("""
        SELECT f.*, u.nombre AS usuario_nombre FROM faltantes f LEFT JOIN usuarios u ON u.usuario = f.usuario
        WHERE ($1 IS NULL OR f.estado = $1) ORDER BY f.id DESC LIMIT 500
        """, estado);

    public void AtenderFaltante(long id, string usuario, string? nota)
    {
        _db.Ejecutar("UPDATE faltantes SET estado='atendido', atendido_por=$2, atendido_en=$3, nota=$4 WHERE id=$1", id, usuario, Db.Ahora(), nota);
        ev.Emitir(new() { ["tipo"] = "faltante", ["id"] = id, ["estado"] = "atendido", ["por"] = usuario }, (u, rol) => Seguridad.RolesLectura.Contains(rol) || rol == "produccion");
    }

    public void Salir(string usuario) { _ctx.TryRemove(usuario, out _); sv.SesionDe(usuario).PickingOlvidar(); }
}

public sealed record PickAccion(string? Accion, int? Item, string? Ubicacion, string? Nivel, string? Articulo, string? Contenedor, double? Cantidad,
    Dictionary<string, string>? Valores, string? Boton, string? Comentario = null);
