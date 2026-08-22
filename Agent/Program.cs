using System.Diagnostics;
using System.Net.Http.Json;
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
        Application.ThreadException += (_, e) => CrashLog.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) CrashLog.Write(ex); };
        Application.Run(new MainForm());
    }
}

internal static class CrashLog
{
    public static void Write(Exception ex)
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
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 21600000 };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label status = LabelOf("Démarrage…");
    private readonly Label server = LabelOf("Serveur…");
    private readonly Label rules = LabelOf("Règles…");
    private readonly Label update = LabelOf("Mise à jour…");
    private readonly Label portLabel = LabelOf("Port : —");
    private readonly ListBox log = new();
    private LocalServer? localServer;
    private int port = DefaultPort;
    private int operation;
    private bool closing;

    public MainForm()
    {
        Text = "Noxo Parental Control";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(760, 520);
        BackColor = Color.FromArgb(9, 13, 24);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        BuildUi();
        Shown += async (_, _) => await StartupAsync();
        statusTimer.Tick += async (_, _) => await RefreshAsync();
        updateTimer.Tick += async (_, _) => await UpdateAsync(false);
        FormClosed += (_, _) => Shutdown();
    }

    private void BuildUi()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 205, BackColor = Color.FromArgb(14, 20, 34), Padding = new Padding(16) };
        sidebar.Controls.Add(new Label { Text = "NOXO\nPARENTAL", Dock = DockStyle.Top, Height = 68, Font = new Font("Segoe UI", 15, FontStyle.Bold) });
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 220, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        nav.Controls.Add(Nav("⌂  Accueil", OpenHome));
        nav.Controls.Add(Nav("◷  Temps d'écran", OpenDashboard));
        nav.Controls.Add(Nav("▣  Applications", OpenDashboard));
        nav.Controls.Add(Nav("⚙  Paramètres", OpenDashboard));
        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(new Label { Text = "Agent local\nInterface légère", Dock = DockStyle.Bottom, Height = 65, ForeColor = Color.FromArgb(148, 163, 184) });

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(26), BackColor = Color.FromArgb(9, 13, 24) };
        main.Controls.Add(new Label { Text = "Vue d'ensemble", Dock = DockStyle.Top, Height = 42, Font = new Font("Segoe UI", 21, FontStyle.Bold) });
        main.Controls.Add(new Label { Text = "Contrôle parental Windows • local • léger", Dock = DockStyle.Top, Height = 32, ForeColor = Color.FromArgb(148, 163, 184) });

        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 105, ColumnCount = 3, Padding = new Padding(0, 8, 0, 8) };
        for (var i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.Controls.Add(Card("État", status), 0, 0); cards.Controls.Add(Card("Serveur", server), 1, 0); cards.Controls.Add(Card("Règles", rules), 2, 0);
        main.Controls.Add(cards);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, WrapContents = false, Padding = new Padding(0, 5, 0, 5) };
        actions.Controls.Add(Action("Ouvrir le contrôle", OpenDashboardAsync, true));
        actions.Controls.Add(Action("Actualiser", RefreshAsync, false));
        actions.Controls.Add(Action("Mise à jour", () => UpdateAsync(true), false));
        actions.Controls.Add(Action("Redémarrer", RestartAsync, false));
        main.Controls.Add(actions);

        update.Dock = DockStyle.Top; update.Height = 34; update.BackColor = Color.FromArgb(14, 20, 34); update.Padding = new Padding(10, 7, 0, 0); main.Controls.Add(update);
        log.Dock = DockStyle.Fill; log.BackColor = Color.FromArgb(14, 20, 34); log.ForeColor = Color.FromArgb(226, 232, 240); log.BorderStyle = BorderStyle.None; log.IntegralHeight = false; main.Controls.Add(log);
        portLabel.Dock = DockStyle.Bottom; portLabel.Height = 24; portLabel.ForeColor = Color.FromArgb(100, 116, 139); main.Controls.Add(portLabel);
        Controls.Add(main); Controls.Add(sidebar);
    }

    private static Label LabelOf(string text) => new() { Text = text, AutoSize = false, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.White };

    private static Panel Card(string title, Label value)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(17, 24, 39), Margin = new Padding(0, 0, 8, 0), Padding = new Padding(12) };
        p.Controls.Add(value); value.Dock = DockStyle.Fill; value.Text = "…";
        var t = new Label { Text = title, Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(148, 163, 184) }; p.Controls.Add(t); return p;
    }

    private static Button Nav(string text, Func<Task> action) => MakeButton(text, action, 174, 40, false);
    private static Button Action(string text, Func<Task> action, bool primary) => MakeButton(text, action, primary ? 160 : 125, 40, primary);

    private static Button MakeButton(string text, Func<Task> action, int width, int height, bool primary)
    {
        var b = new Button { Text = text, Width = width, Height = height, Margin = new Padding(0, 2, 7, 2), FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(79, 70, 229) : Color.FromArgb(30, 41, 59), ForeColor = Color.White, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0;
        b.Click += async (_, _) => await RunSafeAsync(action);
        return b;
    }

    private static async Task RunSafeAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); } catch { }
    }

    private async Task StartupAsync()
    {
        try
        {
            Add("system", "Démarrage…");
            await Task.Run(StartLocalServer, lifetime.Token);
            await RefreshAsync();
            _ = UpdateAsync(false);
            statusTimer.Start(); updateTimer.Start();
            Add("system", "Agent prêt.");
        }
        catch (Exception ex) { Add("error", ex.Message); SetState("● Agent limité", "Serveur indisponible", "Réessai automatique", Color.FromArgb(251, 146, 60)); }
    }

    private void StartLocalServer()
    {
        if (localServer?.IsRunning == true) return;
        for (var candidate = DefaultPort; candidate <= DefaultPort + 20; candidate++)
        {
            if (closing) return;
            var s = new LocalServer(candidate, settings, Add);
            s.Start();
            if (s.IsRunning) { localServer = s; port = candidate; Ui(() => portLabel.Text = $"Port local : {port}"); return; }
            s.Dispose();
        }
        throw new InvalidOperationException("Aucun port local disponible.");
    }

    private async Task RefreshAsync()
    {
        if (closing || Interlocked.Exchange(ref operation, 1) != 0) return;
        try
        {
            await Task.Run(StartLocalServer, lifetime.Token);
            using var r = await http.GetAsync($"http://127.0.0.1:{port}/api/agent-config", lifetime.Token);
            r.EnsureSuccessStatusCode();
            var cfg = await r.Content.ReadFromJsonAsync<AgentConfig>(cancellationToken: lifetime.Token);
            SetState("● Agent actif", $"127.0.0.1:{port}", cfg is null ? "Règles disponibles" : $"{cfg.BlockedApps.Count} app. • {cfg.DailyLimitMinutes} min/jour", Color.FromArgb(74, 222, 128));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Add("server", ex.Message); SetState("● En attente", "Reconnexion automatique", "Serveur local", Color.FromArgb(251, 146, 60)); }
        finally { Interlocked.Exchange(ref operation, 0); }
    }

    private async Task RestartAsync()
    {
        if (Interlocked.Exchange(ref operation, 1) != 0) return;
        try
        {
            Add("server", "Redémarrage…");
            await Task.Run(() => { localServer?.Dispose(); localServer = null; Thread.Sleep(100); StartLocalServer(); }, lifetime.Token);
            SetState("● Agent actif", $"127.0.0.1:{port}", "Serveur relancé", Color.FromArgb(74, 222, 128));
        }
        catch (Exception ex) { Add("error", ex.Message); }
        finally { Interlocked.Exchange(ref operation, 0); }
    }

    private Task OpenDashboard() => OpenDashboardAsync();

    private async Task OpenDashboardAsync()
    {
        try
        {
            await Task.Run(StartLocalServer, lifetime.Token);
            Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}/") { UseShellExecute = true });
        }
        catch (Exception ex) { Add("error", "Dashboard : " + ex.Message); }
    }

    private Task OpenHome() { return Task.CompletedTask; }

    private async Task UpdateAsync(bool interactive)
    {
        try
        {
            Ui(() => update.Text = "Mises à jour : vérification…");
            var release = await UpdateChecker.CheckAsync(http, lifetime.Token);
            if (release is null) { Ui(() => update.Text = "Mises à jour : aucune information disponible"); return; }
            if (UpdateChecker.IsNewer(release.tag_name, out _))
            {
                Ui(() => update.Text = $"● Mise à jour disponible : {release.tag_name}");
                if (interactive) UpdateChecker.OpenRelease(release.html_url);
            }
            else Ui(() => update.Text = $"● À jour • v{UpdateChecker.CurrentVersion.ToString(3)}");
        }
        catch (Exception ex) { Add("update", ex.Message); }
    }

    private void SetState(string a, string b, string c, Color color) => Ui(() => { status.Text = a; status.ForeColor = color; server.Text = b; rules.Text = c; });

    private void Add(string type, string message)
    {
        if (closing || IsDisposed || Disposing) return;
        try
        {
            Ui(() => { log.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {type.ToUpperInvariant()}  {message}"); while (log.Items.Count > 60) log.Items.RemoveAt(log.Items.Count - 1); });
        }
        catch { }
    }

    private void Ui(Action action)
    {
        if (closing || IsDisposed || Disposing) return;
        try { if (InvokeRequired) BeginInvoke((MethodInvoker)(() => Ui(action))); else action(); } catch { }
    }

    private void Shutdown()
    {
        if (closing) return;
        closing = true;
        try { statusTimer.Stop(); updateTimer.Stop(); lifetime.Cancel(); localServer?.Dispose(); http.Dispose(); lifetime.Dispose(); } catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { closing = true; base.OnFormClosing(e); }
    protected override void OnFormClosed(FormClosedEventArgs e) { Shutdown(); base.OnFormClosed(e); }
}

public sealed class AgentConfig
{
    public int DailyLimitMinutes { get; set; }
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "21:00";
    public List<string> BlockedApps { get; set; } = [];
}
