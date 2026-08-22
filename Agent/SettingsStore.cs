using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoxoParental;

public sealed class ParentalSettings
{
    public int DailyLimitMinutes { get; set; } = 120;
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "21:00";
    public List<string> BlockedApps { get; set; } = [];
    public bool ObservationMode { get; set; } = true;
    public string ParentPinHash { get; set; } = "";
    public string ApiToken { get; set; } = "";
}

public sealed class SettingsStore
{
    private readonly object sync = new();
    private readonly string filePath;
    private ParentalSettings settings;

    public SettingsStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoxoParental");
        Directory.CreateDirectory(dir);
        filePath = Path.Combine(dir, "settings.json");
        settings = Load();
    }

    public ParentalSettings Snapshot()
    {
        lock (sync)
        {
            return new ParentalSettings
            {
                DailyLimitMinutes = settings.DailyLimitMinutes,
                StartTime = settings.StartTime,
                EndTime = settings.EndTime,
                BlockedApps = [.. settings.BlockedApps],
                ObservationMode = settings.ObservationMode,
                ParentPinHash = settings.ParentPinHash,
                ApiToken = settings.ApiToken
            };
        }
    }

    public void Update(Action<ParentalSettings> change)
    {
        lock (sync)
        {
            change(settings);
            Normalize(settings);
            SaveUnsafe();
        }
    }

    public bool VerifyPin(string pin)
    {
        lock (sync)
        {
            if (string.IsNullOrWhiteSpace(settings.ParentPinHash)) return pin == "1234";
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(settings.ParentPinHash),
                SHA256.HashData(Encoding.UTF8.GetBytes(pin)));
        }
    }

    public void SetPin(string pin)
    {
        Update(s => s.ParentPinHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pin))));
    }

    private ParentalSettings Load()
    {
        try
        {
            if (File.Exists(filePath))
            {
                var loaded = JsonSerializer.Deserialize<ParentalSettings>(File.ReadAllText(filePath));
                if (loaded is not null)
                {
                    Normalize(loaded);
                    if (string.IsNullOrWhiteSpace(loaded.ApiToken)) loaded.ApiToken = CreateToken();
                    if (string.IsNullOrWhiteSpace(loaded.ParentPinHash))
                        loaded.ParentPinHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("1234")));
                    File.WriteAllText(filePath, JsonSerializer.Serialize(loaded, new JsonSerializerOptions { WriteIndented = true }));
                    return loaded;
                }
            }
        }
        catch { }

        var fresh = new ParentalSettings
        {
            ApiToken = CreateToken(),
            ParentPinHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("1234")))
        };
        return fresh;
    }

    private void SaveUnsafe() => File.WriteAllText(filePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static void Normalize(ParentalSettings s)
    {
        s.DailyLimitMinutes = Math.Clamp(s.DailyLimitMinutes, 1, 1440);
        if (!TimeSpan.TryParse(s.StartTime, out _)) s.StartTime = "08:00";
        if (!TimeSpan.TryParse(s.EndTime, out _)) s.EndTime = "21:00";
        s.BlockedApps = s.BlockedApps
            .Select(x => x.Trim().ToLowerInvariant().Replace(".exe", ""))
            .Where(x => x.Length > 0 && x.Length <= 80 && x.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
    }
}
