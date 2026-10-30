using System.ComponentModel;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Sona.Panel.Pantallas;
using WinForms = System.Windows.Forms;

namespace Sona.Panel;

/// <summary>Ventana principal estilo SAP: barra de menú, código de transacción, árbol de navegación, pestañas y barra de estado.</summary>
public sealed class VentanaPrincipal : Window
{
    sealed record Tx(string Codigo, string Nombre, string Grupo, Func<Pantalla> Crear, bool SoloIntegrado = false);

    static readonly Tx[] Transacciones =
    {
        // Monitoreo: cómo va el depósito
        new("ZMON", "Monitor del depósito", "Monitoreo", () => new MonitorPantalla()),
        new("ZMOV", "Movimientos", "Monitoreo", () => new MovimientosPantalla()),
        new("ZAUT", "Autorizaciones", "Monitoreo", () => new Autorizaciones()),
        new("ZPRO", "Productividad de operarios", "Monitoreo", () => new Productividad()),
        new("ZMSG", "Mensajes con operarios", "Monitoreo", () => new Chat()),
        // Movimientos: consultar y lo mismo que hace la app
        new("ZCON", "Consultar", "Movimientos", () => new Consultar()),
        new("ZTRF", "Transferir", "Movimientos", () => new Transferir()),
        new("ZGUA", "Guardar recepciones", "Movimientos", () => new Guardar()),
        new("ZCAJ", "Reubicar cajas", "Movimientos", () => new ReubicarCajas()),
        new("ZAJU", "Ajustar contenedor", "Movimientos", () => new Ajustar()),
        new("ZCES", "Cambiar estado", "Movimientos", () => new CambiarEstado()),
        new("ZALT", "Crear contenedor", "Movimientos", () => new CrearContenedor()),
        new("ZNN", "Enviar a NN (lote)", "Movimientos", () => new TransferirNn()),
        new("ZELI", "Eliminar contenedor", "Movimientos", () => new EliminarContenedor()),
        new("ZART", "Artículos por empresa", "Consultas", () => new ArticulosEmpresa()),
        new("ZCAT", "Catálogo de fotos", "Consultas", () => new Catalogo()),
        new("ZPOS", "Fichas de posiciones", "Ubicaciones", () => new Posiciones()),
        new("ZZON", "Zonas · Layout Objetivo", "Ubicaciones", () => new Zonas()),
        new("ZMAP", "Mapa de pasillo", "Ubicaciones", () => new Mapa()),
        new("ZCNT", "Tareas de conteo", "Inventario", () => new Conteos()),
        new("ZDIF", "Conteos y diferencias", "Inventario", () => new Diferencias()),
        new("ZUSR", "Usuarios, perfiles y permisos", "Administración", () => new Usuarios()),
        new("ZPAR", "Parámetros", "Administración", () => new ParametrosPantalla()),
        new("ZEMP", "Empresas", "Administración", () => new Empresas()),
        new("ZAUD", "Auditoría de cambios", "Administración", () => new Auditoria()),
        new("ZCFG", "Configuración de plataformas", "Sistema", () => new ConfigPlataformas()),
        new("ZEST", "Estado y conexiones", "Sistema", () => new Estado()),
        new("ZIA", "Reconocimiento por foto (IA)", "Sistema", () => new IaPantalla()),
        new("ZAPP", "App de los celulares (actualizar)", "Sistema", () => new AppCelulares()),
        new("ZIMP", "Impresoras de etiquetas de bultos", "Sistema", () => new ImpresorasEtiquetas()),
        new("ZLOG", "Registro del servidor", "Sistema", () => new Registro(), true),
        new("ZCRE", "Mis credenciales (LES, e-TMS…)", "Sistema", () => new MisCredencialesPantalla()),
    };
    /// <summary>Permiso de ZUSR que habilita cada transacción (las que no figuran usan su propio código).</summary>
    static readonly Dictionary<string, string> PermisoTx = new()
    {
        ["ZCON"] = "consultar", ["ZTRF"] = "transferir", ["ZGUA"] = "guardar", ["ZCAJ"] = "reubicar_cajas", ["ZAJU"] = "ajustar", ["ZCES"] = "cambiar_estado",
        ["ZALT"] = "crear_contenedor", ["ZNN"] = "enviar_nn", ["ZELI"] = "eliminar", ["ZAUT"] = "autorizar",
    };
    bool Habilitada(Tx t) => t.Codigo == "ZCRE" || ((!t.SoloIntegrado || _integrado) && _api.Puede(PermisoTx.GetValueOrDefault(t.Codigo, t.Codigo)));   // ZCRE: cada uno las suyas
    bool Habilitada(string codigo) => Transacciones.FirstOrDefault(x => x.Codigo == codigo) is { } t && Habilitada(t);

