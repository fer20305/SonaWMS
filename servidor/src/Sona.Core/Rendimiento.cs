using System.Globalization;

namespace Sona;

/// <summary>
/// Productividad por operario en un rango de días. Junta todas las fuentes:
///   · movimientos de depósito (transferencias, guardados, estados, reubicar cajas, ajustes, altas)
///   · picking (picking_eventos: OPs, líneas, unidades, errores evitados, tiempo activo descontando las pausas)
///   · control de bulto / medición (solicitudes y bultos medidos)
///   · control de pedido (pedidos controlados, bultos, completos)
///   · conteos (posiciones contadas y diferencias)
/// </summary>
public static class Rendimiento
{
    /// <summary>Tipos de movimiento que vienen del picker (se cuentan en su propia columna, no como "movimientos de depósito").</summary>
    public static readonly HashSet<string> TiposPicker = new() { "picking", "medicion", "control_pedido" };

    /// <summary>Un hueco de más de esto entre dos eventos de picking no cuenta como trabajo (almuerzo, otra tarea…).</summary>
    const double MaxHuecoMin = 15;

    static DateTime Local(string? iso) => DateTime.Parse(iso!, null, DateTimeStyles.RoundtripKind).ToLocalTime();

    public static object Calcular(Db db, DateTime desde, DateTime hasta)
    {
        var ini = Db.InicioDia(desde); var fin = Db.InicioDia(hasta.AddDays(1));
        var movs = db.Q("SELECT usuario, tipo, estado, hora_servidor FROM movimientos WHERE hora_servidor >= $1 AND hora_servidor < $2", ini, fin);
        var conteos = db.Q("SELECT usuario, tarea_id, posicion, novedad, hora FROM conteo_lineas WHERE hora >= $1 AND hora < $2", ini, fin);
        var picks = db.Q("SELECT usuario, op, tipo, cantidad, hora FROM picking_eventos WHERE hora >= $1 AND hora < $2 ORDER BY hora", ini, fin);
        var controles = db.Q("SELECT usuario, nro, bultos, completo, hora FROM control_pedidos WHERE hora >= $1 AND hora < $2", ini, fin);
        var usuarios = db.Q("SELECT usuario, nombre, rol FROM usuarios").ToDictionary(u => u.S("usuario")!, u => u);
        var todos = movs.Select(m => m.S("usuario")!).Concat(conteos.Select(x => x.S("usuario")!)).Concat(picks.Select(x => x.S("usuario")!))
            .Concat(controles.Select(x => x.S("usuario")!)).Distinct();

