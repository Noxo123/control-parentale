using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NoxoParental;

public sealed class LocalServer : IDisposable
{
    private readonly int port;
    private readonly SettingsStore settings;
    private readonly Action<string, string> log;
    private TcpListener? listener;
    private CancellationTokenSource? cts;
    private Task? loop;

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
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start(64);
            cts = new CancellationTokenSource();
            loop = Task.Run(() => AcceptLoopAsync(cts.Token));
            IsRunning = true;
            log("server", $"Serveur local démarré sur {Url}");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            listener = null;
            log("error", $"Port {port} indisponible : {ex.Message}");
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener != null)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { log("server", ex.Message); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            stream.ReadTimeout = 5000;
            stream.WriteTimeout = 5000;
            try
            {
                var buffer = new byte[65536];
                var length = await stream.ReadAsync(buffer, token);
                if (length <= 0) return;
                var request = Encoding.UTF8.GetString(buffer, 0, length);
                var headerEnd = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0) { await Send(stream, 400, "text/plain; charset=utf-8", "Bad Request"); return; }

                var header = request[..headerEnd];
                var body = request[(headerEnd + 4)..];
                var firstLine = header.Split("\r\n", StringSplitOptions.None)[0].Split(' ');
                if (firstLine.Length < 2) { await Send(stream, 400, "text/plain; charset=utf-8", "Bad Request"); return; }

                var method = firstLine[0];
                var path = firstLine[1].Split('?', 2)[0];
                var headers = ParseHeaders(header);

                // Read a larger JSON body when Content-Length says it exists.
                if (headers.TryGetValue("content-length", out var contentLengthText) && int.TryParse(contentLengthText, out var contentLength))
                {
                    contentLength = Math.Clamp(contentLength, 0, 65536);
                    while (Encoding.UTF8.GetByteCount(body) < contentLength)
                    {
                        var remaining = new byte[Math.Min(65536, contentLength - Encoding.UTF8.GetByteCount(body))];
                        var n = await stream.ReadAsync(remaining, token);
                        if (n <= 0) break;
                        body += Encoding.UTF8.GetString(remaining, 0, n);
                    }
                }

                if (method == "GET" && (path == "/" || path == "/dashboard"))
                {
                    await Send(stream, 200, "text/html; charset=utf-8", DashboardHtml());
                    return;
                }

                if (method == "GET" && path == "/api/status")
                {
                    await Json(stream, new { ok = true, service = "Noxo Parental Control", embedded = true, port, version = UpdateChecker.CurrentVersion.ToString(3), time = DateTimeOffset.UtcNow });
                    return;
                }

                if (method == "GET" && (path == "/api/settings" || path == "/api/agent-config"))
                {
                    await Json(stream, PublicSettings());
                    return;
                }

                if (method == "POST" && path == "/api/settings")
                {
                    if (!Authorized(headers)) { await Json(stream, new { error = "PIN parent requis." }, 401); return; }
                    var dto = JsonSerializer.Deserialize<SettingsDto>(body, JsonOptions);
                    if (dto is null || dto.DailyLimitMinutes is < 1 or > 1440) { await Json(stream, new { error = "Configuration invalide." }, 400); return; }
                    if (!TimeSpan.TryParse(dto.StartTime, out var start) || !TimeSpan.TryParse(dto.EndTime, out var end) || start.TotalHours >= 24 || end.TotalHours >= 24)
                    { await Json(stream, new { error = "Horaires invalides." }, 400); return; }
                    var apps = (dto.BlockedApps ?? []).Select(CleanApp).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
                    settings.Update(s => { s.DailyLimitMinutes = dto.DailyLimitMinutes; s.StartTime = start.ToString(@"hh\:mm"); s.EndTime = end.ToString(@"hh\:mm"); s.ObservationMode = dto.ObservationMode; s.BlockedApps = apps; });
                    log("settings", "Paramètres modifiés depuis le dashboard.");
                    await Json(stream, PublicSettings());
                    return;
                }

                if (method == "POST" && path == "/api/pin")
                {
                    if (!Authorized(headers)) { await Json(stream, new { error = "PIN parent requis." }, 401); return; }
                    var dto = JsonSerializer.Deserialize<PinDto>(body, JsonOptions);
                    if (dto?.Pin is null || dto.Pin.Length < 4 || dto.Pin.Length > 12 || !dto.Pin.All(char.IsDigit)) { await Json(stream, new { error = "PIN invalide." }, 400); return; }
                    settings.SetPin(dto.Pin);
                    log("security", "PIN parent modifié.");
                    await Json(stream, new { ok = true });
                    return;
                }

                if (method == "POST" && path == "/api/agent-event")
                {
                    try
                    {
                        var item = JsonSerializer.Deserialize<AgentEvent>(body, JsonOptions);
                        if (item != null && !string.IsNullOrWhiteSpace(item.Message)) log(item.Type ?? "agent", item.Message[..Math.Min(500, item.Message.Length)]);
                    }
                    catch (JsonException) { }
                    await Json(stream, new { ok = true });
                    return;
                }

                await Send(stream, 404, "text/plain; charset=utf-8", "Not Found");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { log("server", ex.Message); }
        }
    }

    private bool Authorized(Dictionary<string, string> headers) => headers.TryGetValue("x-parent-pin", out var pin) && settings.VerifyPin(pin);

    private static string CleanApp(string value)
    {
        var x = (value ?? "").Trim().ToLowerInvariant();
        if (x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) x = x[..^4];
        return x.Length is > 0 and <= 80 && x.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-') ? x : "";
    }

    private object PublicSettings()
    {
        var s = settings.Snapshot();
        return new { s.DailyLimitMinutes, s.StartTime, s.EndTime, s.BlockedApps, s.ObservationMode };
    }

    private static Dictionary<string, string> ParseHeaders(string header)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in header.Split("\r\n").Skip(1))
        {
            var i = line.IndexOf(':');
            if (i > 0) result[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return result;
    }

    private static async Task Json(NetworkStream stream, object data, int status = 200) => await Send(stream, status, "application/json; charset=utf-8", JsonSerializer.Serialize(data, JsonOptions));

    private static async Task Send(NetworkStream stream, int status, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var text = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", _ => "Error" };
        var head = $"HTTP/1.1 {status} {text}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        var header = Encoding.ASCII.GetBytes(head);
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private string DashboardHtml() => $@"<!doctype html><html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Noxo Parental Control</title><style>body{{font-family:Segoe UI,Arial;background:#080d18;color:#eef2ff;margin:0}}main{{max-width:900px;margin:auto;padding:24px}}nav{{display:flex;gap:8px;flex-wrap:wrap}}button{{background:#4f46e5;color:white;border:0;border-radius:9px;padding:10px 14px;cursor:pointer}}section{{background:#111827;border:1px solid #273449;border-radius:16px;padding:20px;margin-top:14px}}input{{background:#0b1220;color:white;border:1px solid #334155;border-radius:8px;padding:10px;margin:5px;width:calc(100% - 30px)}}.grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}}.value{{font-size:25px;font-weight:700}}.muted{{color:#94a3b8}}.danger{{background:#dc2626}}#msg{{padding:10px;background:#182236;border-radius:9px;margin-top:10px}}</style></head><body><main><h1>Noxo Parental Control</h1><p class='muted'>Serveur local • 127.0.0.1:{port}</p><nav><button onclick='load()'>Accueil</button><button onclick='save()'>Enregistrer</button><button onclick='pin()'>Modifier PIN</button></nav><section><div class='grid'><div><span class='muted'>Limite</span><div id='limit' class='value'>—</div></div><div><span class='muted'>Planning</span><div id='schedule' class='value'>—</div></div><div><span class='muted'>Applications</span><div id='count' class='value'>—</div></div></div><div id='msg'>Chargement...</div></section><section><h2>Réglages</h2><label>Minutes par jour<input id='daily' type='number' min='1' max='1440'></label><label>Début<input id='start' type='time'></label><label>Fin<input id='end' type='time'></label><label>Applications bloquées, une par ligne<textarea id='apps' style='width:calc(100% - 20px);height:120px;background:#0b1220;color:white;border:1px solid #334155;border-radius:8px;padding:10px'></textarea></label></section></main><script>let cfg=null,pinValue=sessionStorage.getItem('noxo_pin')||'';async function api(u,o={{}}){{o.headers=Object.assign({{}},o.headers||{{}},pinValue?{{'X-Parent-Pin':pinValue}}:{{}});let r=await fetch(u,Object.assign(o,{{cache:'no-store'}}));let d=await r.json().catch(()=>({{}}));if(r.status===401){{let p=prompt('PIN parent');if(!p)throw Error('PIN requis');pinValue=p;sessionStorage.setItem('noxo_pin',p);return api(u,o)}}if(!r.ok)throw Error(d.error||'Erreur');return d}}async function load(){{try{{cfg=await api('/api/settings');daily.value=cfg.dailyLimitMinutes;start.value=cfg.startTime;end.value=cfg.endTime;apps.value=cfg.blockedApps.join('\n');limit.textContent=cfg.dailyLimitMinutes+' min';schedule.textContent=cfg.startTime+' → '+cfg.endTime;count.textContent=cfg.blockedApps.length;msg.textContent='Serveur opérationnel. Version '+(await api('/api/status')).version}}catch(e){{msg.textContent=e.message}}}}async function save(){{if(!cfg)return;try{{await api('/api/settings',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{dailyLimitMinutes:+daily.value,startTime:start.value,endTime:end.value,observationMode:cfg.observationMode,blockedApps:apps.value.split('\n').map(x=>x.trim()).filter(Boolean)}})}});await load()}}catch(e){{msg.textContent=e.message}}}}async function pin(){{let p=prompt('Nouveau PIN (4-12 chiffres)');if(!/^\\d{{4,12}}$/.test(p||''))return;try{{await api('/api/pin',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{pin:p}})}});pinValue=p;sessionStorage.setItem('noxo_pin',p);alert('PIN modifié')}}catch(e){{alert(e.message)}}}}load();</script></body></html>";

    public void Dispose()
    {
        IsRunning = false;
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        cts?.Dispose();
        listener = null;
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
