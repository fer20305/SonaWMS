using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sona.Dominio;
using Sona.Wms;

namespace Sona;

/// <summary>Lo que manda el celular por cada movimiento. El id lo genera el celular: reenviar nunca duplica.</summary>
public sealed class MovimientoEntrada
{
    public string Id { get; set; } = "";
    public string? Grupo { get; set; }
    public string Tipo { get; set; } = "";
    public string Contenedor { get; set; } = "";
    public string? Origen { get; set; }
    public string? Destino { get; set; }
    public int? Nivel { get; set; }
    public bool? Disponible { get; set; }
    public string? Motivo { get; set; }
    /// <summary>Reubicar cajas: cantidad que se pasa del contenedor origen al destino.</summary>
    public double? Cantidad { get; set; }
    /// <summary>Multicontenedor: se agrupan contenedores de distintas empresas a propósito (no pide autorización de zona).</summary>
    public bool? Multi { get; set; }
    /// <summary>Ajuste: true = suma, false/null = resta.</summary>
    public bool? Sumar { get; set; }
    /// <summary>Reubicar cajas: dónde está el contenedor que recibe (Ubic.Ctrl o código de posición), con el nivel en Nivel.</summary>
    public string? Posicion_destino { get; set; }
    /// <summary>Transferir posición completa con todos los contenedores elegidos: al terminar, SONA vuelve a mirar el origen y mueve los que hayan quedado.</summary>
    public bool? Completa { get; set; }
    public List<string>? Fotos { get; set; }
    public string? Comentario { get; set; }
    public string? Hora_celular { get; set; }
}

public sealed record ResultadoMov(string Id, string Estado, string? Mensaje = null, long? Autorizacion = null);

public sealed class ErrorUsuario(string mensaje, int codigo = 400) : Exception(mensaje) { public int Codigo { get; } = codigo; }

/// <summary>Registro de movimientos, autorizaciones del analista y la cola que los carga en el WMS.</summary>
public sealed class Movimientos(Servicios sv, Eventos ev, ILogger<Movimientos> log)
{
    readonly Db _db = sv.Db;
    readonly SemaphoreSlim _despertador = new(0);
    public void Despertar() { if (_despertador.CurrentCount == 0) _despertador.Release(); }

    static readonly HashSet<string> Tipos = new() { "transferencia", "guardado", "cambio_estado", "reubicar_cajas", "ajuste" };

