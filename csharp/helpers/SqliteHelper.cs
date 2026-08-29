#:include ../models/DbParameter.cs
#:include ../models/Url.cs

#:package Microsoft.Data.Sqlite@10.0.11

using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Dotfiles.Models;
using Microsoft.Data.Sqlite;
using Npgsql;
using DbParameter = Dotfiles.Models.DbParameter;

namespace Dotfiles.Helpers;

public static partial class SqliteHelper {
    private static readonly Regex WithRegex = WithPattern();

    public static SqliteConnection? BuildSqliteClient(string? url, string fallbackEnvName = "DATABASE_URL") {
        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(fallbackEnvName))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        return Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToSqliteClient()
            : new SqliteConnection(url);
    }

    private static string WrapSqlQuery(string rawSql) {
        // SQLite 3.38+ supports json_object() combined with subquery results.
        // If a CTE exists at the top level, extract and lift it to avoid syntax errors inside the subquery.
        return WithRegex.IsMatch(rawSql)
            // language=sqlite
            ? $"""
               WITH __user_cte__ AS (
                   {rawSql}
               )
               SELECT json_object(*) FROM __user_cte__
               """
            : $"""
               SELECT json_object(*) 
               FROM (
                   {rawSql}
               ) __q__
               """;
    }

    [GeneratedRegex(@"^\s*WITH\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WithPattern();

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

        /// <summary>
        /// Streams database records as strongly typed objects using JSON converter.
        /// </summary>
        /// <param name="sql">SQL query having resultset</param>
        /// <param name="jsonTypeInfo">JSON converter</param>
        /// <param name="parameters">SQL query parameters</param>
        /// <param name="transaction">Optional DB transaction</param>
        /// <param name="cancellationToken"></param>
        public async IAsyncEnumerable<T> QueryAsync<T>(
            string sql, JsonTypeInfo<T> jsonTypeInfo,
            IReadOnlyList<DbParameter>? parameters = null, IDbTransaction? transaction = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await using var command = db.CreateCommand();
            command.CommandText = WrapSqlQuery(sql);
            if (parameters is { Count: > 0 }) db.PopulateParameters(command, parameters);
            command.Transaction = transaction switch {
                null => null,
                SqliteTransaction sqliteTransaction => sqliteTransaction,
                _ => throw new InvalidOperationException("Only accept transaction with same DB")
            };

            // SequentialAccess prevents the driver from buffering entire rows/payloads in RAM
            await using var reader =
                await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) {
                if (reader.IsDBNull(0)) continue;

                await using var stream = reader.GetStream(0);
                var item = await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken);
                if (item is not null) yield return item;
            }
        }

        private void PopulateParameters(SqliteCommand command, IReadOnlyList<DbParameter> parameters) {
            foreach (var parameter in parameters) {
                var parameterName = parameter.Name.TrimStart('@');
                switch (parameter) {
                    case DbParameter.Null:
                        command.Parameters.AddWithValue(parameterName, DBNull.Value);
                        break;
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
