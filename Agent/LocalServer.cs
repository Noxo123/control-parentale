using System.Net;
using System.Text;
using System.Text.Json;

namespace NoxoParental;

public sealed class LocalServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly int port;
    private readonly Func<object> config;
    private readonly Action<string, string> log;
    private CancellationTokenSource? cts;

    public LocalServer(int port, Func<object> config, Action<string, string> log)
    {
        this.port = port;
        this.config = config;
        this.log = log;
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public bool IsRunning => listener.IsListening;

    public void Start()
    {
        if (listener.IsListening) return;
        cts = new CancellationTokenSource();
        try
        {
            listener.Start();
            _ = Task.Run(() => LoopAsync(cts.Token));
            log("server", $"Serveur local démarré sur http://127.0.0.1:{port}");
        }
        catch (HttpListenerException ex)
        {
            log("error", $"Impossible d'ouvrir le port {port} : {ex.Message}");
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleAsync(context), token);
            }
            catch when (token.IsCancellationRequested || !listener.IsListening) { }
            catch (Exception ex) { log("error", $"Serveur local : {ex.Message}"); }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (path == "/api/status")
                await Json(context, new { ok = true, service = "Noxo Parental Control", embedded = true, port });
            else if (path == "/api/agent-config")
                await Json(context, config());
            else if (path == "/api/agent-event" && context.Request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                try
                {
                    var item = JsonSerializer.Deserialize<AgentEvent>(body);
                    if (item != null) log(item.Type ?? "agent", item.Message ?? "");
                }
                catch { }
                await Json(context, new { ok = true });
            }
            else if (path == "/")
                await Html(context);
            else
            {
                context.Response.StatusCode = 404;
                await Json(context, new { error = "Not found" });
            }
        }
        catch (Exception ex) { log("error", $"Requête locale : {ex.Message}"); }
        finally { try { context.Response.Close(); } catch { } }
    }

    private static async Task Json(HttpListenerContext c, object value)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        c.Response.ContentType = "application/json; charset=utf-8";
        c.Response.ContentLength64 = bytes.Length;
        await c.Response.OutputStream.WriteAsync(bytes);
    }

    private static async Task Html(HttpListenerContext c)
    {
        const string html = "<!doctype html><html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Noxo Parental Control</title><style>body{margin:0;background:#0b1220;color:#e5e7eb;font-family:Segoe UI,Arial}.wrap{max-width:900px;margin:60px auto;padding:24px}.card{background:#111827;border:1px solid #243044;border-radius:18px;padding:24px}h1{margin:0 0 8px}.ok{color:#4ade80}.muted{color:#94a3b8}code{background:#020617;padding:4px 8px;border-radius:6px}</style></head><body><div class='wrap'><div class='card'><h1>Noxo Parental Control</h1><div class='ok'>● Serveur local actif</div><p class='muted'>Le serveur est intégré directement dans NoxoParental.exe.</p><p>API : <code>/api/status</code> · <code>/api/agent-config</code></p></div></div></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        c.Response.ContentType = "text/html; charset=utf-8";
        c.Response.ContentLength64 = bytes.Length;
        await c.Response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        try { cts?.Cancel(); } catch { }
        try { listener.Stop(); listener.Close(); } catch { }
        cts?.Dispose();
    }

    private sealed class AgentEvent { public string? Type { get; set; } public string? Message { get; set; } }
}
