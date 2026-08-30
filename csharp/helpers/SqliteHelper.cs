#:include ../models/DbParameter.cs
#:include ../models/Url.cs

#:package Microsoft.Data.Sqlite@10.0.11

using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
            await db.EnsureOpenAsync();
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
            await db.EnsureOpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = sql;
            if (parameters is { Count: > 0 }) db.PopulateParameters(command, parameters);
            command.Transaction = transaction switch {
                null => null,
                SqliteTransaction sqliteTransaction => sqliteTransaction,
                _ => throw new InvalidOperationException("Only accept transaction with same DB")
            };

            // SequentialAccess prevents the driver from buffering entire rows/payloads in RAM
            await using var reader =
                await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            var columns = await reader.GetColumnSchemaAsync(cancellationToken);
            var columnMap = new string[columns.Count];
            for (var c = 0; c < columns.Count; c++) columnMap[c] = columns[c].ColumnName;
            using var memoryStream = new MemoryStream();
            var propertyMap =
                jsonTypeInfo.Properties.ToDictionary(x => x.Name, x => x, StringComparer.InvariantCultureIgnoreCase);
            while (await reader.ReadAsync(cancellationToken)) {
                if (reader.IsDBNull(0)) continue;

                memoryStream.Position = 0;
                memoryStream.SetLength(0);
                WriteJson(memoryStream, reader);
                ReadOnlySpan<byte> utf8Json = memoryStream.GetBuffer().AsSpan(0, (int)memoryStream.Length);
                Console.WriteLine(Encoding.UTF8.GetString(utf8Json));
                var item = JsonSerializer.Deserialize(utf8Json, jsonTypeInfo);
                if (item is not null) yield return item;
            }

            yield break;

            void WriteJson(MemoryStream memory, SqliteDataReader row) {
                using var writer = new Utf8JsonWriter(memory, new JsonWriterOptions { SkipValidation = true });
                writer.WriteStartObject();
                for (var ordinal = 0; ordinal < row.FieldCount; ordinal++) {
                    var columnName = columnMap[ordinal];
                    writer.WritePropertyName(columnName);
                    if (row.IsDBNull(ordinal)) writer.WriteNullValue();
                    else {
                        if (!propertyMap.TryGetValue(columnName, out var propInfo)) continue;

                        var targetType = Nullable.GetUnderlyingType(propInfo.PropertyType) ??
                                         propInfo.PropertyType;
                        switch (row.GetValue(ordinal)) {
                            case short number: writer.WriteNumberValue(number); break;
                            case int number: writer.WriteNumberValue(number); break;
                            case long number:
                                if (targetType == typeof(bool))
                                    writer.WriteBooleanValue(number > 0);
                                else
                                    writer.WriteNumberValue(number);
                                break;
                            case double number: writer.WriteNumberValue(number); break;
                            case float number: writer.WriteNumberValue(number); break;
                            case decimal number: writer.WriteNumberValue(number); break;
                            case bool boolean: writer.WriteBooleanValue(boolean); break;
                            case DateTime dateTime: writer.WriteStringValue(dateTime.ToString("O")); break;
                            case DateOnly dateOnly: writer.WriteStringValue(dateOnly.ToString("O")); break;
                            case TimeOnly timeOnly: writer.WriteStringValue(timeOnly.ToString("O")); break;
                            case string text:
                                var isJsonText = text.Length > 0 && (text[0] == '{' || text[0] == '[');
                                if (isJsonText && targetType != typeof(string))
                                    writer.WriteRawValue(text);
                                else if (targetType.IsEnum && int.TryParse(text, out var enumNumber))
                                    writer.WriteNumberValue(enumNumber);
                                else if (targetType == typeof(DateTime)) {
                                    writer.WriteStringValue(DateTime.TryParse(text, out var dt)
                                        ? dt.ToString("O")
                                        : text);
                                }
                                else if (targetType == typeof(DateTimeOffset)) {
                                    writer.WriteStringValue(DateTimeOffset.TryParse(text, out var dto)
                                        ? dto.ToString("O")
                                        : text);
                                }
                                else
                                    writer.WriteStringValue(text);

                                break;
                        }
                    }
                }

                writer.WriteEndObject();
            }
        }

        public async Task<bool> DoesColumnExistsAsync(string table, string column, IDbTransaction? transaction = null) {
            ArgumentException.ThrowIfNullOrWhiteSpace(table);
            ArgumentException.ThrowIfNullOrWhiteSpace(column);
            await db.EnsureOpenAsync();
            await using var command = db.CreateCommand();
            command.Transaction = transaction switch {
                null => null,
                SqliteTransaction sqliteTransaction => sqliteTransaction,
                _ => throw new InvalidOperationException("Only accept transaction with same DB")
            };
            command.CommandText = $"PRAGMA table_info({table})";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                if (column.Equals(reader.GetString(1), StringComparison.InvariantCultureIgnoreCase))
                    return true;
            }

            return false;
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
                        command.Parameters.AddWithValue(parameterName, date.Value.ToString("O"));
                        break;
                    case DbParameter.FullTime fullTime:
                        command.Parameters.AddWithValue(parameterName, fullTime.Value.ToString("O"));
                        break;
                    case DbParameter.Ip ip:
                        command.Parameters.AddWithValue(parameterName, ip.Value.ToString());
                        break;
                    case DbParameter.Json json:
                        command.Parameters.AddWithValue(parameterName,
                            json.Value is { RootElement: var root } ? root.GetRawText() : DBNull.Value);
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
                        command.Parameters.AddWithValue(parameterName, time.Value.ToString("O"));
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