    public async Task<List<ResultadoMov>> Registrar(string usuario, string? dispositivo, List<MovimientoEntrada> lista, string rol = "", HashSet<string>? permisos = null)
    {
        var res = new List<ResultadoMov>();
        var pedir = new Dictionary<string, string>();
        // cada tipo de movimiento necesita su permiso (ZUSR › permisos); sin lista de permisos (uso interno) se mira el rol como antes
        var nn = _db.Parametro("nn_ubic", 5422L).ToString();
        string PermisoDe(MovimientoEntrada m) => m.Tipo switch
        {
            // a la posición NN alcanza con "Enviar a NN" (o con "Transferir")
            "transferencia" => (m.Destino ?? "").Trim() == nn ? "enviar_nn" : "transferir",
            "guardado" => "guardar", "cambio_estado" => "cambiar_estado", "reubicar_cajas" => "reubicar_cajas",
            "ajuste" => m.Cantidad == null ? "eliminar" : "ajustar", _ => "transferir",
        };
        if (permisos != null)
        {
            var falta = lista.Select(PermisoDe).Distinct().Where(p => !permisos.Contains(p) && !(p == "enviar_nn" && permisos.Contains("transferir"))).ToList();
            if (falta.Count > 0) throw new ErrorUsuario("Tu usuario no tiene permiso para: " + string.Join(", ", falta.Select(f => Permisos.Catalogo.FirstOrDefault(x => x.Codigo == f)?.Nombre ?? f)) + ". Pedíselo a un administrador (ZUSR).", 403);
        }
        else if (rol is "picker" or "supervisor" or "produccion" && lista.Count > 0) throw new ErrorUsuario("Tu usuario no puede hacer movimientos de stock.", 403);
        foreach (var m in lista)
        {
            if (!Guid.TryParse(m.Id, out _) || !Tipos.Contains(m.Tipo) || string.IsNullOrWhiteSpace(m.Contenedor)) throw new ErrorUsuario("Movimiento inválido (id, tipo o contenedor).");
            var ya = _db.Q1("SELECT estado, respuesta_wms FROM movimientos WHERE id=$1", m.Id);
            if (ya != null) { res.Add(new ResultadoMov(m.Id, ya.S("estado")!, ya.S("respuesta_wms"))); continue; }
            var grupo = m.Grupo ?? Guid.NewGuid().ToString();
            var contenedor = m.Contenedor.Trim().ToUpperInvariant();
            var info = await sv.InfoContenedor(contenedor, false);
            string? destino = null; long? destinoUbic = null; var nivel = m.Nivel;
            var estado = "pendiente_wms";
            var comentario = m.Comentario;

            if (m.Tipo is "transferencia" or "guardado")
            {
                if (string.IsNullOrWhiteSpace(m.Destino)) throw new ErrorUsuario("Falta el destino.");
                var d = await sv.ResolverPosicion(m.Destino, nivel, usuario);
                if (d == null || d.Nivel == null) { estado = "rechazado"; comentario = $"Destino \"{m.Destino}\" no reconocido o sin nivel."; }
                else
                {
                    destino = d.Codigo; destinoUbic = d.UbicCtrl; nivel = d.Nivel;
                    var ficha = _db.Q1("SELECT empresa, habilitada FROM pos_efectiva WHERE codigo=$1", d.Codigo);
                    if (ficha != null && !ficha.B("habilitada")) { estado = "rechazado"; comentario = "Destino inhabilitado."; }
                    else if (m.Multi != true && ficha?.I("empresa") is int ez && info.Empresa != null && ez != info.Empresa)
                    {
                        if (_db.Q1("SELECT 1 AS x FROM autorizaciones WHERE grupo=$1 AND estado='aprobada'", grupo) == null)
                        {
                            estado = "esperando_autorizacion";
                            pedir[grupo] = $"Mover a {d.Codigo} (zona de {sv.NombreEmpresa(ez) ?? "empresa " + ez}) material de {info.EmpresaNombre ?? "otra empresa"}";
                        }
                    }
                }
            }
            double? cantidadMov = info.Cantidad;
            if (m.Tipo == "reubicar_cajas")
            {
                // destino = contenedor que recibe las cajas
                destino = m.Destino?.Trim().ToUpperInvariant();
                cantidadMov = m.Cantidad;
                nivel = null;
                // el handheld pide dónde está el contenedor que recibe (Ubic.Ctrl + nivel): lo manda el celular o sale del buscador
                if (!string.IsNullOrWhiteSpace(m.Posicion_destino))
                {
                    var pd = await sv.ResolverPosicion(m.Posicion_destino, m.Nivel, usuario);
                    if (pd?.UbicCtrl != null && pd.Nivel != null) { destinoUbic = pd.UbicCtrl; nivel = pd.Nivel; }
                }
                if (string.IsNullOrEmpty(destino)) { estado = "rechazado"; comentario = "Falta el contenedor destino."; }
                else if (destino == contenedor) { estado = "rechazado"; comentario = "El contenedor destino es el mismo que el origen."; }
                else if (m.Cantidad is not > 0) { estado = "rechazado"; comentario = "La cantidad tiene que ser mayor que cero."; }
                else if (info.Cantidad != null && m.Cantidad > info.Cantidad + 1e-6) { estado = "rechazado"; comentario = $"El contenedor {contenedor} tiene {info.Cantidad} y se quieren pasar {m.Cantidad}."; }
                else
                {
                    var infoDest = await sv.InfoContenedor(destino, false);
                    if (infoDest.Empresa != null && info.Empresa != null && infoDest.Empresa != info.Empresa)
                    { estado = "rechazado"; comentario = $"{destino} es de {infoDest.EmpresaNombre ?? "otra empresa"} y {contenedor} de {info.EmpresaNombre ?? "otra"}: no se pueden pasar cajas entre empresas."; }
                }
            }
            if (m.Tipo == "ajuste")
            {
                // Cantidad null = eliminar (se resta todo lo que tenga): solo administradores. Ajustar (sumar/restar): administradores y analistas.
                if (permisos == null)
                {
                    if (m.Cantidad == null && rol != "admin") throw new ErrorUsuario("Solo los administradores pueden eliminar contenedores.", 403);
                    if (rol is not ("admin" or "analista")) throw new ErrorUsuario("Solo administradores y analistas pueden ajustar contenedores.", 403);
                }
                if (string.IsNullOrWhiteSpace(m.Motivo)) { estado = "rechazado"; comentario = "Falta el motivo del ajuste."; }
                else if (m.Cantidad is <= 0) { estado = "rechazado"; comentario = "La cantidad del ajuste tiene que ser mayor que cero."; }
                else if (m.Cantidad != null && m.Sumar != true && info.Cantidad != null && m.Cantidad > info.Cantidad + 1e-6)
                { estado = "rechazado"; comentario = $"El contenedor tiene {info.Cantidad} y se quieren restar {m.Cantidad}."; }
                cantidadMov = m.Cantidad;
                nivel = null;
            }
            string? estadoNuevo = null;
            if (m.Tipo == "cambio_estado")
            {
                estadoNuevo = m.Disponible == true ? "DISPONIBLE" : "NO DISPONIBLE" + (m.Motivo != null ? " – " + m.Motivo.ToUpperInvariant() : "");
                var obligatorias = _db.Parametro("fotos_obligatorias", new List<string> { "ROTURA", "NO CONFORME" }).Select(x => x.ToUpperInvariant());
                if (m.Disponible != true && m.Motivo != null && obligatorias.Any(o => m.Motivo.ToUpperInvariant().Contains(o)) && (m.Fotos == null || m.Fotos.Count == 0))
                { estado = "rechazado"; comentario = $"Para \"{m.Motivo}\" la foto es obligatoria."; }
            }
            var detalle = new JsonObject { ["disponible"] = m.Disponible, ["motivo"] = m.Motivo, ["descripcion"] = info.Descripcion, ["m3"] = info.M3 };
            if (m.Tipo == "reubicar_cajas") { detalle["cantidad_origen"] = info.Cantidad; detalle["contenedor_destino"] = destino; }
            if (m.Tipo == "ajuste") { detalle["cantidad_antes"] = info.Cantidad; detalle["eliminar"] = m.Cantidad == null; detalle["sumar"] = m.Sumar == true; }
            if (m.Multi == true) detalle["multi"] = true;
            if (m.Completa == true && m.Tipo == "transferencia") detalle["completa"] = true;
            _db.Ejecutar("""
                INSERT OR IGNORE INTO movimientos (id, grupo, tipo, contenedor, articulo, cantidad, empresa, origen, destino, destino_ubic, nivel,
                  estado_nuevo, usuario, dispositivo, hora_celular, hora_servidor, estado, comentario, fotos, detalle)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20)
                """, m.Id, grupo, m.Tipo, contenedor, info.Articulo, cantidadMov, info.Empresa, m.Origen ?? Posicion.Normalizar(info.Posicion),
                destino, destinoUbic, nivel, estadoNuevo, usuario, dispositivo, m.Hora_celular, Db.Ahora(), estado, comentario, m.Fotos ?? new List<string>(), detalle);
            res.Add(new ResultadoMov(m.Id, estado, comentario));
            ev.Emitir(new() { ["tipo"] = "movimiento", ["id"] = m.Id, ["estado"] = estado, ["usuario"] = usuario, ["mensaje"] = estado == "rechazado" ? comentario : null, ["contenedor"] = contenedor });
        }
        foreach (var (grupo, motivo) in pedir)
        {
            var id = SolicitarAutorizacion(grupo, motivo, usuario);
            for (var i = 0; i < res.Count; i++) if (res[i].Autorizacion == null) res[i] = res[i] with { Autorizacion = id };
        }
        Despertar();
        return res;
    }

