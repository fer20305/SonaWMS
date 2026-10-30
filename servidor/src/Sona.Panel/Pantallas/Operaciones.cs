using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>
/// Base de las operaciones del panel (las mismas de la app): formulario a la izquierda y, a la derecha, lo registrado en esta
/// pantalla con su estado en el WMS (se actualiza en vivo). Todo pasa por /api/movimientos, igual que desde el celular.
/// </summary>
public abstract class Operacion : Pantalla
{
    protected override string[] Eventos => new[] { "movimiento", "autorizacion" };
    static readonly Col[] ColsReg =
    {
        new("Hora", "hora"), new("Tipo", "tipo", "etiqueta"), new("Contenedor", "contenedor", "mono"), new("Detalle", "detalle", Ancho: 220),
        new("Estado", "estado", "estado"), new("Respuesta", "mensaje", Ancho: 320),
    };
    readonly DataGrid _reg = Grilla(ColsReg);
    readonly List<JsonObject> _registrados = new();
    protected readonly StackPanel Form = new() { Margin = new Thickness(0, 0, 14, 0) };
    protected readonly TextBlock Resultado = T("", 13, true, Marino, new Thickness(0, 8, 0, 0));

    protected abstract string Ayuda { get; }
    protected abstract void ArmarFormulario();

    protected override void Armar()
    {
        Arriba(Marco(T(Ayuda, 12.5, color: Info), InfoClaro, InfoClaro));
        ArmarFormulario();
        Form.Children.Add(Resultado);
        var der = new DockPanel();
        var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var bz = Boton("Ver en Movimientos (ZMOV)", () => C.Abrir("ZMOV", null)); DockPanel.SetDock(bz, Dock.Right); cab.Children.Add(bz);
        cab.Children.Add(T("Registrado desde esta pantalla", 13.5, true, Marino));
        DockPanel.SetDock(cab, Dock.Top); der.Children.Add(cab);
        _reg.MouseDoubleClick += (_, _) => { if (Seleccion(_reg) is { } o) C.Abrir("ZCON", new() { ["codigo"] = Str(o["contenedor"]) }); };
        der.Children.Add(_reg);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(470) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var izq = Desplazable(Form); g.Children.Add(izq);
        Grid.SetColumn(der, 1); g.Children.Add(der);
        Centro(g);
    }

    protected override async Task Cargar()
    {
        // estado en el WMS de lo registrado (aplicado / error / esperando autorización)
        foreach (var f in _registrados.Where(f => Str(f["estado"]) is "pendiente_wms" or "esperando_autorizacion" or "registrado"))
        {
            try
            {
                var m = await C.Api.Get($"/api/movimientos/{Str(f["id"])}");
                f["estado"] = Str(m?["estado"]);
                f["mensaje"] = Str(m?["respuesta_wms"]) is { Length: > 0 } rw ? rw : Str(m?["comentario"]);
            }
            catch { }
        }
        Mostrar();
    }

    void Mostrar() => _reg.ItemsSource = Tabla(new JsonArray(_registrados.Select(f => (JsonNode)f.DeepClone()).ToArray()), ColsReg).DefaultView;

    /// <summary>Datos del contenedor (artículo, empresa, cantidad, posición).</summary>
    protected async Task<JsonObject?> InfoCont(string contenedor)
    {
        var r = await C.Api.Post("/api/contenedores/info", new { contenedores = new[] { contenedor.Trim().ToUpperInvariant() } });
        return r?.AsArray().OfType<JsonObject>().FirstOrDefault();
    }

    protected static string Resumen(JsonObject? i) => i == null || i["encontrado"]?.GetValue<bool>() != true ? "✖ No figura en stock" :
        $"{Str(i["articulo"])} · {Str(i["descripcion"])}\n{Str(i["empresaNombre"])} · {Str(i["cantidad"])} u. · en {(Str(i["posicion"]) is { Length: > 0 } p ? p : "—")}" +
        (i["disponible"]?.GetValue<bool>() == false ? " · NO DISPONIBLE" : "");