    static readonly string[] Grupos = { "Monitoreo", "Movimientos", "Consultas", "Ubicaciones", "Inventario", "Administración", "Sistema" };

    readonly ApiCliente _api;
    readonly PanelConfig _panel;
    readonly Contexto _ctx;
    readonly bool _integrado;
    readonly TabControl _tabs = new() { Background = Ui.Fondo, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
    readonly TreeView _arbol = new() { BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = Ui.Linea, Background = Ui.Blanco, Padding = new Thickness(0, 6, 0, 6) };
    readonly ComboBox _tx = new() { IsEditable = true, Width = 190, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock _sServidor = Ui.T("", 12, color: Brushes.White), _sPend = Ui.T("", 12, color: Brushes.White), _sSes = Ui.T("", 12, color: Brushes.White),
        _sCon = Ui.T("", 12, color: Brushes.White), _sMsg = Ui.T("", 12, true, Brushes.White), _sHora = Ui.T("", 12, color: Brushes.White);
    readonly Ellipse _vivo = new() { Width = 9, Height = 9, Fill = Brushes.Gray, Margin = new Thickness(0, 0, 6, 0) };
    readonly TextBlock _vivoT = Ui.T("Conectando…", 12, color: Ui.Tenue);
    readonly DispatcherTimer _reloj = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _estado = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer _msgFin = new() { Interval = TimeSpan.FromSeconds(8) };
    WinForms.NotifyIcon? _bandeja;
    bool _cerrarDeVerdad;
    int _pendAut;
    Button? _btnMsg;

    public VentanaPrincipal(ApiCliente api, PanelConfig panel, Configuracion? cfgLocal, LogMemoria log, bool integrado)
    {
        _api = api; _panel = panel; _integrado = integrado;
        _ctx = new Contexto
        {
            Api = api, Mensaje = Mensaje, Abrir = Abrir, Integrado = integrado, ConfigLocal = cfgLocal, Log = log, Panel = panel,
            Reiniciar = async () => await ((App)Application.Current).Reiniciar(),
        };
        Ui.AbrirContenedor = c => Abrir("ZCON", new Dictionary<string, string> { ["codigo"] = c });
        Title = "SONA Depósito – Panel de control";
        Width = 1400; Height = 860; MinWidth = 1000; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Fondo;
        FontFamily = new FontFamily("Segoe UI");
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/sona-icono.ico")); } catch { }

        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        Agregar(raiz, BarraTitulo(), 0);
        Agregar(raiz, BarraMenu(), 1);
        Agregar(raiz, BarraHerramientas(), 2);
        Agregar(raiz, Cuerpo(), 3);
        Agregar(raiz, BarraEstado(), 4);
        Content = raiz;

        _api.Evento += e => Dispatcher.BeginInvoke(() => AlEvento(e));
        _api.Conexion += c => Dispatcher.BeginInvoke(() => { _vivo.Fill = c ? Ui.Ok : Ui.Error; _vivoT.Text = c ? "En vivo" : "Reconectando…"; });
        _api.Conectar();
        _reloj.Tick += (_, _) => _sHora.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        _reloj.Start();
        _estado.Tick += async (_, _) => await LeerEstado();
        _estado.Start();
        _msgFin.Tick += (_, _) => { _msgFin.Stop(); _sMsg.Text = ""; };
        Loaded += async (_, _) =>
        {
            if (new[] { "ZMON", "ZCON" }.Concat(Transacciones.Select(x => x.Codigo)).FirstOrDefault(Habilitada) is { } ini) Abrir(ini, null);
            else Mensaje("Tu usuario no tiene transacciones habilitadas en el panel. Pedíselas a un administrador (ZUSR).", true);
            await LeerEstado();
        };
        PreviewKeyDown += Teclas;
        CrearBandeja();
    }

    static void Agregar(Grid g, UIElement e, int fila) { Grid.SetRow(e, fila); g.Children.Add(e); }

    // ------------------------------------------------------------------ barras
    UIElement BarraTitulo()
    {
        var d = new DockPanel { Background = Ui.Marino, LastChildFill = false };
        var izq = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        izq.Children.Add(new TextBlock { Text = "S", FontSize = 24, FontWeight = FontWeights.Black, Foreground = Ui.Naranja, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        izq.Children.Add(Ui.T("SONA", 15, true, Brushes.White));
        izq.Children.Add(Ui.T("  Depósito", 15, false, Brushes.White));
        izq.Children.Add(Ui.T("   Panel de control", 12, color: Ui.Hex("#9FB3C6")));
        izq.Children.Add(new Border { Child = Ui.T(_integrado ? "SERVIDOR INTEGRADO" : "REMOTO · " + _api.Base, 10.5, true, Ui.Marino), Background = Ui.Naranja, CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(izq, Dock.Left);
        d.Children.Add(izq);
        var der = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        der.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Naranja, Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock { Text = _api.Usuario.Length >= 2 ? _api.Usuario[..2] : _api.Usuario, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        der.Children.Add(Ui.T($"{(_api.Nombre.Length > 0 ? _api.Nombre : _api.Usuario)} · {Ui.Et(_api.Rol)}", 12.5, color: Brushes.White, margen: new Thickness(0, 0, 12, 0)));
        var salir = new Button { Content = "Cerrar sesión", Foreground = Brushes.White, Background = Brushes.Transparent, BorderBrush = Ui.Hex("#4F6B85"), Padding = new Thickness(10, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
        salir.Click += async (_, _) => { _panel.Clave = ""; _panel.Guardar(); await ((App)Application.Current).Reiniciar(); };
        der.Children.Add(salir);
        DockPanel.SetDock(der, Dock.Right);
        d.Children.Add(der);
        return d;
    }

    UIElement BarraMenu()
    {
        var m = new Menu { Background = Ui.Hex("#F8FAFB"), Padding = new Thickness(4, 2, 4, 2), BorderBrush = Ui.Linea, BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 13 };
        foreach (var g in Grupos)
        {
            var mi = new MenuItem { Header = g, Padding = new Thickness(10, 4, 10, 4) };
            if (g != "Sistema" && !Transacciones.Any(t => t.Grupo == g && Habilitada(t))) continue;
            foreach (var t in Transacciones.Where(t => t.Grupo == g && Habilitada(t)))
            {
                var item = new MenuItem { Header = t.Nombre, InputGestureText = t.Codigo };
                item.Click += (_, _) => Abrir(t.Codigo, null);
                mi.Items.Add(item);
            }
            if (g == "Sistema")
            {
                mi.Items.Add(new Separator());
                var modo = new MenuItem { Header = "Servidor: integrado / Docker…" };
                modo.Click += (_, _) => { if (new VentanaModo(_panel) { Owner = this }.ShowDialog() == true) _ctx.Reiniciar(); };
                mi.Items.Add(modo);
                var prod = new MenuItem { Header = "Panel de producción (asignar pedidos)…" };
                prod.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_api.Base.TrimEnd('/') + "/produccion") { UseShellExecute = true });
                if (_api.Puede("produccion_ver")) mi.Items.Add(prod);
                var ing = new MenuItem { Header = "Módulo de ingreso (recepción LES + WMS)…" };
                ing.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_api.Base.TrimEnd('/') + "/ingresos" + (_api.Token.Length > 0 ? "#t=" + Uri.EscapeDataString(_api.Token) : "")) { UseShellExecute = true });
                if (_api.Puede("ingreso")) mi.Items.Add(ing);
                if (_integrado)
                {
                    var web = new MenuItem { Header = "Abrir panel web en el navegador" };
                    web.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_api.Base) { UseShellExecute = true });
                    mi.Items.Add(web);
                    var carpeta = new MenuItem { Header = "Abrir carpeta de datos" };
                    carpeta.Click += (_, _) => { if (_ctx.ConfigLocal != null) System.Diagnostics.Process.Start("explorer.exe", _ctx.ConfigLocal.Carpeta); };
                    mi.Items.Add(carpeta);
                }
                mi.Items.Add(new Separator());
                var salir = new MenuItem { Header = "Salir (detiene el servidor)" };
                salir.Click += (_, _) => Salir();
                mi.Items.Add(salir);
            }
            m.Items.Add(mi);
        }
        var ayuda = new MenuItem { Header = "Ayuda", Padding = new Thickness(10, 4, 10, 4) };
        var atajos = new MenuItem { Header = "Atajos de teclado" };
        atajos.Click += (_, _) => MessageBox.Show("Ctrl+K: código de transacción\nF3: cerrar pestaña\nF5: actualizar\n/N + código: abre en la misma pestaña\n/O + código: abre en pestaña nueva", "Atajos");
        ayuda.Items.Add(atajos);
        var acerca = new MenuItem { Header = "Acerca de SONA Depósito" };
        acerca.Click += (_, _) => MessageBox.Show($"SONA Depósito {Sona.Api.Rutas.Version}\nPanel de control y servidor (.NET 8 / WPF)", "SONA Depósito");
        ayuda.Items.Add(acerca);
        m.Items.Add(ayuda);
        return m;
    }

    UIElement BarraHerramientas()
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Background = Ui.Hex("#F3F6F8"), Margin = new Thickness(0) };
        var marco = new Border { BorderBrush = Ui.Linea, BorderThickness = new Thickness(0, 0, 0, 1), Child = s };
        s.Children.Add(new Border { Child = Ui.T("Transacción", 12, color: Ui.Tenue), Padding = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        foreach (var t in Transacciones.Where(Habilitada)) _tx.Items.Add($"{t.Codigo}  {t.Nombre}");
        _tx.Margin = new Thickness(0, 7, 4, 7);
        _tx.KeyDown += (_, e) => { if (e.Key == Key.Enter) { EjecutarTx(_tx.Text); e.Handled = true; } };
        _tx.SelectionChanged += (_, _) => { if (_tx.IsDropDownOpen && _tx.SelectedItem is string sel) EjecutarTx(sel); };
        s.Children.Add(_tx);
        s.Children.Add(Ui.Boton("✔", () => EjecutarTx(_tx.Text)));
        s.Children.Add(new Separator { Width = 1, Margin = new Thickness(4, 8, 8, 8), Background = Ui.Linea });
        foreach (var (c, t) in new[] { ("ZMON", "📊 Monitor"), ("ZMOV", "⇄ Movimientos"), ("ZAUT", "✋ Autorizaciones"), ("ZMSG", "💬 Mensajes"), ("ZCON", "🔎 Consultar"), ("ZCFG", "⚙ Configuración") })
        {
            if (!Habilitada(c)) continue;
            var b = Ui.Boton(t, () => Abrir(c, null));
            if (c == "ZMSG") _btnMsg = b;
            b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent; b.Margin = new Thickness(0, 6, 2, 6);
            s.Children.Add(b);
        }
        var vivo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        vivo.Children.Add(_vivo); vivo.Children.Add(_vivoT);
        s.Children.Add(vivo);
        return marco;
    }

    UIElement Cuerpo()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var grupo in Grupos)
        {
            if (!Transacciones.Any(t => t.Grupo == grupo && Habilitada(t))) continue;
            var nodo = new TreeViewItem { Header = Ui.T(grupo, 13, true, Ui.Marino), IsExpanded = true, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var t in Transacciones.Where(t => t.Grupo == grupo && Habilitada(t)))
            {
                var fila = new DockPanel { Width = 190, LastChildFill = true };
                var cod = Ui.T(t.Codigo, 10.5, color: Ui.Tenue); DockPanel.SetDock(cod, Dock.Right);
                fila.Children.Add(cod); fila.Children.Add(Ui.T(t.Nombre, 12.5));
                var hoja = new TreeViewItem { Header = fila, Tag = t.Codigo, Padding = new Thickness(2, 3, 2, 3) };
                hoja.MouseLeftButtonUp += (_, e) => { Abrir(t.Codigo, null); e.Handled = true; };
                hoja.KeyDown += (_, e) => { if (e.Key == Key.Enter) Abrir(t.Codigo, null); };
                nodo.Items.Add(hoja);
            }
            _arbol.Items.Add(nodo);
        }
        Grid.SetColumn(_arbol, 0); g.Children.Add(_arbol);
        var sp = new GridSplitter { Width = 4, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Ui.Fondo };
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        Grid.SetColumn(_tabs, 2); g.Children.Add(_tabs);
        return g;
    }

    UIElement BarraEstado()
    {
        var s = new DockPanel { Background = Ui.Marino, LastChildFill = true };
        var izq = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        foreach (var t in new[] { _sServidor, _sPend, _sSes, _sCon }) { t.Margin = new Thickness(0, 0, 20, 0); izq.Children.Add(t); }
        DockPanel.SetDock(izq, Dock.Left); s.Children.Add(izq);
        _sHora.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(_sHora, Dock.Right); s.Children.Add(_sHora);
        s.Children.Add(_sMsg);
        return s;
    }

    // ------------------------------------------------------------------ transacciones
    void EjecutarTx(string texto)
    {
        var t = texto.Trim().ToUpperInvariant();
        var nueva = t.StartsWith("/O");
        if (t.StartsWith("/N") || t.StartsWith("/O")) t = t[2..].Trim();
        var cod = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        _tx.Text = "";
        if (Transacciones.All(x => x.Codigo != cod)) { Mensaje($"La transacción {cod} no existe", true); return; }
        Abrir(cod, nueva ? new Dictionary<string, string> { ["__nueva"] = "1" } : null);
    }

    public void Abrir(string codigo, Dictionary<string, string>? parametros)
    {
        var t = Transacciones.FirstOrDefault(x => x.Codigo == codigo);
        if (t == null) return;
        if (!Habilitada(t)) { Mensaje($"No tenés permiso para {t.Codigo} · {t.Nombre}. Pedíselo a un administrador (ZUSR).", true); return; }
        var nueva = parametros?.ContainsKey("__nueva") == true;
        var existente = _tabs.Items.OfType<TabItem>().FirstOrDefault(i => ((Pantalla)i.Content).Codigo == codigo);
        if (existente != null && !nueva && (parametros == null || parametros.Count == 0)) { _tabs.SelectedItem = existente; return; }
        var p = t.Crear();
        p.Iniciar(_ctx, parametros);
        var cab = new StackPanel { Orientation = Orientation.Horizontal };
        cab.Children.Add(Ui.T(t.Nombre, 12.5));
        var x = new Button { Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(2, 0, 2, 0), Foreground = Ui.Tenue, Cursor = Cursors.Hand };
        var tab = new TabItem { Header = cab, Content = p, Padding = new Thickness(10, 5, 10, 5) };
        x.Click += (_, _) => _tabs.Items.Remove(tab);
        cab.Children.Add(x);
        if (existente != null && !nueva) { var i = _tabs.Items.IndexOf(existente); _tabs.Items.RemoveAt(i); _tabs.Items.Insert(i, tab); }
        else _tabs.Items.Add(tab);
        _tabs.SelectedItem = tab;
    }

    void Teclas(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { _tx.Focus(); e.Handled = true; }
        else if (e.Key == Key.F3 && _tabs.SelectedItem is TabItem t && _tabs.Items.Count > 1) { _tabs.Items.Remove(t); e.Handled = true; }
        else if (e.Key == Key.F5 && _tabs.SelectedItem is TabItem t2) { _ = ((Pantalla)t2.Content).CargarSeguro(); e.Handled = true; }
    }

    public void Mensaje(string texto, bool error)
    {
        _sMsg.Text = (error ? "⛔ " : "✔ ") + texto;
        _sMsg.Foreground = error ? Ui.Hex("#FFB3B3") : Ui.Hex("#9BE7B0");
        _msgFin.Stop(); _msgFin.Start();
        if (error) SystemSounds.Asterisk.Play();
    }

    async Task LeerEstado()
    {
        try
        {
            var e = await _api.Get("/api/estado");
            _sServidor.Text = $"● Servidor v{Ui.Str(e?["version"])}";
            _sPend.Text = $"Pendientes WMS: {Ui.Str(e?["pendientesWms"])}";
            _sSes.Text = $"Sesiones WMS: {e?["sesionesWms"]?.AsArray().Count ?? 0}";
            _sCon.Text = $"Conectados: {Ui.Str(e?["conectados"])}";
            var a = await _api.Get("/api/autorizaciones?estado=pendiente");
            _pendAut = a?.AsArray().Count ?? 0;
            if (_bandeja != null) _bandeja.Text = _pendAut > 0 ? $"SONA Depósito – {_pendAut} autorización(es) pendiente(s)" : "SONA Depósito";
            var ms = await _api.Get("/api/mensajes/sin-leer");
            var n = int.TryParse(Ui.Str(ms?["panel"]), out var x) ? x : 0;
            if (_btnMsg != null)
            {
                _btnMsg.Content = n > 0 ? $"💬 Mensajes ({n})" : "💬 Mensajes";
                _btnMsg.Foreground = n > 0 ? Ui.Naranja : Ui.Marino;
                _btnMsg.FontWeight = n > 0 ? FontWeights.Bold : FontWeights.Normal;
            }
        }
        catch { _sServidor.Text = "● Servidor sin conexión"; }
    }

    void AlEvento(JsonElement e)
    {
        foreach (var t in _tabs.Items.OfType<TabItem>()) ((Pantalla)t.Content).AlEvento(e);
        var tipo = e.TryGetProperty("tipo", out var tp) ? tp.GetString() : null;
        string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        if (tipo == "autorizacion" && S("estado") == "pendiente")
        {
            Mensaje($"Pedido de autorización de {S("usuario")}: {S("motivo")}", false);
            SystemSounds.Exclamation.Play();
            _bandeja?.ShowBalloonTip(8000, "Autorización pendiente", $"{S("usuario")}: {S("motivo")}", WinForms.ToolTipIcon.Warning);
            if (!IsVisible || WindowState == WindowState.Minimized) { Mostrar(); }
            Abrir("ZAUT", null);
            _ = LeerEstado();
        }
        else if (tipo == "movimiento" && S("estado") == "error") Mensaje($"Error WMS en {S("contenedor")}: {S("mensaje")}", true);
        else if (tipo == "mensaje")
        {
            foreach (var t in _tabs.Items.OfType<TabItem>()) if (t.Content is Chat ch) ch.AlMensaje(e);
            var desdePanel = e.TryGetProperty("desde_panel", out var dp) && dp.ValueKind == JsonValueKind.True;
            if (!desdePanel)
            {
                var quien = S("nombre").Length > 0 ? S("nombre") : S("de");
                Mensaje($"💬 {quien}: {S("texto")}", false);
                SystemSounds.Asterisk.Play();
                _bandeja?.ShowBalloonTip(6000, $"Mensaje de {quien}", S("texto"), WinForms.ToolTipIcon.Info);
            }
            _ = LeerEstado();
        }
        if (tipo is "movimiento" or "autorizacion") _ = LeerEstado();
    }

    // ------------------------------------------------------------------ bandeja del sistema
    void CrearBandeja()
    {
        System.Drawing.Icon? icono = null;
        try
        {
            var s = Application.GetResourceStream(new Uri("pack://application:,,,/sona-icono.ico"))?.Stream;
            if (s != null) icono = new System.Drawing.Icon(s);
        }
        catch { }
        _bandeja = new WinForms.NotifyIcon { Icon = icono ?? System.Drawing.SystemIcons.Application, Text = "SONA Depósito", Visible = true };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Abrir panel", null, (_, _) => Dispatcher.Invoke(Mostrar));
        menu.Items.Add("Monitor", null, (_, _) => Dispatcher.Invoke(() => { Mostrar(); Abrir("ZMON", null); }));
        menu.Items.Add("Autorizaciones", null, (_, _) => Dispatcher.Invoke(() => { Mostrar(); Abrir("ZAUT", null); }));
        menu.Items.Add("Mensajes", null, (_, _) => Dispatcher.Invoke(() => { Mostrar(); Abrir("ZMSG", null); }));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir (detiene el servidor)", null, (_, _) => Dispatcher.Invoke(Salir));
        _bandeja.ContextMenuStrip = menu;
        _bandeja.DoubleClick += (_, _) => Dispatcher.Invoke(Mostrar);
    }

    public void MostrarEnBandeja() { _bandeja?.ShowBalloonTip(3000, "SONA Depósito", _integrado ? "El servidor está atendiendo. Doble clic para abrir el panel." : "Panel conectado.", WinForms.ToolTipIcon.Info); }

    void Mostrar() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }

