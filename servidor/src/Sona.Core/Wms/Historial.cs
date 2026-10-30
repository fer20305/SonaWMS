using System.Text.RegularExpressions;

namespace Sona.Wms;

/// <summary>Una tabla traída de un reporte (WMS o e-TMS): columnas en orden y filas columna → valor.</summary>
public sealed record TablaReporte(List<string> Columnas, List<Dictionary<string, string>> Filas);

/// <summary>Lee los "XLS" del WMS, que en realidad son páginas HTML con una tabla (Historia de contenedor/artículo).</summary>
public static class TablaExcelHtml
{
    static readonly Regex ReTr = new(@"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase);
    static readonly Regex ReTd = new(@"<t[dh]\b[^>]*>([\s\S]*?)</t[dh]>", RegexOptions.IgnoreCase);
    static readonly Regex ReCabecera = new(@"^(Fecha|Contenedor|Art[ií]culo|Tipo|C[oó]digo|Usuario|Cantidad|Movimiento|Ubicaci[oó]n)", RegexOptions.IgnoreCase);

    /// <summary>null si el HTML no tiene una tabla con cabecera reconocible.</summary>
    public static TablaReporte? Leer(string html)
    {
        var filas = ReTr.Matches(html).Select(m => ReTd.Matches(m.Groups[1].Value).Select(c => Gx.Texto(c.Groups[1].Value).Trim()).ToList())
            .Where(f => f.Count > 0).ToList();
        // la cabecera: varias celdas con nombres de columna conocidos y sin "Desde:" (la fila de parámetros del reporte)
        var i = filas.FindIndex(f => f.Count >= 4 && f.Count(c => ReCabecera.IsMatch(c)) >= 2 && !f.Any(c => c.EndsWith(':')));
        if (i < 0) return null;
        var cols = new List<string>();
        foreach (var c in filas[i])
        {
            var n = c.Length == 0 ? $"Col{cols.Count + 1}" : c;
            while (cols.Contains(n)) n += "_";
            cols.Add(n);
        }
        var datos = new List<Dictionary<string, string>>();
        foreach (var f in filas.Skip(i + 1))
        {
            if (f.All(string.IsNullOrWhiteSpace)) continue;
            var d = new Dictionary<string, string>();
            for (var k = 0; k < cols.Count; k++) d[cols[k]] = k < f.Count ? f[k] : "";
            datos.Add(d);
        }
        return new TablaReporte(cols, datos);
    }
}
