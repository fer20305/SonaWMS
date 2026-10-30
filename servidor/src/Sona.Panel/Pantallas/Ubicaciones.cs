using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZCON – Consultar Ubic.Ctrl, posición, contenedor o artículo (como la opción 8 del handheld, con fotos).</summary>
public sealed class Consultar : Pantalla
{
    public override string Codigo => "ZCON";
    public override string Titulo => "Consultar";
    readonly TextBox _cod = Caja("", true);
    readonly TextBox _nivel = Caja();
    readonly StackPanel _res = new();
    // buscador "a medida que escribís" por código o descripción (como el filtro del LES)
    static readonly Col[] ColsSug = { new("Artículo", "articulo", "mono"), new("Descripción", "descripcion", Ancho: 360), new("Empresa", "empresaNombre"), new("Stock", "stock", "numero"), new("Cont.", "contenedores", "numero"), new("Origen", "origen") };
    readonly DataGrid _sug = Grilla(ColsSug);
    readonly TextBlock _sugT = T("", 12, color: Tenue);
    readonly System.Windows.Threading.DispatcherTimer _demoraSug = new() { Interval = TimeSpan.FromMilliseconds(350) };
    bool _elegido;
    int _pedido;

    protected override void Armar()
    {
        var f = Filtros();
        f.Children.Add(Campo("Ubic.Ctrl, posición (FI1502), contenedor, artículo o descripción", _cod, 300));
        f.Children.Add(Campo("Nivel (vacío = todos)", _nivel, 90));
        var b = Boton("Consultar", async () => await Buscar(), true); b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 0, 6);
        f.Children.Add(b);
        Arriba(Marco(f, FilaPar, Linea2));
        var sp = new StackPanel { Visibility = Visibility.Collapsed };
        sp.Children.Add(_sugT); _sug.MaxHeight = 260; sp.Children.Add(_sug);
        Arriba(sp);
        _cod.TextChanged += (_, _) => { if (_elegido) return; _demoraSug.Stop(); _demoraSug.Start(); };
        _demoraSug.Tick += async (_, _) => { _demoraSug.Stop(); await Sugerir(sp); };
        void Elegir() { if (Seleccion(_sug) is { } a) { _elegido = true; _cod.Text = Str(a["articulo"]); _elegido = false; sp.Visibility = Visibility.Collapsed; _ = Buscar(); } }
        _sug.MouseDoubleClick += (_, _) => Elegir();
        _sug.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Elegir(); } };
        _cod.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Down && sp.Visibility == Visibility.Visible && _sug.Items.Count > 0) { _sug.SelectedIndex = 0; _sug.Focus(); e.Handled = true; } };
        _cod.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { _demoraSug.Stop(); sp.Visibility = Visibility.Collapsed; await Buscar(); } };
        _nivel.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await Buscar(); };
        Centro(Desplazable(_res));
        if (Parametros.TryGetValue("codigo", out var c)) { _cod.Text = c; _ = Buscar(); }
    }

    protected override Task Cargar() => Task.CompletedTask;

    async Task Sugerir(StackPanel sp)
    {
        var q = _cod.Text.Trim();
        // no para Ubic.Ctrl (solo números) ni pedidos (10395-421)
        if (q.Length < 2 || System.Text.RegularExpressions.Regex.IsMatch(q, @"^\d+(-\d+)?$")) { sp.Visibility = Visibility.Collapsed; return; }
        var n = ++_pedido;
        _sugT.Text = "Buscando…"; sp.Visibility = Visibility.Visible;
        try
        {
            var r = await C.Api.Get("/api/articulos/sugerir?q=" + Uri.EscapeDataString(q));
            if (n != _pedido) return;   // ya escribió otra cosa
            var items = r?["items"]?.AsArray();
            _sug.ItemsSource = Tabla(items, ColsSug, "articulo").DefaultView;
            _sugT.Text = $"{items?.Count ?? 0} artículo(s) · doble clic o Enter para consultar" + (Str(r?["errorLes"]) is { Length: > 0 } el ? " · ⚠ " + el : "");
            sp.Visibility = (items?.Count ?? 0) > 0 || Str(r?["errorLes"]).Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e) { if (n == _pedido) _sugT.Text = e.Message; }
    }

    async Task Buscar()
    {
        if (string.IsNullOrWhiteSpace(_cod.Text)) return;
        _res.Children.Clear();
        _res.Children.Add(T("Consultando el WMS…", 13, color: Tenue));
        try
        {
            var r = await C.Api.Get($"/api/consulta/{Uri.EscapeDataString(_cod.Text.Trim())}" + (_nivel.Text.Trim().Length > 0 ? "?nivel=" + _nivel.Text.Trim() : ""));
            _res.Children.Clear();
            switch (Str(r?["tipo"]))
            {
                case "posicion": Posicion(r!); break;
                case "contenedor": Contenedor(r!["contenedor"]!.AsObject(), r["movimientos"]?.AsArray(), r["conteos"]?.AsArray(), Str(r["aviso"])); break;
                case "articulo": Articulo(r!["articulo"]!.AsObject()); break;
                case "descripcion":
                {
                    _res.Children.Add(T($"Artículos con \"{Str(r!["texto"])}\" en la descripción (doble clic para consultar)", 14, true, Marino, new Thickness(0, 0, 0, 6)));
                    var g = Grilla(ColsSug);
                    g.ItemsSource = Tabla(r["articulos"]?.AsArray(), ColsSug, "articulo").DefaultView;
                    g.MouseDoubleClick += async (_, _) => { if (Seleccion(g) is { } a) { _elegido = true; _cod.Text = Str(a["articulo"]); _elegido = false; await Buscar(); } };
                    _res.Children.Add(g);
                    break;
                }
                default: _res.Children.Add(Marco(T(Str(r?["mensaje"]), 13, color: Aviso), AvisoClaro, AvisoClaro)); break;
            }
        }
        catch (Exception e) { _res.Children.Clear(); _res.Children.Add(Marco(T(e.Message, 13, color: Error), ErrorClaro, ErrorClaro)); }
    }

    static readonly Col[] ColsCont = { new("Contenedor", "contenedor", "mono"), new("Artículo", "articulo", "mono"), new("Descripción", "descripcion", Ancho: 260), new("Empresa", "empresaNombre"), new("Cantidad", "cantidad", "numero"), new("m³", "m3", "numero") };

    void Posicion(JsonNode r)
    {
        var p = r["posicion"]!;
        _res.Children.Add(T($"Posición {Str(p["legible"])}" + (Str(p["ubicCtrl"]) is { Length: > 0 } u ? $"  ·  Ubic.Ctrl {u}" : ""), 15, true, Marino, new Thickness(0, 0, 0, 8)));
        foreach (var n in r["niveles"]!.AsArray().OfType<JsonObject>())
        {
            var s = new StackPanel();
            var cab = new StackPanel { Orientation = Orientation.Horizontal };
            var t = T(Str(n["codigo"]), 15, true, Marino); t.FontFamily = new FontFamily("Consolas"); cab.Children.Add(t);
            var libres = n["libres"]?.GetValue<int>() ?? 0;
            cab.Children.Add(T($"    {Str(n["ocupados"])}/{Str(n["capacidad"])} ocupados · {libres} libres", 13, true, libres == 0 ? Error : Ok));
            if (n["ficha"] is JsonObject fi) cab.Children.Add(T($"    {Et(Str(fi["tipo"]))} · {Et(Str(fi["rol_zona"]))} · {Et(Str(fi["mezcla"]))}{(fi["habilitada"]?.GetValue<bool>() == false ? " · INHABILITADA" : "")}", 12, color: Tenue));
            s.Children.Add(cab);
            var det = n["detalle"]!.AsArray();
            if (det.Count == 0) s.Children.Add(T("Vacía", 12, color: Tenue, margen: new Thickness(0, 6, 0, 0)));
            else
            {
                var g = Grilla(ColsCont); g.Margin = new Thickness(0, 8, 0, 0); g.MaxHeight = 260;
                g.ItemsSource = Tabla(det, ColsCont, "contenedor").DefaultView;
                g.MouseDoubleClick += (_, _) => { if (Seleccion(g) is { } c) { _cod.Text = Str(c["contenedor"]); _nivel.Text = ""; _ = Buscar(); } };
                s.Children.Add(g);
            }
            _res.Children.Add(Marco(s));
        }
    }

    void Contenedor(JsonObject c, JsonArray? movs, JsonArray? conteos, string aviso)
    {
        if (aviso.Length > 0) _res.Children.Add(Marco(T("⚠ " + aviso, 13, true, Aviso), AvisoClaro, AvisoClaro));
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        if (Str(c["foto"]) is { Length: > 0 } foto) g.Children.Add(MovimientosPantalla.Miniatura(C.Api, foto, 130));
        var s = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        var t = Seleccionable(Str(c["contenedor"]), 18, true, Marino); t.FontFamily = new FontFamily("Consolas"); s.Children.Add(t);
        s.Children.Add(Seleccionable($"{Str(c["articulo"])} · {Str(c["descripcion"])}", 14, true));
        s.Children.Add(T(Str(c["empresaNombre"]) is { Length: > 0 } en ? en : Str(c["cliente"]), 13, color: Tenue));
        if (c["articuloHabilitado"] is JsonValue av && av.TryGetValue<bool>(out var ah)) s.Children.Add(T(ah ? "✔ Artículo habilitado en LES" : "✖ Artículo deshabilitado en LES", 12.5, true, ah ? Ok : Error));
        s.Children.Add(T($"Cantidad {Str(c["cantidad"])}" + (Str(c["m3"]) is { Length: > 0 } m3 ? $" · {m3} m³" : "") + $" · Posición {Str(c["posicion"])}", 13, color: Tenue));
        foreach (var (k, v) in c["campos"]?.AsObject() ?? new JsonObject())
            if (k is not ("CLIENTE" or "CODIGO_ARTICULO" or "DESCRIPCION_ARTICULO" or "CONTENEDOR" or "CANTIDAD" or "POS")) s.Children.Add(T($"{k}: {Str(v)}", 12, color: Tenue));
        Grid.SetColumn(s, 1); g.Children.Add(s);
        var fotos = c["fotos"]?.AsArray().Select(f => Str(f)).Where(f => f.Length > 0).ToList() ?? new();
        if (fotos.Count > 1)
        {
            var todo = new StackPanel(); todo.Children.Add(g);
            todo.Children.Add(T($"Fotos del artículo ({fotos.Count}) · clic para ver en grande", 12, color: Tenue, margen: new Thickness(0, 10, 0, 4)));
            var w = new WrapPanel();
            foreach (var f in fotos) w.Children.Add(MovimientosPantalla.Miniatura(C.Api, f));
            todo.Children.Add(w);
            _res.Children.Add(Marco(todo));
        }
        else _res.Children.Add(Marco(g));
        // reimprimir la etiqueta del contenedor (con QR): Zebra o A4, en la impresora del servidor o desde esta PC
        {
            var cont = Str(c["contenedor"]);
            var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            w.Children.Add(T("🖨 Reimprimir etiqueta (QR):", 13, true, Marino, new Thickness(0, 6, 10, 0)));
            foreach (var (f, nomF) in new[] { ("zebra", "Zebra"), ("a4", "Hoja A4 grande") })
            {
                // primero: esta PC (diálogo de impresión del navegador, con las impresoras de esta computadora)
                w.Children.Add(Boton($"🖨 {nomF} · en esta PC", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    $"{C.Api.Base}/api/contenedores/{Uri.EscapeDataString(cont)}/etiqueta.html?formato={f}&token={Uri.EscapeDataString(C.Api.Token)}") { UseShellExecute = true }), f == "zebra"));
                w.Children.Add(Boton($"{nomF} · en el servidor", async () => await Hacer(async () =>
                {
                    var r = await C.Api.Post($"/api/contenedores/{Uri.EscapeDataString(cont)}/imprimir", new { formato = f });
                    if (r?["ok"]?.GetValue<bool>() == true) C.Mensaje($"Etiqueta de {cont} enviada a {Str(r["impresora"])}", false);
                    else C.Mensaje($"No se pudo imprimir en {Str(r?["impresora"])}: {Str(r?["error"])}", true);
                })));
            }
            _res.Children.Add(w);
        }
        _res.Children.Add(T("Historial en SONA Depósito", 13.5, true, Marino, new Thickness(0, 4, 0, 6)));
        if (movs is { Count: > 0 })
        {
            var cols = new Col[] { new("Fecha", "hora_servidor", "fecha"), new("Tipo", "tipo", "etiqueta"), new("Origen", "origen", "mono"), new("Destino", "destino", "mono"),
                new("Cant.", "cantidad", "numero"), new("Operario", "usuario"), new("Estado", "estado", "estado"), new("Respuesta del WMS", "respuesta_wms", Ancho: 320), new("Comentario", "comentario") };
            var gm = Grilla(cols); gm.MaxHeight = 320; gm.ItemsSource = Tabla(movs, cols).DefaultView;
            _res.Children.Add(gm);
        }
        else _res.Children.Add(T("SONA no registró movimientos de este contenedor.", 12.5, color: Tenue));
        if (conteos is { Count: > 0 })
        {
            var cols = new Col[] { new("Fecha", "hora", "fecha"), new("Tarea", "tarea_id", "numero"), new("Posición", "posicion", "mono"), new("Contado", "cantidad_contada", "numero"),
                new("Sistema", "cantidad_wms", "numero"), new("Novedad", "novedad", "etiqueta"), new("Revisión", "revision", "etiqueta"), new("Operario", "usuario") };
            var gc = Grilla(cols); gc.MaxHeight = 220; gc.ItemsSource = Tabla(conteos, cols).DefaultView;
            _res.Children.Add(T("Conteos", 13.5, true, Marino, new Thickness(0, 10, 0, 6)));
            _res.Children.Add(gc);
        }
        Historias(Str(c["contenedor"]), Str(c["articulo"]), Str(c["empresa"]));
    }

    /// <summary>Botones para traer la historia del WMS oficial (picking, egresos, etc.) y los ingresos/egresos del e-TMS.</summary>
    void Historias(string contenedor, string articulo, string empresa)
    {
        var area = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var acc = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (contenedor.Length > 0) acc.Children.Add(Boton($"📜 Historia del contenedor en el WMS", async () => await HistoriaWms(area, "contenedor=" + Uri.EscapeDataString(contenedor), $"Historia del contenedor {contenedor} en el WMS (último año)"), true));
        if (articulo.Length > 0)
        {
            acc.Children.Add(Boton($"📜 Historia del artículo {articulo} en el WMS", async () => await HistoriaWms(area, "articulo=" + Uri.EscapeDataString(articulo), $"Historia del artículo {articulo} en el WMS (último año)"), contenedor.Length == 0));
            acc.Children.Add(Boton($"📦 Ingresos / Egresos de {articulo} (e-TMS)", async () => await IngresosEgresos(area, articulo, empresa), fondo: Marino2));
        }
        _res.Children.Add(acc);
        _res.Children.Add(area);
    }

    async Task HistoriaWms(StackPanel area, string q, string titulo)
    {
        area.Children.Clear();
        area.Children.Add(T("Pidiendo la historia al WMS (puede tardar unos segundos)…", 13, color: Tenue));
        try
        {
            var r = await C.Api.Get("/api/historia?" + q);
            area.Children.Clear();
            var nombres = r?["columnas"]?.AsArray().Select(Str).ToList() ?? new();
            var filas = new JsonArray();
            foreach (var f in r?["filas"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                var o = new JsonObject();
                for (var i = 0; i < nombres.Count; i++) o["c" + i] = Str(f[nombres[i]]);
                o["contenedor"] = Str(f["Contenedor"]);
                filas.Add(o);
            }
            area.Children.Add(T($"{titulo} · {filas.Count} movimiento(s)", 13.5, true, Marino, new Thickness(0, 0, 0, 6)));
            if (filas.Count == 0) { area.Children.Add(T("El WMS no informa movimientos en el último año.", 12.5, color: Tenue)); return; }
            var cols = nombres.Select((n, i) => new Col(n, "c" + i, n.Contains("Cantidad", StringComparison.OrdinalIgnoreCase) ? "numero" : n is "Contenedor" or "Ubicación" ? "mono" : "texto")).ToArray();
            var g = Grilla(cols); g.MaxHeight = 420; g.ItemsSource = Tabla(filas, cols, null).DefaultView;
            area.Children.Add(g);
        }
        catch (Exception e) { area.Children.Clear(); area.Children.Add(Marco(T(e.Message, 13, color: Error), ErrorClaro, ErrorClaro)); }
    }

    async Task IngresosEgresos(StackPanel area, string articulo, string empresa)
    {
        area.Children.Clear();
        area.Children.Add(T("Consultando el e-TMS…", 13, color: Tenue));
        try
        {
            var r = await C.Api.Get($"/api/ingresos-egresos/{Uri.EscapeDataString(articulo)}" + (empresa.Length > 0 ? "?empresa=" + empresa : ""));
            area.Children.Clear();
            area.Children.Add(T($"Ingresos / Egresos de {Str(r?["articulo"])} · {Str(r?["descripcion"])} (e-TMS)", 13.5, true, Marino, new Thickness(0, 0, 0, 4)));
            area.Children.Add(T($"Ingresado {Str(r?["ingresado"])}   ·   Egresado {Str(r?["egresado"])}   ·   Saldo {Str(r?["saldo"])}", 13, true, Texto, new Thickness(0, 0, 0, 6)));
            var cols = new Col[] { new("Fecha", "fecha"), new("Movimiento", "tipo", "etiqueta"), new("Entrada", "entrada", "numero"), new("Salida", "salida", "numero"),
                new("PDV", "pdv", Ancho: 220), new("Cód. PDV", "codigoPdv", "mono"), new("Remito", "remito", "mono"), new("Solicitud", "solicitud", "mono"), new("Estado", "estado"), new("Referencia", "referencia", Ancho: 260) };
            var g = Grilla(cols); g.MaxHeight = 420; g.ItemsSource = Tabla(r?["movimientos"]?.AsArray(), cols, null).DefaultView;
            area.Children.Add(g);
        }
        catch (Exception e) { area.Children.Clear(); area.Children.Add(Marco(T(e.Message, 13, color: Error), ErrorClaro, ErrorClaro)); }
    }

    void Articulo(JsonObject a)
    {
        var s = new StackPanel();
        s.Children.Add(T($"{Str(a["articulo"])} · {Str(a["descripcion"])}", 16, true, Marino));
        s.Children.Add(T($"{Str(a["empresaNombre"])}  ·  total {Str(a["total"])}", 13, color: Tenue));
        if (a["les"] is JsonObject les)
        {
            bool? hab = les["habilitado"] is JsonValue hv && hv.TryGetValue<bool>(out var hb) ? hb : null;
            if (hab != null) s.Children.Add(T(hab == true ? "✔ Habilitado en LES" : "✖ Deshabilitado en LES", 13, true, hab == true ? Ok : Error, new Thickness(0, 2, 0, 0)));
            s.Children.Add(T($"Stock en LES: físico {Str(les["stockFisico"])} · disponible {Str(les["stockDisponible"])} · no disponible {Str(les["stockNoDisponible"])} · reservado {Str(les["stockReservado"])}", 12.5, color: Marino2));
        }
        if ((a["contenedores"]?.AsArray().Count ?? 0) == 0) s.Children.Add(T("No tiene stock en el depósito según el buscador SONA.", 12.5, true, Aviso));
        var w = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var f in a["fotos"]?.AsArray() ?? new JsonArray()) w.Children.Add(MovimientosPantalla.Miniatura(C.Api, Str(f!["url"])));
        s.Children.Add(w);
        _res.Children.Add(Marco(s));
        // estado según el WMS (Ver un hueco › Datos de Ubicaciones)
        var conts = new JsonArray((a["contenedores"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>()).Select(c =>
        {
            var x = (JsonObject)c.DeepClone();
            bool? d = x["disponible"] is JsonValue dv && dv.TryGetValue<bool>(out var db) ? db : null;
            x["estado_wms"] = d == true ? "Disponible" : d == false ? "NO DISPONIBLE · " + (Str(x["estadoTexto"]) is { Length: > 0 } t ? t : "BLOQUEADO") : Str(x["estadoTexto"]);
            return (JsonNode)x;
        }).ToArray());
        var noDisp = conts.OfType<JsonObject>().Count(x => x["disponible"] is JsonValue dv && dv.TryGetValue<bool>(out var db) && !db);
        if (noDisp > 0) _res.Children.Add(T($"⚠ {noDisp} contenedor(es) NO disponible(s) según el WMS", 13, true, Error, new Thickness(0, 6, 0, 4)));
        var cols = new Col[] { new("Contenedor", "contenedor", "mono"), new("Posición", "posicion", "mono"), new("Cantidad", "cantidad", "numero"), new("Estado (WMS)", "estado_wms", Ancho: 220) };
        var g = Grilla(cols); g.MaxHeight = 400; g.ItemsSource = Tabla(conts, cols, null).DefaultView;
        g.MouseDoubleClick += (_, _) => { if (Seleccion(g) is { } c) { _cod.Text = Str(c["contenedor"]); _ = Buscar(); } };
        _res.Children.Add(g);
        Historias("", Str(a["articulo"]), Str(a["empresa"]));
    }
}

