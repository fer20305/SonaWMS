using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sona.Wms;

namespace Sona;

/// <summary>
/// Flujo de documentos (como en SAP): a partir de una OP, una solicitud, un remito o un número de bulto arma la cadena del pedido:
/// Solicitud (LES) → Orden de preparación (WMS) → Asignación y carátula → Picking (con faltantes y postergaciones) →
/// Control de pedido → Bultos y etiquetas de medida → Despacho. Cada documento con su estado, fecha y usuario.
/// </summary>
public sealed class FlujoDocumentos(Servicios sv, Produccion prod, Pedidos pedidos, ILogger<FlujoDocumentos> log)
{
    readonly Db _db = sv.Db;

    /// <summary>Un documento del flujo. Estado: ok (terminado), curso (en proceso), pendiente, aviso (con problemas), error.</summary>
    public sealed class Doc
    {
        public string Tipo { get; set; } = "";
        public string Titulo { get; set; } = "";
        public string? Numero { get; set; }
        public string Estado { get; set; } = "pendiente";
        public string? EstadoTexto { get; set; }
        public string? Fecha { get; set; }
        public string? Usuario { get; set; }
        public List<object> Datos { get; set; } = new();
        public List<Doc> Hijos { get; set; } = new();
        public Doc Dato(string campo, object? valor) { if (valor is not null && !(valor is string s && string.IsNullOrWhiteSpace(s))) Datos.Add(new { campo, valor }); return this; }
    }

    static readonly Regex ReSolicitud = new(@"^\d{2,6}-\d{1,5}$");

