using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Sona.Panel;

/// <summary>Colores SONA y piezas de interfaz armadas por código (estilo SAP: barras, grillas tipo ALV, campos rotulados).</summary>
public static class Ui
{
    public static readonly SolidColorBrush Marino = Hex("#0B2A42"), Marino2 = Hex("#1C4C73"), Naranja = Hex("#F47920"), NaranjaClaro = Hex("#FFE7D4"),
        Fondo = Hex("#EEF1F4"), Blanco = Brushes.White, Linea = Hex("#C9D1D9"), Linea2 = Hex("#E2E7EC"), Texto = Hex("#1B2733"), Tenue = Hex("#5D6B79"),
        Ok = Hex("#1E8E3E"), OkClaro = Hex("#E3F4E8"), Aviso = Hex("#B7791F"), AvisoClaro = Hex("#FFF5DB"), Error = Hex("#C62828"), ErrorClaro = Hex("#FDE7E7"),
        Info = Hex("#1565C0"), InfoClaro = Hex("#E3EEFB"), Cabecera = Hex("#E9EEF3"), FilaPar = Hex("#F7F9FB");

    public static SolidColorBrush Hex(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); b.Freeze(); return b; }

    public static readonly Dictionary<string, string> Etiquetas = new()
    {
        ["pendiente_wms"] = "Pendiente WMS", ["aplicado"] = "Aplicado", ["error"] = "Error", ["esperando_autorizacion"] = "Esperando autorización",
        ["rechazado"] = "Rechazado", ["anulado"] = "Anulado", ["registrado"] = "Registrado", ["pendiente"] = "Pendiente", ["aprobada"] = "Aprobada",
        ["rechazada"] = "Rechazada", ["cancelada"] = "Cancelada", ["abierta"] = "Abierta", ["en_curso"] = "En curso", ["cerrada"] = "Cerrada",
        ["transferencia"] = "Transferencia", ["guardado"] = "Guardado", ["cambio_estado"] = "Cambio de estado", ["anulacion"] = "Anulación", ["reubicar_cajas"] = "Reubicar cajas", ["ajuste"] = "Ajuste / eliminación", ["ciego"] = "Ciego", ["confirmacion"] = "Confirmación", ["articulo"] = "Por artículo",
        ["medicion"] = "Medición de bultos", ["control_pedido"] = "Control de pedido", ["alta"] = "Crear contenedor", ["pausa"] = "Pausa", ["retoma"] = "Retoma",
        ["bultos"] = "Medición de bultos", ["control"] = "Control de pedido", ["completo"] = "Completo", ["parcial"] = "Con observación",
        ["pick"] = "Pick", ["inicio"] = "Inicio OP", ["fin"] = "OP terminada", ["isla"] = "A isla de control", ["error_lectura"] = "Error de lectura (evitado)", ["rechazo_wms"] = "Rechazo del WMS", ["cerrar"] = "Cerrar contenedor", ["postergar"] = "Postergar OP",
        ["ok"] = "OK", ["falta"] = "Falta", ["sobra"] = "Sobra", ["ajeno"] = "Ajeno", ["diferencia"] = "Diferencia", ["recontar"] = "Recontar",
        ["reserva"] = "Reserva", ["picking"] = "Picking", ["proyectos"] = "Proyectos", ["nn"] = "NN", ["otro_deposito"] = "Otro depósito",
        ["nucleo"] = "Núcleo", ["desborde"] = "Desborde", ["flex"] = "Flex", ["monoproducto"] = "Monoproducto", ["mismo_cliente"] = "Mismo cliente", ["libre"] = "Libre",
        ["maquinista"] = "Maquinista", ["conteo"] = "Asistente de conteo", ["picker"] = "Picker", ["supervisor"] = "Supervisor (solo ve)", ["produccion"] = "Producción (asigna pedidos)", ["analista"] = "Analista", ["admin"] = "Administrador",
    };
    public static string Et(string? s) => s == null ? "" : Etiquetas.GetValueOrDefault(s, s);

    public static (Brush F, Brush C) ColorEstado(string? e) => e switch
    {
        "aplicado" or "aprobada" or "ok" or "cerrada" => (OkClaro, Ok),
        "pendiente_wms" or "pendiente" or "en_curso" or "abierta" => (InfoClaro, Info),
        "esperando_autorizacion" or "diferencia" or "sobra" or "recontar" => (AvisoClaro, Aviso),
        "error" or "rechazado" or "rechazada" or "falta" or "ajeno" => (ErrorClaro, Error),
        _ => (Hex("#EEF0F2"), Tenue),
    };

    // ------------------------------------------------------------------ piezas
    public static TextBlock T(string texto, double tam = 13, bool negrita = false, Brush? color = null, Thickness? margen = null) => new()
    {
        Text = texto, FontSize = tam, FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal, Foreground = color ?? Texto,
        Margin = margen ?? new Thickness(0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
    };

    public static Button Boton(string texto, Action alClic, bool primario = false, Brush? fondo = null)
    {
        var b = new Button
        {
            Content = texto, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), MinHeight = 30, Cursor = System.Windows.Input.Cursors.Hand,
            Background = fondo ?? (primario ? Naranja : Blanco), Foreground = primario || fondo != null ? Brushes.White : Marino,
            BorderBrush = fondo ?? (primario ? Naranja : Linea), BorderThickness = new Thickness(1), FontWeight = primario ? FontWeights.SemiBold : FontWeights.Normal,
        };
        b.Click += (_, _) => alClic();
        return b;
    }

    public static FrameworkElement Campo(string etiqueta, FrameworkElement control, double ancho = 150)
    {
        control.MinWidth = ancho;
        if (control is Control c) { c.Height = 30; c.VerticalContentAlignment = VerticalAlignment.Center; c.Padding = new Thickness(6, 0, 6, 0); }
        var s = new StackPanel { Margin = new Thickness(0, 0, 14, 6) };
        s.Children.Add(T(etiqueta, 11.5, color: Tenue, margen: new Thickness(0, 0, 0, 3)));
        s.Children.Add(control);
        return s;
    }

    public static TextBox Caja(string texto = "", bool mayus = false)
    {
        var t = new TextBox { Text = texto, BorderBrush = Linea };
        if (mayus) t.CharacterCasing = CharacterCasing.Upper;
        return t;
    }

    public static ComboBox Combo(IEnumerable<(string Valor, string Texto)> ops, string? elegido = null)
    {
        var c = new ComboBox { DisplayMemberPath = "Item2", SelectedValuePath = "Item1" };
        foreach (var o in ops) c.Items.Add(Tuple.Create(o.Valor, o.Texto));
        c.SelectedValue = elegido ?? "";
        if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }
    public static string Valor(ComboBox c) => c.SelectedValue as string ?? "";

    public static WrapPanel Filtros() => new() { Margin = new Thickness(0, 0, 0, 10), Background = FilaPar };

    public static Border Marco(UIElement contenido, Brush? fondo = null, Brush? borde = null, double radio = 6) => new()
    {
        Child = contenido, Background = fondo ?? Blanco, BorderBrush = borde ?? Linea, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radio), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10),
    };

    public static Border Chip(string? estado)
    {
        var (f, c) = ColorEstado(estado);
        return new Border { Background = f, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 1), Child = T(Et(estado), 11.5, true, c), HorizontalAlignment = HorizontalAlignment.Left };
    }

    public static Border Kpi(string titulo, string valor, Brush acento, Action? clic = null)
    {
        var s = new StackPanel();
        s.Children.Add(T(valor, 26, true, Marino));
        s.Children.Add(T(titulo, 12, color: Tenue));
        var b = new Border { Child = s, Background = Blanco, BorderBrush = Linea, BorderThickness = new Thickness(4, 1, 1, 1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 10), MinWidth = 180 };
        b.BorderBrush = acento;
        if (clic != null) { b.Cursor = System.Windows.Input.Cursors.Hand; b.MouseLeftButtonUp += (_, _) => clic(); }
        return b;
    }

    // ------------------------------------------------------------------ fechas y números
    public static string FechaHora(string? iso) =>
        iso != null && DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("dd/MM/yy HH:mm:ss") : "";
    public static string Str(JsonNode? n) => n switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "Sí" : "No",
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString("0.##", CultureInfo.GetCultureInfo("es-AR")),
        JsonArray a => string.Join(", ", a.Select(Str)),
        _ => n.ToJsonString(),
    };

    // ------------------------------------------------------------------ grilla tipo ALV
    public sealed record Col(string Titulo, string Campo, string Tipo = "texto", double Ancho = double.NaN);

    /// <summary>Convierte un arreglo JSON en una tabla para la grilla (fechas legibles, etiquetas de estado, números alineados).</summary>
    public static DataTable Tabla(JsonArray? filas, IEnumerable<Col> cols, string? clave = "id")
    {
        var t = new DataTable();
        var lista = cols.ToList();
        if (clave != null) t.Columns.Add("__clave", typeof(string));
        t.Columns.Add("__json", typeof(object));
        foreach (var c in lista) t.Columns.Add(c.Campo, c.Tipo == "numero" ? typeof(double) : typeof(string));
        foreach (var f in filas ?? new JsonArray())
        {
            if (f is not JsonObject o) continue;
            var r = t.NewRow();
            if (clave != null) r["__clave"] = Str(o[clave]);
            r["__json"] = o;
            foreach (var c in lista)
            {
                var v = o[c.Campo];
                r[c.Campo] = c.Tipo switch
                {
                    "numero" => v is JsonValue jv && double.TryParse(Str(jv).Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : DBNull.Value,
                    "fecha" => FechaHora(Str(v)),
                    "estado" or "etiqueta" => Et(Str(v)),
                    _ => Str(v),
                };
            }
            t.Rows.Add(r);
        }
        return t;
    }

    public static DataGrid Grilla(IEnumerable<Col> cols, bool multiple = false)
    {
        var g = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = multiple ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = Linea2, AlternatingRowBackground = FilaPar,
            RowHeight = 26, BorderBrush = Linea, Background = Blanco, FontSize = 12.5, EnableRowVirtualization = true,
            ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
        };
        Copiable(g);
        var cab = new Style(typeof(DataGridColumnHeader));
        cab.Setters.Add(new Setter(Control.BackgroundProperty, Cabecera));
        cab.Setters.Add(new Setter(Control.ForegroundProperty, Marino));
        cab.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        cab.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        cab.Setters.Add(new Setter(Control.BorderBrushProperty, Linea));
        cab.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        g.ColumnHeaderStyle = cab;
        var sel = new Style(typeof(DataGridCell));
        sel.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var tr = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        tr.Setters.Add(new Setter(Control.BackgroundProperty, NaranjaClaro));
        tr.Setters.Add(new Setter(Control.ForegroundProperty, Texto));
        sel.Triggers.Add(tr);
        g.CellStyle = sel;
        foreach (var c in cols)
        {
            var col = new DataGridTextColumn { Header = c.Titulo, Binding = new Binding($"[{c.Campo}]") { StringFormat = c.Tipo == "numero" ? "#,0.##" : null }, Width = double.IsNaN(c.Ancho) ? DataGridLength.Auto : new DataGridLength(c.Ancho) };
            var est = new Style(typeof(TextBlock));
            est.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 0, 6, 0)));
            est.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            if (c.Tipo == "numero") est.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            if (c.Tipo == "mono") est.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Consolas")));
            if (c.Tipo == "estado")
            {
                est.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                foreach (var (clave, et) in Etiquetas)
                {
                    var (_, color) = ColorEstado(clave);
                    if (color == Tenue) continue;
                    var dt = new DataTrigger { Binding = new Binding($"[{c.Campo}]"), Value = et };
                    dt.Setters.Add(new Setter(TextBlock.ForegroundProperty, color));
                    est.Triggers.Add(dt);
                }
            }
            col.ElementStyle = est;
            g.Columns.Add(col);
        }
        return g;
    }

    // ================================================================ copiar desde las grillas

    /// <summary>Lo pone la ventana principal: abre ZCON con ese contenedor (historial).</summary>
    public static Action<string>? AbrirContenedor;

    public static void AlPortapapeles(string texto)
    {
        for (var i = 0; i < 6; i++)
        {
            try { Clipboard.SetDataObject(texto, true); return; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(80); }   // el portapapeles de Windows a veces está ocupado
        }
    }

    static string CampoDe(DataGridColumn c) => c is DataGridBoundColumn { Binding: Binding b } ? b.Path.Path.Trim('[', ']') : "";

    static string Valor(object? item, DataGridColumn c)
    {
        if (item is not DataRowView rv) return "";
        var campo = CampoDe(c);
        if (campo.Length == 0 || !rv.Row.Table.Columns.Contains(campo)) return "";
        return rv.Row[campo] switch { DBNull => "", double d => d.ToString("0.##", CultureInfo.CurrentCulture), var v => v?.ToString() ?? "" };
    }

    static string Fila(DataGrid g, object item) => string.Join("\t", g.Columns.OrderBy(c => c.DisplayIndex).Select(c => Valor(item, c)));

    static string ContenedorDeFila(object? item) => item is DataRowView rv && rv.Row.Table.Columns.Contains("__json") && rv.Row["__json"] is JsonObject o ? Str(o["contenedor"]).Trim() : "";

    /// <summary>
    /// Ctrl+C copia la celda donde se hizo clic; clic derecho: copiar la celda, la fila o toda la tabla (se pega directo en Excel),
    /// y si la fila tiene contenedor, ver su historial.
    /// </summary>
    static void Copiable(DataGrid g)
    {
        string Celda() => g.CurrentCell.Column != null && g.CurrentCell.Item != null ? Valor(g.CurrentCell.Item, g.CurrentCell.Column) : "";
        g.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
        {
            var t = Celda();
            if (t.Length == 0 && g.SelectedItems.Count > 0) t = string.Join("\r\n", g.SelectedItems.Cast<object>().Select(i => Fila(g, i)));
            if (t.Length > 0) AlPortapapeles(t);
            e.Handled = true;
        }));
        g.PreviewMouseRightButtonDown += (_, e) =>
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not DataGridCell) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            if (d is not DataGridCell cel) return;
            if (!g.SelectedItems.Contains(cel.DataContext)) g.SelectedItem = cel.DataContext;
            g.CurrentCell = new DataGridCellInfo(cel);
        };
        var menu = new ContextMenu();
        g.ContextMenu = menu;
        g.ContextMenuOpening += (_, e) =>
        {
            menu.Items.Clear();
            if (g.SelectedItem == null) { e.Handled = true; return; }
            void Item(string texto, Action a) { var mi = new MenuItem { Header = texto }; mi.Click += (_, _) => a(); menu.Items.Add(mi); }
            var valor = Celda();
            if (valor.Length > 0) Item($"Copiar \"{(valor.Length > 40 ? valor[..40] + "…" : valor)}\"    (Ctrl+C)", () => AlPortapapeles(valor));
            var filas = g.SelectedItems.Cast<object>().ToList();
            Item(filas.Count > 1 ? $"Copiar las {filas.Count} filas" : "Copiar la fila", () => AlPortapapeles(string.Join("\r\n", filas.Select(i => Fila(g, i)))));
            Item("Copiar toda la tabla (para pegar en Excel)", () =>
            {
                var cab = string.Join("\t", g.Columns.OrderBy(c => c.DisplayIndex).Select(c => c.Header?.ToString() ?? ""));
                AlPortapapeles(cab + "\r\n" + string.Join("\r\n", g.Items.Cast<object>().Select(i => Fila(g, i))));
            });
            var cont = ContenedorDeFila(g.SelectedItem);
            if (cont.Length > 0 && AbrirContenedor != null)
            {
                menu.Items.Add(new Separator());
                Item($"Ver contenedor {cont} e historial (ZCON)", () => AbrirContenedor(cont));
            }
        };
    }

    /// <summary>Texto que se puede seleccionar y copiar (para códigos en las fichas).</summary>
    public static TextBox Seleccionable(string texto, double tam = 13, bool negrita = false, Brush? color = null) => new()
    {
        Text = texto, FontSize = tam, FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal, Foreground = color ?? Texto, IsReadOnly = true,
        BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), TextWrapping = TextWrapping.Wrap, Cursor = Cursors.IBeam,
    };

    public static JsonObject? Seleccion(DataGrid g) => (g.SelectedItem as DataRowView)?.Row["__json"] as JsonObject;
    public static List<JsonObject> Selecciones(DataGrid g) => g.SelectedItems.OfType<DataRowView>().Select(r => (JsonObject)r.Row["__json"]).ToList();

    public static ScrollViewer Desplazable(UIElement c) => new() { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    public static string Json(JsonNode? n) => n?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "";
}