/// <summary>ZPOS – Fichas de posiciones: edición de varias a la vez (cliente, rol, mezcla, huecos, inhabilitar).</summary>
public sealed class Posiciones : Pantalla
{
    public override string Codigo => "ZPOS";
    public override string Titulo => "Fichas de posiciones";
    protected override string[] Eventos => new[] { "posiciones", "layout" };
    static readonly Col[] Cols =
    {
        new("Posición", "codigo", "mono"), new("Ubic.Ctrl", "ubic_ctrl", "numero"), new("Tipo", "tipo", "etiqueta"), new("Cliente asignado", "empresa_nombre"),
        new("Rol", "rol_zona", "etiqueta"), new("Mezcla", "mezcla", "etiqueta"), new("Huecos", "capacidad", "numero"), new("Ancho", "ancho_cm", "numero"),
        new("Alto", "alto_cm", "numero"), new("Prof.", "prof_cm", "numero"), new("WMS", "estado_wms"), new("Cont.", "contenedores", "numero"),
        new("Habilitada", "habilitada"), new("Motivo", "motivo_inhab"),
    };
    readonly DataGrid _g = Grilla(Cols, true);
    readonly ComboBox _cara = Combo(new[] { ("", "—") });
    readonly TextBox _texto = Caja("", true);
    readonly TextBlock _res = T("", 12.5);

