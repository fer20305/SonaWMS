using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sona.Servidor;

/// <summary>Registro del servidor en archivos diarios (carpeta de datos › registro), para ver qué pasó cuando corre como servicio. Guarda 30 días.</summary>
public sealed class RegistroArchivo : ILoggerProvider
{
    readonly string _dir;
    readonly BlockingCollection<string> _cola = new(10000);
    readonly Thread _hilo;

    public RegistroArchivo(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        try { foreach (var f in Directory.GetFiles(dir, "servidor_*.log").OrderByDescending(f => f).Skip(30)) File.Delete(f); } catch { }
        _hilo = new Thread(Escribir) { IsBackground = true, Name = "registro" };
        _hilo.Start();
    }

    void Escribir()
    {
        foreach (var l in _cola.GetConsumingEnumerable())
            try { File.AppendAllText(Path.Combine(_dir, $"servidor_{DateTime.Now:yyyyMMdd}.log"), l + Environment.NewLine); } catch { }
    }

    public ILogger CreateLogger(string categoria) => new Logger(this, categoria);
    public void Dispose() { _cola.CompleteAdding(); _hilo.Join(2000); }

    sealed class Logger(RegistroArchivo p, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel nivel) => nivel >= LogLevel.Information && !(categoria.StartsWith("Microsoft") && nivel < LogLevel.Warning);
        public void Log<TState>(LogLevel nivel, EventId id, TState state, Exception? e, Func<TState, Exception?, string> fmt)
        {
            if (!IsEnabled(nivel)) return;
            var cat = categoria[(categoria.LastIndexOf('.') + 1)..];
            p._cola.TryAdd($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {nivel.ToString()[..4].ToUpperInvariant()} {cat}: {fmt(state, e)}{(e != null ? " | " + e : "")}");
        }
    }
}
