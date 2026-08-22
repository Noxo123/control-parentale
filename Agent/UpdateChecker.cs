using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace NoxoParental;

public sealed record GitHubRelease(string? tag_name, string? html_url, string? name, string? published_at);

public static class UpdateChecker
{
    private const string ReleaseApi = "https://api.github.com/repos/Noxo123/control-parentale/releases/latest";
    private const string TagsApi = "https://api.github.com/repos/Noxo123/control-parentale/tags?per_page=20";
    private static readonly Version FallbackVersion = new(1, 0, 7);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<GitHubRelease?> CheckAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsync(http, ReleaseApi, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
                if (release?.tag_name is not null && ParseTag(release.tag_name) is not null) return release;
            }

            using var tagsResponse = await SendAsync(http, TagsApi, cancellationToken);
            if (!tagsResponse.IsSuccessStatusCode) return null;
            var tagsJson = await tagsResponse.Content.ReadAsStringAsync(cancellationToken);
            var tags = JsonSerializer.Deserialize<List<GitHubTag>>(tagsJson, JsonOptions) ?? [];
            var newest = tags
                .Select(x => new { Tag = x.name, Version = ParseTag(x.name) })
                .Where(x => x.Version is not null)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            return newest is null
                ? null
                : new GitHubRelease(newest.Tag, "https://github.com/Noxo123/control-parentale/releases", newest.Tag, null);
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NoxoParental", CurrentVersion.ToString(3)));
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value[1..];
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
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true }); } catch { }
    }

    private sealed record GitHubTag(string? name);
}