    protected override void Armar()
    {
        var f = Filtros();
        f.Children.Add(Campo("Cara (pasillo)", _cara, 140)); f.Children.Add(Campo("Código contiene", _texto, 150));
        var b = Boton("Mostrar", async () => await CargarSeguro(), true); b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 0, 6); f.Children.Add(b);
        f.Children.Add(T("Las fichas vienen del WMS (Ubicaciones). Lo editado acá pisa lo de la zona; «← lo de la zona» vuelve a tomarlo.", 12, color: Tenue, margen: new Thickness(10, 0, 0, 10)));
        Arriba(Marco(f, FilaPar, Linea2));
        var acc = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var der = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(der, Dock.Right);
        der.Children.Add(Boton("Seleccionar todo", () => _g.SelectAll()));
        der.Children.Add(Boton("✎ Editar seleccionadas", async () =>
        {
            var sel = Selecciones(_g).Select(x => Str(x["codigo"])).ToList();
            if (sel.Count == 0) { C.Mensaje("Elegí una o más posiciones (Ctrl/Shift para varias)", true); return; }
            if (await EditorPosiciones.Mostrar(C, sel)) await CargarSeguro();
        }, true));
        acc.Children.Add(der); acc.Children.Add(_res);
        Arriba(acc);
        _g.MouseDoubleClick += (_, _) => { if (Seleccion(_g) is { } p) C.Abrir("ZCON", new() { ["codigo"] = Str(p["codigo"]) }); };
        Centro(_g);
        if (Parametros.TryGetValue("texto", out var t)) _texto.Text = t;
        if (Parametros.TryGetValue("cara", out var ca)) { _cara.Items.Add(Tuple.Create(ca, ca)); _cara.SelectedValue = ca; }
        _ = CargarCaras();
    }

    async Task CargarCaras()
    {
        try
        {
            var z = await C.Api.Get("/api/zonas");
            var actual = Valor(_cara);
            _cara.Items.Clear(); _cara.Items.Add(Tuple.Create("", "—"));
            foreach (var x in z!.AsArray()) _cara.Items.Add(Tuple.Create(Str(x!["cara"]), $"{Str(x["cara"])} ({Str(x["posiciones"])})"));
            _cara.SelectedValue = actual;
        }
        catch { }
    }

    protected override async Task Cargar()
    {
        if (Valor(_cara).Length == 0 && _texto.Text.Trim().Length == 0) { _res.Text = "Elegí una cara o escribí parte de un código."; return; }
        var d = await C.Api.Get($"/api/posiciones?cara={Uri.EscapeDataString(Valor(_cara))}&texto={Uri.EscapeDataString(_texto.Text.Trim())}");
        var arr = d!.AsArray();
        _g.ItemsSource = Tabla(arr, Cols, "codigo").DefaultView;
        _res.Text = $"{arr.Count} posiciones · {arr.Count(p => Str(p!["estado_wms"]) == "Libre")} libres según WMS · {arr.Count(p => p!["habilitada"]?.GetValue<bool>() == false)} inhabilitadas";
    }
}