    /// <summary>Manda los movimientos y los agrega a la grilla de la derecha.</summary>
    protected async Task<List<JsonObject>> Registrar(List<JsonObject> movs, string detalle)
    {
        var r = await C.Api.Post("/api/movimientos", new JsonObject { ["movimientos"] = new JsonArray(movs.Select(m => (JsonNode)m.DeepClone()).ToArray()) });
        var res = r?["resultados"]?.AsArray().OfType<JsonObject>().ToList() ?? new();
        foreach (var m in movs)
        {
            var x = res.FirstOrDefault(y => Str(y["id"]) == Str(m["id"]));
            _registrados.Insert(0, new JsonObject
            {
                ["id"] = Str(m["id"]), ["hora"] = DateTime.Now.ToString("HH:mm:ss"), ["tipo"] = Str(m["tipo"]), ["contenedor"] = Str(m["contenedor"]),
                ["detalle"] = detalle, ["estado"] = Str(x?["estado"]) is { Length: > 0 } e ? e : "registrado", ["mensaje"] = Str(x?["mensaje"]),
            });
        }
        Mostrar();
        var rech = res.Count(x => Str(x["estado"]) == "rechazado");
        var esp = res.Count(x => Str(x["estado"]) == "esperando_autorizacion");
        Resultado.Foreground = rech > 0 ? Error : Ok;
        Resultado.Text = rech > 0 ? $"✖ {rech} rechazado(s): {string.Join(" · ", res.Where(x => Str(x["estado"]) == "rechazado").Select(x => Str(x["mensaje"])).Distinct())}"
            : esp > 0 ? $"⏳ {esp} esperando autorización de zona (ZAUT)" : $"✔ {movs.Count} movimiento(s) registrados: se cargan en el WMS en segundo plano.";
        return res;
    }

    protected static JsonObject Mov(string tipo, string contenedor, string? grupo = null) => new()
    {
        ["id"] = Guid.NewGuid().ToString(), ["grupo"] = grupo ?? Guid.NewGuid().ToString(), ["tipo"] = tipo, ["contenedor"] = contenedor.Trim().ToUpperInvariant(),
    };

    protected static List<string> Codigos(string texto) => Regex.Split(texto.ToUpperInvariant(), @"[\s,;]+").Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();

