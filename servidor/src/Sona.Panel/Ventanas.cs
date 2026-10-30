using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sona.Panel;

/// <summary>Ingreso con el usuario y la clave del WMS (los mismos de SONA Depósito).</summary>
public sealed class VentanaLogin : Window
{
    public bool CambioModo { get; private set; }

    public VentanaLogin(ApiCliente api, PanelConfig cfg, bool integrado)
    {
        Title = "SONA Depósito – Ingreso";
        Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Marino;
        var s = new StackPanel { Margin = new Thickness(30) };
        var logo = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        logo.Children.Add(new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(12), Background = Ui.Marino, Child = new TextBlock { Text = "S", FontSize = 38, FontWeight = FontWeights.Black, Foreground = Ui.Naranja, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        var tl = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        tl.Children.Add(Ui.T("SONA", 26, true, Ui.Marino));
        tl.Children.Add(Ui.T("Depósito · Panel de control", 12, color: Ui.Tenue));
        logo.Children.Add(tl);
        s.Children.Add(logo);
        s.Children.Add(Ui.T(integrado ? "Servidor integrado en esta PC" : "Servidor: " + api.Base, 11.5, color: Ui.Tenue, margen: new Thickness(0, 4, 0, 16)));
        var usuario = Ui.Caja(cfg.Usuario, true);
        var clave = new PasswordBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0) };
        s.Children.Add(Ui.Campo("Usuario (el del WMS)", usuario, 340));
        s.Children.Add(Ui.Campo("Clave", clave, 340));
        var recordar = new CheckBox { Content = "Mantener la sesión en esta PC (reconecta solo)", IsChecked = cfg.Recordar, Margin = new Thickness(0, 4, 0, 10), Foreground = Ui.Tenue };
        s.Children.Add(recordar);
        var error = Ui.T("", 12.5, true, Ui.Error, new Thickness(0, 0, 0, 10));
        s.Children.Add(error);
        Button entrar = null!;
        entrar = Ui.Boton("Ingresar", async () =>
        {
            entrar.IsEnabled = false; error.Text = "";
            entrar.Content = "Validando con el WMS…";
            try
            {
                await api.Ingresar(usuario.Text.Trim(), clave.Password);
                cfg.Usuario = usuario.Text.Trim(); cfg.Recordar = recordar.IsChecked == true;
                cfg.Clave = cfg.Recordar ? clave.Password : "";
                cfg.Guardar();
                DialogResult = true;
            }
            catch (Exception e) { error.Text = e.Message; }
            finally { entrar.IsEnabled = true; entrar.Content = "Ingresar"; }
        }, true);
        entrar.Height = 40; entrar.IsDefault = true; entrar.Margin = new Thickness(0);
        s.Children.Add(entrar);
        var modo = new Button { Content = "Cambiar servidor (integrado / Docker)…", Margin = new Thickness(0, 12, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Marino2, Cursor = System.Windows.Input.Cursors.Hand };
        modo.Click += (_, _) =>
        {
            if (new VentanaModo(cfg).ShowDialog() == true) { CambioModo = true; DialogResult = false; }
        };
        s.Children.Add(modo);
        Content = new Border { Background = Ui.Blanco, CornerRadius = new CornerRadius(10), Margin = new Thickness(16), Child = s };
        Loaded += (_, _) => { if (usuario.Text.Length > 0) clave.Focus(); else usuario.Focus(); };
    }
}

/// <summary>Dónde corre el servidor: en este programa (integrado) o en Docker / otra PC (remoto).</summary>
public sealed class VentanaModo : Window
{
    public VentanaModo(PanelConfig cfg)
    {
        Title = "Servidor de SONA Depósito"; Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var s = new StackPanel { Margin = new Thickness(20) };
        s.Children.Add(Ui.T("¿Dónde corre el servidor?", 16, true, Ui.Marino, new Thickness(0, 0, 0, 12)));
        var integrado = new RadioButton { Content = "En esta PC, dentro del panel (integrado)", IsChecked = cfg.Modo == "integrado", Margin = new Thickness(0, 0, 0, 4), FontWeight = FontWeights.SemiBold };
        s.Children.Add(integrado);
        s.Children.Add(Ui.T("Este programa atiende a los celulares, guarda la base y carga los movimientos en el WMS. Tiene que quedar abierto (se minimiza a la bandeja).", 12, color: Ui.Tenue, margen: new Thickness(22, 0, 0, 12)));
        var remoto = new RadioButton { Content = "En Docker u otra PC (remoto)", IsChecked = cfg.Modo == "remoto", Margin = new Thickness(0, 0, 0, 4), FontWeight = FontWeights.SemiBold };
        s.Children.Add(remoto);
        var url = Ui.Caja(cfg.ServidorRemoto);
        var campo = Ui.Campo("Dirección del servidor (ej. http://10.0.50.207:8080)", url, 440);
        ((FrameworkElement)campo).Margin = new Thickness(22, 4, 0, 12);
        s.Children.Add(campo);
        var inicio = new CheckBox { Content = "Abrir SONA Depósito al iniciar Windows", IsChecked = cfg.IniciarConWindows, Margin = new Thickness(0, 4, 0, 4) };
        var bandeja = new CheckBox { Content = "Al cerrar la ventana, seguir en la bandeja (el servidor sigue atendiendo)", IsChecked = cfg.MinimizarABandeja, Margin = new Thickness(0, 0, 0, 14) };
        s.Children.Add(inicio); s.Children.Add(bandeja);
        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        botones.Children.Add(Ui.Boton("Cancelar", () => DialogResult = false));
        botones.Children.Add(Ui.Boton("Guardar", () =>
        {
            cfg.Modo = remoto.IsChecked == true ? "remoto" : "integrado";
            cfg.ServidorRemoto = url.Text.Trim().TrimEnd('/');
            cfg.IniciarConWindows = inicio.IsChecked == true; cfg.MinimizarABandeja = bandeja.IsChecked == true;
            cfg.Guardar(); cfg.AplicarInicioWindows();
            DialogResult = true;
        }, true));
        s.Children.Add(botones);
        Content = s;
    }
}