/// <summary>Editor de varias posiciones a la vez.</summary>
public static class EditorPosiciones
{
    public static async Task<bool> Mostrar(Contexto c, List<string> codigos)
    {
        var emp = await c.Api.Get("/api/empresas");
        var w = new Window { Title = $"Editar {codigos.Count} posición(es)", Width = 560, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow, ResizeMode = ResizeMode.NoResize };
        var s = new StackPanel { Margin = new Thickness(16) };
        var lista = T(string.Join(" ", codigos.Take(40)) + (codigos.Count > 40 ? " …" : ""), 11.5, color: Tenue, margen: new Thickness(0, 0, 0, 10)); lista.FontFamily = new FontFamily("Consolas");
        s.Children.Add(lista);
        (string, string)[] Ops(params string[] v) => new[] { ("", "(no cambiar)"), ("null", "← lo de la zona") }.Concat(v.Select(x => (x, Et(x)))).ToArray();
        var tipo = Combo(Ops("reserva", "picking", "proyectos", "nn", "otro_deposito"));
        var cliente = Combo(new[] { ("", "(no cambiar)"), ("null", "← lo de la zona") }.Concat(emp!.AsArray().Select(e => (Str(e!["codigo"]), Str(e["nombre"])))));
        var rol = Combo(Ops("nucleo", "desborde", "flex"));
        var mezcla = Combo(Ops("monoproducto", "mismo_cliente", "libre"));
        var huecos = Caja();
        var hab = Combo(new[] { ("", "(no cambiar)"), ("si", "Sí"), ("no", "No (inhabilitar)") });
        var motivo = Caja();
        var g = new WrapPanel();
        g.Children.Add(Campo("Tipo", tipo, 230)); g.Children.Add(Campo("Cliente asignado", cliente, 230)); g.Children.Add(Campo("Rol de zona", rol, 230));
        g.Children.Add(Campo("Mezcla", mezcla, 230)); g.Children.Add(Campo("Huecos (capacidad)", huecos, 230)); g.Children.Add(Campo("Habilitada", hab, 230));
        g.Children.Add(Campo("Motivo de inhabilitación", motivo, 476));
        s.Children.Add(g);
        var ok = false;
        var bs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        bs.Children.Add(Boton("Cancelar", () => w.Close()));
        bs.Children.Add(Boton("Guardar", async () =>
        {
            var cambios = new JsonObject();
            void Poner(string k, string v, bool numero = false) { if (v == "") return; cambios[k] = v == "null" ? null : numero ? JsonValue.Create(long.Parse(v)) : JsonValue.Create(v); }
            Poner("tipo", Valor(tipo)); Poner("empresa", Valor(cliente), true); Poner("rol_zona", Valor(rol)); Poner("mezcla", Valor(mezcla));
            if (long.TryParse(huecos.Text.Trim(), out var h)) cambios["capacidad"] = h;
            if (Valor(hab) == "si") { cambios["habilitada"] = true; cambios["motivo_inhab"] = null; }
            if (Valor(hab) == "no") { cambios["habilitada"] = false; cambios["motivo_inhab"] = motivo.Text.Trim().Length > 0 ? motivo.Text.Trim() : null; }
            try
            {
                var r = await c.Api.Put("/api/posiciones", new JsonObject { ["codigos"] = new JsonArray(codigos.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()), ["cambios"] = cambios });
                c.Mensaje($"{Str(r?["cambiadas"])} posiciones actualizadas", false); ok = true; w.Close();
            }
            catch (Exception e) { MessageBox.Show(e.Message); }
        }, true));
        s.Children.Add(bs);
        w.Content = s;
        w.ShowDialog();
        return ok;
    }
}