    protected static TextBox Lista(double alto = 150) => new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, CharacterCasing = CharacterCasing.Upper, Height = alto, MinWidth = 220,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 13, BorderBrush = Linea,
    };

    protected static WrapPanel Fila(params UIElement[] e) { var w = new WrapPanel(); foreach (var x in e) w.Children.Add(x); return w; }
    protected void Titulo2(string t) => Form.Children.Add(T(t, 13.5, true, Marino, new Thickness(0, 8, 0, 6)));
    protected static int? Entero(TextBox t) => int.TryParse(t.Text.Trim(), out var n) ? n : null;
    protected static double? Numero(TextBox t) => double.TryParse(t.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>ZTRF – Transferir uno o varios contenedores (o una posición completa) a otra posición, con sugerencia y control de destino.</summary>
public sealed class Transferir : Operacion
{
    public override string Codigo => "ZTRF";
    public override string Titulo => "Transferir";
    protected override string Ayuda => "Igual que en la app: cargá los contenedores (o traé los de una posición), pedí sugerencia o escribí el destino, controlalo y transferí. " +
                                       "Si el destino es zona de otro cliente pide autorización (ZAUT), salvo que marques multicontenedor.";
    readonly TextBox _conts = Lista(), _origen = Caja("", true), _origenNiv = Caja(), _destino = Caja("", true), _nivel = Caja();
    readonly CheckBox _multi = new() { Content = "Multicontenedor (distintas empresas a propósito)", Margin = new Thickness(0, 4, 0, 4) };
    readonly CheckBox _completa = new() { Content = "Posición completa (al terminar mueve lo que haya quedado en el origen)", Margin = new Thickness(0, 0, 0, 4) };
    readonly StackPanel _sug = new(), _val = new();
    bool _forzar;

    protected override void ArmarFormulario()
    {
        Titulo2("1 · Qué se mueve");
        Form.Children.Add(Campo("Contenedores (uno por línea, o pegados)", _conts, 300));
        Form.Children.Add(Fila(Campo("o traer los de la posición", _origen, 130), Campo("Nivel", _origenNiv, 60),
            Boton("Traer contenedores", async () => await TraerPosicion())));
        Titulo2("2 · A dónde");
        Form.Children.Add(Fila(Campo("Destino (Ubic.Ctrl o posición)", _destino, 160), Campo("Nivel", _nivel, 60), Boton("Sugerir destino", async () => await Sugerir())));
        Form.Children.Add(_sug);
        Form.Children.Add(_multi); Form.Children.Add(_completa);
        Form.Children.Add(Fila(Boton("Controlar destino", async () => await Validar(), fondo: Marino), Boton("Transferir", async () => await Hacer(Mandar), true)));
        Form.Children.Add(_val);
        _destino.TextChanged += (_, _) => _forzar = false;
    }

    async Task TraerPosicion()
    {
        if (_origen.Text.Trim().Length == 0) return;
        await Hacer(async () =>
        {
            var r = await C.Api.Get($"/api/posicion/{Uri.EscapeDataString(_origen.Text.Trim())}" + (Entero(_origenNiv) is int n ? $"?nivel={n}" : ""));
            var conts = new List<string>();
            foreach (var nv in r?["niveles"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                foreach (var c in nv["contenedores"]?.AsArray() ?? new JsonArray())
                    conts.Add(c is JsonObject o ? Str(o["contenedor"] ?? o["codigo"]) : Str(c));
            conts = conts.Where(x => x.Length > 0).Distinct().ToList();
            _conts.Text = string.Join(Environment.NewLine, conts);
            _completa.IsChecked = conts.Count > 0 && Entero(_origenNiv) != null;
            C.Mensaje($"{conts.Count} contenedor(es) en {_origen.Text.Trim()}", false);
        });
    }

    async Task Sugerir()
    {
        var cs = Codigos(_conts.Text);
        if (cs.Count == 0) { C.Mensaje("Cargá al menos un contenedor.", true); return; }
        await Hacer(async () =>
        {
            var r = await C.Api.Post("/api/sugerencia", new { contenedores = cs, origen = _origen.Text.Trim().Length > 0 ? _origen.Text.Trim() : null, cuantas = 6 });
            _sug.Children.Clear();
            var l = r?["sugerencias"]?.AsArray().OfType<JsonObject>().ToList() ?? new();
            if (l.Count == 0) { _sug.Children.Add(T("Sin sugerencias para esta carga.", 12, color: Tenue)); return; }
            _sug.Children.Add(T("Sugerencias (clic para usar):", 12, color: Tenue));
            foreach (var s in l)
            {
                var b = Boton($"{Str(s["legible"])} nivel {Str(s["nivel"])} · {Str(s["motivo"])}" + (s["libres"] != null ? $" · {Str(s["libres"])} libres" : ""), () => { });
                b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 4);
                b.Click += (_, _) => { _destino.Text = Str(s["ubicCtrl"]) is { Length: > 0 } u ? u : Str(s["codigo"]); _nivel.Text = Str(s["nivel"]); };
                _sug.Children.Add(b);
            }
        });
    }

    async Task<bool> Validar()
    {
        var cs = Codigos(_conts.Text);
        if (cs.Count == 0 || _destino.Text.Trim().Length == 0) { C.Mensaje("Faltan contenedores o destino.", true); return false; }
        var ok = false;
        await Hacer(async () =>
        {
            var v = await C.Api.Post("/api/validar", new { contenedores = cs, origen = _origen.Text.Trim().Length > 0 ? _origen.Text.Trim() : null, destino = _destino.Text.Trim(), nivel = Entero(_nivel), multi = _multi.IsChecked == true });
            _val.Children.Clear();
            ok = v?["ok"]?.GetValue<bool>() == true;
            foreach (var b in v?["bloqueos"]?.AsArray() ?? new JsonArray()) _val.Children.Add(T("⛔ " + Str(b), 12.5, true, Error));
            foreach (var f in v?["forzables"]?.AsArray() ?? new JsonArray()) _val.Children.Add(T("⚠ " + Str(f) + " (se puede transferir igual)", 12.5, color: Aviso));
            foreach (var a in v?["avisos"]?.AsArray() ?? new JsonArray()) _val.Children.Add(T("• " + Str(a), 12.5, color: Tenue));
            if (Str(v?["autorizacion"]) is { Length: > 0 } au) _val.Children.Add(T("🔐 Pide autorización: " + au, 12.5, true, Aviso));
            if (ok) _val.Children.Add(T("✔ Destino válido" + (v?["destino"] is JsonObject d ? $": {Str(d["legible"])} nivel {Str(d["nivel"])}" : ""), 12.5, true, Ok));
            _forzar = !ok && (v?["bloqueos"]?.AsArray().Count ?? 0) == 0 && (v?["forzables"]?.AsArray().Count ?? 0) > 0;
        });
        return ok || _forzar;
    }

    async Task Mandar()
    {
        var cs = Codigos(_conts.Text);
        if (!await Validar()) { C.Mensaje("El destino no pasó el control.", true); return; }
        if (!Confirmar($"¿Transferir {cs.Count} contenedor(es) a {_destino.Text.Trim()}" + (Entero(_nivel) is int n ? $" nivel {n}" : "") + "?")) return;
        var grupo = Guid.NewGuid().ToString();
        var movs = cs.Select(c =>
        {
            var m = Mov("transferencia", c, grupo);
            m["destino"] = _destino.Text.Trim(); m["nivel"] = Entero(_nivel); m["multi"] = _multi.IsChecked == true;
            if (_origen.Text.Trim().Length > 0) m["origen"] = _origen.Text.Trim();
            if (_completa.IsChecked == true) m["completa"] = true;
            m["comentario"] = "Desde el panel";
            return m;
        }).ToList();
        await Registrar(movs, $"→ {_destino.Text.Trim()} N{_nivel.Text.Trim()}");
        _conts.Text = ""; _sug.Children.Clear(); _val.Children.Clear();
    }
}

/// <summary>ZGUA – Guardar: recepciones pendientes de guardar, con destino sugerido.</summary>
public sealed class Guardar : Operacion
{
    public override string Codigo => "ZGUA";
    public override string Titulo => "Guardar recepciones";
    protected override string Ayuda => "Contenedores recibidos que todavía no se guardaron. Elegí uno, pedí la sugerencia o escribí el destino y guardalo.";
    static readonly Col[] Cols = { new("Contenedor", "contenedor", "mono"), new("Artículo", "articulo", "mono"), new("Descripción", "descripcion", Ancho: 180), new("Empresa", "empresaNombre"), new("Cant.", "cantidad", "numero"), new("Recepción", "recepcion") };
    readonly DataGrid _g = Grilla(Cols);
    readonly TextBox _destino = Caja("", true), _nivel = Caja();
    readonly StackPanel _sug = new();

    protected override void ArmarFormulario()
    {
        Form.Children.Add(Fila(Boton("↻ Leer pendientes del WMS", async () => await Leer(), fondo: Marino)));
        _g.Height = 260; Form.Children.Add(_g);
        _g.SelectionChanged += async (_, _) => await Sugerir();
        Form.Children.Add(Fila(Campo("Destino", _destino, 150), Campo("Nivel", _nivel, 60)));
        Form.Children.Add(_sug);
        Form.Children.Add(Fila(Boton("Guardar", async () => await Hacer(Mandar), true)));
    }

    async Task Leer() => await Hacer(async () =>
    {
        var l = await C.Api.Get("/api/guardado/pendientes");
        var ok = new JsonArray((l?.AsArray().OfType<JsonObject>().Where(x => x["contenedor"] != null).Select(x => (JsonNode)x.DeepClone()) ?? Enumerable.Empty<JsonNode>()).ToArray());
        _g.ItemsSource = Tabla(ok, Cols, "contenedor").DefaultView;
        var err = l?.AsArray().OfType<JsonObject>().Where(x => x["error"] != null).Select(x => Str(x["error"])).ToList() ?? new();
        Resultado.Foreground = err.Count > 0 ? Error : Marino;
        Resultado.Text = $"{ok.Count} pendiente(s) de guardar" + (err.Count > 0 ? " · " + string.Join(" · ", err) : "");
    });

    async Task Sugerir()
    {
        if (Seleccion(_g) is not { } o) return;
        _sug.Children.Clear();
        await Hacer(async () =>
        {
            var r = await C.Api.Post("/api/sugerencia", new { contenedores = new[] { Str(o["contenedor"]) }, cuantas = 4 });
            foreach (var s in r?["sugerencias"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                var b = Boton($"{Str(s["legible"])} nivel {Str(s["nivel"])} · {Str(s["motivo"])}", () => { });
                b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 4);
                b.Click += (_, _) => { _destino.Text = Str(s["ubicCtrl"]) is { Length: > 0 } u ? u : Str(s["codigo"]); _nivel.Text = Str(s["nivel"]); };
                _sug.Children.Add(b);
            }
        });
    }

    async Task Mandar()
    {
        if (Seleccion(_g) is not { } o) { C.Mensaje("Elegí un contenedor de la lista.", true); return; }
        if (_destino.Text.Trim().Length == 0) { C.Mensaje("Falta el destino.", true); return; }
        var m = Mov("guardado", Str(o["contenedor"]));
        m["destino"] = _destino.Text.Trim(); m["nivel"] = Entero(_nivel); m["comentario"] = "Desde el panel";
        await Registrar(new() { m }, $"guardado → {_destino.Text.Trim()} N{_nivel.Text.Trim()}");
        _destino.Text = ""; _nivel.Text = ""; _sug.Children.Clear();
    }
}

/// <summary>ZCAJ – Reubicar cajas: pasar unidades de un contenedor a otro del mismo artículo.</summary>
public sealed class ReubicarCajas : Operacion
{
    public override string Codigo => "ZCAJ";
    public override string Titulo => "Reubicar cajas";
    protected override string Ayuda => "Pasar cajas (unidades) de un contenedor a otro del mismo artículo y empresa. \"Buscar destinos\" muestra los otros contenedores del artículo.";
    readonly TextBox _origen = Caja("", true), _destino = Caja("", true), _cant = Caja(), _posDest = Caja("", true), _nivel = Caja();
    readonly TextBlock _info = T("", 12.5, color: Tenue);
    static readonly Col[] Cols = { new("Contenedor", "contenedor", "mono"), new("Posición", "posicion", "mono"), new("Cant.", "cantidad", "numero") };
    readonly DataGrid _g = Grilla(Cols);

    protected override void ArmarFormulario()
    {
        Form.Children.Add(Fila(Campo("Contenedor origen", _origen, 150), Boton("Ver", async () => await Hacer(async () => _info.Text = Resumen(await InfoCont(_origen.Text)))),
            Boton("Buscar destinos", async () => await Destinos())));
        Form.Children.Add(_info);
        _g.Height = 160; _g.Margin = new Thickness(0, 6, 0, 6); Form.Children.Add(_g);
        _g.SelectionChanged += (_, _) => { if (Seleccion(_g) is { } o) { _destino.Text = Str(o["contenedor"]); _posDest.Text = Str(o["posicion"]); } };
        Form.Children.Add(Fila(Campo("Contenedor destino", _destino, 150), Campo("Cantidad a pasar", _cant, 90)));
        Form.Children.Add(Fila(Campo("Dónde está el destino (opcional)", _posDest, 150), Campo("Nivel", _nivel, 60)));
        Form.Children.Add(Fila(Boton("Reubicar", async () => await Hacer(Mandar), true)));
    }

    async Task Destinos() => await Hacer(async () =>
    {
        _info.Text = Resumen(await InfoCont(_origen.Text));
        var r = await C.Api.Get($"/api/cajas/destinos/{Uri.EscapeDataString(_origen.Text.Trim())}");
        _g.ItemsSource = Tabla(r?["destinos"]?.AsArray(), Cols, "contenedor").DefaultView;
    });

    async Task Mandar()
    {
        if (_origen.Text.Trim().Length == 0 || _destino.Text.Trim().Length == 0 || Numero(_cant) is not > 0) { C.Mensaje("Completá origen, destino y cantidad.", true); return; }
        if (!Confirmar($"¿Pasar {Numero(_cant)} de {_origen.Text.Trim()} a {_destino.Text.Trim()}?")) return;
        var m = Mov("reubicar_cajas", _origen.Text);
        m["destino"] = _destino.Text.Trim().ToUpperInvariant(); m["cantidad"] = Numero(_cant); m["comentario"] = "Desde el panel";
        if (_posDest.Text.Trim().Length > 0) { m["posicion_destino"] = _posDest.Text.Trim(); m["nivel"] = Entero(_nivel); }
        await Registrar(new() { m }, $"{Numero(_cant)} u. → {_destino.Text.Trim()}");
        _cant.Text = "";
    }
}

/// <summary>ZAJU – Ajustar (sumar o restar unidades de un contenedor) con motivo.</summary>
public sealed class Ajustar : Operacion
{
    public override string Codigo => "ZAJU";
    public override string Titulo => "Ajustar contenedor";
    protected override string Ayuda => "Sumar o restar unidades de un contenedor (analistas y administradores). Queda registrado con el motivo.";
    readonly TextBox _cont = Caja("", true), _cant = Caja(), _motivo = Caja();
    readonly ComboBox _signo = Combo(new[] { ("restar", "Restar"), ("sumar", "Sumar") });
    readonly TextBlock _info = T("", 12.5, color: Tenue);

    protected override void ArmarFormulario()
    {
        Form.Children.Add(Fila(Campo("Contenedor", _cont, 150), Boton("Ver", async () => await Hacer(async () => _info.Text = Resumen(await InfoCont(_cont.Text))))));
        Form.Children.Add(_info);
        Form.Children.Add(Fila(Campo("Operación", _signo, 110), Campo("Cantidad", _cant, 90)));
        Form.Children.Add(Campo("Motivo", _motivo, 320));
        Form.Children.Add(Fila(Boton("Ajustar", async () => await Hacer(Mandar), true)));
    }

    async Task Mandar()
    {
        if (_cont.Text.Trim().Length == 0 || Numero(_cant) is not > 0 || _motivo.Text.Trim().Length == 0) { C.Mensaje("Completá contenedor, cantidad y motivo.", true); return; }
        var sumar = Valor(_signo) == "sumar";
        if (!Confirmar($"¿{(sumar ? "Sumar" : "Restar")} {Numero(_cant)} al contenedor {_cont.Text.Trim()}?")) return;
        var m = Mov("ajuste", _cont.Text);
        m["cantidad"] = Numero(_cant); m["sumar"] = sumar; m["motivo"] = _motivo.Text.Trim(); m["comentario"] = "Desde el panel";
        await Registrar(new() { m }, $"{(sumar ? "+" : "−")}{Numero(_cant)} · {_motivo.Text.Trim()}");
        _cant.Text = ""; _info.Text = "";
    }
}

/// <summary>ZCES – Cambiar estado (disponible / no disponible con motivo y foto).</summary>
public sealed class CambiarEstado : Operacion
{
    public override string Codigo => "ZCES";
    public override string Titulo => "Cambiar estado";
    protected override string Ayuda => "Marcar contenedores como no disponibles (rotura, no conforme, cuarentena…) o volverlos a disponible. Para rotura y no conforme la foto es obligatoria.";
    static readonly string[] Motivos = { "ROTURA", "NO CONFORME", "INCOMPLETO", "CUARENTENA", "OBSOLETO", "SCRAP", "DEPOSITO EXTERNO" };
    readonly TextBox _conts = Lista(110), _coment = Caja();
    readonly ComboBox _estado = Combo(new[] { ("no", "No disponible"), ("si", "Disponible") });
    readonly ComboBox _motivo = Combo(Motivos.Select(m => (m, m)));
    readonly List<string> _fotos = new();
    readonly TextBlock _fotosT = T("Sin fotos", 12, color: Tenue);

    protected override void ArmarFormulario()
    {
        Form.Children.Add(Campo("Contenedores", _conts, 300));
        Form.Children.Add(Fila(Campo("Estado", _estado, 140), Campo("Motivo", _motivo, 170)));
        _estado.SelectionChanged += (_, _) => _motivo.IsEnabled = Valor(_estado) == "no";
        Form.Children.Add(Campo("Comentario", _coment, 320));
        Form.Children.Add(Fila(Boton("📷 Agregar fotos…", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Fotos|*.jpg;*.jpeg;*.png", Multiselect = true };
            if (d.ShowDialog() == true) { _fotos.AddRange(d.FileNames); _fotosT.Text = $"{_fotos.Count} foto(s)"; }
        }), Boton("Quitar fotos", () => { _fotos.Clear(); _fotosT.Text = "Sin fotos"; }), _fotosT));
        Form.Children.Add(Fila(Boton("Cambiar estado", async () => await Hacer(Mandar), true)));
    }

    async Task Mandar()
    {
        var cs = Codigos(_conts.Text);
        if (cs.Count == 0) { C.Mensaje("Cargá al menos un contenedor.", true); return; }
        var disp = Valor(_estado) == "si";
        if (!Confirmar($"¿Marcar {cs.Count} contenedor(es) como {(disp ? "DISPONIBLE" : "NO DISPONIBLE – " + Valor(_motivo))}?")) return;
        var ids = new List<string>();
        foreach (var f in _fotos) { var r = await C.Api.Subir("/api/fotos", new[] { f }); if (Str(r?["id"]) is { Length: > 0 } id) ids.Add(id); }
        var grupo = Guid.NewGuid().ToString();
        var movs = cs.Select(c =>
        {
            var m = Mov("cambio_estado", c, grupo);
            m["disponible"] = disp; if (!disp) m["motivo"] = Valor(_motivo);
            m["comentario"] = _coment.Text.Trim().Length > 0 ? _coment.Text.Trim() : "Desde el panel";
            if (ids.Count > 0) m["fotos"] = new JsonArray(ids.Select(i => (JsonNode)i).ToArray());
            return m;
        }).ToList();
        await Registrar(movs, disp ? "DISPONIBLE" : "NO DISPONIBLE – " + Valor(_motivo));
        _conts.Text = ""; _fotos.Clear(); _fotosT.Text = "Sin fotos";
    }
}

/// <summary>ZALT – Crear contenedor: número del LES + carga inicial en el WMS.</summary>
public sealed class CrearContenedor : Operacion
{
    public override string Codigo => "ZALT";
    public override string Titulo => "Crear contenedor";
    protected override string Ayuda => "El LES da el número de contenedor y SONA lo carga en el WMS (carga inicial) en la posición y nivel, con la cantidad. Si ya tenés el número, ponelo para reintentar solo la carga.";
    readonly TextBox _art = Caja("", true), _cant = Caja(), _pos = Caja("", true), _nivel = Caja(), _cont = Caja("", true);
    readonly ComboBox _pref = Combo(new[] { ("PP", "PP"), ("PC", "PC"), ("BULTO", "Bulto") });

    protected override void ArmarFormulario()
    {
        Form.Children.Add(Fila(Campo("Artículo", _art, 150), Campo("Cantidad", _cant, 90)));
        Form.Children.Add(Fila(Campo("Posición", _pos, 150), Campo("Nivel", _nivel, 60), Campo("Tipo", _pref, 90)));
        Form.Children.Add(Campo("Contenedor (solo para reintentar)", _cont, 150));
        Form.Children.Add(Fila(Boton("Crear contenedor", async () => await Hacer(Mandar), true)));
    }

    async Task Mandar()
    {
        if (_art.Text.Trim().Length == 0 || Numero(_cant) is not > 0 || _pos.Text.Trim().Length == 0) { C.Mensaje("Completá artículo, cantidad y posición.", true); return; }
        var r = await C.Api.Post("/api/contenedores/crear", new
        {
            articulo = _art.Text.Trim(), cantidad = Numero(_cant), posicion = _pos.Text.Trim(), nivel = Entero(_nivel), prefijo = Valor(_pref),
            contenedor = _cont.Text.Trim().Length > 0 ? _cont.Text.Trim() : null,
        });
        var ok = r?["ok"]?.GetValue<bool>() == true;
        Resultado.Foreground = ok ? Ok : Error;
        Resultado.Text = (ok ? "✔ " : "✖ ") + Str(r?["mensaje"]);
        if (Str(r?["contenedor"]) is { Length: > 0 } c)
        {
            _cont.Text = ok ? "" : c;
            if (ok && Confirmar($"Contenedor {c} creado. ¿Abrir la etiqueta para imprimirla?"))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"{C.Api.Base}/api/etiquetas/{Uri.EscapeDataString(c)}?token={Uri.EscapeDataString(C.Api.Token)}") { UseShellExecute = true });
        }
    }
}

