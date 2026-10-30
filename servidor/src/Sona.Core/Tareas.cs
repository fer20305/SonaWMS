using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sona.Dominio;
using Sona.Wms;

namespace Sona;

public sealed class ResumenLayout
{
    public int Caras { get; set; }
    public int Posiciones { get; set; }
    public List<string> Errores { get; set; } = new();
    public string Inicio { get; set; } = Db.Ahora();
    public string? Fin { get; set; }
}

/// <summary>
/// Trae del WMS web ("Ubicaciones") la ficha de cada posición: Ubic.Ctrl, medidas, capacidad, estado y artículo.
/// No toca la configuración propia (tipo, cliente, rol de zona, mezcla, habilitada).
/// </summary>
public sealed class SincronizadorLayout(Configuracion cfg, Servicios sv, Eventos ev, ILogger<SincronizadorLayout> log)
{
    readonly Db _db = sv.Db;
    Task<ResumenLayout>? _corriendo;
    public ResumenLayout? Ultima { get; private set; }

    public Task<ResumenLayout> Sincronizar(string? usuario = null, List<string>? caras = null)
    {
        lock (this) { return _corriendo ??= Hacer(usuario, caras).ContinueWith(t => { _corriendo = null; return t.Result; }); }
    }

    (string U, string C) Credenciales(string? usuario)
    {
        var candidatos = new List<string>();
        if (usuario != null) candidatos.Add(usuario.ToUpperInvariant());
        candidatos.AddRange(cfg.Admins.Select(a => a.ToUpperInvariant()));
        foreach (var u in candidatos)
            if (_db.Q1("SELECT clave_cifrada FROM usuarios WHERE usuario=$1", u)?.S("clave_cifrada") is { Length: > 0 } c) return (u, Seguridad.Descifrar(c, cfg.Secreto));
        var otro = _db.Q1("SELECT usuario, clave_cifrada FROM usuarios WHERE rol IN ('admin','analista') AND clave_cifrada IS NOT NULL ORDER BY ultimo_acceso DESC LIMIT 1");
        if (otro != null) return (otro.S("usuario")!, Seguridad.Descifrar(otro.S("clave_cifrada")!, cfg.Secreto));
        throw new ErrorUsuario("Para sincronizar el layout tiene que haber ingresado al menos un analista o administrador.");
    }

    async Task<ResumenLayout> Hacer(string? usuario, List<string>? caras)
    {
        var res = new ResumenLayout();
        try
        {
            var (u, c) = Credenciales(usuario);
            var web = new SesionWeb(cfg, u, c);
            var lista = caras is { Count: > 0 } ? caras : _db.Q("SELECT cara FROM zonas WHERE tipo <> 'otro_deposito' ORDER BY cara").Select(f => f.S("cara")!).ToList();
            ev.Emitir(new() { ["tipo"] = "layout", ["estado"] = "inicio", ["caras"] = lista.Count });
            foreach (var cara in lista)
            {
                try
                {
                    var filas = await web.LayoutDeCara(cara);
                    foreach (var f in filas) Guardar(f);
                    res.Caras++; res.Posiciones += filas.Count;
                    ev.Emitir(new() { ["tipo"] = "layout", ["estado"] = "avance", ["cara"] = cara, ["filas"] = filas.Count });
                }
                catch (Exception e) { res.Errores.Add($"{cara}: {e.Message}"); }
            }
        }
        catch (Exception e) { res.Errores.Add(e.Message); }
        res.Fin = Db.Ahora();
        Ultima = res;
        sv.OlvidarCaras();
        ev.Emitir(new() { ["tipo"] = "layout", ["estado"] = "fin", ["caras"] = res.Caras, ["posiciones"] = res.Posiciones });
        log.LogInformation("Layout sincronizado: {Caras} caras, {Pos} posiciones, {Err} errores", res.Caras, res.Posiciones, res.Errores.Count);
        return res;
    }

    void Guardar(FilaLayout f) => Servicios.GuardarFilaLayout(_db, f);
}

/// <summary>Copia de seguridad diaria de la base (copia consistente del archivo SQLite, comprimida). Se guardan 30.</summary>
public sealed class Respaldo(Configuracion cfg, Db db, ILogger<Respaldo> log)
{
    public string Hacer()
    {
        var dir = Path.Combine(cfg.Carpeta, "respaldos");
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "tmp.db");
        if (File.Exists(tmp)) File.Delete(tmp);
        db.Ejecutar("VACUUM INTO $1", tmp);   // copia consistente aunque el servidor esté trabajando
        var archivo = Path.Combine(dir, $"sona_{DateTime.Now:yyyyMMdd_HHmm}.zip");
        using (var z = ZipFile.Open(archivo, ZipArchiveMode.Create)) z.CreateEntryFromFile(tmp, "sona.db", CompressionLevel.Optimal);
        File.Delete(tmp);
        foreach (var viejo in Directory.GetFiles(dir, "sona_*.zip").OrderByDescending(f => f).Skip(30)) File.Delete(viejo);
        log.LogInformation("Respaldo creado: {Archivo}", archivo);
        return archivo;
    }
}

/// <summary>Tareas periódicas: sincronizar el layout y respaldar la base.</summary>
public sealed class TareasPeriodicas(Configuracion cfg, SincronizadorLayout layout, Respaldo respaldo, ILogger<TareasPeriodicas> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var proximoRespaldo = DateTime.Now.AddMinutes(1);
        var proximoLayout = cfg.SincronizarLayoutHoras > 0 ? DateTime.Now.AddHours(cfg.SincronizarLayoutHoras) : DateTime.MaxValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTime.Now >= proximoRespaldo) { respaldo.Hacer(); proximoRespaldo = DateTime.Now.AddDays(1); }
                if (DateTime.Now >= proximoLayout) { await layout.Sincronizar(); proximoLayout = DateTime.Now.AddHours(Math.Max(1, cfg.SincronizarLayoutHoras)); }
            }
            catch (Exception e) { log.LogWarning("Tarea periódica: {E}", e.Message); proximoRespaldo = DateTime.Now.AddHours(1); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { break; }
        }
    }
}
