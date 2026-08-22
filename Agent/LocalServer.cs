using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NoxoParental;

public sealed class LocalServer : IDisposable
{
    private WebApplication? app;
    private readonly int port;
    private readonly Func<object> config;
    private readonly Action<string, string> log;

    public LocalServer(int port, Func<object> config, Action<string, string> log)
    {
        this.port = port;
        this.config = config;
        this.log = log;
    }

    public bool IsRunning { get; private set; }
    public int Port => port;
    public string Url => $"http://127.0.0.1:{port}";

    public void Start()
    {
        if (IsRunning) return;

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ApplicationName = typeof(LocalServer).Assembly.GetName().Name
            });

            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(Url);
            app = builder.Build();

            app.MapGet("/api/status", () => Results.Json(new
            {
                ok = true,
                service = "Noxo Parental Control",
                embedded = true,
                port,
                version = UpdateChecker.CurrentVersion.ToString(3),
                time = DateTimeOffset.UtcNow
            }));

            app.MapGet("/api/agent-config", () => Results.Json(config()));

            app.MapPost("/api/agent-event", async (HttpRequest request) =>
            {
                try
                {
                    var item = await JsonSerializer.DeserializeAsync<AgentEvent>(request.Body);
                    if (item != null && !string.IsNullOrWhiteSpace(item.Message))
                        log(item.Type ?? "agent", item.Message);
                }
                catch (JsonException) { }
                return Results.Json(new { ok = true });
            });

            app.MapGet("/", () => Results.Content(DashboardHtml(), "text/html; charset=utf-8"));
            app.MapGet("/dashboard", () => Results.Content(DashboardHtml(), "text/html; charset=utf-8"));

            app.StartAsync().GetAwaiter().GetResult();
            IsRunning = true;
            log("server", $"Serveur web intégré démarré sur {Url}");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            try { app?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            app = null;
            log("error", $"Impossible de démarrer le serveur web sur {Url} : {ex.Message}");
        }
    }

    private string DashboardHtml()
    {
        var html = @"<!doctype html>
<html lang='fr'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Noxo Parental Control</title>
<style>
:root { color-scheme: dark; }
* { box-sizing:border-box }
body { margin:0; font-family:Segoe UI,Arial,sans-serif; background:#070b14; color:#e5e7eb }
.wrap { max-width:1100px; margin:auto; padding:32px }
header { display:flex; justify-content:space-between; gap:20px; align-items:center; margin-bottom:24px }
h1 { margin:0; font-size:30px } .muted { color:#94a3b8 }
.grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(220px,1fr)); gap:16px }
.card { background:#111827; border:1px solid #243044; border-radius:18px; padding:20px; box-shadow:0 10px 30px #0003 }
.value { font-size:28px; font-weight:700; margin-top:8px }
.ok { color:#4ade80 } .err { color:#f87171 }
button { border:0; border-radius:10px; padding:10px 14px; background:#4f46e5; color:white; cursor:pointer; font-weight:600 }
button:hover { background:#6366f1 }
pre { white-space:pre-wrap; word-break:break-word; color:#cbd5e1 }
</style>
</head>
<body>
<div class='wrap'>
<header>
<div><h1>Noxo Parental Control</h1><div class='muted'>Dashboard local intégré à NoxoParental.exe</div></div>
<button onclick='refresh()'>Actualiser</button>
</header>
<div class='grid'>
<div class='card'><div class='muted'>État</div><div id='state' class='value'>Connexion...</div></div>
<div class='card'><div class='muted'>Version</div><div id='version' class='value'>—</div></div>
<div class='card'><div class='muted'>Port</div><div id='port' class='value'>__PORT__</div></div>
<div class='card'><div class='muted'>Limite quotidienne</div><div id='limit' class='value'>—</div></div>
</div>
<div class='card' style='margin-top:16px'><h2>Configuration</h2><pre id='config'>Chargement...</pre></div>
</div>
<script>
async function refresh() {
 try {
  const s=await fetch('/api/status',{cache:'no-store'}).then(r=>r.json());
  const c=await fetch('/api/agent-config',{cache:'no-store'}).then(r=>r.json());
  document.querySelector('#state').textContent=s.ok?'● Actif':'● Erreur';
  document.querySelector('#state').className='value '+(s.ok?'ok':'err');
  document.querySelector('#version').textContent=s.version||'—';
  document.querySelector('#port').textContent=s.port||'—';
  document.querySelector('#limit').textContent=(c.dailyLimitMinutes??c.daily_limit_minutes??'—')+' min';
  document.querySelector('#config').textContent=JSON.stringify(c,null,2);
 } catch(e) {
  document.querySelector('#state').textContent='● Erreur';
  document.querySelector('#state').className='value err';
  document.querySelector('#config').textContent='Impossible de contacter le serveur local.';
 }
}
refresh(); setInterval(refresh,5000);
</script>
</body></html>";

        return html.Replace("__PORT__", port.ToString());
    }

    public void Dispose()
    {
        IsRunning = false;
        try { app?.StopAsync().GetAwaiter().GetResult(); } catch { }
        try { app?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        app = null;
    }

    private sealed class AgentEvent
    {
        public string? Type { get; set; }
        public string? Message { get; set; }
    }
}
