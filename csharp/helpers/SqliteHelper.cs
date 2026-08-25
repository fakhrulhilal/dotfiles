#:include ../models/DbParameter.cs
#:include ../models/Url.cs

#:package Microsoft.Data.Sqlite@10.0.11

using System.Data;
using Dotfiles.Models;
using Microsoft.Data.Sqlite;
using DbParameter = Dotfiles.Models.DbParameter;

namespace Dotfiles.Helpers;

public static class SqliteHelper {
    public static SqliteConnection? BuildSqliteClient(string? url, string fallbackEnvName = "DATABASE_URL") {
        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(fallbackEnvName))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        return Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToSqliteClient()
            : new SqliteConnection(url);
    }

    extension(SqliteConnection db) {
        public async ValueTask EnsureOpenAsync() {
            if (db.State == ConnectionState.Closed) await db.OpenAsync();
        }

        public async ValueTask<int> ExecuteAsync(string sql, IReadOnlyList<DbParameter>? parameters = null,
            IDbTransaction? transaction = null) {
            await using var command = db.CreateCommand();
            if (parameters is { Count: > 0 }) db.PopulateParameters(command, parameters);
            command.Transaction = transaction switch {
                null => null,
                SqliteTransaction npgsqlTransaction => npgsqlTransaction,
                _ => throw new InvalidOperationException("Only accept transaction with same DB")
            };
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync();
        }

        private void PopulateParameters(SqliteCommand command, IReadOnlyList<DbParameter> parameters) {
            foreach (var parameter in parameters) {
                var parameterName = parameter.Name.TrimStart('@');
                switch (parameter) {
                    case DbParameter.Bit bit:
                        command.Parameters.AddWithValue(parameterName, bit.Value);
                        break;
                    case DbParameter.Date date:
                        command.Parameters.AddWithValue(parameterName, date.Value);
                        break;
                    case DbParameter.FullTime fullTime:
                        command.Parameters.AddWithValue(parameterName, fullTime.Value);
                        break;
                    case DbParameter.Ip ip:
                        command.Parameters.AddWithValue(parameterName, ip.Value.ToString());
                        break;
                    case DbParameter.Json json:
                        command.Parameters.AddWithValue(parameterName, json.Value);
                        break;
                    case DbParameter.Number number:
                        command.Parameters.AddWithValue(parameterName, number.Value);
                        break;
                    case DbParameter.Text text:
                        command.Parameters.AddWithValue(parameterName, text.Value);
                        break;
                    case DbParameter.LongText text:
                        command.Parameters.AddWithValue(parameterName, text.Value);
                        break;
                    case DbParameter.Time time:
                        command.Parameters.AddWithValue(parameterName, time.Value);
                        break;
                }
            }
        }
    }

    extension(Url url) {
        public SqliteConnection ToSqliteClient() {
            var builder = new SqliteConnectionStringBuilder { DataSource = url.Path };
            if (!string.IsNullOrEmpty(url.Password))
                builder.Password = url.Password;
            builder.Pooling = true;
            foreach (var (key, value) in url.Extras) {
                switch (key.ToLowerInvariant()) {
                    case "cache" when Enum.TryParse<SqliteCacheMode>(value, out var cacheMode):
                        builder.Cache = cacheMode;
                        break;
                    case "mode" when Enum.TryParse<SqliteOpenMode>(value, out var mode):
                        builder.Mode = mode;
                        break;
                    case "password":
                        builder.Password = value;
                        break;
                    case "defaulttimeout" when int.TryParse(value, out var timeout):
                        builder.DefaultTimeout = timeout;
                        break;
                    case "Pooling" when bool.TryParse(value, out var pooling):
                        builder.Pooling = pooling;
                        break;
                }
            }

            var connectionString = builder.ToString();
            return new SqliteConnection(connectionString);
        }
    }
}
