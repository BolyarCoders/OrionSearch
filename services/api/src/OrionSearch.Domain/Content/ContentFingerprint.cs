using System.Security.Cryptography;
using System.Text;

namespace OrionSearch.Domain.Content;

public static class ContentFingerprint
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Normalize(NormalizationForm.FormC).ReplaceLineEndings("\n").Trim();
    }

    public static string Compute(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(text))));
}
