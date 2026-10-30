using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Sona;

/// <summary>Avisos en tiempo real a panel y celulares (movimientos, autorizaciones, conteos, layout).</summary>
public sealed class Eventos
{
    sealed record Cliente(WebSocket Ws, string Usuario, string Rol, SemaphoreSlim Envio);
    readonly ConcurrentDictionary<Guid, Cliente> _clientes = new();

    /// <summary>También lo escucha el panel de escritorio cuando corre en la misma PC.</summary>
    public event Action<JsonElement>? Local;

    public int Conectados => _clientes.Count;

    public async Task Atender(WebSocket ws, string usuario, string rol, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var c = new Cliente(ws, usuario, rol, new SemaphoreSlim(1, 1));
        _clientes[id] = c;
        try
        {
            await Enviar(c, JsonSerializer.Serialize(new { tipo = "hola", usuario }));
            var buf = new byte[1024];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var r = await ws.ReceiveAsync(buf, ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch { /* desconexión */ }
        finally
        {
            _clientes.TryRemove(id, out _);
            try { if (ws.State == WebSocketState.Open) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }
    }

    static async Task Enviar(Cliente c, string msg)
    {
        await c.Envio.WaitAsync();
        try { await c.Ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { c.Envio.Release(); }
    }

    public void Emitir(Dictionary<string, object?> evento, Func<string, string, bool>? filtro = null)
    {
        evento["hora"] = Db.Ahora();
        var msg = JsonSerializer.Serialize(evento);
        foreach (var (id, c) in _clientes)
        {
            if (filtro != null && !filtro(c.Usuario, c.Rol)) continue;
            _ = Task.Run(async () => { try { await Enviar(c, msg); } catch { _clientes.TryRemove(id, out _); } });
        }
        try { using var d = JsonDocument.Parse(msg); Local?.Invoke(d.RootElement.Clone()); } catch { }
    }
}
