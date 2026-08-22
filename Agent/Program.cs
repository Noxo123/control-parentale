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

        using var role = new RoleForm();
        if (role.ShowDialog() != DialogResult.OK) return;
        Application.Run(role.IsParent ? new ParentForm() : new ChildForm());
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

public sealed class RoleForm : Form
{
    public bool IsParent { get; private set; }

    public RoleForm()
    {
        Text = "Noxo Parental Control";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 360);
        BackColor = Color.FromArgb(9, 13, 24);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        Controls.Add(new Label { Text = "Noxo Parental", Font = new Font("Segoe UI", 26, FontStyle.Bold), Dock = DockStyle.Top, Height = 70, TextAlign = ContentAlignment.MiddleCenter });
        Controls.Add(new Label { Text = "Choisis le mode de cet ordinateur", Dock = DockStyle.Top, Height = 42, TextAlign = ContentAlignment.TopCenter, ForeColor = Color.FromArgb(148, 163, 184) });

        var parent = BigButton("PARENT\nGérer les règles", Color.FromArgb(79, 70, 229));
        var child = BigButton("ENFANT\nSe connecter avec un code", Color.FromArgb(30, 41, 59));
        parent.Location = new Point(35, 130);
        child.Location = new Point(290, 130);
        parent.Click += (_, _) => SelectRole(true);
        child.Click += (_, _) => SelectRole(false);
        Controls.Add(child);
        Controls.Add(parent);
    }

    private void SelectRole(bool parent)
    {
        IsParent = parent;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Button BigButton(string text, Color color) => new()
    {
        Text = text,
        Width = 235,
        Height = 120,
        FlatStyle = FlatStyle.Flat,
        BackColor = color,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 12, FontStyle.Bold),
        Cursor = Cursors.Hand
    };
}

public sealed class ParentForm : Form
{
    private readonly SettingsStore settings = new();
    private readonly PairingStore pairing = new();
    private readonly Label state = LabelOf("Démarrage…");
    private readonly Label code = LabelOf("------");
    private readonly Label address = LabelOf("Adresse : —");
    private readonly Label paired = LabelOf("Aucun appareil associé");
    private readonly Label rules = LabelOf("—");
    private LocalServer? server;
    private int port = 20570;
    private bool closing;

    public ParentForm()
    {
        Text = "Noxo Parental — Parent";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(920, 600);
        MinimumSize = new Size(820, 540);
        BackColor = Color.FromArgb(9, 13, 24);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;
        BuildUi();
        Shown += (_, _) => StartAsync();
        FormClosed += (_, _) => Stop();
    }

    private void BuildUi()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Color.FromArgb(14, 20, 34), Padding = new Padding(18) };
        sidebar.Controls.Add(new Label { Text = "NOXO\nPARENTAL", Dock = DockStyle.Top, Height = 70, Font = new Font("Segoe UI", 16, FontStyle.Bold) });
        sidebar.Controls.Add(new Label { Text = "MODE PARENT", Dock = DockStyle.Bottom, Height = 40, ForeColor = Color.FromArgb(99, 102, 241) });

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28), BackColor = Color.FromArgb(9, 13, 24) };
        main.Controls.Add(new Label { Text = "Espace parent", Dock = DockStyle.Top, Height = 48, Font = new Font("Segoe UI", 23, FontStyle.Bold) });
        main.Controls.Add(new Label { Text = "Gère les appareils enfants sans interface technique.", Dock = DockStyle.Top, Height = 32, ForeColor = Color.FromArgb(148, 163, 184) });

        var pairingCard = new Panel { Dock = DockStyle.Top, Height = 190, BackColor = Color.FromArgb(17, 24, 39), Padding = new Padding(20) };
        pairingCard.Controls.Add(new Label { Text = "Code de connexion enfant", Dock = DockStyle.Top, Height = 30, ForeColor = Color.FromArgb(148, 163, 184) });
        code.TextAlign = ContentAlignment.MiddleLeft;
        code.Font = new Font("Segoe UI", 34, FontStyle.Bold);
        code.Dock = DockStyle.Top;
        code.Height = 65;
        pairingCard.Controls.Add(code);
        var newCode = ButtonOf("Générer un nouveau code", 190);
        newCode.Click += (_, _) => GenerateCode();
        pairingCard.Controls.Add(newCode);
        paired.Dock = DockStyle.Bottom;
        paired.Height = 30;
        pairingCard.Controls.Add(paired);
        main.Controls.Add(pairingCard);

        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 125, ColumnCount = 3, Padding = new Padding(0, 10, 0, 10) };
        for (var i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.Controls.Add(Card("État", state), 0, 0);
        cards.Controls.Add(Card("Réseau", address), 1, 0);
        cards.Controls.Add(Card("Règles", rules), 2, 0);
        main.Controls.Add(cards);

        var dashboard = ButtonOf("Ouvrir le contrôle avancé", 220);
        dashboard.Click += (_, _) => OpenDashboard();
        main.Controls.Add(dashboard);
        Controls.Add(main);
        Controls.Add(sidebar);
    }

    private static Label LabelOf(string text) => new() { Text = text, AutoSize = false, ForeColor = Color.White };

    private static Panel Card(string title, Label value)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(17, 24, 39), Padding = new Padding(12), Margin = new Padding(0, 0, 8, 0) };
        p.Controls.Add(value);
        value.Dock = DockStyle.Fill;
        p.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(148, 163, 184) });
        return p;
    }

    private static Button ButtonOf(string text, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 42,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(79, 70, 229),
        ForeColor = Color.White,
        Cursor = Cursors.Hand
    };

    private void StartAsync()
    {
        try
        {
            GenerateCode();
            for (var p = 20570; p <= 20590; p++)
            {
                var candidate = new LocalServer(p, settings, pairing, Log);
                candidate.Start();
                if (candidate.IsRunning)
                {
                    server = candidate;
                    port = p;
                    break;
                }
                candidate.Dispose();
            }

            if (server is null) throw new InvalidOperationException("Impossible d'ouvrir le serveur parent.");
            state.Text = "● Parent actif";
            state.ForeColor = Color.FromArgb(74, 222, 128);
            address.Text = GetLocalIp() + ":" + port;
            var snap = settings.Snapshot();
            rules.Text = $"{snap.BlockedApps.Count} app. • {snap.DailyLimitMinutes} min";
        }
        catch (Exception ex)
        {
            state.Text = "● Erreur";
            state.ForeColor = Color.FromArgb(248, 113, 113);
            Log("error", ex.Message);
        }
    }

    private void GenerateCode()
    {
        code.Text = pairing.GenerateCode();
        paired.Text = "En attente de l'appareil enfant…";
    }

    private void Log(string type, string message)
    {
        if (!closing && type == "error") state.Text = "● Erreur";
    }

    private void OpenDashboard()
    {
        try { Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}/") { UseShellExecute = true }); } catch { }
    }

    private static string GetLocalIp()
    {
        try
        {
            return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(x))?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    private void Stop()
    {
        closing = true;
        try { server?.Dispose(); } catch { }
    }
}