    public long SolicitarAutorizacion(string grupo, string motivo, string usuario)
    {
        var ya = _db.Q1("SELECT id FROM autorizaciones WHERE grupo=$1 AND estado='pendiente'", grupo);
        if (ya != null) return ya.L("id")!.Value;
        var id = Convert.ToInt64(_db.Escalar("INSERT INTO autorizaciones (grupo, motivo, solicitado_por, creado) VALUES ($1,$2,$3,$4) RETURNING id", grupo, motivo, usuario, Db.Ahora()));
        ev.Emitir(new() { ["tipo"] = "autorizacion", ["id"] = id, ["estado"] = "pendiente", ["motivo"] = motivo, ["usuario"] = usuario },
            (u, rol) => Seguridad.RolesPanel.Contains(rol) || u == usuario);
        return id;
    }

    /// <summary>El analista aprueba o rechaza (panel o celular de analista).</summary>
    public void ResolverAutorizacion(long id, bool aprobar, string analista, string? nota)
    {
        var a = _db.Transaccion((c, t) =>
        {
            var fila = Db.Q(c, t, "SELECT * FROM autorizaciones WHERE id=$1 AND estado='pendiente'", id).FirstOrDefault()
                       ?? throw new ErrorUsuario("La autorización ya fue resuelta.");
            Db.Ejecutar(c, t, "UPDATE autorizaciones SET estado=$2, resuelto_por=$3, resuelto_en=$4 WHERE id=$1", id, aprobar ? "aprobada" : "rechazada", analista, Db.Ahora());
            Db.Ejecutar(c, t, "UPDATE movimientos SET estado=$2, comentario=$3 WHERE grupo=$1 AND estado='esperando_autorizacion'",
                fila.S("grupo"), aprobar ? "pendiente_wms" : "rechazado", aprobar ? $"Autorizado por {analista}" : $"Rechazado por {analista}" + (string.IsNullOrEmpty(nota) ? "" : ": " + nota));
            return fila;
        });
        ev.Emitir(new() { ["tipo"] = "autorizacion", ["id"] = id, ["estado"] = aprobar ? "aprobada" : "rechazada", ["grupo"] = a.S("grupo"), ["resuelto_por"] = analista, ["solicitado_por"] = a.S("solicitado_por") });
        Despertar();
    }

