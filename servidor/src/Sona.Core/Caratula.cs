using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;

namespace Sona;

/// <summary>Renglón del PDF "Simulación de Faltantes": artículo, disponible, pedido y faltante.</summary>
public sealed record RenglonSimulacion(string Articulo, string? Descripcion, double Disponible, double Pedido, double Faltante);

/// <summary>
/// Carátula de la OP: el PDF de "Sim de Faltantes" del WMS con el picker asignado bien grande arriba a la derecha.
/// Se dibuja cada página (pdfium) y se arma un PDF nuevo (Skia) con el sello; la impresión va directo a la impresora configurada
/// en la PC del servidor (Windows), sin diálogo.
/// </summary>
public static class Caratula
{
    const int Dpi = 200;
    static readonly object _pdfium = new();   // pdfium no es multihilo

    sealed record Pagina(SKBitmap Imagen, float AnchoPt, float AltoPt);

    static List<Pagina> Dibujar(byte[] pdf)
    {
        lock (_pdfium)
        {
            using var doc = DocLib.Instance.GetDocReader(pdf, new PageDimensions(Dpi / 72.0));
            var paginas = new List<Pagina>();
            for (var i = 0; i < doc.GetPageCount(); i++)
            {
                using var pr = doc.GetPageReader(i);
                int w = pr.GetPageWidth(), h = pr.GetPageHeight();
                var crudo = pr.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover(255, 255, 255), RenderFlags.RenderAnnotations);
                var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                System.Runtime.InteropServices.Marshal.Copy(crudo, 0, bmp.GetPixels(), Math.Min(crudo.Length, w * h * 4));
                // pdfium deja el fondo transparente: se pinta sobre blanco
                var blanco = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgb888x, SKAlphaType.Opaque));
                using (var c = new SKCanvas(blanco)) { c.Clear(SKColors.White); c.DrawBitmap(bmp, 0, 0); }
                bmp.Dispose();
                paginas.Add(new Pagina(blanco, w * 72f / Dpi, h * 72f / Dpi));
            }
            return paginas;
        }
    }

    /// <summary>Texto del PDF (para leer los faltantes de la simulación).</summary>
    public static string Texto(byte[] pdf)
    {
        lock (_pdfium)
        {
            using var doc = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1));
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < doc.GetPageCount(); i++) { using var pr = doc.GetPageReader(i); sb.AppendLine(pr.GetText()); }
            return sb.ToString();
        }
    }

    /// <summary>Renglones de la simulación de faltantes ("JJVC0510 BOLSAS ACUVUE ... 202 50 0").</summary>
    public static List<RenglonSimulacion> Renglones(byte[] pdf)
    {
        var lista = new List<RenglonSimulacion>();
        string texto;
        try { texto = Texto(pdf); } catch { return lista; }
        static double N(string s) => double.TryParse(s.Replace(".", "").Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        foreach (var linea in texto.Split('\n').Select(l => l.Trim()))
        {
            var m = Regex.Match(linea, @"^([A-Z0-9][A-Z0-9_.\-/]{2,})\s+(.*?)\s+(-?[\d.,]+)\s+(-?[\d.,]+)\s+(-?[\d.,]+)$");
            if (!m.Success || m.Groups[1].Value is "Pagina" or "Fecha") continue;
            lista.Add(new RenglonSimulacion(m.Groups[1].Value, m.Groups[2].Value.Trim(), N(m.Groups[3].Value), N(m.Groups[4].Value), N(m.Groups[5].Value)));
        }
        return lista;
    }

    static SKTypeface Fuente(bool negrita) =>
        SKTypeface.FromFamilyName("Arial", negrita ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;

    /// <summary>Sello del picker en el recuadro del título, a la derecha (coordenadas en puntos de la página A4 del WMS).</summary>
    static void Sello(SKCanvas c, float anchoPt, string nombre, string? codigo, string? pie)
    {
        // recuadro libre a la derecha del título: de ~445 a ~572 pt en una página de 598 pt
        var der = anchoPt - 28f; var izq = anchoPt * 0.757f; var ancho = der - izq;
        const float arriba = 9f, abajo = 55f;
        using var fondo = new SKPaint { Color = new SKColor(0xFF, 0xF1, 0xE6), IsAntialias = true };
        using var borde = new SKPaint { Color = new SKColor(0xF4, 0x79, 0x20), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
        var caja = new SKRoundRect(new SKRect(izq - 4, arriba, der + 4, abajo), 4);
        c.DrawRoundRect(caja, fondo); c.DrawRoundRect(caja, borde);

        using var etiqueta = new SKPaint { Color = new SKColor(0x5D, 0x6B, 0x79), IsAntialias = true, Typeface = Fuente(true), TextSize = 7.5f };
        var etq = "PICKER" + (string.IsNullOrWhiteSpace(codigo) || codigo.Equals(nombre, StringComparison.OrdinalIgnoreCase) ? "" : "  ·  " + codigo);
        c.DrawText(etq, der - etiqueta.MeasureText(etq), arriba + 9f, etiqueta);

        using var p = new SKPaint { Color = SKColors.Black, IsAntialias = true, Typeface = Fuente(true) };
        // una línea si entra con letra grande; si no, en dos líneas (nombre / apellido)
        var lineas = new List<string> { nombre.Trim().ToUpperInvariant() };
        float Tam(IEnumerable<string> ls, float max) { var t = max; foreach (var l in ls) { p.TextSize = 1; var w = p.MeasureText(l); if (w > 0) t = Math.Min(t, ancho / w); } return t; }
        var tam = Tam(lineas, 22f);
        if (tam < 15f && lineas[0].Contains(' '))
        {
            var pal = lineas[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var mejor = (L: lineas, T: tam);
            for (var k = 1; k < pal.Length; k++)
            {
                var dos = new List<string> { string.Join(' ', pal.Take(k)), string.Join(' ', pal.Skip(k)) };
                var t = Math.Min(Tam(dos, 17f), 17f);
                if (t > mejor.T) mejor = (dos, t);
            }
            (lineas, tam) = mejor;
        }
        tam = Math.Max(7f, Math.Min(tam, lineas.Count == 1 ? 22f : 17f));
        p.TextSize = tam;
        var y = lineas.Count == 1 ? arriba + 13f + (abajo - arriba - 13f) / 2 + tam * 0.35f : arriba + 12f + tam;
        foreach (var l in lineas) { c.DrawText(l, der - p.MeasureText(l), y, p); y += tam * 1.05f; }

        if (!string.IsNullOrWhiteSpace(pie))
        {
            using var chico = new SKPaint { Color = new SKColor(0x5D, 0x6B, 0x79), IsAntialias = true, Typeface = Fuente(false), TextSize = 6.5f };
            c.DrawText(pie, der - chico.MeasureText(pie), abajo + 8f, chico);
        }
    }

    /// <summary>PDF con el picker sellado en cada página.</summary>
    public static byte[] Sellar(byte[] pdf, string nombre, string? codigo, string? pie = null)
    {
        var paginas = Dibujar(pdf);
        try
        {
            using var ms = new MemoryStream();
            using (var doc = SKDocument.CreatePdf(ms, new SKDocumentPdfMetadata { Title = "Carátula " + nombre, Creator = "SONA Depósito", RasterDpi = Dpi, EncodingQuality = 101 }))
            {
                foreach (var pg in paginas)
                {
                    var c = doc.BeginPage(pg.AnchoPt, pg.AltoPt);
                    c.DrawBitmap(pg.Imagen, new SKRect(0, 0, pg.AnchoPt, pg.AltoPt));
                    Sello(c, pg.AnchoPt, nombre, codigo, pie);
                    doc.EndPage();
                }
                doc.Close();
            }
            return ms.ToArray();
        }
        finally { foreach (var pg in paginas) pg.Imagen.Dispose(); }
    }

    /// <summary>Cada página como imagen PNG a 200 dpi con el sello del picker (para la impresora; no pasa por un PDF intermedio).</summary>
    public static List<byte[]> Imagenes(byte[] pdf, string? nombre = null, string? codigo = null, string? pie = null)
    {
        var paginas = Dibujar(pdf);
        try
        {
            return paginas.Select(pg =>
            {
                if (nombre != null)
                    using (var c = new SKCanvas(pg.Imagen)) { c.Scale(Dpi / 72f); Sello(c, pg.AnchoPt, nombre, codigo, pie); }
                using var img = SKImage.FromBitmap(pg.Imagen);
                using var data = img.Encode(SKEncodedImageFormat.Png, 90);
                return data.ToArray();
            }).ToList();
        }
        finally { foreach (var pg in paginas) pg.Imagen.Dispose(); }
    }

    /// <summary>Hoja A4 simple (para probar la impresora).</summary>
    public static byte[] PaginaPrueba(string texto)
    {
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms))
        {
            var c = doc.BeginPage(595f, 842f);
            using var p = new SKPaint { Color = SKColors.Black, IsAntialias = true, Typeface = Fuente(true), TextSize = 20 };
            using var marco = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            c.DrawRect(new SKRect(9, 7, 586, 56), marco);
            c.DrawText("SONA Depósito", 30, 38, p);
            p.TextSize = 13; p.Typeface = Fuente(false);
            c.DrawText(texto, 30, 100, p);
            c.DrawText("Si ves el recuadro del picker arriba a la derecha, la impresora quedó bien configurada.", 30, 124, p);
            doc.EndPage();
            doc.Close();
        }
        return ms.ToArray();
    }

    // ================================================================ impresión (Windows)

    public static List<string> Impresoras()
    {
        if (!OperatingSystem.IsWindows()) return new();
        try { return ImpresorasWin(); } catch { return new(); }
    }

    public static string? ImpresoraPredeterminada()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return new System.Drawing.Printing.PrinterSettings().PrinterName; } catch { return null; }
    }

    [SupportedOSPlatform("windows")]
    static List<string> ImpresorasWin() => System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().OrderBy(x => x).ToList();

    /// <summary>Imprime el PDF del WMS con el sello del picker en la impresora indicada (vacío = la predeterminada de Windows).</summary>
    public static void Imprimir(byte[] pdf, string? nombre, string? codigo, string? pie, string? impresora, int copias, string documento)
    {
        if (!OperatingSystem.IsWindows()) throw new ErrorUsuario("La impresión directa funciona con el servidor en Windows.");
        ImprimirWin(Imagenes(pdf, nombre, codigo, pie), impresora, Math.Clamp(copias, 1, 5), documento);
    }

    /// <summary>Imprime imágenes PNG (por ejemplo etiquetas) ajustadas a la hoja de la impresora.</summary>
    /// <param name="etiquetaMm">Medida de la etiqueta: se fuerza ese tamaño de papel (si no, el driver usa el suyo y la etiqueta sale chica o corrida).</param>
    public static void ImprimirImagenes(List<byte[]> pngs, string? impresora, int copias, string documento, (int AnchoMm, int AltoMm)? etiquetaMm = null)
    {
        if (!OperatingSystem.IsWindows()) throw new ErrorUsuario("La impresión directa funciona con el servidor en Windows.");
        if (pngs.Count == 0) return;
        ImprimirWin(pngs, impresora, Math.Clamp(copias, 1, 5), documento, etiquetaMm);
    }

    [SupportedOSPlatform("windows")]
    static void ImprimirWin(List<byte[]> pngs, string? impresora, int copias, string documento, (int AnchoMm, int AltoMm)? etiquetaMm = null)
    {
        using var pd = new System.Drawing.Printing.PrintDocument();
        if (!string.IsNullOrWhiteSpace(impresora)) pd.PrinterSettings.PrinterName = impresora;
        if (!pd.PrinterSettings.IsValid) throw new ErrorUsuario($"La impresora \"{impresora}\" no está instalada en la PC del servidor SONA.");
        pd.PrinterSettings.Copies = (short)copias;
        pd.DocumentName = documento;
        pd.PrintController = new System.Drawing.Printing.StandardPrintController();   // sin ventana de "Imprimiendo…"
        pd.OriginAtMargins = false;
        if (etiquetaMm is { } et)
        {
            // tamaño de la etiqueta en centésimas de pulgada, sin márgenes
            pd.DefaultPageSettings.PaperSize = new System.Drawing.Printing.PaperSize($"SONA {et.AnchoMm}x{et.AltoMm} mm", (int)Math.Round(et.AnchoMm / 25.4 * 100), (int)Math.Round(et.AltoMm / 25.4 * 100));
            pd.DefaultPageSettings.Margins = new System.Drawing.Printing.Margins(0, 0, 0, 0);
            pd.DefaultPageSettings.Landscape = false;
        }
        var i = 0;
        // hojas: vertical u horizontal según la imagen (la etiqueta A4 del contenedor va apaisada)
        pd.QueryPageSettings += (_, e) =>
        {
            if (etiquetaMm != null || i >= pngs.Count) return;
            var (w, h) = PaginaImpresion.Tamano(pngs[i]);
            e.PageSettings.Landscape = w > h;
        };
        pd.PrintPage += (_, e) =>
        {
            using var ms = new MemoryStream(pngs[i]);
            using var img = System.Drawing.Image.FromStream(ms);
            var g = e.Graphics!;
            var hoja = e.PageBounds;   // centésimas de pulgada
            float hx = e.PageSettings.HardMarginX, hy = e.PageSettings.HardMarginY;
            // tamaño de la imagen en centésimas de pulgada (200 dpi) ajustado a la hoja sin deformar
            float iw = img.Width * 100f / Dpi, ih = img.Height * 100f / Dpi;
            var esc = Math.Min(hoja.Width / iw, hoja.Height / ih);
            float w = iw * esc, h = ih * esc;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, (hoja.Width - w) / 2 - hx, (hoja.Height - h) / 2 - hy, w, h);
            i++;
            e.HasMorePages = i < pngs.Count;
        };
        pd.Print();
    }
}
