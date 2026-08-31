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
#:package NetEscapades.EnumGenerators@1.0.0-beta21*

using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dotfiles.Helpers;
using Dotfiles.Models;
using Dotfiles.Web;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NetEscapades.EnumGenerators;
using Npgsql;
using static Helper;
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
builder.Services.AddCors(opt => {
    var allowedOrigins = builder.Configuration.GetValue<string>("App:AllowedOrigins") ?? "*";
    opt.AddDefaultPolicy(x => {
        x.AllowAnyHeader().AllowAnyMethod();
        if (allowedOrigins == "*")
            x.AllowAnyOrigin();
        else {
            x.AllowCredentials();
            x.WithOrigins(allowedOrigins.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    });
});
builder.Services.AddValidation();
builder.Services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
builder.Services.AddSingleton<ISignatureProvider, WebhookSignatureProvider>();
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
app.UseCors();
app.MapHealthChecks("/_health", new() { Predicate = check => check.Tags.Contains("self") }).ShortCircuit();
app.MapHealthChecks("/_health/ready", new() { Predicate = check => check.Tags.Contains("dependency") }).ShortCircuit();
app.MapGet("/favicon.ico", ([FromServices] IOptions<AppConfig> config) => config.Value switch {
    { IconUrl: var url } when !string.IsNullOrEmpty(url) => Results.Redirect(url, true),
    { IconPath: var path } when !string.IsNullOrEmpty(path) && File.Exists(path) =>
        Results.File(path, "image/x-icon", "favicon.ico"),
    _ => Results.NotFound()
});
app.Map("/", () => "POST /webhook/{identifier}");
app.MapGet("/webhook/{identifier}/{id:int}/{format:alpha=json}", FormatWebhookLog);
app.MapGet("/webhook/{identifier}", GetWebhookLog);
app.MapPost("/webhook/{identifier}", ReceiveWebhook);
app.MapPost("/config", CreateWebhookConfig);
app.MapPatch("/config/{identifier}", SaveWebhookConfig);
app.MapGet("/config/{identifier}", GetWebhookConfig).WithName("GetConfigDetails");
try {
    await app.RunAsync();
}
catch (Exception exception) {
    Console.Error.WriteLine($"Unknown error when running application: {exception.Message}");
    Environment.Exit(1);
}

return;

static async ValueTask<IResult> FormatWebhookLog(
    string identifier, int id, string format,
    [FromServices] IDbConnection db, CancellationToken token = default) {
    if (!FormatType.TryParse(format, out var formatType, true))
        return Invalid(nameof(format), $"Unknown format '{format}'");
    if (await db.GetWebhook(identifier, id, token) is not { } detail) return Results.NotFound();

    return formatType switch {
        FormatType.Json => Results.Json(detail.ToDto(), WebOpts.Default.WebhookLogDto),
        FormatType.Curl => Results.Text(detail.ToCurl(), "text/x-shellscript"),
        FormatType.Fetch => Results.Text(detail.ToFetch(), "text/javascript"),
        FormatType.Netcat => Results.Text(detail.ToNetcat(), "text/x-shellscript"),
        _ => Invalid(nameof(format), $"Unsupported format '{format}'")
    };
}

static async ValueTask GetWebhookLog(string identifier, [FromServices] IDbConnection db,
    HttpContext context, CancellationToken cancellationToken) {
    var dtoStream = db.GetWebhooks(identifier, cancellationToken).Select(x => x.ToDto());
    context.Response.ContentType = "application/json";
    await JsonSerializer.SerializeAsync(
        context.Response.BodyWriter.AsStream(),
        dtoStream,
        WebOpts.Default.IAsyncEnumerableWebhookLogDto,
        cancellationToken);
}

static async ValueTask<IResult> ReceiveWebhook(HttpContext context, string identifier,
    [FromServices] IOptions<AppConfig> appConfig, [FromServices] IDbConnection db,
    [FromServices] ISignatureProvider signatureProvider, [FromServices] ILogger<WebhookRequest> logger) {
    var body = await context.Request.GetRawBody() ?? string.Empty;
    var headers = context.Request.Headers.ToDictionary(x => x.Key, x => string.Join(";", x.Value.ToArray()));
    var request = new WebhookRequest {
        ClientIp = context.GetClientIp() ?? IPAddress.None, Identifier = identifier, Headers = headers, Body = body
    };
    if (string.IsNullOrWhiteSpace(identifier))
        return await SaveAndReturn(WebhookResponse.BadRequest("Identifier is required"));

    if (string.IsNullOrWhiteSpace(body)) {
        logger.NoPayload(identifier);
        return await SaveAndReturn(WebhookResponse.BadRequest("Empty payload"));
    }

    logger.WebhookReceived(identifier, body);
    if (await db.GetConfig(identifier) is not { Validate: true } config)
        return await SaveAndReturn(new WebhookResponse { Code = (int)HttpStatusCode.OK });
    if (config.Secret is null)
        return await SaveAndReturn(WebhookResponse.BadRequest("Webhook misconfigured"));

    var signatureConfig = config.Signature ??= SignatureConfig.WebSub;
    if (signatureProvider.GetReceived(signatureConfig.Header, context.Request.Headers)
        is not { } actualSignature) {
        logger.SignatureHeaderNotFound(identifier);
        return await SaveAndReturn(WebhookResponse.BadRequest("Missing signature header"));
    }

    if (actualSignature.Algorithm.HasValue)
        signatureConfig.Algorithm = actualSignature.Algorithm.Value;
    var expectedSignature = await signatureProvider.ComputeAsync(signatureConfig, config.Secret, context.Request);
    if (expectedSignature != actualSignature) {
        logger.SignatureMissmatch(identifier, expectedSignature.Value, actualSignature.Value);
        return await SaveAndReturn(WebhookResponse.Reject("Signature missmatch"));
    }

    return await SaveAndReturn(new WebhookResponse { Code = (int)HttpStatusCode.Accepted });

    async Task<IResult> SaveAndReturn(WebhookResponse response) {
        request.Response = response;
        await db.SaveRequest(request);
        return request.Response.ToHttpResult();
    }
}

static async ValueTask<IResult> CreateWebhookConfig(CreateConfigDto dto, IDbConnection db, TimeProvider clock) {
    if (await db.GetConfig(dto.Identifier) is not null)
        return Invalid(nameof(dto.Identifier), $"Webhook {dto.Identifier} is not available");
    if (dto.Validate && string.IsNullOrWhiteSpace(dto.Secret.Value))
        return Invalid(nameof(dto.Secret), "Secret is required when enabling validation");

    var createdId = await db.CreateConfig(new WebhookConfig {
        Identifier = dto.Identifier,
        Validate = dto.Validate,
        Signature = dto.Signature,
        Secret = dto.Secret,
        CreatedAt = clock.GetUtcNow().UtcDateTime
    });
    return createdId > 0
        ? Results.CreatedAtRoute("GetConfigDetails",
            RouteValueDictionary.FromArray([KeyValuePair.Create<string, object?>("identifier", dto.Identifier)]))
        : Results.Problem("Failed to create webhook config");
}

static async ValueTask<IResult> SaveWebhookConfig(string identifier, JsonDocument patch, IDbConnection db,
    TimeProvider clock) {
    if (await db.GetConfig(identifier) is not { } config)
        return Invalid(nameof(identifier), $"Webhook {identifier} is not available");

    var model = new SaveConfigDto { Validate = config.Validate, Signature = config.Signature, Secret = config.Secret };
    patch.ApplyMergePatch(model, WebOpts.Default.SaveConfigDto);
    if (model.Validate && (model is not { Secret.Value: var secret } || string.IsNullOrWhiteSpace(secret)))
        return Invalid(nameof(model.Secret), "Secret is required when enabling validation");

    var updated = new WebhookConfig {
        Id = config.Id,
        Identifier = config.Identifier,
        Signature = model.Signature,
        Secret = model.Secret,
        Validate = model.Validate,
        ModifiedAt = clock.GetUtcNow().UtcDateTime
    };
    var savedId = await db.UpdateConfig(updated);
    return savedId == config.Id
        ? Results.NoContent()
        : Results.Problem("Failed to save webhook config");
}

static async ValueTask<IResult> GetWebhookConfig(string identifier, IDbConnection db) {
    var config = await db.GetConfig(identifier);
    return config is not null
        ? Results.Ok(new GetWebhookConfigDto(config))
        : Results.NotFound();
}

internal static class Const {
    public static readonly StringComparer CompareMode = StringComparer.InvariantCultureIgnoreCase;
    public const StringComparison CompareMode2 = StringComparison.InvariantCultureIgnoreCase;
    public const int PageSize = 100;
}

internal sealed class AppConfig {
    public string? IconUrl { get; set; }
    public string? IconPath { get; set; }

    /// <summary>
    /// Comma separated domain or '*' (default) to allow all
    /// </summary>
    public string? AllowedOrigins { get; set; }
}

[EnumExtensions]
enum FormatType { Json, Curl, Fetch, Netcat }

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
        await db.EnsureOpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
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
            """, transaction: transaction);
        await db.ExecuteAsync(
            // language=PostgreSQL
            """
            DO $$ 
            DECLARE 
                v_udt_name text;
            BEGIN
                -- Use udt_name to correctly catch 'jsonb'
                SELECT udt_name INTO v_udt_name
                FROM information_schema.columns 
                WHERE table_name = 'requests' AND column_name = 'body';

                IF FOUND THEN
                    -- Only alter if it's not already a string type
                    IF v_udt_name NOT IN ('text', 'varchar', 'bpchar') THEN
                        ALTER TABLE requests 
                            ALTER COLUMN body TYPE TEXT USING COALESCE(body::text, ''),
                            ALTER COLUMN body SET NOT NULL;
                    END IF;
                ELSE
                    ALTER TABLE requests ADD COLUMN body TEXT NOT NULL DEFAULT '';
                END IF;
            END $$;
            """, transaction: transaction);
        await db.ExecuteAsync(
            // language=PostgreSQL
            """
            CREATE TABLE IF NOT EXISTS configs (
                id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                identifier VARCHAR(255) UNIQUE NOT NULL,
                signature JSONB NOT NULL,
                secret JSONB NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                modified_at TIMESTAMPTZ NULL
            );
            COMMENT ON TABLE configs IS 'Webhook registration configuration';
            COMMENT ON COLUMN configs.identifier IS 'Webhook identifier which is available for use in webhook URLs';
            COMMENT ON COLUMN configs.signature IS 'Signature configuration with the following properties: algorithm, template, header, encoding';
            COMMENT ON COLUMN configs.secret IS 'Secret configuration with the following properties: value, encoding';
            """, transaction: transaction);
        await db.ExecuteAsync(
            // language=PostgreSQL
            """
            DO $$ 
            BEGIN 
                IF EXISTS (
                    SELECT 1 
                    FROM information_schema.columns 
                    WHERE table_name = 'configs' AND column_name = 'signature'
                ) THEN
                    ALTER TABLE configs ALTER COLUMN signature DROP NOT NULL;
                END IF;

                IF EXISTS (
                    SELECT 1 
                    FROM information_schema.columns 
                    WHERE table_name = 'configs' AND column_name = 'secret'
                ) THEN
                    ALTER TABLE configs ALTER COLUMN secret DROP NOT NULL;
                END IF;

                ALTER TABLE configs 
                    ADD COLUMN IF NOT EXISTS validate BOOLEAN NOT NULL DEFAULT TRUE;
            END $$;
            """, transaction: transaction);
        await transaction.CommitAsync();
    }

    private async ValueTask MigrateSqlite(SqliteConnection db) {
        await db.EnsureOpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
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
            """, transaction: transaction);
        await db.ExecuteAsync(
            // language=SQLite
            """
            CREATE TABLE IF NOT EXISTS configs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                identifier TEXT NOT NULL,
                signature TEXT NOT NULL,
                secret TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                modified_at TEXT NULL
            );
            """, transaction: transaction);
        await db.ExecuteAsync(
            // language=SQLite
            """
            PRAGMA foreign_keys=off;

            -- Re-create the new table with the exact same structure, 
            -- BUT omit the 'NOT NULL' for signature and secret.
            -- (Make sure to include all your other existing columns here exactly as they are)
            CREATE TABLE config_new (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                identifier TEXT NOT NULL,
                signature TEXT,
                secret TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                modified_at TEXT NULL
            );

            -- Copy all data from the old table to the new table
            INSERT INTO config_new (id, identifier, signature, secret)
            SELECT id, identifier, signature, secret FROM configs;

            DROP TABLE configs;
            ALTER TABLE config_new RENAME TO configs;
            PRAGMA foreign_keys=on;
            """, transaction: transaction);
        if (!await db.DoesColumnExistsAsync("configs", "validate", transaction))
            await db.ExecuteAsync(
                // language=SQLite
                """
                ALTER TABLE configs ADD COLUMN validate INTEGER NOT NULL DEFAULT 1
                """, transaction: transaction);
        await transaction.CommitAsync();
    }
}

