using System.Globalization;
using System.Net;
using System.Text;

namespace Sona;

/// <summary>
/// "Impresora virtual": la etiqueta del contenedor como página (10 x 8 cm, igual que la de LES) con el código de barras Code 128.
/// Se ve en el celular y se imprime o se guarda en PDF con la impresión de Android, aunque no haya impresora enchufada.
/// </summary>
public static class Etiqueta
{
    // Anchos de barra/espacio de los 107 símbolos de Code 128 (0..105 + stop de 7 elementos)
    static readonly string[] Patrones =
    {
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213","221312","231212","112232","122132","122231","113222","123122","123221",
        "223211","221132","221231","213212","223112","312131","311222","321122","321221","312212","322112","322211","212123","212321","232121","111323","131123","131321",
        "112313","132113","132311","211313","231113","231311","112133","112331","132131","113123","113321","133121","313121","211331","231131","213113","213311","213131",
        "311123","311321","331121","312113","312311","332111","314111","221411","431111","111224","111422","121124","121421","141122","141221","112214","112412","122114",
        "122411","142112","142211","241211","221114","413111","241112","134111","111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141","114131","311141","411131","211412","211214","211232","2331112",
    };

    /// <summary>Anchos (en módulos) de barras y espacios alternados del Code 128 (set B) del texto, empezando por barra.</summary>
    public static List<int> Code128Anchos(string texto)
    {
        var codigos = new List<int> { 104 };
        foreach (var ch in texto) codigos.Add(ch is >= ' ' and <= '~' ? ch - 32 : '?' - 32);
        var suma = 104;
        for (var i = 1; i < codigos.Count; i++) suma += codigos[i] * i;
        codigos.Add(suma % 103);
        codigos.Add(106);
        return codigos.SelectMany(c => Patrones[c].Select(x => x - '0')).ToList();
    }

    /// <summary>SVG del código Code 128 (set B) del texto.</summary>
    public static string Code128Svg(string texto, double alto = 60)
    {
        var codigos = new List<int> { 104 };   // Start B
        foreach (var ch in texto) codigos.Add(ch is >= ' ' and <= '~' ? ch - 32 : '?' - 32);
        var suma = 104;
        for (var i = 1; i < codigos.Count; i++) suma += codigos[i] * i;
        codigos.Add(suma % 103);
        codigos.Add(106);                      // Stop
        var sb = new StringBuilder();
        var x = 10;                            // zona libre
        foreach (var c in codigos)
        {
            var p = Patrones[c];
            for (var i = 0; i < p.Length; i++)
            {
                var w = p[i] - '0';
                if (i % 2 == 0) sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"0\" width=\"{w}\" height=\"{alto}\"/>");
                x += w;
            }
        }
        x += 10;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {x} {alto.ToString(CultureInfo.InvariantCulture)}\" preserveAspectRatio=\"none\" class=\"barras\">{sb}</svg>";
    }

    static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string Html(string contenedor, string? articulo, string? descripcion, string? cliente, double? cantidad, string? posicion, DateTime fecha) => $$"""
<!doctype html>
<html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Etiqueta {{H(contenedor)}}</title>
<style>
  @page { size: 100mm 80mm; margin: 0; }
  * { box-sizing: border-box; }
  body { margin: 0; font-family: Arial, Helvetica, sans-serif; color: #000; background: #fff; }
  .etiqueta { width: 100mm; height: 80mm; padding: 3mm 5mm; display: flex; flex-direction: column; gap: 1mm; border: 1px dashed #bbb; margin: 0 auto; }
  .cliente { font-size: 11pt; font-weight: bold; text-transform: uppercase; }
  .codigo { font-size: 20pt; font-weight: 900; letter-spacing: .5pt; }
  .desc { font-size: 10pt; line-height: 1.2; max-height: 2.5em; overflow: hidden; }
  .fila { display: flex; justify-content: space-between; font-size: 10pt; }
  .qrfila { display: flex; align-items: center; gap: 4mm; }
  .qr { width: 24mm; height: 24mm; flex: none; }
  .barras { width: 100%; height: 11mm; }
  .cont { text-align: center; font-size: 20pt; font-weight: 900; letter-spacing: 1pt; }
  .pie { font-size: 7pt; color: #444; text-align: center; }
  @media print { .etiqueta { border: 0; } }
</style></head>
<body><div class="etiqueta">
  <div class="cliente">{{H(cliente)}}</div>
  <div class="codigo">{{H(articulo)}}</div>
  <div class="desc">{{H(descripcion)}}</div>
  <div class="fila"><span>Fecha: {{fecha:dd/MM/yyyy}}</span><span>{{(cantidad != null ? "Cant.: " + cantidad.Value.ToString("0.##", CultureInfo.InvariantCulture) : "")}}</span><span>{{H(posicion)}}</span></div>
  <div class="qrfila">{{EtiquetaContenedor.QrSvg(contenedor)}}<div class="cont">{{H(contenedor)}}</div></div>
  {{Code128Svg(contenedor)}}
  <div class="pie">SONA Depósito · impresora virtual</div>
</div></body></html>
""";
}
