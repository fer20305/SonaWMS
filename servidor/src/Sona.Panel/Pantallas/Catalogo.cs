using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZCAT – Catálogo de fotos: subir fotos propias a un artículo (por su código) para los que no tienen imagen en tradebox/e-TMS.</summary>
public sealed class Catalogo : Pantalla
{
    public override string Codigo => "ZCAT";
    public override string Titulo => "Catálogo de fotos de artículos";
    readonly TextBox _art = Caja("", true);
    readonly StackPanel _res = new();

    protected override void Armar()
    {
        Arriba(Marco(T("Las fotos se asocian al CÓDIGO del artículo (nunca por parecido). Las propias se muestran primero en la app (picking, consultas) y también le enseñan a la IA de reconocimiento.", 12.5, color: Info), InfoClaro, InfoClaro));
        var f = Filtros();
        f.Children.Add(Campo("Código de artículo", _art, 220));
        var b = Boton("Buscar", async () => await CargarSeguro(), true); b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 8, 6);
        f.Children.Add(b);
        var s = Boton("📷 Subir fotos…", async () => await Subir(), fondo: Marino); s.VerticalAlignment = VerticalAlignment.Bottom; s.Margin = new Thickness(0, 0, 8, 6);
        f.Children.Add(s);
        _art.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await CargarSeguro(); };
        Arriba(Marco(f, FilaPar, Linea2));
        Centro(Desplazable(_res));
        if (Parametros.TryGetValue("articulo", out var a)) _art.Text = a;
    }

    protected override async Task Cargar()
    {
        _res.Children.Clear();
        var art = _art.Text.Trim();
        if (art.Length == 0) return;
        var r = await C.Api.Get($"/api/catalogo/{Uri.EscapeDataString(art)}");
        _res.Children.Add(T($"Fotos propias de {Str(r?["articulo"])}", 14, true, Marino, new Thickness(0, 0, 0, 6)));
        var w = new WrapPanel();
        foreach (var f in r?["propias"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var st = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
            st.Children.Add(MovimientosPantalla.Miniatura(C.Api, Str(f["url"]), 160));
            st.Children.Add(T($"{Str(f["subidoPor"])} · {FechaHora(Str(f["fecha"]))}", 11, color: Tenue));
            var id = Str(f["id"]);
            st.Children.Add(Boton("Borrar", async () => { if (Confirmar("¿Borrar esta foto del catálogo?")) await Hacer(async () => { await C.Api.Delete($"/api/catalogo/fotos/{id}"); await CargarSeguro(); }, "Foto borrada"); }, fondo: Error));
            w.Children.Add(st);
        }
        if (w.Children.Count == 0) w.Children.Add(T("Todavía no tiene fotos propias.", 12.5, color: Tenue));
        _res.Children.Add(Marco(w));
        _res.Children.Add(T("Fotos de tradebox / e-TMS", 14, true, Marino, new Thickness(0, 10, 0, 6)));
        var we = new WrapPanel();
        foreach (var u in r?["externas"]?.AsArray() ?? new JsonArray()) we.Children.Add(MovimientosPantalla.Miniatura(C.Api, Str(u), 140));
        if (we.Children.Count == 0) we.Children.Add(T("IMAGEN NO DISPONIBLE en tradebox ni en el e-TMS.", 12.5, true, Aviso));
        _res.Children.Add(Marco(we));
    }

    async Task Subir()
    {
        var art = _art.Text.Trim();
        if (art.Length == 0) { C.Mensaje("Escribí el código del artículo.", true); return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Imágenes|*.jpg;*.jpeg;*.png", Title = $"Fotos para {art.ToUpperInvariant()}" };
        if (dlg.ShowDialog() != true) return;
        await Hacer(async () => { await C.Api.Subir($"/api/catalogo/{Uri.EscapeDataString(art)}/fotos", dlg.FileNames); await CargarSeguro(); }, $"{dlg.FileNames.Length} foto(s) agregadas a {art.ToUpperInvariant()}");
    }
}
