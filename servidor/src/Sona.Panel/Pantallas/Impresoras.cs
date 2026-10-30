using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZIMP – Impresoras de las etiquetas de medida (control de bulto): una por puesto, en la PC del servidor SONA.</summary>
public sealed class ImpresorasEtiquetas : Pantalla
{
    public override string Codigo => "ZIMP";
    public override string Titulo => "Impresoras de etiquetas";
    readonly StackPanel _cuerpo = new();
    readonly Dictionary<string, ComboBox> _porPuesto = new();
    ComboBox? _general;
    readonly CheckBox _alGuardar = new() { Content = "Imprimir al guardar (en la app se puede destildar para un pedido)", Margin = new Thickness(0, 4, 0, 4) };
    readonly CheckBox _soloNuevos = new() { Content = "Imprimir solo los bultos nuevos y los modificados (si no, todos los de la solicitud)", Margin = new Thickness(0, 0, 0, 6) };
    readonly TextBox _copias = Caja(), _ancho = Caja(), _alto = Caja();
    readonly Image _previa = new() { MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };

    protected override void Armar()
    {
        Arriba(Marco(T("Cuando en la app (Control de bulto) se toca \"Guardar y generar etiquetas\", SONA guarda las medidas en LES e imprime la etiqueta de cada bulto " +
                       "en la impresora del puesto elegido. Las impresoras son las instaladas en la PC del servidor SONA.", 12.5, color: Info), InfoClaro, InfoClaro));
        Centro(Desplazable(_cuerpo));
    }

