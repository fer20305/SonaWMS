using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sona;

/// <summary>
/// Actualización de la app de los celulares desde el servidor: el panel sube el APK (o se deja junto al SonaDeposito.exe)
/// y cada celular, al abrir la app, ve que hay una versión nueva, la baja del servidor y la instala.
/// </summary>
public sealed class AppMovil(Configuracion cfg, Eventos ev)
{
    public sealed record Apk(string Archivo, string Version, long Tamano, DateTime Fecha);

    string Carpeta { get { var d = Path.Combine(cfg.Carpeta, "app"); Directory.CreateDirectory(d); return d; } }
    string ArchivoInfo => Path.Combine(Carpeta, "info.json");

    static readonly Regex ReVersion = new(@"(\d+(?:\.\d+){1,3})", RegexOptions.Compiled);

    public static Version? Ver(string? s)
    {
        var m = ReVersion.Match(s ?? "");
        if (!m.Success) return null;
        var partes = m.Groups[1].Value.Split('.').Select(int.Parse).ToList();
        while (partes.Count < 2) partes.Add(0);
        return partes.Count switch { 2 => new Version(partes[0], partes[1]), 3 => new Version(partes[0], partes[1], partes[2]), _ => new Version(partes[0], partes[1], partes[2], partes[3]) };
    }

    /// <summary>El APK más nuevo: el subido desde el panel (carpeta de datos › app) o el que vino junto al programa.</summary>
    public Apk? Ultima()
    {
        var lista = new List<Apk>();
        foreach (var dir in new[] { Carpeta, AppContext.BaseDirectory }.Distinct())
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*.apk"))
                {
                    var n = Path.GetFileNameWithoutExtension(f);
                    if (!n.Contains("SONA", StringComparison.OrdinalIgnoreCase) || Ver(n) is not { } v) continue;
                    var fi = new FileInfo(f);
                    lista.Add(new Apk(f, v.ToString(), fi.Length, fi.LastWriteTimeUtc));
                }
            }
            catch { }
        }
        return lista.OrderByDescending(a => Ver(a.Version)).ThenByDescending(a => a.Fecha).FirstOrDefault();
    }

    JsonObject Info()
    {
        try { return File.Exists(ArchivoInfo) ? JsonNode.Parse(File.ReadAllText(ArchivoInfo)) as JsonObject ?? new() : new(); } catch { return new(); }
    }

    public object Version()
    {
        var a = Ultima();
        var i = Info();
        var mismaVersion = a != null && i["version"]?.ToString() == a.Version;
        return new
        {
            version = a?.Version, tamano = a?.Tamano, fecha = a?.Fecha.ToString("o"),
            notas = mismaVersion ? i["notas"]?.ToString() : null,
            obligatoria = mismaVersion && i["obligatoria"] is JsonValue o && o.TryGetValue<bool>(out var b) && b,
            subidaPor = mismaVersion ? i["por"]?.ToString() : null,
            descarga = a != null ? "/api/app/descargar" : null,
        };
    }

    public async Task<object> Subir(Stream datos, string nombre, string? version, string? notas, bool obligatoria, string usuario)
    {
        var v = Ver(version) ?? Ver(Path.GetFileNameWithoutExtension(nombre))
                ?? throw new ErrorUsuario("No se pudo saber la versión: poné la versión (ej. 1.17) o subí el archivo con el nombre SONA_Deposito_1.17.apk.");
        var destino = Path.Combine(Carpeta, $"SONA_Deposito_{v}.apk");
        var tmp = destino + ".subiendo";
        await using (var fs = File.Create(tmp)) await datos.CopyToAsync(fs);
        var cab = new byte[4];
        await using (var fs = File.OpenRead(tmp)) _ = await fs.ReadAsync(cab);
        if (cab[0] != 'P' || cab[1] != 'K') { File.Delete(tmp); throw new ErrorUsuario("El archivo no es un APK."); }
        File.Move(tmp, destino, true);
        // se dejan solo las últimas 3 versiones subidas
        foreach (var viejo in Directory.GetFiles(Carpeta, "SONA_Deposito_*.apk").OrderByDescending(f => Ver(Path.GetFileNameWithoutExtension(f))).Skip(3))
            try { File.Delete(viejo); } catch { }
        File.WriteAllText(ArchivoInfo, new JsonObject
        {
            ["version"] = v.ToString(), ["notas"] = notas ?? "", ["obligatoria"] = obligatoria, ["por"] = usuario, ["hora"] = Db.Ahora(),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        ev.Emitir(new() { ["tipo"] = "app_nueva", ["version"] = v.ToString(), ["obligatoria"] = obligatoria, ["notas"] = notas });
        return Version();
    }
}