    public async Task<object> Armar(string usuario, string texto)
    {
        var t = texto.Trim().ToUpperInvariant().Replace("REM:", "").Trim();
        if (t.Length == 0) throw new ErrorUsuario("Escribí una OP, una solicitud (ej. 9117-166), un remito o un número de bulto.");

        // OPs del WMS en las bandejas (lo último leído; no se fuerza la lectura)
        List<OpWms> bandejas = new();
        try { bandejas = await prod.OpsEnBandejas(usuario); } catch (Exception e) { log.LogWarning("Flujo: no se pudieron leer las bandejas del WMS: {E}", e.Message); }

        // 1) a qué solicitud y OPs se refiere lo escrito
        string? solicitud = null, remito = null;
        var ops = new HashSet<string>();
        void SumarOp(string? op) { if (!string.IsNullOrWhiteSpace(op)) ops.Add(op.Trim().TrimStart('0')); }
        if (ReSolicitud.IsMatch(t)) solicitud = t;
        else if (Regex.IsMatch(t, @"^\d{1,6}$"))
        {
            // número corto: OP (si SONA la conoce); si no, puede ser un remito
            var conocida = bandejas.Any(o => o.Op.TrimStart('0') == t.TrimStart('0'))
                || _db.Q1("SELECT 1 FROM produccion_caratulas WHERE op=$1 UNION SELECT 1 FROM picking_eventos WHERE op=$1 UNION SELECT 1 FROM op_items WHERE op=$1 LIMIT 1", t.TrimStart('0')) != null;
            if (conocida) SumarOp(t);
            else remito = t;
        }
        else if (Regex.IsMatch(t, @"^\d{7,8}$"))
        {
            // número de bulto (etiqueta de medida): el buscador dice de qué solicitud es
            try { var r = await sv.Buscador.Buscar("BOT_BusquedaBulto", t); solicitud = r.FirstOrDefault()?.Campos.GetValueOrDefault("NROSOLICITUD")?.Trim(); } catch { }
            if (solicitud == null) remito = t;
        }
        else remito = Regex.Match(t, @"\d+").Value is { Length: > 0 } d ? d : t;

        // remito → OPs cuyo destino lo menciona → solicitud
        if (remito != null)
        {
            foreach (var o in bandejas.Where(o => o.Remito == remito)) { SumarOp(o.Op); solicitud ??= o.Solicitud; }
            foreach (var f in _db.Q("SELECT op, solicitud, destino FROM produccion_caratulas WHERE destino LIKE $1", $"%Rem:{remito}%")) { SumarOp(f.S("op")); solicitud ??= f.S("solicitud") ?? SesionEscritorio.Partes(f.S("destino")).Solicitud; }
            foreach (var f in _db.Q("SELECT DISTINCT op, destino FROM op_items WHERE destino LIKE $1", $"%Rem:{remito}%")) { SumarOp(f.S("op")); solicitud ??= SesionEscritorio.Partes(f.S("destino")).Solicitud; }
        }
        // OP → solicitud
        foreach (var op in ops.ToList())
        {
            var b = bandejas.FirstOrDefault(o => o.Op.TrimStart('0') == op);
            solicitud ??= b?.Solicitud ?? _db.Q1("SELECT solicitud FROM produccion_caratulas WHERE op=$1 AND solicitud IS NOT NULL ORDER BY id DESC LIMIT 1", op)?.S("solicitud")
                ?? SesionEscritorio.Partes(_db.Q1("SELECT destino FROM op_items WHERE op=$1 AND destino IS NOT NULL LIMIT 1", op)?.S("destino")).Solicitud;
            remito ??= b?.Remito ?? SesionEscritorio.Partes(_db.Q1("SELECT destino FROM produccion_caratulas WHERE op=$1 ORDER BY id DESC LIMIT 1", op)?.S("destino")).Remito;
        }
        // solicitud → todas sus OPs
        if (solicitud != null)
        {
            foreach (var o in bandejas.Where(o => o.Solicitud == solicitud)) SumarOp(o.Op);
            foreach (var f in _db.Q("SELECT DISTINCT op FROM produccion_caratulas WHERE solicitud=$1 OR destino LIKE $2", solicitud, $"%{solicitud}%")) SumarOp(f.S("op"));
            foreach (var f in _db.Q("SELECT DISTINCT op FROM op_items WHERE destino LIKE $1", $"%{solicitud}%")) SumarOp(f.S("op"));
        }
        if (solicitud == null && ops.Count == 0) throw new ErrorUsuario($"No se encontró nada para {t}: probá con la OP, la solicitud (ej. 9117-166) o el remito.", 404);

        // 2) datos de la solicitud (buscador SONA + LES): fechas, control, bultos, despacho
        JsonObject? ped = null;
        if (solicitud != null)
            try { ped = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(await pedidos.Consultar(solicitud), Motor.Json)) as JsonObject; }
            catch (Exception e) { log.LogInformation("Flujo: sin datos del pedido {S}: {E}", solicitud, e.Message); }
        string? P(string k) => ped?[k] is JsonValue v ? v.ToString() : null;

        var raiz = new Doc
        {
            Tipo = "solicitud", Titulo = "Solicitud (pedido LES)", Numero = solicitud ?? "—", Fecha = P("fechaAlta"),
            Estado = P("despachado") is { Length: > 0 } dsp && !dsp.StartsWith("N", StringComparison.OrdinalIgnoreCase) ? "ok" : solicitud == null ? "aviso" : "curso",
            EstadoTexto = P("estado") ?? (solicitud == null ? "No se pudo saber la solicitud de la OP" : null),
        };
        raiz.Dato("Cliente", P("cliente")).Dato("Proyecto", P("proyecto")).Dato("PDV", P("pdv")).Dato("Remito", remito).Dato("Viaje", P("viaje"))
            .Dato("Categoría", P("categoria")).Dato("Distrito", P("distrito")).Dato("Alta", P("fechaAlta")).Dato("Confirmación", P("fechaConfirmacion"))
            .Dato("Fecha de realización", P("fechaRealizacion"));

        // 3) una rama por cada OP
        foreach (var op in ops.OrderBy(o => o.PadLeft(8, '0')))
            raiz.Hijos.Add(RamaOp(op, bandejas.Where(o => o.Op.TrimStart('0') == op).ToList()));
        if (ops.Count == 0)
            raiz.Hijos.Add(new Doc { Tipo = "op", Titulo = "Orden de preparación (WMS)", Estado = "pendiente", EstadoTexto = "SONA no tiene OPs de esta solicitud (todavía no se vio en las bandejas del WMS)" });