public sealed class ChildForm : Form
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly TextBox host = new() { PlaceholderText = "Adresse du PC parent, ex. 192.168.1.20", Width = 360 };
    private readonly TextBox code = new() { PlaceholderText = "Code à 6 chiffres", Width = 360, MaxLength = 6 };
    private readonly Label state = LabelOf("Non connecté");
    private readonly Label rules = LabelOf("Entre l'adresse et le code du PC parent.");

    public ChildForm()
    {
        Text = "Noxo Parental — Enfant";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(650, 520);
        BackColor = Color.FromArgb(9, 13, 24);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(50), BackColor = Color.FromArgb(9, 13, 24) };
        main.Controls.Add(new Label { Text = "Connexion", Dock = DockStyle.Top, Height = 55, Font = new Font("Segoe UI", 25, FontStyle.Bold) });
        main.Controls.Add(new Label { Text = "Entre l'adresse du PC parent puis le code affiché dessus.", Dock = DockStyle.Top, Height = 45, ForeColor = Color.FromArgb(148, 163, 184) });
        host.Dock = DockStyle.Top;
        main.Controls.Add(host);
        code.Dock = DockStyle.Top;
        main.Controls.Add(code);
        var connect = new Button { Text = "Se connecter", Dock = DockStyle.Top, Height = 45, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(79, 70, 229), ForeColor = Color.White };
        connect.Click += async (_, _) => await PairAsync();
        main.Controls.Add(connect);
        state.Dock = DockStyle.Top;
        state.Height = 55;
        state.Padding = new Padding(0, 15, 0, 0);
        main.Controls.Add(state);
        rules.Dock = DockStyle.Top;
        rules.Height = 100;
        rules.ForeColor = Color.FromArgb(148, 163, 184);
        main.Controls.Add(rules);
        Controls.Add(main);
    }

    private static Label LabelOf(string text) => new() { Text = text, AutoSize = false, ForeColor = Color.White };

    private async Task PairAsync()
    {
        try
        {
            var address = host.Text.Trim();
            if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) address = address[7..];
            if (!address.Contains(':')) address += ":20570";
            if (!Uri.TryCreate("http://" + address, UriKind.Absolute, out var uri)) throw new InvalidOperationException("Adresse parent invalide.");
            if (code.Text.Length != 6 || !code.Text.All(char.IsDigit)) throw new InvalidOperationException("Le code doit contenir 6 chiffres.");

            state.Text = "Connexion…";
            var response = await http.PostAsJsonAsync(new Uri(uri, "/api/pair"), new { code = code.Text.Trim() });
            var data = await response.Content.ReadFromJsonAsync<PairResponse>();
            if (!response.IsSuccessStatusCode || data is null || string.IsNullOrWhiteSpace(data.Token)) throw new InvalidOperationException("Code incorrect, expiré ou parent inaccessible.");

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, "/api/settings"));
            request.Headers.TryAddWithoutValidation("X-Child-Token", data.Token);
            using var r = await http.SendAsync(request);
            r.EnsureSuccessStatusCode();
            var cfg = await r.Content.ReadFromJsonAsync<AgentConfig>();
            state.Text = "● Connecté au parent";
            state.ForeColor = Color.FromArgb(74, 222, 128);
            rules.Text = cfg is null ? "Règles reçues." : $"Limite : {cfg.DailyLimitMinutes} min/jour\r\nPlage : {cfg.StartTime} → {cfg.EndTime}\r\nApplications surveillées : {cfg.BlockedApps.Count}";
        }
        catch (Exception ex)
        {
            state.Text = "● Connexion impossible";
            state.ForeColor = Color.FromArgb(248, 113, 113);
            rules.Text = ex.Message;
        }
    }

    private sealed class PairResponse { public string? Token { get; set; } }
}

public sealed class AgentConfig
{
    public int DailyLimitMinutes { get; set; }
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "21:00";
    public List<string> BlockedApps { get; set; } = [];
}
