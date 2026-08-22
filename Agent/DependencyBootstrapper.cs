using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Net.Http;

namespace NoxoParental;

internal static class DependencyBootstrapper
{
    private const string WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/?linkid=2124703";

    public static bool IsWebView2Installed()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> EnsureWebView2Async(IProgress<string>? progress = null)
    {
        if (IsWebView2Installed())
        {
            progress?.Report("WebView2 est déjà installé.");
            return true;
        }

        progress?.Report("WebView2 n'est pas installé. Téléchargement du composant Microsoft...");

        var tempFile = Path.Combine(Path.GetTempPath(), $"NoxoParental-WebView2-{Guid.NewGuid():N}.exe");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NoxoParental/0.2");

            await using (var input = await client.GetStreamAsync(WebView2BootstrapperUrl))
            await using (var output = File.Create(tempFile))
            {
                await input.CopyToAsync(output);
            }

            progress?.Report("Installation de WebView2...");

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = tempFile,
                Arguments = "/silent /install",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetTempPath()
            });

            if (process is null)
                return false;

            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && !IsWebView2Installed())
            {
                progress?.Report($"L'installation de WebView2 a échoué (code {process.ExitCode}).");
                return false;
            }

            var installed = IsWebView2Installed();
            progress?.Report(installed ? "WebView2 est prêt." : "WebView2 n'a pas pu être détecté après l'installation.");
            return installed;
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Installation WebView2 annulée.");
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            progress?.Report("Installation annulée par Windows.");
            return false;
        }
        catch (Exception ex)
        {
            progress?.Report($"Impossible d'installer WebView2 : {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }
}
