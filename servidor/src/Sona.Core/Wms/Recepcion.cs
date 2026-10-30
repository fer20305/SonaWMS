using System.Globalization;
using System.Text.RegularExpressions;

namespace Sona.Wms;

public sealed record RenglonRecepcion(string Articulo, double Cantidad);
public sealed record RecepcionCreada(bool Ok, string? Numero, string Mensaje, byte[]? Comprobante);
public sealed record ContenedorRecibir(string Contenedor, string Articulo, double Cantidad, string? Lote);
public sealed record ContenedorRecibido(string Contenedor, bool Ok, string Mensaje);
public enum EstadoArticuloWms { NoExiste, NoConfigurado, Ok }

/// <summary>
/// Ingreso en el WMS de escritorio: "Ingreso de Operaciones" (hingeve → hingeve2) para armar la recepción por contenedor,
/// y Datos Maestros › Artículos para ver si el artículo existe y está configurado (y configurarlo).
/// </summary>
public sealed partial class SesionEscritorio
{
    static string Num(double d) => d % 1 == 0 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString(CultureInfo.InvariantCulture).Replace('.', ',');

    void Guardar(string nombre, Respuesta r)
    {
        try
        {
            var dir = Path.Combine(_cfg.Carpeta, "wms_paginas");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{nombre}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.html"), $"<!-- {r.Url} -->\n" + r.Texto);
        }
        catch { }
    }

    /// <summary>Ingreso de Operaciones › Escritorio › Recepción por contenedor › Original, con la empresa y origen "Proveedor".</summary>
    async Task<Respuesta> AbrirRecepcion(int empresa)
    {
        var r = await Abrir("hingeve.aspx", "_OTIPOOPERC");
        r = await Enviar(r, Evento("E_CFINGSALOTROS.CLICK."), new() { ["_CFINGSALOTROS"] = "E" });
        r = await Enviar(r, Evento("E_OTIPOOPERC.CLICK."), new() { ["_OTIPOOPERC"] = "EEXT" });
        r = await Enviar(r, Evento("E_OTIPOOPDERIV.CLICK."), new() { ["_OTIPOOPDERIV"] = "EEXT" });
        r = await Enviar(r, Evento("E_NNATROPDERIVADA.CLICK."), new() { ["_NNATROPDERIVADA"] = "ORIG" });
        var f = Gx.Leer(r.Texto);
        r = await Enviar(r, f.Botones.FirstOrDefault(b => b.Nombre == "BUTTON1" && b.Evento == "EENTER.") ?? Gx.BotonEnter(f), new());
        if (!r.Url.AbsolutePath.Contains("hingeve2", StringComparison.OrdinalIgnoreCase))
        {
            Guardar("recepcion_no_abrio", r);
            throw new WmsError("El WMS no abrió \"Ingreso de Operaciones\" para la recepción por contenedor: " + MensajesPantalla(r.Texto));
        }
        r = await Enviar(r, Evento("E_EVEMPRESA.CLICK."), new() { ["_EVEMPRESA"] = empresa.ToString() });
        r = await Enviar(r, Evento("E_EVCATCLIENTE.CLICK."), new() { ["_EVCATCLIENTE"] = "02" });   // origen: PROVEEDOR
        return r;
    }

    /// <summary>Proveedores que ofrece el WMS para la empresa (código y nombre, el nombre viene cortado a 20 letras).</summary>
    public Task<List<(string Codigo, string Nombre)>> Proveedores(int empresa) => EnSerie(async () =>
    {
        var r = await AbrirRecepcion(empresa);
        var f = Gx.Leer(r.Texto);
        return (f.Selects.GetValueOrDefault("_PROVEEDOR") ?? new()).Where(o => o.Valor.Trim().Length > 0)
            .Select(o => (o.Valor.Trim(), o.Texto.Trim())).ToList();
    });

