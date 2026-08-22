#:include ../models/Url.cs
#:include ../models/PagedQuery.cs
#:package Microsoft.Data.SqlClient@7.0.2

using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dotfiles.Models;
using Microsoft.Data.SqlClient;

namespace Dotfiles.Helpers;

public static class SqlServerHelper {
    public static SqlConnection? BuildSqlServerClient(string? url, string fallbackEnvName = "MSSQL_CONNECTIONSTRING") {
        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(fallbackEnvName))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        return Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToSqlServerClient()
            : new SqlConnection(url);
    }

    extension(SqlConnection client) {
        /// <summary>
        /// Query DB using JSON mapper
        /// </summary>
        /// <param name="sql">SQL query. DO NOT use CTE.</param>
        /// <param name="parameters">SQL parameters</param>
        /// <param name="aggregate">Aggregate query</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public async Task<long> AggregateAsync(string sql, List<SqlParameter> parameters, string aggregate) {
            var queryBuilder = new StringBuilder(sql);
            queryBuilder.Insert(0, $"SELECT CAST({aggregate}('') AS BIGINT) AS AggregateValue FROM (");
            queryBuilder.Append(") AS _AggregatedSource");
            var sqlQuery = queryBuilder.ToString();
            var internalParameters = parameters.ToArray().ToList();
            return await QueryInternal(client, sqlQuery, internalParameters, "AggregateValue") is long result
                ? result
                : 0;
        }

        /// <summary>
        /// Query DB using JSON mapper
        /// </summary>
        /// <param name="sql">SQL query. DO NOT use CTE.</param>
        /// <param name="parameters">SQL parameters</param>
        /// <param name="query">Paged query</param>
        /// <param name="jsonConverter">JSON mapper</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, List<SqlParameter> parameters, PagedQuery query,
            JsonTypeInfo<IEnumerable<T>> jsonConverter) {
            var offset = (query.PageNumber - 1) * query.PageSize;
            var queryBuilder = new StringBuilder(sql);
            queryBuilder.AppendLine("\nOFFSET @pageOffset ROWS FETCH NEXT @pageSize ROWS ONLY");
            queryBuilder.AppendLine("FOR JSON PATH, INCLUDE_NULL_VALUES");
            queryBuilder.Insert(0, "SELECT ISNULL((");
            queryBuilder.Append("), '[]') AS JsonResult");
            var sqlQuery = queryBuilder.ToString();
            var internalParameters = parameters.ToArray().ToList();
            internalParameters.Add(new SqlParameter("@pageOffset", offset));
            internalParameters.Add(new SqlParameter("@pageSize", query.PageSize));
            return await QueryInternal(client, sqlQuery, internalParameters, "JsonResult") is not string jsonResult
                ? throw new InvalidOperationException("Unable to query DB")
                : JsonSerializer.Deserialize(jsonResult, jsonConverter) ?? [];
        }

        private async Task<object?> QueryInternal(string sql, List<SqlParameter> parameters,
            string resultName) {
            await using var command = client.CreateCommand();
            try {
                command.CommandText = sql;
                command.CommandType = CommandType.Text;
                parameters.ForEach(p => command.Parameters.Add(p));
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) {
                    return reader.GetValue(resultName);
                }

                return null;
            }
            finally {
                command.Parameters.Clear();
            }
        }
    }

    extension(Url url) {
        public SqlConnection ToSqlServerClient() {
            var connectionBuilder = new StringBuilder();
            var host = url.Port is > 0 ? $"{url.Host},{url.Port.Value}" : url.Host;
            connectionBuilder.Append($"Server={host};");
            var database = url.Path?.Trim('/');
            connectionBuilder.Append(!string.IsNullOrEmpty(database) ? $"Database={database};" : "Database=master;");
            if (!string.IsNullOrEmpty(url.Username) && !string.IsNullOrEmpty(url.Password)) {
                connectionBuilder.Append($"User ID={url.Username};");
                connectionBuilder.Append($"Password={url.Password};");
            }
            else {
                connectionBuilder.Append("Integrated Security=True;");
            }

            if (url.Extras.TryGetValue("trustServerCertificate", out var trustValue)) {
                var alwaysTrust = (bool.TryParse(trustValue, out var boolValue) && boolValue)
                                  || (int.TryParse(trustValue, out var intValue) && intValue != 0);
                if (alwaysTrust) connectionBuilder.Append("TrustServerCertificate=True;");
            }

            var connectionString = connectionBuilder.ToString();
            return new SqlConnection(connectionString);
        }
    }
}

internal sealed record Aggregate(long Value);

[JsonSerializable(typeof(Aggregate))]
internal sealed partial class SqlServerJsonOpt : JsonSerializerContext;
