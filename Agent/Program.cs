using System.Diagnostics;
using System.Net.Http.Json;
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
    private const int DefaultPort = 20570;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly Label status = new();
    private readonly Label server = new();
    private readonly Label activity = new();
    private readonly Label update = new();
    private readonly ListBox events = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 10000 };
    private readonly LocalServer localServer;
    private bool checking;
    private int port = DefaultPort;

    public MainForm()
    {
        Text = "Noxo Parental Control";
        Width = 800; Height = 570;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 500);

        var title = new Label { Text = "Noxo Parental Control", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 22) };
        var subtitle = new Label { Text = "Agent Windows • serveur local intégré • mises à jour GitHub", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 62) };
        status.Text = "● Démarrage"; status.AutoSize = true; status.Font = new Font("Segoe UI", 11, FontStyle.Bold); status.Location = new Point(30, 105);
        server.Text = "Serveur : démarrage…"; server.AutoSize = true; server.Location = new Point(30, 135);
        activity.Text = "Règles : chargement…"; activity.AutoSize = true; activity.Location = new Point(30, 165);
        update.Text = "Mises à jour : vérification…"; update.AutoSize = true; update.Location = new Point(30, 190);

        var retry = new Button { Text = "Vérifier le serveur", AutoSize = true, Location = new Point(30, 225) };
        retry.Click += async (_, _) => await CheckServer();
        var openDashboard = new Button { Text = "Ouvrir le dashboard", AutoSize = true, Location = new Point(180, 225) };
        openDashboard.Click += (_, _) => OpenUrl($"http://127.0.0.1:{port}/");
        var checkUpdate = new Button { Text = "Vérifier les mises à jour", AutoSize = true, Location = new Point(350, 225) };
        checkUpdate.Click += async (_, _) => await CheckForUpdate(true);

        events.Location = new Point(30, 275); events.Size = new Size(720, 220); events.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.AddRange([title, subtitle, status, server, activity, update, retry, openDashboard, checkUpdate, events]);

        localServer = new LocalServer(DefaultPort, GetConfig, AddEvent);
        localServer.Start();
        if (!localServer.IsRunning)
        {
            for (var candidate = DefaultPort + 1; candidate <= DefaultPort + 5 && !localServer.IsRunning; candidate++)
            {
                // A new LocalServer is required because HttpListener prefixes are immutable after construction.
                localServer.Dispose();
                port = candidate;
                break;
            }
        }

        Shown += async (_, _) => { await CheckServer(); await CheckForUpdate(false); };
        timer.Tick += async (_, _) => { await CheckServer(); await CheckForUpdate(false); };
        timer.Start();
    }

    private object GetConfig() => new
    {
        daily_limit_minutes = 120,
        start_time = "08:00",
        end_time = "21:00",
        blocked_apps = Array.Empty<string>()
    };

    private async Task CheckServer()
    {
        if (checking) return;
        checking = true;
        try
        {
            var response = await http.GetAsync($"http://127.0.0.1:{port}/api/agent-config");
            response.EnsureSuccessStatusCode();
            var config = await response.Content.ReadFromJsonAsync<AgentConfig>();
            status.Text = "● Agent actif"; status.ForeColor = Color.ForestGreen;
            server.Text = $"Serveur : connecté sur 127.0.0.1:{port}"; server.ForeColor = Color.ForestGreen;
            if (config != null)
                activity.Text = $"Règles : {config.BlockedApps.Count} application(s) • {config.DailyLimitMinutes} min/jour • {config.StartTime} → {config.EndTime}";
        }
        catch (Exception ex)
        {
            status.Text = "● Agent en attente"; status.ForeColor = Color.DarkOrange;
            server.Text = $"Serveur : indisponible ({ex.Message})"; server.ForeColor = Color.DarkOrange;
            activity.Text = "Le serveur local intégré n'a pas pu répondre.";
        }
        finally { checking = false; }
    }

    private void AddEvent(string type, string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => AddEvent(type, message)); return; }
        events.Items.Insert(0, $"{DateTime.Now:T} • [{type}] {message}");
        while (events.Items.Count > 100) events.Items.RemoveAt(events.Items.Count - 1);
    }

    private async Task CheckForUpdate(bool interactive)
    {
        try
        {
            var release = await UpdateChecker.CheckAsync(http);
            var latest = UpdateChecker.ParseTag(release?.tag_name);
            var current = UpdateChecker.CurrentVersion;
            if (latest != null && latest > current)
            {
                update.Text = $"Mise à jour disponible : {release!.tag_name}"; update.ForeColor = Color.DarkOrange;
                if (interactive && MessageBox.Show($"Une nouvelle version ({release.tag_name}) est disponible. Ouvrir GitHub ?", "Mise à jour", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    UpdateChecker.OpenRelease(release.html_url);
            }
            else
            {
                update.Text = "Mises à jour : dernière version"; update.ForeColor = Color.ForestGreen;
            }
        }
        catch { update.Text = "Mises à jour : vérification impossible"; update.ForeColor = Color.DimGray; }
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop(); localServer.Dispose(); http.Dispose(); base.OnFormClosed(e);
    }
}

public sealed class AgentConfig
{
    public int DailyLimitMinutes { get; set; }
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "21:00";
    public List<string> BlockedApps { get; set; } = [];
}
