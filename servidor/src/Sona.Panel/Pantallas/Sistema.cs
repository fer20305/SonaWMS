using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>
/// ZCFG – Configuración de plataformas: todas las direcciones que usa SONA Depósito en un solo lugar
/// (WMS local / externo, buscador SONA, LES tradebox, servidor para los celulares), con prueba de conexión.
/// </summary>
public sealed class ConfigPlataformas : Pantalla
{
    public override string Codigo => "ZCFG";
    public override string Titulo => "Configuración de plataformas";
    readonly TextBox _wmsLocal = Caja(), _wmsExt = Caja(), _wmsTimeout = Caja(), _lesUrl = Caja(), _lesAcc = Caja(), _buscador = Caja(), _lesUsr = Caja();
    readonly PasswordBox _lesClave = new();
    readonly TextBox _etmsUrl = Caja(), _etmsUsr = Caja();
    readonly PasswordBox _etmsClave = new();
    readonly TextBlock _etmsEstado = T("", 11.5, color: Tenue);
    readonly TextBox _puerto = Caja(), _dirLocal = Caja(), _dirExt = Caja(), _admins = Caja("", true), _layoutHoras = Caja();
    readonly TextBlock _carpeta = T("", 12, color: Tenue), _claveEstado = T("", 11.5, color: Tenue);
    readonly StackPanel _pruebas = new();

    protected override void Armar()
    {
        var s = new StackPanel { MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Seccion("WMS (handheld y web GeneXus)", "Se prueba primero la red local; si no responde, la IP externa.",
            Campo("Red local", _wmsLocal, 420), Campo("IP externa", _wmsExt, 420), Campo("Tiempo de espera (segundos)", _wmsTimeout, 120)));
        s.Children.Add(Seccion("Buscador SONA", "Detalle de contenedores y artículos (cliente, cantidad, posición).",
            Campo("Dirección del buscador", _buscador, 520)));
        s.Children.Add(Seccion("LES · tradebox (fotos de artículos)", "Usuario y clave de tradebox para traer las fotos. La clave se guarda cifrada.",
            Campo("LES", _lesUrl, 300), Campo("Cuentas (login)", _lesAcc, 300), Campo("Usuario tradebox", _lesUsr, 200), Campo("Clave tradebox (vacío = no cambiar)", _lesClave, 220), _claveEstado));
        s.Children.Add(Seccion("e-TMS (ingresos y egresos de artículos)", "Para ver a qué punto de venta salió cada artículo. Si el usuario queda vacío, se usa el usuario y la clave del WMS de quien consulta.",
            Campo("Dirección", _etmsUrl, 300), Campo("Usuario e-TMS (vacío = el del WMS)", _etmsUsr, 220), Campo("Clave e-TMS (vacío = no cambiar)", _etmsClave, 220), _etmsEstado));
        s.Children.Add(Seccion("Servidor SONA Depósito", "Puerto donde atiende a los celulares y las direcciones que se configuran en la app (Wi-Fi y datos móviles).",
            Campo("Puerto", _puerto, 100), Campo("Dirección para celulares (Wi-Fi)", _dirLocal, 300), Campo("Dirección para celulares (datos móviles)", _dirExt, 300),
            Campo("Administradores iniciales (separados por coma)", _admins, 300), Campo("Sincronizar layout cada (horas, 0 = nunca)", _layoutHoras, 120), _carpeta));
        var acc = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
        acc.Children.Add(Boton("💾 Guardar configuración", async () => await Guardar(), true));
        acc.Children.Add(Boton("🔌 Probar conexiones", async () => await Probar(), fondo: Marino));
        acc.Children.Add(Boton("Servidor: integrado / Docker…", async () => { if (new VentanaModo(C.Panel) { Owner = Window.GetWindow(this) }.ShowDialog() == true) C.Reiniciar(); }));
        s.Children.Add(acc);
        s.Children.Add(_pruebas);
        Centro(Desplazable(s));
        if (C.Api.Rol != "admin") s.IsEnabled = false;
    }

