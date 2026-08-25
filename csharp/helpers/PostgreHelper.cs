#:include ../models/DbParameter.cs
#:include ../models/Url.cs

#:package Npgsql@10.0.3

using System.Data;
using Dotfiles.Models;
using Npgsql;
using NpgsqlTypes;

namespace Dotfiles.Helpers;

public static class PostgreHelper {
    private readonly struct Defaults {
        public const string Host = "localhost";
        public const int Port = 5432;
        public const string Database = "postgres";
    }

    public static NpgsqlConnection? BuildPostgreClient(string? url, string fallbackEnvName = "DATABASE_URL") {
        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(fallbackEnvName))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        return Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToPostgreClient()
            : new NpgsqlConnection(url);
    }

    extension(NpgsqlConnection db) {
        public async ValueTask EnsureOpenAsync() {
            if (db.State == ConnectionState.Closed) await db.OpenAsync();
        }

        public async ValueTask<int> ExecuteAsync(string sql, IReadOnlyList<DbParameter>? parameters = null,
            IDbTransaction? transaction = null) {
            await using var command = db.CreateCommand();
            if (parameters is { Count: > 0 }) db.PopulateParameters(command, parameters);
            command.Transaction = transaction switch {
                null => null,
                NpgsqlTransaction npgsqlTransaction => npgsqlTransaction,
                _ => throw new InvalidOperationException("Only accept transaction with same DB")
            };
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync();
        }

        private void PopulateParameters(NpgsqlCommand command, IReadOnlyList<DbParameter> parameters) {
            foreach (var parameter in parameters) {
                var parameterName = parameter.Name.TrimStart('@');
                switch (parameter) {
                    case DbParameter.Bit bit:
                        command.Parameters.AddWithValue(parameterName, NpgsqlDbType.Boolean, bit.Value);
                        break;
                    case DbParameter.Date date:
                        command.Parameters.AddWithValue(parameterName, NpgsqlDbType.Date, date.Value);
                        break;
                    case DbParameter.FullTime fullTime:
                        command.Parameters.AddWithValue(parameterName, NpgsqlDbType.Timestamp, fullTime.Value);
                        break;
                    case DbParameter.Ip ip:
                        command.Parameters.AddWithValue(parameterName, NpgsqlDbType.Inet, ip.Value);
                        break;
                    case DbParameter.Json json:
                        command.Parameters.AddWithValue(parameterName, NpgsqlDbType.Jsonb, json.Value);
                        break;
                    case DbParameter.Number number:
                        command.Parameters.AddWithValue(parameterName, NpgsqlDbType.Integer, number.Value);
                        break;
                    case DbParameter.Text text:
                        command.Parameters.AddWithValue(parameterName, NpgsqlDbType.Varchar, text.Value);
                        break;
                    case DbParameter.LongText text:
                        command.Parameters.AddWithValue(parameterName, NpgsqlDbType.Text, text.Value);
                        break;
                    case DbParameter.Time time:
                        command.Parameters.AddWithValue(parameterName, NpgsqlDbType.Time, time.Value);
                        break;
                }
            }
        }
    }

    extension(Url url) {
        public NpgsqlConnection ToPostgreClient() {
            var builder = new NpgsqlConnectionStringBuilder {
                Host = !string.IsNullOrEmpty(url.Host)
                    ? url.Host
                    : Environment.GetEnvironmentVariable("PGHOST") ?? Defaults.Host,
                Port = url.Port switch {
                    > 0 => url.Port.Value,
                    _ when int.TryParse(Environment.GetEnvironmentVariable("PGPORT"), out var tmpPort) &&
                           tmpPort > 0 => tmpPort,
                    _ => Defaults.Port
                }
            };
            var database = url.Path?.Trim('/');
            builder.Database = !string.IsNullOrEmpty(database)
                ? database
                : Environment.GetEnvironmentVariable("PGDATABASE") ?? Defaults.Database;
            if (!string.IsNullOrEmpty(url.Username) && !string.IsNullOrEmpty(url.Password)) {
                builder.Username = url.Username;
                builder.Password = url.Password;
            }
            else {
                if (Environment.GetEnvironmentVariable("PGUSER") is { } tmpUser && !string.IsNullOrEmpty(tmpUser))
                    builder.Username = tmpUser;
                if (Environment.GetEnvironmentVariable("PGPASSWORD") is { } tmpPass && !string.IsNullOrEmpty(tmpPass))
                    builder.Password = tmpPass;
            }

            foreach (var (key, value) in url.Extras) {
                switch (key.ToLowerInvariant()) {
                    case "sslmode" when Enum.TryParse<SslMode>(value, out var sslMode):
                        builder.SslMode = sslMode;
                        break;
                    case "applicationname":
                        builder.ApplicationName = value;
                        break;
                    case "timeout" when int.TryParse(value, out var timeout):
                        builder.Timeout = timeout;
                        break;
                    case "commandtimeout":
                        builder.CommandTimeout = 1;
                        break;
                }
            }

            var connectionString = builder.ToString();
            return new NpgsqlConnection(connectionString);
        }
    }
}