    /// <summary>
    /// Arma la recepción por contenedor en el WMS (lo que hoy se copia a mano del LES): empresa, proveedor, remito
    /// (número, centro emisor, letra), fecha de emisión, documento externo (= N° de recepción del LES) y los renglones.
    /// Devuelve el número de recepción que da el WMS (ej. 930) y el comprobante.
    /// </summary>
    public Task<RecepcionCreada> CrearRecepcion(int empresa, string proveedor, long remito, int centroEmisor, string letra, string docExterno,
        DateTime fechaEmision, List<RenglonRecepcion> renglones) => EnSerie(async () =>
    {
        if (renglones.Count == 0) throw new WmsError("La recepción no tiene artículos.");
        var r = await AbrirRecepcion(empresa);
        var f = Gx.Leer(r.Texto);
        if (f.Selects.TryGetValue("_PROVEEDOR", out var provs) && !provs.Any(o => o.Valor.Trim() == proveedor))
            throw new WmsError($"El WMS no tiene el proveedor {proveedor} para esta empresa.");
        var cabecera = new Dictionary<string, string>
        {
            ["_EVEMPRESA"] = empresa.ToString(), ["_EVCATCLIENTE"] = "02", ["_PROVEEDOR"] = proveedor,
            ["_NROPEDCTE"] = remito.ToString(CultureInfo.InvariantCulture), ["_EVCTOEMCOM"] = centroEmisor.ToString(), ["_EVLETRCOMP"] = letra.Trim().ToUpperInvariant(),
            ["_FECHA"] = fechaEmision.ToString("dd/MM/yyyy"), ["_EVDOCEXTERNO"] = docExterno,
        };
        foreach (var l in renglones)
        {
            var cambios = new Dictionary<string, string>(cabecera) { ["_ARTCOD"] = l.Articulo.Trim().ToUpperInvariant(), ["_CANTLOG"] = Num(l.Cantidad) };
            f = Gx.Leer(r.Texto);
            r = await Enviar(r, BotonDe(f, "AGREGALINEA", "E'CARGA LA SDT'."), cambios);
            var cargados = Gx.Grilla(r.Texto).Values.Select(c => c.GetValueOrDefault("CTLGRPCOD")?.Trim().ToUpperInvariant()).ToHashSet();
            if (!cargados.Contains(l.Articulo.Trim().ToUpperInvariant()))
            {
                Guardar("recepcion_renglon", r);
                var m = MensajesPantalla(r.Texto);
                throw new WmsError($"El WMS no agregó el renglón {l.Articulo} × {Num(l.Cantidad)}" + (m.Length > 0 ? ": " + m : ". ¿El artículo existe y está configurado para la empresa?"));
            }
        }
        f = Gx.Leer(r.Texto);
        var fin = await Enviar(r, BotonDe(f, "GRABAR", "E'GRABAR'."), cabecera);
        var msj = MensajesPantalla(fin.Texto);
        var link = Regex.Match(fin.Texto, @"href=""(oimpintpdf\.aspx\?([^""]*))""", RegexOptions.IgnoreCase);
        string? numero = null;
        if (Gx.Leer(fin.Texto).Valores.TryGetValue("_NRODOCBASE", out var nd) && long.TryParse(nd.Trim(), out var n) && n > 0) numero = n.ToString();
        if (numero == null && link.Success)
        {
            // parámetros del comprobante: el número de recepción es el número que no es la empresa, el remito ni el doc. externo
            var conocidos = new HashSet<string> { empresa.ToString(), remito.ToString(CultureInfo.InvariantCulture), docExterno.Trim().TrimStart('0'), centroEmisor.ToString() };
            numero = Uri.UnescapeDataString(Gx.Desescapar(link.Groups[2].Value)).Split(',', ';', '&')
                .Select(x => x.Trim().TrimStart('0')).FirstOrDefault(x => x.Length > 0 && x.All(char.IsDigit) && !conocidos.Contains(x));
        }
        if (numero == null)
        {
            Guardar("recepcion_grabar", fin);
            return new RecepcionCreada(false, null, msj.Length > 0 ? msj : "El WMS no informó el número de recepción.", null);
        }
        byte[]? pdf = null;
        if (link.Success)
            try
            {
                var (datos, _, url) = await _nav.Bajar(new Uri(fin.Url, Gx.Desescapar(link.Groups[1].Value)), fin.Url.ToString());
                if (datos.Length > 4 && datos[0] == '%' && datos[1] == 'P') pdf = datos;
                else
                {
                    var html = System.Text.Encoding.UTF8.GetString(datos);
                    var m = Regex.Match(html, @"(?:src|href|data)=""([^""]+\.pdf[^""]*)""", RegexOptions.IgnoreCase);
                    if (m.Success) { var (d2, _, _) = await _nav.Bajar(new Uri(url, Gx.Desescapar(m.Groups[1].Value)), url.ToString()); if (d2.Length > 4 && d2[0] == '%') pdf = d2; }
                }
            }
            catch { /* el comprobante no es crítico */ }
        return new RecepcionCreada(true, numero, msj.Length > 0 ? msj : "Recepción generada.", pdf);
    });

