#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true
#:property EnableConfigurationBindingGenerator=true
#:property JsonSerializerIsReflectionEnabledByDefault=false
#:property NoWarn=NU1510,CS2002
#:property StripSymbols=true

#:include web/WebApp.cs

#:package Microsoft.AspNetCore.Authentication.OpenIdConnect@10.0.11
#:package Microsoft.IdentityModel.Protocols.OpenIdConnect@8.22.0

using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dotfiles.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Logging;

var builder = WebApp.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonOpt.Default);
});
builder.Services.AddOptionsWithValidateOnStart<OidcConfig, OidcConfig.Validator>().BindConfiguration("oidc");
builder.Services.AddSingleton<ConfigurationManager<OpenIdConnectConfiguration>>(provider => {
    var config = provider.GetRequiredService<IOptions<OidcConfig>>().Value;
    return new ConfigurationManager<OpenIdConnectConfiguration>(
        config.AuthorityUrl.TrimEnd('/') + "/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever { RequireHttps = true });
});
if (builder.Environment.IsDevelopment())
    IdentityModelEventSource.ShowPII = true;
else
    builder.Services.AddProblemDetails();
builder.Services.AddHttpClient();
builder.Services
    .AddAuthorization()
    .AddAuthentication(options => {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options => {
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
    });
builder.Services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
    .Configure<IOptions<OidcConfig>>((forOidc, fromApp) => {
        var app = fromApp.Value;
        forOidc.ClientId = app.ClientId;
        forOidc.ClientSecret = app.ClientSecret;
        forOidc.Authority = app.AuthorityUrl.TrimEnd('/');
        forOidc.Scope.Clear();
        Array.ForEach(app.Scopes, scope => forOidc.Scope.Add(scope));
    });
builder.Services.AddAuthorization();
var app = builder.Build();
app.MapGet("/", () => Results.LocalRedirect("/login", true));
app.MapGet("/profile", (HttpContext ctx) => Results.Ok(ctx.User.Claims.Select(c => new { c.Type, c.Value })))
    .RequireAuthorization();
app.MapGet("/login", () => Results.Text(Helper.LoginPage, "text/html"));
app.MapPost("/login/authorization_code", () => Results.Challenge(
    new AuthenticationProperties { RedirectUri = "/profile" },
    [OpenIdConnectDefaults.AuthenticationScheme]));
app.MapPost("/login/password", async (
    HttpContext ctx, Credential credential,
    [FromServices] IOptions<OidcConfig> config, [FromServices] IHttpClientFactory httpFactory,
    [FromServices] ConfigurationManager<OpenIdConnectConfiguration> configManager) => {
    if (!credential.IsValid())
        return Results.BadRequest("Username and password are required");

    var oidcConfig = await configManager.GetConfigurationAsync();
    var wrapped = new WrappedConfig(config.Value, oidcConfig);
    var httpClient = httpFactory.CreateClient();
    var tokenResponse = await wrapped.GetTokenAsync(httpClient, "password", credential);
    var principal = await wrapped.ValidateAndSignInAsync(tokenResponse, ctx, "pwd");
    return Results.Ok(principal);
});
app.MapPost("/login/client_credentials", async (
    HttpContext ctx, IHttpClientFactory httpFactory,
    [FromServices] IOptions<OidcConfig> config,
    [FromServices] ConfigurationManager<OpenIdConnectConfiguration> configManager) => {
    var oidcConfig = await configManager.GetConfigurationAsync();
    var wrapped = new WrappedConfig(config.Value, oidcConfig);
    var http = httpFactory.CreateClient();
    var tokenResponse = await wrapped.GetTokenAsync(http, "client_credentials");
    var principal = await wrapped.ValidateAndSignInAsync(tokenResponse, ctx, "client_credentials");
    return Results.Ok(principal);
});
app.MapGet("/logout", () => Results.SignOut(
    new AuthenticationProperties { RedirectUri = "/" },
    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
app.MapGet("/tokens",
    async ctx => Results.Ok(new {
        access_token = await ctx.GetTokenAsync("access_token"),
        id_token = await ctx.GetTokenAsync("id_token"),
        refresh_token = await ctx.GetTokenAsync("refresh_token"),
    })).RequireAuthorization();
await app.RunAsync();
return 0;

file static class Helper {
    public static bool IsValid(this Credential credential) =>
        !string.IsNullOrEmpty(credential.Username) &&
        !string.IsNullOrEmpty(credential.Password);

    extension(WrappedConfig config) {
        public async Task<TokenResponse> GetTokenAsync(
            HttpClient http, string grantType, Credential? credential = null) {
            var compareMode = StringComparer.InvariantCultureIgnoreCase;
            using var request = new HttpRequestMessage(HttpMethod.Post, config.Idp.TokenEndpoint);
            if (config.Idp.TokenEndpointAuthMethodsSupported.Contains("client_secret_post", compareMode))
                BuildClientSettingsOnBody();
            else if (config.Idp.TokenEndpointAuthMethodsSupported.Contains("client_secret_basic", compareMode))
                BuildClientSettingsOnHeader();
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Token request failed ({(int)response.StatusCode}): {body}");

            return JsonSerializer.Deserialize<TokenResponse>(body, JsonOpt.Default.TokenResponse)
                   ?? throw new InvalidOperationException($"Token request failed: {body}");

            static Dictionary<string, string> BuildBasicPayload(string grantType, string[] scopes,
                Credential? credential) {
                var form = new Dictionary<string, string> { ["grant_type"] = grantType };
                if (credential?.IsValid() ?? false) {
                    form["username"] = credential.Username!;
                    form["password"] = credential.Password!;
                }

                if (scopes is { Length: > 0 })
                    form["scope"] = string.Join(' ', scopes);
                return form;
            }

            void BuildClientSettingsOnBody() {
                var form = BuildBasicPayload(grantType, config.App.Scopes, credential);
                form["client_id"] = config.App.ClientId;
                form["client_secret"] = config.App.ClientSecret;
                request.Content = new FormUrlEncodedContent(form);
            }

            void BuildClientSettingsOnHeader() {
                var encodedId = Uri.EscapeDataString(config.App.ClientId);
                var encodedSecret = Uri.EscapeDataString(config.App.ClientSecret);
                var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{encodedId}:{encodedSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
                var form = BuildBasicPayload(grantType, config.App.Scopes, credential);
                request.Content = new FormUrlEncodedContent(form);
            }
        }

        public async Task<ClaimsPrincipal> ValidateAndSignInAsync(TokenResponse token, HttpContext ctx,
            string authMethod) {
            var handler = new JsonWebTokenHandler();
            var tokenForClaims = token switch {
                { IdToken: var idToken } when !string.IsNullOrEmpty(idToken) => idToken,
                { AccessToken: var accessToken } when handler.CanReadToken(accessToken) => accessToken,
                _ => null
            };
            if (string.IsNullOrEmpty(tokenForClaims)) throw new ArgumentException("No valid token found");

            var validationParameters = new TokenValidationParameters {
                ValidIssuer = config.Idp.Issuer,
                IssuerSigningKeys = config.Idp.SigningKeys,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                // Access tokens are often audience-restricted to the API you intend to call.
                // Set this once you know the audience your IdP issues (commonly the API's
                // resource identifier). Left off validation here since it varies by IdP.
                ValidateAudience = false,
            };
            var result = await handler.ValidateTokenAsync(tokenForClaims, validationParameters);
            if (!result.IsValid)
                throw new SecurityTokenException("Access token failed validation.", result.Exception);

            var identity = result.ClaimsIdentity;
            identity.AddClaim(new Claim("amr", authMethod));
            var principal = new ClaimsPrincipal(identity);
            var authProperties = new AuthenticationProperties();
            var tokens = new List<AuthenticationToken> {
                new() { Name = "access_token", Value = token.AccessToken },
                new() { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn).ToString("o") },
            };
            if (!string.IsNullOrEmpty(token.IdToken)) tokens.Add(new() { Name = "id_token", Value = token.IdToken });
            if (!string.IsNullOrEmpty(token.RefreshToken))
                tokens.Add(new() { Name = "refresh_token", Value = token.RefreshToken });
            authProperties.StoreTokens(tokens);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
            return new ClaimsPrincipal(identity);
        }
    }

    public const string LoginPage =
        // language=html
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="UTF-8">
        <title>Sign in</title>
        <link href="https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.3.3/css/bootstrap.min.css" rel="stylesheet">
        <script>
          function applyGrantType() {
            const grantType = document.getElementById('grantType').value;
            const form = document.getElementById('grant-form');
            document.getElementById('creds').style.display = grantType !== 'password' ? 'none' : '';
            form.action = `/login/${grantType}`;
          }
        </script>
        </head>
        <body class="d-flex align-items-center py-4 bg-body-tertiary" style="height:100vh">
        <main class="form-signin w-100 m-auto" style="max-width:330px">
          <form method="post" id="grant-form" action="/login">
            <h1 class="h3 mb-3 fw-normal">Please sign in</h1>

            <div class="form-floating mb-3">
              <select class="form-select" id="grantType" name="grant_type" onchange="applyGrantType()">
                <option value="client_credentials">Client Credentials</option>
                <option value="password" selected>Password</option>
                <option value="authorization_code">Authorization Code</option>
              </select>
              <label for="grantType">Grant type</label>
            </div>

            <div id="creds">
              <div class="form-floating">
                <input type="text" class="form-control" id="username" name="username" placeholder="Username">
                <label for="username">Username</label>
              </div>
              <div class="form-floating">
                <input type="password" class="form-control" id="password" name="password" placeholder="Password">
                <label for="password">Password</label>
              </div>
            </div>

            <button class="btn btn-primary w-100 py-2 mt-3" type="submit">Sign in</button>
          </form>
        </main>
        </body>
        </html>
        """;
}

internal sealed partial class OidcConfig {
    [Required]
    public required string AuthorityUrl { get; set; }

    [Required]
    public required string Audience { get; set; }

    [Required]
    public required string ClientId { get; set; }

    [Required]
    public required string ClientSecret { get; set; }

    public string[] Scopes { get; set; } = [];

    [OptionsValidator]
    public sealed partial class Validator : IValidateOptions<OidcConfig>;
}

internal sealed record WrappedConfig(OidcConfig App, OpenIdConnectConfiguration Idp);

internal sealed class Credential : IBindableFromHttpContext<Credential> {
    public string? Username { get; set; }
    public string? Password { get; set; }

    public static async ValueTask<Credential?> BindAsync(HttpContext context, ParameterInfo parameter) {
        var form = await context.Request.ReadFormAsync();
        return new Credential { Username = form["username"], Password = form["password"], };
    }
}

internal sealed record TokenResponse(
    string TokenType,
    string Scope,
    int ExpiresIn,
    string AccessToken,
    string? RefreshToken,
    string IdToken
);

[JsonSerializable(typeof(TokenResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal sealed partial class JsonOpt : JsonSerializerContext;
