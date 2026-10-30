using System.Data;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZCNT – Tareas de conteo ciego.</summary>
public sealed class Conteos : Pantalla
{
    public override string Codigo => "ZCNT";
    public override string Titulo => "Tareas de conteo";
    protected override string[] Eventos => new[] { "conteo" };
    static readonly Col[] Cols = { new("N°", "id", "numero"), new("Tarea", "nombre", Ancho: 240), new("Modo", "modo", "etiqueta"), new("Artículo", "articulo", "mono"), new("Asignada a", "asignado_a"), new("Contadas", "contadas", "numero"), new("Total", "total", "numero"), new("Diferencias", "diferencias", "numero"), new("Estado", "estado", "estado"), new("Creada", "creado", "fecha"), new("Por", "creado_por") };
    readonly DataGrid _g = Grilla(Cols);
    static readonly Col[] ColsDet = { new("Posición", "posicion", "mono"), new("Contada", "contada"), new("Líneas", "lineas", "numero"), new("Dif.", "diferencias", "numero") };
    readonly DataGrid _det = Grilla(ColsDet);

    protected override void Armar()
    {
        Arriba(Marco(T("Conteo ciego: el contador ve contenedor, artículo, descripción y foto, pero no la cantidad del sistema. Las diferencias y todos los conteos (también los sueltos, sin tarea) se ven en ZDIF.", 12.5, color: Info), InfoClaro, InfoClaro));
        var acc = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        acc.Children.Add(Boton("＋ Nueva tarea", async () => { if (await Nueva()) await CargarSeguro(); }, true));
        acc.Children.Add(Boton("Ver diferencias", () => { if (Seleccion(_g) is { } t) C.Abrir("ZDIF", new() { ["tarea"] = Str(t["id"]) }); }));
        acc.Children.Add(Boton("Todos los conteos", () => C.Abrir("ZDIF", new() { ["ver"] = "todas" })));
        acc.Children.Add(Boton("Cerrar tarea", async () => { if (Seleccion(_g) is { } t && Confirmar($"¿Cerrar la tarea {Str(t["nombre"])}?")) await Hacer(async () => { await C.Api.Post($"/api/conteo/tareas/{Str(t["id"])}/cerrar"); await CargarSeguro(); }, "Tarea cerrada"); }, fondo: Error));
        Arriba(acc);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        g.Children.Add(_g);
        _det.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(_det, 1); g.Children.Add(_det);
        _g.SelectionChanged += async (_, _) =>
        {
            if (Seleccion(_g) is not { } t) { _det.ItemsSource = null; return; }
            try { var d = await C.Api.Get($"/api/conteo/tareas/{Str(t["id"])}"); _det.ItemsSource = Tabla(d?["detalle"]?.AsArray(), ColsDet, "posicion").DefaultView; } catch { }
        };
        Centro(g);
    }

    protected override async Task Cargar() => _g.ItemsSource = Tabla((await C.Api.Get("/api/conteo/tareas"))?.AsArray(), Cols).DefaultView;

