using System.Net;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sona.Les;
using Sona.Wms;

namespace Sona;

/// <summary>
/// Módulo de Ingreso: una sola ventana para la recepción de mercadería.
/// Trae la precarga del LES (Ingresos Pendientes) por N° de recepción, carga lo que pide el LES (descarga, pallets, fotos,
/// remito firmado, foto POP), guarda todo en SONA y con eso arma la recepción en el WMS (artículos, recepción por contenedor,
/// contenedores) y cierra en el LES. Cada paso queda registrado y se puede retomar.
/// </summary>
public sealed class Ingresos(Configuracion cfg, Servicios sv, IngresosLes etiquetas, Eventos ev, MisCredenciales cred, ILogger<Ingresos> log)
{
    public const string Esquema = """
CREATE TABLE IF NOT EXISTS ingresos (
  les_id INTEGER PRIMARY KEY, nro_recepcion TEXT, cliente TEXT, cliente_id INTEGER, remito TEXT, proveedor TEXT, proveedor_les TEXT,
  estado_les TEXT, pallets_declarados INTEGER, pallets INTEGER, deposito TEXT, observacion TEXT, observacion_descarga TEXT, a_desglosar INTEGER NOT NULL DEFAULT 0,
  proveedor_wms TEXT, centro_emisor INTEGER, letra TEXT, fecha_emision TEXT, recepcion_wms TEXT, comprobante TEXT,
  articulos TEXT, descargado TEXT, cerrado TEXT, creado TEXT NOT NULL, actualizado TEXT NOT NULL, usuario TEXT);
CREATE TABLE IF NOT EXISTS ingreso_pallets (
  id INTEGER PRIMARY KEY AUTOINCREMENT, les_id INTEGER NOT NULL, orden INTEGER NOT NULL, contenedor TEXT, articulo TEXT, cantidad REAL, lote TEXT,
  recibido INTEGER NOT NULL DEFAULT 0, respuesta TEXT, recibido_en TEXT);
CREATE INDEX IF NOT EXISTS ingreso_pallets_ing ON ingreso_pallets(les_id);
CREATE TABLE IF NOT EXISTS ingreso_fotos (
  id INTEGER PRIMARY KEY AUTOINCREMENT, les_id INTEGER NOT NULL, tipo TEXT NOT NULL, codigo TEXT, archivo TEXT NOT NULL, subida INTEGER NOT NULL DEFAULT 0,
  respuesta TEXT, hora TEXT NOT NULL, usuario TEXT);
CREATE INDEX IF NOT EXISTS ingreso_fotos_ing ON ingreso_fotos(les_id);
CREATE TABLE IF NOT EXISTS ingreso_eventos (
  id INTEGER PRIMARY KEY AUTOINCREMENT, les_id INTEGER NOT NULL, hora TEXT NOT NULL, usuario TEXT, paso TEXT NOT NULL, ok INTEGER NOT NULL, mensaje TEXT);
CREATE INDEX IF NOT EXISTS ingreso_eventos_ing ON ingreso_eventos(les_id);
CREATE TABLE IF NOT EXISTS proveedores_wms (
  proveedor_les TEXT PRIMARY KEY, nombre TEXT, codigo_wms TEXT NOT NULL, nombre_wms TEXT, actualizado TEXT);
""";

    readonly Db _db = sv.Db;
    readonly ConcurrentDictionary<string, LesIngresos> _les = new();
    readonly ConcurrentDictionary<string, SesionEscritorio> _wms = new();
    readonly ConcurrentDictionary<long, SemaphoreSlim> _candados = new();

    string Carpeta(long id) { var d = Path.Combine(cfg.Carpeta, "ingresos", id.ToString()); Directory.CreateDirectory(d); return d; }

    // ================================================================ credenciales

    /// <summary>Usuario del LES (Ingresos) de cada usuario de SONA: se carga en "Mis credenciales"; si no cargó, se usa el del WMS.</summary>
    public object MiLes(string usuario)
    {
        var p = cred.Propias(usuario, "les_ingresos");
        return new { usuario = p?.Usuario ?? usuario.ToUpperInvariant(), cargado = p != null || cred.Wms(usuario) != null, propio = p != null };
    }

    public async Task<object> GuardarMiLes(string usuario, string lesUsuario, string clave)
    {
        await cred.Guardar(usuario, "les_ingresos", lesUsuario, clave);   // prueba antes de guardar: si no entra, no se guarda
        return MiLes(usuario);
    }

    LesIngresos Les(string usuario)
    {
        var u = usuario.ToUpperInvariant();
        var (lu, lc) = cred.Propias(u, "les_ingresos") ?? cred.Wms(u)
            ?? throw new ErrorUsuario("Cargá tu usuario y clave del LES (Ingresos) en \"Mis credenciales\".", 428);
        // la sesión se rehace sola si cambia el usuario o la clave
        var k = u + "|" + lu + "|" + lc.GetHashCode();
        if (_les.TryGetValue(k, out var c)) return c;
        foreach (var vieja in _les.Keys.Where(x => x.StartsWith(u + "|")).ToList()) _les.TryRemove(vieja, out _);
        return _les[k] = new LesIngresos(cfg, lu, lc);
    }

    SesionEscritorio Wms(string usuario)
    {
        var u = usuario.ToUpperInvariant();
        var c = sv.SesionDe(u).Clave;
        if (string.IsNullOrEmpty(c)) throw new ErrorUsuario("No tengo tu clave del WMS: salí e ingresá de nuevo.");
        return _wms.GetOrAdd(u + "|" + c.GetHashCode(), _ => new SesionEscritorio(cfg, u, c));
    }

