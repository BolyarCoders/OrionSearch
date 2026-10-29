namespace OrionSearch.Api.Authentication;

public sealed class NativeIdentitySettings
{
    public bool Enabled { get; set; }
    public string KeyEncryptionCertificatePath { get; set; } = "";
    public string KeyEncryptionCertificateBase64 { get; set; } = "";
    public string KeyEncryptionCertificatePassword { get; set; } = "";
    public int RequestsPerMinute { get; set; } = 10;

    public bool IsValid(bool development) => !Enabled || RequestsPerMinute is >= 1 and <= 1000
        && (string.IsNullOrWhiteSpace(KeyEncryptionCertificatePath) || string.IsNullOrWhiteSpace(KeyEncryptionCertificateBase64))
        && (development || Path.IsPathFullyQualified(KeyEncryptionCertificatePath) || !string.IsNullOrWhiteSpace(KeyEncryptionCertificateBase64));
}
