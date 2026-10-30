using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZMSG – Mensajes: chat con cada operario y avisos a todos los celulares.</summary>
public sealed class Chat : Pantalla
{
    public override string Codigo => "ZMSG";
    public override string Titulo => "Mensajes";

    readonly ListBox _convs = new() { BorderThickness = new Thickness(0), Background = Blanco, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly StackPanel _hilo = new() { Margin = new Thickness(6) };
    readonly ScrollViewer _scroll;
    readonly TextBox _texto = new() { AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, MinHeight = 40, MaxHeight = 120, FontSize = 14, Padding = new Thickness(8, 6, 8, 6), BorderBrush = Linea, VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock _cabecera = T("Elegí una conversación", 15, true, Marino);
    string? _actual;
    bool _cargandoLista;

    public Chat() { _scroll = Desplazable(_hilo); }

    protected override void Armar()
    {
        Arriba(Marco(T("Cada operario escribe desde el celular (menú Mensajes) y acá lo ven todos los analistas. \"📢 A todos\" manda un aviso a todos los celulares. Enter envía.", 12.5, color: Info), InfoClaro, InfoClaro));
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var izq = new Border { Child = _convs, BorderBrush = Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = Blanco };
        g.Children.Add(izq);

        var der = new DockPanel { LastChildFill = true };
        var cab = new Border { Child = _cabecera, Padding = new Thickness(12, 8, 12, 8), Background = FilaPar, BorderBrush = Linea, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(cab, Dock.Top); der.Children.Add(cab);
        var pie = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
        var enviar = Boton("Enviar ➤", async () => await Enviar(), true);
        enviar.Height = 40; enviar.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(enviar, Dock.Right); pie.Children.Add(enviar); pie.Children.Add(_texto);
        DockPanel.SetDock(pie, Dock.Bottom); der.Children.Add(pie);
        der.Children.Add(_scroll);
        var marcoDer = new Border { Child = der, BorderBrush = Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = Fondo };
        Grid.SetColumn(marcoDer, 2); g.Children.Add(marcoDer);
        Centro(g);

        _texto.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { e.Handled = true; await Enviar(); }
        };
        _convs.SelectionChanged += async (_, _) =>
        {
            if (_cargandoLista || _convs.SelectedItem is not ListBoxItem { Tag: string conv }) return;
            _actual = conv;
            await CargarHilo();
        };
        if (Parametros.TryGetValue("con", out var c)) _actual = c;
    }

    protected override async Task Cargar()
    {
        await CargarLista();
        if (_actual != null) await CargarHilo();
    }

    async Task CargarLista()
    {
        var arr = (await C.Api.Get("/api/mensajes/conversaciones"))?.AsArray() ?? new JsonArray();
        _cargandoLista = true;
        try
        {
            _convs.Items.Clear();
            foreach (var cv in arr.OfType<JsonObject>())
            {
                var conv = Str(cv["conversacion"]);
                var todos = conv == "*";
                var sinLeer = int.TryParse(Str(cv["sin_leer"]), out var n) ? n : 0;
                var fila = new DockPanel { Margin = new Thickness(4, 6, 4, 6), LastChildFill = true };
                if (sinLeer > 0)
                {
                    var b = new Border { Background = Naranja, CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 7, 1), VerticalAlignment = VerticalAlignment.Center, Child = T(sinLeer.ToString(), 11.5, true, Brushes.White) };
                    DockPanel.SetDock(b, Dock.Right); fila.Children.Add(b);
                }
                var s = new StackPanel();
                var nombre = Str(cv["nombre"]);
                s.Children.Add(T(todos ? "📢 A todos los celulares" : (nombre.Length > 0 ? $"{nombre} ({conv})" : conv), 13, true, todos ? Naranja : Marino));
                var ult = Str(cv["ultimo"]);
                s.Children.Add(T(ult.Length > 0 ? (ult.Length > 48 ? ult[..48] + "…" : ult) + "  ·  " + FechaHora(Str(cv["hora"])) : (todos ? "Aviso para todos" : Et(Str(cv["rol"]))), 11.5, color: Tenue));
                fila.Children.Add(s);
                var item = new ListBoxItem { Content = fila, Tag = conv, Padding = new Thickness(2), BorderBrush = Linea, BorderThickness = new Thickness(0, 0, 0, 1) };
                if (sinLeer > 0) item.Background = NaranjaClaro;
                _convs.Items.Add(item);
                if (conv == _actual) _convs.SelectedItem = item;
            }
        }
        finally { _cargandoLista = false; }
    }

    async Task CargarHilo()
    {
        if (_actual == null) return;
        _cabecera.Text = _actual == "*" ? "📢 Aviso a todos los celulares" : $"Conversación con {_actual}";
        var arr = (await C.Api.Get("/api/mensajes?con=" + Uri.EscapeDataString(_actual)))?.AsArray() ?? new JsonArray();
        _hilo.Children.Clear();
        if (arr.Count == 0) _hilo.Children.Add(T("Todavía no hay mensajes. Escribí abajo.", 13, color: Tenue, margen: new Thickness(10)));
        foreach (var m in arr.OfType<JsonObject>())
        {
            var propio = m["desde_panel"]?.GetValue<bool>() == true;
            var burbuja = new StackPanel();
            var de = Str(m["nombre"]) is { Length: > 0 } nn ? nn : Str(m["de"]);
            burbuja.Children.Add(T($"{de}  ·  {FechaHora(Str(m["hora"]))}", 11, color: propio ? Hex("#FFE7D4") : Tenue));
            burbuja.Children.Add(T(Str(m["texto"]), 14, color: propio ? Brushes.White : Texto, margen: new Thickness(0, 2, 0, 0)));
            _hilo.Children.Add(new Border
            {
                Child = burbuja, Background = propio ? Marino2 : Blanco, BorderBrush = propio ? Marino2 : Linea, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 8), Margin = new Thickness(propio ? 80 : 0, 4, propio ? 0 : 80, 4),
                HorizontalAlignment = propio ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 620,
            });
        }
        _scroll.ScrollToEnd();
        if (_actual != "*")
        {
            try { await C.Api.Post("/api/mensajes/leido", new { con = _actual }); } catch { }
        }
        _texto.Focus();
    }

    async Task Enviar()
    {
        var t = _texto.Text.Trim();
        if (t.Length == 0) return;
        if (_actual == null) { C.Mensaje("Elegí a quién escribirle", true); return; }
        await Hacer(async () =>
        {
            await C.Api.Post("/api/mensajes", new { para = _actual, texto = t });
            _texto.Text = "";
            await CargarHilo();
            await CargarLista();
        });
    }

    /// <summary>Mensaje nuevo en vivo: si es de la conversación abierta se recarga el hilo; siempre se actualiza la lista.</summary>
    public void AlMensaje(JsonElement e)
    {
        var conv = e.TryGetProperty("conversacion", out var c) ? c.GetString() : null;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (conv != null && conv == _actual && IsVisible) await CargarHilo();
                await CargarLista();
            }
            catch { }
        });
    }
}
