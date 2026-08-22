using System.Diagnostics;
using System.Net.Http.Json;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NoxoParental;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private const int DefaultPort = 20570;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly SettingsStore settings = new();
    private readonly Label status = new();
    private readonly Label server = new();
    private readonly Label activity = new();
    private readonly Label update = new();
    private readonly Label version = new();
    private readonly Label portLabel = new();
    private readonly ListBox events = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15000 };
    private LocalServer? localServer;
    private bool checking;
    private bool checkingUpdate;
    private int port = DefaultPort;

    public MainForm()
    {
        Text = "Noxo Parental Control";
        Width = 900; Height = 650;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        BackColor = Color.FromArgb(10, 15, 27);
        ForeColor = Color.White;

        var title = new Label { Text = "Noxo Parental Control", Font = new Font("Segoe UI", 23, FontStyle.Bold), AutoSize = true, Location = new Point(30, 24), ForeColor = Color.White };
        var subtitle = new Label { Text = "Contrôle parental Windows • sécurisé • local", ForeColor = Color.FromArgb(148, 163, 184), AutoSize = true, Location = new Point(33, 65) };

        ConfigureStatus(status, "● Démarrage", 30, 108);
        ConfigureStatus(server, "Serveur : démarrage…", 30, 140);
        ConfigureStatus(activity, "Règles : chargement…", 30, 172);
        ConfigureStatus(update, "Mises à jour : vérification…", 30, 204);

        var dashboard = Button("Ouvrir le dashboard", 30, 245, 170, 42);
        dashboard.Click += (_, _) => OpenDashboard();
        var refresh = Button("Actualiser", 212, 245, 110, 42);
        refresh.Click += async (_, _) => await SafeRefresh();
        var updates = Button("Mises à jour", 334, 245, 125, 42);
        updates.Click += async (_, _) => await CheckForUpdate(true);
        var restart = Button("Relancer le serveur", 471, 245, 155, 42);
        restart.Click += async (_, _) => await RestartServer();

        var security = new Label { Text = "🔒 Dashboard local • modifications protégées par PIN parent", AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(30, 300) };

        events.Location = new Point(30, 335); events.Size = new Size(820, 235); events.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        events.BackColor = Color.FromArgb(17, 24, 39); events.ForeColor = Color.FromArgb(226, 232, 240); events.BorderStyle = BorderStyle.FixedSingle;

        version.Text = "Version : " + UpdateChecker.CurrentVersion.ToString(3); version.AutoSize = true; version.ForeColor = Color.FromArgb(148, 163, 184); version.Location = new Point(30, 585); version.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        portLabel.Text = "Port : —"; portLabel.AutoSize = true; portLabel.ForeColor = Color.FromArgb(148, 163, 184); portLabel.Location = new Point(160, 585); portLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

        Controls.AddRange([title, subtitle, status, server, activity, update, dashboard, refresh, updates, restart, security, events, version, portLabel]);
        Shown += async (_, _) => await SafeRefresh();
        timer.Tick += async (_, _) => await SafeRefresh();
        timer.Start();
    }

    private static void ConfigureStatus(Label label, string text, int x, int y)
    {
        label.Text = text; label.AutoSize = true; label.Location = new Point(x, y); label.Font = new Font("Segoe UI", 10, FontStyle.Bold);
    }

    private static Button Button(string text, int x, int y, int width, int height) => new()
    {
        Text = text, Location = new Point(x, y), Size = new Size(width, height), FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(79, 70, 229), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand
    };

    private async Task SafeRefresh()
    {
        try
        {
            StartLocalServer(false);
            await CheckServer();
            await CheckForUpdate(false);
        }
        catch (Exception ex)
        {
            AddEvent("error", ex.Message);
        }
    }

    private async Task RestartServer()
    {
        try
        {
            localServer?.Dispose();
            localServer = null;
            await Task.Delay(150);
            StartLocalServer(true);
            await CheckServer();
        }
        catch (Exception ex) { AddEvent("error", "Redémarrage : " + ex.Message); }
    }

    private void StartLocalServer(bool forceRestart)
    {
        if (forceRestart) { localServer?.Dispose(); localServer = null; }
        if (localServer?.IsRunning == true) return;

        for (var candidate = DefaultPort; candidate <= DefaultPort + 20; candidate++)
        {
            var candidateServer = new LocalServer(candidate, settings, AddEvent);
            candidateServer.Start();
            if (candidateServer.IsRunning)
            {
                localServer = candidateServer;
                port = candidate;
                portLabel.Text = $"Port : {port}";
                return;
            }
            candidateServer.Dispose();
        }
        throw new InvalidOperationException("Impossible d'ouvrir un port local disponible.");
    }

    private async Task CheckServer()
    {
        if (checking || IsDisposed) return;
        checking = true;
        try
        {
            if (localServer?.IsRunning != true) StartLocalServer(false);
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/agent-config");
            response.EnsureSuccessStatusCode();
            var config = await response.Content.ReadFromJsonAsync<AgentConfig>();
            status.Text = "● Agent actif"; status.ForeColor = Color.FromArgb(74, 222, 128);
            server.Text = $"Serveur : connecté sur 127.0.0.1:{port}"; server.ForeColor = Color.FromArgb(74, 222, 128);
            if (config != null) activity.Text = $"Règles : {config.BlockedApps.Count} application(s) • {config.DailyLimitMinutes} min/jour • {config.StartTime} → {config.EndTime}";
        }
        catch (Exception ex)
        {
            status.Text = "● Agent en attente"; status.ForeColor = Color.FromArgb(251, 146, 60);
            server.Text = "Serveur : reconnexion automatique…"; server.ForeColor = Color.FromArgb(251, 146, 60);
            activity.Text = "Le serveur intégré sera relancé automatiquement.";
            AddEvent("server", ex.Message);
        }
        finally { checking = false; }
    }

    private void OpenDashboard()
    {
        try
        {
            StartLocalServer(false);
            Process.Start(new ProcessStartInfo { FileName = $"http://127.0.0.1:{port}/", UseShellExecute = true });
        }
        catch (Exception ex) { AddEvent("error", ex.Message); MessageBox.Show(this, "Impossible d'ouvrir le dashboard.\n" + ex.Message, "Noxo Parental Control", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
        if (checkingUpdate) return;
        checkingUpdate = true;
        try
        {
            var release = await UpdateChecker.CheckAsync(http);
            if (release is null) { update.Text = "Mises à jour : GitHub indisponible"; update.ForeColor = Color.Gray; return; }
            if (UpdateChecker.IsNewer(release.tag_name, out _))
            {
                update.Text = $"🟠 Nouvelle version : {release.tag_name}"; update.ForeColor = Color.FromArgb(251, 146, 60);
                if (interactive && MessageBox.Show(this, $"La version {release.tag_name} est disponible. Ouvrir GitHub ?", "Mise à jour", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) UpdateChecker.OpenRelease(release.html_url);
            }
            else { update.Text = $"Mises à jour : {UpdateChecker.CurrentVersion.ToString(3)} à jour"; update.ForeColor = Color.FromArgb(74, 222, 128); }
        }
        catch (Exception ex) { update.Text = "Mises à jour : erreur de vérification"; AddEvent("update", ex.Message); }
        finally { checkingUpdate = false; }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop(); localServer?.Dispose(); http.Dispose(); base.OnFormClosed(e);
    }
}

public sealed class AgentConfig
{
    public int DailyLimitMinutes { get; set; }
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "21:00";
    public List<string> BlockedApps { get; set; } = [];
}
