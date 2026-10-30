using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZMON – Monitor en vivo.</summary>
public sealed class MonitorPantalla : Pantalla
{
    public override string Codigo => "ZMON";
    public override string Titulo => "Monitor del depósito";
    protected override string[] Eventos => new[] { "movimiento", "autorizacion", "layout" };
    readonly WrapPanel _kpis = new();
    readonly StackPanel _barras = new() { Orientation = Orientation.Horizontal, Height = 150, VerticalAlignment = VerticalAlignment.Bottom };
    readonly DataGrid _oper = Grilla(new Col[] { new("Operario", "usuario"), new("Nombre", "nombre"), new("Rol", "rol", "etiqueta"), new("Movs.", "hoy", "numero"), new("Pend.", "pendientes", "numero"), new("Errores", "errores", "numero"), new("Último", "ultimo", "fecha") });
    static readonly Col[] ColsUlt = { new("Hora", "hora_servidor", "fecha"), new("Operario", "usuario"), new("Tipo", "tipo", "etiqueta"), new("Contenedor", "contenedor", "mono"), new("Artículo", "articulo", "mono"), new("Descripción", "descripcion"), new("Empresa", "empresa_nombre"), new("Origen", "origen", "mono"), new("Destino", "destino", "mono"), new("Estado", "estado", "estado") };
    readonly DataGrid _ult = Grilla(ColsUlt);
    readonly TextBlock _layout = T("", 12, color: Tenue);
    readonly Border _ocup = new()
    {
        CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed,
        BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(46, 120, 170, 220)), BorderThickness = new Thickness(1),
        Background = new System.Windows.Media.RadialGradientBrush(
            new System.Windows.Media.GradientStopCollection { new(System.Windows.Media.Color.FromRgb(0x17, 0x47, 0x6E), 0), new(System.Windows.Media.Color.FromRgb(0x0B, 0x2A, 0x42), 0.55), new(System.Windows.Media.Color.FromRgb(0x08, 0x1F, 0x31), 1) })
        { GradientOrigin = new Point(0, 0), Center = new Point(0, 0), RadiusX = 1.2, RadiusY = 1.6 },
    };

    // semáforo de ocupación
    static readonly System.Windows.Media.SolidColorBrush SVerde = Hex("#22C55E"), SAmarillo = Hex("#F59E0B"), SRojo = Hex("#EF4444"), SInhab = Hex("#64748B"), SSinDato = Hex("#DDE2E7");
    static System.Windows.Media.Brush ColorSemaforo(string e) => e switch { "v" => SVerde, "a" => SAmarillo, "r" => SRojo, "x" => SInhab, _ => SSinDato };

    protected override void Armar()
    {
        var s = new StackPanel();
        s.Children.Add(_kpis);
        // ocupación de posiciones: franja compacta estilo tablero (medidor del total + una ficha por cara)
        s.Children.Add(_ocup);
        var dos = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        dos.ColumnDefinitions.Add(new ColumnDefinition()); dos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); dos.ColumnDefinitions.Add(new ColumnDefinition());
        var b1 = new StackPanel(); b1.Children.Add(T("Movimientos por hora (hoy)", 13.5, true, Marino, new Thickness(0, 0, 0, 8))); b1.Children.Add(_barras);
        var c1 = Marco(b1); Grid.SetColumn(c1, 0); dos.Children.Add(c1);
        var b2 = new DockPanel(); var t2 = T("Operarios hoy", 13.5, true, Marino, new Thickness(0, 0, 0, 8)); DockPanel.SetDock(t2, Dock.Top); b2.Children.Add(t2); _oper.Height = 170; b2.Children.Add(_oper);
        var c2 = Marco(b2); Grid.SetColumn(c2, 2); dos.Children.Add(c2);
        _oper.MouseDoubleClick += (_, _) => { if (Seleccion(_oper) is { } o) C.Abrir("ZMOV", new() { ["usuario"] = Str(o["usuario"]) }); };
        s.Children.Add(dos);
        s.Children.Add(T("Últimos movimientos (en vivo)", 13.5, true, Marino, new Thickness(0, 0, 0, 6)));
        _ult.Height = 330;
        _ult.MouseDoubleClick += (_, _) => { if (Seleccion(_ult) is { } o) C.Abrir("ZMOV", new() { ["texto"] = Str(o["contenedor"]) }); };
        s.Children.Add(_ult);
        s.Children.Add(_layout);
        Centro(Desplazable(s));
    }

    protected override async Task Cargar()
    {
        var d = await C.Api.Get("/api/monitor");
        var tot = d?["totales"];
        _kpis.Children.Clear();
        _kpis.Children.Add(Kpi("Movimientos hoy", Str(tot?["hoy"]) is { Length: > 0 } h ? h : "0", Marino));
        _kpis.Children.Add(Kpi("Autorizaciones pendientes", Str(d?["autorizacionesPendientes"]), Naranja, () => C.Abrir("ZAUT", null)));
        _kpis.Children.Add(Kpi("Pendientes de cargar en WMS", Str(tot?["pendientes"]) is { Length: > 0 } p ? p : "0", Aviso, () => C.Abrir("ZMOV", new() { ["estado"] = "pendiente_wms" })));
        _kpis.Children.Add(Kpi("Con error del WMS", Str(tot?["errores"]) is { Length: > 0 } e ? e : "0", Error, () => C.Abrir("ZMOV", new() { ["estado"] = "error" })));
        _kpis.Children.Add(Kpi("Operarios activos hoy", (d?["operarios"]?.AsArray().Count ?? 0).ToString(), Ok));
        _kpis.Children.Add(Kpi("Equipos conectados", Str(d?["conectados"]), Marino2));
        var por = d?["porHora"]?.AsArray().ToDictionary(x => x!["hora"]!.GetValue<int>(), x => x!["n"]!.GetValue<int>()) ?? new();
        var max = Math.Max(1, por.Values.DefaultIfEmpty(0).Max());
        _barras.Children.Clear();
        for (var hh = 5; hh <= 21; hh++)
        {
            var n = por.GetValueOrDefault(hh);
            var col = new StackPanel { Width = 30, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Bottom };
            col.Children.Add(T(n > 0 ? n.ToString() : "", 10, color: Tenue));
            col.Children.Add(new Border { Height = Math.Max(2, 110.0 * n / max), Background = hh == DateTime.Now.Hour ? Naranja : Marino2, CornerRadius = new CornerRadius(3, 3, 0, 0) });
            col.Children.Add(new TextBlock { Text = hh.ToString(), FontSize = 10, Foreground = Tenue, HorizontalAlignment = HorizontalAlignment.Center });
            _barras.Children.Add(col);
        }
        _oper.ItemsSource = Tabla(d?["operarios"]?.AsArray(), new Col[] { new("", "usuario"), new("", "nombre"), new("", "rol", "etiqueta"), new("", "hoy", "numero"), new("", "pendientes", "numero"), new("", "errores", "numero"), new("", "ultimo", "fecha") }, "usuario").DefaultView;
        _ult.ItemsSource = Tabla(d?["ultimos"]?.AsArray(), ColsUlt).DefaultView;
        await CargarOcupacion();
        var l = d?["layout"];
        _layout.Text = l is JsonObject ? $"Layout sincronizado del WMS: {FechaHora(Str(l["fin"]))} · {Str(l["posiciones"])} posiciones" : "";
    }

    async Task CargarOcupacion()
    {
        JsonNode? o;
        try { o = await C.Api.Get("/api/monitor/ocupacion"); } catch { return; }
        int Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var x) ? x : 0;
        var tot = Num(o?["total"]);
        if (tot == 0) { _ocup.Visibility = Visibility.Collapsed; return; }
        int libres = Num(o?["libres"]), parc = Num(o?["parciales"]), llenas = Num(o?["llenas"]), inhab = Num(o?["inhabilitadas"]);
        var hab = Math.Max(1, tot - inhab);
        var pct = (int)Math.Round(100.0 * (parc + llenas) / hab);
        var blanco = System.Windows.Media.Brushes.White;
        var tenueO = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 220, 230, 239));

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // medidor circular: llenas (rojo), con lugar (ámbar) y libres (verde) sobre las habilitadas
        const double D = 84, R = 34, Grosor = 8;
        var med = new Grid { Width = D, Height = D, Margin = new Thickness(0, 0, 18, 0), ToolTip = $"{pct}% de las posiciones habilitadas tiene mercadería" };
        var lienzo = new Canvas { Width = D, Height = D };
        lienzo.Children.Add(new System.Windows.Shapes.Ellipse { Width = 2 * R, Height = 2 * R, Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(22, 255, 255, 255)), StrokeThickness = Grosor, Margin = new Thickness(D / 2 - R, D / 2 - R, 0, 0) });
        double ang = 0;
        foreach (var (n, color) in new[] { (llenas, SRojo), (parc, SAmarillo), (libres, SVerde) })
        {
            if (n <= 0) continue;
            var barrido = 360.0 * n / hab;
            var hasta = ang + Math.Max(0.5, barrido - 1.5);
            Point P(double a) { var r = (a - 90) * Math.PI / 180; return new Point(D / 2 + R * Math.Cos(r), D / 2 + R * Math.Sin(r)); }
            if (barrido >= 359.5) lienzo.Children.Add(new System.Windows.Shapes.Ellipse { Width = 2 * R, Height = 2 * R, Stroke = color, StrokeThickness = Grosor, Margin = new Thickness(D / 2 - R, D / 2 - R, 0, 0) });
            else
            {
                var fig = new System.Windows.Media.PathFigure { StartPoint = P(ang), IsClosed = false };
                fig.Segments.Add(new System.Windows.Media.ArcSegment(P(hasta), new Size(R, R), 0, hasta - ang > 180, System.Windows.Media.SweepDirection.Clockwise, true));
                lienzo.Children.Add(new System.Windows.Shapes.Path { Data = new System.Windows.Media.PathGeometry(new[] { fig }), Stroke = color, StrokeThickness = Grosor });
            }
            ang += barrido;
        }
        med.Children.Add(lienzo);
        var centro = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var num = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
        num.Inlines.Add(new System.Windows.Documents.Run(pct.ToString()) { FontSize = 22, FontWeight = FontWeights.Bold, Foreground = blanco });
        num.Inlines.Add(new System.Windows.Documents.Run("%") { FontSize = 12, Foreground = tenueO });
        centro.Children.Add(num);
        centro.Children.Add(new TextBlock { Text = "OCUPADO", FontSize = 9, Foreground = tenueO, HorizontalAlignment = HorizontalAlignment.Center });
        med.Children.Add(centro);
        g.Children.Add(med);

        // contadores
        var kp = new Grid { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        kp.ColumnDefinitions.Add(new ColumnDefinition()); kp.ColumnDefinitions.Add(new ColumnDefinition());
        kp.RowDefinitions.Add(new RowDefinition()); kp.RowDefinitions.Add(new RowDefinition());
        var i = 0;
        foreach (var (n, t, color) in new[] { (libres, "libres", SVerde), (parc, "con lugar", SAmarillo), (llenas, "llenas", SRojo), (inhab, "inhab.", SInhab) })
        {
            var f = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 16, 2) };
            f.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = ((System.Windows.Media.SolidColorBrush)color).Color, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.9 } });
            f.Children.Add(new TextBlock { Text = n.ToString(), FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = blanco, MinWidth = 34, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 6, 0) });
            f.Children.Add(new TextBlock { Text = t, FontSize = 11, Foreground = tenueO, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(f, i % 2); Grid.SetRow(f, i / 2); kp.Children.Add(f); i++;
        }
        var kpBorde = new Border { Child = kp, BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 255, 255)), BorderThickness = new Thickness(0, 0, 1, 0), Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(kpBorde, 1); g.Children.Add(kpBorde);

        // una ficha por cara: código, % y barra (llenas / con lugar / libres); clic = mapa del pasillo
        var fichas = new WrapPanel();
        foreach (var cara in o?["caras"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var nombre = Str(cara["cara"]);
            int ct = Num(cara["total"]), cl = Num(cara["libres"]), cp = Num(cara["parciales"]), cr = Num(cara["llenas"]), ci = Num(cara["inhabilitadas"]);
            var h = Math.Max(1, ct - ci);
            var p = (int)Math.Round(100.0 * (cp + cr) / h);
            var acento = p >= 90 ? SRojo : p >= 70 ? SAmarillo : null;
            var cont = new Grid { Width = 66 };
            cont.ColumnDefinitions.Add(new ColumnDefinition()); cont.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cont.RowDefinitions.Add(new RowDefinition()); cont.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            cont.Children.Add(new TextBlock { Text = nombre, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 12, Foreground = blanco });
            var tp = new TextBlock { Text = p + "%", FontSize = 10.5, Foreground = acento == SRojo ? Hex("#FCA5A5") : acento == SAmarillo ? Hex("#FCD34D") : tenueO, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tp, 1); cont.Children.Add(tp);
            var barra = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Bottom, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)), ClipToBounds = true };
            foreach (var (n, color) in new[] { (cr, SRojo), (cp, SAmarillo), (cl, SVerde) })
                barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, n), GridUnitType.Star) });
            barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, h - cr - cp - cl), GridUnitType.Star) });
            var k = 0;
            foreach (var color in new[] { SRojo, SAmarillo, SVerde }) { var r = new System.Windows.Shapes.Rectangle { Fill = color }; Grid.SetColumn(r, k++); barra.Children.Add(r); }
            Grid.SetRow(barra, 1); Grid.SetColumnSpan(barra, 2); cont.Children.Add(barra);
            var ficha = new Border
            {
                Child = cont, Padding = new Thickness(7, 5, 7, 6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(5), Cursor = System.Windows.Input.Cursors.Hand,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(13, 255, 255, 255)),
                BorderBrush = acento ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(26, 255, 255, 255)), BorderThickness = new Thickness(1),
                ToolTip = $"Cara {nombre} · {p}% ocupado\n{cl} libres · {cp} con lugar · {cr} llenas · {ci} inhabilitadas\nClic: abrir el mapa del pasillo",
            };
            if (acento == SRojo) ficha.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = SRojo.Color, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.5 };
            ficha.MouseEnter += (_, _) => ficha.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(32, 255, 255, 255));
            ficha.MouseLeave += (_, _) => ficha.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(13, 255, 255, 255));
            ficha.MouseLeftButtonUp += (_, _) => C.Abrir("ZMAP", new() { ["cara"] = nombre });
            fichas.Children.Add(ficha);
        }
        var desp = new ScrollViewer { Content = fichas, MaxHeight = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(desp, 2); g.Children.Add(desp);
        _ocup.Child = g;
        _ocup.Visibility = Visibility.Visible;
    }
}