    void Salir()
    {
        if (_integrado && !Confirmar("Si salís, el servidor deja de atender a los celulares hasta que vuelvas a abrir SONA Depósito.\n\n¿Salir igual?")) return;
        CerrarDefinitivo();
        Application.Current.Shutdown();
    }

    static bool Confirmar(string t) => MessageBox.Show(t, "SONA Depósito", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public void CerrarDefinitivo()
    {
        _cerrarDeVerdad = true;
        _api.Desconectar();
        if (_bandeja != null) { _bandeja.Visible = false; _bandeja.Dispose(); _bandeja = null; }
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_cerrarDeVerdad && _integrado && _panel.MinimizarABandeja)
        {
            e.Cancel = true;
            Hide();
            _bandeja?.ShowBalloonTip(3000, "SONA Depósito sigue funcionando", "El servidor sigue atendiendo a los celulares. Para salir: clic derecho en este ícono › Salir.", WinForms.ToolTipIcon.Info);
            return;
        }
        if (!_cerrarDeVerdad)
        {
            if (_integrado && !Confirmar("Si cerrás, el servidor deja de atender a los celulares.\n\n¿Cerrar igual?")) { e.Cancel = true; return; }
            _cerrarDeVerdad = true;
            _api.Desconectar();
            if (_bandeja != null) { _bandeja.Visible = false; _bandeja.Dispose(); }
            Application.Current.Shutdown();
        }
        base.OnClosing(e);
    }
}