        var lista = todos.Select(u =>
        {
            var msTodos = movs.Where(m => m.S("usuario") == u).ToList();
            var ms = msTodos.Where(m => !TiposPicker.Contains(m.S("tipo") ?? "")).ToList();   // movimientos de depósito
            var cs = conteos.Where(x => x.S("usuario") == u).ToList();
            var ps = picks.Where(x => x.S("usuario") == u).ToList();
            var ks = controles.Where(x => x.S("usuario") == u).ToList();
            int T(string tipo) => ms.Count(m => m.S("tipo") == tipo);

            var lineasPick = ps.Count(x => x.S("tipo") == "pick");
            var unidadesPick = ps.Where(x => x.S("tipo") == "pick").Sum(x => x.D("cantidad") ?? 0);
            var medidas = ps.Where(x => x.S("tipo") == "bultos").ToList();
            var solicitudesMedidas = medidas.Select(x => x.S("op")).Distinct().Count();
            var bultosMedidos = medidas.Sum(x => x.D("cantidad") ?? 0);
            var posContadas = cs.Select(x => $"{x.S("tarea_id")}|{x.S("posicion")}").Distinct().Count();
            var minPicking = MinutosActivos(ps.Where(x => x.S("tipo") != "bultos").ToList());
            var opsTerminadas = ps.Count(x => x.S("tipo") == "fin");

            var horas = msTodos.Select(m => Local(m.S("hora_servidor"))).Concat(cs.Select(x => Local(x.S("hora")))).Concat(ps.Select(x => Local(x.S("hora"))))
                .Concat(ks.Select(x => Local(x.S("hora")))).OrderBy(x => x).ToList();
            var activas = horas.Select(h => h.ToString("yyyyMMddHH")).Distinct().Count();
            var tareas = ms.Count + lineasPick + solicitudesMedidas + ks.Count + posContadas;
            return new
            {
                usuario = u, nombre = usuarios.GetValueOrDefault(u)?.S("nombre"), rol = usuarios.GetValueOrDefault(u)?.S("rol"),
                tareas,
                movimientos = ms.Count, transferencias = T("transferencia"), guardados = T("guardado"), cambios_estado = T("cambio_estado"),
                reubicar_cajas = T("reubicar_cajas"), ajustes = T("ajuste"), altas = T("alta"),
                aplicados = ms.Count(m => m.S("estado") == "aplicado"), errores = ms.Count(m => m.S("estado") is "error" or "rechazado"),
                // picking
                ops_picking = ps.Where(x => x.S("op") != null && x.S("tipo") != "bultos").Select(x => x.S("op")).Distinct().Count(), ops_terminadas = opsTerminadas,
                lineas_pick = lineasPick, unidades_pick = unidadesPick, errores_pick = ps.Count(x => x.S("tipo") is "error_lectura" or "rechazo_wms"),
                min_picking = Math.Round(minPicking, 0),
                lineas_hora = minPicking >= 5 ? Math.Round(lineasPick / (minPicking / 60), 1) : (double?)null,
                unidades_hora = minPicking >= 5 ? Math.Round(unidadesPick / (minPicking / 60), 0) : (double?)null,
                min_por_op = opsTerminadas > 0 ? Math.Round(minPicking / opsTerminadas, 1) : (double?)null,
                pausas = ps.Count(x => x.S("tipo") == "pausa"),
                // control de bulto (medición) y control de pedido
                solicitudes_medidas = solicitudesMedidas, bultos_medidos = bultosMedidos,
                pedidos_controlados = ks.Count, bultos_controlados = ks.Sum(x => x.I("bultos") ?? 0), pedidos_completos = ks.Count(x => x.B("completo")),
                // conteo
                posiciones_contadas = posContadas, lineas_contadas = cs.Count, diferencias = cs.Count(x => x.S("novedad") is not ("ok" or null)),
                primero = horas.Count > 0 ? horas[0].ToString("dd/MM HH:mm") : null, ultimo = horas.Count > 0 ? horas[^1].ToString("dd/MM HH:mm") : null,
                horas_activas = activas, por_hora = activas > 0 ? Math.Round((double)tareas / activas, 1) : 0,
            };
        }).OrderByDescending(x => x.tareas).ToList();

        // actividad por hora: cada tarea en su hora (movimiento de depósito, línea pickeada, medición, control, posición contada)
        var porHora = movs.Where(m => !TiposPicker.Contains(m.S("tipo") ?? "")).Select(m => Local(m.S("hora_servidor")).Hour)
            .Concat(conteos.Select(x => Local(x.S("hora")).Hour))
            .Concat(picks.Where(x => x.S("tipo") is "pick" or "bultos").Select(x => Local(x.S("hora")).Hour))
            .Concat(controles.Select(x => Local(x.S("hora")).Hour))
            .GroupBy(h => h).OrderBy(g => g.Key).Select(g => new { hora = g.Key, n = g.Count() });
        var pickers = lista.Where(x => x.lineas_pick > 0 || x.solicitudes_medidas > 0 || x.pedidos_controlados > 0).ToList();
        var minTot = pickers.Sum(x => x.min_picking);
        return new
        {
            desde = desde.ToString("yyyy-MM-dd"), hasta = hasta.ToString("yyyy-MM-dd"),
            totales = new
            {
                movimientos = lista.Sum(x => x.movimientos), operarios = lista.Count, errores = lista.Sum(x => x.errores), posiciones_contadas = lista.Sum(x => x.posiciones_contadas),
                lineas_pick = lista.Sum(x => x.lineas_pick), unidades_pick = lista.Sum(x => x.unidades_pick), ops_terminadas = lista.Sum(x => x.ops_terminadas),
                errores_pick = lista.Sum(x => x.errores_pick), solicitudes_medidas = lista.Sum(x => x.solicitudes_medidas), bultos_medidos = lista.Sum(x => x.bultos_medidos),
                pedidos_controlados = lista.Sum(x => x.pedidos_controlados), bultos_controlados = lista.Sum(x => x.bultos_controlados),
                pickers = pickers.Count, lineas_hora = minTot >= 5 ? Math.Round(lista.Sum(x => x.lineas_pick) / (minTot / 60), 1) : (double?)null,
                tareas = lista.Sum(x => x.tareas),
            },
            operarios = lista, porHora,
        };
    }

    /// <summary>Minutos de trabajo de picking: suma de los tramos entre eventos seguidos (sin contar pausas ni huecos largos).</summary>
    public static double MinutosActivos(List<Fila> eventos)
    {
        double total = 0;
        for (var i = 1; i < eventos.Count; i++)
        {
            if (eventos[i - 1].S("tipo") == "pausa") continue;          // de la pausa a la retoma no cuenta
            var gap = (Local(eventos[i].S("hora")) - Local(eventos[i - 1].S("hora"))).TotalMinutes;
            if (gap > 0 && gap <= MaxHuecoMin) total += gap;
        }
        return total;
    }

