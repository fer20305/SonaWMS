using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZART – Todos los artículos de una empresa (maestro de LES copiado en SONA), con buscador, habilitado y stock.</summary>
public sealed class ArticulosEmpresa : Pantalla
{
    public override string Codigo => "ZART";
    public override string Titulo => "Artículos por empresa";
    static readonly Col[] Cols =
    {
        new("Artículo", "articulo", "mono"), new("Descripción", "descripcion", Ancho: 320), new("Empresa", "empresaNombre"), new("Habilitado", "hab"),
        new("Stock físico", "stockFisico", "numero"), new("Disponible", "stockDisponible", "numero"), new("No disp.", "stockNoDisponible", "numero"),
        new("Reservado", "stockReservado", "numero"), new("Marca", "marca"),
    };
    readonly DataGrid _g = Grilla(Cols);
    readonly ComboBox _empresa = Combo(new[] { ("", "Todas las empresas") });
    readonly TextBox _texto = Caja();
    readonly CheckBox _conStock = new() { Content = "Solo con stock", Margin = new Thickness(10, 18, 10, 0) };
    readonly CheckBox _hab = new() { Content = "Solo habilitados", Margin = new Thickness(0, 18, 10, 0) };
    readonly TextBlock _info = T("", 12.5, color: Tenue);
    readonly System.Windows.Threading.DispatcherTimer _demora = new() { Interval = TimeSpan.FromMilliseconds(350) };
    bool _empresasCargadas;

    protected override void Armar()
    {
        var f = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        f.Children.Add(Campo("Empresa", _empresa, 280));
        f.Children.Add(Campo("Código, descripción o marca (todas las palabras)", _texto, 280));
        f.Children.Add(_conStock); f.Children.Add(_hab);
        var b = Boton("↻ Actualizar desde LES", async () => await Hacer(async () => { await C.Api.Post("/api/articulos/catalogo/actualizar", new { }); C.Mensaje("Actualizando el maestro de artículos desde LES…", false); await Task.Delay(1500); await Cargar(); }));
        b.VerticalAlignment = VerticalAlignment.Bottom; f.Children.Add(b);
        Arriba(f); Arriba(_info);
        _demora.Tick += async (_, _) => { _demora.Stop(); await CargarSeguro(); };
        _texto.TextChanged += (_, _) => { _demora.Stop(); _demora.Start(); };
        _empresa.SelectionChanged += async (_, _) => { if (_empresasCargadas) await CargarSeguro(); };
        _conStock.Click += async (_, _) => await CargarSeguro();
        _hab.Click += async (_, _) => await CargarSeguro();
        _g.MouseDoubleClick += (_, _) => { if (Seleccion(_g) is { } a) C.Abrir("ZCON", new() { ["codigo"] = Str(a["articulo"]) }); };
        Centro(_g);
    }

    protected override async Task Cargar()
    {
        var q = new List<string> { "limite=2000" };
        if (Valor(_empresa) is { Length: > 0 } e) q.Add("empresa=" + e);
        if (_texto.Text.Trim().Length > 0) q.Add("q=" + Uri.EscapeDataString(_texto.Text.Trim()));
        if (_conStock.IsChecked == true) q.Add("conStock=1");
        if (_hab.IsChecked == true) q.Add("habilitados=1");
        var r = await C.Api.Get("/api/articulos/catalogo?" + string.Join("&", q));
        if (!_empresasCargadas)
        {
            var elegida = Valor(_empresa);
            _empresa.Items.Clear();
            _empresa.Items.Add(Tuple.Create("", "Todas las empresas"));
            foreach (var x in r?["empresas"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                if (Str(x["empresa"]).Length > 0) _empresa.Items.Add(Tuple.Create(Str(x["empresa"]), $"{Str(x["nombre"])} ({Str(x["articulos"])})"));
            _empresa.SelectedIndex = 0;
            _empresasCargadas = (r?["empresas"]?.AsArray().Count ?? 0) > 0;
        }
        var filas = new JsonArray((r?["items"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>()).Select(a =>
        {
            var x = (JsonObject)a.DeepClone();
            x["hab"] = a["habilitado"]?.GetValue<bool>() == true ? "Sí" : "No";
            return (JsonNode)x;
        }).ToArray());
        _g.ItemsSource = Tabla(filas, Cols, "articulo").DefaultView;
        var est = r?["estado"];
        _info.Text = $"{Str(r?["total"])} artículo(s)" + (Str(r?["total"]) != Str(r?["mostrados"]) ? $" · se muestran {Str(r?["mostrados"])}" : "")
            + (est?["actualizando"]?.GetValue<bool>() == true ? $" · leyendo LES: {Str(est["leidos"])} artículos…" : "")
            + (Str(est?["error"]) is { Length: > 0 } er ? " · ⚠ " + er : "") + " · doble clic para consultar";
        if (est?["actualizando"]?.GetValue<bool>() == true) { await Task.Delay(3000); _empresasCargadas = false; await CargarSeguro(); }
    }
}
