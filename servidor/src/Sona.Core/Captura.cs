using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Sona.Wms;

namespace Sona;

/// <summary>
/// Captura para diagnóstico: guarda cada pantalla del WMS y cada respuesta del LES (con las claves tapadas)
/// en carpeta-de-datos/capturas/&lt;nombre&gt;/ para poder ajustar SONA a las pantallas reales.
/// </summary>
public sealed class Captura
{
    readonly string _dir;
    int _n;

    public string Carpeta => _dir;

    public static string Raiz(Configuracion cfg) => Path.Combine(cfg.Carpeta, "capturas");

    public Captura(Configuracion cfg, string nombre)
    {
        _dir = Path.Combine(Raiz(cfg), $"{DateTime.Now:yyyyMMdd}_{Regex.Replace(nombre, @"[^\w\-]", "_")}");
        Directory.CreateDirectory(_dir);
        _n = Directory.GetFiles(_dir).Length;
        Limpiar(cfg);
    }

    /// <summary>Tapa claves en lo enviado (formularios y JSON).</summary>
    public static string TaparClaves(string s) =>
        Regex.Replace(Regex.Replace(s, @"((?:^|&)[^=&]*(?:PASSWORD|Password|clave|Clave|PWD|pwd)[^=&]*=)[^&]*", "$1***"),
            @"(""(?:[Pp]assword|[Cc]lave)""\s*:\s*"")[^""]*", "$1***");

    static string Archivo(string url)
    {
        var u = url.Split('?')[0];
        var n = u[(u.LastIndexOf('/') + 1)..];
        return Regex.Replace(n.Length == 0 ? "pagina" : n, @"[^\w\.\-]", "_");
    }

    public void Guardar(string pedido, string enviado, string url, int estado, string texto, string extension = ".html")
    {
        try
        {
            var n = Interlocked.Increment(ref _n);
            var nombre = $"{n:000}_{Archivo(url)}";
            if (!nombre.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) nombre += extension;
            var cab = $"PEDIDO: {pedido}\nRESPUESTA: {estado} {url}\nENVIADO: {enviado}\nHORA: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
            var cuerpo = extension == ".html" ? $"<!--\n{cab.Replace("--", "- -")}-->\n{texto}" : $"/*\n{cab}*/\n{texto}";
            File.WriteAllText(Path.Combine(_dir, nombre), cuerpo, Encoding.UTF8);
        }
        catch { }
    }

    public void Nota(string texto)
    {
        try { File.AppendAllText(Path.Combine(_dir, "000_notas.txt"), $"{DateTime.Now:HH:mm:ss} {texto}\n", Encoding.UTF8); } catch { }
    }

    /// <summary>Para enganchar en SesionEscritorio / SesionHandheld.Traza.</summary>
    public Action<string, string, Respuesta> Wms => (pedido, enviado, r) => Guardar(pedido, enviado, r.Url.ToString(), r.Estado, r.Texto);

    /// <summary>Para LesIngresos.Traza (JSON o HTML).</summary>
    public Action<string, string, int, string> Les => (pedido, enviado, estado, texto) =>
        Guardar(pedido, enviado, pedido.Split(' ').Last(), estado, texto, Regex.IsMatch(pedido, @"\.js(\?|$)") ? ".js" : texto.TrimStart().StartsWith("{") || texto.TrimStart().StartsWith("[") ? ".json" : ".html");

    /// <summary>Borra capturas de más de 30 días.</summary>
    static void Limpiar(Configuracion cfg)
    {
        try
        {
            foreach (var d in Directory.GetDirectories(Raiz(cfg)))
                if (Directory.GetLastWriteTime(d) < DateTime.Now.AddDays(-30)) Directory.Delete(d, true);
            foreach (var f in Directory.GetFiles(Raiz(cfg), "*.zip"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30)) File.Delete(f);
        }
        catch { }
    }

    /// <summary>Junta todas las capturas en un zip (y deja una copia en Descargas\SONA_Deposito\capturas si existe esa carpeta).</summary>
    public static (byte[] Datos, string Nombre, string? Copia) Zip(Configuracion cfg)
    {
        var raiz = Raiz(cfg);
        Directory.CreateDirectory(raiz);
        var nombre = $"sona_capturas_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var d in Directory.GetDirectories(raiz))
                foreach (var f in Directory.GetFiles(d))
                    z.CreateEntryFromFile(f, Path.GetFileName(d) + "/" + Path.GetFileName(f), CompressionLevel.Optimal);
        var datos = ms.ToArray();
        string? copia = null;
        try
        {
            var desc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SONA_Deposito");
            if (Directory.Exists(desc) && Environment.GetEnvironmentVariable("SONA_PRUEBAS") != "1")   // las pruebas automáticas no dejan copia
            {
                var c = Path.Combine(desc, "capturas");
                Directory.CreateDirectory(c);
                copia = Path.Combine(c, nombre);
                File.WriteAllBytes(copia, datos);
            }
        }
        catch { copia = null; }
        return (datos, nombre, copia);
    }

    public static string Decodificar(string s) => WebUtility.HtmlDecode(s);
}
