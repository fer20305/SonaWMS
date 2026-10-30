using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sona.Les;

namespace Sona;

public sealed record BultoPedido(string Nombre, string Numero, double? Alto, double? Ancho, double? Profundidad)
{
    public double? M3 => Alto != null && Ancho != null && Profundidad != null ? Math.Round(Alto.Value * Ancho.Value * Profundidad.Value / 1_000_000, 3) : null;
}
public sealed record ArticuloPedido(string Articulo, string? Descripcion, double? Cantidad, string? Origen, List<string> Fotos)
{
    public string? Foto => Fotos.FirstOrDefault();
}
public sealed record ControlEntrada(double? Cantidad, int? Bultos, bool? Completo, string? Observacion, List<string>? Fotos, List<ArticuloControl>? Articulos);
public sealed record ArticuloControl(string Articulo, double? Cantidad);

/// <summary>
/// Consultar pedido / solicitud (ej. 10395-421) y control de pedido.
///   · Datos del pedido: buscador SONA (BOT_BusquedaPedido: cliente, proyecto, PDV, estado, viaje, bultos, fechas).
///   · Bultos y si está controlado: LES (Api/Bultos/GetBultos).
///   · Artículos con foto: los que informe el buscador (si trae el detalle), los que se cargaron en un control de pedido de SONA
///     y los que pickeó SONA para esa solicitud.
/// El control de pedido queda grabado (productividad del controlador) y, si está configurado, se avisa al grupo de Telegram con la foto.
/// </summary>
public sealed class Pedidos(Servicios sv, BultosLes bultosLes, HttpClient http, Eventos ev, ILogger<Pedidos> log)
{
    readonly Db _db = sv.Db;

    /// <summary>Un número de solicitud tiene la forma NNNNN-NNN (proyecto-pedido).</summary>
    public static bool PareceSolicitud(string t) => Regex.IsMatch(t.Trim(), @"^\d{3,7}-\d{1,5}$");

