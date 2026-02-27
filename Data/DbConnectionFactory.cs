using Npgsql;

namespace CiberZone.API.Data;

public static class DbConnectionFactory
{
    private const string LocalFallbackConnection =
        "Host=localhost;Port=5432;Database=ciberzone_db;Username=postgres;Password=postgres";

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var fromConnectionStrings = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(fromConnectionStrings))
        {
            return NormalizeConnectionString(fromConnectionStrings);
        }

        var databaseUrl = configuration["DATABASE_URL"]
                          ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            return NormalizeConnectionString(databaseUrl);
        }

        return LocalFallbackConnection;
    }

    public static string NormalizeConnectionString(string rawConnection)
    {
        var value = (rawConnection ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return LocalFallbackConnection;
        }

        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return FromPostgresUri(value);
        }

        // Common typo from logs: Host=tcp://host:port
        value = value.Replace("Host=tcp://", "Host=", StringComparison.OrdinalIgnoreCase);
        value = value.Replace("Server=tcp://", "Server=", StringComparison.OrdinalIgnoreCase);

        var builder = new NpgsqlConnectionStringBuilder(value);
        if (!string.IsNullOrWhiteSpace(builder.Host) &&
            builder.Host.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
            builder.Host = builder.Host["tcp://".Length..];
        }

        return builder.ConnectionString;
    }

    private static string FromPostgresUri(string uri)
    {
        var parsed = new Uri(uri);
        var userInfo = parsed.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;

        var database = parsed.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(database))
        {
            database = "postgres";
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = parsed.Host,
            Port = parsed.Port > 0 ? parsed.Port : 5432,
            Username = username,
            Password = password,
            Database = database
        };

        var query = parsed.Query.TrimStart('?');
        if (!string.IsNullOrWhiteSpace(query))
        {
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var item = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(item[0]);
                var val = item.Length > 1 ? Uri.UnescapeDataString(item[1]) : string.Empty;

                switch (key.ToLowerInvariant())
                {
                    case "sslmode":
                        if (Enum.TryParse<SslMode>(val, true, out var sslMode))
                        {
                            builder.SslMode = sslMode;
                        }
                        break;
                    case "timeout":
                        if (int.TryParse(val, out var timeout))
                        {
                            builder.Timeout = timeout;
                        }
                        break;
                    case "command_timeout":
                        if (int.TryParse(val, out var cmdTimeout))
                        {
                            builder.CommandTimeout = cmdTimeout;
                        }
                        break;
                }
            }
        }

        return builder.ConnectionString;
    }
}