    static Border Seccion(string titulo, string ayuda, params UIElement[] campos)
    {
        var s = new StackPanel();
        s.Children.Add(T(titulo, 14, true, Marino));
        s.Children.Add(T(ayuda, 12, color: Tenue, margen: new Thickness(0, 2, 0, 8)));
        var w = new WrapPanel();
        foreach (var c in campos) w.Children.Add(c);
        s.Children.Add(w);
        return Marco(s);
    }

    protected override async Task Cargar()
    {
        var c = await C.Api.Get("/api/config");
        _wmsLocal.Text = Str(c?["wms"]?["local"]); _wmsExt.Text = Str(c?["wms"]?["externo"]); _wmsTimeout.Text = Str(c?["wms"]?["timeoutSegundos"]);
        _buscador.Text = Str(c?["les"]?["buscador"]); _lesUrl.Text = Str(c?["les"]?["url"]); _lesAcc.Text = Str(c?["les"]?["account"]); _lesUsr.Text = Str(c?["les"]?["usuario"]);
        _claveEstado.Text = c?["les"]?["claveCargada"]?.GetValue<bool>() == true ? "✔ Clave de tradebox cargada" : "Sin clave de tradebox: no se ven fotos";
        _etmsUrl.Text = Str(c?["etms"]?["url"]); _etmsUsr.Text = Str(c?["etms"]?["usuario"]);
        _etmsEstado.Text = c?["etms"]?["claveCargada"]?.GetValue<bool>() == true ? "✔ Clave del e-TMS cargada" : "Sin usuario propio: se usa el del WMS";
        _puerto.Text = Str(c?["puerto"]); _dirLocal.Text = Str(c?["servidor"]?["direccionLocal"]); _dirExt.Text = Str(c?["servidor"]?["direccionExterna"]);
        _admins.Text = string.Join(", ", c?["admins"]?.AsArray().Select(Str) ?? Enumerable.Empty<string>());
        _layoutHoras.Text = Str(c?["sincronizarLayoutHoras"]);
        _carpeta.Text = "Carpeta de datos del servidor: " + Str(c?["carpetaDatos"]);
    }