    public bool CancelarAutorizacion(long id, string usuario, bool esPanel)
    {
        var a = _db.Q1("SELECT grupo, solicitado_por FROM autorizaciones WHERE id=$1 AND estado='pendiente'", id);
        if (a == null || (!esPanel && a.S("solicitado_por") != usuario)) return false;
        _db.Ejecutar("UPDATE autorizaciones SET estado='cancelada', resuelto_por=$2, resuelto_en=$3 WHERE id=$1", id, usuario, Db.Ahora());
        _db.Ejecutar("UPDATE movimientos SET estado='anulado', comentario='Solicitud cancelada' WHERE grupo=$1 AND estado='esperando_autorizacion'", a.S("grupo"));
        ev.Emitir(new() { ["tipo"] = "autorizacion", ["id"] = id, ["estado"] = "cancelada", ["grupo"] = a.S("grupo") });
        return true;
    }

    /// <summary>Anular: si no se cargó en el WMS se cancela; si ya se cargó, se registra la anulación.</summary>
    public string Anular(string id, string usuario, string motivo)
    {
        var m = _db.Q1("SELECT * FROM movimientos WHERE id=$1", id) ?? throw new ErrorUsuario("No existe el movimiento.", 404);
        var estado = m.S("estado");
        if (estado is "pendiente_wms" or "error" or "esperando_autorizacion")
        {
            _db.Ejecutar("UPDATE movimientos SET estado='anulado', comentario=$2 WHERE id=$1", id, $"Anulado por {usuario}: {motivo}");
            ev.Emitir(new() { ["tipo"] = "movimiento", ["id"] = id, ["estado"] = "anulado" });
            return "Anulado (no se había cargado en el WMS).";
        }
        if (estado == "aplicado")
        {
            _db.Ejecutar("""
                INSERT INTO movimientos (id, grupo, tipo, contenedor, articulo, empresa, origen, destino, usuario, hora_servidor, estado, comentario, detalle)
                VALUES ($1,$2,'anulacion',$3,$4,$5,$6,$7,$8,$9,'registrado',$10,$11)
                """, Guid.NewGuid().ToString(), m.S("grupo"), m.S("contenedor"), m.S("articulo"), m.I("empresa"), m.S("destino"), m.S("origen"), usuario, Db.Ahora(), motivo,
                new JsonObject { ["anula"] = id });
            return "Ya estaba cargado en el WMS: quedó registrada la anulación; hay que mover el contenedor de vuelta.";
        }
        throw new ErrorUsuario($"No se puede anular un movimiento en estado {estado}.");
    }

