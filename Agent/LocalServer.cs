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
    private readonly SettingsStore settings;
    private readonly Action<string, string> log;

    public LocalServer(int port, SettingsStore settings, Action<string, string> log)
    {
        this.port = port;
        this.settings = settings;
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

            app.Use(async (context, next) =>
            {
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                await next();
            });

            app.MapGet("/api/status", () => Results.Json(new
            {
                ok = true,
                service = "Noxo Parental Control",
                embedded = true,
                port,
                version = UpdateChecker.CurrentVersion.ToString(3),
                time = DateTimeOffset.UtcNow
            }));

            app.MapGet("/api/settings", () => Results.Json(PublicSettings()));

            app.MapPost("/api/settings", async (HttpRequest request) =>
            {
                if (!await IsAuthorized(request))
                    return Results.Unauthorized();

                var dto = await JsonSerializer.DeserializeAsync<SettingsDto>(request.Body);
                if (dto is null)
                    return Results.BadRequest(new { error = "Configuration invalide." });

                if (dto.DailyLimitMinutes is < 1 or > 1440)
                    return Results.BadRequest(new { error = "Limite quotidienne invalide." });

                if (!TimeSpan.TryParse(dto.StartTime, out var start) ||
                    !TimeSpan.TryParse(dto.EndTime, out var end) ||
                    start.TotalHours >= 24 || end.TotalHours >= 24)
                    return Results.BadRequest(new { error = "Horaires invalides." });

                var apps = (dto.BlockedApps ?? new List<string>())
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => x.Length > 0 && x.Length <= 80)
                    .Where(x => x.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(100)
                    .ToList();

                settings.Update(s =>
                {
                    s.DailyLimitMinutes = dto.DailyLimitMinutes;
                    s.StartTime = start.ToString(@"hh\:mm");
                    s.EndTime = end.ToString(@"hh\:mm");
                    s.ObservationMode = dto.ObservationMode;
                    s.BlockedApps = apps;
                });

                log("settings", "Paramètres modifiés depuis le dashboard.");
                return Results.Ok(PublicSettings());
            });

            app.MapPost("/api/pin", async (HttpRequest request) =>
            {
                if (!await IsAuthorized(request))
                    return Results.Unauthorized();

                var dto = await JsonSerializer.DeserializeAsync<PinDto>(request.Body);
                if (dto?.Pin is null || dto.Pin.Length < 4 || dto.Pin.Length > 12 || !dto.Pin.All(char.IsDigit))
                    return Results.BadRequest(new { error = "Le PIN doit contenir 4 à 12 chiffres." });

                settings.SetPin(dto.Pin);
                log("security", "PIN parent modifié.");
                return Results.Ok(new { ok = true });
            });

            app.MapGet("/api/agent-config", () => Results.Json(PublicSettings()));

            app.MapPost("/api/agent-event", async (HttpRequest request) =>
            {
                try
                {
                    var item = await JsonSerializer.DeserializeAsync<AgentEvent>(request.Body);
                    if (item != null && !string.IsNullOrWhiteSpace(item.Message))
                    {
                        var message = item.Message[..Math.Min(item.Message.Length, 500)];
                        log(item.Type ?? "agent", message);
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed agent telemetry.
                }

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

    private object PublicSettings()
    {
        var s = settings.Snapshot();
        return new
        {
            s.DailyLimitMinutes,
            s.StartTime,
            s.EndTime,
            s.BlockedApps,
            s.ObservationMode
        };
    }

    private Task<bool> IsAuthorized(HttpRequest request)
    {
        var pin = request.Headers["X-Parent-Pin"].FirstOrDefault();
        return Task.FromResult(!string.IsNullOrWhiteSpace(pin) && settings.VerifyPin(pin));
    }

    private string DashboardHtml()
    {
        const string html = """
<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Noxo Parental Control</title>
<style>
:root{color-scheme:dark;--bg:#070b14;--panel:#111827;--line:#263248;--text:#eef2ff;--muted:#94a3b8;--accent:#6366f1;--danger:#ef4444;--ok:#22c55e}
*{box-sizing:border-box}body{margin:0;background:linear-gradient(135deg,#070b14,#111827);color:var(--text);font-family:Segoe UI,Arial,sans-serif}
.app{max-width:1100px;margin:auto;padding:28px}.top{display:flex;justify-content:space-between;gap:15px;align-items:center;margin-bottom:22px}.brand{font-size:28px;font-weight:800}.muted{color:var(--muted)}
.tabs{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:16px}.tab,button{border:0;border-radius:10px;padding:10px 14px;background:#243044;color:white;font-weight:700;cursor:pointer}.tab.active{background:var(--accent)}
.view{display:none}.view.active{display:block}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:14px}.card{background:#111827ee;border:1px solid var(--line);border-radius:18px;padding:20px;margin-bottom:14px}.value{font-size:27px;font-weight:800;margin-top:7px}.ok{color:var(--ok)}.error{color:#fb7185}
label{display:block;margin:12px 0;color:var(--muted)}input{width:100%;padding:11px;border-radius:10px;border:1px solid var(--line);background:#0b1220;color:var(--text)}.row{display:flex;gap:10px;align-items:end}.row>*{flex:1}.danger{background:var(--danger)}.notice{padding:12px;border-radius:10px;background:#182236;margin-top:12px}.item{display:flex;justify-content:space-between;gap:10px;align-items:center;padding:10px;background:#0b1220;border-radius:10px;margin-top:8px}.toast{position:fixed;right:20px;bottom:20px;background:#182236;border:1px solid var(--line);padding:14px 18px;border-radius:12px;display:none}
@media(max-width:650px){.app{padding:16px}.row{display:block}}
</style>
</head>
<body>
<div class="app">
<header class="top"><div><div class="brand">Noxo Parental Control</div><div class="muted">Contrôle parental local • port __PORT__</div></div><button onclick="loadData()">Actualiser</button></header>
<nav class="tabs">
<button class="tab active" data-view="home">Accueil</button>
<button class="tab" data-view="time">Temps d'écran</button>
<button class="tab" data-view="apps">Applications</button>
<button class="tab" data-view="security">Sécurité</button>
<button class="tab" data-view="system">Système</button>
</nav>
<section id="home" class="view active"><div class="grid">
<div class="card"><div class="muted">Agent</div><div id="agent" class="value">Connexion...</div></div>
<div class="card"><div class="muted">Limite</div><div id="limit" class="value">—</div></div>
<div class="card"><div class="muted">Planning</div><div id="schedule" class="value">—</div></div>
<div class="card"><div class="muted">Applications</div><div id="count" class="value">—</div></div>
</div><div class="card"><h2>État</h2><div id="message" class="notice">Chargement…</div></div></section>
<section id="time" class="view"><div class="card"><h2>Temps d'écran</h2><label>Limite quotidienne (minutes)<input id="daily" type="number" min="1" max="1440"></label><div class="row"><label>Début<input id="start" type="time"></label><label>Fin<input id="end" type="time"></label></div><button onclick="saveSettings()">Enregistrer</button></div></section>
<section id="apps" class="view"><div class="card"><h2>Applications surveillées</h2><div class="row"><label>Processus<input id="newapp" placeholder="ex: discord"></label><button onclick="addApp()">Ajouter</button></div><div id="appslist"></div></div></section>
<section id="security" class="view"><div class="card"><h2>Sécurité</h2><label><input id="observation" type="checkbox" style="width:auto"> Mode observation</label><p class="muted">En mode observation, l'agent détecte les processus mais ne les ferme pas.</p><label>Nouveau PIN parent<input id="pin" type="password" inputmode="numeric" maxlength="12"></label><button onclick="changePin()">Changer le PIN</button></div></section>
<section id="system" class="view"><div class="card"><h2>Système</h2><p>Version : <b id="version">—</b></p><p>Serveur : <b id="server">—</b></p><button onclick="location.reload()">Recharger</button></div></section>
</div><div id="toast" class="toast"></div>
<script>
let cfg=null;
let parentPin=sessionStorage.getItem('noxo_parent_pin')||'';
const el=id=>document.getElementById(id);
function toast(message){el('toast').textContent=message;el('toast').style.display='block';setTimeout(()=>el('toast').style.display='none',2500)}
async function api(url,options={}){const headers=Object.assign({},options.headers||{});if(parentPin)headers['X-Parent-Pin']=parentPin;const response=await fetch(url,Object.assign({},options,{headers,cache:'no-store'}));const data=await response.json().catch(()=>({}));if(response.status===401){const p=prompt('PIN parent requis');if(!p)throw Error('PIN requis');parentPin=p;sessionStorage.setItem('noxo_parent_pin',p);return api(url,options)}if(!response.ok)throw Error(data.error||'Erreur serveur');return data}
async function loadData(){try{const status=await api('/api/status');cfg=await api('/api/settings');el('agent').textContent=status.ok?'● Actif':'● Erreur';el('agent').className='value '+(status.ok?'ok':'error');el('limit').textContent=cfg.dailyLimitMinutes+' min';el('schedule').textContent=cfg.startTime+' → '+cfg.endTime;el('count').textContent=cfg.blockedApps.length;el('daily').value=cfg.dailyLimitMinutes;el('start').value=cfg.startTime;el('end').value=cfg.endTime;el('observation').checked=cfg.observationMode;el('version').textContent=status.version;el('server').textContent='127.0.0.1:'+status.port;el('message').textContent='Serveur local opérationnel.';renderApps()}catch(error){el('message').textContent=error.message;toast(error.message)}}
async function saveSettings(){try{await api('/api/settings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({dailyLimitMinutes:Number(el('daily').value),startTime:el('start').value,endTime:el('end').value,observationMode:el('observation').checked,blockedApps:cfg?cfg.blockedApps:[]})});toast('Paramètres enregistrés');await loadData()}catch(error){toast(error.message)}}
async function addApp(){const name=el('newapp').value.trim().toLowerCase();if(!name)return;const list=cfg?cfg.blockedApps.slice():[];if(list.indexOf(name)<0)list.push(name);try{await api('/api/settings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({dailyLimitMinutes:cfg.dailyLimitMinutes,startTime:cfg.startTime,endTime:cfg.endTime,observationMode:el('observation').checked,blockedApps:list})});el('newapp').value='';await loadData()}catch(error){toast(error.message)}}
async function removeApp(name){const list=(cfg?cfg.blockedApps:[]).filter(x=>x!==name);try{await api('/api/settings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({dailyLimitMinutes:cfg.dailyLimitMinutes,startTime:cfg.startTime,endTime:cfg.endTime,observationMode:cfg.observationMode,blockedApps:list})});await loadData()}catch(error){toast(error.message)}}
function renderApps(){const list=cfg?cfg.blockedApps:[];el('appslist').innerHTML='';if(!list.length){el('appslist').innerHTML='<p class="muted">Aucune application configurée.</p>';return}list.forEach(name=>{const item=document.createElement('div');item.className='item';const text=document.createElement('span');text.textContent=name+'.exe';const button=document.createElement('button');button.className='danger';button.textContent='Retirer';button.onclick=()=>removeApp(name);item.append(text,button);el('appslist').appendChild(item)})}
async function changePin(){const value=el('pin').value;if(!/^\d{4,12}$/.test(value)){toast('PIN invalide');return}try{await api('/api/pin',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({pin:value})});parentPin=value;sessionStorage.setItem('noxo_parent_pin',value);el('pin').value='';toast('PIN modifié')}catch(error){toast(error.message)}}
document.querySelectorAll('.tab').forEach(button=>button.addEventListener('click',()=>{document.querySelectorAll('.tab').forEach(x=>x.classList.remove('active'));document.querySelectorAll('.view').forEach(x=>x.classList.remove('active'));button.classList.add('active');el(button.dataset.view).classList.add('active')}));
loadData();setInterval(loadData,10000);
</script>
</body>
</html>
""";

        return html.Replace("__PORT__", port.ToString());
    }

    public void Dispose()
    {
        IsRunning = false;
        try { app?.StopAsync().GetAwaiter().GetResult(); } catch { }
        try { app?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        app = null;
    }

    private sealed class SettingsDto
    {
        public int DailyLimitMinutes { get; set; }
        public string StartTime { get; set; } = "08:00";
        public string EndTime { get; set; } = "21:00";
        public bool ObservationMode { get; set; } = true;
        public List<string>? BlockedApps { get; set; }
    }

    private sealed class PinDto
    {
        public string? Pin { get; set; }
    }

    private sealed class AgentEvent
    {
        public string? Type { get; set; }
        public string? Message { get; set; }
    }
}