/// <summary>ZZON – Zonas del Layout Objetivo y sincronización con el WMS.</summary>
public sealed class Zonas : Pantalla
{
    public override string Codigo => "ZZON";
    public override string Titulo => "Zonas · Layout Objetivo";
    protected override string[] Eventos => new[] { "layout" };
    static readonly Col[] Cols = { new("Cara", "cara", "mono"), new("Tipo", "tipo", "etiqueta"), new("Cliente", "empresa_nombre", Ancho: 260), new("Rol", "rol_zona", "etiqueta"), new("Mezcla", "mezcla", "etiqueta"), new("Posiciones", "posiciones", "numero") };
    readonly DataGrid _g = Grilla(Cols, true);
    readonly TextBlock _estado = T("", 12, color: Tenue);

    protected override void Armar()
    {
        Arriba(Marco(T("Cada cara (pasillo) tiene tipo, cliente, rol (núcleo / desborde / flex) y regla de mezcla. Con esto el sistema sugiere el destino y bloquea o pide autorización. Una posición puntual se cambia en ZPOS.", 12.5, color: Info), InfoClaro, InfoClaro));
        var acc = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        acc.Children.Add(Boton("＋ Nueva cara", async () => { if (await Editar(null)) await CargarSeguro(); }, true));
        acc.Children.Add(Boton("✎ Editar", async () => { if (Seleccion(_g) is { } z && await Editar(z)) await CargarSeguro(); }));
        acc.Children.Add(Boton("⟳ Sincronizar layout del WMS (todas)", async () => await Hacer(async () => { var r = await C.Api.Post("/api/layout/sincronizar", new { }); C.Mensaje(Str(r?["mensaje"]), false); }), fondo: Marino));
        acc.Children.Add(Boton("⟳ Sincronizar seleccionadas", async () =>
        {
            var caras = Selecciones(_g).Select(x => Str(x["cara"])).ToList();
            if (caras.Count == 0) return;
            await Hacer(async () => { var r = await C.Api.Post("/api/layout/sincronizar", new { caras }); C.Mensaje(Str(r?["mensaje"]), false); });
        }));
        acc.Children.Add(Boton("Mapa", () => { if (Seleccion(_g) is { } z) C.Abrir("ZMAP", new() { ["cara"] = Str(z["cara"]) }); }));
        Arriba(acc);
        Arriba(_estado);
        _g.MouseDoubleClick += async (_, _) => { if (Seleccion(_g) is { } z && await Editar(z)) await CargarSeguro(); };
        Centro(_g);
    }

