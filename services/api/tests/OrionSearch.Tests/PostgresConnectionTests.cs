using Npgsql;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Tests;

public sealed class PostgresConnectionTests
{
    [Fact]
    public void ExplicitConnectionConfigurationTakesPrecedence()
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:OrionSearch"] = "", ["DATABASE_URL"] = "invalid" };
        Assert.Equal("", PostgresConnection.Resolve(key => values.GetValueOrDefault(key)));
        Assert.Null(PostgresConnection.Resolve(_ => null));
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("postgresql")]
    public void RailwayUriIsConvertedToNpgsqlAndPercentEncodedCredentialsArePreserved(string scheme)
    {
        var result = PostgresConnection.FromUri($"{scheme}://user%40example:p%3Aa%3Bs%40s%2F%25@db.example:6543/research%20dev?sslmode=verify-full");
        var connection = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("user@example", connection.Username);
        Assert.Equal("p:a;s@s/%", connection.Password);
        Assert.Equal("db.example", connection.Host);
        Assert.Equal(6543, connection.Port);
        Assert.Equal("research dev", connection.Database);
        Assert.Equal(SslMode.VerifyFull, connection.SslMode);
        var values = new Dictionary<string, string?> { ["DATABASE_URL"] = $"{scheme}://user:password@db.example/research" };
        Assert.Equal(5432, new NpgsqlConnectionStringBuilder(PostgresConnection.Resolve(key => values.GetValueOrDefault(key))).Port);
    }

    [Fact]
    public void RailwayPgVariablesConstructAnAdoConnectionWithoutInterpolation()
    {
        var values = new Dictionary<string, string?>
        {
            ["PGHOST"] = "postgres.railway.internal",
            ["PGPORT"] = "5433",
            ["PGDATABASE"] = "research",
            ["PGUSER"] = "app",
            ["PGPASSWORD"] = "test;password=literal"
        };
        var connection = new NpgsqlConnectionStringBuilder(PostgresConnection.Resolve(key => values.GetValueOrDefault(key)));
        Assert.Equal(values["PGPASSWORD"], connection.Password);
        Assert.Equal(values["PGHOST"], connection.Host);
        Assert.Equal(5433, connection.Port);
        Assert.Equal("research", connection.Database);
    }

    [Theory]
    [InlineData("https://user:secret@host/db")]
    [InlineData("postgres://host/db")]
    [InlineData("postgres://user:secret@host/")]
    [InlineData("postgres://user:secret@host/db?sslmode=unsafe")]
    [InlineData("postgres://user:secret@host/db?unknown=secret")]
    public void InvalidUriErrorsDoNotEchoSecrets(string uri)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PostgresConnection.FromUri(uri));
        Assert.DoesNotContain("secret", exception.Message);
        Assert.DoesNotContain(uri, exception.Message);
    }
}
