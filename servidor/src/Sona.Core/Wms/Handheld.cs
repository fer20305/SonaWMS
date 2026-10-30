using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Sona.Wms;

public sealed class WmsError(string mensaje) : Exception(mensaje);

public sealed record Hueco(string Profundidad, string? Contenedor, bool Inhabilitado, string? Estado = null);
public sealed record VistaHueco(string Cara, int Columna, int Nivel, string? Area, List<Hueco> Huecos, string? Mensaje)
{
    /// <summary>Contenedores que según el WMS hay en el nivel (huecos − vacíos − inhabilitados), si la pantalla lo informa.</summary>
    public int? Esperados { get; init; }
    /// <summary>Huecos totales del nivel (_I) e inhabilitados (_CANTINH) según el WMS: SONA deja de paginar cuando ya tiene todos los contenedores.</summary>
    public int? Total { get; init; }
    public int? InhabilitadosWms { get; init; }
}
public sealed record Pendiente(string Contenedor, string Recepcion, bool Guardada);
/// <summary>"Datos de Ubicaciones" del handheld (Ver un hueco › elegir la profundidad): situación real del contenedor en el WMS.</summary>
public sealed record DatosContenedorWms(string Contenedor, bool Existe, bool? Bloqueado, string? Motivo, double? Unidades, string? Articulo, string? Descripcion,
    string? Cara, int? Columna, int? Nivel, string? Profundidad, int? Empresa, string? Vencimiento, string? UltimaAccion, string? Mensaje)
{
    public string? Posicion => Cara is { Length: > 0 } && Columna != null && Nivel != null ? Sona.Dominio.Posicion.Codigo(Cara, Columna.Value, Nivel.Value) : null;
}
public sealed record ResultadoWms(bool Ok, string Mensaje, string? Origen = null, string? SugeridaWms = null);
public sealed record FilaLayout(string Cara, int Columna, int Nivel, int? UbicCtrl, string Tipo, int? Ancho, int? Alto, int? Prof, int? Capacidad,
    int? Empresa, string? Articulo, string Estado, bool Mezcla);

/// <summary>
/// Sesión del handheld del WMS (hdis.aspx → menú RF) para un usuario. Recorre las pantallas igual que el operario.
/// Prueba la red local y, si no responde, la externa. Las operaciones de un mismo usuario van de a una.
/// </summary>
public sealed partial class SesionHandheld
{
    readonly Configuracion _cfg;
    readonly Navegador _nav;
    readonly SemaphoreSlim _cola = new(1, 1);
    string _clave;
    string? _base;
    string? _usuarioWms;
    bool _logueado;

    public string Usuario { get; }
    public string? Red => _base;
    /// <summary>Captura de todas las pantallas (diagnóstico).</summary>
    public Action<string, string, Respuesta>? Traza { get => _nav.Traza; set => _nav.Traza = value; }
    public List<(int Codigo, string Nombre)> Empresas { get; private set; } = new();

    public SesionHandheld(Configuracion cfg, string usuario, string clave)
    {
        _cfg = cfg; Usuario = usuario.ToUpperInvariant(); _clave = clave;
        _nav = new Navegador(cfg.Wms.TimeoutSegundos);
    }

    async Task<T> EnSerie<T>(Func<Task<T>> fn)
    {
        await _cola.WaitAsync();
        try { return await fn(); } finally { _cola.Release(); }
    }

    // ------------------------------------------------------------------ login
    public async Task Login()
    {
        var bases = _cfg.BasesWms.ToList();
        if (_base != null) bases = bases.OrderBy(b => b == _base ? 0 : 1).ToList();
        Exception? ultimo = null;
        foreach (var b in bases)
        {
            try { await LoginEn(b); _base = b; return; }
            catch (WmsError) { throw; }
            catch (Exception e) { ultimo = e; }
        }
        throw new WmsError($"No se pudo llegar al WMS ({string.Join(" ni ", bases)}). {ultimo?.Message}");
    }