/// <summary>ZMOV – Movimientos con filtros por operario, fechas, tipo, estado, empresa y texto.</summary>
public sealed class MovimientosPantalla : Pantalla
{
    public override string Codigo => "ZMOV";
    public override string Titulo => "Movimientos";
    protected override string[] Eventos => new[] { "movimiento", "autorizacion" };
    static readonly Col[] Cols =
    {
        new("Fecha y hora", "hora_servidor", "fecha"), new("Operario", "usuario"), new("Tipo", "tipo", "etiqueta"), new("Contenedor", "contenedor", "mono"),
        new("Artículo", "articulo", "mono"), new("Descripción", "descripcion", Ancho: 220), new("Empresa", "empresa_nombre"), new("Cant.", "cantidad", "numero"),
        new("Origen", "origen", "mono"), new("Destino", "destino", "mono"), new("Estado nuevo", "estado_nuevo"), new("Estado", "estado", "estado"),
    };
    readonly DataGrid _g = Grilla(Cols);
    readonly ComboBox _usuario = Combo(new[] { ("", "Todos") });
    readonly DatePicker _desde = new() { SelectedDate = DateTime.Today }, _hasta = new();
    readonly ComboBox _tipo = Combo(new[] { ("", "Todos"), ("transferencia", "Transferencia"), ("guardado", "Guardado"), ("cambio_estado", "Cambio de estado"), ("reubicar_cajas", "Reubicar cajas"), ("ajuste", "Ajuste / eliminación"), ("alta", "Crear contenedor"), ("picking", "Picking"), ("medicion", "Medición de bultos"), ("control_pedido", "Control de pedido"), ("anulacion", "Anulación") });
    readonly ComboBox _estado = Combo(new[] { ("", "Todos"), ("pendiente_wms", "Pendiente WMS"), ("aplicado", "Aplicado"), ("error", "Error"), ("esperando_autorizacion", "Esperando autorización"), ("rechazado", "Rechazado"), ("anulado", "Anulado") });
    readonly ComboBox _empresa = Combo(new[] { ("", "Todas") });
    readonly TextBox _texto = Caja("", true);
    readonly TextBlock _total = T("", 12.5);
    readonly StackPanel _detalle = new();
    readonly Border _marcoDet;

