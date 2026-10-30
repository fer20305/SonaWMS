using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZUSR – Usuarios (los del WMS) y su rol en SONA Depósito.</summary>
public sealed class Usuarios : Pantalla
{
    public override string Codigo => "ZUSR";
    public override string Titulo => "Usuarios, perfiles y permisos";
    static readonly Col[] Cols = { new("Usuario", "usuario", "mono"), new("Nombre", "nombre", Ancho: 200), new("Rol", "rol", "etiqueta"), new("Activo", "activo"), new("Puede operar WMS", "clave_guardada"), new("Último ingreso", "ultimo_acceso", "fecha"), new("Último contacto celular", "ultimo_contacto", "fecha") };
    readonly DataGrid _g = Grilla(Cols);

    protected override void Armar()
    {
        Arriba(Marco(T("Usuario y clave son los del WMS. Cada usuario tiene uno o más perfiles (Maquinista, Picker, Analista… o los que armes en \"Perfiles\") " +
                       "y, si hace falta, permisos agregados o quitados solo para él. En la app, el panel y los paneles web cada uno ve solo lo que tiene permitido.", 12.5, color: Info), InfoClaro, InfoClaro));
        var acc = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var admin = C.Api.Rol == "admin";
        acc.Children.Add(Boton("＋ Dar de alta", async () => { if (await Editar(null)) await CargarSeguro(); }, true));
        acc.Children.Add(Boton("✎ Datos", async () => { if (Seleccion(_g) is { } u && await Editar(u)) await CargarSeguro(); }));
        acc.Children.Add(Boton("🔐 Permisos", async () => { if (Seleccion(_g) is { } u && await PermisosUsuario.Editar(C, Str(u["usuario"]), Str(u["nombre"]))) await CargarSeguro(); }, fondo: Marino));
        acc.Children.Add(Boton("Perfiles…", async () => { await PermisosUsuario.Perfiles(C); await CargarSeguro(); }));
        foreach (var b in acc.Children.OfType<Button>()) b.IsEnabled = admin;
        Arriba(acc);
        _g.MouseDoubleClick += async (_, _) => { if (admin && Seleccion(_g) is { } u && await PermisosUsuario.Editar(C, Str(u["usuario"]), Str(u["nombre"]))) await CargarSeguro(); };
        Centro(_g);
    }

    protected override async Task Cargar() => _g.ItemsSource = Tabla((await C.Api.Get("/api/usuarios"))?.AsArray(), Cols, "usuario").DefaultView;

    async Task<bool> Editar(JsonObject? u)
    {
        var w = new Window { Title = u == null ? "Alta de usuario" : "Usuario " + Str(u["usuario"]), Width = 500, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow, ResizeMode = ResizeMode.NoResize };
        var s = new WrapPanel { Margin = new Thickness(16) };
        var usuario = Caja(Str(u?["usuario"]), true); usuario.IsEnabled = u == null;
        var nombre = Caja(Str(u?["nombre"]));
        var rol = Combo(new[] { "maquinista", "conteo", "picker", "supervisor", "produccion", "analista", "admin" }.Select(x => (x, Et(x))), Str(u?["rol"]) is { Length: > 0 } r ? r : "maquinista");
        var activo = Combo(new[] { ("si", "Activo"), ("no", "Inactivo") }, u?["activo"]?.GetValue<bool>() == false ? "no" : "si");
        s.Children.Add(Campo("Usuario del WMS", usuario, 200)); s.Children.Add(Campo("Nombre y apellido", nombre, 210));
        s.Children.Add(Campo("Rol", rol, 200)); s.Children.Add(Campo("Estado", activo, 210));
        var ok = false;
        var bs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        bs.Children.Add(Boton("Cancelar", () => w.Close()));
        bs.Children.Add(Boton("Guardar", async () =>
        {
            if (usuario.Text.Trim().Length == 0) return;
            try { await C.Api.Put($"/api/usuarios/{Uri.EscapeDataString(usuario.Text.Trim())}", new { nombre = nombre.Text.Trim(), rol = Valor(rol), activo = Valor(activo) == "si" }); C.Mensaje("Usuario guardado", false); ok = true; w.Close(); }
            catch (Exception e) { MessageBox.Show(e.Message); }
        }, true));
        s.Children.Add(bs);
        w.Content = s;
        w.ShowDialog();
        return ok;
    }
}

