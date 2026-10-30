using System.Globalization;
using System.Text.RegularExpressions;

namespace Sona.Wms;

/// <summary>OP tal como la muestran las bandejas de egresos del WMS de escritorio (Pendientes de Aceptar / Pendientes de Preparar).</summary>
public sealed record OpWms(
    string Op, string Tipo, int Empresa, string EmpresaNombre, string? Fecha, string Destino, string? Solicitud, string? Remito, string? Lugar,
    int Articulos, double Unidades, double Kg, double M3, string? Viaje, string? GrupoPicking, int? Prioridad,
    string? Picker, int? Avance, bool Comenzada, string? Fila)
{
    /// <summary>Solo en "Pendientes de Aceptar": el WMS marca picking / lote / vencimiento no disponible.</summary>
    public string? NoDisponible { get; init; }
}

public sealed record PickerWms(string Codigo, string Nombre);

public sealed record AsignacionWms(bool Ok, string Mensaje, byte[]? Pdf, string? PickerNombre, bool SinFaltantes);

/// <summary>
/// Sesión del WMS de escritorio (hinicio.aspx) para el panel de producción: lee las bandejas de egresos y hace la asignación
/// igual que el operador: elegir el picker → "Sim de Faltantes" (PDF/carátula) → "Confirmación SIN Depuración".
/// Las operaciones de una sesión van de a una.
/// </summary>
public sealed partial class SesionEscritorio
{
    readonly Configuracion _cfg;
    readonly Navegador _nav;
    readonly string _usuario, _clave;
    readonly SemaphoreSlim _uso = new(1, 1);
    string? _base;

    public string Usuario => _usuario;
    /// <summary>Captura de todas las pantallas (diagnóstico).</summary>
    public Action<string, string, Respuesta>? Traza { get => _nav.Traza; set => _nav.Traza = value; }

    public SesionEscritorio(Configuracion cfg, string usuario, string clave)
    {
        _cfg = cfg; _usuario = usuario.ToUpperInvariant(); _clave = clave;
        _nav = new Navegador(Math.Max(60, cfg.Wms.TimeoutSegundos));
    }

