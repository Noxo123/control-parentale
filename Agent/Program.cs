using System.Diagnostics;
using System.Net.Http.Json;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NoxoParental;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) LogFatal(ex);
        };
        Application.Run(new MainForm());
    }

    private static void LogFatal(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoxoParental");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:O}] {ex}\r\n");
        }
        catch { }
    }
}

public sealed class MainForm : Form
{
    private const int DefaultPort = 20570;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly SettingsStore settings = new();
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 30000 };
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 6 * 60 * 60 * 1000 };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label status = new();
    private readonly Label server = new();
    private readonly Label activity = new();
    private readonly Label update = new();
    private readonly Label portLabel = new();
    private readonly ListBox events = new();
    private LocalServer? localServer;
    private int port = DefaultPort;
    private int busy;
    private bool closing;

    public MainForm()
    {
        Text = "Noxo Parental Control";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(940, 620);
        MinimumSize = new Size(820, 560);
        BackColor = Color.FromArgb(9, 13, 24);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;

        BuildInterface();
        Shown += async (_, _) => await StartupAsync();
        statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        updateTimer.Tick += async (_, _) => await CheckUpdateAsync(false);
        FormClosing += (_, _) => closing = true;
        FormClosed += (_, _) => Shutdown();
    }

    private void BuildInterface()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Color.FromArgb(14, 20, 34), Padding = new Padding(18) };
        var brand = new Label { Text = "NOXO\nPARENTAL", Dock = DockStyle.Top, Height = 70, Font = new Font("Segoe UI", 15, FontStyle.Bold), ForeColor = Color.White };
        sidebar.Controls.Add(brand);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 245, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        nav.Controls.Add(NavButton("⌂  Accueil", true, () => ShowHome()));
        nav.Controls.Add(NavButton("◷  Temps d'écran", false, OpenDashboard));
        nav.Controls.Add(NavButton("▣  Applications", false, OpenDashboard));
        nav.Controls.Add(NavButton("⚙  Paramètres", false, OpenDashboard));
        sidebar.Controls.Add(nav);

        var sideStatus = new Label { Dock = DockStyle.Bottom, Height = 80, ForeColor = Color.FromArgb(148, 163, 184), Text = "Agent local\nDémarrage…", Padding = new Padding(0, 8, 0, 0) };
        sidebar.Controls.Add(sideStatus);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28), BackColor = Color.FromArgb(9, 13, 24) };
        var title = new Label { Text = "Vue d'ensemble", Dock = DockStyle.Top, Height = 46, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Color.White };
        content.Controls.Add(title);

        var subtitle = new Label { Text = "Contrôle parental simple, local et léger", Dock = DockStyle.Top, Height = 34, ForeColor = Color.FromArgb(148, 163, 184) };
        content.Controls.Add(subtitle);

        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 132, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 8, 0, 8) };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        cards.Controls.Add(Card("État", status, 0), 0, 0);
        cards.Controls.Add(Card("Serveur", server, 1), 1, 0);
        cards.Controls.Add(Card("Règles", activity, 2), 2, 0);
        content.Controls.Add(cards);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, WrapContents = false, Padding = new Padding(0, 8, 0, 8) };
        actions.Controls.Add(ActionButton("Ouvrir le contrôle", OpenDashboard, true));
        actions.Controls.Add(ActionButton("Actualiser", async () => await RefreshStatusAsync(), false));
        actions.Controls.Add(ActionButton("Mise à jour", async () => await CheckUpdateAsync(true), false));
        actions.Controls.Add(ActionButton("Redémarrer le service", async () => await RestartServerAsync(), false));
        content.Controls.Add(actions);

        var updatePanel = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.FromArgb(14, 20, 34), Padding = new Padding(12, 8, 12, 8) };
        update.Text = "Mises à jour : vérification…";
        update.Dock = DockStyle.Fill;
        update.ForeColor = Color.FromArgb(148, 163, 184);
        updatePanel.Controls.Add(update);
        content.Controls.Add(updatePanel);

        events.Dock = DockStyle.Fill;
        events.BackColor = Color.FromArgb(14, 20, 34);
        events.ForeColor = Color.FromArgb(226, 232, 240);
        events.BorderStyle = BorderStyle.None;
        events.IntegralHeight = false;
        events.HorizontalScrollbar = false;
        content.Controls.Add(events);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30 };
        portLabel.Text = "Port : —";
        portLabel.Dock = DockStyle.Left;
        portLabel.ForeColor = Color.FromArgb(100, 116, 139);
        footer.Controls.Add(portLabel);
        var version = new Label { Text = $"v{UpdateChecker.CurrentVersion.ToString(3)}", Dock = DockStyle.Right, ForeColor = Color.FromArgb(100, 116, 139), TextAlign = ContentAlignment.MiddleRight };
        footer.Controls.Add(version);
        content.Controls.Add(footer);

        Controls.Add(content);
        Controls.Add(sidebar);
    }

    private static Panel Card(string caption, Label value, int index)
    {
        var p = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), BackColor = Color.FromArgb(17, 24, 39), Padding = new Padding(14) };
        var c = new Label { Text = caption, Dock = DockStyle.Top, Height = 25, ForeColor = Color.FromArgb(148, 163, 184) };
        value.Dock = DockStyle.Fill;
        value.Text = "…";
        value.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        value.ForeColor = Color.White;
        p.Controls.Add(value);
        p.Controls.Add(c);
        return p;
    }

    private static Button NavButton(string text, bool selected, Action action)
    {
        var b = new Button { Text = text, Width = 174, Height = 42, Margin = new Padding(0, 2, 0, 2), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = selected ? Color.FromArgb(79, 70, 229) : Color.Transparent, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), Cursor = Cursors.Hand };
        b.Click += (_, _) => SafeUi(action);
        return b;
    }

    private static Button ActionButton(string text, Action action, bool primary)
    {
        var b = new Button { Text = text, Width = primary ? 165 : 140, Height = 42, Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = primary ? Color.FromArgb(79, 70, 229) : Color.FromArgb(30, 41, 59), ForeColor = Color.White, Cursor = Cursors.Hand };
        b.Click += (_, _) => SafeUi(action);
        return b;
    }

    private static void SafeUi(Action action)
    {
        try { action(); } catch { }
    }

    private async Task StartupAsync()
    {
        try
        {
            status.Text = "Démarrage…";
            AddEvent("system", "Initialisation de l'agent…");
            await Task.Run(StartLocalServer, lifetime.Token);
            await RefreshStatusAsync();
            await CheckUpdateAsync(false);
            statusTimer.Start();
            updateTimer.Start();
            AddEvent("system", "Agent prêt.");
        }
        catch (Exception ex)
        {
            status.Text = "Agent limité";
            status.ForeColor = Color.FromArgb(251, 146, 60);
            AddEvent("error", "Démarrage : " + ex.Message);
        }
    }

    private void StartLocalServer()
    {
        if (localServer?.IsRunning == true) return;
        for (var candidate = DefaultPort; candidate <= DefaultPort + 20; candidate++)
        {
            if (closing) return;
            var candidateServer = new LocalServer(candidate, settings, AddEvent);
            candidateServer.Start();
            if (candidateServer.IsRunning)
            {
                localServer = candidateServer;
                port = candidate;
                BeginUi(() => portLabel.Text = $"Port local : {port}");
                return;
            }
            candidateServer.Dispose();
        }
        throw new InvalidOperationException("Aucun port local disponible.");
    }

    private async Task RefreshStatusAsync()
    {
        if (closing || Interlocked.Exchange(ref busy, 1) != 0) return;
        try
        {
            await Task.Run(StartLocalServer, lifetime.Token);
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/agent-config", lifetime.Token);
            response.EnsureSuccessStatusCode();
            var config = await response.Content.ReadFromJsonAsync<AgentConfig>(cancellationToken: lifetime.Token);
            BeginUi(() =>
            {
                status.Text = "● Agent actif";
                status.ForeColor = Color.FromArgb(74, 222, 128);
                server.Text = $"127.0.0.1:{port}";
                server.ForeColor = Color.FromArgb(74, 222, 128);
                if (config != null) activity.Text = $"{config.BlockedApps.Count} app. • {config.DailyLimitMinutes} min/jour";
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            BeginUi(() =>
            {
                status.Text = "● En attente";
                status.ForeColor = Color.FromArgb(251, 146, 60);
                server.Text = "Reconnexion automatique";
                activity.Text = "Serveur local en cours de récupération";
            });
            AddEvent("server", ex.Message);
        }
        finally { Interlocked.Exchange(ref busy, 0); }
    }

    private async Task RestartServerAsync()
    {
        if (Interlocked.Exchange(ref busy, 1) != 0) return;
        try
        {
            AddEvent("server", "Redémarrage du serveur local…");
            await Task.Run(() =>
            {
                localServer?.Dispose();
                localServer = null;
                Thread.Sleep(100);
                StartLocalServer();
            }, lifetime.Token);
            await RefreshStatusCoreAsync();
        }
        catch (Exception ex) { AddEvent("error", "Redémarrage : " + ex.Message); }
        finally { Interlocked.Exchange(ref busy, 0); }
    }

    private async Task RefreshStatusCoreAsync()
    {
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/agent-config", lifetime.Token);
            response.EnsureSuccessStatusCode();
            BeginUi(() => { status.Text = "● Agent actif"; status.ForeColor = Color.FromArgb(74, 222, 128); server.Text = $"127.0.0.1:{port}"; });
        }
        catch (Exception ex) { AddEvent("server", ex.Message); }
    }

    private void OpenDashboard()
    {
        try
        {
            StartLocalServer();
            Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}/") { UseShellExecute = true });
        }
        catch (Exception ex) { AddEvent("error", "Dashboard : " + ex.Message); }
    }

    private async Task CheckUpdateAsync(bool interactive)
    {
        try
        {
            update.Text = "Mises à jour : vérification…";
            var release = await UpdateChecker.CheckAsync(http, lifetime.Token);
            if (release is null)
            {
                update.Text = "Mises à jour : vérification impossible (hors ligne ou aucune release)";
                update.ForeColor = Color.FromArgb(148, 163, 184);
                return;
            }
            if (UpdateChecker.IsNewer(release.tag_name, out _))
            {
                update.Text = $"● Mise à jour disponible : {release.tag_name}";
                update.ForeColor = Color.FromArgb(251, 146, 60);
                if (interactive) UpdateChecker.OpenRelease(release.html_url);
            }
            else
            {
                update.Text = $"● Application à jour • v{UpdateChecker.CurrentVersion.ToString(3)}";
                update.ForeColor = Color.FromArgb(74, 222, 128);
            }
        }
        catch (Exception ex) { AddEvent("update", ex.Message); }
    }

    private void AddEvent(string type, string message)
    {
        if (closing || IsDisposed || Disposing) return;
        var text = $"{DateTime.Now:HH:mm:ss}  {type.ToUpperInvariant()}  {message}";
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => AddEvent(type, message)));
                return;
            }
            events.Items.Insert(0, text);
            while (events.Items.Count > 80) events.Items.RemoveAt(events.Items.Count - 1);
        }
        catch { }
    }

    private void BeginUi(Action action)
    {
        if (closing || IsDisposed || Disposing) return;
        try
        {
            if (InvokeRequired) BeginInvoke((MethodInvoker)(() => BeginUi(action)));
            else action();
        }
        catch { }
    }

    private void ShowHome() { }

    private void Shutdown()
    {
        closing = true;
        try { statusTimer.Stop(); updateTimer.Stop(); } catch { }
        try { lifetime.Cancel(); } catch { }
        try { localServer?.Dispose(); } catch { }
        try { http.Dispose(); } catch { }
        lifetime.Dispose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Shutdown();
        base.OnFormClosed(e);
    }
}

public sealed class AgentConfig
{
    public int DailyLimitMinutes { get; set; }
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "21:00";
    public List<string> BlockedApps { get; set; } = [];
}
