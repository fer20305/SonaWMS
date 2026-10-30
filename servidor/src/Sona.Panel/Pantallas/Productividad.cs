using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>
/// ZPRO – Productividad de los operarios: cuánto hizo cada uno, de qué tipo, en qué horas, y el detalle de todo lo que hizo.
/// Incluye a los pickers: OPs, líneas, unidades, líneas por hora (sin contar pausas), medición de bultos y control de pedidos.
/// </summary>
public sealed class Productividad : Pantalla
{
    public override string Codigo => "ZPRO";
    public override string Titulo => "Productividad de operarios";
    protected override string[] Eventos => new[] { "movimiento", "conteo", "picking" };
    readonly DatePicker _desde = new() { SelectedDate = DateTime.Today }, _hasta = new() { SelectedDate = DateTime.Today };
    readonly WrapPanel _kpis = new();
    readonly WrapPanel _horas = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly TextBlock _titDet = T("Elegí un operario para ver sus movimientos", 13, true, Marino, new Thickness(0, 8, 0, 4));
    static readonly Col[] Cols =
    {
        new("Operario", "usuario", "mono"), new("Nombre", "nombre", Ancho: 160), new("Rol", "rol", "etiqueta"), new("Tareas", "tareas", "numero"), new("Tareas / hora", "por_hora", "numero"),
        new("Movimientos", "movimientos", "numero"), new("Transf.", "transferencias", "numero"), new("Guardados", "guardados", "numero"), new("Estado", "cambios_estado", "numero"),
        new("Reub. cajas", "reubicar_cajas", "numero"), new("Ajustes", "ajustes", "numero"), new("Altas", "altas", "numero"),
        new("OPs", "ops_picking", "numero"), new("OPs terminadas", "ops_terminadas", "numero"), new("Líneas pick", "lineas_pick", "numero"), new("Unid. pick", "unidades_pick", "numero"),
        new("Líneas / hora", "lineas_hora", "numero"), new("Min / OP", "min_por_op", "numero"), new("Min picking", "min_picking", "numero"), new("Pausas", "pausas", "numero"),
        new("Errores evitados", "errores_pick", "numero"),
        new("Sol. medidas", "solicitudes_medidas", "numero"), new("Bultos medidos", "bultos_medidos", "numero"),
        new("Ped. controlados", "pedidos_controlados", "numero"), new("Bultos controlados", "bultos_controlados", "numero"), new("Completos", "pedidos_completos", "numero"),
        new("Posic. contadas", "posiciones_contadas", "numero"), new("Dif. conteo", "diferencias", "numero"),
        new("Aplicados", "aplicados", "numero"), new("Errores", "errores", "numero"), new("Primero", "primero"), new("Último", "ultimo"),
        new("Horas activas", "horas_activas", "numero"),
    };
    static readonly Col[] ColsDet =
    {
        new("Fecha", "hora", "fecha"), new("Tipo", "tipo", "etiqueta"), new("Contenedor / Sol.", "contenedor", "mono"), new("Artículo", "articulo", "mono"),
        new("Descripción", "descripcion", Ancho: 200), new("Cant.", "cantidad", "numero"), new("Origen", "origen", "mono"), new("Destino", "destino", "mono"),
        new("Estado", "estado", "estado"), new("Detalle / respuesta del WMS", "detalle", Ancho: 320),
    };
    static readonly Col[] ColsCtl =
    {
        new("Fecha", "hora", "fecha"), new("Solicitud", "nro", "mono"), new("Cant. controlada", "cantidad", "numero"), new("Bultos", "bultos", "numero"),
        new("Completo", "completo_t"), new("Observación", "observacion", Ancho: 300), new("Telegram", "telegram", Ancho: 140),
    };
    static readonly Col[] ColsPick =
    {
        new("Fecha", "hora", "fecha"), new("Evento", "tipo", "etiqueta"), new("Modo", "modo"), new("OP", "op", "mono"), new("Artículo", "articulo", "mono"),
        new("Cant.", "cantidad", "numero"), new("Posición", "posicion", "mono"), new("Contenedor", "contenedor", "mono"), new("Cont. picking", "contenedor_picking", "mono"),
        new("Detalle", "detalle", Ancho: 300),
    };
    readonly DataGrid _g = Grilla(Cols), _det = Grilla(ColsDet), _pick = Grilla(ColsPick), _ctl = Grilla(ColsCtl);
    readonly TabControl _tabs = new();

