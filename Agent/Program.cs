using System.Drawing;
using Microsoft.Web.WebView2.Core;
using Proton;

namespace NoxoParental;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => WriteCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) WriteCrash(ex);
        };

        Application.Run(new NoxoForm());
    }

    internal static void WriteCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoxoParental");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch { }
    }
}

internal sealed class NoxoForm : ProtonForm
{
    private ProtonWebView? webView;
    private Label? status;
    private Button? retry;

    public NoxoForm()
    {
        Text = "Noxo Parental";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(246, 248, 252);
        Shown += async (_, _) => await InitializeWebAsync();
        FormClosing += (_, _) => webView?.Dispose();
    }

    private async Task InitializeWebAsync()
    {
        ShowLoading("Vérification des composants…");

        if (!DependencyBootstrapper.IsWebView2Installed())
        {
            var progress = new Progress<string>(ShowLoading);
            var installed = await DependencyBootstrapper.EnsureWebView2Async(progress);
            if (!installed)
            {
                ShowError("WebView2 est nécessaire pour Noxo Parental.");
                return;
            }
        }

        try
        {
            ShowLoading("Démarrage de l'interface…");
            webView = new ProtonWebView(this) { Dock = DockStyle.Fill, AllowResizable = true };
            Controls.Add(webView);

            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoxoParental", "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(environment);
            webView.DefaultBackgroundColor = Color.Transparent;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("noxo.local", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.Navigate("https://noxo.local/index.html");
        }
        catch (Exception ex)
        {
            Program.WriteCrash(ex);
            ShowError("L'interface web n'a pas pu démarrer.\n\n" + ex.Message);
        }
    }

    private void ShowLoading(string message)
    {
        if (IsDisposed) return;
        Controls.Clear();
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(246, 248, 252) };
        var title = new Label { Text = "Noxo Parental", AutoSize = true, Font = new Font("Segoe UI", 26, FontStyle.Bold), Location = new Point(40, 40) };
        status = new Label { Text = message, AutoSize = true, Font = new Font("Segoe UI", 11), Location = new Point(43, 105) };
        panel.Controls.Add(title);
        panel.Controls.Add(status);
        Controls.Add(panel);
    }

    private void ShowError(string message)
    {
        Controls.Clear();
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(246, 248, 252) };
        panel.Controls.Add(new Label { Text = "Noxo Parental", AutoSize = true, Font = new Font("Segoe UI", 26, FontStyle.Bold), Location = new Point(40, 40) });
        panel.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(800, 0), Font = new Font("Segoe UI", 11), Location = new Point(43, 105) });
        retry = new Button { Text = "Réessayer", AutoSize = true, Location = new Point(40, 165) };
        retry.Click += async (_, _) => await InitializeWebAsync();
        panel.Controls.Add(retry);
        Controls.Add(panel);
    }
}
