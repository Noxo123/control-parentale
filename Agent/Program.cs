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
    private const string ServerUrl = "http://127.0.0.1:20570";
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly Label status = new();
    private readonly Label server = new();
    private readonly Label activity = new();
    private readonly Label update = new();
    private readonly Button retry = new();
    private readonly Button openDashboard = new();
    private readonly Button checkUpdate = new();
    private readonly ListBox events = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 10000 };
    private bool checking;

    public MainForm()
    {
        Text = "Noxo Parental Control";
        Width = 800; Height = 570;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 500);

        var title = new Label { Text = "Noxo Parental Control", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 22) };
        var subtitle = new Label { Text = "Agent Windows • interface graphique • mises à jour GitHub", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 62) };
        status.Text = "● Démarrage"; status.AutoSize = true; status.Font = new Font("Segoe UI", 11, FontStyle.Bold); status.Location = new Point(30, 105);
        server.Text = "Serveur : vérification…"; server.AutoSize = true; server.Location = new Point(30, 135);
        activity.Text = "Règles : aucune"; activity.AutoSize = true; activity.Location = new Point(30, 165);
        update.Text = "Mises à jour : vérification…"; update.AutoSize = true; update.Location = new Point(30, 190);

        retry.Text = "Vérifier le serveur"; retry.AutoSize = true; retry.Location = new Point(30, 225); retry.Click += async (_, _) => await CheckServer();
        openDashboard.Text = "Ouvrir le dashboard"; openDashboard.AutoSize = true; openDashboard.Location = new Point(180, 225); openDashboard.Click += (_, _) => OpenUrl(ServerUrl);
        checkUpdate.Text = "Vérifier les mises à jour"; checkUpdate.AutoSize = true; checkUpdate.Location = new Point(350, 225); checkUpdate.Click += async (_, _) => await CheckForUpdate(true);

        events.Location = new Point(30, 275); events.Size = new Size(720, 220); events.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.AddRange([title, subtitle, status, server, activity, update, retry, openDashboard, checkUpdate, events]);
        Shown += async (_, _) => { await CheckServer(); await CheckForUpdate(false); };
        timer.Tick += async (_, _) => { await CheckServer(); await CheckForUpdate(false); };
        timer.Start();
    }

    private async Task CheckServer()
    {
        if (checking) return;
        checking = true;
        try
        {
            var response = await http.GetAsync(ServerUrl + "/api/agent-config");
            response.EnsureSuccessStatusCode();
            var config = await response.Content.ReadFromJsonAsync<AgentConfig>();
            status.Text = "● Agent actif"; status.ForeColor = Color.ForestGreen;
            server.Text = "Serveur : connecté sur 127.0.0.1:20570"; server.ForeColor = Color.ForestGreen;
            if (config != null)
            {
                activity.Text = $"Règles : {config.BlockedApps.Count} application(s) • {config.DailyLimitMinutes} min/jour • {config.StartTime} → {config.EndTime}";
                await Detect(config);
            }
        }
        catch (HttpRequestException)
        {
            status.Text = "● Agent en attente"; status.ForeColor = Color.DarkOrange;
            server.Text = "Serveur : non démarré sur 127.0.0.1:20570"; server.ForeColor = Color.DarkOrange;
            activity.Text = "Le serveur local doit être lancé pour récupérer les règles.";
        }
        catch (Exception ex)
        {
            status.Text = "● Erreur"; status.ForeColor = Color.Firebrick;
            server.Text = "Erreur : " + ex.Message;
        }
        finally { checking = false; }
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
                    events.Items.Insert(0, message);
                    while (events.Items.Count > 100) events.Items.RemoveAt(events.Items.Count - 1);
                    await SendEvent("observation", $"Application surveillée détectée : {process.ProcessName}.exe");
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
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
                update.Text = "Mises à jour : vous utilisez la dernière version publiée"; update.ForeColor = Color.ForestGreen;
            }
        }
        catch
        {
            update.Text = "Mises à jour : GitHub indisponible (nouvel essai automatique plus tard)"; update.ForeColor = Color.DimGray;
        }
    }

    private async Task SendEvent(string type, string message)
    {
        try { await http.PostAsJsonAsync(ServerUrl + "/api/agent-event", new { type, message }); } catch { }
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

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
