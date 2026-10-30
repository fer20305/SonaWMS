using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Sona.Wms;

/// <summary>Un renglón de la lista de la OP (pantalla hpck0).</summary>
public sealed record ItemPick(int Orden, string Articulo, string? Descripcion, string? Ubicacion, double? Cantidad, string? Contenedor, bool ConStock);

/// <summary>
/// Pantalla de picking del WMS traducida a datos: menu (hselpck), lista (hpck0), ir (hpck1: ir a la posición y leer),
/// tomar (hpck2: cantidad y contenedor de picking), etiquetar (hpck4: isla de control), fin (hpck9: OP concluida) u otra.
/// </summary>
public sealed class PasoPicking
{
    public string Pantalla { get; set; } = "otra";
    public string? Op { get; set; }
    public string? OpDescripcion { get; set; }
    public string? Destino { get; set; }
    public string? ContenedorPicking { get; set; }
    public List<ItemPick> Items { get; set; } = new();
    public string? Articulo { get; set; }
    public string? Descripcion { get; set; }
    public string? Unidad { get; set; }
    public string? Cara { get; set; }
    public int? Columna { get; set; }
    public int? Nivel { get; set; }
    public string? Profundidad { get; set; }
    public string? Posicion { get; set; }
    public long? UbicCtrl { get; set; }
    public double? Cantidad { get; set; }
    public string? ContenedorOrigen { get; set; }
    public double? CantPorBulto { get; set; }
    public string? Isla { get; set; }
    public double? TotalUnidades { get; set; }
    public string? TotalM3 { get; set; }
    public string? TotalKg { get; set; }
    public string? Entrega { get; set; }
    public string? Mensaje { get; set; }
    /// <summary>
    /// El WMS avisa pero deja seguir (ej. "Aviso: Contenedor Supera Dias / Rango FIFO/FEFO del Articulo" por lote o vencimiento):
    /// la pantalla trae visible el link "Continuar" (evento E'CONTINUAR').
    /// </summary>
    public bool PuedeContinuar { get; set; }
    /// <summary>Para pantallas que SONA todavía no conoce: campos editables y botones del WMS.</summary>
    public List<string> Campos { get; set; } = new();
    public List<string> Botones { get; set; } = new();
    /// <summary>Texto legible de cada botón (el value del WMS, sin "BTN").</summary>
    public List<string> Etiquetas { get; set; } = new();
    public string? Texto { get; set; }
    public List<string> Fotos { get; set; } = new();
    public string? Foto { get; set; }
}

public sealed partial class SesionHandheld
{
    Respuesta? _pick;
    public PasoPicking? UltimoPaso { get; private set; }

