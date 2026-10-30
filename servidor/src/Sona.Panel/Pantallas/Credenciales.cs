using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using static Sona.Panel.Ui;

namespace Sona.Panel.Pantallas;

/// <summary>ZCRE – Mis credenciales: cada usuario carga una vez sus usuarios y claves de las otras plataformas (quedan cifradas en el servidor).</summary>
public sealed class MisCredencialesPantalla : Pantalla
{
    public override string Codigo => "ZCRE";
    public override string Titulo => "Mis credenciales";
    readonly StackPanel _cuerpo = new();

    protected override void Armar()
    {
        Arriba(Marco(T("Cargá una sola vez tu usuario y clave de cada plataforma: SONA entra con ellos en tu nombre (panel, módulo de Ingreso y la app). " +
                       "Quedan cifrados en el servidor; nadie los ve, ni los administradores. Antes de guardar, SONA prueba que entren.", 12.5, color: Info), InfoClaro, InfoClaro));
        Centro(Desplazable(_cuerpo));
    }

    protected override async Task Cargar()
    {
        var d = await C.Api.Get("/api/yo/credenciales");
        Pintar(d);
    }

    void Pintar(JsonNode? d)
    {
        _cuerpo.Children.Clear();
        var yo = Str(d?["usuario"]);
        foreach (var s in d?["sistemas"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var cod = Str(s["codigo"]);
            var cargada = s["cargada"]?.GetValue<bool>() == true;
            var usaWms = s["usaWms"]?.GetValue<bool>() == true;
            var editable = s["editable"]?.GetValue<bool>() == true;
            var panel = new StackPanel();
            var cab = new WrapPanel();
            cab.Children.Add(T(Str(s["nombre"]), 14.5, true, Marino, new Thickness(0, 0, 10, 0)));
            cab.Children.Add(T(usaWms ? "usa tu usuario del WMS" : cargada ? "cargada" : "sin cargar", 12, true, usaWms || cargada ? Ok : Error, new Thickness(0, 2, 10, 0)));
            if (s["ok"] is JsonValue okv && okv.TryGetValue<bool>(out var ok))
                cab.Children.Add(T((ok ? "✔ probada " : "✖ no entró ") + FechaHora(Str(s["probado"])), 12, false, ok ? Ok : Error, new Thickness(0, 2, 0, 0)));
            panel.Children.Add(cab);
            panel.Children.Add(T(Str(s["descripcion"]), 12.5, color: Tenue, margen: new Thickness(0, 4, 0, 6)));
            if (s["ok"]?.ToString() == "false" && Str(s["mensaje"]) is { Length: > 0 } m) panel.Children.Add(T(m, 12.5, color: Error, margen: new Thickness(0, 0, 0, 6)));
            var fila = new WrapPanel();
            fila.Children.Add(T("Usuario: " + (Str(s["usuario"]) is { Length: > 0 } u ? u : usaWms ? yo : "—"), 13, true, null, new Thickness(0, 6, 12, 0)));
            if (editable)
            {
                var cu = Caja(Str(s["usuario"]) is { Length: > 0 } u2 ? u2 : yo);
                var cc = new PasswordBox { Width = 170, Padding = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
                var edicion = new WrapPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
                edicion.Children.Add(Campo("Usuario", cu, 170));
                edicion.Children.Add(Campo("Clave", cc, 170));
                var guardar = Boton("Probar y guardar", async () => await Hacer(async () =>
                {
                    Pintar(await C.Api.Put($"/api/yo/credenciales/{cod}", new { usuario = cu.Text.Trim(), clave = cc.Password }));
                    C.Mensaje($"{Str(s["nombre"])}: entró bien y quedó guardado", false);
                }), true);
                guardar.VerticalAlignment = VerticalAlignment.Bottom;
                edicion.Children.Add(guardar);
                fila.Children.Add(Boton(cargada && !usaWms ? "Cambiar" : "Cargar", () => edicion.Visibility = Visibility.Visible, true));
                panel.Children.Add(fila);
                panel.Children.Add(edicion);
                if (cargada && !usaWms)
                    fila.Children.Add(Boton("Borrar", async () => { if (Confirmar($"¿Borrar tu usuario de {Str(s["nombre"])}?")) await Hacer(async () => Pintar(await C.Api.Delete($"/api/yo/credenciales/{cod}"))); }));
            }
            else panel.Children.Add(fila);
            if (cargada || usaWms)
                fila.Children.Add(Boton("Probar", async () => await Hacer(async () =>
                {
                    var r = await C.Api.Post($"/api/yo/credenciales/{cod}/probar");
                    C.Mensaje($"{Str(s["nombre"])}: {Str(r?["mensaje"])}", r?["ok"]?.GetValue<bool>() != true);
                    Pintar(r?["lista"]);
                })));
            var marco = Marco(panel); marco.Margin = new Thickness(0, 0, 0, 10);
            _cuerpo.Children.Add(marco);
        }
    }
}