    protected override async Task Cargar()
    {
        _g.ItemsSource = Tabla((await C.Api.Get("/api/zonas"))?.AsArray(), Cols, "cara").DefaultView;
        var l = await C.Api.Get("/api/layout/estado");
        _estado.Text = l is JsonObject ? $"Última sincronización: {FechaHora(Str(l["fin"]) is { Length: > 0 } f ? f : Str(l["inicio"]))} · {Str(l["caras"])} caras · {Str(l["posiciones"])} posiciones" + (l["errores"]?.AsArray().Count > 0 ? " · errores: " + Str(l["errores"]) : "") : "Layout sin sincronizar desde que arrancó el servidor.";
    }

    async Task<bool> Editar(JsonObject? z)
    {
        var emp = await C.Api.Get("/api/empresas");
        var w = new Window { Title = z == null ? "Nueva cara" : "Cara " + Str(z["cara"]), Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow, ResizeMode = ResizeMode.NoResize };
        var s = new WrapPanel { Margin = new Thickness(16) };
        var cara = Caja(Str(z?["cara"]), true); cara.IsEnabled = z == null;
        var tipo = Combo(new[] { "reserva", "picking", "proyectos", "nn", "otro_deposito" }.Select(x => (x, Et(x))), Str(z?["tipo"]) is { Length: > 0 } t ? t : "reserva");
        var cli = Combo(new[] { ("", "— flex (cualquiera) —") }.Concat(emp!.AsArray().Select(e => (Str(e!["codigo"]), Str(e["nombre"])))), Str(z?["empresa"]));
        var rol = Combo(new[] { "nucleo", "desborde", "flex" }.Select(x => (x, Et(x))), Str(z?["rol_zona"]) is { Length: > 0 } r ? r : "nucleo");
        var mez = Combo(new[] { "monoproducto", "mismo_cliente", "libre" }.Select(x => (x, Et(x))), Str(z?["mezcla"]) is { Length: > 0 } m ? m : "monoproducto");
        s.Children.Add(Campo("Cara", cara, 200)); s.Children.Add(Campo("Tipo", tipo, 220)); s.Children.Add(Campo("Cliente", cli, 436));
        s.Children.Add(Campo("Rol", rol, 200)); s.Children.Add(Campo("Mezcla", mez, 220));
        var ok = false;
        var bs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        bs.Children.Add(Boton("Cancelar", () => w.Close()));
        bs.Children.Add(Boton("Guardar", async () =>
        {
            if (cara.Text.Trim().Length == 0) return;
            try
            {
                await C.Api.Put($"/api/zonas/{Uri.EscapeDataString(cara.Text.Trim())}", new { tipo = Valor(tipo), empresa = int.TryParse(Valor(cli), out var e) ? e : (int?)null, rol_zona = Valor(rol), mezcla = Valor(mez) });
                C.Mensaje("Zona guardada", false); ok = true; w.Close();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }, true));
        s.Children.Add(bs);
        w.Content = s;
        w.ShowDialog();
        return ok;
    }
}

/// <summary>ZMAP – Mapa de pasillo (columnas × niveles): rol de zona, ocupación e inhabilitadas.</summary>
public sealed class Mapa : Pantalla
{
    public override string Codigo => "ZMAP";
    public override string Titulo => "Mapa de pasillo";
    protected override string[] Eventos => new[] { "posiciones", "layout" };
    readonly ComboBox _cara = Combo(new[] { ("", "—") });
    readonly Grid _mapa = new();
    readonly HashSet<string> _sel = new();

