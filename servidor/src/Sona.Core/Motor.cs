using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sona.Api;
using Sona.Les;

namespace Sona;

/// <summary>
/// El servidor de SONA Depósito (API para celulares y panel, base, cola hacia el WMS, tareas periódicas).
/// Lo arranca el panel WPF (modo integrado) o el programa de consola / contenedor Docker (modo servidor).
/// </summary>
public static class Motor
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static WebApplication Crear(Configuracion cfg, string[]? args = null, ILoggerProvider? logExtra = null, Action<WebApplicationBuilder>? configurar = null)
    {
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args ?? Array.Empty<string>(), ContentRootPath = AppContext.BaseDirectory });
        configurar?.Invoke(b);   // servidor sin pantalla: servicio de Windows / systemd
        b.WebHost.UseKestrel(o => { o.ListenAnyIP(cfg.Puerto); o.Limits.MaxRequestBodySize = 20 * 1024 * 1024; });
        b.Logging.ClearProviders();
        b.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        if (logExtra != null) b.Logging.AddProvider(logExtra);
        b.Logging.SetMinimumLevel(LogLevel.Information);
        b.Logging.AddFilter("Microsoft", LogLevel.Warning);

        b.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = Json.PropertyNamingPolicy;
            o.SerializerOptions.DictionaryKeyPolicy = null;
            o.SerializerOptions.NumberHandling = Json.NumberHandling;
            o.SerializerOptions.Encoder = Json.Encoder;
            o.SerializerOptions.PropertyNameCaseInsensitive = true;
        });
        b.Services.AddSingleton(cfg);
        b.Services.AddSingleton(_ => { var db = new Db(cfg.Carpeta); db.Iniciar(); return db; });
        b.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        b.Services.AddSingleton<Buscador>();
        b.Services.AddSingleton<FotosLes>();
        b.Services.AddSingleton<BultosLes>();
        b.Services.AddSingleton<IngresosLes>();
        b.Services.AddSingleton<Altas>();
        b.Services.AddSingleton<Servicios>();
        b.Services.AddSingleton<Eventos>();
        b.Services.AddSingleton<Movimientos>();
        b.Services.AddSingleton<Conteo>();
        b.Services.AddSingleton<Mensajes>();
        b.Services.AddSingleton<Reconocedor>();
        b.Services.AddSingleton<Picking>();
        b.Services.AddSingleton<Pedidos>();
        b.Services.AddSingleton<Produccion>();
        b.Services.AddSingleton<Ingresos>();
        b.Services.AddSingleton<AppMovil>();
        b.Services.AddSingleton<Permisos>();
        b.Services.AddSingleton<CatalogoArticulos>();
        b.Services.AddSingleton<FlujoDocumentos>();
        b.Services.AddSingleton<MisCredenciales>();
        b.Services.AddHostedService<IaFondo>();
        b.Services.AddSingleton<SincronizadorLayout>();
        b.Services.AddSingleton<Respaldo>();
        b.Services.AddHostedService<ColaWms>();
        b.Services.AddHostedService<TareasPeriodicas>();
        b.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        // detener rápido: 10 s como mucho para terminar lo que está en curso (el administrador de servicios no espera para siempre)
        b.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(10));
        b.WebHost.ConfigureKestrel(k => k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30));
        var app = b.Build();
        var db = app.Services.GetRequiredService<Db>();
        app.UseCors();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(25) });
        Rutas.Middleware(app, cfg, db);

        // Panel web (el mismo de antes) si está la carpeta wwwroot: sirve para entrar desde cualquier navegador.
        var web = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(web))
        {
            var fp = new PhysicalFileProvider(web);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fp });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fp });
        }

        Rutas.Mapear(app, cfg, db, app.Services.GetRequiredService<Servicios>(), app.Services.GetRequiredService<Movimientos>(),
            app.Services.GetRequiredService<Conteo>(), app.Services.GetRequiredService<SincronizadorLayout>(),
            app.Services.GetRequiredService<Respaldo>(), app.Services.GetRequiredService<Eventos>(), app.Services.GetRequiredService<Mensajes>(), app.Services.GetRequiredService<Reconocedor>(), app.Services.GetRequiredService<Picking>());

        app.Services.GetRequiredService<Permisos>().Sembrar();
        RutasProduccion.Mapear(app, cfg, app.Services.GetRequiredService<Produccion>());
        RutasIngresos.Mapear(app, app.Services.GetRequiredService<Ingresos>());
        var cred = app.Services.GetRequiredService<MisCredenciales>();
        cred.Migrar();
        RutasCredenciales.Mapear(app, cred);

        // Paneles web (desde cualquier navegador o celular): http://servidor:8080/supervisor (solo lectura) y /produccion (asignar pedidos)
        foreach (var (ruta, recurso) in new[] { ("/supervisor", "supervisor.html"), ("/produccion", "produccion.html"), ("/ingresos", "ingresos.html") })
            app.MapGet(ruta, async ctx =>
            {
                var asm = typeof(Motor).Assembly;
                var nombre = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(recurso, StringComparison.OrdinalIgnoreCase));
                if (nombre == null) { ctx.Response.StatusCode = 404; return; }
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-cache";
                await using var st = asm.GetManifestResourceStream(nombre)!;
                await st.CopyToAsync(ctx.Response.Body);
            });

        if (Directory.Exists(web))
            app.MapFallback(async ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api")) { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { error = "No existe" }); return; }
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.SendFileAsync(Path.Combine(web, "index.html"));
            });
        else app.MapGet("/", () => new { sistema = "SONA Depósito", version = Rutas.Version });
        return app;
    }
}
