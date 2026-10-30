using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZNN – Transferir un lote de contenedores (pegados desde el Bloc de notas) a la posición NN.</summary>
public sealed class TransferirNn : Pantalla
{
    public override string Codigo => "ZNN";
    public override string Titulo => "Transferir lote a NN";
    static string _ultimoDestino = "5422", _ultimoNivel = "1";

    readonly TextBox _lista = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, CharacterCasing = CharacterCasing.Upper, Height = 260, Width = 230,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 13,
    };
    readonly TextBox _destino = Caja(_ultimoDestino), _nivel = Caja(_ultimoNivel);
    readonly TextBlock _resumen = T("", 13, true, Marino);
    static readonly Col[] Cols =
    {
        new("Contenedor", "contenedor", "mono"), new("En stock", "ok"), new("Artículo", "articulo", "mono"), new("Descripción", "descripcion", Ancho: 220),
        new("Empresa", "empresaNombre"), new("Cantidad", "cantidad", "numero"), new("Posición actual", "posicion", "mono"), new("Estado del movimiento", "estado", "estado"), new("Respuesta del WMS", "respuesta", Ancho: 300),
    };
    readonly DataGrid _g = Grilla(Cols, true);
    List<JsonObject> _filas = new();
    readonly Button _transferir;

    public TransferirNn() { _transferir = Boton("2 · Transferir a NN", async () => await Transferir(), true); _transferir.IsEnabled = false; }

    protected override void Armar()
    {
        Arriba(Marco(T("Pegá los contenedores (uno por línea, o separados por coma, espacio o tab, como los tenés en el Bloc de notas). " +
                       "Primero se controla la lista y después se mandan todas las transferencias juntas al WMS. No pide autorización de zona.", 12.5, color: Info), InfoClaro, InfoClaro));
        var izq = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        izq.Children.Add(Campo("Contenedores (Ctrl+V)", _lista, 230));
        var dest = new WrapPanel();
        dest.Children.Add(Campo("Destino (Ubic.Ctrl o posición)", _destino, 140));
        dest.Children.Add(Campo("Nivel", _nivel, 60));
        izq.Children.Add(dest);
        var acc = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        acc.Children.Add(Boton("1 · Controlar lista", async () => await Controlar(), fondo: Marino));
        acc.Children.Add(_transferir);
        izq.Children.Add(acc);
        acc = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        acc.Children.Add(Boton("↻ Ver cómo van en el WMS", async () => await Estados()));
        acc.Children.Add(Boton("Limpiar", () => { _lista.Text = ""; _filas.Clear(); _g.ItemsSource = null; _resumen.Text = ""; _transferir.IsEnabled = false; }));
        izq.Children.Add(acc);
        var der = new DockPanel();
        DockPanel.SetDock(_resumen, Dock.Top); _resumen.Margin = new Thickness(0, 0, 0, 6);
        der.Children.Add(_resumen); der.Children.Add(_g);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(izq); Grid.SetColumn(der, 1); g.Children.Add(der);
        Centro(g);
    }

    protected override Task Cargar() => Task.CompletedTask;

    List<string> Codigos() => Regex.Split(_lista.Text.ToUpperInvariant(), @"[\s,;]+").Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();

    async Task Controlar()
    {
        var cods = Codigos();
        if (cods.Count == 0) { C.Mensaje("Pegá al menos un contenedor.", true); return; }
        _resumen.Text = $"Controlando {cods.Count} contenedores…";
        _transferir.IsEnabled = false;
        await Hacer(async () =>
        {
            var r = await C.Api.Post("/api/contenedores/info", new { contenedores = cods });
            _filas = r?.AsArray().OfType<JsonObject>().ToList() ?? new();
            foreach (var f in _filas) f["ok"] = f["encontrado"]?.GetValue<bool>() == true ? "✔" : "✖ no figura";
            Mostrar();
            var ok = _filas.Count(f => f["encontrado"]?.GetValue<bool>() == true);
            _resumen.Text = $"{_filas.Count} contenedores · {ok} en stock · {_filas.Count - ok} no figuran (no se transfieren)";
            _transferir.Content = $"2 · Transferir {ok} a NN ({_destino.Text.Trim()} nivel {_nivel.Text.Trim()})";
            _transferir.IsEnabled = ok > 0;
        });
    }

    void Mostrar() => _g.ItemsSource = Tabla(new JsonArray(_filas.Select(f => (JsonNode)f.DeepClone()).ToArray()), Cols, "contenedor").DefaultView;

    async Task Transferir()
    {
        var destino = _destino.Text.Trim();
        if (destino.Length == 0 || !int.TryParse(_nivel.Text.Trim(), out var nivel)) { C.Mensaje("Falta el destino o el nivel.", true); return; }
        var ok = _filas.Where(f => f["encontrado"]?.GetValue<bool>() == true && f["id"] == null).ToList();
        if (ok.Count == 0) return;
        if (!Confirmar($"¿Transferir {ok.Count} contenedores a {destino} nivel {nivel}?\nSe cargan en el WMS uno por uno.")) return;
        _ultimoDestino = destino; _ultimoNivel = nivel.ToString();
        var grupo = Guid.NewGuid().ToString();
        foreach (var f in ok) f["id"] = Guid.NewGuid().ToString();
        await Hacer(async () =>
        {
            var movs = ok.Select(f => new { id = Str(f["id"]), grupo, tipo = "transferencia", contenedor = Str(f["contenedor"]), destino, nivel, multi = true, comentario = "Lote a NN desde el panel" }).ToList();
            var r = await C.Api.Post("/api/movimientos", new { movimientos = movs });
            var res = r?["resultados"]?.AsArray().OfType<JsonObject>().ToDictionary(x => Str(x["id"]), x => x) ?? new();
            foreach (var f in ok)
                if (res.TryGetValue(Str(f["id"]), out var x)) { f["estado"] = Str(x["estado"]); f["respuesta"] = Str(x["mensaje"]); }
            Mostrar();
            _transferir.IsEnabled = false;
            _resumen.Text = $"{ok.Count} transferencias registradas. Se cargan en el WMS en segundo plano: tocá \"Ver cómo van en el WMS\".";
        }, $"{ok.Count} transferencias a NN registradas");
    }

    async Task Estados()
    {
        var con = _filas.Where(f => f["id"] != null).ToList();
        if (con.Count == 0) return;
        await Hacer(async () =>
        {
            foreach (var f in con)
            {
                var m = await C.Api.Get($"/api/movimientos/{Str(f["id"])}");
                f["estado"] = Str(m?["estado"]); f["respuesta"] = Str(m?["respuesta_wms"]) is { Length: > 0 } rw ? rw : Str(m?["comentario"]);
            }
            Mostrar();
            var aplicados = con.Count(f => Str(f["estado"]) == "aplicado");
            var errores = con.Count(f => Str(f["estado"]) is "error" or "rechazado");
            _resumen.Text = $"{aplicados} de {con.Count} cargados en el WMS" + (errores > 0 ? $" · {errores} con error (ver respuesta; se reintentan desde ZMOV)" : "");
        });
    }
}