    protected override void Armar()
    {
        var f = Filtros();
        f.Children.Add(Campo("Cara", _cara, 140));
        _cara.SelectionChanged += async (_, _) => { _sel.Clear(); await CargarSeguro(); };
        f.Children.Add(T("■ Núcleo  ■ Desborde  ■ Flex · fondo gris = ocupada · rayado rojo = inhabilitada · Ctrl+clic para elegir varias", 12, color: Tenue, margen: new Thickness(10, 0, 10, 10)));
        var ed = Boton("✎ Editar elegidas", async () => { if (_sel.Count > 0 && await EditorPosiciones.Mostrar(C, _sel.ToList())) await CargarSeguro(); }, true);
        ed.VerticalAlignment = VerticalAlignment.Bottom; ed.Margin = new Thickness(0, 0, 0, 6);
        f.Children.Add(ed);
        Arriba(Marco(f, FilaPar, Linea2));
        Centro(new ScrollViewer { Content = _mapa, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _ = CargarCaras();
    }

    async Task CargarCaras()
    {
        try
        {
            var z = await C.Api.Get("/api/zonas");
            foreach (var x in z!.AsArray()) _cara.Items.Add(Tuple.Create(Str(x!["cara"]), Str(x["cara"])));
            if (Parametros.TryGetValue("cara", out var c)) _cara.SelectedValue = c;
        }
        catch { }
    }

    protected override async Task Cargar()
    {
        _mapa.Children.Clear(); _mapa.RowDefinitions.Clear(); _mapa.ColumnDefinitions.Clear();
        var cara = Valor(_cara);
        if (cara.Length == 0) return;
        var pos = (await C.Api.Get($"/api/posiciones?cara={Uri.EscapeDataString(cara)}"))!.AsArray().OfType<JsonObject>().ToList();
        var cols = pos.Select(p => p["columna"]!.GetValue<int>()).Distinct().OrderBy(x => x).ToList();
        var nivs = pos.Select(p => p["nivel"]!.GetValue<int>()).Distinct().OrderByDescending(x => x).ToList();
        _mapa.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        foreach (var _ in cols) _mapa.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        foreach (var _ in nivs) _mapa.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        _mapa.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        for (var r = 0; r < nivs.Count; r++)
        {
            var et = T("N" + nivs[r], 12, color: Tenue); Grid.SetRow(et, r); _mapa.Children.Add(et);
            for (var c = 0; c < cols.Count; c++)
            {
                var p = pos.FirstOrDefault(x => x["columna"]!.GetValue<int>() == cols[c] && x["nivel"]!.GetValue<int>() == nivs[r]);
                if (p == null) continue;
                var cod = Str(p["codigo"]);
                var rolColor = Str(p["rol_zona"]) switch { "nucleo" => Marino, "desborde" => Naranja, _ => Hex("#9AA5B1") };
                var inhab = p["habilitada"]?.GetValue<bool>() == false;
                var celda = new Border
                {
                    Margin = new Thickness(2), BorderThickness = new Thickness(3, 1, 1, 1), BorderBrush = rolColor, CornerRadius = new CornerRadius(3), Cursor = System.Windows.Input.Cursors.Hand,
                    Background = _sel.Contains(cod) ? NaranjaClaro : inhab ? new LinearGradientBrush(ErrorClaro.Color, Colors.White, 45) { SpreadMethod = GradientSpreadMethod.Repeat, EndPoint = new Point(0.1, 0.1) } : Str(p["estado_wms"]) == "Ocupado" ? Hex("#E8EEF5") : Blanco,
                    ToolTip = $"{cod} · {Et(Str(p["tipo"]))} · {(Str(p["empresa_nombre"]) is { Length: > 0 } en ? en : "flex")} · {Et(Str(p["mezcla"]))} · {Str(p["estado_wms"])}",
                };
                var st = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var tc = T(cod, 10.5, true); tc.FontFamily = new FontFamily("Consolas"); st.Children.Add(tc);
                st.Children.Add(T($"{Str(p["contenedores"])}/{Str(p["capacidad"])}", 10, color: Tenue));
                celda.Child = st;
                celda.MouseLeftButtonUp += async (_, e) =>
                {
                    if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) { if (!_sel.Add(cod)) _sel.Remove(cod); }
                    else { _sel.Clear(); _sel.Add(cod); }
                    if (e.ClickCount == 2) { C.Abrir("ZCON", new() { ["codigo"] = cod }); return; }
                    await CargarSeguro();
                };
                Grid.SetRow(celda, r); Grid.SetColumn(celda, c + 1); _mapa.Children.Add(celda);
            }
        }
        for (var c = 0; c < cols.Count; c++) { var t = T(cols[c].ToString(), 11, color: Tenue); t.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(t, nivs.Count); Grid.SetColumn(t, c + 1); _mapa.Children.Add(t); }
    }
}