    static readonly Regex ReBulto = new(@"(B\d+)\s*-\s*(\d+)\s*-\s*([\d.,]+)\s*cm\s*x\s*([\d.,]+)\s*cm\s*x\s*([\d.,]+)\s*cm", RegexOptions.IgnoreCase);
    static double? Num(string s) => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static List<BultoPedido> LeerBultos(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? new() : ReBulto.Matches(texto).Select(m => new BultoPedido(m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value,
            Num(m.Groups[3].Value), Num(m.Groups[4].Value), Num(m.Groups[5].Value))).ToList();

    /// <summary>Códigos de artículo que aparezcan en los campos de detalle del buscador (si el pedido los trae).</summary>
    static IEnumerable<(string Codigo, double? Cantidad)> ArticulosEnTexto(Dictionary<string, string> campos)
    {
        foreach (var (k, v) in campos)
        {
            if (!Regex.IsMatch(k, "ARTIC|ITEM|DETALLE|MATERIAL|SKU|PRODUCT|COMPONE")) continue;
            foreach (var parte in Regex.Split(v, @"//|;|\|"))
            {
                var m = Regex.Match(parte, @"\b([A-Z]{2,}[A-Z0-9-]*\d[A-Z0-9-]*)\b(?:.*?\b(?:x|cant\.?:?|cantidad:?)\s*([\d.,]+))?", RegexOptions.IgnoreCase);
                if (m.Success) yield return (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Success ? Num(m.Groups[2].Value) : null);
            }
        }
    }

    /// <summary>
    /// Pedido por número de OP (orden de preparación del WMS, ej. 2055): los artículos que SONA vio en la OP al pickear
    /// (cantidad pedida, ubicación) y lo que se pickeó de cada uno (quién, cuánto, en qué contenedor), con las fotos.
    /// null si SONA no tiene picking de esa OP.
    /// </summary>
    public async Task<object?> ConsultarOp(string op)
    {
        var o = op.Trim().TrimStart('0');
        if (o.Length == 0) return null;
        var items = _db.Q("SELECT * FROM op_items WHERE op=$1 ORDER BY orden", o);
        var picks = _db.Q("SELECT * FROM movimientos WHERE tipo='picking' AND grupo=$1 ORDER BY hora_servidor", "OP-" + o);
        if (items.Count == 0 && picks.Count == 0) return null;
        var eventos = _db.Q("SELECT tipo, usuario, hora, detalle FROM picking_eventos WHERE op=$1 ORDER BY hora", o);
        var codigos = items.Select(i => i.S("articulo")!).Concat(picks.Select(x => x.S("articulo")).Where(x => x != null).Select(x => x!)).Distinct().ToList();
        var articulos = new List<object>();
        foreach (var cod in codigos.Take(80))
        {
            var it = items.Where(i => i.S("articulo") == cod).ToList();
            var ps = picks.Where(x => x.S("articulo") == cod).ToList();
            var desc = it.Select(i => i.S("descripcion")).FirstOrDefault(d => d != null) ?? _db.Q1("SELECT descripcion FROM articulos WHERE codigo=$1", cod)?.S("descripcion");
            List<string> fotos = new();
            try { fotos = (await sv.FotosArticulo(cod, it.FirstOrDefault()?.I("empresa"))).Select(f => Servicios.UrlImagen(f.Url)).ToList(); } catch { }
            var pedida = it.Sum(i => i.D("cantidad") ?? 0);
            var pickeada = ps.Sum(x => x.D("cantidad") ?? 0);
            articulos.Add(new
            {
                articulo = cod, descripcion = desc, cantidad = it.Count > 0 ? pedida : (double?)null, pickeada,
                ubicacion = it.Select(i => i.S("ubicacion")).FirstOrDefault(u => u != null) ?? ps.Select(x => x.S("origen")).FirstOrDefault(),
                completo = it.Count > 0 && pickeada >= pedida - 1e-6,
                pickers = ps.Select(x => x.S("usuario")).Distinct().ToList(), contenedores = ps.Select(x => x.S("destino")).Where(x => x != null).Distinct().ToList(),
                origen = "picking", fotos, foto = fotos.FirstOrDefault(),
            });
        }
        var empresa = items.Select(i => i.I("empresa")).FirstOrDefault(e => e != null) ?? picks.Select(x => x.I("empresa")).FirstOrDefault(e => e != null);
        return new
        {
            tipo = "op", op = o, empresa, empresaNombre = sv.NombreEmpresa(empresa), destino = items.Select(i => i.S("destino")).FirstOrDefault(d => d != null),
            modo = items.Select(i => i.S("modo")).FirstOrDefault(),
            pickers = picks.Select(x => x.S("usuario")).Concat(eventos.Select(e => e.S("usuario"))).Where(x => x != null).Distinct().ToList(),
            inicio = eventos.FirstOrDefault(e => e.S("tipo") == "inicio")?.S("hora") ?? picks.FirstOrDefault()?.S("hora_servidor"),
            fin = eventos.LastOrDefault(e => e.S("tipo") == "fin")?.S("hora"),
            isla = eventos.LastOrDefault(e => e.S("tipo") is "isla" or "fin")?.S("detalle"),
            lineas = picks.Count, unidades = picks.Sum(x => x.D("cantidad") ?? 0), articulos,
        };
    }

    public async Task<object> Consultar(string nro)
    {
        var n = nro.Trim().ToUpperInvariant();
        // número de OP (solo dígitos, corto): si SONA pickeó esa OP, se muestra con sus artículos
        if (Regex.IsMatch(n, @"^\d{1,6}$") && await ConsultarOp(n) is { } op) return op;
        List<Registro> regs = new();
        try { regs = await sv.Buscador.Buscar("BOT_BusquedaPedido", n); }
        catch (Exception e) { log.LogWarning("Buscador SONA (pedido {N}): {E}", n, e.Message); }
        // un número de bulto (etiqueta de medida) también lleva al pedido
        if (regs.Count == 0 && Regex.IsMatch(n, @"^\d{6,8}$"))
            try { regs = await sv.Buscador.Buscar("BOT_BusquedaBulto", n); } catch { }
        var campos = regs.FirstOrDefault()?.Campos ?? new Dictionary<string, string>();
        string? C(params string[] ks) => ks.Select(k => campos.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
        var solicitud = C("NROSOLICITUD", "SOLICITUD", "PEDIDO") ?? n;

        BultosLes.Pedido? les = null; string? avisoLes = null;
        try { les = await bultosLes.Buscar(solicitud); }
        catch (Exception e) { avisoLes = e.Message; }
        if (campos.Count == 0 && les == null) throw new ErrorUsuario($"No se encontró el pedido {n} (buscador SONA ni LES).", 404);

        // artículos: buscador (si trae detalle) + controles de SONA + picking de SONA para esa solicitud
        var arts = new Dictionary<string, (double? Cant, string Origen)>();
        foreach (var (cod, cant) in ArticulosEnTexto(campos)) arts.TryAdd(cod, (cant, "buscador"));
        foreach (var k in _db.Q("SELECT detalle FROM control_pedidos WHERE nro=$1 ORDER BY id DESC", solicitud))
            foreach (var a in (k["detalle"] as JsonObject)?["articulos"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                if (a["articulo"]?.ToString() is { Length: > 0 } cod) arts.TryAdd(cod.ToUpperInvariant(), (a["cantidad"] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null, "control"));
        // OPs del WMS cuyo destino menciona la solicitud
        foreach (var m in _db.Q("SELECT articulo, sum(cantidad) AS cantidad FROM op_items WHERE destino LIKE $1 GROUP BY articulo", "%" + solicitud + "%"))
            arts.TryAdd(m.S("articulo")!, (m.D("cantidad"), "picking"));
        var articulos = new List<ArticuloPedido>();
        foreach (var (cod, (cant, origen)) in arts.Take(60))
        {
            var desc = _db.Q1("SELECT descripcion FROM articulos WHERE codigo=$1", cod)?.S("descripcion");
            List<string> fotos = new();
            try { fotos = (await sv.FotosArticulo(cod)).Select(f => Servicios.UrlImagen(f.Url)).ToList(); } catch { }
            if (desc == null) try { desc = (await sv.LeerFichaLes(cod))?.Descripcion; } catch { }
            articulos.Add(new ArticuloPedido(cod, desc, cant, origen, fotos));
        }

        var bultos = LeerBultos(C("BULTOS"));
        if (bultos.Count == 0 && les != null)
            bultos = les.Bultos.Select((b, i) => new BultoPedido("B" + (i + 1), b.NroContenedor.ToString(CultureInfo.InvariantCulture), b.Alto, b.Ancho, b.Profundidad)).ToList();
        var controles = _db.Q("SELECT id, usuario, cantidad, bultos, completo, observacion, fotos, hora, telegram FROM control_pedidos WHERE nro=$1 ORDER BY id DESC LIMIT 20", solicitud);
        var datos = campos.Where(kv => kv.Key is not ("BULTOS" or "VALORBUSQUEDA")).Select(kv => new { campo = Lindo(kv.Key), valor = kv.Value }).ToList();
        return new
        {
            tipo = "pedido", nroSolicitud = solicitud, cliente = C("CLIENTE") ?? les?.Cliente, proyecto = C("PROYECTO") ?? les?.Proyecto,
            categoria = C("CATEGORIA"), pdv = C("PDV"), distrito = C("DISTRITO"), estado = C("ESTADO"), viaje = C("VIAJE"),
            fechaRealizacion = C("FECHAREALIZACION"), fechaSalida = C("FECHASALIDA"), preparado = C("PREPARADO"), despachado = C("DESPACHADO"),
            fechaAlta = C("FECHAALTA"), fechaConfirmacion = C("FECHACONFIRMACION"), fechaPreparacion = C("FECHAPREPARACION"),
            controlado = les?.Controlado, usuarioControl = les?.UsuarioControl, fechaControl = les?.FechaControl, avisoLes,
            bultos, m3 = bultos.Sum(b => b.M3 ?? 0), articulos, datos, controles,
            avisoArticulos = articulos.Count == 0 ? "El buscador no informa los artículos de este pedido: aparecen cuando se cargan en un control de pedido o se pickean con SONA." : null,
        };
    }

    static string Lindo(string k) => k switch
    {
        "NROSOLICITUD" => "Solicitud", "FECHAREALIZACION" => "Fecha de realización", "FECHASALIDA" => "Fecha de salida", "FECHAALTA" => "Alta",
        "FECHACONFIRMACION" => "Confirmación", "FECHAPREPARACION" => "Preparación", "PDV" => "PDV",
        _ => k.Length > 1 ? k[0] + k[1..].ToLowerInvariant() : k,
    };

    /// <summary>Control de pedido hecho en SONA: queda grabado (y como movimiento) y se avisa al grupo de Telegram si está configurado.</summary>
    public async Task<object> Controlar(string usuario, string nro, ControlEntrada b, string carpetaFotos)
    {
        var n = nro.Trim().ToUpperInvariant();
        if (b.Completo != true && string.IsNullOrWhiteSpace(b.Observacion)) throw new ErrorUsuario("Tildá \"pedido completo\" o escribí la observación.");
        if (b.Cantidad is < 0 || b.Bultos is < 0) throw new ErrorUsuario("Las cantidades no pueden ser negativas.");
        var fotos = (b.Fotos ?? new()).Where(f => f.Length > 0).Select(f => Path.GetFileName(f)).Where(f => f.Length > 0).ToList();
        var articulos = (b.Articulos ?? new()).Where(a => !string.IsNullOrWhiteSpace(a.Articulo))
            .Select(a => (JsonNode)new JsonObject { ["articulo"] = a.Articulo.Trim().ToUpperInvariant(), ["cantidad"] = a.Cantidad }).ToArray();
        var hora = Db.Ahora();
        var detalle = new JsonObject { ["articulos"] = new JsonArray(articulos) };
        var id = Convert.ToInt64(_db.Escalar("""
            INSERT INTO control_pedidos (nro, usuario, cantidad, bultos, completo, observacion, fotos, detalle, hora) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9) RETURNING id
            """, n, usuario, b.Cantidad, b.Bultos, b.Completo == true, b.Observacion?.Trim(), fotos, detalle, hora));
        var idMov = Guid.NewGuid().ToString();
        _db.Ejecutar("""
            INSERT INTO movimientos (id, grupo, tipo, contenedor, cantidad, usuario, hora_servidor, estado, comentario, fotos, detalle, aplicado_en)
            VALUES ($1,$2,'control_pedido',$3,$4,$5,$6,'registrado',$7,$8,$9,$6)
            """, idMov, "SOL-" + n, n, b.Cantidad, usuario, hora,
            (b.Completo == true ? "Pedido completo" : "Con observación") + (string.IsNullOrWhiteSpace(b.Observacion) ? "" : ": " + b.Observacion.Trim()),
            fotos, new JsonObject { ["control"] = id, ["bultos"] = b.Bultos, ["completo"] = b.Completo == true });
        ev.Emitir(new() { ["tipo"] = "movimiento", ["id"] = idMov, ["estado"] = "registrado", ["usuario"] = usuario }, (u, rol) => Seguridad.RolesLectura.Contains(rol));
        ev.Emitir(new() { ["tipo"] = "picking", ["usuario"] = usuario, ["evento"] = "control", ["op"] = n }, (u, rol) => Seguridad.RolesLectura.Contains(rol));

        var telegram = await AvisarTelegram(usuario, n, b, fotos.Select(f => Path.Combine(carpetaFotos, f)).Where(File.Exists).ToList());
        _db.Ejecutar("UPDATE control_pedidos SET telegram=$2 WHERE id=$1", id, telegram);
        return new { ok = true, id, telegram };
    }

    /// <summary>Manda el control al grupo de Telegram (parámetros telegram_token y telegram_chat en ZPAR). Si no está configurado, no hace nada.</summary>
    async Task<string> AvisarTelegram(string usuario, string nro, ControlEntrada b, List<string> fotos)
    {
        var token = _db.Parametro("telegram_token", "").Trim();
        var chat = _db.Parametro("telegram_chat", "").Trim();
        if (token.Length == 0 || chat.Length == 0) return "sin configurar";
        var texto = $"✅ Control de pedido {nro}\nControló: {usuario}\n" +
                    (b.Cantidad != null ? $"Cantidad controlada: {b.Cantidad:0.##}\n" : "") + (b.Bultos != null ? $"Bultos: {b.Bultos}\n" : "") +
                    (b.Completo == true ? "Pedido completo ✔" : "Observación: " + b.Observacion);
        try
        {
            var url = $"https://api.telegram.org/bot{token}/";
            HttpResponseMessage r;
            if (fotos.Count > 0)
            {
                using var form = new MultipartFormDataContent { { new StringContent(chat), "chat_id" }, { new StringContent(texto), "caption" } };
                var bytes = await File.ReadAllBytesAsync(fotos[0]);
                form.Add(new ByteArrayContent(bytes), "photo", Path.GetFileName(fotos[0]));
                r = await http.PostAsync(url + "sendPhoto", form);
            }
            else r = await http.PostAsJsonAsync(url + "sendMessage", new { chat_id = chat, text = texto });
            var t = await r.Content.ReadAsStringAsync();
            return r.IsSuccessStatusCode ? "enviado" : "error " + (int)r.StatusCode + ": " + t[..Math.Min(150, t.Length)];
        }
        catch (Exception e) { log.LogWarning("Telegram: {E}", e.Message); return "error: " + e.Message; }
    }
}
