using System.Text.RegularExpressions;

namespace Sona.Dominio;

/// <summary>
/// Códigos de posición.
///  - Código nuevo: CARA + columna (2 dígitos, 3 si pasa de 99) + nivel (2). Ej. FI1502.
///  - QR del WMS: CARA + columna (3) + nivel (2). Ej. IE_PK00403.
///  - Con separadores: FI-15-02. Ubic.Ctrl: solo números (cara+columna; el nivel va aparte).
/// </summary>
public sealed record Pos(string Cara, int Columna, int? Nivel);

public static class Posicion
{
    public static bool EsUbicCtrl(string t) => Regex.IsMatch(t.Trim(), @"^\d{3,8}$");

    public static string Codigo(string cara, int columna, int nivel) =>
        cara.ToUpperInvariant() + (columna > 99 ? columna.ToString("000") : columna.ToString("00")) + nivel.ToString("00");

    public static string Legible(string cara, int columna, int? nivel) =>
        $"{cara}-{columna:00}" + (nivel != null ? $"-{nivel:00}" : "");

    public static Pos? Interpretar(string texto, IEnumerable<string> caras)
    {
        var t = Regex.Replace(texto.Trim().ToUpperInvariant(), @"\s+", " ");
        if (t.Length == 0 || EsUbicCtrl(t)) return null;
        var sep = Regex.Match(t, @"^([A-Z][A-Z0-9_-]*?)[\s/.-]+(\d{1,3})(?:[\s/.-]+(\d{1,2}))?$");
        foreach (var cara in caras.Select(c => c.ToUpperInvariant()).Distinct().OrderByDescending(c => c.Length))
        {
            if (!t.StartsWith(cara)) continue;
            var resto = Regex.Replace(t[cara.Length..], @"^[\s/.-]+", "");
            var r = Numeros(resto);
            if (r != null) return new Pos(cara, r.Value.col, r.Value.niv);
        }
        if (sep.Success) return new Pos(sep.Groups[1].Value, int.Parse(sep.Groups[2].Value), sep.Groups[3].Success ? int.Parse(sep.Groups[3].Value) : null);
        var m = Regex.Match(t.Replace(" ", ""), @"^([A-Z][A-Z_-]*[A-Z])(\d{3,6})$");
        if (m.Success && Numeros(m.Groups[2].Value) is { } n) return new Pos(m.Groups[1].Value, n.col, n.niv);
        return null;
    }

    static (int col, int? niv)? Numeros(string s)
    {
        var sep = Regex.Match(s, @"^(\d{1,3})[\s/.-]+(\d{1,2})$");
        if (sep.Success) return (int.Parse(sep.Groups[1].Value), int.Parse(sep.Groups[2].Value));
        if (!Regex.IsMatch(s, @"^\d+$")) return null;
        return s.Length switch
        {
            1 or 2 or 3 => (int.Parse(s), null),
            4 => (int.Parse(s[..2]), int.Parse(s[2..])),
            5 => (int.Parse(s[..3]), int.Parse(s[3..])),
            _ => null,
        };
    }

    /// <summary>"DI-004-06", "DI /004/06" → "DI0406".</summary>
    public static string? Normalizar(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return null;
        var m = Regex.Match(t, @"^\s*([A-Z][A-Z0-9_-]*?)\s*[-/ ]+\s*(\d{1,3})\s*[-/ ]+\s*(\d{1,2})\s*$", RegexOptions.IgnoreCase);
        return m.Success ? Codigo(m.Groups[1].Value.ToUpperInvariant(), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : t.Trim().ToUpperInvariant();
    }
}
