# WebhookCli (`dothook`)

A webhook receiver for development. It accepts a request on `POST /webhook/{identifier}`, stores the headers, the raw
body and the response it gave in a database, and can check an HMAC signature before accepting it. The stored requests
can be read back as JSON or as a `curl`, `fetch` or `netcat` command to replay them. Source:
[`csharp/WebhookCli.cs`](../csharp/WebhookCli.cs). 

It is a web server, so it is configured through environment variables (or `--Section:Key=value` arguments) rather than
command options.

## Installation

1. Through mise: see [Home](Home.md) for detail
2. Using container: [`iroel/webhook`](https://hub.docker.com/r/iroel/webhook) (x64 and MacOS arm64)
3. Build, run, and expose through Ngrok: see [csharp/docker-compose.yaml](../csharp/docker-compose.yaml)

## Configuration

| Setting                      | Description                                                             |
|------------------------------|-------------------------------------------------------------------------|
| `ASPNETCORE_URLS`            | Address to listen on, for example `http://+:8080`. Default port is 5000 |
| `DB__Type`                   | `Sqlite` or `Postgre`. Required                                         |
| `DB__ConnectionString`       | Connection string of that database. Required                            |
| `App__AllowedOrigins`        | Comma separated CORS origins. Defaults to `*`                           |
| `App__IconUrl`               | Redirect `/favicon.ico` to this URL                                     |
| `App__IconPath`              | Serve `/favicon.ico` from this file when no `IconUrl` is set            |
| `Logging__LogLevel__Default` | ASP.NET Core log level. Logs are JSON lines unless in Development       |

The database schema is created and migrated at startup by a background service, so an empty SQLite file or an empty
PostgreSQL database is enough.

```shell
# SQLite
export DB__Type=Sqlite
export DB__ConnectionString='Data Source=webhook.db'

# PostgreSQL (a standard Npgsql connection string)
export DB__Type=Postgre
export DB__ConnectionString='Host=localhost;Port=5432;Database=webhook;Username=app;Password=secret'

export ASPNETCORE_URLS=http://localhost:8080
dothook
```

Behind a reverse proxy or a tunnel such as ngrok or Tailscale Serve, the client address is taken from the
`X-Forwarded-For` and `X-Forwarded-Proto` headers.

## Endpoints

| Method and path                           | Purpose                                                           |
|-------------------------------------------|-------------------------------------------------------------------|
| `POST /config`                            | Register an identifier                                            |
| `GET /config/{identifier}`                | Read a registration (the secret is never returned)                |
| `PATCH /config/{identifier}`              | Change a registration with a JSON merge patch                     |
| `POST /webhook/{identifier}`              | Receive a webhook                                                 |
| `GET /webhook/{identifier}`               | List the stored requests of that identifier as JSON               |
| `GET /webhook/{identifier}/{id}/{format}` | Show one request as `json` (default), `curl`, `fetch` or `netcat` |
| `GET /_health`                            | The process is up                                                 |
| `GET /_health/ready`                      | The database is reachable                                         |

An identifier can be used without registering it. Every request is stored, and it is answered with `200` because there
is nothing to validate.

This repo uses [Bruno](https://www.usebruno.com) (postman alternative), and available at [endpoints](../endpoints) folder. 

### Webhook Config (Identifier)

#### Creating a new webhook receiver (identifier)

Implementation: `CreateWebhookConfig` method.

Sample for creating Github webhook
```http request
POST http://localhost:8080/config
Content-Type: application/json

{
  "identifier": "github",
  "validate": true,
  "signature": {
    "encoding": "hex",
    "algorithm": "sha256",
    "header": "X-Hub-Signature-256",
    "template": "sha256=hash({body})"
  },
  "secret": {
    "encoding": "plain",
    "value": "my-webhook-secret"
  }
}
```

Payloads:
- `identifier` is 2 to 25 letters, digits or underscores and must not exist yet.
- `secret.value` is required when `validate` is `true`. `secret.encoding` is `Plain` (raw text) or `Base64`.
- `signature` is optional. Without it the standard [WebSub](https://www.w3.org/TR/websub/#signing-content) scheme is used, see below.

The receiver reads the signature from the configured header, computes the HMAC of the request with the secret and
compares the two. [HashGenerator](HashGenerator.md) creates such a signature for a manual test.

```shell
body='{"event":"ping"}'
signature=$(dotnet run HashGenerator.cs -- sha256 my-secret "$body")
curl -X POST http://localhost:8080/webhook/demo -H "X-Hub-Signature: sha256=$signature" -d "$body"
```

**Signature settings**

| Field       | Values                                        | Default (WebSub)           |
|-------------|-----------------------------------------------|----------------------------|
| `algorithm` | `Sha1`, `Sha256`, `Sha384`, `Sha512`          | `Sha256`                   |
| `encoding`  | `Hex`, `Base64`                               | `Hex`                      |
| `header`    | Name of the request header with the signature | `X-Hub-Signature`          |
| `template`  | What to sign, see below                       | `{algorithm}=hash({body})` |

When the received header looks like `sha256=<hash>`, the algorithm in front of the equals sign wins over `algorithm`.

`template` describes the exact text the sender signed. Wrap the part that is hashed in `hash( )` and the rest is used as
is. These variables are available:

| Variable        | Value                                                           |
|-----------------|-----------------------------------------------------------------|
| `{algorithm}`   | The algorithm in lowercase without `HMAC`, for example `sha256` |
| `{body}`        | The raw request body                                            |
| `{header:Name}` | The value of the request header `Name`                          |

For example, a sender that signs the timestamp header followed by the body and puts the result in `X-Signature`:

```shell
curl -X PATCH http://localhost:8080/config/demo -H 'Content-Type: application/json' -d '{
  "signature": {
    "algorithm": "Sha256",
    "encoding": "Base64",
    "header": "X-Signature",
    "template": "hash({header:X-Timestamp}{body})"
  }
}'
```

Then our webhook URL is available at `http://localhost:8080/webhook/github` (replace base URL accordingly).
There are samples for creating webhook for various websites under [endpoints](../endpoints) folder.

#### Updating the existing webhook receiver (identifier)

Implementation: `SaveWebhookConfig` method

This uses [JSON Merge Patch](https://www.rfc-editor.org/info/rfc7386/), do not confuse with [JSON Patch](https://www.rfc-editor.org/info/rfc6902).
Sample - update signature algorithm:
```http request
PATCH http://localhost:8080/config/github
Content-Type: application/json

{
  "signature": {
    "algorithm": "sha",
    "header": "X-Hub-Signature",
    "template": "sha=hash({body})"
  }
}
```

Sample - disable signature validation:
```http request
PATCH http://localhost:8080/config/github
Content-Type: application/json

{
  "validate": false,
  "signature": null,
  "secret": null
}
```

#### Get an existing webhook receiver (identifier)

Implementation: `GetWebhookConfig` method

Sample:
```http request
GET http://localhost:8080/config/github
```

### Webhook Log Request

#### Receiving Webhook Request

Implementation: `ReceiveWebhook` method

This is the URL that we share to the public. A quick start with [docker compose](../csharp/docker-compose.yaml) file + Ngrok 
can be used to start with.

Sample
`POST http://localhost:8080/webhook/github` stores every request it gets, including the ones it rejects:

| Status | Meaning                                                                               |
|--------|---------------------------------------------------------------------------------------|
| `200`  | Accepted. The identifier is not registered, or its validation is off                  |
| `202`  | Accepted. The signature is valid                                                      |
| `400`  | The body is empty, the signature header is missing, or the registration has no secret |
| `406`  | The signature does not match                                                          |


#### Reading Stored Webhook Requests

Implementation: `GetWebhookLog` method

Sample:
`GET http://localhost:8080/webhook/github` returns a JSON array, order by last received, with the following properties:
- `id` (integer): DB generated IDE
- `receivedAt` (timestamp): When the webhook was received
- `headers` (key-value pairs): The headers sent by the webhook sender
- `body` (object): The body sent by the webhook sender
- `response` (object): The status code and text that were returned to the sender

This is hard limited to the latest 100 requests (see `Const.PageSize`).

#### Getting Individual Webhook Request

Implementation: `FormatWebhookLog` method

Sample:
`GET http://localhost:8080/webhook/github/124` 

It produces JSON as the default format. However, it supports certain format so you don't have to parse JSON to be able 
to replay the request:
1. netcat: `curl -sL http://localhost:8080/webhook/github/124/netcat | sh`
2. curl: `eval "$(curl -sL http://localhost:8080/webhook/github/124/curl)"`
3. JS fetch: `bun -e "$(curl http://localhost:8080/webhook/github/124/fetch)"`

### Health checks

- `GET http://localhost:8080/_health` for liveness probe
- `GET http://localhost:8080/_health/ready` for readiness probe (check dependencies, i.e. database)

## Errors

Any failure (invalid input, unknown error) during receiving an HTTP request will follow the [problem details](https://datatracker.ietf.org/doc/html/rfc7807) specification. 
Any app failure at startup is written to standard error and the process exits with `1`.