    // ================================================================ artículos

    async Task<bool> BuscarArticulo(int empresa, string codigo, string configurado)
    {
        var r = await Abrir("hgrpart.aspx", "SEARCHBUTTON");
        var f = Gx.Leer(r.Texto);
        r = await Enviar(r, BotonDe(f, "SEARCHBUTTON", "E'SEARCH'."), new() { ["_COD_EMP"] = empresa.ToString(), ["_GRPCOD"] = codigo, ["_ARTCFG"] = configurado, ["_ARTDSC"] = "" });
        return Gx.Grilla(r.Texto).Values.Any(c => string.Equals(c.GetValueOrDefault("GRPCOD")?.Trim(), codigo, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Datos Maestros › Artículos: si el artículo existe para la empresa y si está "configurado" (si no, el handheld lo rechaza).</summary>
    public Task<EstadoArticuloWms> EstadoArticulo(int empresa, string codigo) => EnSerie(async () =>
    {
        var c = codigo.Trim().ToUpperInvariant();
        if (!await BuscarArticulo(empresa, c, "")) return EstadoArticuloWms.NoExiste;
        return await BuscarArticulo(empresa, c, "N") ? EstadoArticuloWms.NoConfigurado : EstadoArticuloWms.Ok;
    });

    /// <summary>
    /// Configura un artículo que existe pero está "no configurado", como se hizo a mano: abrir la ficha y Grabar (queda configurado),
    /// confirmar la zonificación por herencia que propone el WMS y salir del alta de código de barras.
    /// </summary>
    public Task<(bool Ok, string Mensaje)> ConfigurarArticulo(int empresa, string codigo) => EnSerie(async () =>
    {
        var c = codigo.Trim().ToUpperInvariant();
        var r = await Abrir($"tgrpart.aspx?UPD,{empresa},{c}");
        var f = Gx.Leer(r.Texto);
        r = await Enviar(r, BotonDe(f, "BUTTON2", "E'GRABAR'."), new());
        var pasos = new List<string> { "ficha grabada" };
        if (r.Url.AbsolutePath.Contains("hzonificacionporherencia", StringComparison.OrdinalIgnoreCase))
        {
            f = Gx.Leer(r.Texto);
            r = await Enviar(r, f.Botones.FirstOrDefault(b => b.Nombre == "BUTTON2") ?? Gx.BotonEnter(f), new());
            pasos.Add("zonificación confirmada");
        }
        else if (r.Url.AbsolutePath.Contains("tgrpart", StringComparison.OrdinalIgnoreCase))
        {
            Guardar("articulo_configurar", r);
            var m = MensajesPantalla(r.Texto);
            return (false, "El WMS no grabó la ficha del artículo" + (m.Length > 0 ? ": " + m : "."));
        }
        var ok = !await BuscarArticulo(empresa, c, "N") && await BuscarArticulo(empresa, c, "");
        return (ok, ok ? "Artículo configurado (" + string.Join(", ", pasos) + ")." : "El WMS sigue mostrando el artículo como no configurado.");
    });
}

/// <summary>Recepción por contenedor desde el handheld (Recepciones › 1): orden de recepción, contenedor, artículo, cantidad y lote.</summary>
public sealed partial class SesionHandheld
{
    public Task<List<ContenedorRecibido>> RecibirContenedores(int empresa, string nroRecepcion, List<ContenedorRecibir> items) => EnSerie(async () =>
    {
        var res = new List<ContenedorRecibido>();
        var menu = await OpcionMenu(empresa, 1);   // RECEPCIONES
        var r = await Enviar(menu, new() { ["_OPCION"] = "1" }, f => f.BotonEvento("EENTER.") ?? Gx.BotonEnter(f));   // 1-RECEPCION POR CONTENEDOR
        if (!r.Url.AbsolutePath.Contains("hingetqextrf", StringComparison.OrdinalIgnoreCase))
        {
            GuardarPagina("recepcion_rf_no_abrio", r);
            throw new WmsError("El handheld no abrió \"Recepción por contenedor\": " + Gx.Mensajes(Gx.Leer(r.Texto)));
        }
        var fo = Gx.Leer(r.Texto);
        var orden = fo.Selects.GetValueOrDefault("_ORDENDERECEPCION")?.FirstOrDefault(o => Regex.IsMatch(o.Valor, @"^R:\s*0*" + Regex.Escape(nroRecepcion.Trim()) + @"\b"))
                    ?? throw new WmsError($"La orden de recepción {nroRecepcion} no aparece en el handheld para esta empresa (¿ya se recibió todo o es de otra empresa?).");
        r = await Enviar(r, new() { ["_ORDENDERECEPCION"] = orden.Valor }, _ => new Gx.Boton("", "", "E_ORDENDERECEPCION.CLICK.", "", "", false));
        foreach (var it in items)
        {
            var cambios = new Dictionary<string, string>
            {
                ["_ORDENDERECEPCION"] = orden.Valor, ["_PALLET"] = it.Contenedor.Trim().ToUpperInvariant(), ["_ARTCOD"] = it.Articulo.Trim().ToUpperInvariant(),
                ["_CANTBULTOS"] = it.Cantidad % 1 == 0 ? ((long)it.Cantidad).ToString(CultureInfo.InvariantCulture) : it.Cantidad.ToString(CultureInfo.InvariantCulture).Replace('.', ','),
                ["_LOTE"] = it.Lote ?? "",
            };
            Gx.Boton? Leer(Gx.Formulario f) => f.Botones.FirstOrDefault(b => b.Nombre.Equals("LEERETIQUETA", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f);
            var p = await Enviar(r, cambios, Leer);
            var fp = Gx.Leer(p.Texto);
            var ok = fp.Valores.TryGetValue("_MENSOK", out var mok) && mok.Contains("OK", StringComparison.OrdinalIgnoreCase);
            if (!ok)
            {
                // como en el handheld: a veces pide los datos de a uno (contenedor → artículo → cantidad); se reenvía
                p = await Enviar(p, cambios, Leer);
                fp = Gx.Leer(p.Texto);
                ok = fp.Valores.TryGetValue("_MENSOK", out mok) && mok.Contains("OK", StringComparison.OrdinalIgnoreCase);
            }
            var msj = string.Join(" ", fp.Valores.Where(kv => Regex.IsMatch(kv.Key, @"^_MENS(LOC\d|OK)$")).Select(kv => kv.Value.Trim()).Where(s => s.Length > 0));
            if (!ok) GuardarPagina("recepcion_rf_" + it.Contenedor, p);
            res.Add(new ContenedorRecibido(it.Contenedor, ok, ok ? "OK" : (msj.Length > 0 ? msj : "El WMS no confirmó el contenedor.")));
            r = p;
        }
        // 3 = salir de la recepción (vuelve al menú), como lo hace el operario
        try { await Enviar(r, new() { ["_OPCION"] = "3", ["_PALLET"] = "", ["_ARTCOD"] = "", ["_CANTBULTOS"] = "0" }, f => f.Botones.FirstOrDefault(b => b.Nombre.Equals("LEERETIQUETA", StringComparison.OrdinalIgnoreCase)) ?? Gx.BotonEnter(f)); }
        catch { }
        return res;
    });
}