    static double? NumWms(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().Replace(".", "").Replace(',', '.');
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    static readonly Regex ReInputOrden = new(@"<input\b[^>]*\bname=""?([A-Za-z0-9_]+)""?[^>]*>", RegexOptions.IgnoreCase);
    static readonly Regex ReValor = new(@"\bvalue=(""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);

    /// <summary>Renglones de la OP en el orden de la pantalla (los nombres de los controles no siguen el número de renglón).</summary>
    static List<ItemPick> LeerItems(string html, Gx.Formulario f)
    {
        var lista = new List<ItemPick>();
        Dictionary<string, string>? fila = null;
        void Cerrar()
        {
            if (fila == null) return;
            var art = fila.GetValueOrDefault("ARTCODIGO")?.Trim();
            if (!string.IsNullOrEmpty(art) && int.TryParse(fila.GetValueOrDefault("ARTORDEN"), out var ord) && ord > 0)
            {
                var ub = fila.GetValueOrDefault("UBICACION")?.Trim();
                lista.Add(new ItemPick(ord, art.ToUpperInvariant(), fila.GetValueOrDefault("ARTICULOPENDIENTE")?.Trim(), ub, NumWms(fila.GetValueOrDefault("CANTIDAD")),
                    f.Valores.GetValueOrDefault("_PALLETYATOMADO" + ord)?.Trim() is { Length: > 0 } p ? p : null,
                    !string.Equals(ub, "No hay stock", StringComparison.OrdinalIgnoreCase)));
            }
            fila = null;
        }
        var limpio = Regex.Replace(html, @"<(script|style)\b[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        foreach (Match m in ReInputOrden.Matches(limpio))
        {
            var nombre = m.Groups[1].Value.ToUpperInvariant();
            var c = Regex.Match(nombre, @"^(ARTORDEN|ARTCODIGO|UBICACION|ARTICULOPENDIENTE|CANTIDAD)\d+$");
            if (!c.Success) continue;
            var v = ReValor.Match(m.Value);
            var valor = WebUtility.HtmlDecode(v.Success ? (v.Groups[2].Success ? v.Groups[2].Value : v.Groups[3].Value) : "");
            if (c.Groups[1].Value == "ARTORDEN") { Cerrar(); fila = new(); }
            if (fila != null) fila[c.Groups[1].Value] = valor;
        }
        Cerrar();
        return lista;
    }

    /// <summary>Traduce la página actual de picking a un paso.</summary>
    public static PasoPicking LeerPaso(Respuesta r)
    {
        var f = Gx.Leer(r.Texto);
        var v = f.Valores;
        string? V(string k) => v.GetValueOrDefault(k)?.Trim() is { Length: > 0 } x ? x : null;
        var texto = Gx.Texto(r.Texto);
        var pag = r.Url.AbsolutePath.ToLowerInvariant();
        var p = new PasoPicking { Op = V("_EVNOAPLICA"), OpDescripcion = V("_ODESCOPERC"), Mensaje = Gx.Mensajes(f) is { Length: > 0 } msj ? msj : null };
        string? Isla() => Regex.Match(texto, @"ISLA DE CONTROL\s+([A-Z0-9][A-Z0-9_-]*)", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value
                        : Regex.Match(texto, @"CONCLUIDA\s+([A-Z0-9][A-Z0-9_-]*)", RegexOptions.IgnoreCase) is { Success: true } m2 ? m2.Groups[1].Value : null;
        if (pag.Contains("hselpck")) p.Pantalla = "menu";
        else if (pag.Contains("hpck0"))
        {
            p.Pantalla = "lista";
            p.ContenedorPicking = V("_NEWPLT");
            p.Items = LeerItems(r.Texto, f);
            var d = Regex.Match(texto, @"DESTINO:\s*(.*?)\s*CONTENEDOR:", RegexOptions.IgnoreCase);
            if (d.Success && d.Groups[1].Value.Trim().Length > 0) p.Destino = d.Groups[1].Value.Trim();
        }
        else if (pag.Contains("hpck1"))
        {
            p.Pantalla = "ir";
            p.Articulo = V("_ARTICULO"); p.Descripcion = V("_DESCART"); p.Unidad = V("_ARTUNIDALM");
            p.Cara = V("_COD_DI1_UB");
            p.Columna = int.TryParse(V("_COD_DI2_UB"), out var col) ? col : null;
            p.Nivel = int.TryParse(V("_COD_DI3_UB"), out var niv) ? niv : null;
            p.Profundidad = V("_PROFUNDIDAD");
            p.Posicion = p.Cara != null ? $"{p.Cara}-{V("_COD_DI2_UB")}-{V("_COD_DI3_UB")}" : null;
            p.UbicCtrl = long.TryParse(V("_UBICMNEMOT"), out var ub) && ub > 0 ? ub : null;
            p.Cantidad = NumWms(V("_SDOAPCK"));
            p.ContenedorOrigen = V("_PALLET");
            p.ContenedorPicking = V("_NEWPLT");
            p.PuedeContinuar = HayContinuar(r.Texto);
        }
        else if (pag.Contains("hpck2"))
        {
            p.Pantalla = "tomar";
            p.Articulo = V("_ARTICULO"); p.Descripcion = V("_ARTABR"); p.Unidad = V("_ARTUNIDAD");
            p.Cantidad = NumWms(V("_CANTSUGE")) ?? NumWms(V("_CANTPCK"));
            p.CantPorBulto = NumWms(V("_CANTXBULTO"));
            p.ContenedorPicking = V("_PLTEXTERNO") ?? V("_NEWPLT");
        }
        else if (pag.Contains("hpck4"))
        {
            p.Pantalla = "etiquetar";
            p.ContenedorPicking = V("_NEWPLT"); p.TotalUnidades = NumWms(V("_TOTALUNIDADES")); p.TotalM3 = V("_TOTALM3"); p.TotalKg = V("_TOTALKGS");
            p.Entrega = V("_EVFHPROG"); p.Isla = Isla();
        }
        else if (pag.Contains("hpck9"))
        {
            p.Pantalla = "fin";
            p.TotalUnidades = NumWms(V("_CANTTOTAL")); p.TotalM3 = V("_M3TOTAL"); p.Isla = Isla();
        }
        else
        {
            p.Pantalla = pag.Contains("hmenurf") ? "menu_rf" : "otra";
            p.Campos = f.Editables.Select(c => c.Nombre).ToList();
            p.Botones = f.Botones.Where(b => !b.Oculto).Select(b => b.Nombre).ToList();
            p.Etiquetas = f.Botones.Where(b => !b.Oculto).Select(b => EtiquetaBoton(b)).ToList();
            p.Texto = texto.Length > 600 ? texto[..600] : texto;
            // "¿Abandona el Pedido? Pck5 PREPARAR PEDIDOS ORDEN DE PREP. CONSOLIDADA 2055 ..." (lo muestra el WMS al salir de una OP)
            if (Regex.IsMatch(texto, @"Abandona\s+el\s+Pedido", RegexOptions.IgnoreCase))
            {
                p.Pantalla = "abandonar";
                p.Op ??= Regex.Match(texto, @"(?:CONSOLIDADA|UNIDADES|ORDEN DE PREP\.?[A-Z ]*)\s+(\d+)", RegexOptions.IgnoreCase) is { Success: true } mo ? mo.Groups[1].Value : null;
                p.Entrega = Regex.Match(texto, @"Entrega:\s*([0-9/]+)", RegexOptions.IgnoreCase) is { Success: true } me ? me.Groups[1].Value : null;
            }
        }
        return p;
    }

    /// <summary>Link "Continuar" del WMS visible (span CONTINUAR sin display:none que dispara E'CONTINUAR').</summary>
    static bool HayContinuar(string html)
    {
        var m = Regex.Match(html, @"<span\b[^>]*\bid=""?CONTINUAR""?[^>]*>", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        var estilo = Regex.Match(m.Value, @"style=""([^""]*)""", RegexOptions.IgnoreCase).Groups[1].Value;
        return !Regex.IsMatch(estilo, @"display\s*:\s*none", RegexOptions.IgnoreCase) && html.IndexOf("CONTINUAR\\'.", m.Index, StringComparison.OrdinalIgnoreCase) > 0;
    }

    static string EtiquetaBoton(Gx.Boton b)
    {
        var v = (b.Valor ?? "").Trim();
        if (v.Length == 0 || v.Equals(b.Nombre, StringComparison.OrdinalIgnoreCase)) v = Regex.Replace(b.Nombre, "^BTN", "", RegexOptions.IgnoreCase);
        return v.Length == 0 ? b.Nombre : char.ToUpperInvariant(v[0]) + v[1..].ToLowerInvariant();
    }

    Respuesta? _pickBueno;
    /// <summary>Hay un paso de la OP conocido (lista, ir, tomar…) al que se puede volver.</summary>
    public bool HayPasoBueno => _pickBueno != null;

    PasoPicking GuardarPick(Respuesta r)
    {
        _pick = r;
        UltimoPaso = LeerPaso(r);
        if (UltimoPaso.Pantalla is "lista" or "ir" or "tomar" or "etiquetar") _pickBueno = r;
        if (UltimoPaso.Pantalla is "otra" or "abandonar") GuardarPagina("picking_" + Regex.Replace(r.Url.AbsolutePath, @"[^a-zA-Z0-9]", ""), r);
        return UltimoPaso;
    }

    /// <summary>Vuelve al último paso conocido de la OP (por ejemplo si el WMS preguntó "¿Abandona el Pedido?" y el picker quiere seguir).</summary>
    public Task<PasoPicking> PickingRetomar() => EnSerie(async () =>
    {
        var bueno = _pickBueno ?? throw new WmsError("No hay un paso de la OP al que volver: entrá de nuevo a la opción de picking (el WMS retoma la OP).");
        var r = await _nav.Get(bueno.Url);
        if (Gx.EsLogin(r.Texto)) { _pick = null; _logueado = false; throw new WmsError("Se cortó la sesión del WMS: volvé a entrar a la opción de picking (el WMS retoma la OP)."); }
        return GuardarPick(r);
    });

    /// <summary>7-PREPARACIÓN y la opción de picking (9 consolidado, 5 por unidades): el WMS asigna la próxima OP.</summary>
    public Task<PasoPicking> PickingIniciar(int empresa, int opcion) => EnSerie(async () =>
    {
        var menu = await OpcionMenu(empresa, 7);
        if (!menu.Url.AbsolutePath.Contains("hselpck", StringComparison.OrdinalIgnoreCase)) return GuardarPick(menu);
        var r = await Enviar(menu, new() { ["_OPCION"] = opcion.ToString() });
        return GuardarPick(r);
    });

    /// <summary>La pantalla de picking actual (relee la página).</summary>
    public Task<PasoPicking?> PickingActual() => EnSerie<PasoPicking?>(async () =>
    {
        if (_pick == null) return null;
        var r = await _nav.Get(_pick.Url);
        if (Gx.EsLogin(r.Texto)) { _pick = null; _logueado = false; return null; }
        return GuardarPick(r);
    });

    /// <summary>Manda la pantalla actual con los valores y el botón (por nombre); null = el ENTER/ACEPTAR.</summary>
    public Task<PasoPicking> PickingEnviar(Dictionary<string, string> valores, string? boton = null) => EnSerie(async () =>
    {
        if (_pick == null) throw new WmsError("No hay un picking en curso: entrá de nuevo a la opción.");
        var r = await Enviar(_pick, valores, f => boton == null
            ? (f.Botones.FirstOrDefault(b => b.Evento == "EENTER." && !b.Oculto && b.Valor.Contains("ACEPTAR", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f))
            : boton.StartsWith("E'") ? new Gx.Boton("", "", boton, null, null, false)   // evento de un link (ej. E'CONTINUAR'.)
            : f.Botones.FirstOrDefault(b => b.Nombre.Equals(boton, StringComparison.OrdinalIgnoreCase)));
        if (Gx.EsLogin(r.Texto)) { _pick = null; _logueado = false; throw new WmsError("Se cortó la sesión del WMS: volvé a entrar a la opción de picking (el WMS retoma la OP)."); }
        return GuardarPick(r);
    });

    /// <summary>
    /// Opción en la lista de la OP (número de ítem, 5 cerrar contenedor, 6 postergar, 7 ver pendiente, 99 volver), como el operario en la pistola:
    /// se escribe en el campo visible de la pantalla. Con un faltante el WMS muestra otro campo (viene con "1"): si se escribe solo en _I,
    /// el WMS lee el visible y contesta "NÚMERO TIPEADO ERRÓNEO". Si igual lo rechaza, se guarda la pantalla para diagnóstico.
    /// </summary>
    public Task<PasoPicking> PickingOpcion(string opcion) => EnSerie(async () =>
    {
        if (_pick == null) throw new WmsError("No hay un picking en curso: entrá de nuevo a la opción.");
        var antes = _pick;
        var f = Gx.Leer(antes.Texto);
        var cambios = new Dictionary<string, string> { ["_I"] = opcion };
        // el campo visible donde escribe el operario: _I si está a la vista; si no, el único editable (o el que parezca de ítem/opción)
        if (!f.Editables.Any(e => e.Nombre.Equals("_I", StringComparison.OrdinalIgnoreCase)))
        {
            var visibles = f.Editables.Count == 1 ? f.Editables
                : f.Editables.Where(e => Regex.IsMatch(e.Nombre, @"^_?(I\d*|ITEM\w*|OPC\w*|NRO\w*|NUM\w*|ELEG\w*)$", RegexOptions.IgnoreCase)).ToList();
            foreach (var c in visibles) cambios[c.Nombre] = opcion;
        }
        Gx.Boton? Aceptar(Gx.Formulario x) => x.Botones.FirstOrDefault(b => b.Evento == "EENTER." && !b.Oculto && b.Valor.Contains("ACEPTAR", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(x);
        var r = await Enviar(antes, cambios, Aceptar);
        if (Gx.EsLogin(r.Texto)) { _pick = null; _logueado = false; throw new WmsError("Se cortó la sesión del WMS: volvé a entrar a la opción de picking (el WMS retoma la OP)."); }
        var paso = LeerPaso(r);
        if (paso.Pantalla == "lista" && paso.Mensaje is { } m && Regex.IsMatch(m, @"TIPEADO\s+ERR", RegexOptions.IgnoreCase))
        {
            // para revisar la pantalla real: lo que había, lo que se mandó y lo que contestó el WMS
            try
            {
                var cap = new Captura(_cfg, "picking_" + Usuario);
                cap.Nota($"Opción {opcion} rechazada por el WMS: {m}. Campos editables: {string.Join(", ", f.Editables.Select(e => e.Nombre))}. Botones: {string.Join(", ", f.Botones.Select(b => b.Nombre + "=" + b.Evento + (b.Oculto ? "(oculto)" : "")))}");
                cap.Guardar("pantalla antes", "", antes.Url.ToString(), antes.Estado, antes.Texto);
                cap.Guardar("respuesta", string.Join("&", Gx.Envio(f, Aceptar(f), cambios).Select(kv => kv.Key + "=" + kv.Value)), r.Url.ToString(), r.Estado, r.Texto);
            }
            catch { }
        }
        return GuardarPick(r);
    });

    public void PickingOlvidar() { _pick = null; _pickBueno = null; UltimoPaso = null; }

    /// <summary>Empresas que el usuario puede elegir en el menú RF del WMS.</summary>
    public Task<List<(int Codigo, string Nombre)>> EmpresasWms() => EnSerie(async () => { await Asegurar(); return Empresas; });
}
