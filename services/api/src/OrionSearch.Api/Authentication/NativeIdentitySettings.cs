namespace OrionSearch.Api.Authentication;

public sealed class NativeIdentitySettings
{
    public bool Enabled { get; set; }
    public string KeyEncryptionCertificatePath { get; set; } = "";
    public string KeyEncryptionCertificateBase64 { get; set; } = "";
    public string KeyEncryptionCertificatePassword { get; set; } = "";
    // Retained for compatibility with existing configuration; no longer enforced.
    public int RequestsPerMinute { get; set; } = 10;

    public bool IsValid(bool development) => !Enabled || (string.IsNullOrWhiteSpace(KeyEncryptionCertificatePath) || string.IsNullOrWhiteSpace(KeyEncryptionCertificateBase64))
        && (development || Path.IsPathFullyQualified(KeyEncryptionCertificatePath) || !string.IsNullOrWhiteSpace(KeyEncryptionCertificateBase64));
}