        // 4) control de pedido (SONA y LES)
        var controles = _db.Q("SELECT usuario, cantidad, bultos, completo, observacion, hora FROM control_pedidos WHERE nro=$1 ORDER BY id", solicitud ?? "-");
        var ctl = new Doc
        {
            Tipo = "control", Titulo = "Control de pedido", Numero = controles.Count > 0 ? $"{controles.Count} control(es)" : null,
            Estado = P("controlado") == "true" || controles.Any(c => c.B("completo")) ? "ok" : controles.Count > 0 ? "aviso" : "pendiente",
            EstadoTexto = P("controlado") == "true" ? "Controlado en LES" : controles.Count > 0 ? "Controlado en SONA (incompleto)" : "Sin controlar",
            Fecha = P("fechaControl") ?? controles.LastOrDefault()?.S("hora"), Usuario = P("usuarioControl") ?? controles.LastOrDefault()?.S("usuario"),
        };
        foreach (var c in controles)
            ctl.Hijos.Add(new Doc { Tipo = "control_sona", Titulo = "Control en SONA", Estado = c.B("completo") ? "ok" : "aviso", EstadoTexto = c.B("completo") ? "Completo" : "Con diferencias",
                Fecha = c.S("hora"), Usuario = c.S("usuario") }.Dato("Cantidad", c.D("cantidad")).Dato("Bultos", c.L("bultos")).Dato("Observación", c.S("observacion")));
        raiz.Hijos.Add(ctl);

        // 5) bultos y etiquetas de medida (LES)
        var bultos = ped?["bultos"]?.AsArray().OfType<JsonObject>().ToList() ?? new();
        var medicion = _db.Q("SELECT usuario, cantidad, hora_servidor AS hora, comentario FROM movimientos WHERE tipo='medicion' AND contenedor=$1 ORDER BY hora_servidor", solicitud ?? "-");
        var bul = new Doc
        {
            Tipo = "bultos", Titulo = "Bultos / etiquetas de medida", Numero = bultos.Count > 0 ? $"{bultos.Count} bulto(s)" : null,
            Estado = bultos.Count > 0 ? "ok" : "pendiente", EstadoTexto = bultos.Count > 0 ? $"{P("m3")} m³" : "Sin bultos medidos",
            Fecha = medicion.LastOrDefault()?.S("hora"), Usuario = medicion.LastOrDefault()?.S("usuario"),
        };
        foreach (var b in bultos)
            bul.Hijos.Add(new Doc { Tipo = "bulto", Titulo = "Bulto", Numero = b["contenedor"]?.ToString() ?? b["nro"]?.ToString(), Estado = "ok",
                EstadoTexto = $"{b["alto"]} × {b["ancho"]} × {b["profundidad"]} cm" });
        raiz.Hijos.Add(bul);

        // 6) despacho (buscador SONA)
        var desp = P("despachado");
        raiz.Hijos.Add(new Doc
        {
            Tipo = "despacho", Titulo = "Despacho", Numero = P("viaje") is { Length: > 0 } vj ? "Viaje " + vj : null,
            Estado = desp is { Length: > 0 } && !desp.StartsWith("N", StringComparison.OrdinalIgnoreCase) ? "ok" : P("preparado") is { Length: > 0 } pr && !pr.StartsWith("N", StringComparison.OrdinalIgnoreCase) ? "curso" : "pendiente",
            EstadoTexto = desp is { Length: > 0 } && !desp.StartsWith("N", StringComparison.OrdinalIgnoreCase) ? "Despachado" : "Sin despachar",
            Fecha = P("fechaSalida"),
        }.Dato("Preparado", P("preparado")).Dato("Despachado", desp).Dato("Fecha de preparación", P("fechaPreparacion")).Dato("Fecha de salida", P("fechaSalida")));

