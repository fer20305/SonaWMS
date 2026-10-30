using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sona;

public sealed record Sesion(string Usuario, string Rol, string? Dispositivo);

/// <summary>Cifrado de claves (AES-256-GCM) y tokens de sesión firmados (JWT HS256).</summary>
public static class Seguridad
{
    public static readonly string[] RolesPanel = { "analista", "admin" };
    /// <summary>Pueden ver el panel (web o escritorio). El supervisor solo mira: no puede cambiar nada.</summary>
    public static readonly string[] RolesLectura = { "supervisor", "analista", "admin" };

    static byte[] Llave(string secreto, string uso) => SHA256.HashData(Encoding.UTF8.GetBytes(uso + ":" + secreto));

    public static string Cifrar(string texto, string secreto)
    {
        var iv = RandomNumberGenerator.GetBytes(12);
        var datos = Encoding.UTF8.GetBytes(texto);
        var cifrado = new byte[datos.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Llave(secreto, "cifrado"), 16);
        aes.Encrypt(iv, datos, cifrado, tag);
        return "v1." + Convert.ToBase64String(iv) + "." + Convert.ToBase64String(tag) + "." + Convert.ToBase64String(cifrado);
    }

    public static string Descifrar(string guardado, string secreto)
    {
        var p = guardado.Split('.');
        var (iv, tag, datos) = (Convert.FromBase64String(p[1]), Convert.FromBase64String(p[2]), Convert.FromBase64String(p[3]));
        var plano = new byte[datos.Length];
        using var aes = new AesGcm(Llave(secreto, "cifrado"), 16);
        aes.Decrypt(iv, datos, tag, plano);
        return Encoding.UTF8.GetString(plano);
    }

    /// <summary>Si el texto está cifrado lo descifra; si no, lo devuelve igual (claves cargadas a mano en config.json).</summary>
    public static string DescifrarSiHace(string t, string secreto)
    {
        if (string.IsNullOrEmpty(t) || !t.StartsWith("v1.")) return t;
        try { return Descifrar(t, secreto); } catch { return ""; }
    }

    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static byte[] DeB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    public static string Token(Sesion s, string secreto)
    {
        var ahora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cab = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var cuerpo = B64(JsonSerializer.SerializeToUtf8Bytes(new { sub = s.Usuario, rol = s.Rol, disp = s.Dispositivo, iat = ahora, exp = ahora + 14 * 86400 }));
        var firma = B64(HMACSHA256.HashData(Llave(secreto, "jwt"), Encoding.ASCII.GetBytes(cab + "." + cuerpo)));
        return cab + "." + cuerpo + "." + firma;
    }

    public static Sesion? Verificar(string token, string secreto)
    {
        try
        {
            var p = token.Split('.');
            if (p.Length != 3) return null;
            var esperada = B64(HMACSHA256.HashData(Llave(secreto, "jwt"), Encoding.ASCII.GetBytes(p[0] + "." + p[1])));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(esperada), Encoding.ASCII.GetBytes(p[2]))) return null;
            using var doc = JsonDocument.Parse(DeB64(p[1]));
            var r = doc.RootElement;
            if (r.GetProperty("exp").GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            var disp = r.TryGetProperty("disp", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            return new Sesion(r.GetProperty("sub").GetString()!, r.GetProperty("rol").GetString()!, disp);
        }
        catch { return null; }
    }
}
