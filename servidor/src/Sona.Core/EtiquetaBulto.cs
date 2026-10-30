using System.Globalization;
using SkiaSharp;

namespace Sona;

/// <summary>
/// Etiqueta de medida de un bulto (control de bulto): solicitud, número de bulto con código de barras, medidas, m³, cliente y "bulto X de N".
/// Se dibuja como imagen (para la impresora de etiquetas) del tamaño configurado.
/// </summary>
public static class EtiquetaBulto
{
    public sealed record Datos(string Solicitud, long Bulto, int Numero, int Total, double Alto, double Ancho, double Profundidad,
        string? Cliente, string? Proyecto, string? Origen, string Usuario, DateTime Fecha);

    const int Dpi = 203;   // impresoras de etiquetas térmicas

    static SKTypeface Fuente(bool negrita) => SKTypeface.FromFamilyName("Arial", negrita ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;
    static string N(double v) => v.ToString(v % 1 == 0 ? "0" : "0.#", CultureInfo.GetCultureInfo("es-AR"));

    public static byte[] Png(Datos d, int anchoMm = 100, int altoMm = 80)
    {
        int W = (int)Math.Round(anchoMm / 25.4 * Dpi), H = (int)Math.Round(altoMm / 25.4 * Dpi);
        using var bmp = new SKBitmap(W, H);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            float m = W * 0.04f, u = H / 100f;   // margen y unidad vertical (1% del alto)
            using var negro = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var linea = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(2, W / 250f) };
            void Texto(string t, float x, float y, float tam, bool neg = false, SKTextAlign al = SKTextAlign.Left, float? maxAncho = null)
            {
                using var p = new SKPaint { Color = SKColors.Black, IsAntialias = true, Typeface = Fuente(neg), TextSize = tam, TextAlign = al };
                if (maxAncho is float mx) while (p.MeasureText(t) > mx && p.TextSize > 8) p.TextSize -= 1;
                c.DrawText(t, x, y, p);
            }
            c.DrawRect(new SKRect(linea.StrokeWidth, linea.StrokeWidth, W - linea.StrokeWidth, H - linea.StrokeWidth), linea);

            // encabezado: solicitud y "bulto X de N"
            Texto("SOLICITUD", m, 9 * u, 5.5f * u);
            Texto(d.Solicitud, m, 21 * u, 12 * u, true, maxAncho: W * 0.62f);
            Texto("BULTO", W - m, 9 * u, 5.5f * u, al: SKTextAlign.Right);
            Texto($"{d.Numero} / {d.Total}", W - m, 21 * u, 12 * u, true, SKTextAlign.Right);
            c.DrawLine(m, 25 * u, W - m, 25 * u, linea);

            // medidas
            Texto($"{N(d.Alto)} × {N(d.Ancho)} × {N(d.Profundidad)} cm", W / 2f, 37 * u, 10 * u, true, SKTextAlign.Center, W - 2 * m);
            var m3 = d.Alto * d.Ancho * d.Profundidad / 1_000_000;
            Texto($"Alto × Ancho × Prof.   ·   {m3.ToString("0.000", CultureInfo.GetCultureInfo("es-AR"))} m³", W / 2f, 45 * u, 5.5f * u, al: SKTextAlign.Center);

            // código de barras del número de bulto
            var cod = d.Bulto.ToString(CultureInfo.InvariantCulture);
            var anchos = Etiqueta.Code128Anchos(cod);
            var modulos = anchos.Sum();
            var zona = W - 2 * m;
            var mod = Math.Max(2f, MathF.Floor(zona / (modulos + 20)));
            float x = (W - modulos * mod) / 2f, y0 = 49 * u, y1 = 69 * u;
            for (var i = 0; i < anchos.Count; i++)
            {
                var w = anchos[i] * mod;
                if (i % 2 == 0) c.DrawRect(new SKRect(x, y0, x + w, y1), negro);
                x += w;
            }
            Texto(cod, W / 2f, 76 * u, 6.5f * u, true, SKTextAlign.Center);
            c.DrawLine(m, 79 * u, W - m, 79 * u, linea);

            // cliente / proyecto / origen / fecha
            Texto(string.Join(" · ", new[] { d.Cliente, d.Proyecto }.Where(s => !string.IsNullOrWhiteSpace(s))), m, 87 * u, 5.5f * u, true, maxAncho: W - 2 * m);
            Texto($"{d.Origen} · {d.Fecha:dd/MM/yyyy HH:mm} · {d.Usuario}", m, 95 * u, 4.6f * u, maxAncho: W - 2 * m);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
