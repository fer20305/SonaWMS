// SONA Depósito – servidor sin pantalla (servicio de Windows, servicio de Linux con systemd, o Docker).
// La configuración vive en la carpeta de datos (SONA_DATOS; en Windows C:\ProgramData\SONA\Deposito, en Linux ./datos o lo que diga
// SONA_DATOS, en Docker /datos) y se edita desde el panel (ZCFG) o desde el panel web.
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sona;
using Sona.Servidor;

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

var cfg = Configuracion.Cargar();
var registro = Path.Combine(cfg.Carpeta, "registro");
Directory.CreateDirectory(registro);
void Fallo(string msj)
{
    var t = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} NO ARRANCÓ: {msj}";
    Console.Error.WriteLine("\n  " + t + "\n");
    try { File.AppendAllText(Path.Combine(registro, "arranque_error.txt"), t + Environment.NewLine); } catch { }
}
AppDomain.CurrentDomain.UnhandledException += (_, e) => Fallo(e.ExceptionObject?.ToString() ?? "error desconocido");

// el puerto ocupado es la causa más común (el panel SonaDeposito.exe en modo integrado ya usa el 8080 en esa PC)
try { var l = new TcpListener(IPAddress.Any, cfg.Puerto); l.Start(); l.Stop(); }
catch (SocketException)
{
    Fallo($"el puerto {cfg.Puerto} ya está en uso en esta PC. Cerrá el panel SonaDeposito.exe (o pasalo a modo remoto) " +
          $"o cambiá el puerto con la variable SONA_PUERTO. Datos: {cfg.Carpeta}");
    return 2;
}

WebApplication app;
try
{
    app = Motor.Crear(cfg, args, new RegistroArchivo(registro), b =>
    {
        // si lo arranca el administrador de servicios de Windows o systemd, se comporta como servicio; si no, como programa de consola
        b.Services.AddWindowsService(o => o.ServiceName = "SonaDeposito");
        b.Services.AddSystemd();
    });
}
catch (Exception e) { Fallo(e.ToString()); return 1; }
Console.WriteLine($"""

  SONA Depósito {Sona.Api.Rutas.Version} – servidor
  Puerto:   {cfg.Puerto}
  Datos:    {cfg.Carpeta}
  Registro: {registro}

""");
try { app.Run(); }
catch (OperationCanceledException)
{
    // no es un error de arranque: el servicio se detuvo y algo tardó más de la cuenta en cerrar
    try { File.AppendAllText(Path.Combine(registro, "arranque_error.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} (aviso) el servicio se detuvo; tardó en cerrar algunas conexiones.{Environment.NewLine}"); } catch { }
    return 0;
}
catch (Exception e) { Fallo(e.ToString()); return 1; }
return 0;
