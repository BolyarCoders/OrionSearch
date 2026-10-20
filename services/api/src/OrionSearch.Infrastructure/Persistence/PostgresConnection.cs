using Npgsql;

namespace OrionSearch.Infrastructure.Persistence;

public static class PostgresConnection
{
    public static string? Resolve(Func<string, string?> setting)
    {
        // Preserve an explicit configuration value, including an intentionally empty one.
        if (setting("ConnectionStrings:OrionSearch") is { } explicitValue) return explicitValue;
        if (!string.IsNullOrWhiteSpace(setting("DATABASE_URL"))) return FromUri(setting("DATABASE_URL")!);
        if (string.IsNullOrWhiteSpace(setting("PGHOST"))) return null;
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = setting("PGHOST"),
            Database = setting("PGDATABASE"),
            Username = setting("PGUSER"),
            Password = setting("PGPASSWORD")
        };
        if (setting("PGPORT") is { Length: > 0 } port)
        {
            if (!int.TryParse(port, out var number) || number is < 1 or > 65535)
                throw new InvalidOperationException("PGPORT must be between 1 and 65535.");
            connection.Port = number;
        }
        return connection.ConnectionString;
    }

    public static string FromUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("postgres" or "postgresql")
            || string.IsNullOrWhiteSpace(uri.Host) || uri.Fragment.Length != 0)
            throw new InvalidOperationException("DATABASE_URL must be a valid PostgreSQL URI.");
        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2 || string.IsNullOrWhiteSpace(credentials[0]) || uri.AbsolutePath.Length <= 1)
            throw new InvalidOperationException("DATABASE_URL must specify a user, password and database.");
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1])
        };
        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = parameter.Split('=', 2);
            if (parts.Length != 2 || parts[0] != "sslmode")
                throw new InvalidOperationException("DATABASE_URL supports only the sslmode query parameter; use ConnectionStrings:OrionSearch for other options.");
            connection.SslMode = Uri.UnescapeDataString(parts[1]).ToLowerInvariant() switch
            {
                "disable" => SslMode.Disable,
                "allow" => SslMode.Allow,
                "prefer" => SslMode.Prefer,
                "require" => SslMode.Require,
                "verify-ca" => SslMode.VerifyCA,
                "verify-full" => SslMode.VerifyFull,
                _ => throw new InvalidOperationException("DATABASE_URL contains an unsupported sslmode.")
            };
        }
        return connection.ConnectionString;
    }
}
