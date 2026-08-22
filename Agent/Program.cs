using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Forms;

namespace NoxoParental;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private const string ServerUrl = "http://127.0.0.1:20570";
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly Label status = new();
    private readonly Label server = new();
    private readonly Label activity = new();
    private readonly Button retry = new();
    private readonly Button openDashboard = new();
    private readonly ListBox events = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 10000 };

    public MainForm()
    {
        Text = "Noxo Parental Control";
        Width = 760;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 450);

        var title = new Label { Text = "Noxo Parental Control", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 22) };
        var subtitle = new Label { Text = "Agent Windows • surveillance transparente", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 62) };

        status.Text = "● Démarrage"; status.AutoSize = true; status.Font = new Font("Segoe UI", 11, FontStyle.Bold); status.Location = new Point(30, 105);
        server.Text = "Serveur : vérification…"; server.AutoSize = true; server.Location = new Point(30, 135);
        activity.Text = "Activité : aucune"; activity.AutoSize = true; activity.Location = new Point(30, 165);

        retry.Text = "Vérifier maintenant"; retry.AutoSize = true; retry.Location = new Point(30, 200); retry.Click += async (_, _) => await CheckServer();
        openDashboard.Text = "Ouvrir le dashboard"; openDashboard.AutoSize = true; openDashboard.Location = new Point(180, 200); openDashboard.Click += (_, _) => Process.Start(new ProcessStartInfo { FileName = ServerUrl, UseShellExecute = true });

        events.Location = new Point(30, 250); events.Size = new Size(680, 185); events.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange([title, subtitle, status, server, activity, retry, openDashboard, events]);
        Shown += async (_, _) => await CheckServer();
        timer.Tick += async (_, _) => await CheckServer();
        timer.Start();
    }

    private async Task CheckServer()
    {
        try
        {
            var response = await http.GetAsync(ServerUrl + "/api/agent-config");
            response.EnsureSuccessStatusCode();
            var config = await response.Content.ReadFromJsonAsync<AgentConfig>();
            status.Text = "● Agent actif";
            status.ForeColor = Color.ForestGreen;
            server.Text = "Serveur : connecté sur 127.0.0.1:20570";
            server.ForeColor = Color.ForestGreen;
            if (config != null)
            {
                activity.Text = $"Règles : {config.BlockedApps.Count} application(s) surveillée(s) • {config.DailyLimitMinutes} min/jour • {config.StartTime} → {config.EndTime}";
                await Detect(config);
            }
        }
        catch (HttpRequestException)
        {
            status.Text = "● Agent en attente";
            status.ForeColor = Color.DarkOrange;
            server.Text = "Serveur : non démarré sur 127.0.0.1:20570";
            server.ForeColor = Color.DarkOrange;
            activity.Text = "Lance le dashboard/serveur avec npm start, puis clique sur Vérifier.";
        }
        catch (Exception ex)
        {
            status.Text = "● Erreur";
            status.ForeColor = Color.Firebrick;
            server.Text = "Erreur : " + ex.Message;
        }
    }

    private async Task Detect(AgentConfig config)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (config.BlockedApps.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    var message = $"{DateTime.Now:T} • Application surveillée : {process.ProcessName}.exe";
                    if (!events.Items.Contains(message)) events.Items.Insert(0, message);
                    if (events.Items.Count > 100) events.Items.RemoveAt(events.Items.Count - 1);
                    await SendEvent("observation", $"Application surveillée détectée : {process.ProcessName}.exe");
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    private async Task SendEvent(string type, string message)
    {
        try { await http.PostAsJsonAsync(ServerUrl + "/api/agent-event", new { type, message }); } catch { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop(); http.Dispose(); base.OnFormClosed(e);
    }
}

public sealed class AgentConfig
{
    public int DailyLimitMinutes { get; set; }
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "21:00";
    public List<string> BlockedApps { get; set; } = [];
}
