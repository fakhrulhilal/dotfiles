#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true
#:property EnableConfigurationBindingGenerator=true
#:property NoWarn=NU1510,CS2002
#:property StripSymbols=true

#:include helpers/PostgreHelper.cs
#:include helpers/SqliteHelper.cs
#:include models/JsonConverters.cs
#:include web/DbHealthCheck.cs
#:include web/WebApp.cs

#:package System.Linq.Async@*
#:package Microsoft.Extensions.Telemetry.Abstractions@10

using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dotfiles.Helpers;
using Dotfiles.Models;
using Dotfiles.Web;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using DbType = Dotfiles.Models.DbType;

var builder = WebApp.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, WebOpts.Default);
});
builder.Services.AddOptions<AppConfig>().BindConfiguration("App");
builder.Services.AddOptionsWithValidateOnStart<DbConfig, DbConfigValidator>().BindConfiguration("DB");
builder.Services.AddScoped<IDbConnection>(provider => {
    var config = provider.GetRequiredService<IOptions<DbConfig>>().Value;
    return config.Type switch {
        DbType.Sqlite =>
            SqliteHelper.BuildSqliteClient(config.ConnectionString) ??
            throw new InvalidOperationException(
                $"Failed to build Sqlite client for connection string: {config.ConnectionString}"),
        DbType.Postgre =>
            PostgreHelper.BuildPostgreClient(config.ConnectionString) ??
            throw new InvalidOperationException(
                $"Failed to build Postgre client for connection string: {config.ConnectionString}"),
        _ => throw new InvalidOperationException($"Unsupported DB type: {config.Type}")
    };
});
builder.Services.Configure<ForwardedHeadersOptions>(options => {
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Explicitly allow loopback proxies (Tailscale Serve runs on localhost)
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddHostedService<DbMigration>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck("web", () => HealthCheckResult.Healthy(), ["self"])
    .AddCheck<DbHealthCheck>("db", HealthStatus.Unhealthy, ["dependency"]);
var app = builder.Build();
app.MapHealthChecks("/_health", new() { Predicate = check => check.Tags.Contains("self") }).ShortCircuit();
app.MapHealthChecks("/_health/ready", new() { Predicate = check => check.Tags.Contains("dependency") }).ShortCircuit();
app.Map("/", () => "POST /webhook/{identifier}");
app.MapGet("/webhook/{identifier}", GetWebhookLog);
app.MapPost("/webhook/{identifier}", ReceiveWebhook);
try {
    await app.RunAsync();
}
catch (Exception exception) {
    Console.Error.WriteLine($"Unknown error when running application: {exception.Message}");
    Environment.Exit(1);
}

return;

static async Task GetWebhookLog(string identifier, [FromServices] IDbConnection db,
    HttpContext context, CancellationToken cancellationToken) {
    var dtoStream = db.GetWebhooks(identifier, cancellationToken).Select(x => new WebhookLogDto {
        ReceivedAt = x.ReceivedAt,
        ClientIp = x.ClientIp,
        Headers = x.Headers,
        Body = x.Body,
        Response = x.Response
    });
    context.Response.ContentType = "application/json";
    await JsonSerializer.SerializeAsync(
        context.Response.BodyWriter.AsStream(),
        dtoStream,
        WebOpts.Default.IAsyncEnumerableWebhookLogDto,
        cancellationToken);
}

static async Task<IResult> ReceiveWebhook(HttpContext context, string identifier,
    [FromServices] IOptions<AppConfig> appConfig, [FromServices] IDbConnection db,
    [FromServices] ILogger<WebhookRequest> logger) {
    using var body = await JsonDocument.ParseAsync(context.Request.Body);
    var headers = context.Request.Headers.ToDictionary(x => x.Key, x => string.Join(";", x.Value.ToArray()));
    var request = new WebhookRequest {
        ClientIp = context.GetClientIp() ?? IPAddress.None, Identifier = identifier, Headers = headers, Body = body
    };
    var config = appConfig.Value;
    if (string.IsNullOrWhiteSpace(identifier))
        return await SaveAndReturn(WebhookResponse.BadRequest("Identifier is required"));

    logger.WebhookReceived(body.RootElement.GetRawText());
    if (!string.IsNullOrWhiteSpace(config.SignatureHeader) &&
        (!headers.TryGetValue(config.SignatureHeader, out var header) || string.IsNullOrWhiteSpace(header))) {
        logger.SignatureHeaderNotFound(identifier);
        return await SaveAndReturn(WebhookResponse.BadRequest("Missing signature header"));
    }

    if (body.IsEmpty()) {
        logger.NoPayload(identifier);
        return await SaveAndReturn(WebhookResponse.BadRequest("Empty payload"));
    }

    return await SaveAndReturn(new WebhookResponse { Code = (int)HttpStatusCode.NoContent });

    async Task<IResult> SaveAndReturn(WebhookResponse response) {
        request.Response = response;
        await db.SaveRequest(request);
        return request.Response.ToHttpResult();
    }
}

internal static class Const {
    public const int PageSize = 100;
}

internal sealed class AppConfig {
    public required string? SignatureHeader { get; set; }
}

internal sealed class DbMigration(ILogger<DbMigration> logger, IServiceScopeFactory scopeFactory) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        logger.LogInformation("Migrating database using {DB}", db.ConnectionString);
        if (db is NpgsqlConnection postgre)
            await MigrationPostgre(postgre);
        else if (db is SqliteConnection sqlite)
            await MigrateSqlite(sqlite);
        logger.LogInformation("Database migration completed");
    }

    private async ValueTask MigrationPostgre(NpgsqlConnection db) {
        await db.ExecuteAsync(
            // language=PostgreSQL
            """
            CREATE TABLE IF NOT EXISTS requests (
                id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                received_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                identifier VARCHAR(255) NOT NULL,
                client_ip INET NOT NULL,
                headers JSONB NOT NULL,
                body JSONB NOT NULL,
                response JSONB NOT NULL
            );
            COMMENT ON TABLE requests IS 'Stores incoming webhook requests and their responses';
            COMMENT ON COLUMN requests.headers IS 'HTTP headers stored as JSON key-value pairs';
            COMMENT ON COLUMN requests.body IS 'Raw webhook request body';
            COMMENT ON COLUMN requests.response IS 'WebhookResponse object stored as JSON with code and text fields';
            """);
    }

    private async ValueTask MigrateSqlite(SqliteConnection db) {
        await db.ExecuteAsync(
            // language=SQLite
            """
            CREATE TABLE IF NOT EXISTS requests (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                received_at TEXT NOT NULL DEFAULT (datetime('now')),
                identifier TEXT NOT NULL,
                client_ip TEXT NOT NULL,
                headers TEXT NOT NULL,
                body TEXT NOT NULL,
                response TEXT NOT NULL
            );
            """);
    }
}

