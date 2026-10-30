using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZIA – Reconocimiento de artículos por foto: estado del catálogo y procesamiento de las fotos de tradebox.</summary>
public sealed class IaPantalla : Pantalla
{
    public override string Codigo => "ZIA";
    public override string Titulo => "Reconocimiento por foto (IA)";
    protected override string[] Eventos => new[] { "ia" };
    readonly WrapPanel _kpis = new();
    readonly TextBlock _estado = T("", 13.5);
    readonly ComboBox _filtro = Combo(new[] { ("pendientes", "Sin identificar"), ("confirmadas", "Confirmadas (ya aprendidas)"), ("descartadas", "Descartadas"), ("todas", "Todas") });
    readonly WrapPanel _fotos = new();
    string _firma = "";

    protected override void Armar()
    {
        Arriba(Marco(T("El reconocimiento corre en esta PC, sin costo por consulta. La primera vez baja el modelo (≈90 MB) y procesa las fotos de tradebox de cada artículo. " +
                       "Desde el celular (solo administradores: menú \"Reconocer por foto\") se saca una foto y se elige entre los artículos más parecidos; " +
                       "cada confirmación se suma al catálogo, así que con el uso reconoce mejor.", 12.5, color: Info), InfoClaro, InfoClaro));
        var acc = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        acc.Children.Add(Boton("▶ Procesar artículos nuevos", async () => await Hacer(async () => { await C.Api.Post("/api/ia/indexar", new { }); await CargarSeguro(); }, "Procesamiento iniciado"), true));
        acc.Children.Add(Boton("⇣ Traer TODO el maestro de tradebox y procesar", async () =>
        {
            if (!Confirmar("Trae todos los artículos de tradebox y procesa sus fotos. Puede tardar varias horas la primera vez (sigue en segundo plano). ¿Seguir?")) return;
            await Hacer(async () => { await C.Api.Post("/api/ia/indexar", new { catalogo = true }); await CargarSeguro(); }, "Procesamiento iniciado");
        }));
        acc.Children.Add(Boton("■ Detener", async () => await Hacer(async () => { await C.Api.Post("/api/ia/indexar", new { detener = true }); await CargarSeguro(); }), fondo: Error));
        Arriba(acc);
        Arriba(_kpis);
        Arriba(Marco(_estado));
        var cab = Filtros();
        cab.Children.Add(Campo("Fotos consultadas desde el celular", _filtro, 240));
        cab.Children.Add(T("  Escribí el artículo correcto y \"Asignar\": la IA aprende con esa foto. Se puede corregir una ya confirmada.", 12, color: Tenue));
        Arriba(cab);
        _filtro.SelectionChanged += async (_, _) => await CargarSeguro();
        Centro(Desplazable(_fotos));
    }

    FrameworkElement Tarjeta(JsonObject f)
    {
        var foto = Str(f["foto"]);
        var s = new StackPanel { Width = 250 };
        s.Children.Add(MovimientosPantalla.Miniatura(C.Api, Str(f["url"]), 240));
        var estado = Str(f["estado"]);
        s.Children.Add(T(estado switch { "confirmada" => "✔ " + Str(f["articulo"]), "descartada" => "Descartada", "ninguno" => "Ninguno de los sugeridos", _ => "Sin respuesta" },
            13, true, estado == "confirmada" ? Ok : estado == "descartada" ? Tenue : Aviso, new Thickness(0, 4, 0, 0)));
        s.Children.Add(T($"{FechaHora(Str(f["creado"]))} · {Str(f["usuario"])}", 11.5, color: Tenue));
        var art = Caja(estado == "confirmada" ? Str(f["articulo"]) : "", true);
        var sug = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var c in (f["candidatos"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Take(3))
        {
            var cod = Str(c["articulo"]);
            var pct = (int)Math.Round((c["similitud"]?.GetValue<double>() ?? 0) * 100);
            var b = Boton($"{cod} {pct}%", () => art.Text = cod);
            b.ToolTip = Str(c["descripcion"]); b.Padding = new Thickness(6, 2, 6, 2); b.MinHeight = 22; b.FontSize = 11; b.Margin = new Thickness(0, 0, 4, 4);
            sug.Children.Add(b);
        }
        if (sug.Children.Count > 0) { s.Children.Add(T("Sugeridos (clic para elegir):", 11, color: Tenue, margen: new Thickness(0, 4, 0, 0))); s.Children.Add(sug); }
        s.Children.Add(Campo("Artículo correcto", art, 236));
        var acc = new WrapPanel();
        acc.Children.Add(Boton("Asignar", async () =>
        {
            if (art.Text.Trim().Length == 0) { C.Mensaje("Escribí el código de artículo.", true); return; }
            await Hacer(async () => { await C.Api.Post("/api/ia/confirmar", new { fotoId = foto, articulo = art.Text.Trim() }); await CargarSeguro(); }, $"Listo: la IA aprendió que esa foto es {art.Text.Trim().ToUpperInvariant()}");
        }, true));
        if (estado != "descartada")
            acc.Children.Add(Boton("Descartar", async () => await Hacer(async () => { await C.Api.Post("/api/ia/descartar", new { fotoId = foto }); await CargarSeguro(); }, "Foto descartada")));
        s.Children.Add(acc);
        art.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ((Button)acc.Children[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
        var m = Marco(s); m.Margin = new Thickness(0, 0, 10, 10);
        return m;
    }

    protected override async Task Cargar()
    {
        var e = await C.Api.Get("/api/ia/estado");
        _kpis.Children.Clear();
        _kpis.Children.Add(Kpi("Artículos en el catálogo", Str(e?["articulos"]), Ok));
        _kpis.Children.Add(Kpi("Fotos (huellas)", Str(e?["vectores"]), Marino2));
        _kpis.Children.Add(Kpi("Confirmadas en el depósito", Str(e?["confirmadas"]), Naranja));
        _kpis.Children.Add(Kpi("Fotos sin identificar", Str(e?["sinIdentificar"]), Aviso, () => _filtro.SelectedValue = "pendientes"));
        _kpis.Children.Add(Kpi("Pendientes de procesar", Str(e?["pendientes"]), Aviso));
        _kpis.Children.Add(Kpi("Sin foto en tradebox", Str(e?["sinFoto"]), Error));
        var modelo = e?["modelo"]?.GetValue<bool>() == true ? "✔ Modelo bajado" : "Modelo todavía no bajado (se baja solo al procesar o al primer reconocimiento)";
        var prog = Str(e?["progreso"]);
        var err = Str(e?["ultimoError"]);
        _estado.Text = modelo + "\n" + (e?["indexando"]?.GetValue<bool>() == true ? "⏳ " + (prog.Length > 0 ? prog : "Procesando…") : "En espera.") +
                       (err.Length > 0 ? "\n⛔ Último error: " + err : "") + $"\nArtículos conocidos por SONA: {Str(e?["conocidos"])}";
        var h = await C.Api.Get("/api/ia/historial?estado=" + Valor(_filtro));
        var lista = h?.AsArray().OfType<JsonObject>().ToList() ?? new();
        var firma = Valor(_filtro) + "|" + string.Join(",", lista.Select(f => Str(f["foto"]) + Str(f["estado"]) + Str(f["articulo"])));
        if (firma == _firma) return;   // no se rearma si no cambió nada (así no se pierde lo que se está escribiendo)
        _firma = firma;
        _fotos.Children.Clear();
        foreach (var f in lista) _fotos.Children.Add(Tarjeta(f));
        if (_fotos.Children.Count == 0) _fotos.Children.Add(T("No hay fotos en esta lista.", 13, color: Tenue));
    }
}