    public MovimientosPantalla() { _marcoDet = Marco(Desplazable(_detalle), FilaPar); _marcoDet.Width = 380; _marcoDet.Margin = new Thickness(10, 0, 0, 0); _marcoDet.Visibility = Visibility.Collapsed; }

    string Qs()
    {
        var p = new List<string>();
        void A(string k, string? v) { if (!string.IsNullOrEmpty(v)) p.Add(k + "=" + Uri.EscapeDataString(v)); }
        A("usuario", Valor(_usuario)); A("desde", _desde.SelectedDate?.ToString("yyyy-MM-dd")); A("hasta", _hasta.SelectedDate?.ToString("yyyy-MM-dd"));
        A("tipo", Valor(_tipo)); A("estado", Valor(_estado)); A("empresa", Valor(_empresa)); A("texto", _texto.Text.Trim());
        return string.Join("&", p);
    }

    protected override void Armar()
    {
        if (Parametros.TryGetValue("estado", out var es)) { _estado.SelectedValue = es; _desde.SelectedDate = null; }
        if (Parametros.TryGetValue("texto", out var tx)) { _texto.Text = tx; _desde.SelectedDate = null; }
        var f = Filtros();
        f.Children.Add(Campo("Operario", _usuario, 180)); f.Children.Add(Campo("Desde", _desde, 130)); f.Children.Add(Campo("Hasta", _hasta, 130));
        f.Children.Add(Campo("Tipo", _tipo, 140)); f.Children.Add(Campo("Estado", _estado, 160)); f.Children.Add(Campo("Empresa", _empresa, 200));
        f.Children.Add(Campo("Contenedor / artículo / posición", _texto, 200));
        var bs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
        bs.Children.Add(Boton("Ejecutar (F8)", async () => await CargarSeguro(), true));
        bs.Children.Add(Boton("Hoy", async () => { _desde.SelectedDate = DateTime.Today; _hasta.SelectedDate = null; await CargarSeguro(); }));
        bs.Children.Add(Boton("7 días", async () => { _desde.SelectedDate = DateTime.Today.AddDays(-7); _hasta.SelectedDate = null; await CargarSeguro(); }));
        bs.Children.Add(Boton("Limpiar", async () => { _usuario.SelectedIndex = 0; _tipo.SelectedIndex = 0; _estado.SelectedIndex = 0; _empresa.SelectedIndex = 0; _texto.Text = ""; _desde.SelectedDate = null; _hasta.SelectedDate = null; await CargarSeguro(); }));
        f.Children.Add(bs);
        var marcoF = Marco(f, FilaPar, Linea2); Arriba(marcoF);
        var acc = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var der = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(der, Dock.Right);
        der.Children.Add(Boton("↻", async () => await CargarSeguro()));
        der.Children.Add(Boton("⬇ Excel", async () => await Exportar(), fondo: Marino));
        acc.Children.Add(der); acc.Children.Add(_total);
        Arriba(acc);
        PreviewKeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.F8) await CargarSeguro(); };
        _texto.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await CargarSeguro(); };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(_g); Grid.SetColumn(_marcoDet, 1); g.Children.Add(_marcoDet);
        _g.SelectionChanged += (_, _) => MostrarDetalle(Seleccion(_g));
        Centro(g);
        _ = CargarCombos();
    }

    async Task CargarCombos()
    {
        try
        {
            var u = await C.Api.Get("/api/usuarios");
            var elegido = Parametros.GetValueOrDefault("usuario");
            _usuario.Items.Clear(); _usuario.Items.Add(Tuple.Create("", "Todos"));
            foreach (var x in u!.AsArray()) _usuario.Items.Add(Tuple.Create(Str(x!["usuario"]), Str(x["nombre"]) is { Length: > 0 } n ? $"{n} ({Str(x["usuario"])})" : Str(x["usuario"])));
            _usuario.SelectedValue = elegido ?? ""; if (_usuario.SelectedIndex < 0) _usuario.SelectedIndex = 0;
            var e = await C.Api.Get("/api/empresas");
            foreach (var x in e!.AsArray()) _empresa.Items.Add(Tuple.Create(Str(x!["codigo"]), Str(x["nombre"])));
            if (elegido != null) await CargarSeguro();
        }
        catch { }
    }

    protected override async Task Cargar()
    {
        var d = await C.Api.Get("/api/movimientos?" + Qs() + "&limite=3000");
        var filas = d?["filas"]?.AsArray();
        _g.ItemsSource = Tabla(filas, Cols).DefaultView;
        _total.Text = $"{Str(d?["total"])} movimientos" + ((filas?.Count ?? 0) < (d?["total"]?.GetValue<long>() ?? 0) ? $" (se muestran {filas!.Count})" : "");
    }

    async Task Exportar()
    {
        var dlg = new SaveFileDialog { FileName = $"movimientos_{DateTime.Now:yyyy-MM-dd}.xlsx", Filter = "Excel|*.xlsx" };
        if (dlg.ShowDialog() != true) return;
        await Hacer(async () => { await File.WriteAllBytesAsync(dlg.FileName, await C.Api.Bytes("/api/movimientos/export.xlsx?" + Qs())); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); }, "Excel exportado");
    }

    void MostrarDetalle(JsonObject? m)
    {
        _detalle.Children.Clear();
        if (m == null) { _marcoDet.Visibility = Visibility.Collapsed; return; }
        _marcoDet.Visibility = Visibility.Visible;
        _detalle.Children.Add(T($"{Et(Str(m["tipo"]))} · {Str(m["contenedor"])}", 14, true, Marino, new Thickness(0, 0, 0, 8)));
        _detalle.Children.Add(Chip(Str(m["estado"])));
        void F(string et, string? v) { if (string.IsNullOrEmpty(v)) return; var g = new Grid { Margin = new Thickness(0, 3, 0, 0) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) }); g.ColumnDefinitions.Add(new ColumnDefinition()); var a = T(et, 12, color: Tenue); var b = T(v, 12); Grid.SetColumn(b, 1); g.Children.Add(a); g.Children.Add(b); _detalle.Children.Add(g); }
        F("Operario", Str(m["usuario_nombre"]) is { Length: > 0 } un ? un : Str(m["usuario"]));
        F("Hora servidor", FechaHora(Str(m["hora_servidor"]))); F("Hora celular", FechaHora(Str(m["hora_celular"])));
        F("Artículo", $"{Str(m["articulo"])} {Str(m["descripcion"])}"); F("Empresa", Str(m["empresa_nombre"]));
        F("Cantidad", Str(m["cantidad"])); F("Origen", Str(m["origen"]));
        F("Destino", Str(m["destino"]) + (Str(m["destino_ubic"]) is { Length: > 0 } du ? $" (Ubic.Ctrl {du})" : "") + (Str(m["nivel"]) is { Length: > 0 } nv ? $" nivel {nv}" : ""));
        F("Estado nuevo", Str(m["estado_nuevo"])); F("Sugerida WMS", Str(m["detalle"]?["sugerida_wms"]));
        F("Respuesta WMS", Str(m["respuesta_wms"]) is { Length: > 0 } rw ? rw : "—"); F("Intentos", Str(m["intentos"])); F("Aplicado", FechaHora(Str(m["aplicado_en"])));
        F("Comentario", Str(m["comentario"])); F("Dispositivo", Str(m["dispositivo"])); F("ID", Str(m["id"]));
        var fotos = m["fotos"]?.AsArray();
        if (fotos is { Count: > 0 })
        {
            _detalle.Children.Add(T("Fotos", 13, true, Marino, new Thickness(0, 10, 0, 4)));
            var w = new WrapPanel();
            foreach (var f in fotos) w.Children.Add(Miniatura(C.Api, "/api/fotos/" + Str(f)));
            _detalle.Children.Add(w);
        }
        var acc = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var est = Str(m["estado"]);
        if (est == "error") acc.Children.Add(Boton("↻ Reintentar en WMS", async () => await Hacer(async () => { await C.Api.Post($"/api/movimientos/{Str(m["id"])}/reintentar"); await CargarSeguro(); }, "Se reintenta cargar en el WMS"), fondo: Marino));
        if (est is "pendiente_wms" or "error" or "esperando_autorizacion" or "aplicado" && Str(m["tipo"]) != "anulacion")
            acc.Children.Add(Boton("Anular…", async () =>
            {
                var motivo = Pedir.Texto("Anular movimiento", est == "aplicado" ? "Ya está cargado en el WMS: se registra la anulación y hay que mover el contenedor de vuelta.\nMotivo:" : "Motivo de la anulación:");
                if (string.IsNullOrWhiteSpace(motivo)) return;
                await Hacer(async () => { var r = await C.Api.Post($"/api/movimientos/{Str(m["id"])}/anular", new { motivo }); C.Mensaje(Str(r?["mensaje"]), false); await CargarSeguro(); });
            }, fondo: Error));
        _detalle.Children.Add(acc);
    }

    /// <summary>Miniatura de una foto (se abre en grande con un clic).</summary>
    public static FrameworkElement Miniatura(ApiCliente api, string url, double tam = 100)
    {
        var img = new Image { Width = tam, Height = tam, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 6, 6), Cursor = System.Windows.Input.Cursors.Hand };
        _ = Task.Run(async () =>
        {
            try
            {
                var b = await api.Bytes(url);
                img.Dispatcher.Invoke(() => { var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(b); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); img.Source = bi; });
            }
            catch { }
        });
        img.MouseLeftButtonUp += (_, _) =>
        {
            if (img.Source == null) return;
            var w = new Window { Title = "Foto", Width = 900, Height = 700, Background = Brushes.Black, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = new Image { Source = img.Source, Stretch = Stretch.Uniform } };
            w.Show();
        };
        return new Border { Child = img, BorderBrush = Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 6) };
    }
}