/// <summary>ZELI – Eliminar contenedor (lo ajusta a 0 en el WMS). Solo administradores.</summary>
public sealed class EliminarContenedor : Operacion
{
    public override string Codigo => "ZELI";
    public override string Titulo => "Eliminar contenedor";
    protected override string Ayuda => "Ajusta el contenedor a 0 en el WMS (resta todo lo que tiene). Solo administradores. Queda registrado con el motivo.";
    readonly TextBox _conts = Lista(110), _motivo = Caja();

    protected override void ArmarFormulario()
    {
        Form.Children.Add(Campo("Contenedores", _conts, 300));
        Form.Children.Add(Campo("Motivo", _motivo, 320));
        Form.Children.Add(Fila(Boton("Eliminar", async () => await Hacer(Mandar), fondo: Error)));
    }

    async Task Mandar()
    {
        var cs = Codigos(_conts.Text);
        if (cs.Count == 0 || _motivo.Text.Trim().Length == 0) { C.Mensaje("Cargá los contenedores y el motivo.", true); return; }
        if (!Confirmar($"¿ELIMINAR {cs.Count} contenedor(es)? Se ajustan a 0 en el WMS.\n{string.Join(", ", cs.Take(10))}{(cs.Count > 10 ? "…" : "")}")) return;
        var grupo = Guid.NewGuid().ToString();
        var movs = cs.Select(c => { var m = Mov("ajuste", c, grupo); m["motivo"] = _motivo.Text.Trim(); m["comentario"] = "Eliminado desde el panel"; return m; }).ToList();
        await Registrar(movs, "eliminar · " + _motivo.Text.Trim());
        _conts.Text = "";
    }
}