/// <summary>Editores de permisos (ZUSR): perfiles de un usuario + excepciones, y los perfiles con sus permisos.</summary>
static class PermisosUsuario
{
    static Window Ventana(string titulo, double ancho = 900) => new()
    {
        Title = titulo, Width = ancho, Height = 680, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow,
    };

    static IEnumerable<IGrouping<string, JsonObject>> Grupos(JsonNode? datos) => (datos?["catalogo"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>()).GroupBy(p => Str(p["grupo"]));
    static List<string> Lista(JsonNode? n) => n?.AsArray().Select(x => Str(x)).Where(x => x.Length > 0).ToList() ?? new();

    static WrapPanel Rejilla() => new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
    static CheckBox Tilde(string texto, bool marcado) => new() { Content = texto, IsChecked = marcado, Width = 270, Margin = new Thickness(0, 2, 0, 2) };

    public static async Task<bool> Editar(Contexto c, string usuario, string nombre)
    {
        JsonNode? datos, actual;
        try { datos = await c.Api.Get("/api/permisos"); actual = await c.Api.Get($"/api/usuarios/{Uri.EscapeDataString(usuario)}/permisos"); }
        catch (Exception e) { MessageBox.Show(e.Message); return false; }
        var perfiles = Lista(actual?["perfiles"]).ToHashSet();
        var mas = Lista(actual?["mas"]).ToHashSet();
        var menos = Lista(actual?["menos"]).ToHashSet();
        var defPerfiles = datos?["perfiles"]?.AsArray().OfType<JsonObject>().ToList() ?? new();
        var w = Ventana($"Permisos de {(nombre.Length > 0 ? nombre : usuario)}");
        var s = new StackPanel { Margin = new Thickness(16) };
        var ok = false;
        var tildes = new Dictionary<string, CheckBox>();
        HashSet<string> DePerfiles() => defPerfiles.Where(p => perfiles.Contains(Str(p["codigo"]))).SelectMany(p => Lista(p["permisos"])).ToHashSet();
        var aviso = T("", 12.5, true, Info, new Thickness(0, 4, 0, 4));
        var refrescando = false;
        void Refrescar()
        {
            refrescando = true;
            var esAdmin = perfiles.Contains("admin"); var dp = DePerfiles();
            foreach (var (cod, cb) in tildes)
            {
                cb.IsChecked = esAdmin || ((dp.Contains(cod) || mas.Contains(cod)) && !menos.Contains(cod));
                cb.IsEnabled = !esAdmin;
                cb.Foreground = mas.Contains(cod) ? Ok : menos.Contains(cod) ? Error : Texto;
                cb.FontWeight = mas.Contains(cod) || menos.Contains(cod) ? FontWeights.SemiBold : FontWeights.Normal;
            }
            aviso.Text = esAdmin ? "Administrador: tiene todos los permisos (no se le pueden quitar)." : "Tildá o destildá para hacer una excepción solo para este usuario (verde = agregado, rojo = quitado).";
            refrescando = false;
        }
        s.Children.Add(T("Perfiles", 13.5, true, Marino, new Thickness(0, 0, 0, 4)));
        var rp = Rejilla();
        foreach (var p in defPerfiles)
        {
            var cod = Str(p["codigo"]);
            var cb = Tilde(Str(p["nombre"]) + (p["sistema"]?.GetValue<bool>() == true ? "" : " (propio)"), perfiles.Contains(cod));
            cb.ToolTip = Str(p["descripcion"]) is { Length: > 0 } d ? d : null;
            cb.Checked += (_, _) => { perfiles.Add(cod); Refrescar(); };
            cb.Unchecked += (_, _) => { perfiles.Remove(cod); Refrescar(); };
            rp.Children.Add(cb);
        }
        s.Children.Add(rp);
        s.Children.Add(aviso);
        foreach (var g in Grupos(datos))
        {
            s.Children.Add(T(g.Key == "Panel" ? "Panel (transacciones)" : g.Key, 13.5, true, Marino, new Thickness(0, 8, 0, 4)));
            var r = Rejilla();
            foreach (var p in g)
            {
                var cod = Str(p["codigo"]);
                var cb = Tilde(Str(p["nombre"]), false);
                RoutedEventHandler cambio = (_, _) =>
                {
                    if (refrescando) return;
                    var dp = DePerfiles(); var on = cb.IsChecked == true;
                    mas.Remove(cod); menos.Remove(cod);
                    if (on && !dp.Contains(cod)) mas.Add(cod);
                    if (!on && dp.Contains(cod)) menos.Add(cod);
                    Refrescar();
                };
                cb.Checked += cambio; cb.Unchecked += cambio;
                tildes[cod] = cb; r.Children.Add(cb);
            }
            s.Children.Add(r);
        }
        Refrescar();
        var pie = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };
        pie.Children.Add(Boton("Cancelar", () => w.Close()));
        pie.Children.Add(Boton("Guardar", async () =>
        {
            if (perfiles.Count == 0) { MessageBox.Show("Elegí al menos un perfil."); return; }
            try { await c.Api.Put($"/api/usuarios/{Uri.EscapeDataString(usuario)}/permisos", new { perfiles, mas, menos }); c.Mensaje($"Permisos de {usuario} guardados", false); ok = true; w.Close(); }
            catch (Exception e) { MessageBox.Show(e.Message); }
        }, true));
        var dock = new DockPanel(); DockPanel.SetDock(pie, Dock.Bottom); dock.Children.Add(pie); dock.Children.Add(Desplazable(s));
        w.Content = dock;
        w.ShowDialog();
        return ok;
    }