    async Task Guardar()
    {
        var cuerpo = new JsonObject
        {
            ["wms"] = new JsonObject { ["local"] = _wmsLocal.Text.Trim(), ["externo"] = _wmsExt.Text.Trim(), ["timeoutSegundos"] = int.TryParse(_wmsTimeout.Text, out var t) ? t : 25 },
            ["les"] = new JsonObject { ["url"] = _lesUrl.Text.Trim(), ["account"] = _lesAcc.Text.Trim(), ["buscador"] = _buscador.Text.Trim(), ["usuario"] = _lesUsr.Text.Trim(), ["clave"] = _lesClave.Password },
            ["etms"] = new JsonObject { ["url"] = _etmsUrl.Text.Trim(), ["usuario"] = _etmsUsr.Text.Trim(), ["clave"] = _etmsClave.Password },
            ["servidor"] = new JsonObject { ["direccionLocal"] = _dirLocal.Text.Trim(), ["direccionExterna"] = _dirExt.Text.Trim() },
            ["admins"] = new JsonArray(_admins.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
            ["sincronizarLayoutHoras"] = int.TryParse(_layoutHoras.Text, out var h) ? h : 12,
        };
        if (int.TryParse(_puerto.Text, out var p)) cuerpo["puerto"] = p;
        try
        {
            var r = await C.Api.Put("/api/config", cuerpo);
            _lesClave.Password = ""; _etmsClave.Password = "";
            C.Mensaje("Configuración guardada", false);
            await Cargar();
            if (r?["reiniciar"]?.GetValue<bool>() == true)
            {
                if (C.Integrado && MessageBox.Show("Cambió el puerto: hay que reiniciar el servidor. ¿Reiniciar ahora?", "SONA Depósito", MessageBoxButton.YesNo) == MessageBoxResult.Yes) C.Reiniciar();
                else if (!C.Integrado) MessageBox.Show("Cambió el puerto: reiniciá el contenedor (docker compose restart) y actualizá la dirección del panel.");
            }
        }
        catch (Exception e) { C.Mensaje(e.Message, true); }
    }

    async Task Probar()
    {
        _pruebas.Children.Clear();
        _pruebas.Children.Add(T("Probando conexiones…", 13, color: Tenue));
        try
        {
            var r = await C.Api.Post("/api/config/probar");
            _pruebas.Children.Clear();
            foreach (var p in r!.AsArray().OfType<JsonObject>())
            {
                var ok = p["ok"]?.GetValue<bool>() == true;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(T(ok ? "✔" : "✕", 16, true, ok ? Ok : Error));
                var n = T(Str(p["plataforma"]), 13, true); Grid.SetColumn(n, 1); g.Children.Add(n);
                var d = T($"{Str(p["direccion"])}\n{Str(p["detalle"])}", 12, color: Tenue); Grid.SetColumn(d, 2); g.Children.Add(d);
                var ms = T($"{Str(p["ms"])} ms", 11.5, color: Tenue); Grid.SetColumn(ms, 3); g.Children.Add(ms);
                _pruebas.Children.Add(Marco(g, ok ? OkClaro : ErrorClaro, ok ? Hex("#B9E2C5") : Hex("#F3C2C2")));
            }
        }
        catch (Exception e) { _pruebas.Children.Clear(); C.Mensaje(e.Message, true); }
    }
}

/// <summary>ZEST – Estado del servidor, sesiones en el WMS y celulares.</summary>
public sealed class Estado : Pantalla
{
    public override string Codigo => "ZEST";
    public override string Titulo => "Estado y conexiones";
    protected override string[] Eventos => new[] { "movimiento", "layout" };
    readonly WrapPanel _kpis = new();
    static readonly Col[] ColsSes = { new("Usuario", "usuario"), new("Red del WMS", "red", Ancho: 360) };
    static readonly Col[] ColsDisp = { new("Usuario", "usuario"), new("Modelo", "modelo"), new("App", "version_app"), new("Último contacto", "ultimo_contacto", "fecha"), new("Id", "id", "mono") };
    readonly DataGrid _ses = Grilla(ColsSes), _disp = Grilla(ColsDisp);
    readonly TextBlock _info = T("", 12.5, color: Tenue);

    protected override void Armar()
    {
        var s = new StackPanel();
        s.Children.Add(_kpis);
        s.Children.Add(_info);
        var acc = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        acc.Children.Add(Boton("💾 Respaldar base ahora", async () => await Hacer(async () => { var r = await C.Api.Post("/api/respaldo"); C.Mensaje("Respaldo: " + Str(r?["archivo"]), false); })));
        s.Children.Add(acc);
        s.Children.Add(T("Sesiones abiertas en el WMS", 13.5, true, Marino, new Thickness(0, 0, 0, 6)));
        _ses.Height = 180; s.Children.Add(_ses);
        s.Children.Add(T("Celulares", 13.5, true, Marino, new Thickness(0, 12, 0, 6)));
        _disp.Height = 260; s.Children.Add(_disp);
        Centro(Desplazable(s));
    }

