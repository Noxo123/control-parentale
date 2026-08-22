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
                if (!await IsAuthorized(request)) return Results.Unauthorized();
                var dto = await JsonSerializer.DeserializeAsync<SettingsDto>(request.Body);
                if (dto is null) return Results.BadRequest(new { error = "Configuration invalide." });
                if (dto.DailyLimitMinutes is < 1 or > 1440) return Results.BadRequest(new { error = "Limite invalide." });
                if (!TimeSpan.TryParse(dto.StartTime, out _) || !TimeSpan.TryParse(dto.EndTime, out _))
                    return Results.BadRequest(new { error = "Horaires invalides." });

                settings.Update(s =>
                {
                    s.DailyLimitMinutes = dto.DailyLimitMinutes;
                    s.StartTime = dto.StartTime;
                    s.EndTime = dto.EndTime;
                    s.ObservationMode = dto.ObservationMode;
                    s.BlockedApps = dto.BlockedApps ?? [];
                });
                log("settings", "Paramètres modifiés depuis le dashboard.");
                return Results.Ok(PublicSettings());
            });

            app.MapPost("/api/pin", async (HttpRequest request) =>
            {
                if (!await IsAuthorized(request)) return Results.Unauthorized();
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
                        log(item.Type ?? "agent", item.Message[..Math.Min(item.Message.Length, 500)]);
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

    private object PublicSettings()
    {
        var s = settings.Snapshot();
        return new { s.DailyLimitMinutes, s.StartTime, s.EndTime, s.BlockedApps, s.ObservationMode };
    }

    private async Task<bool> IsAuthorized(HttpRequest request)
    {
        var pin = request.Headers["X-Parent-Pin"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(pin) && settings.VerifyPin(pin);
    }

    private string DashboardHtml()
    {
        return @"<!doctype html>
<html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Noxo Parental Control</title>
<style>
:root{color-scheme:dark;--bg:#070b14;--panel:#111827;--line:#263248;--text:#eef2ff;--muted:#94a3b8;--accent:#6366f1;--danger:#ef4444;--ok:#22c55e}*{box-sizing:border-box}body{margin:0;background:linear-gradient(135deg,#070b14,#111827);color:var(--text);font-family:Segoe UI,Arial,sans-serif}.app{max-width:1180px;margin:auto;padding:28px}.top{display:flex;justify-content:space-between;align-items:center;gap:16px;margin-bottom:24px}.brand{font-size:28px;font-weight:800}.muted{color:var(--muted)}.tabs{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:18px}.tab{background:#182236;border:1px solid var(--line)}.tab.active{background:var(--accent)}.view{display:none}.view.active{display:block}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:14px}.card{background:#111827dd;border:1px solid var(--line);border-radius:18px;padding:20px;box-shadow:0 15px 40px #0004}.value{font-size:27px;font-weight:800;margin-top:7px}.row{display:flex;gap:10px;align-items:center;flex-wrap:wrap}label{display:block;margin:12px 0;color:var(--muted)}input{width:100%;padding:11px;border-radius:10px;border:1px solid var(--line);background:#0b1220;color:var(--text)}button{border:0;border-radius:10px;padding:10px 14px;background:var(--accent);color:white;font-weight:700;cursor:pointer}button.secondary{background:#243044}button.danger{background:var(--danger)}.list{display:grid;gap:8px;margin-top:12px}.item{display:flex;justify-content:space-between;gap:10px;padding:11px;background:#0b1220;border-radius:10px}.ok{color:var(--ok)}.dangerText{color:#fb7185}.notice{padding:12px;border-radius:10px;background:#182236;margin-top:12px}.switch{display:flex;align-items:center;gap:10px}.switch input{width:auto}.toast{position:fixed;right:20px;bottom:20px;background:#182236;border:1px solid var(--line);padding:14px 18px;border-radius:12px;display:none;max-width:360px}
</style></head><body><div class='app'>
<div class='top'><div><div class='brand'>Noxo Parental Control</div><div class='muted'>Contrôle parental local • port __PORT__</div></div><button class='secondary' onclick='load()'>Actualiser</button></div>
<div class='tabs'><button class='tab active' data-v='home'>Accueil</button><button class='tab' data-v='time'>Temps d'écran</button><button class='tab' data-v='apps'>Applications</button><button class='tab' data-v='security'>Sécurité</button><button class='tab' data-v='system'>Système</button></div>
<section id='home' class='view active'><div class='grid'><div class='card'><div class='muted'>Agent</div><div id='agent' class='value'>Connexion...</div></div><div class='card'><div class='muted'>Limite</div><div id='limit' class='value'>—</div></div><div class='card'><div class='muted'>Planning</div><div id='schedule' class='value'>—</div></div><div class='card'><div class='muted'>Applications</div><div id='count' class='value'>—</div></div></div><div class='card' style='margin-top:14px'><h2>État</h2><div id='message' class='notice'>Chargement…</div></div></section>
<section id='time' class='view'><div class='card'><h2>Temps d'écran</h2><label>Limite quotidienne (minutes)<input id='daily' type='number' min='1' max='1440'></label><div class='row'><label style='flex:1'>Début<input id='start' type='time'></label><label style='flex:1'>Fin<input id='end' type='time'></label></div><button onclick='save()'>Enregistrer</button></div></section>
<section id='apps' class='view'><div class='card'><h2>Applications bloquées</h2><div class='row'><input id='newapp' placeholder='ex: discord' style='flex:1'><button onclick='addApp()'>Ajouter</button></div><div id='appslist' class='list'></div></div></section>
<section id='security' class='view'><div class='card'><h2>Sécurité</h2><div class='switch'><input id='observation' type='checkbox'><span>Mode observation (aucune fermeture de processus)</span></div><div class='notice'>Le dashboard est uniquement accessible sur 127.0.0.1. Les modifications demandent le PIN parent.</div><hr style='border-color:#263248'><label>Nouveau PIN parent<input id='pin' type='password' inputmode='numeric' maxlength='12'></label><button onclick='changePin()'>Changer le PIN</button></div></section>
<section id='system' class='view'><div class='card'><h2>Système</h2><p>Version : <b id='version'>—</b></p><p>Serveur : <b id='server'>—</b></p><button onclick='location.reload()'>Recharger l'interface</button></div></section>
</div><div id='toast' class='toast'></div>
<script>
let cfg=null;let pin=sessionStorage.getItem('noxo_pin')||'';
const $=id=>document.getElementById(id);function toast(t){$('toast').textContent=t;$('toast').style.display='block';setTimeout(()=>$('toast').style.display='none',2500)}
async function api(url,opt={}){opt.headers=Object.assign({'Content-Type':'application/json'},opt.headers||{});if(pin)opt.headers['X-Parent-Pin']=pin;let r=await fetch(url,Object.assign({cache:'no-store'},opt));let d=await r.json().catch(()=>({}));if(r.status===401){pin=prompt('PIN parent requis');if(pin){sessionStorage.setItem('noxo_pin',pin);return api(url,opt)}}if(!r.ok)throw Error(d.error||'Erreur serveur');return d}
async function load(){try{let s=await api('/api/status'),c=await api('/api/settings');cfg=c;$('agent').textContent=s.ok?'● Actif':'● Erreur';$('agent').className='value '+(s.ok?'ok':'dangerText');$('limit').textContent=c.dailyLimitMinutes+' min';$('schedule').textContent=c.startTime+' → '+c.endTime;$('count').textContent=c.blockedApps.length;$('daily').value=c.dailyLimitMinutes;$('start').value=c.startTime;$('end').value=c.endTime;$('observation').checked=c.observationMode;$('version').textContent=s.version;$('server').textContent='127.0.0.1:'+s.port;$('message').textContent='Serveur local opérationnel.';renderApps()}catch(e){$('message').textContent=e.message;toast(e.message)}}
async function save(){try{await api('/api/settings',{method:'POST',body:JSON.stringify({dailyLimitMinutes:+$('daily').value,startTime:$('start').value,endTime:$('end').value,observationMode:$('observation').checked,blockedApps:cfg?.blockedApps||[]})});toast('Paramètres enregistrés');await load()}catch(e){toast(e.message)}}
async function addApp(){let n=$('newapp').value.trim();if(!n)return;let a=[...(cfg?.blockedApps||[]),n];try{await api('/api/settings',{method:'POST',body:JSON.stringify({dailyLimitMinutes:+$('daily').value||120,startTime:$('start').value||'08:00',endTime:$('end').value||'21:00',observationMode:$('observation').checked,blockedApps:a})});$('newapp').value='';await load()}catch(e){toast(e.message)}}
async function removeApp(n){try{let a=(cfg.blockedApps||[]).filter(x=>x!==n);await api('/api/settings',{method:'POST',body:JSON.stringify({dailyLimitMinutes:cfg.dailyLimitMinutes,startTime:cfg.startTime,endTime:cfg.endTime,observationMode:cfg.observationMode,blockedApps:a})});await load()}catch(e){toast(e.message)}}
function renderApps(){$('appslist').innerHTML=(cfg?.blockedApps||[]).map(x=>`<div class='item'><span>${esc(x)}.exe</span><button class='danger' onclick='removeApp(${JSON.stringify(x)})'>Retirer</button></div>`).join('')||'<div class="muted">Aucune application configurée.</div>'}
async function changePin(){let p=$('pin').value;if(!/^\d{4,12}$/.test(p))return toast('PIN invalide');try{await api('/api/pin',{method:'POST',body:JSON.stringify({pin:p})});pin=p;sessionStorage.setItem('noxo_pin',pin);$('pin').value='';toast('PIN modifié')}catch(e){toast(e.message)}}
function esc(x){return String(x).replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]))}
document.querySelectorAll('.tab').forEach(b=>b.onclick=()=>{document.querySelectorAll('.tab').forEach(x=>x.classList.remove('active'));document.querySelectorAll('.view').forEach(x=>x.classList.remove('active'));b.classList.add('active');$(b.dataset.v).classList.add('active')});load();setInterval(load,10000);
</script></body></html>".Replace("__PORT__", port.ToString());
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
    private sealed class PinDto { public string? Pin { get; set; } }
    private sealed class AgentEvent { public string? Type { get; set; } public string? Message { get; set; } }
}
