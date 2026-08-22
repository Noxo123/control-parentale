using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace NoxoParental;

public sealed record GitHubRelease(string? tag_name, string? html_url, string? name, string? published_at);

public static class UpdateChecker
{
    private const string ReleaseApi = "https://api.github.com/repos/Noxo123/control-parentale/releases/latest";
    private static readonly Version FallbackVersion = new(1, 0, 4);

    public static async Task<GitHubRelease?> CheckAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NoxoParental", CurrentVersion.ToString(3)));
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return null;

        return JsonSerializer.Deserialize<GitHubRelease>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null || version == new Version(0, 0, 0, 0) ? FallbackVersion : version;
        }
    }

    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var dash = value.IndexOf('-');
        if (dash >= 0) value = value[..dash];
        return Version.TryParse(value, out var version) ? version : null;
    }

    public static bool IsNewer(string? tag, out Version? latest)
    {
        latest = ParseTag(tag);
        return latest is not null && latest > CurrentVersion;
    }

    public static void OpenRelease(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
    }
}
