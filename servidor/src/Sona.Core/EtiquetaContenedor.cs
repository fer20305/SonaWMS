using System.Globalization;
using QRCoder;
using SkiaSharp;

namespace Sona;

/// <summary>
/// Etiqueta del contenedor (reimpresión) con código QR: formato Zebra (etiqueta térmica, 100 x 80 mm por defecto)
/// o A4 apaisada con letras grandes para ver el pallet de lejos.
/// </summary>
public static class EtiquetaContenedor
{
    public sealed record Datos(string Contenedor, string? Articulo, string? Descripcion, string? Cliente, double? Cantidad, string? Posicion, DateTime Fecha, string Usuario);

    /// <summary>Módulos del QR (true = negro), sin la zona libre.</summary>
    public static bool[,] Qr(string texto)
    {
        using var gen = new QRCodeGenerator();
        using var d = gen.CreateQrCode(texto, QRCodeGenerator.ECCLevel.M);
        var m = d.ModuleMatrix;
        const int borde = 4;   // QRCoder deja 4 módulos de zona libre
        var n = m.Count - 2 * borde;
        var r = new bool[n, n];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                r[y, x] = m[y + borde][x + borde];
        return r;
    }

    /// <summary>SVG del QR (para las páginas HTML).</summary>
    public static string QrSvg(string texto)
    {
        var q = Qr(texto);
        var n = q.GetLength(0);
        var sb = new System.Text.StringBuilder();
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                if (q[y, x]) sb.Append(CultureInfo.InvariantCulture, $"M{x + 2} {y + 2}h1v1h-1z");
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {n + 4} {n + 4}\" shape-rendering=\"crispEdges\" class=\"qr\"><rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path d=\"{sb}\" fill=\"#000\"/></svg>";
    }

    static void DibujarQr(SKCanvas c, string texto, float x, float y, float lado)
    {
        var q = Qr(texto);
        var n = q.GetLength(0);
        var mod = MathF.Floor(lado / n);
        var off = (lado - mod * n) / 2;
        using var p = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
                if (q[j, i]) c.DrawRect(new SKRect(x + off + i * mod, y + off + j * mod, x + off + (i + 1) * mod, y + off + (j + 1) * mod), p);
    }

    /// <summary>Código de barras Code 128 del texto, centrado en el ancho dado (las barras quedan en módulos enteros de píxel).</summary>
    static void DibujarBarras(SKCanvas c, string texto, float x, float y, float ancho, float alto, bool izquierda = false)
    {
        var anchos = Etiqueta.Code128Anchos(texto);
        var modulos = anchos.Sum() + 20;                       // + zona libre de 10 módulos a cada lado
        var mod = Math.Max(1f, MathF.Floor(ancho / modulos));
        var xi = izquierda ? x : x + (ancho - (modulos - 20) * mod) / 2f;
        using var p = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        for (var i = 0; i < anchos.Count; i++)
        {
            var w = anchos[i] * mod;
            if (i % 2 == 0) c.DrawRect(new SKRect(xi, y, xi + w, y + alto), p);
            xi += w;
        }
    }

