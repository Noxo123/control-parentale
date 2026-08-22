using System.Security.Cryptography;
using System.Text;

namespace NoxoParental;

public sealed class PairingStore
{
    private readonly object sync = new();
    private string code = "";
    private string token = "";
    private DateTimeOffset expiresAt = DateTimeOffset.MinValue;
    private bool paired;

    public string Code { get { lock (sync) return code; } }
    public bool IsPaired { get { lock (sync) return paired; } }

    public string GenerateCode()
    {
        lock (sync)
        {
            code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            paired = false;
            return code;
        }
    }

    public bool TryPair(string suppliedCode, out string childToken)
    {
        lock (sync)
        {
            childToken = "";
            var a = Encoding.UTF8.GetBytes(code);
            var b = Encoding.UTF8.GetBytes((suppliedCode ?? "").Trim());
            if (DateTimeOffset.UtcNow > expiresAt || paired || a.Length != b.Length) return false;
            if (!CryptographicOperations.FixedTimeEquals(a, b)) return false;
            paired = true;
            childToken = token;
            return true;
        }
    }

    public bool ValidateToken(string suppliedToken)
    {
        lock (sync)
        {
            if (!paired || string.IsNullOrWhiteSpace(suppliedToken)) return false;
            var a = Encoding.UTF8.GetBytes(token);
            var b = Encoding.UTF8.GetBytes(suppliedToken);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            code = "";
            token = "";
            expiresAt = DateTimeOffset.MinValue;
            paired = false;
        }
    }
}
