# HashGenerator

Prints the HMAC of a message for a given secret. It is handy for producing the signature a webhook sender would attach,
so you can test a receiver such as [WebhookCli](WebhookCli.md) by hand. Source:
[`csharp/HashGenerator.cs`](../csharp/HashGenerator.cs).

It has no release binary. Run it from inside `csharp/`:

```shell
dotnet run HashGenerator.cs -- <algorithm> <secret> <message> [encoding]
```

| Argument    | Description                            |
|-------------|----------------------------------------|
| `algorithm` | One of the algorithms below            |
| `secret`    | The HMAC key. Must not be empty        |
| `message`   | The text to sign. Must not be empty    |
| `encoding`  | `hex` (default, lowercase) or `base64` |

Every algorithm is an HMAC, so `sha256` means HMAC-SHA256, not a plain SHA-256 hash. The secret and the message are read
as UTF-8.

| Algorithm                                   | Notes                                                          |
|---------------------------------------------|----------------------------------------------------------------|
| `sha1`, `hmacsha1`                          |                                                                |
| `sha256`, `hmacsha256`                      |                                                                |
| `sha384`, `hmacsha384`                      |                                                                |
| `sha512`, `hmacsha512`                      |                                                                |
| `hmac3sha256`, `hmac3sha384`, `hmac3sha512` | HMAC over SHA-3. Needs an operating system that provides SHA-3 |

Names are case-insensitive.

```shell
dotnet run HashGenerator.cs -- sha256 my-secret '{"event":"ping"}'
dotnet run HashGenerator.cs -- sha512 my-secret 'hello' base64
```

## Sign a webhook request

[WebhookCli](WebhookCli.md) expects `X-Hub-Signature: sha256=<hex>` by default:

```shell
body='{"event":"ping"}'
signature=$(dotnet run HashGenerator.cs -- sha256 my-secret "$body")
curl -X POST http://localhost:8080/webhook/demo \
    -H 'Content-Type: application/json' \
    -H "X-Hub-Signature: sha256=$signature" \
    -d "$body"
```

## Errors

A missing argument, an empty secret or an empty message prints a message to standard error and exits with `1`. An
unsupported algorithm or encoding stops with an exception message.