    public static async Task Perfiles(Contexto c)
    {
        JsonNode? datos;
        try { datos = await c.Api.Get("/api/permisos"); } catch (Exception e) { MessageBox.Show(e.Message); return; }
        var w = Ventana("Perfiles (conjuntos de permisos)", 980);
        var lista = new ListBox { Width = 250, Margin = new Thickness(0, 0, 12, 0) };
        var der = new StackPanel();
        var nombre = Caja(); var desc = Caja();
        var tildes = new Dictionary<string, CheckBox>();
        string? actual = null;
        void Llenar()
        {
            lista.Items.Clear();
            foreach (var p in datos?["perfiles"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                lista.Items.Add(new ListBoxItem { Content = Str(p["nombre"]) + (p["sistema"]?.GetValue<bool>() == true ? "" : "  (propio)"), Tag = p, Padding = new Thickness(6, 4, 6, 4) });
        }
        void Mostrar(JsonObject? p)
        {
            actual = p == null ? null : Str(p["codigo"]);
            nombre.Text = Str(p?["nombre"]); desc.Text = Str(p?["descripcion"]);
            var ps = Lista(p?["permisos"]).ToHashSet();
            var bloqueado = actual == "admin";
            foreach (var (cod, cb) in tildes) { cb.IsChecked = ps.Contains(cod); cb.IsEnabled = !bloqueado; }
            nombre.IsEnabled = desc.IsEnabled = !bloqueado;
        }
        der.Children.Add(Marco(T("Un perfil es un conjunto de permisos (como un grupo de Active Directory). Los de sistema son los roles de siempre; podés ajustarlos o crear los tuyos. Administrador tiene todo y no se modifica.", 12, color: Info), InfoClaro, InfoClaro));
        var fila = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        fila.Children.Add(Campo("Nombre", nombre, 220)); fila.Children.Add(Campo("Descripción", desc, 380));
        der.Children.Add(fila);
        foreach (var g in Grupos(datos))
        {
            var cab = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
            cab.Children.Add(T(g.Key == "Panel" ? "Panel (transacciones)" : g.Key, 13.5, true, Marino, new Thickness(0, 0, 10, 0)));
            var cods = g.Select(x => Str(x["codigo"])).ToList();
            var todos = Boton("todos", () => { foreach (var k in cods) if (tildes[k].IsEnabled) tildes[k].IsChecked = true; }); todos.Height = 22;
            var ninguno = Boton("ninguno", () => { foreach (var k in cods) if (tildes[k].IsEnabled) tildes[k].IsChecked = false; }); ninguno.Height = 22;
            cab.Children.Add(todos); cab.Children.Add(ninguno);
            der.Children.Add(cab);
            var r = Rejilla();
            foreach (var p in g) { var cb = Tilde(Str(p["nombre"]), false); cb.Width = 300; tildes[Str(p["codigo"])] = cb; r.Children.Add(cb); }
            der.Children.Add(r);
        }
        lista.SelectionChanged += (_, _) => Mostrar((lista.SelectedItem as ListBoxItem)?.Tag as JsonObject);
        var izq = new DockPanel();
        var bIzq = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        bIzq.Children.Add(Boton("＋ Nuevo", () => { lista.SelectedItem = null; Mostrar(null); nombre.Focus(); }));
        bIzq.Children.Add(Boton("Borrar", async () =>
        {
            if ((lista.SelectedItem as ListBoxItem)?.Tag is not JsonObject p) return;
            if (p["sistema"]?.GetValue<bool>() == true) { MessageBox.Show("Los perfiles de sistema no se borran (se pueden modificar)."); return; }
            if (MessageBox.Show($"¿Borrar el perfil {Str(p["nombre"])}?", "Perfiles", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try { datos = await c.Api.Delete($"/api/perfiles/{Uri.EscapeDataString(Str(p["codigo"]))}"); Llenar(); Mostrar(null); }
            catch (Exception e) { MessageBox.Show(e.Message); }
        }));
        DockPanel.SetDock(bIzq, Dock.Bottom); izq.Children.Add(bIzq); izq.Children.Add(lista);
        var pie = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        pie.Children.Add(Boton("Cerrar", () => w.Close()));
        pie.Children.Add(Boton("Guardar perfil", async () =>
        {
            if (actual == "admin") return;
            var nom = nombre.Text.Trim();
            if (nom.Length == 0) { MessageBox.Show("Poné un nombre."); return; }
            var cod = actual ?? System.Text.RegularExpressions.Regex.Replace(new string(nom.Normalize(System.Text.NormalizationForm.FormD)
                .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            try
            {
                datos = await c.Api.Put($"/api/perfiles/{Uri.EscapeDataString(cod)}", new { nombre = nom, descripcion = desc.Text.Trim(), permisos = tildes.Where(t => t.Value.IsChecked == true).Select(t => t.Key).ToList() });
                c.Mensaje($"Perfil {nom} guardado", false);
                Llenar();
                lista.SelectedItem = lista.Items.OfType<ListBoxItem>().FirstOrDefault(i => Str(((JsonObject)i.Tag)["codigo"]) == cod);
            }
            catch (Exception e) { MessageBox.Show(e.Message); }
        }, true));
        var cuerpo = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(izq, Dock.Left); cuerpo.Children.Add(izq);
        var derD = new DockPanel(); DockPanel.SetDock(pie, Dock.Bottom); derD.Children.Add(pie); derD.Children.Add(Desplazable(der));
        cuerpo.Children.Add(derD);
        w.Content = cuerpo;
        Llenar(); Mostrar(null);
        w.ShowDialog();
    }
}

/// <summary>ZPAR – Parámetros de las reglas.</summary>
public sealed class ParametrosPantalla : Pantalla
{
    public override string Codigo => "ZPAR";
    public override string Titulo => "Parámetros";
    static readonly Dictionary<string, string> Ayuda = new()
    {
        ["poca_cantidad_m3"] = "Hasta este volumen (m³) se considera «pocas cajas»: se permite dejarlo junto al mismo artículo o empresa aunque la posición sea monoproducto.",
        ["niveles_max"] = "Niveles que se recorren al consultar una posición sin nivel cuando no hay ficha.",
        ["empresas_guardado"] = "Empresas (código WMS) cuyos pendientes de guardado se muestran en la app. Ej: [6, 38]",
        ["niveles_rotacion_alta"] = "Niveles preferidos para artículos de mucha rotación (los de poca rotación van arriba).",
        ["dias_rotacion"] = "Días hacia atrás para medir la rotación de un artículo.",
        ["fotos_obligatorias"] = "Motivos de «No disponible» que exigen foto. Ej: [\"ROTURA\",\"NO CONFORME\"]",
    };
    readonly StackPanel _lista = new();

    protected override void Armar() => Centro(Desplazable(_lista));

    protected override async Task Cargar()
    {
        _lista.Children.Clear();
        foreach (var p in (await C.Api.Get("/api/parametros"))!.AsArray().OfType<JsonObject>())
        {
            var clave = Str(p["clave"]);
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var k = T(clave, 12.5, true); k.FontFamily = new System.Windows.Media.FontFamily("Consolas"); g.Children.Add(k);
            var v = Caja(p["valor"]?.ToJsonString() ?? ""); v.Height = 28; v.FontFamily = new System.Windows.Media.FontFamily("Consolas"); v.Margin = new Thickness(0, 0, 10, 0); v.IsEnabled = C.Api.Rol == "admin";
            Grid.SetColumn(v, 1); g.Children.Add(v);
            var a = T(Ayuda.GetValueOrDefault(clave, ""), 12, color: Tenue); Grid.SetColumn(a, 2); g.Children.Add(a);
            var b = Boton("Guardar", async () =>
            {
                JsonNode? valor;
                try { valor = JsonNode.Parse(v.Text); } catch { C.Mensaje("Valor inválido (número, texto entre comillas o lista)", true); return; }
                await Hacer(() => C.Api.Put($"/api/parametros/{clave}", new JsonObject { ["valor"] = valor }), "Parámetro guardado");
            }, true);
            b.IsEnabled = C.Api.Rol == "admin";
            Grid.SetColumn(b, 3); g.Children.Add(b);
            _lista.Children.Add(Marco(g));
        }
    }
}

/// <summary>ZEMP – Empresas del WMS y alias (Notro Bazar = Notro Bazar).</summary>
public sealed class Empresas : Pantalla
{
    public override string Codigo => "ZEMP";
    public override string Titulo => "Empresas";
    static readonly Col[] Cols = { new("Código WMS", "codigo", "numero"), new("Nombre", "nombre", Ancho: 320), new("Alias", "alias", Ancho: 360) };
    readonly DataGrid _g = Grilla(Cols);
    readonly TextBox _buscar = Caja();
    JsonArray? _todas;

    protected override void Armar()
    {
        Arriba(Marco(T("La empresa nunca se elige a mano: sale del cliente que informa el buscador para el contenedor o artículo. Los alias sirven para reconocer otros nombres (ej. Notro Bazar = Notro Bazar).", 12.5, color: Info), InfoClaro, InfoClaro));
        var f = Filtros(); f.Children.Add(Campo("Buscar", _buscar, 260)); Arriba(f);
        _buscar.TextChanged += (_, _) => Filtrar();
        _g.MouseDoubleClick += async (_, _) =>
        {
            if (Seleccion(_g) is not { } e || C.Api.Rol != "admin") return;
            var alias = Pedir.Texto("Empresa " + Str(e["codigo"]), $"Alias de «{Str(e["nombre"])}» separados por coma:", Str(e["alias"]));
            if (alias == null) return;
            await Hacer(async () => { await C.Api.Put($"/api/empresas/{Str(e["codigo"])}", new { nombre = Str(e["nombre"]), alias = alias.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList() }); await CargarSeguro(); }, "Empresa guardada");
        };
        Centro(_g);
    }

    void Filtrar()
    {
        var t = _buscar.Text.Trim().ToUpperInvariant();
        var arr = new JsonArray((_todas ?? new JsonArray()).Where(e => t.Length == 0 || $"{Str(e!["codigo"])} {Str(e["nombre"])} {Str(e["alias"])}".ToUpperInvariant().Contains(t)).Select(e => e!.DeepClone()).ToArray());
        _g.ItemsSource = Tabla(arr, Cols, "codigo").DefaultView;
    }

    protected override async Task Cargar() { _todas = (await C.Api.Get("/api/empresas"))?.AsArray(); Filtrar(); }
}

/// <summary>ZAUD – Auditoría de cambios de configuración.</summary>
public sealed class Auditoria : Pantalla
{
    public override string Codigo => "ZAUD";
    public override string Titulo => "Auditoría de cambios";
    static readonly Col[] Cols = { new("Fecha", "hora", "fecha"), new("Usuario", "usuario"), new("Qué", "tabla"), new("Clave", "clave", "mono"), new("Cambio", "cambio", "mono", 600) };
    readonly DataGrid _g = Grilla(Cols);
    protected override void Armar() => Centro(_g);
    protected override async Task Cargar() => _g.ItemsSource = Tabla((await C.Api.Get("/api/auditoria"))?.AsArray(), Cols).DefaultView;
}