    protected override async Task Cargar()
    {
        var e = await C.Api.Get("/api/estado");
        var m = await C.Api.Get("/api/monitor");
        _kpis.Children.Clear();
        _kpis.Children.Add(Kpi("Servidor v" + Str(e?["version"]), "OK", Ok));
        _kpis.Children.Add(Kpi("Cola hacia el WMS", Str(e?["pendientesWms"]), Aviso));
        _kpis.Children.Add(Kpi("Sesiones en el WMS", (e?["sesionesWms"]?.AsArray().Count ?? 0).ToString(), Marino));
        _kpis.Children.Add(Kpi("Equipos en vivo", Str(e?["conectados"]), Marino2));
        _info.Text = (C.Integrado ? "Servidor integrado en esta PC" : "Servidor remoto: " + C.Api.Base) + $" · celulares: {Str(e?["servidor"]?["local"])} (Wi-Fi) / {Str(e?["servidor"]?["externa"])} (datos)";
        var ses = new JsonArray((e?["sesionesWms"]?.AsArray() ?? new JsonArray()).Select(x =>
        {
            var o = x!.DeepClone().AsObject();
            var red = Str(o["red"]);
            o["red"] = red.Length == 0 ? "—" : (red.Contains("192.168") ? "Local · " : "Externa · ") + red;
            return (JsonNode)o;
        }).ToArray());
        _ses.ItemsSource = Tabla(ses, ColsSes, "usuario").DefaultView;
        _disp.ItemsSource = Tabla(m?["dispositivos"]?.AsArray(), ColsDisp).DefaultView;
    }
}

/// <summary>ZLOG – Registro en vivo del servidor integrado (cargas en el WMS, errores, sincronizaciones).</summary>
public sealed class Registro : Pantalla
{
    public override string Codigo => "ZLOG";
    public override string Titulo => "Registro del servidor";
    protected override void Armar()
    {
        var l = new ListBox { FontFamily = new FontFamily("Consolas"), FontSize = 12, BorderBrush = Linea, ItemsSource = C.Log?.Lineas };
        Centro(l);
    }
    protected override Task Cargar() => Task.CompletedTask;
}

/// <summary>ZAPP – App de los celulares: publicar una versión nueva para que los celulares se actualicen solos desde el servidor.</summary>
public sealed class AppCelulares : Pantalla
{
    public override string Codigo => "ZAPP";
    public override string Titulo => "App de los celulares";
    protected override string[] Eventos => new[] { "app_nueva" };
    readonly TextBlock _actual = T("", 15, true, Marino);
    readonly TextBlock _detalle = T("", 12.5, color: Tenue);
    readonly TextBox _version = Caja(), _notas = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 60, BorderBrush = Linea };
    readonly CheckBox _obligatoria = new() { Content = "Obligatoria (no dejan seguir usando la app hasta actualizar)", Margin = new Thickness(0, 4, 0, 6) };
    readonly TextBlock _archivo = T("Ningún archivo elegido", 12.5, color: Tenue);
    string? _ruta;
    static readonly Col[] Cols = { new("Usuario", "usuario"), new("Nombre", "nombre"), new("Rol", "rol", "etiqueta"), new("Celular", "modelo"), new("Versión", "version_app"), new("Estado", "estado", "estado"), new("Último contacto", "ultimo_contacto", "fecha") };
    readonly DataGrid _g = Grilla(Cols);
    string? _publicada;