internal interface ISignatureProvider {
    SignatureHash? GetReceived(string headerName, IHeaderDictionary headers);

    Task<SignatureHash> ComputeAsync(SignatureConfig signature, SecretConfig secret, HttpRequest httpRequest,
        string? rawBody = null);
}

internal sealed partial class WebhookSignatureProvider : ISignatureProvider {
    private static readonly Regex VariableRegex = VariablePattern();
    private static readonly Regex HashFunctionRegex = HashFunctionPattern();

    public SignatureHash? GetReceived(string headerName, IHeaderDictionary headers) =>
        !headers.TryGetValue(headerName, out var signatureHeaders) ||
        signatureHeaders.FirstOrDefault() is not { } signatureHeader
            ? null
            : SignatureHash.Parse(signatureHeader);

    public async Task<SignatureHash> ComputeAsync(SignatureConfig signature, SecretConfig secret,
        HttpRequest httpRequest,
        string? rawBody = null) {
        var template = signature.Template;
        rawBody ??= await httpRequest.GetRawBody() ?? string.Empty;
        if (HashFunctionRegex.Match(template) is { Success: true } toHash) {
            var builder = new StringBuilder();
            builder.Append(template[..(toHash.Index + 1)]);
            var contentToCalculate = CompileTemplate(httpRequest.Headers, toHash.Groups["content"].Value, rawBody);
            var hash = CalculateHash(contentToCalculate);
            builder.Append(hash);
            var endIndex = toHash.Index + toHash.Length;
            if (template.Length > endIndex)
                builder.Append(template[endIndex..]);
            template = builder.ToString();
        }

        template = HashFunctionRegex.Replace(template, match => {
            if (match.Groups["content"] is not { Success: true, Value: var content }) return match.Value;

            return content switch {
                "body" => rawBody,
                _ => throw new InvalidOperationException($"Unknown hash function content: {content}")
            };
        });
        var message = CompileTemplate(httpRequest.Headers, template, rawBody);
        return SignatureHash.Parse(message);

        string CalculateHash(string content) {
            var key = secret.Encoding == SecretEncoding.Base64
                ? Convert.FromBase64String(secret.Value)
                : Encoding.UTF8.GetBytes(secret.Value);
            using HashAlgorithm crypto = signature.Algorithm switch {
                SignatureAlgorithm.Sha1 => new HMACSHA1(key),
                SignatureAlgorithm.Sha256 => new HMACSHA256(key),
                SignatureAlgorithm.Sha384 => new HMACSHA384(key),
                SignatureAlgorithm.Sha512 => new HMACSHA512(key),
                _ => throw new ArgumentOutOfRangeException(nameof(signature.Algorithm),
                    signature.Algorithm, "Invalid signature algorithm")
            };
            var hash = crypto.ComputeHash(Encoding.UTF8.GetBytes(content));
            return signature.Encoding == SignatureEncoding.Base64
                ? Convert.ToBase64String(hash)
                : Convert.ToHexStringLower(hash);
        }
    }

