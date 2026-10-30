using System.Net;
using System.Text;

namespace Sona;

/// <summary>
/// Página para imprimir desde la PC de quien la abre (con el diálogo de impresión del navegador): cada computadora usa
/// SUS impresoras, no las del servidor. Las páginas van como imágenes del tamaño justo; al cargar se abre el diálogo.
/// </summary>
public static class PaginaImpresion
{
    /// <summary>Hojas A4 (carátulas): cada imagen va en su hoja, vertical u horizontal según la imagen.</summary>
    public static string HojasA4(string titulo, IList<byte[]> pngs, IEnumerable<string>? avisos = null) => Armar(titulo, pngs, null, avisos);

    /// <summary>Etiquetas del tamaño indicado en mm (impresora de etiquetas).</summary>
    public static string Etiquetas(string titulo, IList<byte[]> pngs, int anchoMm, int altoMm, IEnumerable<string>? avisos = null) => Armar(titulo, pngs, (anchoMm, altoMm), avisos);

    /// <summary>Ancho y alto de un PNG (del encabezado IHDR).</summary>
    public static (int W, int H) Tamano(byte[] png)
    {
        if (png.Length < 24) return (1, 1);
        int L(int i) => (png[i] << 24) | (png[i + 1] << 16) | (png[i + 2] << 8) | png[i + 3];
        return (Math.Max(1, L(16)), Math.Max(1, L(20)));
    }

    static string Armar(string titulo, IList<byte[]> pngs, (int W, int H)? etiqueta, IEnumerable<string>? avisos)
    {
        var t = WebUtility.HtmlEncode(titulo);
        var paginas = new StringBuilder();
        foreach (var p in pngs)
        {
            string clase, estilo;
            if (etiqueta is { } e) { clase = "pg et"; estilo = $"width:{e.W}mm;height:{e.H}mm"; }
            else
            {
                var (w, h) = Tamano(p);
                var horiz = w > h;
                clase = horiz ? "pg h" : "pg v";
                estilo = horiz ? "width:297mm;height:210mm" : "width:210mm;height:297mm";
            }
            paginas.Append($"<div class=\"{clase}\" style=\"{estilo}\"><img src=\"data:image/png;base64,{Convert.ToBase64String(p)}\" alt=\"\"></div>");
        }
        var pagina = etiqueta is { } et ? $"@page {{ size: {et.W}mm {et.H}mm; margin: 0; }}" : "@page { size: A4 portrait; margin: 0; } @page horiz { size: A4 landscape; margin: 0; } .pg.h { page: horiz; }";
        var av = string.Join("", (avisos ?? Enumerable.Empty<string>()).Select(a => $"<div class=\"aviso\">⚠ {WebUtility.HtmlEncode(a)}</div>"));
        var vacio = pngs.Count == 0 ? "<div class=\"aviso\">No hay nada para imprimir.</div>" : "";
        return $$"""
<!doctype html>
<html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{t}}</title>
<style>
  {{pagina}}
  html, body { margin: 0; padding: 0; background: #fff; }
  .pg { overflow: hidden; break-after: page; page-break-after: always; display: flex; align-items: center; justify-content: center; }
  .pg:last-child { break-after: auto; page-break-after: auto; }
  .pg img { display: block; width: 100%; height: 100%; object-fit: contain; }
  .barra, .aviso { font: 14px system-ui, Segoe UI, Arial, sans-serif; }
  .barra { position: sticky; top: 0; display: flex; gap: 10px; align-items: center; padding: 10px 14px; background: #0B2A42; color: #fff; }
  .barra b { flex: 1; }
  .barra button { font: inherit; font-weight: 700; border: 0; border-radius: 8px; padding: 7px 16px; background: #F47920; color: #fff; cursor: pointer; }
  .aviso { margin: 10px 14px; padding: 8px 12px; border-radius: 8px; background: #FFF5DB; color: #8a5a00; }
  @media screen { body { background: #d9dee3; } .pg { margin: 14px auto; background: #fff; box-shadow: 0 2px 10px rgba(0,0,0,.25); max-width: calc(100% - 28px); height: auto !important; aspect-ratio: auto; } .pg img { height: auto; } }
  @media print { .barra, .aviso { display: none !important; } }
</style></head>
<body>
<div class="barra"><b>🖨 {{t}} · {{pngs.Count}} hoja(s)</b><button onclick="window.print()">Imprimir</button></div>
{{av}}{{vacio}}
{{paginas}}
<script>
  // se abre el diálogo de impresión de esta computadora (sus impresoras); si se cancela, queda el botón "Imprimir"
  window.addEventListener("load", function () { if ({{(pngs.Count > 0 ? "true" : "false")}}) setTimeout(function () { window.focus(); window.print(); }, 250); });
</script>
</body></html>
""";
    }
}
