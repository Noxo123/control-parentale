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
            IsRunning = true;
            _ = AcceptLoopAsync(cts.Token);
            log("server", $"Serveur parent démarré sur {Url}");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            try { listener?.Stop(); } catch { }
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
                _ = HandleClientAsync(client, token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { log("server", ex.Message); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                var buffer = new byte[65536];
                var length = await stream.ReadAsync(buffer, token);
                if (length <= 0) return;

                var request = Encoding.UTF8.GetString(buffer, 0, length);
                var headerEnd = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0)
                {
                    await SendAsync(stream, 400, "text/plain; charset=utf-8", "Bad Request", token);
                    return;
                }

                var header = request[..headerEnd];
                var body = request[(headerEnd + 4)..];
                var firstLine = header.Split("\r\n", StringSplitOptions.None)[0];
                var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    await SendAsync(stream, 400, "text/plain; charset=utf-8", "Bad Request", token);
                    return;
                }

                var method = parts[0];
                var path = parts[1].Split('?', 2)[0];
                var headers = ParseHeaders(header);

                if (headers.TryGetValue("content-length", out var lengthText) && int.TryParse(lengthText, out var contentLength))
                {
                    contentLength = Math.Clamp(contentLength, 0, 65536);
                    var current = Encoding.UTF8.GetByteCount(body);
                    while (current < contentLength)
                    {
                        var remaining = new byte[Math.Min(8192, contentLength - current)];
                        var read = await stream.ReadAsync(remaining, token);
                        if (read <= 0) break;
                        body += Encoding.UTF8.GetString(remaining, 0, read);
                        current += read;
                    }
                }

                if (method == "GET" && (path == "/" || path == "/dashboard"))
                {
                    await SendAsync(stream, 200, "text/html; charset=utf-8", DashboardHtml(), token);
                    return;
                }

                if (method == "GET" && path == "/api/status")
                {
                    await SendJsonAsync(stream, new
                    {
                        ok = true,
                        role = "parent",
                        paired = pairing.IsPaired,
                        pairingCode = pairing.Code,
                        port,
                        version = UpdateChecker.CurrentVersion.ToString(3)
                    }, token);
                    return;
                }

                if (method == "POST" && path == "/api/pair")
                {
                    var dto = JsonSerializer.Deserialize<PairDto>(body, JsonOptions);
                    if (dto == null || string.IsNullOrWhiteSpace(dto.Code) || !pairing.TryPair(dto.Code, out var childToken))
                    {
                        await SendJsonAsync(stream, new { error = "Code invalide ou expiré." }, token, 401);
                        return;
                    }

                    log("pair", "Un appareil enfant vient d'être associé.");
                    await SendJsonAsync(stream, new { ok = true, token = childToken }, token);
                    return;
                }

                if (method == "GET" && path == "/api/settings")
                {
                    if (!IsChild(headers))
                    {
                        await SendJsonAsync(stream, new { error = "Association requise." }, token, 401);
                        return;
                    }

                    await SendJsonAsync(stream, PublicSettings(), token);
                    return;
                }

                if (method == "POST" && path == "/api/settings")
                {
                    if (!IsParent(headers))
                    {
                        await SendJsonAsync(stream, new { error = "Accès parent requis." }, token, 401);
                        return;
                    }

                    var dto = JsonSerializer.Deserialize<SettingsDto>(body, JsonOptions);
                    if (dto == null || dto.DailyLimitMinutes is < 1 or > 1440)
                    {
                        await SendJsonAsync(stream, new { error = "Configuration invalide." }, token, 400);
                        return;
                    }

                    if (!TimeSpan.TryParse(dto.StartTime, out var start) || !TimeSpan.TryParse(dto.EndTime, out var end) || start.TotalHours >= 24 || end.TotalHours >= 24)
                    {
                        await SendJsonAsync(stream, new { error = "Horaires invalides." }, token, 400);
                        return;
                    }

                    var apps = (dto.BlockedApps ?? new List<string>())
                        .Select(CleanApp)
                        .Where(x => x.Length > 0)
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

                    log("settings", "Règles modifiées par le parent.");
                    await SendJsonAsync(stream, PublicSettings(), token);
                    return;
                }

                if (method == "POST" && path == "/api/pin")
                {
                    if (!IsParent(headers))
                    {
                        await SendJsonAsync(stream, new { error = "Accès parent requis." }, token, 401);
                        return;
                    }

                    var dto = JsonSerializer.Deserialize<PinDto>(body, JsonOptions);
                    if (dto?.Pin == null || dto.Pin.Length < 4 || dto.Pin.Length > 12 || !dto.Pin.All(char.IsDigit))
                    {
                        await SendJsonAsync(stream, new { error = "PIN invalide." }, token, 400);
                        return;
                    }

                    settings.SetPin(dto.Pin);
                    await SendJsonAsync(stream, new { ok = true }, token);
                    return;
                }

                await SendAsync(stream, 404, "text/plain; charset=utf-8", "Not Found", token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log("server", ex.Message);
            }
        }
    }

    private bool IsParent(Dictionary<string, string> headers) => headers.TryGetValue("x-parent-pin", out var pin) && settings.VerifyPin(pin);
    private bool IsChild(Dictionary<string, string> headers) => headers.TryGetValue("x-child-token", out var token) && pairing.ValidateToken(token);

    private static string CleanApp(string value)
    {
        var x = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) x = x[..^4];
        return x.Length is > 0 and <= 80 && x.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-') ? x : string.Empty;
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

    private static async Task SendJsonAsync(NetworkStream stream, object data, CancellationToken token, int status = 200)
    {
        await SendAsync(stream, status, "application/json; charset=utf-8", JsonSerializer.Serialize(data, JsonOptions), token);
    }

    private static async Task SendAsync(NetworkStream stream, int status, string contentType, string content, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            _ => "Error"
        };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), token);
        await stream.WriteAsync(bytes, token);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private string DashboardHtml()
    {
        var code = WebUtility.HtmlEncode(pairing.Code);
        var portText = port.ToString();
        return $"<!doctype html><html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Noxo Parental</title><style>body{{font-family:Segoe UI,Arial;background:#080d18;color:#eef2ff;margin:0}}main{{max-width:900px;margin:auto;padding:28px}}.hero,.card{{background:#121a2a;border-radius:18px;padding:24px;margin-bottom:16px}}.code{{font-size:42px;letter-spacing:7px;font-weight:800}}.muted{{color:#94a3b8}}button{{background:#4f46e5;color:white;border:0;border-radius:10px;padding:12px 16px}}h1{{margin-bottom:8px}}</style></head><body><main><div class='hero'><div class='muted'>MODE PARENT</div><h1>Contrôle parental</h1><p>Code enfant</p><div class='code'>{code}</div><p class='muted'>Serveur : 0.0.0.0:{portText}</p></div><div class='card'><h2>État</h2><div id='state'>Chargement...</div></div></main><script>fetch('/api/status').then(r=>r.json()).then(s=>document.getElementById('state').textContent=s.paired?'Appareil enfant associé':'En attente d\'un appareil enfant').catch(()=>document.getElementById('state').textContent='Serveur inaccessible');</script></body></html>";
    }

    public void Dispose()
    {
        IsRunning = false;
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        cts?.Dispose();
        listener = null;
        cts = null;
    }

    private sealed class PairDto { public string? Code { get; set; } }
    private sealed class SettingsDto
    {
        public int DailyLimitMinutes { get; set; }
        public string StartTime { get; set; } = "08:00";
        public string EndTime { get; set; } = "21:00";
        public bool ObservationMode { get; set; } = true;
        public List<string>? BlockedApps { get; set; }
    }
    private sealed class PinDto { public string? Pin { get; set; } }
}
