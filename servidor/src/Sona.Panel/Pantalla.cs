using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace Sona.Panel;

/// <summary>Lo que cada pantalla necesita del panel: la API, la barra de estado y abrir otras transacciones.</summary>
public sealed class Contexto
{
    public required ApiCliente Api { get; init; }
    public required Action<string, bool> Mensaje { get; init; }
    public required Action<string, Dictionary<string, string>?> Abrir { get; init; }
    public bool Integrado { get; init; }
    public Configuracion? ConfigLocal { get; init; }
    public LogMemoria? Log { get; init; }
    public required PanelConfig Panel { get; init; }
    public required Action Reiniciar { get; init; }
}

/// <summary>Base de las transacciones (ZMOV, ZAUT, …): título, carga de datos y recarga por eventos en vivo.</summary>
public abstract class Pantalla : UserControl
{
    protected Contexto C = null!;
    protected Dictionary<string, string> Parametros = new();
    public abstract string Codigo { get; }
    public abstract string Titulo { get; }
    /// <summary>Tipos de evento del servidor que hacen recargar la pantalla.</summary>
    protected virtual string[] Eventos => Array.Empty<string>();
    readonly DispatcherTimer _demora = new() { Interval = TimeSpan.FromMilliseconds(500) };

    protected readonly DockPanel Raiz = new() { LastChildFill = true, Margin = new Thickness(16, 12, 16, 12) };

    public void Iniciar(Contexto c, Dictionary<string, string>? parametros)
    {
        C = c;
        Parametros = parametros ?? new();
        Background = Ui.Blanco;
        var cab = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        cab.Children.Add(Ui.T(Titulo, 18, true, Ui.Marino));
        cab.Children.Add(new Border { Child = Ui.T(Codigo, 10.5, color: Ui.Tenue), BorderBrush = Ui.Linea, BorderThickness = new Thickness(1), Background = Ui.Fondo,
            Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(10, 0, 0, 0), CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(cab, Dock.Top);
        Raiz.Children.Add(cab);
        Content = Raiz;
        Armar();
        _demora.Tick += async (_, _) => { _demora.Stop(); await CargarSeguro(); };
        Loaded += async (_, _) => await CargarSeguro();
    }

    /// <summary>Arma los controles (una vez).</summary>
    protected abstract void Armar();
    /// <summary>Trae los datos del servidor.</summary>
    protected abstract Task Cargar();

    bool _primera = true;
    public async Task CargarSeguro()
    {
        if (!_primera && !IsVisible) return;
        _primera = false;
        try { await Cargar(); }
        catch (Exception e) { C.Mensaje(e.Message, true); }
    }

    public void AlEvento(JsonElement e)
    {
        if (e.TryGetProperty("tipo", out var t) && Eventos.Contains(t.GetString())) { _demora.Stop(); _demora.Start(); }
    }

    protected void Arriba(UIElement e) { DockPanel.SetDock(e, Dock.Top); Raiz.Children.Insert(Raiz.Children.Count, e); }
    protected void Abajo(UIElement e) { DockPanel.SetDock(e, Dock.Bottom); Raiz.Children.Add(e); }
    protected void Centro(UIElement e) => Raiz.Children.Add(e);

    protected async Task Hacer(Func<Task> accion, string? ok = null)
    {
        try { await accion(); if (ok != null) C.Mensaje(ok, false); }
        catch (Exception e) { C.Mensaje(e.Message, true); }
    }

    protected static bool Confirmar(string texto) => MessageBox.Show(texto, "SONA Depósito", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

/// <summary>Registro del servidor integrado en memoria (pantalla ZLOG).</summary>
public sealed class LogMemoria : ILoggerProvider
{
    public ObservableCollection<string> Lineas { get; } = new();
    public Dispatcher? Despachador { get; set; }
    public ILogger CreateLogger(string categoria) => new Registrador(this, categoria);
    public void Dispose() { }

    void Agregar(string linea)
    {
        void Hacer() { Lineas.Insert(0, linea); while (Lineas.Count > 2000) Lineas.RemoveAt(Lineas.Count - 1); }
        if (Despachador == null) return;
        Despachador.BeginInvoke(Hacer);
    }

    sealed class Registrador(LogMemoria m, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel nivel) => nivel >= LogLevel.Information && (!categoria.StartsWith("Microsoft") || nivel >= LogLevel.Warning);
        public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? e, Func<TState, Exception?, string> formato)
        {
            if (!IsEnabled(nivel)) return;
            var cat = categoria.Contains('.') ? categoria[(categoria.LastIndexOf('.') + 1)..] : categoria;
            m.Agregar($"{DateTime.Now:HH:mm:ss}  {nivel.ToString()[..4].ToUpper()}  {cat}: {formato(estado, e)}{(e != null ? " · " + e.Message : "")}");
        }
    }
}