    static SKTypeface Fuente(bool negrita) => SKTypeface.FromFamilyName("Arial", negrita ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;
    static string Cant(double? v) => v == null ? "" : v.Value.ToString(v.Value % 1 == 0 ? "#,0" : "#,0.##", CultureInfo.GetCultureInfo("es-AR"));

    /// <summary>Texto ajustado a un ancho máximo (achica la letra si no entra; corta en dos renglones si se pide).</summary>
    static void Texto(SKCanvas c, string? t, float x, float y, float tam, float maxAncho, bool neg = false, SKTextAlign al = SKTextAlign.Left, int renglones = 1, float minTam = 10)
    {
        if (string.IsNullOrWhiteSpace(t)) return;
        using var p = new SKPaint { Color = SKColors.Black, IsAntialias = true, Typeface = Fuente(neg), TextSize = tam, TextAlign = al };
        if (renglones > 1 && p.MeasureText(t) > maxAncho)
        {
            // dos renglones: corta en el último espacio que entra
            var palabras = t.Split(' ');
            var linea = "";
            var lineas = new List<string>();
            foreach (var w in palabras)
            {
                var prueba = linea.Length == 0 ? w : linea + " " + w;
                if (p.MeasureText(prueba) > maxAncho && linea.Length > 0 && lineas.Count < renglones - 1) { lineas.Add(linea); linea = w; }
                else linea = prueba;
            }
            lineas.Add(linea);
            for (var i = 0; i < lineas.Count; i++) Texto(c, lineas[i], x, y + i * tam * 1.12f, tam, maxAncho, neg, al, 1, minTam);
            return;
        }
        while (p.MeasureText(t) > maxAncho && p.TextSize > minTam) p.TextSize -= 1;
        c.DrawText(t, x, y, p);
    }

    /// <summary>Etiqueta para la impresora Zebra: mm del tamaño configurado (100 x 70 por defecto), a 203 dpi, aprovechando toda la etiqueta.</summary>
    public static byte[] Zebra(Datos d, int anchoMm = 100, int altoMm = 70)
    {
        const int dpi = 203;
        int W = (int)Math.Round(anchoMm / 25.4 * dpi), H = (int)Math.Round(altoMm / 25.4 * dpi);
        using var bmp = new SKBitmap(W, H);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            float m = W * 0.03f, u = H / 100f;
            using var linea = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(2, W / 250f) };
            c.DrawRect(new SKRect(linea.StrokeWidth, linea.StrokeWidth, W - linea.StrokeWidth, H - linea.StrokeWidth), linea);
            Texto(c, d.Cliente?.ToUpperInvariant(), m, 9.5f * u, 8f * u, W - 2 * m, true);
            Texto(c, d.Articulo, m, 23 * u, 13f * u, W - 2 * m, true);
            Texto(c, d.Descripcion, m, 30.5f * u, 5.5f * u, W - 2 * m);
            c.DrawLine(m, 33.5f * u, W - m, 33.5f * u, linea);
            // QR a la izquierda; número de contenedor, cantidad y posición a la derecha
            var lado = 43 * u;
            DibujarQr(c, d.Contenedor, m, 35 * u, lado);
            var xd = m + lado + 4 * u; var ancho = W - m - xd;
            Texto(c, "CONTENEDOR", xd, 41 * u, 5f * u, ancho);
            Texto(c, d.Contenedor, xd, 53.5f * u, 12.5f * u, ancho, true);
            if (d.Cantidad != null) Texto(c, "Cant.: " + Cant(d.Cantidad), xd, 63.5f * u, 8f * u, ancho, true);
            Texto(c, d.Posicion, xd, 71 * u, 6f * u, ancho);
            Texto(c, $"{d.Fecha:dd/MM/yyyy}  ·  {d.Usuario}", xd, 77 * u, 4.8f * u, ancho);
            c.DrawLine(m, 79.5f * u, W - m, 79.5f * u, linea);
            // código de barras (por si la pistola no lee QR)
            DibujarBarras(c, d.Contenedor, m, 82 * u, W - 2 * m, 15 * u);
        }
        return Png(bmp);
    }

    /// <summary>
    /// Hoja A4 HORIZONTAL con letras grandes (para pegar en el pallet y leer de lejos): QR a la derecha y, lejos del QR,
    /// abajo a la izquierda, la cantidad con el código de barras del mismo tamaño.
    /// </summary>
    public static byte[] A4(Datos d)
    {
        const int dpi = 150;
        int W = (int)Math.Round(297 / 25.4 * dpi), H = (int)Math.Round(210 / 25.4 * dpi);   // 1754 x 1240
        using var bmp = new SKBitmap(W, H);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            float m = 60, u = H / 100f;
            using var linea = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 6 };
            c.DrawRect(new SKRect(30, 30, W - 30, H - 30), linea);
            Texto(c, d.Cliente?.ToUpperInvariant(), m, 12 * u, 8.5f * u, W - 2 * m, true, minTam: 36);
            Texto(c, d.Articulo, m, 28.5f * u, 15 * u, W - 2 * m, true, minTam: 40);
            Texto(c, d.Descripcion, m, 36 * u, 5.5f * u, W - 2 * m, minTam: 28);
            c.DrawLine(m, 40 * u, W - m, 40 * u, linea);
            var lado = 50 * u;
            var xq = W - m - lado;
            DibujarQr(c, d.Contenedor, xq, 44 * u, lado);
            // columna izquierda, con un buen espacio libre antes del QR
            var ancho = xq - m - 12 * u;
            Texto(c, "CONTENEDOR", m, 48 * u, 4.5f * u, ancho);
            Texto(c, d.Contenedor, m, 63 * u, 15 * u, ancho, true, minTam: 40);
            // cantidad y, debajo, el código de barras del mismo ancho que la cantidad
            using var pc = new SKPaint { Typeface = Fuente(true), TextSize = 9 * u };
            var txtCant = d.Cantidad != null ? "Cantidad: " + Cant(d.Cantidad) : null;
            if (txtCant != null) Texto(c, txtCant, m, 75.5f * u, 9 * u, ancho, true);
            var anchoBarras = Math.Min(ancho, Math.Max(txtCant != null ? pc.MeasureText(txtCant) : ancho * 0.6f, ancho * 0.55f));
            DibujarBarras(c, d.Contenedor, m, 79 * u, anchoBarras, 11 * u, izquierda: true);
            Texto(c, string.Join("   ·   ", new[] { d.Posicion is { Length: > 0 } p ? "Posición " + p : null, d.Fecha.ToString("dd/MM/yyyy"), d.Usuario }.Where(x => x != null)), m, 95.5f * u, 3.4f * u, ancho);
        }
        return Png(bmp);
    }

    static byte[] Png(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Página para imprimir desde el navegador de esta PC (la imagen del tamaño justo y el diálogo de impresión).</summary>
    public static string Html(string contenedor, byte[] png, string formato, int anchoMm = 100, int altoMm = 80) => $$"""
<!doctype html>
<html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Etiqueta {{System.Net.WebUtility.HtmlEncode(contenedor)}}</title>
<style>
  @page { size: {{(formato == "a4" ? "A4 landscape" : $"{anchoMm}mm {altoMm}mm")}}; margin: 0; }
  html, body { margin: 0; background: #fff; }
  img { display: block; width: {{(formato == "a4" ? "297mm" : $"{anchoMm}mm")}}; height: {{(formato == "a4" ? "210mm" : $"{altoMm}mm")}}; margin: 0 auto; }
  @media screen { body { background: #ddd; padding: 12px; } img { box-shadow: 0 2px 10px rgba(0,0,0,.3); max-width: 100%; height: auto; } }
</style></head>
<body><img src="data:image/png;base64,{{Convert.ToBase64String(png)}}" alt="Etiqueta {{System.Net.WebUtility.HtmlEncode(contenedor)}}" onload="setTimeout(function(){ window.print(); }, 300)"></body></html>
""";
}
