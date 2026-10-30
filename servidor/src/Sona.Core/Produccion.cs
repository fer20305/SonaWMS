using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sona.Wms;

namespace Sona;

/// <summary>
/// Panel de producción: el sector que asigna los pedidos a los pickers y sigue el pickeo.
/// Lee del WMS de escritorio "Pendientes de Aceptar" (sin asignar) y "Pendientes de Preparación" (asignadas, con avance),
/// suma lo que sabe SONA (faltantes y OPs postergadas = stand by) y asigna en un paso: Sim de Faltantes → guarda el PDF →
/// Confirmación SIN Depuración → imprime la carátula con el picker.
/// </summary>
public sealed class Produccion(Configuracion cfg, Servicios sv, Eventos ev, Mensajes mensajes, ILogger<Produccion> log)
{
    public const string Esquema = """
CREATE TABLE IF NOT EXISTS produccion_caratulas (
  id INTEGER PRIMARY KEY AUTOINCREMENT, empresa INTEGER NOT NULL, tipo TEXT NOT NULL, op TEXT NOT NULL, empresa_nombre TEXT, destino TEXT,
  solicitud TEXT, viaje TEXT, picker TEXT, picker_nombre TEXT, asignado_por TEXT, wms_usuario TEXT, hora TEXT NOT NULL, origen TEXT NOT NULL DEFAULT 'simulacion',
  archivo TEXT, archivo_sellado TEXT, pie TEXT, renglones INTEGER, con_faltante INTEGER, estado TEXT NOT NULL, mensaje TEXT,
  impresiones INTEGER NOT NULL DEFAULT 0, ultima_impresion TEXT, error_impresion TEXT);
CREATE INDEX IF NOT EXISTS produccion_caratulas_op ON produccion_caratulas(empresa, tipo, op);
CREATE INDEX IF NOT EXISTS produccion_caratulas_hora ON produccion_caratulas(hora);
""";

    /// <summary>Pueden asignar, reimprimir y elegir la impresora. El supervisor solo mira.</summary>
    public static readonly string[] RolesAsignan = { "produccion", "analista", "admin" };
    public static readonly string[] RolesVen = { "produccion", "supervisor", "analista", "admin" };

    readonly Db _db = sv.Db;

    // ================================================================ prioridad de preparación

    /// <summary>Niveles que se eligen al asignar; el número que se pone en el WMS depende de cómo ordena el WMS.</summary>
    public static readonly string[] Niveles = { "urgente", "alta", "normal", "baja" };
    bool MayorPrimero => cfg.Produccion.PrioridadMayorPrimero;
    public int ValorPrioridad(string? nivel) => (nivel ?? "normal").Trim().ToLowerInvariant() switch
    {
        "urgente" => MayorPrimero ? 90 : 10, "alta" => MayorPrimero ? 70 : 30, "baja" => MayorPrimero ? 30 : 70, _ => 50,
    };
    /// <summary>La que se usa cuando el picker elige una OP: queda primera para el WMS.</summary>
    int PrioridadPrimera => MayorPrimero ? 9999 : 1;
    public string NivelDe(int? p)
    {
        if (p == null) return "normal";
        var v = MayorPrimero ? p.Value : 100 - p.Value;   // v alto = más urgente
        return v >= 80 ? "urgente" : v >= 60 ? "alta" : v > 40 ? "normal" : "baja";
    }
    /// <summary>Orden en que el WMS le da las OP al picker (las empezadas primero).</summary>
    public IEnumerable<OpWms> OrdenWms(IEnumerable<OpWms> ops) =>
        ops.OrderByDescending(o => o.Comenzada).ThenBy(o => MayorPrimero ? -(o.Prioridad ?? 50) : (o.Prioridad ?? 50)).ThenBy(o => o.Fecha);
    readonly ConcurrentDictionary<string, SesionEscritorio> _sesiones = new();
    string? _ultimoUsuario;

    string Carpeta()
    {
        var d = Path.Combine(cfg.Carpeta, "caratulas", DateTime.Now.ToString("yyyy-MM"));
        Directory.CreateDirectory(d);
        return d;
    }

    // ================================================================ sesión del WMS