    /// <summary>Todo lo que hizo un operario en el rango, en orden (movimientos, picking, medición, control de pedido, conteo).</summary>
    public static List<Dictionary<string, object?>> Actividad(Db db, string usuario, DateTime desde, DateTime hasta)
    {
        var ini = Db.InicioDia(desde); var fin = Db.InicioDia(hasta.AddDays(1));
        var lista = new List<Dictionary<string, object?>>();
        foreach (var m in db.Q("""
            SELECT m.*, a.descripcion FROM movimientos m LEFT JOIN articulos a ON a.codigo = m.articulo
            WHERE m.usuario = $1 AND m.hora_servidor >= $2 AND m.hora_servidor < $3 ORDER BY m.hora_servidor DESC LIMIT 3000
            """, usuario, ini, fin))
            lista.Add(new()
            {
                ["hora"] = m.S("hora_servidor"), ["fuente"] = "movimiento", ["tipo"] = m.S("tipo"), ["contenedor"] = m.S("contenedor"), ["articulo"] = m.S("articulo"),
                ["descripcion"] = m.S("descripcion"), ["cantidad"] = m.D("cantidad"), ["origen"] = m.S("origen"), ["destino"] = m.S("destino"), ["estado"] = m.S("estado"),
                ["detalle"] = m.S("respuesta_wms") ?? m.S("comentario"), ["id"] = m.S("id"),
            });
        // del picking, lo que no es línea (las líneas ya están como movimiento "picking"): inicio, fin, pausas, errores evitados…
        var desde212 = lista.Any(x => (string?)x["tipo"] == "picking");
        foreach (var p in db.Q("SELECT * FROM picking_eventos WHERE usuario = $1 AND hora >= $2 AND hora < $3 ORDER BY hora DESC LIMIT 3000", usuario, ini, fin))
        {
            var t = p.S("tipo");
            if (t == "pick" && desde212 && lista.Any(x => (string?)x["tipo"] == "picking" && (string?)x["hora"] == p.S("hora"))) continue;
            if (t == "bultos" && lista.Any(x => (string?)x["tipo"] == "medicion" && (string?)x["contenedor"] == p.S("op"))) continue;
            lista.Add(new()
            {
                ["hora"] = p.S("hora"), ["fuente"] = "picking", ["tipo"] = t, ["contenedor"] = p.S("contenedor") ?? p.S("contenedor_picking"), ["articulo"] = p.S("articulo"),
                ["cantidad"] = p.D("cantidad"), ["origen"] = p.S("posicion"), ["destino"] = p.S("contenedor_picking"), ["estado"] = null,
                ["detalle"] = (p.S("op") is { } op ? "OP/Sol. " + op + " · " : "") + (p.S("detalle") ?? ""),
            });
        }
        foreach (var k in db.Q("SELECT * FROM control_pedidos WHERE usuario = $1 AND hora >= $2 AND hora < $3 ORDER BY hora DESC", usuario, ini, fin))
            if (!lista.Any(x => (string?)x["tipo"] == "control_pedido" && (string?)x["contenedor"] == k.S("nro") && (string?)x["hora"] == k.S("hora")))
                lista.Add(new()
                {
                    ["hora"] = k.S("hora"), ["fuente"] = "control", ["tipo"] = "control_pedido", ["contenedor"] = k.S("nro"), ["cantidad"] = k.D("cantidad"),
                    ["estado"] = k.B("completo") ? "completo" : "parcial", ["detalle"] = k.S("observacion"),
                });
        foreach (var x in db.Q("SELECT * FROM conteo_lineas WHERE usuario = $1 AND hora >= $2 AND hora < $3 ORDER BY hora DESC LIMIT 2000", usuario, ini, fin))
            lista.Add(new()
            {
                ["hora"] = x.S("hora"), ["fuente"] = "conteo", ["tipo"] = "conteo", ["contenedor"] = x.S("contenedor"), ["articulo"] = x.S("articulo"),
                ["cantidad"] = x.D("cantidad_contada"), ["origen"] = x.S("posicion"), ["estado"] = x.S("novedad"), ["detalle"] = x.S("cantidad_wms") is { } w ? "WMS: " + w : null,
            });
        return lista.OrderByDescending(x => (string?)x["hora"]).Take(5000).ToList();
    }
}
