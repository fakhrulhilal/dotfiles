# OidcClient

A small web app for trying an OpenID Connect provider. It signs you in with the flow you pick, keeps the session in a
cookie, and shows the claims and the raw tokens it received. Use it to check that a client registration works or to grab
a token for another tool. Source: [`csharp/OidcClient.cs`](../csharp/OidcClient.cs).

It has no release binary. Run it from inside `csharp/`:

```shell
export oidc__AuthorityUrl=https://login.example.com/realms/dev
export oidc__Audience=my-api
export oidc__ClientId=my-client
export oidc__ClientSecret=<secret>
export oidc__Scopes__0=openid
export oidc__Scopes__1=profile
export ASPNETCORE_URLS=http://localhost:8080
dotnet run OidcClient.cs
```

Then open `http://localhost:8080`.

## Configuration

Settings come from environment variables, or from `--oidc:Name=value` arguments. All but the scopes are required, and
the app refuses to start without them.

| Setting                | Description                                                                 |
|------------------------|-----------------------------------------------------------------------------|
| `oidc__AuthorityUrl`   | The provider's issuer URL. It must use `https`                              |
| `oidc__ClientId`       | Client ID of the registration                                               |
| `oidc__ClientSecret`   | Client secret of the registration                                           |
| `oidc__Audience`       | The API audience. It is required, but tokens are not checked against it yet |
| `oidc__Scopes__0`, ... | Scopes to request, one variable per scope, counting from `0`                |
| `ASPNETCORE_URLS`      | Address to listen on. Default port is 5000                                  |

Register `<base URL>/signin-oidc` as a redirect URI, and `<base URL>/signout-callback-oidc` as a post logout redirect
URI, when the provider requires them. They are used by the authorization code flow. In the Development environment
(`ASPNETCORE_ENVIRONMENT=Development`) the logs also show personal data from the identity libraries.

## Signing in

`/` redirects to `/login`, which asks for a grant type:

| Grant type           | What happens                                                                          |
|----------------------|---------------------------------------------------------------------------------------|
| `authorization_code` | Redirects to the provider, uses PKCE, and returns to the app after you sign in there  |
| `password`           | Asks for a username and password, and exchanges them for tokens at the token endpoint |
| `client_credentials` | Exchanges the client ID and secret for tokens, with no user involved                  |

The two direct grants send the client credentials the way the provider advertises: in the request body when it supports
`client_secret_post`, otherwise in a basic authorization header. The returned ID token, or the access token when there
is no ID token, must be signed by the provider and not expired. Its claims become the signed-in user, marked with an
`amr` claim of `pwd` or `client_credentials`.

## Pages

| Path                             | Needs sign in | Description                                                    |
|----------------------------------|---------------|----------------------------------------------------------------|
| `/login`                         | no            | The login form                                                 |
| `/profile`                       | yes           | The claims of the signed-in user as JSON                       |
| `/tokens`                        | yes           | `access_token`, `id_token` and `refresh_token` as JSON         |
| `/logout`                        | no            | Ends the session and the provider session, then returns to `/` |
| `POST /login/authorization_code` | no            | Starts the authorization code flow, then lands on `/profile`   |
| `POST /login/password`           | no            | Form fields `username` and `password`                          |
| `POST /login/client_credentials` | no            | Signs in as the client                                         |

A page that needs a sign in redirects to the provider when there is no session.

## Errors

A failed token request stops with the provider's status code and response body in the message, which usually names the
problem, for example an unauthorized grant type. Sign in problems on the authorization code flow are reported by the
provider on its own pages. An invalid configuration is reported at startup and the app does not run.