    void Evento(long id, string usuario, string paso, bool ok, string? mensaje)
    {
        _db.Ejecutar("INSERT INTO ingreso_eventos (les_id, hora, usuario, paso, ok, mensaje) VALUES ($1,$2,$3,$4,$5,$6)", id, Db.Ahora(), usuario, paso, ok, mensaje);
        _db.Ejecutar("UPDATE ingresos SET actualizado=$2 WHERE les_id=$1", id, Db.Ahora());
        ev.Emitir(new() { ["tipo"] = "ingreso", ["id"] = id, ["paso"] = paso, ["ok"] = ok, ["usuario"] = usuario }, (u, rol) => Seguridad.RolesPanel.Contains(rol));
    }

    async Task<T> Candado<T>(string usuario, long id, Func<Task<T>> fn)
    {
        var c = _candados.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        if (!await c.WaitAsync(TimeSpan.FromSeconds(2))) throw new ErrorUsuario("Otro usuario está trabajando este ingreso en este momento: esperá unos segundos.", 409);
        try { return await Capturando(usuario, "ingreso_" + id, fn); } finally { c.Release(); }
    }

    /// <summary>
    /// Mientras dura la operación guarda cada pantalla del WMS (escritorio y handheld) y cada respuesta del LES en capturas/
    /// (diagnóstico de las primeras recepciones reales: ver cómo responden de verdad).
    /// </summary>
    Task<T> Capturando<T>(string usuario, string nombre, Func<Task<T>> fn) => Capturando(usuario, new Captura(cfg, nombre), fn);

    async Task<T> Capturando<T>(string usuario, Captura cap, Func<Task<T>> fn)
    {
        LesIngresos? les = null; SesionEscritorio? wms = null; SesionHandheld? hh = null;
        try { les = Les(usuario); les.Traza = cap.Les; } catch { }
        try { wms = Wms(usuario); wms.Traza = cap.Wms; } catch { }
        try { hh = sv.SesionDe(usuario); hh.Traza = cap.Wms; } catch { }
        try { return await fn(); }
        catch (Exception e) { cap.Nota("ERROR: " + e.Message); throw; }
        finally
        {
            if (les != null) les.Traza = null;
            if (wms != null) wms.Traza = null;
            if (hh != null) hh.Traza = null;
        }
    }