    protected override void Armar()
    {
        var f = Filtros();
        f.Children.Add(Campo("Desde", _desde, 130)); f.Children.Add(Campo("Hasta", _hasta, 130));
        var bs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
        void Rango(string t, DateTime d, DateTime h) => bs.Children.Add(Boton(t, async () => { _desde.SelectedDate = d; _hasta.SelectedDate = h; await CargarSeguro(); }));
        bs.Children.Add(Boton("Ejecutar", async () => await CargarSeguro(), true));
        Rango("Hoy", DateTime.Today, DateTime.Today);
        Rango("Ayer", DateTime.Today.AddDays(-1), DateTime.Today.AddDays(-1));
        Rango("7 días", DateTime.Today.AddDays(-6), DateTime.Today);
        Rango("Este mes", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today);
        f.Children.Add(bs);
        Arriba(Marco(f, FilaPar, Linea2));
        // KPIs, actividad por hora y grillas en un área con scroll: en pantallas chicas las grillas no quedan aplastadas
        // (antes se veía solo la cabecera de la tabla de operarios y la del detalle)
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 170 });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.3, GridUnitType.Star), MinHeight = 220 });
        g.Children.Add(_g);
        var div = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Linea2, ResizeDirection = GridResizeDirection.Rows, ToolTip = "Arrastrá para agrandar una tabla u otra" };
        Grid.SetRow(div, 1); g.Children.Add(div);
        Grid.SetRow(_titDet, 2); g.Children.Add(_titDet);
        _tabs.Items.Add(new TabItem { Header = "Todo lo que hizo", Content = _det });
        _tabs.Items.Add(new TabItem { Header = "Picking y medición", Content = _pick });
        _tabs.Items.Add(new TabItem { Header = "Control de pedidos", Content = _ctl });
        Grid.SetRow(_tabs, 3); g.Children.Add(_tabs);
        _g.SelectionChanged += async (_, _) => await Detalle();
        var pila = new StackPanel();
        pila.Children.Add(_kpis); pila.Children.Add(_horas); pila.Children.Add(g);
        var sc = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = pila };
        void Ajustar() => g.Height = Math.Max(480, sc.ViewportHeight - _kpis.ActualHeight - _horas.ActualHeight - 8);
        sc.SizeChanged += (_, _) => Ajustar();
        _kpis.SizeChanged += (_, _) => Ajustar();
        _horas.SizeChanged += (_, _) => Ajustar();
        Centro(sc);
    }

    string Rq() => $"desde={_desde.SelectedDate ?? DateTime.Today:yyyy-MM-dd}&hasta={_hasta.SelectedDate ?? _desde.SelectedDate ?? DateTime.Today:yyyy-MM-dd}";

    protected override async Task Cargar()
    {
        var r = await C.Api.Get("/api/productividad?" + Rq());
        var t = r?["totales"];
        _kpis.Children.Clear();
        _kpis.Children.Add(Kpi("Tareas", Str(t?["tareas"]), Naranja));
        _kpis.Children.Add(Kpi("Movimientos de depósito", Str(t?["movimientos"]), Marino2));
        _kpis.Children.Add(Kpi("Operarios activos", Str(t?["operarios"]), Marino2));
        _kpis.Children.Add(Kpi("Posiciones contadas", Str(t?["posiciones_contadas"]), Ok));
        _kpis.Children.Add(Kpi("Líneas pickeadas", Str(t?["lineas_pick"]), Naranja));
        _kpis.Children.Add(Kpi("Unidades pickeadas", Str(t?["unidades_pick"]), Naranja));
        _kpis.Children.Add(Kpi("Líneas / hora (pickers)", Str(t?["lineas_hora"]) is { Length: > 0 } lh ? lh : "—", Naranja));
        _kpis.Children.Add(Kpi("OPs terminadas", Str(t?["ops_terminadas"]), Ok));
        _kpis.Children.Add(Kpi("Solicitudes medidas", Str(t?["solicitudes_medidas"]), Marino2));
        _kpis.Children.Add(Kpi("Bultos medidos", Str(t?["bultos_medidos"]), Marino2));
        _kpis.Children.Add(Kpi("Pedidos controlados", Str(t?["pedidos_controlados"]), Marino2));
        _kpis.Children.Add(Kpi("Errores de lectura evitados", Str(t?["errores_pick"]), Aviso));
        _kpis.Children.Add(Kpi("Errores / rechazados", Str(t?["errores"]), Error));
        // actividad por hora (barras simples)
        _horas.Children.Clear();
        var ph = r?["porHora"]?.AsArray().OfType<JsonObject>().ToList() ?? new();
        var max = Math.Max(1, ph.Select(x => x["n"]!.GetValue<int>()).DefaultIfEmpty(0).Max());
        if (ph.Count > 0) _horas.Children.Add(T("Actividad por hora:  ", 12, true, Tenue));
        foreach (var x in ph)
        {
            var n = x["n"]!.GetValue<int>();
            var s = new StackPanel { Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Bottom, ToolTip = $"{n} entre las {x["hora"]}:00 y las {x["hora"]}:59" };
            s.Children.Add(T(n.ToString(), 10, color: Tenue));
            s.Children.Add(new Border { Width = 22, Height = 4 + 46.0 * n / max, Background = Naranja, CornerRadius = new CornerRadius(2, 2, 0, 0) });
            s.Children.Add(T($"{x["hora"]}h", 10, color: Tenue));
            _horas.Children.Add(s);
        }
        _g.ItemsSource = Tabla(r?["operarios"]?.AsArray(), Cols, "usuario").DefaultView;
        _titDet.Text = "Elegí un operario para ver todo lo que hizo";
    }

    async Task Detalle()
    {
        if (Seleccion(_g) is not { } o) return;
        var u = Str(o["usuario"]);
        try
        {
            // todo lo que hizo: movimientos de depósito, picking, medición, control de pedidos y conteo, en orden
            var lista = (await C.Api.Get($"/api/productividad/actividad?usuario={Uri.EscapeDataString(u)}&{Rq()}"))?.AsArray();
            _titDet.Text = $"Actividad de {(Str(o["nombre"]) is { Length: > 0 } n ? n : u)} ({lista?.Count ?? 0})";
            _det.ItemsSource = Tabla(lista, ColsDet, null).DefaultView;
            ((TabItem)_tabs.Items[0]).Header = $"Todo lo que hizo ({lista?.Count ?? 0})";
            var pk = await C.Api.Get($"/api/picking/eventos?usuario={Uri.EscapeDataString(u)}&{Rq()}");
            _pick.ItemsSource = Tabla(pk?.AsArray(), ColsPick).DefaultView;
            ((TabItem)_tabs.Items[1]).Header = $"Picking y medición ({pk?.AsArray().Count ?? 0})";
            var ctl = (await C.Api.Get($"/api/control-pedidos?usuario={Uri.EscapeDataString(u)}&{Rq()}"))?.AsArray();
            foreach (var x in ctl?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>()) x["completo_t"] = x["completo"]?.GetValue<bool>() == true ? "Sí" : "No";
            _ctl.ItemsSource = Tabla(ctl, ColsCtl).DefaultView;
            ((TabItem)_tabs.Items[2]).Header = $"Control de pedidos ({ctl?.Count ?? 0})";
        }
        catch (Exception e) { C.Mensaje(e.Message, true); }
    }
}