    public void Reintentar(string id)
    {
        _db.Ejecutar("UPDATE movimientos SET estado='pendiente_wms', intentos=0, proximo_intento=NULL WHERE id=$1 AND estado='error'", id);
        Despertar();
    }

    // ================================================================ cola: carga en el WMS
    public async Task Correr(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var hubo = false;
            try
            {
                var lote = _db.Q("SELECT * FROM movimientos WHERE estado='pendiente_wms' AND (proximo_intento IS NULL OR proximo_intento <= $1) ORDER BY hora_servidor LIMIT 25", Db.Ahora());
                hubo = lote.Count > 0;
                // de a uno por usuario y en orden (el WMS tiene una sesión por usuario); usuarios distintos en paralelo
                await Task.WhenAll(lote.GroupBy(m => m.S("usuario")).Select(async g => { foreach (var m in g) await Aplicar(m); }));
            }
            catch (Exception e) { log.LogError(e, "Error en la cola de movimientos"); }
            try { await _despertador.WaitAsync(hubo ? 500 : 5000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    async Task Aplicar(Fila m)
    {
        bool ok = false, definitivo = true;
        var mensaje = "";
        var id = m.S("id")!; var cont = m.S("contenedor")!; var usuario = m.S("usuario")!;
        try
        {
            var s = sv.SesionDe(usuario);
            var empresa = m.I("empresa");
            if (empresa == null)
            {
                sv.OlvidarContenedor(cont);
                empresa = (await sv.InfoContenedor(cont, false)).Empresa ?? throw new WmsError("No se pudo determinar la empresa del contenedor (buscador SONA).");
            }
            var tipo = m.S("tipo");
            if (tipo is "transferencia" or "guardado")
            {
                var ubic = m.L("destino_ubic") ?? _db.Q1("SELECT ubic_ctrl FROM posiciones WHERE codigo=$1", m.S("destino"))?.L("ubic_ctrl")
                           ?? throw new WmsError($"La posición {m.S("destino")} no tiene Ubic.Ctrl: sincronizá el layout.");
                var r = tipo == "guardado" ? await s.Guardar(empresa.Value, cont, ubic, m.I("nivel") ?? 1) : await s.Reubicar(empresa.Value, cont, ubic, m.I("nivel") ?? 1);
                ok = r.Ok; mensaje = r.Mensaje;
                if (ok)
                {
                    _db.Ejecutar("INSERT INTO contenedores (codigo, posicion, empresa, actualizado) VALUES ($1,$2,$3,$4) ON CONFLICT(codigo) DO UPDATE SET posicion=excluded.posicion, actualizado=excluded.actualizado",
                        cont, m.S("destino"), empresa, Db.Ahora());
                    if (r.Origen != null && m.S("origen") == null) _db.Ejecutar("UPDATE movimientos SET origen=$2 WHERE id=$1", id, Posicion.Normalizar(r.Origen));
                    if (r.SugeridaWms != null) _db.Ejecutar("UPDATE movimientos SET detalle=json_set(COALESCE(detalle,'{}'),'$.sugerida_wms',$2) WHERE id=$1", id, r.SugeridaWms);
                }
            }
            else if (tipo == "cambio_estado")
            {
                var d = m["detalle"] as JsonObject;
                var disp = d?["disponible"]?.GetValue<bool>() ?? false;
                var motivo = d?["motivo"]?.GetValue<string>();
                var r = await s.CambiarDisponibilidad(empresa.Value, cont, disp, motivo);
                ok = r.Ok; mensaje = r.Mensaje;
                if (ok) _db.Ejecutar("UPDATE contenedores SET disponible=$2, motivo=$3, actualizado=$4 WHERE codigo=$1", cont, disp, motivo, Db.Ahora());
            }
            else if (tipo == "reubicar_cajas")
            {
                var destino = m.S("destino") ?? throw new WmsError("Falta el contenedor destino.");
                // el handheld pide también dónde está el contenedor destino (Ubic.Ctrl + nivel), como lo carga el operario:
                // primero lo que escaneó el operario; si no, la posición del buscador (y si SONA no conoce su Ubic.Ctrl, lee el layout de esa cara)
                long? ubicDest = m.L("destino_ubic"); int? nivDest = m.I("nivel");
                if (ubicDest == null || nivDest == null)
                {
                    sv.OlvidarContenedor(destino);
                    var posDest = (await sv.InfoContenedor(destino, false)).Posicion;
                    (ubicDest, nivDest) = sv.UbicacionDe(posDest);
                    if (ubicDest == null && Posicion.Interpretar(Posicion.Normalizar(posDest) ?? "", sv.CarasConocidas()) is { } pp)
                    {
                        try { await sv.RefrescarCara(usuario, pp.Cara, TimeSpan.Zero); } catch { }
                        (ubicDest, nivDest) = sv.UbicacionDe(posDest);
                    }
                    if (ubicDest == null || nivDest == null)
                        throw new WmsError($"No se sabe la posición (Ubic.Ctrl y nivel) de {destino}" + (posDest != null ? $" ({posDest})" : "") + ": escaneá la posición del contenedor que recibe.");
                }
                var r = await s.ReubicarCajas(empresa.Value, cont, destino, m.D("cantidad") ?? 0, ubicDest, nivDest);
                ok = r.Ok; mensaje = r.Mensaje;
                sv.OlvidarContenedor(destino);
            }
            else if (tipo == "ajuste")
            {
                var d = m["detalle"] as JsonObject;
                var eliminar = d?["eliminar"]?.GetValue<bool>() ?? false;
                var sumar = d?["sumar"]?.GetValue<bool>() ?? false;
                var r = await s.AjustarContenedor(empresa.Value, cont, eliminar ? null : m.D("cantidad"), sumar, d?["motivo"]?.GetValue<string>() ?? "INVENTARIO");
                ok = r.Ok; mensaje = r.Mensaje;
                if (ok && eliminar) _db.Ejecutar("UPDATE contenedores SET cantidad=0, posicion=NULL, actualizado=$2 WHERE codigo=$1", cont, Db.Ahora());
            }
            else { ok = true; mensaje = "Registrado"; }
        }
        catch (Exception e)
        {
            mensaje = e.Message;
            // sin respuesta del WMS (red) se reintenta; si el WMS contestó con error, queda en error
            definitivo = e is WmsError && !System.Text.RegularExpressions.Regex.IsMatch(mensaje, "llegar|conect|timeout|tiempo|socket|host", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        sv.OlvidarContenedor(cont);
        var intentos = (m.I("intentos") ?? 0) + 1;
        var estado = ok ? "aplicado" : !definitivo && intentos < 20 ? "pendiente_wms" : "error";
        _db.Ejecutar("UPDATE movimientos SET estado=$2, respuesta_wms=$3, intentos=$4, proximo_intento=$5, aplicado_en=CASE WHEN $2='aplicado' THEN $6 ELSE aplicado_en END WHERE id=$1",
            id, estado, mensaje, intentos, estado == "pendiente_wms" ? Db.Iso(DateTime.UtcNow.AddSeconds(Math.Min(intentos, 10) * 30)) : null, Db.Ahora());
        if (estado != "pendiente_wms") ev.Emitir(new() { ["tipo"] = "movimiento", ["id"] = id, ["estado"] = estado, ["mensaje"] = mensaje, ["usuario"] = usuario, ["contenedor"] = cont });
        log.LogInformation("WMS {Tipo} {Cont}: {Estado} {Mensaje}", m.S("tipo"), cont, estado, mensaje);
        // Posición completa: cuando terminan todos los del grupo, se mira el origen otra vez por si quedó alguno que no se había leído
        if (ok && m.S("tipo") == "transferencia" && (m["detalle"] as JsonObject)?["completa"]?.GetValue<bool>() == true && m.S("grupo") is { } g
            && _db.Contar("SELECT count(*) FROM movimientos WHERE grupo=$1 AND estado IN ('pendiente_wms','esperando_autorizacion')", g) == 0
            && _completando.TryAdd(g, 0))
            _ = Task.Run(async () =>
            {
                try { await CompletarPosicion(g); }
                catch (Exception e) { log.LogWarning("No se pudo revisar el origen del grupo {G}: {E}", g, e.Message); }
                finally { _completando.TryRemove(g, out _); }
            });
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _completando = new();

    /// <summary>
    /// Transferir posición completa: el handheld muestra los huecos de a 5 y a veces SONA no llega a leer todos. Cuando ya se movieron los
    /// leídos, el WMS muestra los que quedaban en la primera página: se leen de nuevo y se mueven al mismo destino (hasta 5 vueltas).
    /// </summary>
    public async Task CompletarPosicion(string grupo)
    {
        var movs = _db.Q("SELECT * FROM movimientos WHERE grupo=$1 AND tipo='transferencia' ORDER BY hora_servidor", grupo);
        var primero = movs.FirstOrDefault(x => x.S("origen") != null && x.S("destino") != null);
        if (primero == null) return;
        var ronda = movs.Max(x => (x["detalle"] as JsonObject)?["ronda"]?.GetValue<int>() ?? 0);
        if (ronda >= 5) return;
        var origen = primero.S("origen")!; var usuario = primero.S("usuario")!;
        var p = Posicion.Interpretar(origen, sv.CarasConocidas());
        if (p?.Nivel == null) return;
        var o = await sv.Ocupacion(usuario, p.Cara, p.Columna, p.Nivel.Value);
        if (o == null) return;
        var yaEnGrupo = movs.Select(x => x.S("contenedor")!.ToUpperInvariant()).ToHashSet();
        var quedan = o.Contenedores.Select(x => x.ToUpperInvariant()).Where(x => !yaEnGrupo.Contains(x)).ToList();
        if (quedan.Count == 0) return;
        foreach (var c in quedan)
        {
            var info = await sv.InfoContenedor(c, false);
            var id = Guid.NewGuid().ToString();
            _db.Ejecutar("""
                INSERT OR IGNORE INTO movimientos (id, grupo, tipo, contenedor, articulo, cantidad, empresa, origen, destino, destino_ubic, nivel, usuario, dispositivo,
                  hora_servidor, estado, comentario, detalle)
                VALUES ($1,$2,'transferencia',$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,'pendiente_wms',$14,$15)
                """, id, grupo, c, info.Articulo, info.Cantidad, info.Empresa, origen, primero.S("destino"), primero.L("destino_ubic"), primero.I("nivel"), usuario,
                primero.S("dispositivo"), Db.Ahora(), "Agregado por SONA: quedaba en el origen al transferir la posición completa",
                new JsonObject { ["completa"] = true, ["ronda"] = ronda + 1, ["descripcion"] = info.Descripcion, ["multi"] = (primero["detalle"] as JsonObject)?["multi"]?.GetValue<bool>() });
            ev.Emitir(new() { ["tipo"] = "movimiento", ["id"] = id, ["estado"] = "pendiente_wms", ["usuario"] = usuario, ["contenedor"] = c });
        }
        ev.Emitir(new() { ["tipo"] = "aviso", ["texto"] = $"En {origen} quedaban {quedan.Count} contenedor(es) más ({string.Join(", ", quedan)}): SONA los transfiere también a {primero.S("destino")}." },
            (u, rol) => u == usuario || Seguridad.RolesPanel.Contains(rol));
        log.LogInformation("Posición completa {Origen}: se agregan {N} contenedores que quedaban ({Lista})", origen, quedan.Count, string.Join(", ", quedan));
        Despertar();
    }
}

/// <summary>La cola corre en segundo plano mientras el servidor esté prendido.</summary>
public sealed class ColaWms(Movimientos mov) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => mov.Correr(ct);
}