    protected override async Task Cargar()
    {
        var d = await C.Api.Get("/api/bultos/impresion");
        _cuerpo.Children.Clear(); _porPuesto.Clear();
        var instaladas = d?["instaladas"]?.AsArray().Select(x => Str(x)).ToList() ?? new();
        var editar = d?["puedeEditar"]?.GetValue<bool>() == true;
        ComboBox Lista(string actual, string vacio)
        {
            var ops = new List<(string, string)> { ("", vacio) };
            if (actual.Length > 0 && !instaladas.Contains(actual)) ops.Add((actual, actual + " (no instalada)"));
            ops.AddRange(instaladas.Select(i => (i, i)));
            var cb = Combo(ops, actual); cb.IsEnabled = editar; return cb;
        }
        if (d?["windows"]?.GetValue<bool>() != true) _cuerpo.Children.Add(T("⚠ El servidor no está en Windows: la impresión directa no funciona en esta instalación.", 12.5, true, Aviso, new Thickness(0, 0, 0, 8)));
        if (Str(d?["predeterminada"]) is { Length: > 0 } pr) _cuerpo.Children.Add(T("Impresora predeterminada del servidor: " + pr, 12.5, color: Tenue, margen: new Thickness(0, 0, 0, 8)));
        _cuerpo.Children.Add(T("Impresora por puesto", 13.5, true, Marino, new Thickness(0, 0, 0, 6)));
        foreach (var o in d?["origenes"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var cod = Str(o["codigo"]);
            var cb = Lista(Str(d?["impresoras"]?[cod]), "— la general —");
            _porPuesto[cod] = cb;
            var fila = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            fila.Children.Add(Campo(Str(o["nombre"]), cb, 320));
            var b = Boton("🖨 Probar", async () => await Probar(cod)); b.IsEnabled = editar; b.VerticalAlignment = VerticalAlignment.Bottom;
            fila.Children.Add(b);
            _cuerpo.Children.Add(fila);
        }
        _general = Lista(Str(d?["impresora"]), "— predeterminada de Windows —");
        _cuerpo.Children.Add(Campo("Impresora general (para los puestos sin impresora propia)", _general, 320));
        _cuerpo.Children.Add(T("Opciones", 13.5, true, Marino, new Thickness(0, 12, 0, 4)));
        _alGuardar.IsChecked = d?["imprimirAlGuardar"]?.GetValue<bool>() != false; _alGuardar.IsEnabled = editar;
        _soloNuevos.IsChecked = d?["soloNuevos"]?.GetValue<bool>() != false; _soloNuevos.IsEnabled = editar;
        _cuerpo.Children.Add(_alGuardar); _cuerpo.Children.Add(_soloNuevos);
        _copias.Text = Str(d?["copias"]); _ancho.Text = Str(d?["anchoMm"]); _alto.Text = Str(d?["altoMm"]);
        _copias.IsEnabled = _ancho.IsEnabled = _alto.IsEnabled = editar;
        var w = new WrapPanel(); w.Children.Add(Campo("Copias", _copias, 70)); w.Children.Add(Campo("Ancho (mm)", _ancho, 90)); w.Children.Add(Campo("Alto (mm)", _alto, 90));
        _cuerpo.Children.Add(w);
        if (editar) _cuerpo.Children.Add(Boton("Guardar", async () => await Guardar(), true));
        else _cuerpo.Children.Add(T("Solo lectura: para cambiarla hace falta el permiso ZIMP (ZUSR › Permisos).", 12.5, color: Aviso));
        _cuerpo.Children.Add(T("Así sale la etiqueta", 13.5, true, Marino, new Thickness(0, 14, 0, 0)));
        _cuerpo.Children.Add(_previa);
        await Previa();
        // etiquetas de contenedor (Consultar › Reimprimir etiqueta, con QR): Zebra y A4
        var e = await C.Api.Get("/api/impresion/etiquetas");
        _cuerpo.Children.Add(T("Etiquetas de contenedor (Consultar › Reimprimir etiqueta, con QR)", 13.5, true, Marino, new Thickness(0, 18, 0, 6)));
        var zebra = Lista(Str(e?["impresoraZebra"]), "— predeterminada de Windows —");
        var a4 = Lista(Str(e?["impresoraA4"]), "— predeterminada de Windows —");
        var an = Caja(Str(e?["anchoMm"])); var al = Caja(Str(e?["altoMm"]));
        an.IsEnabled = al.IsEnabled = editar;
        var we = new WrapPanel(); we.Children.Add(Campo("Impresora Zebra", zebra, 320)); we.Children.Add(Campo("Ancho (mm)", an, 90)); we.Children.Add(Campo("Alto (mm)", al, 90));
        _cuerpo.Children.Add(we);
        _cuerpo.Children.Add(Campo("Impresora para hojas A4", a4, 320));
        if (editar) _cuerpo.Children.Add(Boton("Guardar impresoras de contenedor", async () => await Hacer(async () =>
        {
            await C.Api.Put("/api/impresion/etiquetas", new { impresoraZebra = Valor(zebra), impresoraA4 = Valor(a4), anchoMm = int.TryParse(an.Text, out var x) ? x : 100, altoMm = int.TryParse(al.Text, out var y) ? y : 80 });
            C.Mensaje("Impresoras de etiquetas de contenedor guardadas", false);
        }), true));
    }

    async Task Previa()
    {
        try
        {
            var bytes = await C.Api.Bytes("/api/bultos/PRUEBA-1/etiqueta/3123456");
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit(); bmp.StreamSource = new System.IO.MemoryStream(bytes); bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bmp.EndInit();
            _previa.Source = bmp;
        }
        catch { _previa.Source = null; }
    }

    async Task Guardar() => await Hacer(async () =>
    {
        await C.Api.Put("/api/bultos/impresion", new
        {
            imprimirAlGuardar = _alGuardar.IsChecked == true, soloNuevos = _soloNuevos.IsChecked == true,
            impresoras = _porPuesto.ToDictionary(x => x.Key, x => Valor(x.Value)), impresora = _general != null ? Valor(_general) : "",
            copias = int.TryParse(_copias.Text, out var c) ? c : 1, anchoMm = int.TryParse(_ancho.Text, out var a) ? a : 100, altoMm = int.TryParse(_alto.Text, out var h) ? h : 80,
        });
        C.Mensaje("Configuración de impresoras guardada", false);
        await Previa();
    });

    async Task Probar(string origen) => await Hacer(async () =>
    {
        var r = await C.Api.Post("/api/bultos/impresion/probar", new { origen });
        if (r?["ok"]?.GetValue<bool>() == true) C.Mensaje($"Etiqueta de prueba enviada a {Str(r["impresora"])}", false);
        else C.Mensaje($"No se pudo imprimir en {Str(r?["impresora"])}: {Str(r?["error"])}", true);
    });
}