    async Task<string> Login()
    {
        Exception? ultimo = null;
        foreach (var b in _cfg.BasesWms)
        {
            try
            {
                _nav.Vaciar();
                var ini = await _nav.Get(b + "hinicio.aspx");
                var f = Gx.Leer(ini.Texto);
                var boton = f.Botones.FirstOrDefault(x => x.Valor.Contains("INGRES", StringComparison.OrdinalIgnoreCase))
                            ?? f.Botones.FirstOrDefault(x => x.Nombre.Equals("BUTTON3", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f);
                var r = await _nav.Post(new Uri(new Uri(b), string.IsNullOrEmpty(f.Accion) ? "hinicio.aspx" : f.Accion),
                    Gx.Envio(f, boton, new() { ["_USR"] = _usuario, ["_PASSWORD"] = _clave }), b + "hinicio.aspx");
                if (!Regex.IsMatch(r.Texto, "Bienvenid", RegexOptions.IgnoreCase))
                {
                    var msj = Gx.Mensajes(Gx.Leer(r.Texto));
                    throw new WmsError("El WMS no aceptó el ingreso de " + _usuario + (msj.Length > 0 ? ": " + msj : "."));
                }
                _base = b;
                return b;
            }
            catch (WmsError) { throw; }
            catch (Exception e) { ultimo = e; }
        }
        throw new WmsError("No se pudo llegar al WMS. " + ultimo?.Message);
    }

    async Task<T> EnSerie<T>(Func<Task<T>> fn)
    {
        await _uso.WaitAsync();
        try { return await fn(); } finally { _uso.Release(); }
    }

    /// <summary>Abre una pantalla del WMS (si la sesión venció, vuelve a ingresar).</summary>
    /// <param name="marca">Algo que tiene que estar en la pantalla (ej. el botón Buscar). Si no está, la sesión del WMS se
    /// perdió (otro ingreso con el mismo usuario, sesión vencida, pantalla intermedia): se vuelve a ingresar y se reintenta.</param>
    async Task<Respuesta> Abrir(string pagina, string? marca = null)
    {
        bool Ok(Respuesta x) => !Gx.EsLogin(x.Texto) && x.Texto.Contains("MAINFORM", StringComparison.OrdinalIgnoreCase)
                                && (marca == null || x.Texto.Contains(marca, StringComparison.OrdinalIgnoreCase));
        var b = _base ?? await Login();
        var r = await _nav.Get(b + pagina);
        if (!Ok(r))
        {
            b = await Login();
            r = await _nav.Get(b + pagina);
        }
        if (Gx.EsLogin(r.Texto)) throw new WmsError("El WMS volvió a pedir el ingreso al abrir " + pagina + ".");
        if (!Ok(r))
        {
            GuardarPantalla("pantalla_inesperada_" + pagina.Split('?')[0], r);
            var titulo = System.Text.RegularExpressions.Regex.Match(r.Texto, @"<title>\s*([^<]*?)\s*</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
            var m = MensajesPantalla(r.Texto);
            throw new WmsError($"El WMS no mostró la pantalla esperada ({pagina})" + (titulo.Length > 0 ? $": mostró \"{titulo}\"" : "") + (m.Length > 0 ? " - " + m : "") +
                               ". Puede que el usuario del WMS no tenga permiso o que se haya abierto en otro lado; la pantalla quedó guardada en wms_paginas.");
        }
        return r;
    }

    void GuardarPantalla(string nombre, Respuesta r)
    {
        try
        {
            var dir = Path.Combine(_cfg.Carpeta, "wms_paginas");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{nombre}_{DateTime.Now:yyyyMMdd_HHmmss}.html"), $"<!-- {r.Url} -->\n" + r.Texto);
        }
        catch { }
    }

    async Task<Respuesta> Enviar(Respuesta pag, Gx.Boton? boton, Dictionary<string, string> cambios)
    {
        var f = Gx.Leer(pag.Texto);
        var destino = string.IsNullOrEmpty(f.Accion) ? pag.Url : new Uri(pag.Url, f.Accion);
        var r = await _nav.Post(destino, Gx.Envio(f, boton, cambios), pag.Url.ToString());
        if (Gx.EsLogin(r.Texto)) { _base = null; throw new WmsError("El WMS cerró la sesión: probá de nuevo."); }
        return r;
    }

    static Gx.Boton Evento(string evento) => new("", "", evento, "", "", false);

    static Gx.Boton BotonDe(Gx.Formulario f, string nombre, string? evento = null) =>
        f.Botones.FirstOrDefault(b => b.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase))
        ?? (evento != null ? f.Botones.FirstOrDefault(b => b.Evento.Equals(evento, StringComparison.OrdinalIgnoreCase)) : null)
        ?? throw new WmsError($"No encontré el botón {nombre} en la pantalla del WMS (¿cambió la pantalla o el usuario no tiene permiso?).");

    // ================================================================ lectura de grillas

    static double Dbl(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var t = s.Trim().Replace(".", "").Replace(",", ".");
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }
    static int Ent(string? s) => (int)Math.Round(Dbl(s));
    static string? Nn(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>"(DT) Rem:160224 / 9189-125 / Depósito Oeste 10" → remito 160224, solicitud 9189-125, lugar "Depósito Oeste 10".</summary>
    public static (string? Remito, string? Solicitud, string? Lugar) Partes(string? destino)
    {
        if (string.IsNullOrWhiteSpace(destino)) return (null, null, null);
        var rem = Regex.Match(destino, @"Rem\s*:\s*(\d+)", RegexOptions.IgnoreCase);
        var sol = Regex.Match(destino, @"(?<![\d-])(\d{2,6}-\d{1,5})(?![\d-])");
        var partes = destino.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var lugar = partes.Length >= 3 ? string.Join(" / ", partes.Skip(2)) : partes.Length == 1 && !rem.Success ? partes[0] : null;
        return (rem.Success ? rem.Groups[1].Value : null, sol.Success ? sol.Groups[1].Value : null, Nn(lugar));
    }

    static IEnumerable<string> Filas(Gx.Formulario f, string columnaOp) =>
        f.Valores.Keys.Where(k => k.StartsWith(columnaOp + "_", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(k, @"_\d{4}$"))
            .Select(k => k[^4..]).Distinct().OrderBy(x => x);

    /// <summary>Grilla de "Órdenes Pendientes de Aceptar" (hpenddeautorizar.aspx).</summary>
    public static List<OpWms> LeerPendientesAceptar(string html)
    {
        var f = Gx.Leer(html);
        var g = Gx.Grilla(html);
        string V(string col, string fila) => f.Valores.GetValueOrDefault(col + "_" + fila) ?? g.GetValueOrDefault(fila)?.GetValueOrDefault(col) ?? "";
        var lista = new List<OpWms>();
        foreach (var i in Filas(f, "PENDNROAPL"))
        {
            var op = V("PENDNROAPL", i).Trim();
            if (op.Length == 0 || op == "0") continue;
            var destino = V("_DESTINO", i).Trim();
            var (rem, sol, lugar) = Partes(destino);
            var picker = Nn(V("_NEWUSU", i));
            var nd = new List<string>();
            if (Nn(V("PENDPICKINGNODISP", i)) is { } pk && pk != "0" && !pk.Equals("N", StringComparison.OrdinalIgnoreCase)) nd.Add("picking " + pk);
            if (Nn(V("PENDLOTENODISP", i)) is { } lt && lt != "0" && !lt.Equals("N", StringComparison.OrdinalIgnoreCase)) nd.Add("lote " + lt);
            if (Nn(V("PENDVTONODISPONIBLE", i)) is { } vt && Regex.IsMatch(vt, @"\d")) nd.Add("vto. " + vt);
            lista.Add(new OpWms(op, V("PENDTOAPLI", i).Trim(), Ent(V("PENDEMP", i)), V("_EMPDSC", i).Trim(), Nn(V("PENDFHPROG", i)), destino, sol, rem, lugar,
                Ent(V("_SUMART", i)), Dbl(V("_SUMUNI", i)), Dbl(V("_SUMKG", i)), Dbl(V("_SUMM3", i)),
                Nn(V("_PENDVIAJESNROEXTERNO", i)), Nn(V("_GRUPOPICKING", i)), Ent(V("PENDPRIOR", i)) is var p && p > 0 ? p : null,
                picker, null, false, i) { NoDisponible = nd.Count > 0 ? string.Join(", ", nd) : null });
        }
        return lista;
    }

    /// <summary>Grilla de "Órdenes Pendientes de Preparación" (hasigusuario.aspx): picker asignado y avance.</summary>
    public static List<OpWms> LeerPendientesPreparar(string html)
    {
        var f = Gx.Leer(html);
        var g = Gx.Grilla(html);
        string V(string col, string fila) => f.Valores.GetValueOrDefault(col + "_" + fila) ?? g.GetValueOrDefault(fila)?.GetValueOrDefault(col) ?? "";
        var lista = new List<OpWms>();
        foreach (var i in Filas(f, "ASGNROAPL"))
        {
            var op = V("ASGNROAPL", i).Trim();
            if (op.Length == 0 || op == "0") continue;
            var destino = V("_DESTINO", i).Trim();
            var (rem, sol, lugar) = Partes(destino);
            // avance: artículos totales (_CANTPARANATORIG) y pendientes (_CANTTOTAL); si no, la barrita (Images/Barras/70.bmp)
            int? avance = null;
            var tot = Dbl(V("_CANTPARANATORIG", i)); var pend = Dbl(V("_CANTTOTAL", i));
            if (tot > 0 && pend >= 0 && pend <= tot) avance = (int)Math.Floor(100.0 * (tot - pend) / tot);
            else if (Regex.Match(V("GXimg_IMGBARRITA", i), @"(\d+)\.bmp", RegexOptions.IgnoreCase) is { Success: true } m) avance = int.Parse(m.Groups[1].Value);
            var comenzo = Nn(V("_YACOMENZOAPREP", i)) is { } y && !y.Equals("N", StringComparison.OrdinalIgnoreCase) || (avance ?? 0) > 0;
            lista.Add(new OpWms(op, V("ASGTOAPLI", i).Trim(), Ent(V("ASGEMP", i)), V("_EMPDSC", i).Trim(), Nn(V("ASGFHPROG", i)), destino, sol, rem, lugar,
                Ent(V("_SUMART", i)), Dbl(V("_SUMUNI", i)), Dbl(V("_SUMKG", i)), Dbl(V("_SUMM3", i)),
                Nn(V("_VIAJE", i)) ?? Nn(V("VIAJESNROEXTERNO", i)), Nn(V("_GRUPOPICKING", i)), Ent(V("_ASGPRIOR", i)) is var p && p > 0 ? p : null,
                Nn(V("_NEWUSU", i)), avance, comenzo, i));
        }
        return lista;
    }

    /// <summary>Pickers (usuarios) que ofrece el WMS para asignar: código y nombre.</summary>
    public static List<PickerWms> LeerPickers(string html)
    {
        var f = Gx.Leer(html);
        var ops = f.Selects.Where(kv => kv.Key.StartsWith("_NEWUSU_") || kv.Key is "_OPCUSUARIO" or "_USUARIOASIGMASIVA").SelectMany(kv => kv.Value);
        return ops.Where(o => o.Valor.Trim().Length > 0 && !o.Valor.Equals("Asignar", StringComparison.OrdinalIgnoreCase))
            .GroupBy(o => o.Valor.Trim().ToUpperInvariant()).Select(gr => new PickerWms(gr.Key, gr.First().Texto.Trim()))
            .OrderBy(p => p.Nombre).ToList();
    }

    // ================================================================ operaciones

    List<PickerWms> _pickers = new();
    public List<PickerWms> Pickers => _pickers;

    async Task<Respuesta> PantallaAceptar(int empresa)
    {
        var pag = await Abrir("hpenddeautorizar.aspx", "BUSCARCOD2");
        return await Enviar(pag, Evento("E_EMPRESA.CLICK."), new() { ["_EMPRESA"] = empresa.ToString() });
    }

    /// <summary>Órdenes pendientes de aceptar de todas las empresas ("Pre-visualizar Todas").</summary>
    public Task<List<OpWms>> PendientesAceptar() => EnSerie(async () =>
    {
        var r = await PantallaAceptar(9999);
        var p = LeerPickers(r.Texto);
        if (p.Count > 0) _pickers = p;
        return LeerPendientesAceptar(r.Texto);
    });

    async Task<Respuesta> PantallaPreparar()
    {
        var pag = await Abrir("hasigusuario.aspx", "BUSCARCOD4");
        var f = Gx.Leer(pag.Texto);
        return await Enviar(pag, BotonDe(f, "BUSCARCOD4", "E'CODIGO POSTAL'."), new() { ["_EMPRESA"] = "0", ["_SOLOASIGNADAS"] = "Todas" });
    }

    /// <summary>Órdenes pendientes de preparación (asignadas y no asignadas) con el picker y el avance.</summary>
    public Task<List<OpWms>> PendientesPreparar() => EnSerie(async () =>
    {
        var r = await PantallaPreparar();
        if (_pickers.Count == 0) { var p = LeerPickers(r.Texto); if (p.Count > 0) _pickers = p; }
        return LeerPendientesPreparar(r.Texto);
    });

    static string? Fila(List<OpWms> l, string op, string tipo) =>
        l.FirstOrDefault(x => x.Op == op && (tipo.Length == 0 || x.Tipo.Equals(tipo, StringComparison.OrdinalIgnoreCase)))?.Fila;

    static string NombreDe(Gx.Formulario f, string fila, string picker) =>
        f.Selects.GetValueOrDefault("_NEWUSU_" + fila)?.FirstOrDefault(o => o.Valor.Trim().Equals(picker, StringComparison.OrdinalIgnoreCase))?.Texto.Trim() ?? picker;

    static string MensajesPantalla(string html)
    {
        var f = Gx.Leer(html);
        var m = new List<string>();
        var g = Gx.Mensajes(f); if (g.Length > 0) m.Add(g);
        foreach (var k in new[] { "_MSG2", "_MENSAJE", "_MSGLIMITEDECARGA" })
            if (f.Valores.TryGetValue(k, out var v) && v.Trim().Length > 1 && v.Trim() != "0") m.Add(v.Trim());
        return string.Join(" ", m.Distinct());
    }

    /// <summary>
    /// Asigna la OP como lo hace el operador en "Órdenes Pendientes de Aceptar": tilda la OP, elige el picker, "Sim de Faltantes"
    /// (baja el PDF: la carátula, que después el WMS no deja volver a imprimir) y "Confirmación SIN Depuración".
    /// Si el PDF no llega, NO confirma.
    /// </summary>
    public Task<AsignacionWms> Asignar(int empresa, string tipo, string op, string picker) => EnSerie(async () =>
    {
        picker = picker.Trim().ToUpperInvariant();
        var r = await PantallaAceptar(empresa);
        var fila = Fila(LeerPendientesAceptar(r.Texto), op, tipo)
                   ?? throw new WmsError($"La OP {op} ya no está en \"Pendientes de Aceptar\" (¿la aceptó otro usuario?).");
        var f = Gx.Leer(r.Texto);
        var nombre = NombreDe(f, fila, picker);
        if (f.Selects.TryGetValue("_NEWUSU_" + fila, out var opciones) && !opciones.Any(o => o.Valor.Trim().Equals(picker, StringComparison.OrdinalIgnoreCase)))
            throw new WmsError($"El WMS no ofrece a {picker} como preparador de esta OP.");

        // 1) Sim de Faltantes → PDF
        var sim = await Enviar(r, BotonDe(f, "BOTONSIMULAR", "E'SIMULAR'."), new() { ["_OP_" + fila] = "1", ["_NEWUSU_" + fila] = picker });
        var link = Regex.Match(sim.Texto, @"href=""(osimulacionpdfcomboysustituto\.aspx\?[^""]+)""", RegexOptions.IgnoreCase);
        if (!link.Success)
        {
            var m = MensajesPantalla(sim.Texto);
            throw new WmsError("El WMS no generó la simulación de faltantes" + (m.Length > 0 ? ": " + m : ".") + " No se confirmó la OP.");
        }
        var (pdf, _, _) = await _nav.Bajar(new Uri(sim.Url, Gx.Desescapar(link.Groups[1].Value)), sim.Url.ToString());
        if (pdf.Length < 5 || pdf[0] != '%' || pdf[1] != 'P' || pdf[2] != 'D' || pdf[3] != 'F')
            throw new WmsError("El WMS no devolvió el PDF de la simulación de faltantes. No se confirmó la OP.");

        // 2) Confirmación SIN Depuración (con la OP tildada y el picker elegido)
        var fs = Gx.Leer(sim.Texto);
        var fila2 = Fila(LeerPendientesAceptar(sim.Texto), op, tipo) ?? fila;
        var conf = await Enviar(sim, BotonDe(fs, "CONFSINDEPURACION", "E'CONFSINDEPURACION'."), new() { ["_OP_" + fila2] = "1", ["_NEWUSU_" + fila2] = picker });
        var sigue = Fila(LeerPendientesAceptar(conf.Texto), op, tipo) != null;
        var msj = MensajesPantalla(conf.Texto);
        var sinFaltantes = Regex.IsMatch(Gx.Texto(conf.Texto), @"Faltantes\s+N\b");
        if (sigue)
            return new AsignacionWms(false, "El WMS no confirmó la OP" + (msj.Length > 0 ? ": " + msj : " (sigue en Pendientes de Aceptar)."), pdf, nombre, sinFaltantes);
        return new AsignacionWms(true, msj, pdf, nombre, sinFaltantes);
    });

    /// <summary>Cambia el picker de una OP que ya está en "Pendientes de Preparación" ("Confirmar Cambios").</summary>
    public Task<AsignacionWms> Reasignar(string tipo, string op, string picker) => EnSerie(async () =>
    {
        picker = picker.Trim().ToUpperInvariant();
        var r = await PantallaPreparar();
        var lista = LeerPendientesPreparar(r.Texto);
        var fila = Fila(lista, op, tipo) ?? throw new WmsError($"La OP {op} no está en \"Pendientes de Preparación\".");
        var f = Gx.Leer(r.Texto);
        var nombre = NombreDe(f, fila, picker);
        var conf = await Enviar(r, BotonDe(f, "CONFIRMAR2", "E'ACTUALIZAR'."), new() { ["_OP_" + fila] = "1", ["_NEWUSU_" + fila] = picker });
        var ahora = LeerPendientesPreparar(conf.Texto).FirstOrDefault(x => x.Op == op && x.Tipo.Equals(tipo, StringComparison.OrdinalIgnoreCase));
        if (ahora == null)   // la grilla vuelve vacía: se busca de nuevo
            ahora = LeerPendientesPreparar((await PantallaPreparar()).Texto).FirstOrDefault(x => x.Op == op && x.Tipo.Equals(tipo, StringComparison.OrdinalIgnoreCase));
        var ok = ahora?.Picker?.Equals(picker, StringComparison.OrdinalIgnoreCase) == true;
        var msj = MensajesPantalla(conf.Texto);
        return new AsignacionWms(ok, ok ? msj : "El WMS no tomó el cambio de picker" + (msj.Length > 0 ? ": " + msj : "."), null, nombre, false);
    });

    /// <summary>
    /// Cambia la prioridad de preparación de una OP aceptada (Pendientes de Preparación: columna Prioridad, _ASGPRIOR, y "Confirmar Cambios").
    /// El WMS le da al picker primero las OP según esta prioridad.
    /// </summary>
    public Task<(bool Ok, string Mensaje, int? Antes)> CambiarPrioridad(string tipo, string op, int prioridad) => EnSerie(async () =>
    {
        var r = await PantallaPreparar();
        var lista = LeerPendientesPreparar(r.Texto);
        var o = lista.FirstOrDefault(x => x.Op == op && (tipo.Length == 0 || x.Tipo.Equals(tipo, StringComparison.OrdinalIgnoreCase)))
                ?? throw new WmsError($"La OP {op} no está en \"Pendientes de Preparación\".");
        var fila = o.Fila!;
        var f = Gx.Leer(r.Texto);
        var cambios = new Dictionary<string, string> { ["_OP_" + fila] = "1", ["_ASGPRIOR_" + fila] = prioridad.ToString() };
        if (o.Picker is { Length: > 0 } pk) cambios["_NEWUSU_" + fila] = pk;   // que no le cambie el picker
        var conf = await Enviar(r, BotonDe(f, "CONFIRMAR2", "E'ACTUALIZAR'."), cambios);
        var ahora = LeerPendientesPreparar(conf.Texto).FirstOrDefault(x => x.Op == op && x.Tipo.Equals(o.Tipo, StringComparison.OrdinalIgnoreCase))
                    ?? LeerPendientesPreparar((await PantallaPreparar()).Texto).FirstOrDefault(x => x.Op == op && x.Tipo.Equals(o.Tipo, StringComparison.OrdinalIgnoreCase));
        var ok = ahora?.Prioridad == prioridad;
        var msj = MensajesPantalla(conf.Texto);
        return (ok, ok ? $"Prioridad de la OP {op}: {prioridad}." : "El WMS no tomó el cambio de prioridad" + (msj.Length > 0 ? ": " + msj : "."), o.Prioridad);
    });

    /// <summary>"Ver OP" (overoppdf.aspx): el PDF de la orden, para reimprimir cuando no hay carátula guardada.</summary>
    public Task<byte[]?> VerOp(int empresa, string tipo, string op, bool aceptada) => EnSerie(async () =>
    {
        var b = _base ?? await Login();
        var etapa = aceptada ? "PEND+PREP,,N,PREPARACION" : "PEND+AUTO,,N,AUTORIZACION";
        var (datos, _, url) = await _nav.Bajar(new Uri($"{b}overoppdf.aspx?{empresa},1,{tipo},ORIG,{op},{etapa}"), b + "hasigusuario.aspx");
        if (datos.Length > 4 && datos[0] == '%' && datos[1] == 'P') return datos;
        // a veces devuelve una página con el PDF incrustado
        var html = System.Text.Encoding.UTF8.GetString(datos);
        var m = Regex.Match(html, @"(?:src|href|data)=""([^""]+\.pdf[^""]*)""", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var (d2, _, _) = await _nav.Bajar(new Uri(url, Gx.Desescapar(m.Groups[1].Value)), url.ToString());
        return d2.Length > 4 && d2[0] == '%' && d2[1] == 'P' ? d2 : null;
    });
}