/// <summary>ZAUT – Autorizaciones (destino en zona de otro cliente): Sí / No; el celular recibe la respuesta al instante.</summary>
public sealed class Autorizaciones : Pantalla
{
    public override string Codigo => "ZAUT";
    public override string Titulo => "Autorizaciones";
    protected override string[] Eventos => new[] { "autorizacion", "movimiento" };
    readonly StackPanel _lista = new();
    bool _todas;

    protected override void Armar()
    {
        Arriba(Marco(T("Cuando un operario quiere dejar material en una zona asignada a otro cliente (Layout Objetivo), el celular espera la respuesta de esta pantalla. Al autorizar, el movimiento se carga en el WMS.", 12.5, color: Info), InfoClaro, InfoClaro));
        var acc = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        acc.Children.Add(Boton("Pendientes", async () => { _todas = false; await CargarSeguro(); }, fondo: Marino));
        acc.Children.Add(Boton("Todas", async () => { _todas = true; await CargarSeguro(); }));
        Arriba(acc);
        Centro(Desplazable(_lista));
    }

    protected override async Task Cargar()
    {
        var d = await C.Api.Get("/api/autorizaciones" + (_todas ? "" : "?estado=pendiente"));
        _lista.Children.Clear();
        var arr = d?.AsArray() ?? new JsonArray();
        if (arr.Count == 0) _lista.Children.Add(T(_todas ? "Sin autorizaciones" : "No hay pedidos pendientes 👍", 14, color: Tenue, margen: new Thickness(10)));
        foreach (var a in arr.OfType<JsonObject>())
        {
            var pend = Str(a["estado"]) == "pendiente";
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var txt = new StackPanel();
            var cab = new StackPanel { Orientation = Orientation.Horizontal };
            cab.Children.Add(T(Str(a["solicitado_por"]), 13, true, Marino)); cab.Children.Add(T("  ·  " + FechaHora(Str(a["creado"])) + "  ", 12, color: Tenue)); cab.Children.Add(Chip(Str(a["estado"])));
            if (Str(a["resuelto_por"]) is { Length: > 0 } rp) cab.Children.Add(T($"  por {rp} {FechaHora(Str(a["resuelto_en"]))}", 12, color: Tenue));
            txt.Children.Add(cab);
            txt.Children.Add(T(Str(a["motivo"]), 14.5, margen: new Thickness(0, 4, 0, 4)));
            var movs = a["movimientos"]?.AsArray().OfType<JsonObject>().Select(m => $"{Str(m["contenedor"])} ({Str(m["articulo"])}) {Str(m["origen"])} → {Str(m["destino"])}");
            if (movs != null) { var tm = T(string.Join("   ·   ", movs), 12, color: Tenue); tm.FontFamily = new FontFamily("Consolas"); txt.Children.Add(tm); }
            g.Children.Add(txt);
            if (pend)
            {
                var bs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var nota = Caja(); nota.Width = 180; nota.Height = 34; nota.Margin = new Thickness(0, 0, 8, 0); nota.ToolTip = "Nota (opcional)";
                bs.Children.Add(nota);
                var id = Str(a["id"]);
                var si = Boton("✔  Sí, autorizar", async () => await Hacer(async () => { await C.Api.Post($"/api/autorizaciones/{id}", new { aprobar = true, nota = nota.Text }); await CargarSeguro(); }, "Autorizado"), fondo: Ok);
                var no = Boton("✕  No", async () => await Hacer(async () => { await C.Api.Post($"/api/autorizaciones/{id}", new { aprobar = false, nota = nota.Text }); await CargarSeguro(); }, "Rechazado"), fondo: Error);
                si.Height = 38; no.Height = 38; si.Padding = new Thickness(20, 0, 20, 0); no.Padding = new Thickness(20, 0, 20, 0);
                bs.Children.Add(si); bs.Children.Add(no);
                Grid.SetColumn(bs, 1); g.Children.Add(bs);
            }
            _lista.Children.Add(Marco(g, pend ? AvisoClaro : Blanco, pend ? Hex("#F0DCA6") : Linea));
        }
    }
}

/// <summary>Pedido de un texto simple (motivo, nombre…).</summary>
public static class Pedir
{
    public static string? Texto(string titulo, string pregunta, string inicial = "")
    {
        var w = new Window { Title = titulo, Width = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Owner = Application.Current.MainWindow };
        var s = new StackPanel { Margin = new Thickness(16) };
        s.Children.Add(T(pregunta, 13, margen: new Thickness(0, 0, 0, 8)));
        var t = new TextBox { Text = inicial, Height = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
        s.Children.Add(t);
        string? res = null;
        var bs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        bs.Children.Add(Boton("Cancelar", () => w.Close()));
        bs.Children.Add(Boton("Aceptar", () => { res = t.Text; w.Close(); }, true));
        s.Children.Add(bs);
        w.Content = s;
        w.Loaded += (_, _) => t.Focus();
        w.ShowDialog();
        return res;
    }
}
