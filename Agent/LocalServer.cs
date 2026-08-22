using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NoxoParental;

public sealed class LocalServer : IDisposable
{
    private readonly int port;
    private readonly SettingsStore settings;
    private readonly PairingStore pairing;
    private readonly Action<string, string> log;
    private TcpListener? listener;
    private CancellationTokenSource? cts;
    private Task? loop;

    public LocalServer(int port, SettingsStore settings, PairingStore pairing, Action<string, string> log)
    {
        this.port = port;
        this.settings = settings;
        this.pairing = pairing;
        this.log = log;
    }

    public bool IsRunning { get; private set; }
    public int Port => port;
    public string Url => $"http://{GetLocalAddress()}:{port}";

    public void Start()
    {
        if (IsRunning) return;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(64);
            cts = new CancellationTokenSource();
            loop = Task.Run(() => AcceptLoopAsync(cts.Token));
            IsRunning = true;
            log("server", $"Serveur parent démarré sur {Url}");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            listener = null;
            log("error", $"Port {port} indisponible : {ex.Message}");
        }
    }

    private static string GetLocalAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x));
            return ip?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
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
        using var stream = client.GetStream();
        try
        {
            stream.ReadTimeout = 5000;
            stream.WriteTimeout = 5000;
            var buffer = new byte[65536];
            var length = await stream.ReadAsync(buffer, token);
            if (length <= 0) return;
            var request = Encoding.UTF8.GetString(buffer, 0, length);
            var headerEnd = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0) { await Send(stream, 400, "text/plain; charset=utf-8", "Bad Request"); return; }
            var header = request[..headerEnd];
            var body = request[(headerEnd + 4)..];
            var first = header.Split("\r\n", StringSplitOptions.None)[0].Split(' ');
            if (first.Length < 2) { await Send(stream, 400, "text/plain; charset=utf-8", "Bad Request"); return; }
            var method = first[0];
            var path = first[1].Split('?', 2)[0];
            var headers = ParseHeaders(header);

            if (headers.TryGetValue("content-length", out var lenText) && int.TryParse(lenText, out var len))
            {
                len = Math.Clamp(len, 0, 65536);
                var current = Encoding.UTF8.GetByteCount(body);
                while (current < len)
                {
                    var remaining = new byte[Math.Min(8192, len - current)];
                    var n = await stream.ReadAsync(remaining, token);
                    if (n <= 0) break;
                    body += Encoding.UTF8.GetString(remaining, 0, n);
                    current += n;
                }
            }

            if (method == "GET" && (path == "/" || path == "/dashboard"))
            {
                await Send(stream, 200, "text/html; charset=utf-8", DashboardHtml());
                return;
            }

            if (method == "GET" && path == "/api/status")
            {
                await Json(stream, new { ok = true, role = "parent", paired = pairing.IsPaired, port, version = UpdateChecker.CurrentVersion.ToString(3), time = DateTimeOffset.UtcNow });
                return;
            }

            if (method == "POST" && path == "/api/pair")
            {
                var dto = JsonSerializer.Deserialize<PairDto>(body, JsonOptions);
                if (dto is null || string.IsNullOrWhiteSpace(dto.Code) || !pairing.TryPair(dto.Code, out var childToken))
                {
                    await Json(stream, new { error = "Code invalide ou expiré." }, 401); return;
                }
                log("pair", "Un appareil enfant vient d'être associé.");
                await Json(stream, new { ok = true, token = childToken });
                return;
            }

            if (method == "GET" && path == "/api/settings")
            {
                if (!IsParent(headers) && !IsChild(headers)) { await Json(stream, new { error = "Association requise." }, 401); return; }
                await Json(stream, PublicSettings());
                return;
            }

            if (method == "POST" && path == "/api/settings")
            {
                if (!IsParent(headers)) { await Json(stream, new { error = "Accès parent requis." }, 401); return; }
                var dto = JsonSerializer.Deserialize<SettingsDto>(body, JsonOptions);
                if (dto is null || dto.DailyLimitMinutes is < 1 or > 1440) { await Json(stream, new { error = "Configuration invalide." }, 400); return; }
                if (!TimeSpan.TryParse(dto.StartTime, out var start) || !TimeSpan.TryParse(dto.EndTime, out var end) || start.TotalHours >= 24 || end.TotalHours >= 24)
                { await Json(stream, new { error = "Horaires invalides." }, 400); return; }
                var apps = (dto.BlockedApps ?? []).Select(CleanApp).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
                settings.Update(s => { s.DailyLimitMinutes = dto.DailyLimitMinutes; s.StartTime = start.ToString(@"hh\:mm"); s.EndTime = end.ToString(@"hh\:mm"); s.ObservationMode = dto.ObservationMode; s.BlockedApps = apps; });
                log("settings", "Règles modifiées par le parent.");
                await Json(stream, PublicSettings());
                return;
            }

            if (method == "POST" && path == "/api/pin")
            {
                if (!IsParent(headers)) { await Json(stream, new { error = "Accès parent requis." }, 401); return; }
                var dto = JsonSerializer.Deserialize<PinDto>(body, JsonOptions);
                if (dto?.Pin is null || dto.Pin.Length < 4 || dto.Pin.Length > 12 || !dto.Pin.All(char.IsDigit)) { await Json(stream, new { error = "PIN invalide." }, 400); return; }
                settings.SetPin(dto.Pin);
                await Json(stream, new { ok = true });
                return;
            }

            await Send(stream, 404, "text/plain; charset=utf-8", "Not Found");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log("server", ex.Message); }
    }

    private bool IsParent(Dictionary<string, string> headers) => headers.TryGetValue("x-parent-pin", out var pin) && settings.VerifyPin(pin);
    private bool IsChild(Dictionary<string, string> headers) => headers.TryGetValue("x-child-token", out var token) && pairing.ValidateToken(token);

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
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(bytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private string DashboardHtml() => $@"<!doctype html><html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Noxo Parental</title><style>body{{font-family:Segoe UI,Arial;background:#080d18;color:#eef2ff;margin:0}}main{{max-width:900px;margin:auto;padding:28px}}.hero{{background:#121a2a;border-radius:20px;padding:24px}}.code{{font-size:44px;letter-spacing:8px;font-weight:800}}button,input{{border:0;border-radius:10px;padding:12px;margin:4px}}button{{background:#4f46e5;color:white;cursor:pointer}}input{{background:#202b3f;color:white}}.card{{background:#121a2a;border:1px solid #26344d;border-radius:16px;padding:18px;margin-top:14px}}.muted{{color:#94a3b8}}</style></head><body><main><div class='hero'><div class='muted'>MODE PARENT</div><h1>Contrôle parental</h1><p>Associe un appareil enfant avec un code temporaire.</p><div id='code' class='code'>------</div><button onclick='generate()'>Nouveau code</button></div><div class='card'><h2>État</h2><div id='state'>Chargement...</div><p class='muted'>Le serveur écoute sur le réseau local. Le code expire après 10 minutes.</p></div></main><script>async function generate(){{let r=await fetch('/api/status');let s=await r.json();document.getElementById('state').textContent=s.paired?'Un appareil est associé.':'Aucun appareil associé.'}}generate();</script></body></html>";

    public void Dispose()
    {
        IsRunning = false;
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        cts?.Dispose(); listener = null;
    }

    private sealed class PairDto { public string? Code { get; set; } }
    private sealed class SettingsDto { public int DailyLimitMinutes { get; set; } public string StartTime { get; set; } = "08:00"; public string EndTime { get; set; } = "21:00"; public bool ObservationMode { get; set; } = true; public List<string>? BlockedApps { get; set; } }
    private sealed class PinDto { public string? Pin { get; set; } }
}