    async Task<bool> Nueva()
    {
        var us = await C.Api.Get("/api/usuarios");
        var zs = await C.Api.Get("/api/zonas");
        var w = new Window { Title = "Nueva tarea de conteo", Width = 600, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow, ResizeMode = ResizeMode.NoResize };
        var s = new StackPanel { Margin = new Thickness(16) };
        var nombre = Caja();
        var asig = Combo(new[] { ("", "Cualquier contador") }.Concat(us!.AsArray().Where(u => u!["activo"]?.GetValue<bool>() != false).Select(u => (Str(u!["usuario"]), $"{(Str(u["nombre"]) is { Length: > 0 } n ? n : Str(u["usuario"]))} ({Et(Str(u["rol"]))})"))));
        var w1 = new WrapPanel(); w1.Children.Add(Campo("Nombre", nombre, 260)); w1.Children.Add(Campo("Asignar a", asig, 260)); s.Children.Add(w1);
        var modo = Combo(new[] { ("ciego", "Ciego (no ve cantidades)"), ("confirmacion", "Confirmación (ve lo del sistema y confirma)"), ("articulo", "Por artículo (todas las posiciones del artículo)") });
        var articulo = Caja(mayus: true);
        var w0 = new WrapPanel(); w0.Children.Add(Campo("Modo de conteo", modo, 330)); var campoArt = Campo("Artículo", articulo, 190); campoArt.Visibility = Visibility.Collapsed; w0.Children.Add(campoArt); s.Children.Add(w0);
        var porRango = new RadioButton { Content = "Por rango de pasillo", IsChecked = true, Margin = new Thickness(0, 4, 20, 8) };
        var porLista = new RadioButton { Content = "Lista de posiciones", Margin = new Thickness(0, 4, 0, 8) };
        var rb = new StackPanel { Orientation = Orientation.Horizontal }; rb.Children.Add(porRango); rb.Children.Add(porLista); s.Children.Add(rb);
        var cara = Combo(zs!.AsArray().Select(z => (Str(z!["cara"]), Str(z["cara"]))));
        var desde = Caja("1"); var hasta = Caja("10"); var niveles = Caja();
        var w2 = new WrapPanel(); w2.Children.Add(Campo("Cara", cara, 110)); w2.Children.Add(Campo("Columna desde", desde, 90)); w2.Children.Add(Campo("Columna hasta", hasta, 90)); w2.Children.Add(Campo("Niveles (vacío = todos)", niveles, 140));
        s.Children.Add(w2);
        var lista = new TextBox { Height = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, CharacterCasing = CharacterCasing.Upper, Visibility = Visibility.Collapsed };
        s.Children.Add(lista);
        porLista.Checked += (_, _) => { w2.Visibility = Visibility.Collapsed; lista.Visibility = Visibility.Visible; };
        porRango.Checked += (_, _) => { w2.Visibility = Visibility.Visible; lista.Visibility = Visibility.Collapsed; };
        modo.SelectionChanged += (_, _) =>
        {
            var art = Valor(modo) == "articulo";
            campoArt.Visibility = art ? Visibility.Visible : Visibility.Collapsed;
            rb.Visibility = art ? Visibility.Collapsed : Visibility.Visible;
            w2.Visibility = art || porLista.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            lista.Visibility = !art && porLista.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        };
        var ok = false;
        var bs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        bs.Children.Add(Boton("Cancelar", () => w.Close()));
        bs.Children.Add(Boton("Crear", async () =>
        {
            if (nombre.Text.Trim().Length == 0) { MessageBox.Show("Poné un nombre."); return; }
            try
            {
                if (Valor(modo) == "articulo" && articulo.Text.Trim().Length == 0) { MessageBox.Show("Escribí el artículo."); return; }
                object cuerpo = Valor(modo) == "articulo"
                    ? new { nombre = nombre.Text.Trim(), asignado_a = Valor(asig), modo = "articulo", articulo = articulo.Text.Trim() }
                    : porLista.IsChecked == true
                    ? new { nombre = nombre.Text.Trim(), asignado_a = Valor(asig), modo = Valor(modo), posiciones = lista.Text.Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries) }
                    : (object)new { nombre = nombre.Text.Trim(), asignado_a = Valor(asig), modo = Valor(modo), rango = new { cara = Valor(cara), desde = int.Parse(desde.Text), hasta = int.Parse(hasta.Text),
                        niveles = niveles.Text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList() } };
                await C.Api.Post("/api/conteo/tareas", cuerpo);
                C.Mensaje("Tarea creada y enviada a los contadores", false); ok = true; w.Close();
            }
            catch (Exception e) { MessageBox.Show(e.Message); }
        }, true));
        s.Children.Add(bs);
        w.Content = s;
        w.ShowDialog();
        return ok;
    }
}

/// <summary>ZDIF – Diferencias de conteo para revisar.</summary>
public sealed class Diferencias : Pantalla
{
    public override string Codigo => "ZDIF";
    public override string Titulo => "Conteos y diferencias";
    protected override string[] Eventos => new[] { "conteo" };
    static readonly Col[] Cols =
    {
        new("Fecha", "hora", "fecha"), new("Tarea", "tarea"), new("Posición", "posicion", "mono"), new("Contenedor", "contenedor", "mono"), new("Artículo", "articulo", "mono"),
        new("Descripción", "descripcion", Ancho: 200), new("Sistema", "cantidad_wms", "numero"), new("Contado", "cantidad_contada", "numero"), new("Novedad", "novedad", "estado"),
        new("Contó", "usuario"), new("Revisión", "revision", "estado"),
    };
    readonly DataGrid _g = Grilla(Cols, true);
    readonly ComboBox _tarea = Combo(new[] { ("", "Todas") });
    readonly ComboBox _rev = Combo(new[] { ("pendiente", "Pendientes"), ("", "Todas"), ("aprobada", "Aprobadas"), ("recontar", "A recontar"), ("rechazada", "Rechazadas") });
    readonly ComboBox _ver = Combo(new[] { ("dif", "Solo con diferencia"), ("todas", "Todos los conteos (también OK)") });
    readonly ComboBox _asig = Combo(new[] { ("", "Cualquier contador") });