internal static class Helper {
    public static bool IsEmpty(this JsonDocument? json) {
        if (json is not { RootElement: var root }) return true;

        return root.ValueKind switch {
            JsonValueKind.Undefined or JsonValueKind.Null => true,
            JsonValueKind.Object => root.GetPropertyCount() == 0,
            JsonValueKind.Array => root.GetArrayLength() == 0,
            JsonValueKind.String => string.IsNullOrWhiteSpace(root.GetString()),
            _ => false
        };
    }

    public static IPAddress? GetClientIp(this HttpContext http) {
        if (http.Request.Headers.TryGetValue("X-Forwarded-For", out var headers) &&
            headers.FirstOrDefault() is { } header && !string.IsNullOrWhiteSpace(header) &&
            IPAddress.TryParse(header, out var ip))
            return ip;

        return http.Connection.RemoteIpAddress;
    }

    extension(IDbConnection db) {
        public IAsyncEnumerable<WebhookRequest> GetWebhooks(string identifier, CancellationToken token = default) {
            DbParameter[] parameters = [DbParameter.Create("identifier", identifier)];
            switch (db) {
                case NpgsqlConnection postgre:
                    return postgre.QueryAsync(
                        // language=PostgreSQL
                        $"""
                         SELECT *
                         FROM requests
                         WHERE identifier = @identifier
                         ORDER BY received_at DESC
                         LIMIT {Const.PageSize}
                         """, DbOpts.Default.WebhookRequest, parameters, cancellationToken: token);
                case SqliteConnection sqlite:
                    return sqlite.QueryAsync(
                        // language=SQLite
                        $"""
                         SELECT *
                         FROM requests
                         WHERE identifier = @identifier
                         ORDER BY received_at DESC
                         LIMIT {Const.PageSize}
                         """, DbOpts.Default.WebhookRequest, parameters, cancellationToken: token);
                default:
                    return AsyncEnumerable.Empty<WebhookRequest>();
            }
        }

