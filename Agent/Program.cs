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
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Noxo Parental", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), $"{DateTime.Now:O} {ex}\n");
        };
        Application.Run(new NoxoForm());
    }
}

internal sealed class NoxoForm : ProtonForm
{
    private ProtonWebView? webView;

    public NoxoForm()
    {
        Text = "Noxo Parental";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(246, 248, 252);
        FormClosing += (_, _) => webView?.Dispose();
        Shown += async (_, _) => await InitializeWebAsync();
    }

    private async Task InitializeWebAsync()
    {
        try
        {
            webView = new ProtonWebView(this) { Dock = DockStyle.Fill, AllowResizable = true };
            Controls.Add(webView);
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppContext.BaseDirectory, "webview2"));
            await webView.EnsureCoreWebView2Async(environment);
            webView.DefaultBackgroundColor = Color.Transparent;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("noxo.local", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.Navigate("https://noxo.local/index.html");
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), $"{DateTime.Now:O} WebView: {ex}\n");
            MessageBox.Show("Impossible de démarrer l'interface web. Vérifie que Microsoft Edge WebView2 est installé.\n\n" + ex.Message, "Noxo Parental", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
