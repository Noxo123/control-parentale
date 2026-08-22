using System.Diagnostics;
using System.Text.Json;

const string ServerUrl = "http://127.0.0.1:20570";
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

Console.WriteLine("Noxo Parental Agent - mode observation");
Console.WriteLine("L'agent détecte les processus configurés sans les fermer.");

while (true)
{
    try
    {
        var json = await http.GetStringAsync(ServerUrl + "/api/agent-config");
        var config = JsonSerializer.Deserialize<AgentConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config != null)
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (config.BlockedApps.Contains(process.ProcessName,
                        StringComparer.OrdinalIgnoreCase))
                    {
                        var message = $"Application surveillée détectée : {process.ProcessName}.exe";
                        Console.WriteLine($"[{DateTime.Now:T}] {message}");
                        await SendEvent("observation", message);
                    }
                }
                catch { }
                finally { process.Dispose(); }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:T}] Serveur indisponible : {ex.Message}");
    }

    await Task.Delay(TimeSpan.FromSeconds(10));
}

async Task SendEvent(string type, string message)
{
    try
    {
        var payload = JsonSerializer.Serialize(new { type, message });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        await http.PostAsync(ServerUrl + "/api/agent-event", content);
    }
    catch { }
}

record AgentConfig(
    int Daily_Limit_Minutes,
    string Start_Time,
    string End_Time,
    List<string> Blocked_Apps
)
{
    public int DailyLimitMinutes => Daily_Limit_Minutes;
    public string StartTime => Start_Time;
    public string EndTime => End_Time;
    public List<string> BlockedApps => Blocked_Apps ?? new List<string>();
}
