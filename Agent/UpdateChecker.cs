using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace NoxoParental;

public sealed record GitHubRelease(string tag_name, string html_url, string name);

public static class UpdateChecker
{
    private const string ReleaseApi = "https://api.github.com/repos/Noxo123/control-parentale/releases/latest";

    public static async Task<GitHubRelease?> CheckAsync(HttpClient http)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
        request.Headers.UserAgent.ParseAdd("NoxoParental/1.0");
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<GitHubRelease>();
    }

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        tag = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(tag, out var version) ? version : null;
    }

    public static void OpenRelease(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