        public async ValueTask SaveRequest(WebhookRequest request) {
            DbParameter[] parameters = [
                DbParameter.Create("identifier", request.Identifier),
                DbParameter.Create("ip", request.ClientIp),
                DbParameter.Create("headers", request.Headers, DbOpts.Default.DictionaryStringString),
                DbParameter.Create("body", request.Body),
                DbParameter.Create("response", request.Response, DbOpts.Default.WebhookResponse),
            ];
            switch (db) {
                case NpgsqlConnection postgre:
                    await postgre.EnsureOpenAsync();
                    await postgre.ExecuteAsync(
                        // language=PostgreSQL
                        """
                        INSERT INTO requests(client_ip, identifier, headers, body, response)
                        VALUES(@ip, @identifier, @headers, @body, @response)
                        """, parameters);
                    break;
                case SqliteConnection sqlite:
                    await sqlite.EnsureOpenAsync();
                    await sqlite.ExecuteAsync(
                        // language=SQLite
                        """
                        INSERT INTO Requests(client_ip, identifier, headers, body, response)
                        VALUES(@ip, @identifier, @headers, @body, @response)
                        """, parameters);
                    break;
            }
        }
    }
}

internal sealed class WebhookRequest {
    public int Id { get; set; }
    public DateTime ReceivedAt { get; set; }
    public required string Identifier { get; set; }

    [JsonConverter(typeof(IpAddressJsonConverter))]
    public required IPAddress ClientIp { get; set; }

    public required Dictionary<string, string> Headers { get; set; } = [];
    public required JsonDocument Body { get; set; }
    public WebhookResponse Response { get; set; } = null!;
}

internal sealed class WebhookLogDto {
    public DateTime ReceivedAt { get; set; }

    [JsonConverter(typeof(IpAddressJsonConverter))]
    public required IPAddress ClientIp { get; set; }

    public required Dictionary<string, string> Headers { get; set; } = [];
    public required JsonDocument Body { get; set; }
    public WebhookResponse Response { get; set; } = null!;
}

internal sealed class WebhookResponse {
    public required int Code { get; set; }
    public string? Text { get; set; }

    public static WebhookResponse BadRequest(string reason) =>
        new() { Code = (int)HttpStatusCode.BadRequest, Text = reason };

    public IResult ToHttpResult() => (HttpStatusCode)Code switch {
        HttpStatusCode.NotFound => Results.NotFound(Text),
        HttpStatusCode.BadRequest => Results.BadRequest(Text),
        HttpStatusCode.OK => Results.Ok(),
        _ => Results.NoContent()
    };
}

[JsonSerializable(typeof(WebhookRequest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class DbOpts : JsonSerializerContext;

[JsonSerializable(typeof(WebhookLogDto))]
[JsonSerializable(typeof(IAsyncEnumerable<WebhookLogDto>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class WebOpts : JsonSerializerContext;

internal static partial class LoggerExtensions {
#pragma warning disable LOGGEN018 - Let it by stringified
    [LoggerMessage(
        LogLevel.Information,
        "Webhook received with body {Body}")]
    public static partial void WebhookReceived(this ILogger logger, string? body);
#pragma warning restore LOGGEN018

    [LoggerMessage(LogLevel.Information, "No signature header found for identifier {Identifier}")]
    public static partial void SignatureHeaderNotFound(this ILogger logger, string identifier);

    [LoggerMessage(LogLevel.Warning, "No payload found for identifier {Identifier}")]
    public static partial void NoPayload(this ILogger logger, string identifier);
}