    (string Usuario, string Clave)? Credenciales(string? usuario)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Produccion.Usuario) && cfg.ClaveProduccion.Length > 0) return (cfg.Produccion.Usuario.ToUpperInvariant(), cfg.ClaveProduccion);
        foreach (var u in new[] { usuario, _ultimoUsuario }.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var cl = _db.Q1("SELECT clave_cifrada FROM usuarios WHERE usuario=$1", u!.ToUpperInvariant())?.S("clave_cifrada");
            if (!string.IsNullOrEmpty(cl)) return (u.ToUpperInvariant(), Seguridad.Descifrar(cl, cfg.Secreto));
        }
        return null;
    }

    SesionEscritorio Sesion(string? usuario)
    {
        var cr = Credenciales(usuario) ?? throw new ErrorUsuario("No hay un usuario del WMS para el panel de producción: ingresá al panel con tu usuario del WMS o configurá uno en ⚙ Configuración.");
        if (usuario != null && Seguridad.RolesLectura.Concat(RolesAsignan).Contains(_db.Q1("SELECT rol FROM usuarios WHERE usuario=$1", usuario.ToUpperInvariant())?.S("rol")))
            _ultimoUsuario = usuario.ToUpperInvariant();
        return _sesiones.GetOrAdd(cr.Usuario + "|" + cr.Clave.GetHashCode(), _ => new SesionEscritorio(cfg, cr.Usuario, cr.Clave));
    }

    // ================================================================ lectura (con caché corta)

    sealed record Instantanea(List<OpWms> Aceptar, List<OpWms> Preparar, List<PickerWms> Pickers, DateTime Hora, string Usuario);
    Instantanea? _ultima;
    string? _errorLectura;

    /// <summary>OPs de las dos bandejas del WMS (lo último leído si tiene menos de 5 minutos; si no, se lee).</summary>
    public async Task<List<OpWms>> OpsEnBandejas(string usuario)
    {
        var u = _ultima;
        if (u == null || DateTime.UtcNow - u.Hora > TimeSpan.FromMinutes(5)) u = await Leer(usuario, false);
        return u.Aceptar.Concat(u.Preparar).ToList();
    }
    readonly SemaphoreSlim _leyendo = new(1, 1);

    async Task<Instantanea> Leer(string? usuario, bool forzar)
    {
        var u = _ultima;
        if (!forzar && u != null && DateTime.UtcNow - u.Hora < TimeSpan.FromSeconds(20)) return u;
        await _leyendo.WaitAsync();
        try
        {
            u = _ultima;
            if (!forzar && u != null && DateTime.UtcNow - u.Hora < TimeSpan.FromSeconds(20)) return u;
            var s = Sesion(usuario);
            var aceptar = await s.PendientesAceptar();
            var preparar = await s.PendientesPreparar();
            _errorLectura = null;
            return _ultima = new Instantanea(aceptar, preparar, s.Pickers, DateTime.UtcNow, s.Usuario);
        }
        catch (Exception e) when (_ultima != null && e is WmsError or HttpRequestException or TaskCanceledException)
        {
            _errorLectura = "No se pudo actualizar desde el WMS: " + e.Message;
            log.LogWarning("Producción: {E}", e.Message);
            return _ultima;
        }
        finally { _leyendo.Release(); }
    }

    public void Olvidar() { _ultima = null; _sesiones.Clear(); }

    Fila? UltimaCaratula(int empresa, string tipo, string op) =>
        _db.Q1("SELECT * FROM produccion_caratulas WHERE empresa=$1 AND tipo=$2 AND op=$3 AND archivo IS NOT NULL ORDER BY id DESC LIMIT 1", empresa, tipo, op);

    /// <summary>
    /// Ruta del PDF guardado. Si la carpeta de datos se mudó (otra PC, servidor Linux), se busca en la carpeta de datos actual
    /// por mes y nombre de archivo.
    /// </summary>
    string? Archivo(string? guardado)
    {
        if (string.IsNullOrEmpty(guardado)) return null;
        if (File.Exists(guardado)) return guardado;
        var partes = guardado.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < 2) return null;
        var otra = Path.Combine(cfg.Carpeta, "caratulas", partes[^2], partes[^1]);
        return File.Exists(otra) ? otra : null;
    }

    static string Clave(int empresa, string tipo, string op) => $"{empresa}|{tipo}|{op}";

    /// <summary>Todo el tablero: para asignar, asignadas / en preparación, stand by por faltante, pickers y viajes.</summary>
    public async Task<object> Tablero(string usuario, bool forzar)
    {
        var t = await Leer(usuario, forzar);
        var nombres = t.Pickers.ToDictionary(p => p.Codigo, p => p.Nombre, StringComparer.OrdinalIgnoreCase);
        var sona = _db.Q("SELECT usuario, nombre, rol FROM usuarios").ToDictionary(f => f.S("usuario")!, f => f, StringComparer.OrdinalIgnoreCase);
        string? Nombre(string? cod) => cod == null ? null : nombres.GetValueOrDefault(cod) ?? sona.GetValueOrDefault(cod)?.S("nombre") ?? cod;

        // carátulas guardadas (para saber cuáles se pueden reimprimir)
        var caratulas = _db.Q("SELECT empresa, tipo, op, max(id) AS id, max(hora) AS hora, sum(impresiones) AS impresiones FROM produccion_caratulas WHERE archivo IS NOT NULL GROUP BY empresa, tipo, op")
            .ToDictionary(f => Clave((int)(f.L("empresa") ?? 0), f.S("tipo")!, f.S("op")!), f => f);

        // stand by: faltantes abiertos y OPs postergadas por los pickers (últimos 7 días, sin terminar)
        var faltantes = _db.Q("SELECT f.*, u.nombre AS usuario_nombre FROM faltantes f LEFT JOIN usuarios u ON u.usuario=f.usuario WHERE f.estado='abierto' AND f.op IS NOT NULL ORDER BY f.id DESC LIMIT 500");
        var postergadas = _db.Q("""
            SELECT op, usuario, max(hora) AS hora, max(detalle) AS detalle FROM picking_eventos
            WHERE tipo='postergar' AND op IS NOT NULL AND hora >= $1 AND op NOT IN (SELECT op FROM picking_eventos WHERE tipo='fin' AND op IS NOT NULL)
            GROUP BY op, usuario ORDER BY hora DESC LIMIT 200
            """, Db.Iso(DateTime.UtcNow.AddDays(-7)));
        var faltPorOp = faltantes.GroupBy(f => f.S("op")!).ToDictionary(g => g.Key, g => g.ToList());
        var postPorOp = postergadas.GroupBy(f => f.S("op")!).ToDictionary(g => g.Key, g => g.First());

        object Fila(OpWms o, bool asignada)
        {
            caratulas.TryGetValue(Clave(o.Empresa, o.Tipo, o.Op), out var car);
            faltPorOp.TryGetValue(o.Op, out var fl);
            postPorOp.TryGetValue(o.Op, out var pst);
            return new
            {
                op = o.Op, tipo = o.Tipo, empresa = o.Empresa, empresaNombre = o.EmpresaNombre, fecha = o.Fecha, destino = o.Destino, solicitud = o.Solicitud,
                remito = o.Remito, lugar = o.Lugar, articulos = o.Articulos, unidades = o.Unidades, kg = o.Kg, m3 = o.M3, viaje = o.Viaje, grupo = o.GrupoPicking,
                prioridad = o.Prioridad, prioridadNivel = NivelDe(o.Prioridad), picker = o.Picker, pickerNombre = Nombre(o.Picker), avance = o.Avance, comenzada = o.Comenzada, noDisponible = o.NoDisponible,
                asignada, caratula = car?.L("id"), caratulaHora = car?.S("hora"), impresiones = car?.L("impresiones") ?? 0,
                faltantes = fl?.Select(f => new { id = f.L("id"), articulo = f.S("articulo"), descripcion = f.S("descripcion"), cantidad = f.D("cantidad"), usuario = f.S("usuario"), hora = f.S("hora"), comentario = f.S("comentario") }).ToList(),
                postergada = pst == null ? null : new { usuario = pst.S("usuario"), hora = pst.S("hora"), detalle = pst.S("detalle") },
            };
        }

        var aAsignar = t.Aceptar.Select(o => Fila(o, false)).ToList();
        var preparacion = t.Preparar.Select(o => Fila(o, !string.IsNullOrEmpty(o.Picker))).ToList();
        // stand by que ya no está en las bandejas (ej. la OP salió de pendientes pero el faltante sigue abierto)
        var enBandejas = t.Aceptar.Concat(t.Preparar).Select(o => o.Op).ToHashSet();
        var sueltas = faltPorOp.Keys.Concat(postPorOp.Keys).Distinct().Where(op => !enBandejas.Contains(op)).Select(op => new
        {
            op,
            faltantes = faltPorOp.GetValueOrDefault(op)?.Select(f => new { id = f.L("id"), articulo = f.S("articulo"), descripcion = f.S("descripcion"), cantidad = f.D("cantidad"), usuario = f.S("usuario"), hora = f.S("hora"), comentario = f.S("comentario") }).ToList(),
            postergada = postPorOp.GetValueOrDefault(op) is { } p ? new { usuario = p.S("usuario"), hora = p.S("hora"), detalle = p.S("detalle") } : null,
            empresaNombre = faltPorOp.GetValueOrDefault(op)?.FirstOrDefault()?.L("empresa") is long e ? sv.NombreEmpresa((int)e) : null,
        }).ToList();

        // carga de cada picker: OPs asignadas pendientes y cuántas ya empezó
        var carga = t.Preparar.Where(o => !string.IsNullOrEmpty(o.Picker)).GroupBy(o => o.Picker!.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => new { ops = g.Count(), enCurso = g.Count(o => o.Comenzada), articulos = g.Sum(o => o.Articulos) });
        var pickersSona = sona.Values.Where(f => f.S("rol") == "picker").Select(f => f.S("usuario")!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pickers = t.Pickers.Select(p => new
        {
            codigo = p.Codigo, nombre = p.Nombre, esPicker = pickersSona.Contains(p.Codigo),
            ops = carga.GetValueOrDefault(p.Codigo)?.ops ?? 0, enCurso = carga.GetValueOrDefault(p.Codigo)?.enCurso ?? 0,
        }).OrderByDescending(p => p.esPicker).ThenBy(p => p.nombre).ToList();

        var viajes = t.Aceptar.Concat(t.Preparar).Select(o => o.Viaje).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v).ToList();
        return new
        {
            aAsignar, preparacion, standby = sueltas, pickers, viajes,
            actualizado = t.Hora.ToString("o"), usuarioWms = t.Usuario, error = _errorLectura,
            impresora = string.IsNullOrWhiteSpace(cfg.Produccion.Impresora) ? Caratula.ImpresoraPredeterminada() : cfg.Produccion.Impresora,
            imprimirAlAsignar = cfg.Produccion.ImprimirAlAsignar,
        };
    }

    // ================================================================ asignar

    readonly SemaphoreSlim _asignando = new(1, 1);

    public sealed record Resultado(bool Ok, string Mensaje, long? Caratula, bool Impresa, string? ErrorImpresion, List<RenglonSimulacion>? ConFaltante, string? PickerNombre);

    string Guardar(byte[] datos, string nombre)
    {
        var archivo = Path.Combine(Carpeta(), nombre);
        File.WriteAllBytes(archivo, datos);
        return archivo;
    }

    static string Seguro(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

    /// <summary>Imprime la carátula guardada (PDF del WMS) con el picker sellado.</summary>
    (bool Impresa, string? Error) ImprimirCaratula(long id)
    {
        try
        {
            var f = _db.Q1("SELECT * FROM produccion_caratulas WHERE id=$1", id) ?? throw new ErrorUsuario("No existe la carátula.");
            var arch = Archivo(f.S("archivo"));
            if (arch == null) throw new ErrorUsuario("El PDF de la carátula ya no está en la PC del servidor.");
            Caratula.Imprimir(File.ReadAllBytes(arch), f.S("picker_nombre") ?? f.S("picker") ?? "SIN ASIGNAR", f.S("picker"), f.S("pie"),
                cfg.Produccion.Impresora, cfg.Produccion.Copias, $"SONA carátula OP {f.S("op")} - {f.S("picker_nombre") ?? f.S("picker")}");
            _db.Ejecutar("UPDATE produccion_caratulas SET impresiones=impresiones+1, ultima_impresion=$2, error_impresion=NULL WHERE id=$1", id, Db.Ahora());
            return (true, null);
        }
        catch (Exception e)
        {
            log.LogWarning("Producción: no se pudo imprimir la carátula {Id}: {E}", id, e.Message);
            _db.Ejecutar("UPDATE produccion_caratulas SET error_impresion=$2 WHERE id=$1", id, e.Message);
            return (false, e.Message);
        }
    }

    void Avisar(string accion, string op, string? picker, string usuario, OpWms? info = null, int? prioridad = null)
    {
        ev.Emitir(new()
        {
            ["tipo"] = "produccion", ["accion"] = accion, ["op"] = op, ["picker"] = picker, ["por"] = usuario,
            ["empresa"] = info?.Empresa, ["empresaNombre"] = info?.EmpresaNombre, ["lugar"] = info?.Lugar ?? info?.Destino, ["solicitud"] = info?.Solicitud,
            ["articulos"] = info?.Articulos, ["unidades"] = info?.Unidades, ["viaje"] = info?.Viaje, ["tipoOp"] = info?.Tipo,
            ["prioridad"] = prioridad, ["prioridadNivel"] = prioridad != null ? NivelDe(prioridad) : null,
        },
            (u, rol) => RolesVen.Contains(rol) || (picker != null && u.Equals(picker, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<Resultado> Asignar(string usuario, int empresa, string tipo, string op, string picker, bool? imprimir, string? prioridad = null)
    {
        if (string.IsNullOrWhiteSpace(op) || string.IsNullOrWhiteSpace(picker)) throw new ErrorUsuario("Falta la OP o el picker.");
        tipo = (tipo ?? "").Trim().ToUpperInvariant(); op = op.Trim(); picker = picker.Trim().ToUpperInvariant();
        await _asignando.WaitAsync();
        try
        {
            var s = Sesion(usuario);
            var info = _ultima?.Aceptar.FirstOrDefault(x => x.Op == op && x.Tipo == tipo && x.Empresa == empresa);
            AsignacionWms r;
            try { r = await s.Asignar(empresa, tipo, op, picker); }
            catch (WmsError e)
            {
                _db.Ejecutar("""
                    INSERT INTO produccion_caratulas (empresa, tipo, op, empresa_nombre, destino, solicitud, viaje, picker, asignado_por, wms_usuario, hora, estado, mensaje)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,'error',$12)
                    """, empresa, tipo, op, info?.EmpresaNombre, info?.Destino, info?.Solicitud, info?.Viaje, picker, usuario, s.Usuario, Db.Ahora(), e.Message);
                throw;
            }
            string? original = null;
            var renglones = new List<RenglonSimulacion>();
            var nombre = r.PickerNombre ?? picker;
            if (r.Pdf != null)
            {
                // se guarda el PDF tal cual lo dio el WMS (el WMS no lo deja volver a imprimir); el sello se agrega al ver o imprimir
                original = Guardar(r.Pdf, $"OP{Seguro(op)}_{Seguro(tipo)}_{empresa}_{DateTime.Now:yyyyMMdd_HHmmss}_simulacion.pdf");
                renglones = Caratula.Renglones(r.Pdf);
            }
            var conFaltante = renglones.Where(x => x.Faltante > 0).ToList();
            // prioridad de preparación (la normal es la que pone el WMS: no se toca)
            int? valorPrioridad = null; string? avisoPrioridad = null;
            if (r.Ok && !string.IsNullOrWhiteSpace(prioridad) && !prioridad.Equals("normal", StringComparison.OrdinalIgnoreCase))
            {
                valorPrioridad = ValorPrioridad(prioridad);
                try { var (okp, mp, _) = await s.CambiarPrioridad(tipo, op, valorPrioridad.Value); if (!okp) { avisoPrioridad = mp; valorPrioridad = null; } }
                catch (WmsError e) { avisoPrioridad = "No se pudo poner la prioridad: " + e.Message; valorPrioridad = null; }
            }
            var id = Convert.ToInt64(_db.Escalar("""
                INSERT INTO produccion_caratulas (empresa, tipo, op, empresa_nombre, destino, solicitud, viaje, picker, picker_nombre, asignado_por, wms_usuario, hora,
                  archivo, pie, renglones, con_faltante, estado, mensaje, prioridad)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19) RETURNING id
                """, empresa, tipo, op, info?.EmpresaNombre, info?.Destino, info?.Solicitud, info?.Viaje, picker, nombre, usuario, s.Usuario, Db.Ahora(),
                original, $"OP {op} · asignada {DateTime.Now:dd/MM HH:mm} · {usuario}" + (valorPrioridad != null ? $" · {NivelDe(valorPrioridad).ToUpperInvariant()}" : ""),
                renglones.Count, conFaltante.Count, r.Ok ? "asignada" : "error", r.Mensaje + (avisoPrioridad != null ? " " + avisoPrioridad : ""), valorPrioridad));
            _ultima = null;   // la próxima lectura trae las bandejas actualizadas
            var (impresa, errImp) = (false, (string?)null);
            if (r.Ok && original != null && (imprimir ?? cfg.Produccion.ImprimirAlAsignar))
                (impresa, errImp) = ImprimirCaratula(id);
            if (r.Ok)
            {
                Avisar("asignada", op, picker, usuario, info, valorPrioridad ?? info?.Prioridad);
                try
                {
                    if (_db.Q1("SELECT 1 AS x FROM usuarios WHERE usuario=$1", picker) != null)
                        mensajes.Enviar(usuario, true, picker, $"📋 Te asignaron la OP {op}" + (valorPrioridad != null ? $" ({NivelDe(valorPrioridad).ToUpperInvariant()})" : "") + (info != null ? $" · {info.EmpresaNombre}" + (info.Lugar != null ? $" · {info.Lugar}" : "") + $" ({info.Articulos} art.)" : "") + ". Entrá a Picking para empezarla.");
                }
                catch { /* el aviso al picker no es crítico */ }
            }
            return new Resultado(r.Ok, r.Ok ? $"OP {op} asignada a {nombre}" + (valorPrioridad != null ? $" con prioridad {NivelDe(valorPrioridad)}" : "") + "." + (r.Mensaje.Length > 0 ? " " + r.Mensaje : "") + (avisoPrioridad != null ? " ⚠ " + avisoPrioridad : "") : r.Mensaje,
                id, impresa, errImp, conFaltante, nombre);
        }
        finally { _asignando.Release(); }
    }

    /// <summary>Cambia el picker de una OP ya aceptada. Si hay carátula guardada, se vuelve a sellar con el picker nuevo (y se imprime si se pide).</summary>
    public async Task<Resultado> Reasignar(string usuario, int empresa, string tipo, string op, string picker, bool imprimir)
    {
        tipo = (tipo ?? "").Trim().ToUpperInvariant(); op = op.Trim(); picker = picker.Trim().ToUpperInvariant();
        await _asignando.WaitAsync();
        try
        {
            var s = Sesion(usuario);
            var r = await s.Reasignar(tipo, op, picker);
            _ultima = null;
            if (!r.Ok) return new Resultado(false, r.Mensaje, null, false, null, null, r.PickerNombre);
            var nombre = r.PickerNombre ?? picker;
            long? id = null; var impresa = false; string? errImp = null;
            if (UltimaCaratula(empresa, tipo, op) is { } car)
            {
                id = Convert.ToInt64(_db.Escalar("""
                    INSERT INTO produccion_caratulas (empresa, tipo, op, empresa_nombre, destino, solicitud, viaje, picker, picker_nombre, asignado_por, wms_usuario, hora,
                      origen, archivo, pie, renglones, con_faltante, estado, mensaje)
                    SELECT empresa, tipo, op, empresa_nombre, destino, solicitud, viaje, $2, $3, $4, $5, $6, origen, archivo, $7, renglones, con_faltante, 'reasignada', $8
                    FROM produccion_caratulas WHERE id=$1 RETURNING id
                    """, car.L("id"), picker, nombre, usuario, s.Usuario, Db.Ahora(), $"OP {op} · reasignada {DateTime.Now:dd/MM HH:mm} · {usuario}", r.Mensaje));
                if (imprimir) (impresa, errImp) = ImprimirCaratula(id.Value);
            }
            var infoR = _ultima?.Preparar.FirstOrDefault(x => x.Op == op && x.Tipo == tipo);
            Avisar("reasignada", op, picker, usuario, infoR, infoR?.Prioridad);
            return new Resultado(true, $"OP {op} reasignada a {nombre}.", id, impresa, errImp, null, nombre);
        }
        finally { _asignando.Release(); }
    }

    /// <summary>Cambia la prioridad de preparación de una OP ya asignada (desde el panel).</summary>
    public async Task<object> CambiarPrioridad(string usuario, int empresa, string tipo, string op, string nivel)
    {
        tipo = (tipo ?? "").Trim().ToUpperInvariant(); op = op.Trim();
        if (!Niveles.Contains((nivel ?? "").ToLowerInvariant())) throw new ErrorUsuario("Prioridad inválida (urgente, alta, normal o baja).");
        var valor = ValorPrioridad(nivel);
        await _asignando.WaitAsync();
        try
        {
            var (ok, msj, _) = await Sesion(usuario).CambiarPrioridad(tipo, op, valor);
            _ultima = null;
            if (ok)
            {
                _db.Ejecutar("UPDATE produccion_caratulas SET prioridad=$4 WHERE id=(SELECT max(id) FROM produccion_caratulas WHERE empresa=$1 AND tipo=$2 AND op=$3)", empresa, tipo, op, valor);
                ev.Emitir(new() { ["tipo"] = "produccion", ["accion"] = "prioridad", ["op"] = op, ["por"] = usuario, ["prioridadNivel"] = NivelDe(valor) }, (u, rol) => RolesVen.Contains(rol));
            }
            return new { ok, mensaje = ok ? $"OP {op}: prioridad {NivelDe(valor)}." : msj, prioridad = valor, nivel = NivelDe(valor) };
        }
        finally { _asignando.Release(); }
    }

    /// <summary>
    /// El picker eligió una OP de su lista: se la pone primera en el WMS (prioridad máxima) para que el handheld se la dé al tomar la siguiente.
    /// Usa el usuario del WMS del panel de producción (Pendientes de Preparación). Si no se puede, el picker sigue igual con la que le dé el WMS.
    /// </summary>
    public async Task<(bool Ok, string? Aviso, OpWms? Op)> PonerPrimera(string picker, string op, string? tipo)
    {
        Instantanea t;
        try { t = await Leer(Credenciales(null) != null ? null : picker, true); }
        catch (Exception e) when (e is WmsError or ErrorUsuario or HttpRequestException or TaskCanceledException) { return (false, "No se pudo leer el WMS para ponerla primera: " + e.Message, null); }
        var o = t.Preparar.FirstOrDefault(x => x.Op == op.Trim() && (string.IsNullOrEmpty(tipo) || x.Tipo.Equals(tipo, StringComparison.OrdinalIgnoreCase)));
        if (o == null) return (false, $"La OP {op} ya no está pendiente de preparación.", null);
        if (!picker.Equals(o.Picker, StringComparison.OrdinalIgnoreCase)) return (false, $"La OP {op} está asignada a {o.Picker ?? "nadie"}.", o);
        if (o.Prioridad == PrioridadPrimera) return (true, null, o);
        await _asignando.WaitAsync();
        try
        {
            var (ok, msj, _) = await Sesion(Credenciales(null) != null ? null : picker).CambiarPrioridad(o.Tipo, o.Op, PrioridadPrimera);
            _ultima = null;
            return (ok, ok ? null : msj, o);
        }
        catch (Exception e) when (e is WmsError or ErrorUsuario) { return (false, e.Message, o); }
        finally { _asignando.Release(); }
    }

    /// <summary>Reimprime la última carátula guardada. Si no hay (la OP se asignó desde el WMS), usa "Ver OP" del WMS con el picker sellado.</summary>
    public async Task<Resultado> Reimprimir(string usuario, int empresa, string tipo, string op)
    {
        tipo = (tipo ?? "").Trim().ToUpperInvariant(); op = op.Trim();
        var car = UltimaCaratula(empresa, tipo, op);
        if (car != null && Archivo(car.S("archivo")) != null)
        {
            var (ok, err) = ImprimirCaratula(car.L("id")!.Value);
            return new Resultado(ok, ok ? $"Carátula de la OP {op} enviada a la impresora." : "No se pudo imprimir: " + err, car.L("id"), ok, err, null, car.S("picker_nombre"));
        }
        var id = await CaratulaDesdeWms(usuario, empresa, tipo, op);
        var fila = _db.Q1("SELECT * FROM produccion_caratulas WHERE id=$1", id)!;
        var (ok2, err2) = ImprimirCaratula(id);
        return new Resultado(ok2, ok2 ? $"No había carátula guardada: se imprimió el \"Ver OP\" del WMS de la OP {op}." : "No se pudo imprimir: " + err2, id, ok2, err2, null, fila.S("picker_nombre"));
    }

    async Task<long> CaratulaDesdeWms(string usuario, int empresa, string tipo, string op)
    {
        var s = Sesion(usuario);
        var t = await Leer(usuario, false);
        var o = t.Preparar.FirstOrDefault(x => x.Op == op && x.Tipo == tipo) ?? t.Aceptar.FirstOrDefault(x => x.Op == op && x.Tipo == tipo);
        var pdf = await s.VerOp(empresa, tipo, op, aceptada: t.Preparar.Any(x => x.Op == op && x.Tipo == tipo))
                  ?? throw new ErrorUsuario($"No hay carátula guardada de la OP {op} y el WMS no devolvió el PDF de \"Ver OP\".");
        var nombre = t.Pickers.FirstOrDefault(p => p.Codigo.Equals(o?.Picker, StringComparison.OrdinalIgnoreCase))?.Nombre ?? o?.Picker ?? "SIN ASIGNAR";
        var marca = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var original = Guardar(pdf, $"OP{Seguro(op)}_{Seguro(tipo)}_{empresa}_{marca}_verop.pdf");
        return Convert.ToInt64(_db.Escalar("""
            INSERT INTO produccion_caratulas (empresa, tipo, op, empresa_nombre, destino, solicitud, viaje, picker, picker_nombre, asignado_por, wms_usuario, hora, origen, archivo, pie, estado)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,'verop',$13,$14,'reimpresion') RETURNING id
            """, empresa, tipo, op, o?.EmpresaNombre, o?.Destino, o?.Solicitud, o?.Viaje, o?.Picker, nombre, usuario, s.Usuario, Db.Ahora(), original, $"OP {op} · {DateTime.Now:dd/MM HH:mm}"));
    }

    /// <summary>PDF de la carátula (sellada) para ver en el navegador.</summary>
    public async Task<(byte[] Datos, string Nombre)> Pdf(string usuario, int empresa, string tipo, string op, bool original)
    {
        tipo = (tipo ?? "").Trim().ToUpperInvariant();
        var car = UltimaCaratula(empresa, tipo, op);
        if (car == null) { var id = await CaratulaDesdeWms(usuario, empresa, tipo, op); car = _db.Q1("SELECT * FROM produccion_caratulas WHERE id=$1", id)!; }
        var arch = Archivo(car.S("archivo"));
        if (arch == null) throw new ErrorUsuario("El archivo de la carátula ya no está en la PC del servidor.", 404);
        var datos = File.ReadAllBytes(arch);
        if (original) return (datos, Path.GetFileName(arch));
        return (Caratula.Sellar(datos, car.S("picker_nombre") ?? car.S("picker") ?? "SIN ASIGNAR", car.S("picker"), car.S("pie")), $"Caratula_OP{op}_{car.S("picker")}.pdf");
    }

    /// <summary>
    /// Carátulas (con el picker sellado) de una o varias OPs, como imágenes, para imprimirlas desde la PC de quien las pide
    /// (diálogo de impresión del navegador). Cuenta la impresión igual que en el servidor.
    /// </summary>
    public async Task<(List<byte[]> Paginas, List<string> Avisos)> ImagenesParaImprimir(string usuario, IEnumerable<(int Empresa, string Tipo, string Op)> ops)
    {
        var paginas = new List<byte[]>(); var avisos = new List<string>();
        foreach (var (empresa, t, op) in ops)
        {
            try
            {
                var tipo = (t ?? "").Trim().ToUpperInvariant();
                var car = UltimaCaratula(empresa, tipo, op);
                if (car == null || Archivo(car.S("archivo")) == null) { var nuevo = await CaratulaDesdeWms(usuario, empresa, tipo, op); car = _db.Q1("SELECT * FROM produccion_caratulas WHERE id=$1", nuevo)!; }
                var arch = Archivo(car.S("archivo")) ?? throw new ErrorUsuario("El PDF de la carátula ya no está en la PC del servidor.");
                paginas.AddRange(Caratula.Imagenes(File.ReadAllBytes(arch), car.S("picker_nombre") ?? car.S("picker") ?? "SIN ASIGNAR", car.S("picker"), car.S("pie")));
                _db.Ejecutar("UPDATE produccion_caratulas SET impresiones=impresiones+1, ultima_impresion=$2, error_impresion=NULL WHERE id=$1", car.L("id"), Db.Ahora());
            }
            catch (Exception e) when (e is ErrorUsuario or WmsError or IOException)
            {
                avisos.Add($"OP {op}: {e.Message}");
            }
        }
        return (paginas, avisos);
    }

    public List<Fila> Historial(DateTime desde, DateTime hasta) => _db.Q("""
        SELECT c.id, c.hora, c.empresa, c.empresa_nombre, c.tipo, c.op, c.solicitud, c.destino, c.viaje, c.picker, c.picker_nombre, c.asignado_por, c.wms_usuario,
               c.origen, c.estado, c.mensaje, c.renglones, c.con_faltante, c.impresiones, c.ultima_impresion, c.error_impresion, (c.archivo IS NOT NULL) AS tiene_pdf
        FROM produccion_caratulas c WHERE c.hora >= $1 AND c.hora < $2 ORDER BY c.id DESC LIMIT 2000
        """, Db.InicioDia(desde), Db.InicioDia(hasta.AddDays(1)));

    // ================================================================ app del picker

    /// <summary>OPs asignadas al picker en el WMS (Pendientes de Preparación), con el avance. Se lee con el usuario del panel de producción.</summary>
    public async Task<object> MisOps(string picker)
    {
        Instantanea t;
        try { t = await Leer(Credenciales(null) != null ? null : picker, false); }
        catch (Exception e) when (e is WmsError or ErrorUsuario or HttpRequestException or TaskCanceledException)
        { return new { ops = Array.Empty<object>(), error = "No se pudo leer el WMS: " + e.Message }; }
        var faltantes = _db.Q("SELECT DISTINCT op FROM faltantes WHERE estado='abierto' AND op IS NOT NULL").Select(f => f.S("op")!).ToHashSet();
        var ops = OrdenWms(t.Preparar.Where(o => picker.Equals(o.Picker, StringComparison.OrdinalIgnoreCase)))
            .Select(o => new { op = o.Op, tipo = o.Tipo, empresa = o.Empresa, empresaNombre = o.EmpresaNombre, lugar = o.Lugar ?? o.Destino, solicitud = o.Solicitud,
                articulos = o.Articulos, unidades = o.Unidades, avance = o.Avance, comenzada = o.Comenzada, viaje = o.Viaje, prioridad = o.Prioridad, prioridadNivel = NivelDe(o.Prioridad), fecha = o.Fecha,
                standby = faltantes.Contains(o.Op) }).ToList();
        return new { ops, actualizado = t.Hora.ToString("o"), error = _errorLectura };
    }

    // ================================================================ configuración

    public object Config() => new
    {
        usuario = cfg.Produccion.Usuario, claveCargada = !string.IsNullOrEmpty(cfg.Produccion.Clave), impresora = cfg.Produccion.Impresora,
        imprimirAlAsignar = cfg.Produccion.ImprimirAlAsignar, copias = cfg.Produccion.Copias, prioridadMayorPrimero = cfg.Produccion.PrioridadMayorPrimero,
        niveles = Niveles.Select(n => new { nivel = n, valor = ValorPrioridad(n) }).ToList(),
        impresoras = Caratula.Impresoras(), predeterminada = Caratula.ImpresoraPredeterminada(), windows = OperatingSystem.IsWindows(),
    };

    public void ProbarImpresora()
    {
        var pdf = Caratula.PaginaPrueba($"Prueba de impresión · {DateTime.Now:dd/MM/yyyy HH:mm}");
        Caratula.Imprimir(pdf, "PRUEBA SONA", null, "Impresora: " + (string.IsNullOrWhiteSpace(cfg.Produccion.Impresora) ? "predeterminada" : cfg.Produccion.Impresora),
            cfg.Produccion.Impresora, 1, "SONA - prueba de impresora");
    }
}