    private static string CompileTemplate(IHeaderDictionary headers, string template, string rawBody) =>
        VariableRegex.Replace(template, match => {
            if (match.Groups["name"] is not { Success: true, Value: var name }) return match.Value;
            if ("body".Equals(name, Const.CompareMode2) && !string.IsNullOrEmpty(rawBody)) return rawBody;

            if (match.Groups["key"] is { Success: true, Value: var key } &&
                "header".Equals(key, Const.CompareMode2) &&
                match.Groups["value"] is { Success: true, Value: var headerName }) {
                if ("Authorization".Equals(headerName, Const.CompareMode2) &&
                    headers.Authorization.FirstOrDefault() is { } authHeader)
                    return authHeader.Split(' ').Last();
                if (headers.TryGetValue(headerName, out var headerValues) &&
                    headerValues.FirstOrDefault() is { } value)
                    return value;
            }

            return match.Value;
        });

    [GeneratedRegex(@"(\{(?<name>(?<key>[a-zA-Z0-9]+)(\:(?<value>[a-zA-Z0-9\-]+))?)\})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex VariablePattern();

    [GeneratedRegex(@"(?:\W|^)hash\((?<content>[^\)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HashFunctionPattern();
}

internal static class Helper {
    private static readonly string[] ReservedHeaders = ["Host", "Content-Length"];

    public static IResult Invalid(string key, string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { [key] = [message] });

    public static IPAddress? GetClientIp(this HttpContext http) {
        if (http.Request.Headers.TryGetValue("X-Forwarded-For", out var headers) &&
            headers.FirstOrDefault() is { } header && !string.IsNullOrWhiteSpace(header) &&
            IPAddress.TryParse(header, out var ip))
            return ip;

        return http.Connection.RemoteIpAddress;
    }

    private static (string Host, int Port, bool Secure) GetHostAndPort(
        this Dictionary<string, string> headers, string defaultHost, int defaultPort) {
        var wrap = new Dictionary<string, string>(headers, Const.CompareMode);
        if (!wrap.TryGetValue("host", out var hostValue)) return (defaultHost, defaultPort, false);

        wrap.TryGetValue("X-Forwarded-Proto", out var proto);
        var temp = hostValue.Split(':');
        var host = !string.IsNullOrWhiteSpace(temp[0]) ? temp[0] : defaultHost;
        var (port, secure) = (temp, proto?.ToLowerInvariant()) switch {
            ({ Length: > 1 }, _)when int.TryParse(temp[1], out var portValue) => (portValue, false),
            (_, "https") => (443, true),
            (_, "http") => (80, false),
            _ => (defaultPort, false)
        };
        return (host, port, secure);
    }

    extension(HttpRequest request) {
        public async ValueTask<string?> GetRawBody() {
            request.EnableBuffering();
            if (request.Body.CanSeek)
                request.Body.Seek(0, SeekOrigin.Begin);
            string? result;
            using (var reader = new StreamReader(request.Body, leaveOpen: true)) {
                var body = await reader.ReadToEndAsync();
                result = body;
            }

            if (request.Body.CanSeek)
                request.Body.Seek(0, SeekOrigin.Begin);
            return result;
        }
    }

    extension(IDbConnection db) {
        public async ValueTask<int> UpdateConfig(WebhookConfig config, CancellationToken token = default) {
            List<DbParameter> parameters = [
                DbParameter.Create("identifier", config.Identifier),
                DbParameter.Create("validate", config.Validate),
                config.Signature is not null
                    ? DbParameter.Create("signature", config.Signature, DbOpts.Default.SignatureConfig)
                    : DbParameter.Blank("signature"),
                config.Secret is not null
                    ? DbParameter.Create("secret", config.Secret, DbOpts.Default.SecretConfig)
                    : DbParameter.Blank("secret"),
                DbParameter.Create("modified_at", config.ModifiedAt ?? TimeProvider.System.GetUtcNow().UtcDateTime)
            ];
            return db switch {
                NpgsqlConnection postre => await postre.QueryAsync(
                    // language=PostgreSQL
                    """
                    UPDATE configs SET
                        signature = @signature,
                        secret = @secret,
                        validate = @validate,
                        modified_at = @modified_at
                    WHERE identifier = @identifier
                    RETURNING id;
                    """, DbOpts.Default.SavedId, parameters, cancellationToken: token).FirstOrDefaultAsync(token),
                SqliteConnection sqlite => await sqlite.QueryAsync(
                    // language=SQLite
                    """
                    UPDATE configs SET
                        signature = @signature,
                        secret = @secret,
                        validate = @validate,
                        modified_at = @modified_at
                    WHERE identifier = @identifier
                    RETURNING id;
                    """, DbOpts.Default.SavedId, parameters, cancellationToken: token).FirstOrDefaultAsync(token),
                _ => 0
            };
        }

        public async ValueTask<int> CreateConfig(WebhookConfig config, CancellationToken token = default) {
            List<DbParameter> parameters = [
                DbParameter.Create("identifier", config.Identifier),
                config.Signature is not null
                    ? DbParameter.Create("signature", config.Signature, DbOpts.Default.SignatureConfig)
                    : DbParameter.Blank("signature"),
                config.Secret is not null
                    ? DbParameter.Create("secret", config.Secret, DbOpts.Default.SecretConfig)
                    : DbParameter.Blank("secret"),
                DbParameter.Create("validate", config.Validate),
                DbParameter.Create("createdAt", config.CreatedAt)
            ];
            return db switch {
                NpgsqlConnection postre => await postre.QueryAsync(
                    // language=PostgreSQL
                    """
                    INSERT INTO configs(identifier, validate, signature, secret, created_at)
                    VALUES(@identifier, @validate, @signature, @secret, @createdAt)
                    RETURNING id;
                    """, DbOpts.Default.SavedId, parameters, cancellationToken: token).FirstOrDefaultAsync(token),
                SqliteConnection sqlite => await sqlite.QueryAsync(
                    // language=SQLite
                    """
                    INSERT INTO configs(identifier, validate, signature, secret, created_at)
                    VALUES(@identifier, @validate, @signature, @secret, @createdAt)
                    RETURNING id;
                    """, DbOpts.Default.SavedId, parameters, cancellationToken: token).FirstOrDefaultAsync(token),
                _ => 0
            };
        }

        public async ValueTask<WebhookConfig?> GetConfig(string identifier, CancellationToken token = default) {
            DbParameter[] parameters = [
                DbParameter.Create("identifier", identifier),
            ];
            return db switch {
                NpgsqlConnection postgre => await postgre
                    .QueryAsync(
                        // language=PostgreSQL
                        """
                        SELECT *
                        FROM configs
                        WHERE identifier = @identifier
                        LIMIT 1
                        """, DbOpts.Default.WebhookConfig, parameters, cancellationToken: token)
                    .FirstOrDefaultAsync(cancellationToken: token),
                SqliteConnection sqlite => await sqlite
                    .QueryAsync(
                        // language=SQLite
                        """
                        SELECT *
                        FROM configs
                        WHERE identifier = @identifier
                        LIMIT 1
                        """, DbOpts.Default.WebhookConfig, parameters, cancellationToken: token)
                    .FirstOrDefaultAsync(cancellationToken: token),
                _ => null
            };
        }

        public async ValueTask<WebhookRequest?> GetWebhook(
            string identifier, int logId, CancellationToken token = default) {
            DbParameter[] parameters = [
                DbParameter.Create("identifier", identifier),
                DbParameter.Create("id", logId)
            ];
            return db switch {
                NpgsqlConnection postgre => await postgre
                    .QueryAsync(
                        // language=PostgreSQL
                        """
                        SELECT *
                        FROM requests
                        WHERE identifier = @identifier AND id = @id
                        LIMIT 1
                        """, DbOpts.Default.WebhookRequest, parameters, cancellationToken: token)
                    .FirstOrDefaultAsync(cancellationToken: token),
                SqliteConnection sqlite => await sqlite
                    .QueryAsync(
                        // language=SQLite
                        """
                        SELECT *
                        FROM requests
                        WHERE identifier = @identifier AND id = @id
                        LIMIT 1
                        """, DbOpts.Default.WebhookRequest, parameters, cancellationToken: token)
                    .FirstOrDefaultAsync(cancellationToken: token),
                _ => null
            };
        }

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
                        INSERT INTO requests(client_ip, identifier, headers, body, response)
                        VALUES(@ip, @identifier, @headers, @body, @response)
                        """, parameters);
                    break;
            }
        }
    }

    extension(WebhookRequest request) {
        public WebhookLogDto ToDto() => new(
            request.Id,
            request.ReceivedAt,
            request.ClientIp,
            request.Headers,
            request.Body,
            request.Response
        );

        public string ToCurl() {
            var builder = new StringBuilder();
            var (host, port, secure) = request.Headers.GetHostAndPort("localhost", 80);
            var url = port switch {
                80 => $"http://{host}",
                _ when secure => $"https://{host}",
                _ => $"http://{host}:{port}"
            };
            builder.AppendLine($"curl -flis '{url}/webhook/{request.Identifier}' \\");
            builder.AppendLine($"   --data '{request.Body}' \\");
            foreach (var (key, value) in request.Headers) {
                if (ReservedHeaders.Contains(key, Const.CompareMode))
                    continue;

                if ("User-Agent".Equals(key, Const.CompareMode2))
                    builder.AppendLine($"    --user-agent '{value}' \\");
                else
                    builder.AppendLine($"    -H '{key}: {value}' \\");
            }

            builder.Length -= 2;
            return builder.ToString();
        }

        public string ToFetch() {
            var (host, port, secure) = request.Headers.GetHostAndPort("localhost", 80);
            var url = port switch {
                80 => $"http://{host}",
                _ when secure => $"https://{host}",
                _ => $"http://{host}:{port}"
            };
            var headerBuilder = new StringBuilder(request.Headers.Count * 10);
            var indent = new string(' ', 4);
            foreach (var (key, value) in request.Headers) {
                if (ReservedHeaders.Contains(key, Const.CompareMode))
                    continue;

                headerBuilder.Append($"\n{indent}{indent}{indent}'{key}': '{value}',");
            }

            headerBuilder.Length -= 1;
            var header = headerBuilder.Length > 0 ? $"headers: {{{headerBuilder}\n{indent}{indent}}}," : string.Empty;
            return
                // language=javascript
                $$"""
                  try {
                      const response = await fetch('{{url}}/webhook/{{request.Identifier}}', {
                          method: 'POST',
                          {{header}}
                          body: '{{request.Body}}'
                      });
                      if (!response.ok) {
                          throw new Error(`Response status: ${response.status}`);
                      }

                      console.log(`response code: ${response.status}`);
                  } catch (error) {
                      console.error('[ERROR]', error.message);
                  }
                  """;
        }

        public string ToNetcat() {
            var builder = new StringBuilder();
            var (host, port, secure) = request.Headers.GetHostAndPort("localhost", 80);
            foreach (var (key, value) in request.Headers) {
                if (ReservedHeaders.Contains(key, Const.CompareMode))
                    continue;

                builder.AppendLine($"  printf '{key}: {value}\\r\\n'");
            }

            var command = secure
                ? $"""openssl s_client -connect "$SERVER":{port} -quiet 2>/dev/null"""
                : $"""nc "$SERVER" {port}""";
            return
                // language=sh
                $$"""
                  # Configuration
                  SERVER="{{host}}"
                  REQUEST_PATH="/webhook/{{request.Identifier}}"
                  PAYLOAD='{{request.Body}}'

                  # 1. Calculate payload byte length safely using POSIX wc
                  # We strip trailing newlines and spaces using standard printf
                  PAYLOAD_LEN=$(printf '%s' "$PAYLOAD" | wc -c)

                  # 2. Build the HTTP headers and stream the body payload
                  (
                    printf 'POST %s HTTP/1.1\r\n' "$REQUEST_PATH"
                    printf 'Host: %s\r\n' "$SERVER"
                    printf 'Content-Length: %s\r\n' "$PAYLOAD_LEN"
                  {{builder}}
                    printf 'Connection: close\r\n\r\n'
                    printf '%s' "$PAYLOAD"
                  ) | {{command}}
                  """;
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
    public required string Body { get; set; }
    public WebhookResponse Response { get; set; } = null!;
}

internal sealed record WebhookLogDto(
    int Id,
    DateTime ReceivedAt,
    [property: JsonConverter(typeof(IpAddressJsonConverter))]
    IPAddress ClientIp,
    Dictionary<string, string> Headers,
    [property: JsonConverter(typeof(RawJsonConverter))]
    string Body,
    WebhookResponse Response
);

internal sealed class WebhookResponse {
    public required int Code { get; set; }
    public string? Text { get; set; }

    public static WebhookResponse BadRequest(string reason) =>
        new() { Code = (int)HttpStatusCode.BadRequest, Text = reason };

    public static WebhookResponse Reject(string reason) =>
        new() { Code = (int)HttpStatusCode.NotAcceptable, Text = reason };

    public static WebhookResponse NotFound(string reason) =>
        new() { Code = (int)HttpStatusCode.NotFound, Text = reason };

    public IResult ToHttpResult() => (HttpStatusCode)Code switch {
        HttpStatusCode.NotFound => Results.NotFound(Text),
        HttpStatusCode.BadRequest => Results.BadRequest(Text),
        HttpStatusCode.OK => Results.Ok(),
        var code when !string.IsNullOrEmpty(Text) => Results.Text(Text, statusCode: (int)code),
        var code => Results.StatusCode((int)code)
    };
}

internal sealed record SavedId(int Id) {
    public static implicit operator int(SavedId? id) => id?.Id ?? 0;
}

internal sealed class WebhookConfig {
    public int Id { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public bool Validate { get; set; }
    public SignatureConfig? Signature { get; set; }
    public SecretConfig? Secret { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

internal sealed class SignatureConfig {
    /// <summary>
    /// HMAC based algorithm to calculate the hash.
    /// </summary>
    [EnumDataType(typeof(SignatureAlgorithm), ErrorMessage = "Unsupported algorithm")]
    public SignatureAlgorithm Algorithm { get; set; }

    /// <summary>
    /// Final string encoding after hash calculated.
    /// </summary>
    [EnumDataType(typeof(SignatureEncoding), ErrorMessage = "Unsupported encoding")]
    public SignatureEncoding Encoding { get; set; }

    /// <summary>
    /// Header name to get/put within HTTP request, f.e. X-Hub-Signature-256
    /// </summary>
    [Required]
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// Template to build signature to compute the hash.
    /// It accepts literal string or variable under curly braces.
    /// Use <c>hash()</c> to build hashed text, otherwise, it will be treated as plain text..
    /// Applicable variables:
    /// <list type="number">
    ///     <item><c>{algorithm}</c>, lowercase algorithm without 'HMAC', example: <c>sha256</c></item>
    ///     <item><c>{body}</c>, Plain text HTTP request body</item>
    ///     <item><c>{header:Header-Name}</c>, HTTP request header value</item>
    /// </list>
    /// <example>
    /// <c>{algorithm}=hash({body})</c> for standard WebSub.
    /// <c>hash({header:timestamp}{body})</c> combine timestamp header and body.
    /// </example>
    /// </summary>
    [Required]
    public string Template { get; set; } = string.Empty;

    /// <summary>
    /// Standard WebSub signature.
    /// </summary>
    public static SignatureConfig WebSub => new() {
        Algorithm = SignatureAlgorithm.Sha256,
        Encoding = SignatureEncoding.Hex,
        Header = "X-Hub-Signature",
        Template = "{algorithm}=hash({body})"
    };
}

internal sealed class SecretConfig {
    [EnumDataType(typeof(SecretEncoding), ErrorMessage = "Unsupported encoding")]
    public SecretEncoding Encoding { get; set; }

    [Required]
    public string Value { get; set; } = string.Empty;
}

internal sealed record CreateConfigDto(
    [property: Required, StringLength(25, MinimumLength = 2),
               RegularExpression("^[0-9a-zA-Z_]+$",
                   ErrorMessage = "Only allows alphanumeric characters and underscores")]
    string Identifier,
    bool Validate,
    SignatureConfig? Signature,
    [property: Required] SecretConfig Secret
);

internal sealed class SaveConfigDto {
    public bool Validate { get; set; }
    public SignatureConfig? Signature { get; set; }
    public SecretConfig? Secret { get; set; }
};

internal sealed record GetWebhookConfigDto(
    string Identifier,
    bool Validate,
    SignatureConfig? Signature,
    DateTime? LastModifiedAt) {
    public GetWebhookConfigDto(WebhookConfig record) :
        this(record.Identifier, record.Validate, record.Signature, record.ModifiedAt ?? record.CreatedAt) {
    }
}

/// <summary>
/// Calculated signature hash.
/// <example><c>sha256=a4771c39fbe90f317c7824e83ddef3caae9cb3d976c214ace1f2937e133263c9</c></example>
/// </summary>
/// <param name="Algorithm">Algorithm being used if any</param>
/// <param name="Value">Actual hash</param>
internal sealed record SignatureHash(SignatureAlgorithm? Algorithm, string Value) {
    public static SignatureHash Parse(string raw) {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        var temp = raw.Split('=', 2);
        SignatureAlgorithm? algorithm =
            temp.Length > 1 && SignatureAlgorithm.TryParse(temp[0], out var algo, true) ? algo : null;
        return new(algorithm, temp.Length > 1 ? temp[1] : raw);
    }
}

[EnumExtensions]
enum SignatureAlgorithm { Sha1, Sha256, Sha384, Sha512 }

[EnumExtensions]
enum SignatureEncoding { Hex, Base64 }

[EnumExtensions]
enum SecretEncoding { Plain, Base64 }

[JsonSerializable(typeof(SavedId))]
[JsonSerializable(typeof(WebhookConfig))]
[JsonSerializable(typeof(WebhookRequest))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
internal partial class DbOpts : JsonSerializerContext;

[JsonSerializable(typeof(CreateConfigDto))]
[JsonSerializable(typeof(SaveConfigDto))]
[JsonSerializable(typeof(GetWebhookConfigDto))]
[JsonSerializable(typeof(WebhookLogDto))]
[JsonSerializable(typeof(IAsyncEnumerable<WebhookLogDto>))]
[JsonSerializable(typeof(JsonDocument))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
internal partial class WebOpts : JsonSerializerContext;

internal static partial class LoggerExtensions {
    [LoggerMessage(
        LogLevel.Information,
        "Webhook received for {Identifier} with body: {Body}")]
    public static partial void WebhookReceived(this ILogger logger, string identifier, string? body);

    [LoggerMessage(LogLevel.Information, "No signature header found for identifier {Identifier}")]
    public static partial void SignatureHeaderNotFound(this ILogger logger, string identifier);

    [LoggerMessage(LogLevel.Information,
        "Signature missmatch for {Identifier}, expecting {Expected} but got {Actual}")]
    public static partial void SignatureMissmatch(this ILogger logger, string identifier, string expected,
        string actual);

    [LoggerMessage(LogLevel.Warning, "No payload found for identifier {Identifier}")]
    public static partial void NoPayload(this ILogger logger, string identifier);
}