    async Task LoginEn(string b)
    {
        _nav.Vaciar();
        var inicio = await _nav.Get(b + "hdis.aspx");
        var f = Gx.Leer(inicio.Texto);
        var boton = f.Botones.FirstOrDefault(x => x.Nombre.Equals("CMDACEPTAR", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f);
        var r = await _nav.Post(new Uri(new Uri(b), string.IsNullOrEmpty(f.Accion) ? "hdis.aspx" : f.Accion),
            Gx.Envio(f, boton, new() { ["_USR"] = Usuario, ["_PASSWORD"] = _clave }), b + "hdis.aspx");
        if (Gx.EsLogin(r.Texto) || !r.Url.AbsolutePath.Contains("hmenurf", StringComparison.OrdinalIgnoreCase))
        {
            var msg = Gx.Mensajes(Gx.Leer(r.Texto));
            throw new WmsError("El WMS no aceptó el ingreso: " + (msg.Length > 0 ? msg : "usuario o clave incorrectos"));
        }
        _usuarioWms = (Consulta(r.Url).Split(',')[0] is { Length: > 0 } u ? u : Usuario).ToUpperInvariant();
        var fm = Gx.Leer(r.Texto);
        Empresas = (fm.Selects.GetValueOrDefault("_SELECCEMPRESA") ?? new())
            .Select(o => (int.TryParse(o.Valor, out var n) ? n : 0, o.Texto)).Where(e => e.Item1 > 0).ToList();
        _logueado = true;
    }

    public void CambiarClave(string clave) { _clave = clave; _logueado = false; }

    static string Consulta(Uri u) => u.Query.TrimStart('?');

    async Task Asegurar() { if (!_logueado || _base == null) await Login(); }

    string U(string pagina, params object[] parms) => $"{_base}{pagina}?{string.Join(",", new object[] { _usuarioWms! }.Concat(parms))}";

    async Task<Respuesta> Abrir(Func<string> url)
    {
        await Asegurar();
        var r = await _nav.Get(url());
        if (Gx.EsLogin(r.Texto)) { _logueado = false; await Asegurar(); r = await _nav.Get(url()); }
        return r;
    }

    async Task<Respuesta> Enviar(Respuesta r, Dictionary<string, string> cambios, Func<Gx.Formulario, Gx.Boton?>? boton = null)
    {
        var f = Gx.Leer(r.Texto);
        var b = boton != null ? boton(f) : Gx.BotonEnter(f);
        var destino = string.IsNullOrEmpty(f.Accion) ? r.Url : new Uri(r.Url, f.Accion);
        return await _nav.Post(destino, Gx.Envio(f, b, cambios), r.Url.ToString());
    }

    /// <summary>Entra a una opción del menú RF con la empresa indicada.</summary>
    async Task<Respuesta> OpcionMenu(int empresa, int opcion)
    {
        var menu = await Abrir(() => U("hmenurf.aspx", empresa, ""));
        var r = await Enviar(menu, new() { ["_OPCION"] = opcion.ToString(), ["_SELECCEMPRESA"] = "0" }, f => f.BotonEvento("EENTER."));
        if (r.Url.AbsolutePath.Contains("hmenurf", StringComparison.OrdinalIgnoreCase))
        {
            var m = Gx.Mensajes(Gx.Leer(r.Texto));
            throw new WmsError($"El menú del WMS no abrió la opción {opcion}: {(m.Length > 0 ? m : "sin respuesta")}");
        }
        return r;
    }

    // ------------------------------------------------------------------ consultas
    /// <summary>
    /// Situación de un contenedor en el WMS: la pantalla que abre el handheld al elegir la profundidad en "Ver un hueco"
    /// (hdatubicaciones.aspx). Es solo consulta: dice si está bloqueado (no disponible) y el motivo (ROTURA, CUARENTENA…).
    /// </summary>
    public Task<DatosContenedorWms> DatosContenedor(string contenedor) => EnSerie(async () =>
    {
        var cont = contenedor.Trim().ToUpperInvariant();
        var r = await Abrir(() => U("hdatubicaciones.aspx", 0, Uri.EscapeDataString(cont)));
        if (!r.Url.AbsolutePath.Contains("hdatubicaciones", StringComparison.OrdinalIgnoreCase)) throw new WmsError("No abrió \"Datos de Ubicaciones\" del handheld.");
        var v = Gx.Leer(r.Texto).Valores;
        string? T(string k) => v.GetValueOrDefault(k)?.Trim() is { Length: > 0 } x ? WebUtility.HtmlDecode(x) : null;
        static int? Ent(string? s) => int.TryParse((s ?? "").Trim(), out var n) ? n : null;
        static string? Tras(string? s, string prefijo) => s != null && s.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) ? s[prefijo.Length..].Trim() : s;
        var msg = T("_MENSGRAL");
        var existe = T("_COD_DI1_UB") != null || T("_PLTBLK") != null;
        if (!existe) GuardarPagina("datos_contenedor_sin_datos", r, unaVez: true);
        bool? bloq = T("_PLTBLK")?.ToUpperInvariant() switch { "S" or "SI" => true, "N" or "NO" => false, _ => null };
        double? unidades = double.TryParse((T("_PUAQTY_0001") ?? "").Replace(".", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var u) ? u : null;
        var ultima = string.Join(" · ", new[] { T("_MENSLOC5_0001"), T("_MENSLOC6_0001") }.Where(x => x != null));
        return new DatosContenedorWms(cont, existe, bloq, T("_MTSDSC"), unidades, Tras(T("_MENSLOC1_0001"), "Artículo:"), T("_MENSLOC2_0001"),
            T("_COD_DI1_UB"), Ent(T("_COD_DI2_UB")), Ent(T("_COD_DI3_UB")), T("_COD_DI4_UB"), Ent(T("_EMPPLT")), T("_PLTFARTVTO_0001"),
            ultima.Length > 0 ? ultima : null, msg);
    });

    /// <summary>"Ver un hueco" por Ubic.Ctrl + nivel (Chequeos y ajustes › 8).</summary>
    public Task<VistaHueco?> VerHuecoPorUbic(int empresa, long ubicCtrl, int nivel) => EnSerie(async () =>
    {
        var ajustes = await OpcionMenu(empresa, 5);
        var consulta = await Enviar(ajustes, new() { ["_OPCION"] = "8" });
        if (!consulta.Url.AbsolutePath.Contains("hconsubicac", StringComparison.OrdinalIgnoreCase)) throw new WmsError("No abrió \"Ver un hueco\".");
        var r = await Enviar(consulta, new() { ["_UBICMNEMOT"] = ubicCtrl.ToString(), ["_COD_DI3_UB"] = nivel.ToString("00") }, f => f.BotonEvento("EENTER."));
        return r.Url.AbsolutePath.Contains("hselconsubicac", StringComparison.OrdinalIgnoreCase) ? await LeerHueco(r) : null;
    });

    /// <summary>"Ver un hueco" directo por cara/columna/nivel.</summary>
    public Task<VistaHueco?> VerHueco(int empresa, string cara, int columna, int nivel) => EnSerie<VistaHueco?>(async () =>
    {
        var r = await Abrir(() => U("hselconsubicac.aspx", empresa, cara, columna.ToString("000"), nivel.ToString("00")));
        var v = await LeerHueco(r);
        return v.Cara.Length > 0 ? v : null;
    });

    async Task<VistaHueco> LeerHueco(Respuesta primera)
    {
        GuardarPagina("ver_hueco", primera, unaVez: true);
        var r = primera;
        var huecos = new List<Hueco>();
        var f = Gx.Leer(r.Texto);
        var b = f.Valores;
        // La grilla muestra de a 5 huecos (_ITEMS) y trae los totales: _I = huecos de la posición, _CANTVACIAS y _CANTINH.
        // Con eso se sabe cuántos contenedores hay que encontrar: se pagina (E'SGTE') hasta tenerlos todos (o recorrer todos los huecos).
        static int? Entero(string? s) => double.TryParse((s ?? "").Trim().Replace(".", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : null;
        var totalHuecos = Entero(b.GetValueOrDefault("_I"));
        var vacias = Entero(b.GetValueOrDefault("_CANTVACIAS"));
        var inh = Entero(b.GetValueOrDefault("_CANTINH")) ?? 0;
        int? esperados = totalHuecos != null && vacias != null ? Math.Max(0, totalHuecos.Value - vacias.Value - inh) : null;
        var porPagina = Math.Max(1, Entero(b.GetValueOrDefault("_ITEMS")) ?? 5);
        var maxVueltas = totalHuecos is > 0 ? Math.Min(60, (totalHuecos.Value + porPagina - 1) / porPagina + 1) : 20;
        string? firmaAnterior = null;
        for (var vuelta = 0; vuelta < maxVueltas; vuelta++)
        {
            var nuevos = new List<Hueco>();
            foreach (var (k, v) in f.Valores)
            {
                var m = Regex.Match(k, @"^_COD_DI4_UB_(\d{4})$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var n = m.Groups[1].Value;
                var cont = f.Valores.GetValueOrDefault("_UBCCPL_" + n)?.Trim();
                // si la grilla trae la disponibilidad/calidad del contenedor (el nombre de la columna puede variar), se toma
                var estado = f.Valores.Where(kv => kv.Key.EndsWith("_" + n) && Regex.IsMatch(kv.Key, "DISP|CALID|ESTAD|MTSCOD|MTSDSC", RegexOptions.IgnoreCase)
                                                   && !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Value.Trim()).FirstOrDefault();
                nuevos.Add(new Hueco(v.Trim(), string.IsNullOrEmpty(cont) ? null : cont,
                    string.Equals(f.Valores.GetValueOrDefault("_COD_MOT_IN_" + n)?.Trim(), "si", StringComparison.OrdinalIgnoreCase), estado));
            }
            // un hueco es (profundidad, contenedor): si el WMS repite la profundidad con otro contenedor, cuenta igual
            var agregados = nuevos.Where(h => !huecos.Any(x => x.Profundidad == h.Profundidad && x.Contenedor == h.Contenedor)).ToList();
            huecos.AddRange(agregados);
            var ocupados = huecos.Where(h => h.Contenedor != null).Select(h => h.Contenedor).Distinct().Count();
            if (esperados != null && ocupados >= esperados) break;                 // ya están todos los contenedores
            if (totalHuecos != null && huecos.Count >= totalHuecos) break;          // se recorrieron todos los huecos
            var firma = string.Join("|", nuevos.Select(h => h.Profundidad + ":" + h.Contenedor));
            if (firma == firmaAnterior) break;                                      // el WMS devolvió la misma página
            firmaAnterior = firma;
            var sig = f.Botones.FirstOrDefault(x => x.Evento.Contains("SGTE", StringComparison.OrdinalIgnoreCase) || x.Evento.Contains("SIGUI", StringComparison.OrdinalIgnoreCase))
                      ?? f.Botones.FirstOrDefault(x => x.Valor.Trim() is "->" or ">" or ">>" or "Siguiente");
            if (sig == null) break;
            // como el navegador: se manda el formulario completo (con _PAG) y el evento de la grilla
            var antes = r;
            r = await Enviar(r, new(), _ => sig);
            f = Gx.Leer(r.Texto);
            if (f.Valores.Keys.All(k => !k.StartsWith("_COD_DI4_UB_", StringComparison.OrdinalIgnoreCase))) { GuardarPagina("ver_hueco_pagina_rara", r); r = antes; break; }
        }
        var leidos = huecos.Where(h => h.Contenedor != null).Select(h => h.Contenedor).Distinct().Count();
        if (esperados != null && leidos < esperados)
        {
            GuardarPagina("ver_hueco_incompleto", r);
            _faltantes = esperados.Value - leidos;
        }
        else _faltantes = 0;
        int.TryParse(b.GetValueOrDefault("_COD_DI2_UBSF"), out var col);
        int.TryParse(b.GetValueOrDefault("_COD_DI3_UBSF"), out var niv);
        var area = b.GetValueOrDefault("_AREADESCRIPCION")?.Trim();
        var msj = Gx.Mensajes(Gx.Leer(primera.Texto));
        if (_faltantes > 0) msj = (msj.Length > 0 ? msj + " " : "") + $"Ojo: el WMS informa {esperados} contenedores y SONA leyó {leidos}.";
        return new VistaHueco((b.GetValueOrDefault("_COD_DI1_UBSF") ?? "").Trim(), col, niv, string.IsNullOrEmpty(area) ? null : area, huecos, msj.Length > 0 ? msj : null)
            { Esperados = esperados, Total = totalHuecos, InhabilitadosWms = inh };
    }
    int _faltantes;

    /// <summary>Guarda una pantalla del WMS en datos/wms_paginas (para revisar pantallas nuevas o que cambiaron).</summary>
    void GuardarPagina(string nombre, Respuesta r, bool unaVez = false)
    {
        try
        {
            var dir = Path.Combine(_cfg.Carpeta, "wms_paginas");
            Directory.CreateDirectory(dir);
            var archivo = Path.Combine(dir, unaVez ? nombre + ".html" : $"{nombre}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.html");
            if (unaVez && File.Exists(archivo)) return;
            File.WriteAllText(archivo, $"<!-- {r.Url} -->\n" + r.Texto);
            foreach (var viejo in Directory.GetFiles(dir, "*_2*.html").OrderByDescending(x => x).Skip(300)) File.Delete(viejo);
        }
        catch { /* no es crítico */ }
    }

    // ------------------------------------------------------------------ movimientos
    /// <summary>Opción 4 – Reubicar contenedor: contenedor → Ubic.Ctrl de destino + nivel.</summary>
    public Task<ResultadoWms> Reubicar(int empresa, string contenedor, long ubicCtrl, int nivel) => EnSerie(async () =>
    {
        var p1 = await OpcionMenu(empresa, 4);
        var p2 = await Enviar(p1, new() { ["_PLTCOD"] = contenedor }, f => f.Botones.FirstOrDefault(b => b.Valor.Contains("ACEPTAR", StringComparison.OrdinalIgnoreCase)));
        if (!p2.Url.AbsolutePath.Contains("hreubpl2", StringComparison.OrdinalIgnoreCase))
        {
            var m = MensajeUrl(p2.Url);
            return new ResultadoWms(false, m.Length > 0 ? m : Gx.Mensajes(Gx.Leer(p2.Texto)) is { Length: > 0 } x ? x : "El WMS no aceptó el contenedor");
        }
        var parms = Consulta(p2.Url).Split(',');
        var origen = parms.Length >= 7 ? $"{parms[4]}-{parms[5]}-{parms[6]}" : null;
        var fin = await Enviar(p2, new() { ["_NUEVAUBIC"] = ubicCtrl.ToString(), ["_NIVEL"] = nivel.ToString() }, f => f.BotonEvento("EENTER."));
        var mensaje = MensajeUrl(fin.Url);
        if (mensaje.Length == 0) mensaje = Gx.Mensajes(Gx.Leer(fin.Texto));
        return new ResultadoWms(mensaje.Contains("REUBICADO", StringComparison.OrdinalIgnoreCase), mensaje.Length > 0 ? mensaje : "Sin respuesta del WMS", origen);
    });

    /// <summary>
    /// Opción 3 – Cambiar disponibilidad: Disponible / No disponible + motivo.
    /// OJO: en esta pantalla el ENTER con el contenedor cargado YA aplica el cambio (así se ve en la captura del 12/10),
    /// por eso se elige primero la opción (E_OPCION.CLICK trae los motivos y no cambia nada) y se manda un solo ENTER.
    /// </summary>
    public Task<ResultadoWms> CambiarDisponibilidad(int empresa, string contenedor, bool disponible, string? motivo) => EnSerie(async () =>
    {
        var r = await OpcionMenu(empresa, 3);
        var f = Gx.Leer(r.Texto);
        var opciones = f.Selects.GetValueOrDefault("_OPCION") ?? new();
        var elegida = opciones.FirstOrDefault(o => disponible
            ? o.Valor.StartsWith("DISP", StringComparison.OrdinalIgnoreCase) && !o.Valor.Contains("NO", StringComparison.OrdinalIgnoreCase)
            : o.Valor.Contains("NODISP", StringComparison.OrdinalIgnoreCase));
        var opcion = elegida?.Valor ?? (disponible ? "DISPONIBILIDAD" : "NODISPONIB");
        if (f.Valores.GetValueOrDefault("_OPCION") != opcion)
        {
            var v = Gx.Envio(f, null, new() { ["_PLTCOD"] = contenedor, ["_OPCION"] = opcion });
            v["_EventName"] = "E_OPCION.CLICK.";
            r = await _nav.Post(string.IsNullOrEmpty(f.Accion) ? r.Url : new Uri(r.Url, f.Accion), v, r.Url.ToString());
            f = Gx.Leer(r.Texto);
        }
        var cambios = new Dictionary<string, string> { ["_PLTCOD"] = contenedor, ["_OPCION"] = opcion };
        var ops = f.Selects.GetValueOrDefault("_MTSCOD") ?? new();
        if (!disponible)
        {
            if (string.IsNullOrEmpty(motivo)) return new ResultadoWms(false, "Para pasar a No disponible hace falta el motivo.");
            var m = ops.FirstOrDefault(o => Norm(o.Texto) == Norm(motivo)) ?? ops.FirstOrDefault(o => Norm(o.Texto).Contains(Norm(motivo)));
            if (m == null) return new ResultadoWms(false, $"El WMS no tiene el motivo \"{motivo}\". Motivos: {string.Join(", ", ops.Select(o => o.Texto))}");
            cambios["_MTSCOD"] = m.Valor;
        }
        else if (ops.FirstOrDefault(o => Norm(o.Texto) == "DISPONIBLE") is { } md) cambios["_MTSCOD"] = md.Valor;
        var fin = await Enviar(r, cambios, ff => ff.BotonEvento("EENTER."));
        var mensaje = Gx.Mensajes(Gx.Leer(fin.Texto));
        return new ResultadoWms(mensaje.Contains("CAMBIADO", StringComparison.OrdinalIgnoreCase), mensaje.Length > 0 ? mensaje : "Sin respuesta del WMS");
    });

    /// <summary>
    /// Chequeos y ajustes › 13 – Reubicar cajas (capturado el 13/10):
    ///   hreubcj1: _PLTCOD = contenedor origen → hreubcj2?USR,EMP,CONT,,0,ART,CARA,COL,NIV,PROF,
    ///   hreubcj2: _CANTCAJAS, _NEWPLTCOD = contenedor destino, _NUEVAUBIC + _NIVEL = dónde está el destino, _OPCION = 1 (REUBICAR)
    ///   → hreubcj1?USR,EMP,,,CAJAS+REUBICADAS+EN:+I-186833+IC+/006/04
    /// </summary>
    public Task<ResultadoWms> ReubicarCajas(int empresa, string origen, string destino, double cantidad, long? ubicDestino, int? nivelDestino) => EnSerie(async () =>
    {
        var ajustes = await OpcionMenu(empresa, 5);
        var p1 = await Enviar(ajustes, new() { ["_OPCION"] = "13" }, f => f.BotonEvento("EENTER."));
        if (!p1.Url.AbsolutePath.Contains("hreubcj1", StringComparison.OrdinalIgnoreCase))
            return new ResultadoWms(false, "El WMS no abrió Reubicar cajas (Chequeos y ajustes › 13): " + Gx.Mensajes(Gx.Leer(p1.Texto)));
        var p2 = await Enviar(p1, new() { ["_PLTCOD"] = origen, ["_UBICMNEMOT"] = "0", ["_NIVEL"] = "" },
            f => f.Botones.FirstOrDefault(b => b.Nombre.Equals("BTNACEPTAR", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f));
        if (!p2.Url.AbsolutePath.Contains("hreubcj2", StringComparison.OrdinalIgnoreCase))
        {
            GuardarPagina("reubicar_cajas_error_p1", p2);
            var m = MensajeEnUrl(p2.Url) is { Length: > 0 } mu ? mu : Gx.Mensajes(Gx.Leer(p2.Texto));
            return new ResultadoWms(false, m.Length > 0 ? m : $"El WMS no aceptó el contenedor {origen}");
        }
        var cant = cantidad % 1 == 0 ? ((long)cantidad).ToString(CultureInfo.InvariantCulture) : cantidad.ToString(CultureInfo.InvariantCulture).Replace('.', ',');
        var fin = await Enviar(p2, new()
        {
            ["_CANTCAJAS"] = cant, ["_NEWPLTCOD"] = destino,
            ["_NUEVAUBIC"] = (ubicDestino ?? 0).ToString(CultureInfo.InvariantCulture), ["_NIVEL"] = nivelDestino?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["_OPCION"] = "1",
        }, f => f.Botones.FirstOrDefault(b => b.Nombre.Equals("BTNACEPTAR", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f));
        var mensaje = MensajeEnUrl(fin.Url);
        if (mensaje.Length == 0) mensaje = Gx.Mensajes(Gx.Leer(fin.Texto));
        var ok = mensaje.Contains("REUBICAD", StringComparison.OrdinalIgnoreCase) && !ReErrorWms.IsMatch(mensaje);
        if (!ok) GuardarPagina("reubicar_cajas_error_p2", fin);
        return new ResultadoWms(ok, mensaje.Length > 0 ? mensaje : "Sin respuesta del WMS: revisá el contenedor en el handheld.");
    });

    /// <summary>
    /// Chequeos y ajustes › 5 – Ajuste de contenedores (hajuplt, capturado el 14/10):
    ///   1) ENTER con _PLTCOD → muestra artículo, empresa y cantidad (_CANTPLT) sin ajustar nada.
    ///   2) ENTER con _STRCANTI = cantidad, _SR = R (resta) / S (suma), _MTSCOD = motivo → "se ajustó en: X unidades y quedó en: Y".
    /// Si <paramref name="cantidad"/> es null se resta todo lo que tiene (eliminar el contenedor).
    /// El WMS no deja ajustar dos veces el mismo contenedor en menos de 5 minutos.
    /// </summary>
    public Task<ResultadoWms> AjustarContenedor(int empresa, string contenedor, double? cantidad, bool sumar, string motivo) => EnSerie(async () =>
    {
        var ajustes = await OpcionMenu(empresa, 5);
        var p1 = await Enviar(ajustes, new() { ["_OPCION"] = "5" }, f => f.BotonEvento("EENTER."));
        if (!p1.Url.AbsolutePath.Contains("hajuplt", StringComparison.OrdinalIgnoreCase))
            return new ResultadoWms(false, "El WMS no abrió el ajuste de contenedores (Chequeos y ajustes › 5): " + Gx.Mensajes(Gx.Leer(p1.Texto)));
        Gx.Boton? Aceptar(Gx.Formulario f) => f.Botones.FirstOrDefault(b => b.Nombre.Equals("BTNACEPTAR", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f);
        // 1) leer el contenedor (cantidad 0 y sin suma/resta: no ajusta nada)
        var p2 = await Enviar(p1, new() { ["_PLTCOD"] = contenedor, ["_UBICMNEMOT"] = "0", ["_NIVEL"] = "", ["_STRCANTI"] = "0", ["_SR"] = "" }, Aceptar);
        var f2 = Gx.Leer(p2.Texto);
        var leido = f2.Valores.GetValueOrDefault("_PLTCODAUX", "").Trim();
        if (!leido.Equals(contenedor, StringComparison.OrdinalIgnoreCase))
        {
            GuardarPagina("ajuste_error_p1", p2);
            var m = Gx.Mensajes(f2);
            return new ResultadoWms(false, m.Length > 0 && !m.Contains("Seleccione", StringComparison.OrdinalIgnoreCase) ? m : $"El WMS no mostró el contenedor {contenedor} (¿empresa distinta o no existe?).");
        }
        var tiene = double.TryParse(f2.Valores.GetValueOrDefault("_CANTPLT", "").Trim().Replace(".", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : (double?)null;
        var aAjustar = cantidad ?? tiene ?? 0;
        if (aAjustar <= 0) return new ResultadoWms(cantidad == null && tiene == 0, cantidad == null && tiene == 0 ? "El contenedor ya estaba en 0." : "No hay cantidad para ajustar.");
        if (!sumar && tiene != null && aAjustar > tiene + 1e-6) return new ResultadoWms(false, $"El contenedor tiene {tiene} y se quieren restar {aAjustar}.");
        var ops = f2.Selects.GetValueOrDefault("_MTSCOD") ?? new();
        var mot = ops.FirstOrDefault(o => Norm(o.Texto) == Norm(motivo)) ?? ops.FirstOrDefault(o => Norm(o.Texto).Contains(Norm(motivo)));
        if (mot == null) return new ResultadoWms(false, $"El WMS no tiene el motivo de ajuste \"{motivo}\". Motivos: {string.Join(", ", ops.Select(o => o.Texto))}");
        var cant = aAjustar % 1 == 0 ? ((long)aAjustar).ToString(CultureInfo.InvariantCulture) : aAjustar.ToString(CultureInfo.InvariantCulture).Replace('.', ',');
        // 2) ajustar
        var fin = await Enviar(p2, new() { ["_STRCANTI"] = cant, ["_SR"] = sumar ? "S" : "R", ["_MTSCOD"] = mot.Valor }, Aceptar);
        var mensaje = Gx.Mensajes(Gx.Leer(fin.Texto));
        if (mensaje.Length == 0) mensaje = Regex.Match(Gx.Texto(fin.Texto), @"El Contenedor:.*?unidades y qued[oó] en:\s*[\d.,]+\s*unidades", RegexOptions.IgnoreCase).Value;
        var ok = Regex.IsMatch(mensaje, @"se ajust[oó]", RegexOptions.IgnoreCase);
        if (!ok) GuardarPagina("ajuste_error_p2", fin);
        return new ResultadoWms(ok, mensaje.Length > 0 ? mensaje : "Sin respuesta clara del WMS: revisá el contenedor en el handheld.");
    });

    /// <summary>Clave del WMS del operario (para entrar a LES › Ingresos pendientes con el mismo usuario).</summary>
    public string Clave => _clave;

    /// <summary>
    /// Chequeos y ajustes › 7 – Carga inicial (capturado el 19/10):
    ///   hajustes (_OPCION 7) → hingdcin (BTNACEPTAR) → hingcarin?EMP,CINI,RF,0,USR,
    ///   hingcarin: _PLTCOD, _ARTICULO, _CANTLOG (unidades), _UBICMNEMOT (Ubic.Ctrl), _NIVEL_3 → BUTTON1 ACEPTAR → "Carga Inicial Exitosa".
    /// </summary>
    public Task<ResultadoWms> CargaInicial(int empresa, string contenedor, string articulo, double cantidad, long ubicCtrl, int nivel) => EnSerie(async () =>
    {
        var ajustes = await OpcionMenu(empresa, 5);
        var p1 = await Enviar(ajustes, new() { ["_OPCION"] = "7" }, f => f.BotonEvento("EENTER."));
        if (p1.Url.AbsolutePath.Contains("hingdcin", StringComparison.OrdinalIgnoreCase))
            p1 = await Enviar(p1, new(), f => f.Botones.FirstOrDefault(b => b.Nombre.Equals("BTNACEPTAR", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f));
        if (!p1.Url.AbsolutePath.Contains("hingcarin", StringComparison.OrdinalIgnoreCase))
        {
            GuardarPagina("carga_inicial_error_p1", p1);
            return new ResultadoWms(false, "El WMS no abrió la Carga inicial (Chequeos y ajustes › 7): " + Gx.Mensajes(Gx.Leer(p1.Texto)));
        }
        var cant = cantidad % 1 == 0 ? ((long)cantidad).ToString(CultureInfo.InvariantCulture) : cantidad.ToString(CultureInfo.InvariantCulture).Replace('.', ',');
        var fin = await Enviar(p1, new()
        {
            ["_PLTCOD"] = contenedor, ["_ARTICULO"] = articulo, ["_CANTLOG"] = cant,
            ["_UBICMNEMOT"] = ubicCtrl.ToString(CultureInfo.InvariantCulture), ["_NIVEL_3"] = nivel.ToString(CultureInfo.InvariantCulture),
        }, f => f.Botones.FirstOrDefault(b => b.Nombre.Equals("BUTTON1", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f));
        var texto = Gx.Texto(fin.Texto);
        var ok = Regex.IsMatch(texto, @"Carga\s+Inicial\s+Exitosa", RegexOptions.IgnoreCase);
        var mensaje = Gx.Mensajes(Gx.Leer(fin.Texto));
        if (mensaje.Length == 0) mensaje = ok ? "Carga Inicial Exitosa" : Regex.Match(texto, @"CARGA INICIAL\s+(.{0,160}?)\s+CONTENEDOR:", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        if (!ok) GuardarPagina("carga_inicial_error_p2", fin);
        return new ResultadoWms(ok, mensaje.Length > 0 ? mensaje : "Sin respuesta clara del WMS: revisá el contenedor en el handheld.");
    });

    /// <summary>Mensaje que el handheld deja en la URL de vuelta (el primer parámetro con texto después de usuario y empresa).</summary>
    static string MensajeEnUrl(Uri u)
    {
        var q = u.OriginalString.Contains('?') ? u.OriginalString[(u.OriginalString.IndexOf('?') + 1)..] : "";
        foreach (var parte in q.Split(',').Skip(2))
        {
            string t;
            try { t = Regex.Replace(WebUtility.UrlDecode(parte), @"\s+", " ").Trim(); } catch { t = parte; }
            if (Regex.IsMatch(t, @"[A-Za-z]{3,}\s") ) return t;
        }
        return "";
    }

    static readonly Regex ReErrorWms = new(@"NO EXIST|INEXIST|INCORRECT|ERROR|NO SE PUEDE|NO PUEDE|NO PERTENECE|DIFERENTE|SUPERA|INSUFICIENT|MAYOR QUE|NO HAY|NO ESTA|NO ESTÁ|INV[AÁ]LID|BLOQUEAD", RegexOptions.IgnoreCase);

    /// <summary>Opción 2 – Guardado: contenedores recibidos pendientes de guardar.</summary>
    public Task<List<Pendiente>> PendientesGuardado(int empresa) => EnSerie(async () =>
    {
        var r = await OpcionMenu(empresa, 2);
        var v = Gx.Leer(r.Texto).Valores;
        var lista = new List<Pendiente>();
        foreach (var (k, val) in v)
        {
            var m = Regex.Match(k, @"^_PALLET_(\d{4})$");
            if (!m.Success || string.IsNullOrWhiteSpace(val)) continue;
            lista.Add(new Pendiente(val.Trim(), (v.GetValueOrDefault("RECCABNROAPLICAWMS_" + m.Groups[1].Value) ?? "").Trim(), v.GetValueOrDefault("RECCABGUARDADA_" + m.Groups[1].Value) == "1"));
        }
        return lista.Where(p => !p.Guardada).ToList();
    });

    /// <summary>Opción 2 – Guardar en la posición que eligió SONA (la del WMS es solo sugerencia).</summary>
    public Task<ResultadoWms> Guardar(int empresa, string contenedor, long ubicCtrl, int nivel) => EnSerie(async () =>
    {
        var p1 = await OpcionMenu(empresa, 2);
        var r = await Enviar(p1, new() { ["_PLTCOD"] = contenedor }, f => f.Botones.FirstOrDefault(b => b.Valor.Contains("ACEPTAR", StringComparison.OrdinalIgnoreCase) && b.Evento == "EENTER."));
        for (var i = 0; i < 2 && !r.Url.AbsolutePath.Contains("hingbpr5", StringComparison.OrdinalIgnoreCase); i++)
        {
            var f = Gx.Leer(r.Texto);
            var si = f.Botones.FirstOrDefault(b => b.Evento.Contains("'SI'", StringComparison.OrdinalIgnoreCase));
            if (si == null) { var m = Gx.Mensajes(f); return new ResultadoWms(false, m.Length > 0 ? m : "El WMS no aceptó el contenedor para guardar"); }
            r = await Enviar(r, new(), _ => si);
        }
        if (!r.Url.AbsolutePath.Contains("hingbpr5", StringComparison.OrdinalIgnoreCase))
        {
            var m = Gx.Mensajes(Gx.Leer(r.Texto));
            return new ResultadoWms(false, m.Length > 0 ? m : "No se abrió la pantalla de guardado");
        }
        var fv = Gx.Leer(r.Texto).Valores;
        var sugerida = $"{fv.GetValueOrDefault("_COD_DI1_UB")}-{fv.GetValueOrDefault("_COD_DI2_UB")}-{fv.GetValueOrDefault("_COD_DI3_UB")}";
        var fin = await Enviar(r, new() { ["_UBICMNEMOT"] = ubicCtrl.ToString(), ["_NIV"] = nivel.ToString() }, f => f.BotonEvento("EENTER."));
        var mensaje = Gx.Mensajes(Gx.Leer(fin.Texto));
        var ok = !fin.Url.AbsolutePath.Contains("hingbpr5", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(mensaje, "GUARDAD|UBICAD|OK", RegexOptions.IgnoreCase);
        ok = ok && !Regex.IsMatch(mensaje, "ERROR|INCORRECT|NO SE PUEDE", RegexOptions.IgnoreCase);
        return new ResultadoWms(ok, mensaje.Length > 0 ? mensaje : ok ? "Guardado" : "Sin respuesta del WMS", null, sugerida);
    });

    /// <summary>El handheld devuelve el resultado en la URL: hreubpl1.aspx?USR,6,CONTENEDOR+REUBICADO+EN%3a+DI+++%2f004%2f06,</summary>
    static string MensajeUrl(Uri u)
    {
        var partes = u.OriginalString.Contains('?') ? u.OriginalString[(u.OriginalString.IndexOf('?') + 1)..].Split(',') : Array.Empty<string>();
        if (partes.Length < 3) return "";
        try { return Regex.Replace(WebUtility.UrlDecode(partes[2]), @"\s+", " ").Trim(); } catch { return partes[2]; }
    }

    static string Norm(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetter(ch)) sb.Append(char.ToUpperInvariant(ch));
        return sb.ToString();
    }
}

/// <summary>Sesión web del WMS (hinicio.aspx) para leer "Ubicaciones" (hvolumenes.aspx): la ficha de cada posición.</summary>
public sealed class SesionWeb
{
    readonly Configuracion _cfg;
    readonly Navegador _nav = new(60);
    readonly string _usuario, _clave;
    string? _base;

    public SesionWeb(Configuracion cfg, string usuario, string clave) { _cfg = cfg; _usuario = usuario; _clave = clave; }

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
                    Gx.Envio(f, boton, new() { ["_USR"] = _usuario.ToUpperInvariant(), ["_PASSWORD"] = _clave }), b + "hinicio.aspx");
                if (!Regex.IsMatch(r.Texto, "Bienvenid", RegexOptions.IgnoreCase)) throw new WmsError("El WMS web no aceptó el ingreso");
                _base = b;
                return b;
            }
            catch (WmsError) { throw; }
            catch (Exception e) { ultimo = e; }
        }
        throw new WmsError("No se pudo llegar al WMS web. " + ultimo?.Message);
    }

    readonly SemaphoreSlim _uso = new(1, 1);

    /// <summary>
    /// "Historia de un contenedor / artículo" (hconshistplt.aspx): el WMS arma un archivo de Excel (HTML con una tabla)
    /// con todos los movimientos (ingresos, guardados, reubicaciones, picking, egresos) y SONA lo lee.
    /// </summary>
    public async Task<TablaReporte> Historia(string? contenedor, string? articulo, DateTime desde, DateTime hasta)
    {
        await _uso.WaitAsync();
        try
        {
            var b = _base ?? await Login();
            var pag = await _nav.Get(b + "hconshistplt.aspx");
            if (Gx.EsLogin(pag.Texto)) { b = await Login(); pag = await _nav.Get(b + "hconshistplt.aspx"); }
            var f = Gx.Leer(pag.Texto);
            var boton = f.Botones.FirstOrDefault(x => x.Nombre.Equals("CONFIRMAR", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f);
            var cambios = new Dictionary<string, string>
            {
                ["_EMPRESA"] = "0", ["_FECDESDE"] = desde.ToString("dd/MM/yyyy"), ["_FECHASTA"] = hasta.ToString("dd/MM/yyyy"),
                ["_PLTCODDESDE"] = contenedor ?? "", ["_PLTCODHASTA"] = contenedor ?? "",
                ["_ARTCODIGODESDE"] = articulo ?? "", ["_ARTCODIGOHASTA"] = articulo ?? "",
            };
            var r = await _nav.Post(string.IsNullOrEmpty(f.Accion) ? pag.Url : new Uri(pag.Url, f.Accion), Gx.Envio(f, boton, cambios), pag.Url.ToString());
            var link = Regex.Match(r.Texto, @"href=""(hdownloadlishiplt\.aspx\?[^""]+)""", RegexOptions.IgnoreCase);
            if (!link.Success)
            {
                Guardar("historia_sin_archivo", r);
                var msj = Gx.Mensajes(Gx.Leer(r.Texto));
                throw new WmsError(msj.Length > 0 ? msj : "El WMS no devolvió el archivo de historia.");
            }
            var x = await _nav.Get(new Uri(new Uri(b), Gx.Desescapar(link.Groups[1].Value)), r.Url.ToString());
            var t = TablaExcelHtml.Leer(x.Texto);
            if (t == null) { Guardar("historia_archivo", x); throw new WmsError("No se pudo leer el archivo de historia del WMS (quedó guardado en wms_paginas para revisarlo)."); }
            return t;
        }
        finally { _uso.Release(); }
    }

    void Guardar(string nombre, Respuesta r)
    {
        try
        {
            var dir = Path.Combine(_cfg.Carpeta, "wms_paginas");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{nombre}_{DateTime.Now:yyyyMMdd_HHmmss}.html"), $"<!-- {r.Url} -->\n" + r.Texto);
        }
        catch { }
    }

    public async Task<List<FilaLayout>> LayoutDeCara(string cara)
    {
        var b = _base ?? await Login();
        var pag = await _nav.Get(b + "hvolumenes.aspx");
        if (Gx.EsLogin(pag.Texto)) { b = await Login(); pag = await _nav.Get(b + "hvolumenes.aspx"); }
        var f = Gx.Leer(pag.Texto);
        var buscar = f.Botones.FirstOrDefault(x => x.Evento.Contains("SEARCH", StringComparison.OrdinalIgnoreCase)) ?? f.Botones.FirstOrDefault(x => x.Valor.Contains("BUSCAR", StringComparison.OrdinalIgnoreCase));
        var r = await _nav.Post(string.IsNullOrEmpty(f.Accion) ? pag.Url : new Uri(pag.Url, f.Accion), Gx.Envio(f, buscar, new() { ["_COD_DI1_UB"] = cara }), pag.Url.ToString());
        return LeerLayout(r.Texto);
    }

    static int? Num(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().Replace(".", "").Replace(",", ".");
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : null;
    }

    public static List<FilaLayout> LeerLayout(string html)
    {
        var filas = new List<FilaLayout>();
        foreach (var c in Gx.Grilla(html).Values)
        {
            if (!c.TryGetValue("COD_DI1_UB", out var cara) || string.IsNullOrWhiteSpace(cara)) continue;
            var emp = Num(c.GetValueOrDefault("VOLCODEMP"));
            var art = c.GetValueOrDefault("GRPCOD")?.Trim();
            filas.Add(new FilaLayout(cara.Trim(), Num(c.GetValueOrDefault("COD_DI2_UB")) ?? 0, Num(c.GetValueOrDefault("COD_DI3_UB")) ?? 0,
                Num(c.GetValueOrDefault("UBICMNEMOT")), (c.GetValueOrDefault("VOLTIPUB") ?? "").Trim(),
                Num(c.GetValueOrDefault("CARACOLANC")), Num(c.GetValueOrDefault("CARACOLALT")), Num(c.GetValueOrDefault("CARACOLPRF")), Num(c.GetValueOrDefault("CANTUBICAC")),
                emp is 0 ? null : emp, string.IsNullOrEmpty(art) ? null : art, (c.GetValueOrDefault("VCESTADO") ?? "").Trim(),
                (c.GetValueOrDefault("MEZCLA") ?? "").Trim().StartsWith("s", StringComparison.OrdinalIgnoreCase)));
        }
        return filas;
    }
}