        return new { busqueda = t, solicitud, remito, ops = ops.ToList(), flujo = raiz, avisoLes = P("avisoLes") };
    }

    /// <summary>Rama de una OP: la orden del WMS, su asignación (carátulas), el picking (con faltantes y postergaciones).</summary>
    Doc RamaOp(string op, List<OpWms> enWms)
    {
        var w = enWms.FirstOrDefault();
        var cars = _db.Q("SELECT * FROM produccion_caratulas WHERE op=$1 ORDER BY id", op);
        var evs = _db.Q("SELECT usuario, tipo, articulo, cantidad, detalle, hora FROM picking_eventos WHERE op=$1 ORDER BY hora", op);
        var faltas = _db.Q("SELECT * FROM faltantes WHERE op=$1 ORDER BY id", op);
        var items = _db.Q("SELECT count(*) AS n, sum(cantidad) AS u, max(destino) AS destino, max(empresa) AS empresa FROM op_items WHERE op=$1", op);
        var picks = _db.Q1("SELECT count(*) AS n, sum(cantidad) AS u, min(hora_servidor) AS desde, max(hora_servidor) AS hasta FROM movimientos WHERE tipo='picking' AND grupo=$1", "OP-" + op);
        var fin = evs.LastOrDefault(e => e.S("tipo") == "fin");
        var inicio = evs.FirstOrDefault(e => e.S("tipo") == "inicio");
        var it = items.FirstOrDefault();

        string estadoOp; string textoOp;
        if (fin != null) { estadoOp = "ok"; textoOp = "Preparada"; }
        else if (w != null && !string.IsNullOrEmpty(w.Picker)) { estadoOp = w.Comenzada ? "curso" : "pendiente"; textoOp = w.Comenzada ? $"En preparación{(w.Avance is int a ? $" ({a}%)" : "")}" : "Asignada, sin empezar"; }
        else if (w != null) { estadoOp = "pendiente"; textoOp = "Pendiente de aceptar / asignar"; }
        else if (inicio != null) { estadoOp = "curso"; textoOp = "En preparación"; }
        else { estadoOp = cars.Count > 0 ? "curso" : "pendiente"; textoOp = cars.Count > 0 ? "Asignada" : "Sin datos del WMS"; }
        if (faltas.Any(f => f.S("estado") == "abierto")) { estadoOp = "aviso"; textoOp += " · stand by por faltante"; }

        var raiz = new Doc
        {
            Tipo = "op", Titulo = "Orden de preparación (WMS)", Numero = op, Estado = estadoOp, EstadoTexto = textoOp,
            Fecha = w?.Fecha ?? cars.FirstOrDefault()?.S("hora"),
        };
        raiz.Dato("Tipo", w?.Tipo ?? cars.FirstOrDefault()?.S("tipo")).Dato("Empresa", w?.EmpresaNombre ?? cars.FirstOrDefault()?.S("empresa_nombre") ?? sv.NombreEmpresa((int?)it?.L("empresa")))
            .Dato("Destino", w?.Destino ?? cars.FirstOrDefault()?.S("destino") ?? it?.S("destino")).Dato("Viaje", w?.Viaje ?? cars.FirstOrDefault()?.S("viaje"))
            .Dato("Artículos", w?.Articulos ?? (it?.L("n") > 0 ? it.L("n") : null)).Dato("Unidades", w?.Unidades ?? it?.D("u")).Dato("m³", w?.M3).Dato("Kg", w?.Kg)
            .Dato("Prioridad", w?.Prioridad is int pr ? prod.NivelDe(pr) : null);

        // asignación y carátula (una por cada asignación / reasignación)
        var ultimaOk = cars.LastOrDefault(c => c.S("estado") is "asignada" or "reasignada");
        foreach (var c in cars)
            raiz.Hijos.Add(new Doc
            {
                Tipo = "asignacion", Titulo = c.S("estado") switch { "reasignada" => "Reasignación y carátula", "reimpresion" => "Reimpresión de carátula", "error" => "Asignación (falló)", _ => "Asignación y carátula" },
                Numero = c.S("picker_nombre") ?? c.S("picker"),
                Estado = c.S("estado") is "asignada" or "reasignada" or "reimpresion" ? "ok" : c.S("estado") == "error" ? "error" : "aviso", EstadoTexto = c.S("mensaje") is { Length: > 0 } msj ? msj : c.S("estado") is "asignada" or "reasignada" && !ReferenceEquals(c, ultimaOk) ? "Reemplazada por una reasignación" : c.S("estado") is "asignada" or "reasignada" ? "Vigente" : c.S("estado"),
                Fecha = c.S("hora"), Usuario = c.S("asignado_por"),
            }.Dato("Picker", c.S("picker_nombre") ?? c.S("picker")).Dato("Renglones", c.L("renglones")).Dato("Con faltante (simulación)", c.B("con_faltante") ? "Sí" : null)
             .Dato("Impresiones", c.L("impresiones")).Dato("Prioridad", c.S("prioridad")).Dato("Error de impresión", c.S("error_impresion")));
        if (cars.Count == 0 && w?.Picker is { Length: > 0 } pk)
            raiz.Hijos.Add(new Doc { Tipo = "asignacion", Titulo = "Asignación", Numero = pk, Estado = "ok", EstadoTexto = "Asignada en el WMS (fuera de SONA)" });

        // picking
        if (inicio != null || (picks?.L("n") ?? 0) > 0 || evs.Count > 0)
        {
            var pickers = evs.Select(e => e.S("usuario")).Where(u => u != null).Distinct().ToList();
            var dp = new Doc
            {
                Tipo = "picking", Titulo = "Picking", Numero = $"{picks?.L("n") ?? 0} línea(s)", Estado = fin != null ? "ok" : "curso",
                EstadoTexto = fin != null ? "Terminado" + (fin.S("detalle") is { Length: > 0 } isla ? " · " + isla : "") : "En curso",
                Fecha = inicio?.S("hora") ?? picks?.S("desde"), Usuario = string.Join(", ", pickers),
            };
            dp.Dato("Inicio", inicio?.S("hora") ?? picks?.S("desde")).Dato("Fin", fin?.S("hora")).Dato("Unidades pickeadas", picks?.D("u"))
              .Dato("Contenedor de picking", evs.Select(e => e.S("detalle")).LastOrDefault(d => d != null && d.StartsWith("contenedor", StringComparison.OrdinalIgnoreCase)));
            foreach (var e in evs.Where(e => e.S("tipo") is "postergar" or "pausa" or "retoma" or "cerrar" or "aviso_aceptado"))
                dp.Hijos.Add(new Doc
                {
                    Tipo = "evento", Titulo = e.S("tipo") switch { "postergar" => "Postergada", "pausa" => "Pausa", "retoma" => "Retomada", "cerrar" => "Contenedor cerrado", _ => "Aviso del WMS aceptado" },
                    Estado = e.S("tipo") == "postergar" ? "aviso" : "ok", EstadoTexto = e.S("detalle"), Fecha = e.S("hora"), Usuario = e.S("usuario"),
                });
            raiz.Hijos.Add(dp);
        }
        foreach (var f in faltas)
            raiz.Hijos.Add(new Doc
            {
                Tipo = "faltante", Titulo = "Faltante", Numero = $"N° {f.L("id")} · {f.S("articulo")}", Estado = f.S("estado") == "abierto" ? "error" : "ok",
                EstadoTexto = f.S("estado") == "abierto" ? "Abierto (OP en stand by)" : $"Atendido por {f.S("atendido_por")}", Fecha = f.S("hora"), Usuario = f.S("usuario"),
            }.Dato("Artículo", $"{f.S("articulo")} {f.S("descripcion")}").Dato("Cantidad", f.D("cantidad")).Dato("Posición", f.S("posicion")).Dato("Comentario", f.S("comentario"))
             .Dato("Atendido", f.S("atendido_en")).Dato("Nota", f.S("nota")));
        return raiz;
    }
}