    static string? S(JsonNode? n, string k) => n?[k] is JsonValue v ? v.ToString() : null;
    static long L(JsonNode? n, string k) => long.TryParse(S(n, k), out var x) ? x : 0;
    static double D(JsonNode? n, string k) => double.TryParse(S(n, k), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0;
    static bool B(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    // ================================================================ bandeja

    public async Task<object> Bandeja(string usuario)
    {
        var les = Les(usuario);
        var lista = (await les.Lista()) as JsonArray ?? new JsonArray();
        JsonArray? sla = null;
        try { sla = await les.Sla() as JsonArray; } catch { }
        var locales = _db.Q("SELECT les_id, recepcion_wms, descargado, cerrado, (SELECT count(*) FROM ingreso_pallets p WHERE p.les_id=i.les_id) AS pallets_sona, (SELECT count(*) FROM ingreso_pallets p WHERE p.les_id=i.les_id AND p.recibido=1) AS recibidos FROM ingresos i")
            .ToDictionary(f => f.L("les_id") ?? 0);
        var filas = lista.OfType<JsonObject>().Select(x =>
        {
            var id = L(x, "Id");
            locales.TryGetValue(id, out var loc);
            return new
            {
                id, nroRecepcion = S(x, "NroRecepcion"), cliente = S(x, "Cliente"), remito = S(x, "Remito"), fecha = S(x, "Fecha"), proveedor = S(x, "Proveedor"),
                deposito = S(x, "Deposito"), observacion = S(x, "Observacion"), usuario = S(x, "Usuario"), pallets = L(x, "CantidadPallets"),
                llegada = S(x, "HorarioLLegada"), salida = S(x, "HorarioSalida"), documentacionIncompleta = B(x, "DocumentacionIncompleta"),
                estado = S(x, "Estado"), cambioEstado = L(x, "FechaCambioEstado") is var ms && ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("o") : null,
                observacionEstado = S(x, "ObservacionCambioEstado"),
                recepcionWms = loc?.S("recepcion_wms"), palletsSona = loc?.L("pallets_sona") ?? 0, recibidos = loc?.L("recibidos") ?? 0,
            };
        }).ToList();
        return new { ingresos = filas, sla = sla?.OfType<JsonObject>().Select(s => new { estado = S(s, "Estado"), horas = D(s, "Horas") }).ToList(), lesUsuario = les.Usuario };
    }

    // ================================================================ ficha

    public async Task<object> Ficha(string usuario, long id)
    {
        var les = Les(usuario);
        var det = await les.Detalle(id);
        var ing = det["ingreso"] as JsonObject ?? throw new ErrorUsuario("El LES no devolvió el ingreso.", 404);
        var arts = det["articulos"] as JsonArray ?? new JsonArray();
        // la lista trae datos que el detalle no (cliente, proveedor, N° de recepción)
        JsonObject? cab = null;
        try { cab = ((await les.Lista()) as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => L(x, "Id") == id); } catch { }
        var clienteId = (int)L(ing, "ClienteId");
        var ahora = Db.Ahora();
        _db.Ejecutar("""
            INSERT INTO ingresos (les_id, nro_recepcion, cliente, cliente_id, remito, proveedor, proveedor_les, estado_les, pallets_declarados, observacion, articulos, creado, actualizado, usuario)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$12,$13)
            ON CONFLICT(les_id) DO UPDATE SET nro_recepcion=COALESCE(excluded.nro_recepcion, nro_recepcion), cliente=COALESCE(excluded.cliente, cliente),
              cliente_id=excluded.cliente_id, remito=excluded.remito, proveedor=COALESCE(excluded.proveedor, proveedor), proveedor_les=excluded.proveedor_les,
              estado_les=excluded.estado_les, articulos=excluded.articulos, actualizado=excluded.actualizado
            """, id, S(cab, "NroRecepcion"), S(cab, "Cliente") ?? sv.NombreEmpresa(clienteId), clienteId, S(ing, "Remito"), S(cab, "Proveedor"), S(ing, "ProveedorId"),
            S(ing, "Estado"), L(ing, "CantidadPallets"), S(ing, "Observacion"), arts.ToJsonString(), ahora, usuario);
        var loc = _db.Q1("SELECT * FROM ingresos WHERE les_id=$1", id)!;

        JsonNode? fotosPallets = null, pop = null, fotosCont = null;
        var codigos = arts.OfType<JsonObject>().Select(a => S(a, "Codigo") ?? "").Where(c => c.Length > 0).ToList();
        try { fotosPallets = await les.FotosPallets(id); } catch { }
        if (codigos.Count > 0)
        {
            try { pop = await les.FotosPop(id, clienteId, codigos); } catch { }
            try { fotosCont = await les.FotosContenedor(id, codigos); } catch { }
        }
        var proveedorWms = loc.S("proveedor_wms") ?? _db.Q1("SELECT codigo_wms FROM proveedores_wms WHERE proveedor_les=$1", S(ing, "ProveedorId"))?.S("codigo_wms");
        return new
        {
            id, nroRecepcion = loc.S("nro_recepcion"), cliente = loc.S("cliente"), clienteId, empresaWms = sv.NombreEmpresa(clienteId), remito = S(ing, "Remito"),
            proveedor = loc.S("proveedor"), proveedorLes = S(ing, "ProveedorId"), proveedorWms,
            estado = S(ing, "Estado"), palletsDeclarados = L(ing, "CantidadPallets"), observacion = S(ing, "Observacion"), observacionEstado = S(ing, "ObservacionCambioEstado"),
            deposito = S(ing, "Deposito"), aDesglosar = B(ing, "ADesglosar"), tieneRemitoFirmado = B(ing, "tieneRemitoFirmado"), urlRemitoFirmado = S(ing, "urlRemitoFirmado"),
            articulos = arts.OfType<JsonObject>().Select(a => new
            {
                id = L(a, "Id"), codigo = S(a, "Codigo"), descripcion = S(a, "Descripcion"), cantidad = D(a, "Cantidad"),
                requiereValidacion = B(a, "RequiereValidacion"), tieneEtiquetas = B(a, "TieneEtiquetas"),
                fotoPop = pop?["urls"]?[S(a, "Codigo") ?? ""]?.ToString(),
            }).ToList(),
            popFaltantes = (pop?["missingCodes"] as JsonArray)?.Select(x => x!.ToString()).ToList(),
            contenedorSinFoto = (fotosCont?["missingCodes"] as JsonArray)?.Select(x => x!.ToString()).ToList(),
            fotosPallets = (fotosPallets?["fotos"] as JsonArray)?.OfType<JsonObject>().Select(f => new { nombre = S(f, "nombre"), url = S(f, "url") }).ToList(),
            sona = new
            {
                pallets = loc.L("pallets"), deposito = loc.S("deposito"), observacion = loc.S("observacion_descarga"), aDesglosar = loc.B("a_desglosar"),
                centroEmisor = loc.L("centro_emisor"), letra = loc.S("letra"), fechaEmision = loc.S("fecha_emision"),
                recepcionWms = loc.S("recepcion_wms"), comprobante = loc.S("comprobante") != null, descargado = loc.S("descargado"), cerrado = loc.S("cerrado"),
            },
            contenedores = _db.Q("SELECT id, orden, contenedor, articulo, cantidad, lote, recibido, respuesta, recibido_en FROM ingreso_pallets WHERE les_id=$1 ORDER BY orden", id)
                .Select(p => new
                {
                    id = p.L("id"), orden = p.L("orden"), contenedor = p.S("contenedor"), articulo = p.S("articulo"), cantidad = p.D("cantidad"), lote = p.S("lote"),
                    recibido = p.B("recibido"), respuesta = p.S("respuesta"),
                    guardadoEn = p.S("contenedor") is { } c ? _db.Q1("SELECT destino FROM movimientos WHERE contenedor=$1 AND tipo='guardado' AND estado='aplicado' ORDER BY hora_servidor DESC LIMIT 1", c)?.S("destino") : null,
                }).ToList(),
            fotos = _db.Q("SELECT id, tipo, codigo, subida, respuesta, hora, usuario FROM ingreso_fotos WHERE les_id=$1 ORDER BY id", id),
            eventos = _db.Q("SELECT hora, usuario, paso, ok, mensaje FROM ingreso_eventos WHERE les_id=$1 ORDER BY id DESC LIMIT 100", id),
        };
    }

    // ================================================================ descarga

    public sealed record DescargaReq(int? Pallets, string? Deposito, string? Observacion, bool? ADesglosar);

    public object GuardarDescarga(string usuario, long id, DescargaReq b)
    {
        _db.Ejecutar("UPDATE ingresos SET pallets=COALESCE($2, pallets), deposito=COALESCE($3, deposito), observacion_descarga=COALESCE($4, observacion_descarga), a_desglosar=COALESCE($5, a_desglosar), actualizado=$6 WHERE les_id=$1",
            id, b.Pallets, b.Deposito, b.Observacion, b.ADesglosar, Db.Ahora());
        return new { ok = true };
    }

    /// <summary>Guarda la foto en SONA y la sube al LES: pallet, POP del artículo o contenedor; el remito firmado va con la descarga.</summary>
    public Task<object> Foto(string usuario, long id, string tipo, string? codigo, byte[] datos, string nombre) =>
        Capturando(usuario, "ingreso_" + id, () => FotoInterna(usuario, id, tipo, codigo, datos, nombre));

    async Task<object> FotoInterna(string usuario, long id, string tipo, string? codigo, byte[] datos, string nombre)
    {
        if (datos.Length == 0) throw new ErrorUsuario("La foto está vacía.");
        if (datos.Length > 8 * 1024 * 1024) throw new ErrorUsuario("La foto pesa más de 8 MB.");
        tipo = tipo.ToLowerInvariant();
        if (tipo is not ("pallet" or "remito" or "pop" or "contenedor")) throw new ErrorUsuario("Tipo de foto inválido.");
        var ext = Path.GetExtension(nombre).ToLowerInvariant() is ".png" ? ".png" : ".jpg";
        var archivo = Path.Combine(Carpeta(id), $"{tipo}_{codigo ?? ""}_{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}".Replace("__", "_"));
        await File.WriteAllBytesAsync(archivo, datos);
        var fid = Convert.ToInt64(_db.Escalar("INSERT INTO ingreso_fotos (les_id, tipo, codigo, archivo, hora, usuario) VALUES ($1,$2,$3,$4,$5,$6) RETURNING id", id, tipo, codigo, archivo, Db.Ahora(), usuario));
        if (tipo == "remito") { Evento(id, usuario, "foto_remito", true, "Remito firmado guardado (se sube al LES al confirmar la descarga)."); return new { ok = true, id = fid, subida = false }; }
        var les = Les(usuario);
        try
        {
            var loc = _db.Q1("SELECT cliente_id FROM ingresos WHERE les_id=$1", id);
            JsonNode r = tipo switch
            {
                "pallet" => await les.SubirFotoPallet(id, datos, Path.GetFileName(archivo)),
                "pop" => await les.SubirFotoPop((int)(loc?.L("cliente_id") ?? 0), codigo ?? throw new ErrorUsuario("Falta el artículo de la foto POP."), datos, Path.GetFileName(archivo)),
                _ => await les.SubirFotoContenedor(id, codigo ?? throw new ErrorUsuario("Falta el artículo de la foto del contenedor."), datos, Path.GetFileName(archivo)),
            };
            _db.Ejecutar("UPDATE ingreso_fotos SET subida=1, respuesta=$2 WHERE id=$1", fid, r.ToJsonString());
            Evento(id, usuario, "foto_" + tipo, true, $"Foto {tipo}{(codigo != null ? " " + codigo : "")} subida al LES.");
            return new { ok = true, id = fid, subida = true };
        }
        catch (ErrorUsuario e)
        {
            _db.Ejecutar("UPDATE ingreso_fotos SET respuesta=$2 WHERE id=$1", fid, e.Message);
            Evento(id, usuario, "foto_" + tipo, false, e.Message);
            throw;
        }
    }

    public (byte[] Datos, string Tipo) ArchivoFoto(long fotoId)
    {
        var f = _db.Q1("SELECT archivo FROM ingreso_fotos WHERE id=$1", fotoId)?.S("archivo");
        if (f == null || !File.Exists(f)) throw new ErrorUsuario("No existe la foto.", 404);
        return (File.ReadAllBytes(f), f.EndsWith(".png") ? "image/png" : "image/jpeg");
    }

    public async Task<(byte[] Datos, string Tipo)?> ImagenLes(string usuario, string url) =>
        await Les(usuario).Imagen(url) is { } b ? (b, "image/jpeg") : null;

    /// <summary>"Confirmar" la descarga en el LES (pasa a Pend. ingreso WMS) con el remito firmado.</summary>
    public Task<object> ConfirmarDescarga(string usuario, long id) => Candado<object>(usuario, id, async () =>
    {
        var les = Les(usuario);
        var det = await les.Detalle(id);
        var ing = (JsonObject)det["ingreso"]!;
        var loc = _db.Q1("SELECT * FROM ingresos WHERE les_id=$1", id) ?? throw new ErrorUsuario("Abrí el ingreso primero.");
        var pallets = (int)(loc.L("pallets") ?? 0);
        if (pallets <= 0) throw new ErrorUsuario("Falta la cantidad de pallets descargados.");
        if (string.IsNullOrWhiteSpace(loc.S("deposito"))) throw new ErrorUsuario("Falta el depósito (SONA / GAPP…).");
        var remito = _db.Q1("SELECT archivo FROM ingreso_fotos WHERE les_id=$1 AND tipo='remito' ORDER BY id DESC LIMIT 1", id)?.S("archivo");
        if (remito == null && !B(ing, "tieneRemitoFirmado")) throw new ErrorUsuario("Falta la foto del remito firmado.");
        var arts = new JsonArray();
        foreach (var a in (det["articulos"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            if (B(a, "RequiereValidacion")) throw new ErrorUsuario($"El artículo {S(a, "Codigo")} está pendiente de validación en el LES: corregilo ahí antes de confirmar.");
            arts.Add(new JsonObject { ["Id"] = L(a, "Id"), ["Codigo"] = S(a, "Codigo"), ["Cantidad"] = (int)D(a, "Cantidad"), ["Descripcion"] = S(a, "Descripcion"), ["RequiereValidacion"] = false, ["ObservacionValidacion"] = "" });
        }
        var modelo = new JsonObject
        {
            ["IngresoId"] = id.ToString(), ["ClienteId"] = S(ing, "ClienteId"), ["Remito"] = S(ing, "Remito"), ["ProveedorId"] = S(ing, "ProveedorId"),
            ["CantidadPallets"] = pallets.ToString(), ["Observacion"] = S(ing, "Observacion") ?? "", ["ObservacionCambioEstado"] = loc.S("observacion_descarga") ?? "",
            ["ADesglosar"] = loc.B("a_desglosar"), ["Deposito"] = loc.S("deposito"), ["Articulos"] = arts,
        };
        try
        {
            await les.ConfirmarDescarga(modelo, remito != null ? await File.ReadAllBytesAsync(remito) : null, remito != null ? Path.GetFileName(remito) : null);
            _db.Ejecutar("UPDATE ingresos SET descargado=$2, estado_les='PEND INGRESO WMS' WHERE les_id=$1", id, Db.Ahora());
            Evento(id, usuario, "descarga", true, $"Descarga confirmada en el LES: {pallets} pallet(s) en {loc.S("deposito")}.");
            return new { ok = true, mensaje = "Descarga confirmada en el LES (Pend. ingreso WMS)." };
        }
        catch (ErrorUsuario e) { Evento(id, usuario, "descarga", false, e.Message); throw; }
    });

    // ================================================================ contenedores (pallets)

    public sealed record ContenedorReq(string? Contenedor, string? Articulo, double? Cantidad, string? Lote);

    public object GuardarContenedores(string usuario, long id, List<ContenedorReq> lista)
    {
        var recibidos = _db.Q("SELECT contenedor FROM ingreso_pallets WHERE les_id=$1 AND recibido=1", id).Select(f => f.S("contenedor")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nuevos = lista.Where(x => !string.IsNullOrWhiteSpace(x.Contenedor) || !string.IsNullOrWhiteSpace(x.Articulo)).ToList();
        var repetidos = nuevos.Where(x => !string.IsNullOrWhiteSpace(x.Contenedor)).GroupBy(x => x.Contenedor!.Trim().ToUpperInvariant()).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repetidos.Count > 0) throw new ErrorUsuario("Contenedor repetido: " + string.Join(", ", repetidos));
        _db.Ejecutar("DELETE FROM ingreso_pallets WHERE les_id=$1 AND recibido=0", id);
        var orden = _db.Q1("SELECT COALESCE(max(orden),0) AS m FROM ingreso_pallets WHERE les_id=$1", id)?.L("m") ?? 0;
        foreach (var x in nuevos)
        {
            var c = x.Contenedor?.Trim().ToUpperInvariant();
            if (c != null && recibidos.Contains(c)) continue;
            _db.Ejecutar("INSERT INTO ingreso_pallets (les_id, orden, contenedor, articulo, cantidad, lote) VALUES ($1,$2,$3,$4,$5,$6)",
                id, ++orden, string.IsNullOrEmpty(c) ? null : c, x.Articulo?.Trim().ToUpperInvariant(), x.Cantidad, string.IsNullOrWhiteSpace(x.Lote) ? null : x.Lote.Trim());
        }
        Evento(id, usuario, "contenedores", true, $"{nuevos.Count} contenedor(es) cargados.");
        return new { ok = true };
    }

    /// <summary>Genera las etiquetas (y sus contenedores) en el LES para un artículo: PC, PP o Bultos.</summary>
    public Task<object> GenerarEtiquetasLes(string usuario, long id, string codigo, string prefijo, int cantidad) => Candado<object>(usuario, id, async () =>
    {
        if (cantidad is <= 0 or > 200) throw new ErrorUsuario("Cantidad de etiquetas inválida.");
        if (prefijo is not ("PC" or "PP" or "Bultos")) throw new ErrorUsuario("Tipo de etiqueta inválido (PC, PP o Bultos).");
        var les = Les(usuario);
        var det = await les.Detalle(id);
        var ing = (JsonObject)det["ingreso"]!;
        var a = (det["articulos"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => string.Equals(S(x, "Codigo"), codigo, StringComparison.OrdinalIgnoreCase))
                ?? throw new ErrorUsuario($"El artículo {codigo} no está en el ingreso.");
        try
        {
            var r = await les.GenerarEtiquetas(id, (int)L(ing, "ClienteId"), L(a, "Id"), S(a, "Codigo")!, D(a, "Cantidad"), S(a, "Descripcion") ?? "", prefijo, cantidad);
            // los números de contenedor que devuelve el LES (PC-201425…)
            var conts = Regex.Matches(r.ToJsonString(), @"\b(?:PC|PP|I|B)-\d{4,}\b").Select(m => m.Value).Distinct().ToList();
            Evento(id, usuario, "etiquetas_les", true, $"{cantidad} etiqueta(s) {prefijo} de {codigo} generadas en el LES" + (conts.Count > 0 ? ": " + string.Join(", ", conts) : "."));
            return new { ok = true, contenedores = conts, respuesta = r };
        }
        catch (ErrorUsuario e) { Evento(id, usuario, "etiquetas_les", false, e.Message); throw; }
    });

    /// <summary>
    /// Contenedores generados desde SONA: pide al LES un número de contenedor por cada pallet cargado sin contenedor
    /// (la misma numeración PP/PC del LES) y la etiqueta sale por la impresora virtual de SONA (/api/etiquetas/…).
    /// </summary>
    public Task<object> GenerarContenedoresSona(string usuario, long id, string prefijo) => Candado<object>(usuario, id, async () =>
    {
        var pend = _db.Q("SELECT id, orden, articulo FROM ingreso_pallets WHERE les_id=$1 AND recibido=0 AND (contenedor IS NULL OR contenedor='') ORDER BY orden", id);
        if (pend.Count == 0) throw new ErrorUsuario("No hay pallets sin contenedor.");
        if (pend.Any(p => string.IsNullOrEmpty(p.S("articulo")))) throw new ErrorUsuario("Poné el artículo de cada pallet antes de generar los contenedores.");
        var clave = sv.SesionDe(usuario).Clave;
        var creados = new List<string>();
        try
        {
            foreach (var p in pend)
            {
                var e = await etiquetas.CrearContenedor(usuario, clave, p.S("articulo")!, prefijo);
                _db.Ejecutar("UPDATE ingreso_pallets SET contenedor=$2 WHERE id=$1", p.L("id"), e.Contenedor);
                creados.Add(e.Contenedor);
            }
        }
        finally
        {
            if (creados.Count > 0) Evento(id, usuario, "contenedores_sona", creados.Count == pend.Count, $"Contenedores generados: {string.Join(", ", creados)}" + (creados.Count < pend.Count ? $" (faltaron {pend.Count - creados.Count})" : ""));
        }
        return new { ok = true, contenedores = creados, etiquetas = creados.Select(c => "/api/etiquetas/" + Uri.EscapeDataString(c)).ToList() };
    });

    // ================================================================ WMS: artículos, proveedor y recepción

    public Task<object> VerificarArticulos(string usuario, long id) => Candado<object>(usuario, id, async () =>
    {
        var loc = _db.Q1("SELECT cliente_id, articulos FROM ingresos WHERE les_id=$1", id) ?? throw new ErrorUsuario("Abrí el ingreso primero.");
        var emp = (int)(loc.L("cliente_id") ?? 0);
        var arts = (JsonNode.Parse(loc.S("articulos") ?? "[]") as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(a => S(a, "Codigo")!).Where(c => c != null).Distinct().ToList();
        var wms = Wms(usuario);
        var res = new List<object>();
        foreach (var c in arts)
        {
            var e = await wms.EstadoArticulo(emp, c);
            res.Add(new { codigo = c, estado = e switch { EstadoArticuloWms.Ok => "ok", EstadoArticuloWms.NoConfigurado => "no_configurado", _ => "no_existe" } });
        }
        Evento(id, usuario, "articulos", res.All(x => ((dynamic)x).estado == "ok"), string.Join(", ", res.Select(x => $"{((dynamic)x).codigo}: {((dynamic)x).estado}")));
        return new { empresa = emp, articulos = res };
    });

    public Task<object> ConfigurarArticulo(string usuario, long id, string codigo) => Candado<object>(usuario, id, async () =>
    {
        var emp = (int)(_db.Q1("SELECT cliente_id FROM ingresos WHERE les_id=$1", id)?.L("cliente_id") ?? 0);
        var (ok, msj) = await Wms(usuario).ConfigurarArticulo(emp, codigo);
        Evento(id, usuario, "configurar_articulo", ok, $"{codigo}: {msj}");
        return new { ok, mensaje = msj };
    });

    static string NormProv(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && (char.IsLetterOrDigit(ch) || ch == ' ')) sb.Append(char.ToUpperInvariant(ch));
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>Proveedor del WMS que corresponde al del LES: el que ya se usó, o por nombre (el WMS lo muestra cortado a 20 letras).</summary>
    public Task<object> Proveedores(string usuario, long id) => Candado<object>(usuario, id, async () =>
    {
        var loc = _db.Q1("SELECT cliente_id, proveedor, proveedor_les, proveedor_wms FROM ingresos WHERE les_id=$1", id) ?? throw new ErrorUsuario("Abrí el ingreso primero.");
        var lista = await Wms(usuario).Proveedores((int)(loc.L("cliente_id") ?? 0));
        var guardado = loc.S("proveedor_wms") ?? _db.Q1("SELECT codigo_wms FROM proveedores_wms WHERE proveedor_les=$1", loc.S("proveedor_les"))?.S("codigo_wms");
        var nombre = NormProv(loc.S("proveedor") ?? "");
        var sugeridos = nombre.Length == 0 ? new List<(string Codigo, string Nombre)>() : lista.Where(p =>
        {
            var w = NormProv(p.Nombre);
            return w.Length >= 3 && (nombre.StartsWith(w) || w.StartsWith(nombre) || (w.Length >= 8 && nombre.Contains(w)));
        }).ToList();
        return new
        {
            elegido = guardado ?? (sugeridos.Count == 1 ? sugeridos[0].Codigo : null),
            sugeridos = sugeridos.Select(p => new { codigo = p.Codigo, nombre = p.Nombre }).ToList(),
            todos = lista.Select(p => new { codigo = p.Codigo, nombre = p.Nombre }).ToList(),
        };
    });

    public sealed record RecepcionReq(string? Proveedor, int? CentroEmisor, string? Letra, DateTime? FechaEmision, string? Numero = null);

    static long NumeroRemito(string? remito) => long.TryParse(Regex.Replace(remito ?? "", @"\D", "").TrimStart('0') is { Length: > 0 } s ? s : "0", out var n) ? n : 0;

    /// <summary>Arma la recepción por contenedor en el WMS con lo guardado en SONA (renglones = lo cargado en los pallets, o lo que pide el LES).</summary>
    public Task<object> CrearRecepcion(string usuario, long id, RecepcionReq b) => Candado<object>(usuario, id, async () =>
    {
        var loc = _db.Q1("SELECT * FROM ingresos WHERE les_id=$1", id) ?? throw new ErrorUsuario("Abrí el ingreso primero.");
        if (loc.S("recepcion_wms") is { } ya) throw new ErrorUsuario($"Este ingreso ya tiene la recepción {ya} en el WMS.", 409);
        if (!string.IsNullOrWhiteSpace(b.Numero))
        {
            // la recepción ya se hizo (a mano o el WMS no devolvió el número): solo se vincula
            var nro = Regex.Replace(b.Numero, @"\D", "");
            if (nro.Length == 0) throw new ErrorUsuario("Número de recepción inválido.");
            _db.Ejecutar("UPDATE ingresos SET recepcion_wms=$2 WHERE les_id=$1", id, nro);
            Evento(id, usuario, "recepcion_wms", true, $"Vinculado a la recepción {nro} del WMS (cargada a mano).");
            return new { ok = true, numero = nro, mensaje = "Recepción vinculada.", comprobante = false };
        }
        if (string.IsNullOrWhiteSpace(b.Proveedor)) throw new ErrorUsuario("Elegí el proveedor del WMS.");
        var proveedor = b.Proveedor.Trim();
        var porPallets = _db.Q("SELECT articulo, sum(cantidad) AS c FROM ingreso_pallets WHERE les_id=$1 AND articulo IS NOT NULL AND cantidad > 0 GROUP BY articulo", id)
            .Select(f => new RenglonRecepcion(f.S("articulo")!, f.D("c") ?? 0)).ToList();
        var renglones = porPallets.Count > 0 ? porPallets
            : (JsonNode.Parse(loc.S("articulos") ?? "[]") as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(a => new RenglonRecepcion(S(a, "Codigo")!, D(a, "Cantidad"))).ToList();
        var remito = NumeroRemito(loc.S("remito"));
        var ce = b.CentroEmisor ?? 1; var letra = string.IsNullOrWhiteSpace(b.Letra) ? "R" : b.Letra.Trim().ToUpperInvariant();
        var fecha = b.FechaEmision ?? DateTime.Today;
        try
        {
            var r = await Wms(usuario).CrearRecepcion((int)(loc.L("cliente_id") ?? 0), proveedor, remito, ce, letra, loc.S("nro_recepcion") ?? id.ToString(), fecha, renglones);
            string? comp = null;
            if (r.Comprobante != null) { comp = Path.Combine(Carpeta(id), $"recepcion_wms_{r.Numero}.pdf"); await File.WriteAllBytesAsync(comp, r.Comprobante); }
            _db.Ejecutar("UPDATE ingresos SET proveedor_wms=$2, centro_emisor=$3, letra=$4, fecha_emision=$5, recepcion_wms=$6, comprobante=$7 WHERE les_id=$1",
                id, proveedor, ce, letra, fecha.ToString("yyyy-MM-dd"), r.Ok ? r.Numero : null, comp);
            if (loc.S("proveedor_les") is { Length: > 0 } pl)
                _db.Ejecutar("INSERT INTO proveedores_wms (proveedor_les, nombre, codigo_wms, actualizado) VALUES ($1,$2,$3,$4) ON CONFLICT(proveedor_les) DO UPDATE SET codigo_wms=excluded.codigo_wms, actualizado=excluded.actualizado",
                    pl, loc.S("proveedor"), proveedor, Db.Ahora());
            var msj = r.Ok ? r.Mensaje : r.Mensaje + " ATENCIÓN: puede que el WMS la haya grabado igual; buscala por doc. externo " + (loc.S("nro_recepcion") ?? id.ToString()) + " antes de reintentar y, si existe, vinculá el número.";
            Evento(id, usuario, "recepcion_wms", r.Ok, r.Ok ? $"Recepción {r.Numero} creada en el WMS ({renglones.Count} renglón/es). {r.Mensaje}" : msj);
            return new { ok = r.Ok, numero = r.Numero, mensaje = msj, comprobante = comp != null };
        }
        catch (WmsError e) { Evento(id, usuario, "recepcion_wms", false, e.Message); throw new ErrorUsuario("WMS: " + e.Message, 502); }
    });

    public (byte[] Datos, string Nombre) Comprobante(long id)
    {
        var f = _db.Q1("SELECT comprobante, recepcion_wms FROM ingresos WHERE les_id=$1", id);
        var a = f?.S("comprobante");
        if (a == null || !File.Exists(a)) throw new ErrorUsuario("No hay comprobante guardado de la recepción.", 404);
        return (File.ReadAllBytes(a), Path.GetFileName(a));
    }

    /// <summary>Recepción por contenedor (handheld): cada pallet cargado y no recibido todavía.</summary>
    public Task<object> RecibirContenedores(string usuario, long id) => Candado<object>(usuario, id, async () =>
    {
        var loc = _db.Q1("SELECT * FROM ingresos WHERE les_id=$1", id) ?? throw new ErrorUsuario("Abrí el ingreso primero.");
        var nro = loc.S("recepcion_wms") ?? throw new ErrorUsuario("Primero creá la recepción en el WMS.");
        var pend = _db.Q("SELECT * FROM ingreso_pallets WHERE les_id=$1 AND recibido=0 ORDER BY orden", id);
        if (pend.Count == 0) throw new ErrorUsuario("No hay contenedores pendientes de recibir.");
        var faltan = pend.Where(p => string.IsNullOrEmpty(p.S("contenedor")) || string.IsNullOrEmpty(p.S("articulo")) || (p.D("cantidad") ?? 0) <= 0).Select(p => p.L("orden")).ToList();
        if (faltan.Count > 0) throw new ErrorUsuario("Completá contenedor, artículo y cantidad de los pallets " + string.Join(", ", faltan) + ".");
        try
        {
            var res = await sv.SesionDe(usuario).RecibirContenedores((int)(loc.L("cliente_id") ?? 0), nro,
                pend.Select(p => new ContenedorRecibir(p.S("contenedor")!, p.S("articulo")!, p.D("cantidad") ?? 0, p.S("lote"))).ToList());
            foreach (var x in res)
                _db.Ejecutar("UPDATE ingreso_pallets SET recibido=$3, respuesta=$4, recibido_en=CASE WHEN $3 THEN $5 ELSE recibido_en END WHERE les_id=$1 AND contenedor=$2",
                    id, x.Contenedor.ToUpperInvariant(), x.Ok, x.Mensaje, Db.Ahora());
            var ok = res.Count(x => x.Ok);
            Evento(id, usuario, "recibir", ok == res.Count, $"{ok} de {res.Count} contenedor(es) recibidos en la recepción {nro}." + string.Concat(res.Where(x => !x.Ok).Select(x => $" {x.Contenedor}: {x.Mensaje}.")));
            return new { ok = ok == res.Count, recibidos = ok, total = res.Count, detalle = res };
        }
        catch (WmsError e) { Evento(id, usuario, "recibir", false, e.Message); throw new ErrorUsuario("WMS: " + e.Message, 502); }
    });

    // ================================================================ diagnóstico

    /// <summary>
    /// Captura SOLO LECTURA de todo lo que hace falta para ajustar el módulo a las pantallas reales: LES (página, scripts,
    /// ingresos pendientes y sus detalles) y WMS (menú, recepción por contenedor hasta elegir proveedor, artículos, alta de
    /// artículo sin grabar, rollo de etiquetas, recepciones del handheld). No graba nada. Devuelve un zip con esto y con las
    /// capturas de las operaciones reales que se hicieron desde SONA.
    /// </summary>
    public async Task<(byte[] Datos, string Nombre, string? Copia)> Diagnostico(string usuario)
    {
        var cap = new Captura(cfg, "diagnostico_" + usuario);
        cap.Nota($"Diagnóstico de ingreso - SONA {Api.Rutas.Version} - usuario {usuario}");
        await Capturando<object?>(usuario, cap, async () =>
        {
            var empresas = new Dictionary<int, HashSet<string>>();
            // ---- LES
            try
            {
                var les = Les(usuario);
                var index = await les.Crudo(les.Base + "Index");
                foreach (Match m in Regex.Matches(index, @"<script[^>]+src=""([^""]+)""", RegexOptions.IgnoreCase))
                {
                    var src = WebUtility.HtmlDecode(m.Groups[1].Value);
                    if (Regex.IsMatch(src, @"jquery|bootstrap|cdn|datatables|select2|moment|sweetalert|popper|fontawesome", RegexOptions.IgnoreCase)) continue;
                    if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !src.Contains("les-ejemplo", StringComparison.OrdinalIgnoreCase)) continue;
                    try { await les.Crudo(new Uri(new Uri(les.Base), src).ToString()); } catch (Exception e) { cap.Nota($"script {src}: {e.Message}"); }
                }
                try { await les.Sla(); } catch (Exception e) { cap.Nota("SLA: " + e.Message); }
                var lista = (await les.Lista()) as JsonArray ?? new JsonArray();
                cap.Nota($"LES: {lista.Count} ingreso(s) pendiente(s)");
                foreach (var x in lista.OfType<JsonObject>().Take(15))
                {
                    var id = L(x, "Id");
                    try
                    {
                        var det = await les.Detalle(id);
                        var cli = (int)L(det["ingreso"], "ClienteId");
                        var cods = (det["articulos"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(a => S(a, "Codigo") ?? "").Where(c => c.Length > 0).ToList();
                        if (cli > 0) { if (!empresas.TryGetValue(cli, out var h)) empresas[cli] = h = new(); foreach (var c in cods) h.Add(c); }
                        try { await les.FotosPallets(id); } catch { }
                        if (cods.Count > 0)
                        {
                            try { await les.FotosPop(id, cli, cods); } catch { }
                            try { await les.FotosContenedor(id, cods); } catch { }
                        }
                    }
                    catch (Exception e) { cap.Nota($"ingreso {id}: {e.Message}"); }
                }
            }
            catch (Exception e) { cap.Nota("LES: " + e.Message); }
            foreach (var f in _db.Q("SELECT cliente_id, articulos FROM ingresos ORDER BY actualizado DESC LIMIT 10"))
            {
                var cli = (int)(f.L("cliente_id") ?? 0); if (cli <= 0) continue;
                if (!empresas.TryGetValue(cli, out var h)) empresas[cli] = h = new();
                foreach (var a in (JsonNode.Parse(f.S("articulos") ?? "[]") as JsonArray ?? new JsonArray()).OfType<JsonObject>()) if (S(a, "Codigo") is { Length: > 0 } c) h.Add(c);
            }
            // ---- WMS escritorio
            try
            {
                var notas = await Wms(usuario).Diagnostico(empresas.ToDictionary(k => k.Key, v => v.Value.Take(8).ToList()));
                foreach (var n in notas) cap.Nota("WMS: " + n);
            }
            catch (Exception e) { cap.Nota("WMS escritorio: " + e.Message); }
            // ---- handheld
            foreach (var emp in empresas.Keys.Take(4))
                try { cap.Nota("Handheld: " + await sv.SesionDe(usuario).DiagnosticoRecepciones(emp)); }
                catch (Exception e) { cap.Nota($"Handheld empresa {emp}: {e.Message}"); }
            return null;
        });
        cap.Nota("Fin del diagnóstico.");
        return Captura.Zip(cfg);
    }

    // ================================================================ carátulas y cierre

    public async Task<byte[]> CaratulasPdf(string usuario, long id) => await Les(usuario).CaratulaPdf(id);

    public async Task<object> ImprimirCaratulas(string usuario, long id)
    {
        var pdf = await CaratulasPdf(usuario, id);
        try
        {
            Caratula.Imprimir(pdf, null, null, null, cfg.Produccion.Impresora, 1, $"SONA ingreso {id} - carátulas");
            Evento(id, usuario, "caratulas", true, "Carátulas de los pallets enviadas a la impresora.");
            return new { ok = true };
        }
        catch (Exception e) { Evento(id, usuario, "caratulas", false, e.Message); return new { ok = false, mensaje = e.Message }; }
    }

    /// <summary>Carátulas como imágenes para imprimirlas desde la PC de quien las pide (diálogo del navegador).</summary>
    public async Task<List<byte[]>> ImagenesCaratulas(string usuario, long id)
    {
        var pdf = await CaratulasPdf(usuario, id);
        List<byte[]> pngs;
        try { pngs = Caratula.Imagenes(pdf); }
        catch (Exception e) when (e is not ErrorUsuario) { throw new ErrorUsuario("El LES no devolvió un PDF de carátulas válido: " + e.Message, 502); }
        Evento(id, usuario, "caratulas", true, $"Carátulas de los pallets abiertas para imprimir desde la PC ({pngs.Count} hoja(s)).");
        return pngs;
    }

    /// <summary>"Procesar" en el LES: pasa a Pend. validar.</summary>
    public Task<object> Cerrar(string usuario, long id) => Candado<object>(usuario, id, async () =>
    {
        try
        {
            await Les(usuario).ConfirmarIngresoWms(id);
            _db.Ejecutar("UPDATE ingresos SET cerrado=$2, estado_les='PEND VALIDAR' WHERE les_id=$1", id, Db.Ahora());
            Evento(id, usuario, "cierre", true, "Ingreso procesado en el LES (Pend. validar).");
            return new { ok = true };
        }
        catch (ErrorUsuario e) { Evento(id, usuario, "cierre", false, e.Message); throw; }
    });
}