    protected override void Armar()
    {
        Arriba(Marco(T("Subí el APK nuevo (SONA_Deposito_1.xx.apk) y los celulares lo ven al abrir la app: tocan \"Actualizar ahora\", la app se baja del servidor y Android la instala " +
                       "(la primera vez Android pide permitir \"instalar apps\" para SONA). Si dejás el APK junto al SonaDeposito.exe, el servidor también lo publica solo.", 12.5, color: Info), InfoClaro, InfoClaro));
        var s = new StackPanel();
        s.Children.Add(_actual); s.Children.Add(_detalle);
        var sub = new StackPanel();
        sub.Children.Add(T("Publicar una versión nueva", 13.5, true, Marino, new Thickness(0, 0, 0, 6)));
        var fila = new WrapPanel();
        fila.Children.Add(Boton("Elegir APK…", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "App de Android|*.apk" };
            if (d.ShowDialog() != true) return;
            _ruta = d.FileName;
            _archivo.Text = System.IO.Path.GetFileName(d.FileName) + $" · {new System.IO.FileInfo(d.FileName).Length / 1048576.0:0.0} MB";
            var m = System.Text.RegularExpressions.Regex.Match(System.IO.Path.GetFileNameWithoutExtension(d.FileName), @"\d+(\.\d+)+");
            if (m.Success) _version.Text = m.Value;
        }));
        fila.Children.Add(_archivo);
        sub.Children.Add(fila);
        sub.Children.Add(Campo("Versión", _version, 100));
        sub.Children.Add(Campo("Qué cambia (lo ven en el celular)", _notas, 420));
        sub.Children.Add(_obligatoria);
        sub.Children.Add(Boton("Publicar para los celulares", async () => await Publicar(), true));
        s.Children.Add(Marco(sub));
        s.Children.Add(T("Celulares y la versión que tienen", 13.5, true, Marino, new Thickness(0, 4, 0, 6)));
        _g.Height = 320; s.Children.Add(_g);
        Centro(Desplazable(s));
    }

    protected override async Task Cargar()
    {
        var v = await C.Api.Get("/api/app/version");
        _publicada = Str(v?["version"]);
        _actual.Text = _publicada.Length > 0 ? $"Publicada en el servidor: versión {_publicada}" : "Todavía no hay una versión publicada en el servidor";
        _detalle.Text = _publicada.Length > 0
            ? $"{(v?["tamano"] is JsonValue t && t.TryGetValue<long>(out var b) ? $"{b / 1048576.0:0.0} MB · " : "")}{FechaHora(Str(v?["fecha"]))}" +
              (v?["obligatoria"]?.GetValue<bool>() == true ? " · OBLIGATORIA" : "") + (Str(v?["notas"]) is { Length: > 0 } n ? $" · {n}" : "") + (Str(v?["subidaPor"]) is { Length: > 0 } p ? $" · subió {p}" : "")
            : "";
        var eq = await C.Api.Get("/api/app/equipos");
        var lista = new JsonArray();
        foreach (var e in eq?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var x = (JsonObject)e.DeepClone();
            var ver = Str(x["version_app"]);
            x["estado"] = _publicada.Length == 0 || ver.Length == 0 ? "" : Mayor(_publicada, ver) ? "pendiente" : "ok";
            lista.Add(x);
        }
        _g.ItemsSource = Tabla(lista, Cols).DefaultView;
    }

    static bool Mayor(string a, string b)
    {
        int[] P(string v) => System.Text.RegularExpressions.Regex.Matches(v, @"\d+").Select(m => int.Parse(m.Value)).ToArray();
        var x = P(a); var y = P(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++) { var p = i < x.Length ? x[i] : 0; var q = i < y.Length ? y[i] : 0; if (p != q) return p > q; }
        return false;
    }

    async Task Publicar()
    {
        if (_ruta == null) { C.Mensaje("Elegí el archivo APK.", true); return; }
        if (_version.Text.Trim().Length == 0) { C.Mensaje("Poné la versión (ej. 1.17).", true); return; }
        if (_publicada is { Length: > 0 } pub && !Mayor(_version.Text.Trim(), pub) && !Confirmar($"La versión {_version.Text.Trim()} no es mayor que la publicada ({pub}): los celulares no la van a ver como nueva. ¿Publicar igual?")) return;
        await Hacer(async () =>
        {
            C.Mensaje("Subiendo el APK…", false);
            await C.Api.SubirArchivo("/api/app/subir", _ruta, "apk", new()
            {
                ["version"] = _version.Text.Trim(), ["notas"] = _notas.Text.Trim(), ["obligatoria"] = _obligatoria.IsChecked == true ? "true" : "false",
            });
            _ruta = null; _archivo.Text = "Ningún archivo elegido"; _notas.Text = ""; _obligatoria.IsChecked = false;
            await Cargar();
        }, "App publicada: los celulares la ven al abrir la app.");
    }
}