    /// <summary>Columna con los botones de reconteo al lado de cada diferencia.</summary>
    void ColumnaReconteo()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var oculto = new Style(typeof(StackPanel));
        var dt = new DataTrigger { Binding = new Binding("[novedad]"), Value = Et("ok") };
        dt.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        oculto.Triggers.Add(dt);
        panel.SetValue(FrameworkElement.StyleProperty, oculto);
        foreach (var (texto, tag, tip) in new[] { ("↻ Recontar", "posicion", "Tarea de reconteo de esta posición"), ("🔍 Todo el artículo", "articulo", "Contar el artículo en todas las posiciones donde figura") })
        {
            var b = new FrameworkElementFactory(typeof(Button));
            b.SetValue(ContentControl.ContentProperty, texto);
            b.SetValue(FrameworkElement.TagProperty, tag);
            b.SetValue(FrameworkElement.ToolTipProperty, tip);
            b.SetValue(Control.PaddingProperty, new Thickness(6, 0, 6, 0));
            b.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
            b.SetValue(Control.FontSizeProperty, 11.5);
            b.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
            panel.AppendChild(b);
        }
        _g.Columns.Insert(0, new DataGridTemplateColumn { Header = "Reconteo", CellTemplate = new DataTemplate { VisualTree = panel } });
        _g.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(async (_, e) =>
        {
            if (e.OriginalSource is not Button { Tag: string tipo } b || b.DataContext is not DataRowView rv || rv.Row["__json"] is not JsonObject l) return;
            e.Handled = true;
            await Reconteo(new List<JsonObject> { l }, tipo);
        }));
    }

    async Task Reconteo(List<JsonObject> lineas, string tipo)
    {
        if (lineas.Count == 0) { C.Mensaje("Elegí al menos una línea.", true); return; }
        var que = tipo == "articulo"
            ? "contar " + string.Join(", ", lineas.Select(l => Str(l["articulo"])).Where(a => a.Length > 0).Distinct()) + " en todas las posiciones donde figura"
            : "recontar " + string.Join(", ", lineas.Select(l => Str(l["posicion"])).Distinct());
        if (!Confirmar($"¿Crear la tarea para {que}?\nSe envía a {(Valor(_asig).Length > 0 ? Valor(_asig) : "cualquier contador")}.")) return;
        await Hacer(async () =>
        {
            var r = await C.Api.Post("/api/conteo/reconteo", new { lineas = lineas.Select(l => l["id"]!.GetValue<long>()).ToList(), tipo, asignado_a = Valor(_asig) });
            await CargarSeguro();
            C.Mensaje("Tarea creada: " + string.Join(" · ", r?.AsArray().Select(t => $"{Str(t!["nombre"])} ({t["posiciones"]?.AsArray().Count} posiciones)") ?? Enumerable.Empty<string>()), false);
        });
    }

    protected override void Armar()
    {
        ColumnaReconteo();
        var f = Filtros();
        if (Parametros.TryGetValue("ver", out var ver)) { _ver.SelectedValue = ver; _rev.SelectedValue = ""; }
        f.Children.Add(Campo("Tarea", _tarea, 240)); f.Children.Add(Campo("Revisión", _rev, 150)); f.Children.Add(Campo("Mostrar", _ver, 230));
        _tarea.SelectionChanged += async (_, _) => await CargarSeguro(); _rev.SelectionChanged += async (_, _) => await CargarSeguro();
        _ver.SelectionChanged += async (_, _) => await CargarSeguro();
        foreach (var (r, t, color) in new[] { ("aprobada", "Aprobar", Ok), ("recontar", "Pedir recuento", Marino2), ("rechazada", "Rechazar", Error) })
        {
            var b = Boton(t, async () =>
            {
                var sel = Selecciones(_g);
                if (sel.Count == 0) return;
                await Hacer(async () => { foreach (var l in sel) await C.Api.Post($"/api/conteo/lineas/{Str(l["id"])}/revision", new { revision = r }); await CargarSeguro(); }, $"{sel.Count} líneas: {t.ToLower()}");
            }, fondo: color);
            b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 8, 6);
            f.Children.Add(b);
        }
        f.Children.Add(Campo("Reconteo para", _asig, 200));
        foreach (var (t, tipo) in new[] { ("↻ Recontar posiciones elegidas", "posicion"), ("🔍 Contar artículos en todas sus posiciones", "articulo") })
        {
            var b = Boton(t, async () => await Reconteo(Selecciones(_g), tipo), fondo: Marino);
            b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 8, 6);
            f.Children.Add(b);
        }
        Arriba(Marco(f, FilaPar, Linea2));
        Centro(_g);
        _tarea.Items.Add(Tuple.Create("suelto", "Conteos sueltos (sin tarea)"));
        _ = CargarTareas();
    }

    async Task CargarTareas()
    {
        try
        {
            foreach (var u in (await C.Api.Get("/api/usuarios"))!.AsArray().Where(u => u!["activo"]?.GetValue<bool>() != false))
                _asig.Items.Add(Tuple.Create(Str(u!["usuario"]), Str(u["nombre"]) is { Length: > 0 } n ? $"{n} ({Str(u["usuario"])})" : Str(u["usuario"])));
            foreach (var t in (await C.Api.Get("/api/conteo/tareas"))!.AsArray()) _tarea.Items.Add(Tuple.Create(Str(t!["id"]), $"{Str(t["id"])} · {Str(t["nombre"])}"));
            if (Parametros.TryGetValue("tarea", out var id)) _tarea.SelectedValue = id;
        }
        catch { }
    }

    protected override async Task Cargar()
    {
        var q = new List<string>();
        if (Valor(_tarea).Length > 0) q.Add("tarea=" + Valor(_tarea));
        if (Valor(_rev).Length > 0) q.Add("revision=" + Valor(_rev));
        if (Valor(_ver) == "todas") q.Add("todas=1");
        _g.ItemsSource = Tabla((await C.Api.Get("/api/conteo/diferencias?" + string.Join("&", q)))?.AsArray(), Cols).DefaultView;
    }
}
