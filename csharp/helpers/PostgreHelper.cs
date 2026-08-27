#:include ../models/DbParameter.cs
#:include ../models/Url.cs

#:package Npgsql@10.0.3

using System.Data;
using Dotfiles.Models;
using Npgsql;
using NpgsqlTypes;
using ConnectionBuilder = Npgsql.NpgsqlConnectionStringBuilder;

namespace Dotfiles.Helpers;

delegate void ConfigFactory(string value, ConnectionBuilder config);

public static class PostgreHelper {
    private readonly struct Defaults {
        public const string Host = "localhost";
        public const int Port = 5432;
        public const string Database = "postgres";
        public static readonly StringComparer CompareMode = StringComparer.InvariantCultureIgnoreCase;
    }

    private static ConfigFactory SetBool(Action<ConnectionBuilder, bool> setter) {
        return (value, config) => {
            if (bool.TryParse(value, out var result)) setter(config, result);
            else if (int.TryParse(value, out var intValue)) setter(config, intValue != 0);
        };
    }

    private static ConfigFactory SetInt(Action<ConnectionBuilder, int> setter) {
        return (value, config) => {
            if (int.TryParse(value, out var result)) setter(config, result);
        };
    }

    private static ConfigFactory SetString(Action<ConnectionBuilder, string> setter) {
        return (value, config) => {
            if (!string.IsNullOrWhiteSpace(value)) setter(config, value);
        };
    }

    private static ConfigFactory SetEnum<TValue>(Action<ConnectionBuilder, TValue> setter)
        where TValue : struct, Enum {
        return (value, config) => {
            if (Enum.TryParse(value, out TValue result)) setter(config, result);
        };
    }

    private static readonly Dictionary<string, ConfigFactory> ConfigFactories = new(Defaults.CompareMode) {
        ["passfile"] = SetString((cfg, val) => cfg.Passfile = val),
        ["require_auth"] = SetString((cfg, val) => cfg.RequireAuth = val),
        ["channel_binding"] = SetEnum<ChannelBinding>((cfg, val) => cfg.ChannelBinding = val),
        ["timeout"] = SetInt((cfg, val) => cfg.Timeout = val),
        ["connect_timeout"] = SetInt((cfg, val) => cfg.Timeout = val),
        ["command_timeout"] = SetInt((cfg, val) => cfg.CommandTimeout = val),
        ["client_encoding"] = SetString((cfg, val) => cfg.ClientEncoding = val),
        ["application_name"] = SetString((cfg, val) => cfg.ApplicationName = val),
        ["keepalives"] = SetBool((cfg, val) => cfg.TcpKeepAlive = val),
        ["keepalives_interval"] = SetInt((cfg, val) => cfg.TcpKeepAliveInterval = val),
        ["keepalives_count"] = SetInt((cfg, val) => cfg.KeepAlive = val),
        ["gssencmode"] = SetEnum<GssEncryptionMode>((cfg, val) => cfg.GssEncryptionMode = val),
        ["sslmode"] = SetEnum<SslMode>((cfg, val) => cfg.SslMode = val),
        ["sslnegotiation"] = SetEnum<SslNegotiation>((cfg, val) => cfg.SslNegotiation = val),
        ["sslcert"] = SetString((cfg, val) => cfg.SslCertificate = val),
        ["sslkey"] = SetString((cfg, val) => cfg.SslKey = val),
        ["sslpassword"] = SetString((cfg, val) => cfg.SslPassword = val),
        ["sslrootcert"] = SetString((cfg, val) => cfg.RootCertificate = val),
    };

    private static ConfigFactory SetEnvString(Action<ConnectionBuilder, string> setter, string? fallback = null) {
        return (envName, config) => {
            var value = Environment.GetEnvironmentVariable(envName) ?? fallback;
            if (!string.IsNullOrWhiteSpace(value)) setter(config, value);
        };
    }

    private static readonly Dictionary<string, ConfigFactory> EnvFactories = new(Defaults.CompareMode) {
        ["PGHOST"] = SetEnvString((cfg, val) => cfg.Host = val, Defaults.Host),
        ["PGDATABASE"] = SetEnvString((cfg, val) => cfg.Database = val, Defaults.Database),
        ["PGPORT"] = (envName, cfg) => {
            var value = Environment.GetEnvironmentVariable(envName);
            cfg.Port = !string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var result)
                ? result
                : Defaults.Port;
        },
        ["PGUSER"] = SetEnvString((cfg, val) => cfg.Username = val),
        ["PGPASSWORD"] = SetEnvString((cfg, val) => cfg.Password = val),
        ["PGPASSFILE"] = SetEnvString((cfg, val) => cfg.Passfile = val),
        ["PGSSLCERT"] = SetEnvString((cfg, val) => cfg.SslCertificate = val),
        ["PGSSLKEY"] = SetEnvString((cfg, val) => cfg.SslKey = val),
        ["PGSSLROOTCERT"] = SetEnvString((cfg, val) => cfg.RootCertificate = val),
        ["PGCLIENTENCODING"] = SetEnvString((cfg, val) => cfg.ClientEncoding = val),
        ["TZ"] = SetEnvString((cfg, val) => cfg.Timezone = val),
        ["PGTZ"] = SetEnvString((cfg, val) => cfg.Timezone = val),
        ["PGAPPNAME"] = SetEnvString((cfg, val) => cfg.ApplicationName = val),
        ["PGSSLNEGOTIATION"] = (envName, cfg) => {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value) &&
                Enum.TryParse<SslNegotiation>(value, true, out var result))
                cfg.SslNegotiation = result;
        }
    };

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
            var builder = new ConnectionBuilder();
            foreach (var (envName, setFromEnv) in EnvFactories) setFromEnv(envName, builder);
            if (!string.IsNullOrEmpty(url.Host)) builder.Host = url.Host;
            if (url is { Port: > 0 }) builder.Port = url.Port.Value;
            var database = url.Path?.Trim('/');
            if (string.IsNullOrEmpty(database)) builder.Database = database;
            if (!string.IsNullOrEmpty(url.Username) && !string.IsNullOrEmpty(url.Password)) {
                builder.Username = url.Username;
                builder.Password = url.Password;
            }

            foreach (var (key, value) in url.Extras) {
                if (ConfigFactories.TryGetValue(key, out var factory))
                    factory(value, builder);
            }

            var connectionString = builder.ToString();
            return new NpgsqlConnection(connectionString);
        }
    }
}
