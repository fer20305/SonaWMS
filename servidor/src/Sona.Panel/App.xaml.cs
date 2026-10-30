using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;

namespace Sona.Panel;

/// <summary>
/// Arranque del panel. En modo "integrado" este mismo programa es el servidor (API para celulares, base, cola al WMS);
/// en modo "remoto" se conecta a un servidor que corre en Docker u otra PC.
/// </summary>
public partial class App : Application
{
    static Mutex? _unico;
    public WebApplication? Servidor { get; private set; }
    public Configuracion? ConfigServidor { get; private set; }
    public LogMemoria Log { get; } = new();
    public PanelConfig Panel { get; private set; } = PanelConfig.Cargar();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _unico = new Mutex(true, "SONA_Deposito_Panel", out var primero);
        if (!primero)
        {
            MessageBox.Show("SONA Depósito ya está abierto (mirá en la bandeja del sistema, al lado del reloj).", "SONA Depósito", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, a) => { MessageBox.Show(a.Exception.Message, "SONA Depósito – error", MessageBoxButton.OK, MessageBoxImage.Error); a.Handled = true; };
        Log.Despachador = Dispatcher;
        var minimizado = e.Args.Contains("--minimizado");
        await Arrancar(minimizado);
    }

    public async Task Arrancar(bool minimizado = false)
    {
        string baseUrl;
        if (Panel.Modo == "integrado")
        {
            try
            {
                ConfigServidor = Configuracion.Cargar();
                Servidor = Motor.Crear(ConfigServidor, null, Log);
                await Servidor.StartAsync();
                baseUrl = $"http://localhost:{ConfigServidor.Puerto}";
            }
            catch (Exception ex)
            {
                var r = MessageBox.Show($"No se pudo iniciar el servidor integrado:\n\n{ex.Message}\n\n¿Querés abrir la configuración del panel para conectarte a un servidor remoto (Docker)?",
                    "SONA Depósito", MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (r == MessageBoxResult.Yes) { new VentanaModo(Panel).ShowDialog(); await Reiniciar(); return; }
                Shutdown(); return;
            }
        }
        else baseUrl = Panel.ServidorRemoto;

        var api = new ApiCliente();
        api.Servidor(baseUrl);
        // Reconexión automática con el usuario guardado
        var ok = false;
        if (Panel.Recordar && Panel.Usuario.Length > 0 && Panel.Clave.Length > 0)
        {
            try { await api.Ingresar(Panel.Usuario, Panel.Clave); ok = true; } catch { }
        }
        if (!ok)
        {
            var login = new VentanaLogin(api, Panel, Panel.Modo == "integrado");
            if (login.ShowDialog() != true)
            {
                if (login.CambioModo) { await Reiniciar(); return; }
                await Detener(); Shutdown(); return;
            }
        }
        var w = new VentanaPrincipal(api, Panel, ConfigServidor, Log, Panel.Modo == "integrado");
        MainWindow = w;
        if (minimizado && Panel.MinimizarABandeja) w.MostrarEnBandeja(); else w.Show();
    }

    public async Task Detener()
    {
        if (Servidor != null) { try { await Servidor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token); } catch { } await Servidor.DisposeAsync(); Servidor = null; }
    }

    /// <summary>Reinicia el servidor integrado y vuelve a abrir el panel (después de cambiar configuración o modo).</summary>
    public async Task Reiniciar()
    {
        var anterior = MainWindow;
        await Detener();
        Panel = PanelConfig.Cargar();
        (anterior as VentanaPrincipal)?.CerrarDefinitivo();
        await Arrancar();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Task.Run(Detener).Wait(TimeSpan.FromSeconds(6)); } catch { }
        base.OnExit(e);
    }
}
