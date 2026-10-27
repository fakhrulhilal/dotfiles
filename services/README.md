# Lab services

One Docker Compose project (`lab`) for local infrastructure: databases, storage, cache, mail, message brokers,
workflow tools, observability and identity providers. Every service is opt-in through a profile, so only what you
ask for is started.

## Quick start

```shell
cd services
cp env.txt .env                          # then edit the values, .env is git-ignored
printf '%s' '<APP_PASSWORD>' > secrets/app_password.txt   # same value as APP_PASSWORD in .env
docker compose --profile db up -d        # start one category
docker compose --profile db --profile cache up -d
docker compose --profile kafka up -d     # or only a sub-set of a category
docker compose ps                        # wait until every service reports (healthy)
docker compose --profile db down         # stop; add -v to also drop the volumes (and their data)
docker compose -f $DOT_HOME/services/docker-compose.yaml --profile '*' up -d # run all services
```

The stack is built for [OrbStack](https://orbstack.dev): every service gets a domain such as
`https://postgres.db.dev.local` (`dev.orbstack.domains` label, `DOMAIN` from `.env`). Services that publish a port are
also reachable on `localhost`.

### Trusting the lab certificates

`tool-cert` creates a root CA, an intermediate CA and one certificate per category (`db.${DOMAIN}` plus
`*.db.${DOMAIN}`, and so on) into the `lab_certificate` volume. Files are only created when they are missing. Copy the
CAs out once and trust them on the host:

```shell
docker run --rm -v lab_certificate:/certs:ro alpine cat /certs/root-CA.crt > root-CA.crt
docker run --rm -v lab_certificate:/certs:ro alpine cat /certs/server-CA.crt > server-CA.crt
```

Each certificate comes as `.crt`, `.key`, `.pem` (with the issuer chain) and `.pfx` (protected by `CERT_PASSWORD`).
`ca-bundle.crt` is the system bundle plus the lab root CA, for containers that need `SSL_CERT_FILE`.

## Services

| Profile              | Services                          | Domains (`.${DOMAIN}`)              | Host ports        |
|----------------------|-----------------------------------|-------------------------------------|-------------------|
| `db`, `sql`          | MSSQL, Postgres                   | `mssql.db`, `postgres.db`           | 1433, 5432        |
| `db`, `nosql`        | Mongo, Cosmos emulator            | `mongo.db`, `cosmos.db`             | 27017, 8083, 1234 |
| `storage`, `blob`    | Azurite (blob)                    | `azurite.storage`                   | 10000             |
| `storage`, `s3`      | RustFS                            | `s3.storage`, `rustfs.storage`      | 9000, 9001        |
| `cache`              | Redis, Valkey, Valkey Admin       | `redis.cache`, `console.cache`, ... | -                 |
| `mail`               | MailCrab, smtp4dev                | `mail`, `smtp4dev.mail`             | -                 |
| `pkg`                | BaGetter (NuGet)                  | `nuget.pkg`                         | -                 |
| `broker`, `nats`     | NATS                              | `nats.broker`                       | 4222, 8222        |
| `broker`, `kafka`    | Zookeeper, Kafka, Schema Registry | `kafka.broker`, ...                 | 2181, 9092        |
| `broker`, `redpanda` | Redpanda, Redpanda Console        | `redpanda.broker`, ...              | 8081, 8082, 9644  |
| `workflow`           | n8n, NocoDB                       | `n8n.workflow`, `noco.workflow`     | -                 |
| `otlp`               | OpenTelemetry Collector           | `otlp`, `collector.otlp`            | 4317, 4318        |
| `otlp`               | Seq, Aspire, Grafana, Rootprint   | `seq.otlp`, `aspire.otlp`, ...      | 8041, 5341        |
| `otlp`, `elastic`    | Elasticsearch, Kibana             | `kibana.elastic.otlp`, ...          | 9200, 5601        |
| `auth`               | Rauthy, Casdoor                   | `rauthy.auth`, `casdoor.auth`       | -                 |

## Where to find connection strings

> **Look at the `*-setup` and `*-init` services.** They are the clients of the service they belong to, so their
> `environment`, `command` and mounted key files show exactly how to connect: host, port, user, password, key and TLS
> options. Copy them into your app, swapping the container host for the host-side address (see the list below).

They are one-shot containers (`restart: no`) that do their job and exit:

- **`<service>-setup` runs before the service starts** (pre action). It prepares what the service needs to boot: an
  account key, a rendered config file. The service depends on it with `service_completed_successfully`.
- **`<service>-init` runs after the service is healthy** (post action). It connects to the running service and creates
  databases, users, containers, buckets or API tokens. It depends on the service with `service_healthy`, and apps that
  need that data depend on the `*-init` with `service_completed_successfully`.
- `tool-*` services are shared one-shots for the whole stack (`tool-cert`, `tool-tiny-hc`).

| One-shot                   | Phase | What it does              | Connection settings it shows                      |
|----------------------------|-------|---------------------------|---------------------------------------------------|
| `db-cosmos-setup`          | pre   | Account key into a volume | key file `/key/cosmos_key`                        |
| `storage-azurite-setup`    | pre   | Account key into a volume | key file `/key/azurite_key`                       |
| `auth-rauthy-setup`        | pre   | Rauthy config + root CA   | SMTP in `configs.rauthy_template`                 |
| `db-mssql-init`            | post  | `init/mssql/*.sql`        | `DB_HOST`, `DB_USERNAME`, `DB_PASSWORD`           |
| `db-postgres-init`         | post  | `init/postgres/*.sql`     | `POSTGRES_SERVER`, `_USER`, `_PASSWORD`           |
| `db-cosmos-init`           | post  | Database, containers      | `DB_URL`, `DB_ACCOUNT_KEY_FILE`, `DB_MODE`, TLS   |
| `storage-azurite-init`     | post  | Blob containers, ACL      | `BLOB_URL`, `BLOB_ACCOUNT_KEY_FILE`               |
| `otlp-elastic-search-init` | post  | `kibana_system` password  | URL and `elastic` login in `command`              |
| `otlp-rootprint-init`      | post  | Admin, ingest token       | `ROOTPRINT_URL`, token in `lab_rootprint_secrets` |

`db-*-init` runs every `init/<engine>/*.sql` against the server, then every `init/<engine>/<database>/*.sql` against
that database. Set `DB_INIT=false` to skip the Postgres seed. All scripts are idempotent, so the one-shots are safe to
run again: `docker compose --profile db up db-postgres-init`.

To see the resolved values (with `.env` applied) and to read a generated key or token:

```shell
docker compose --profile db config db-cosmos-init             # prints passwords too, don't paste it anywhere
docker run --rm -v lab_cosmos-key_data:/key:ro alpine cat /key/cosmos_key
docker run --rm -v lab_azurite-key_data:/key:ro alpine cat /key/azurite_key
docker run --rm -v lab_rootprint_secrets:/s:ro alpine cat /s/ingest.token
```

Connection strings that follow from them (`DOMAIN=dev.local`, default account names). Inside the stack use the
service name as host; from the host use `localhost` with the published port, or the OrbStack domain:

```text
MSSQL     Server=db-mssql,1433;User Id=sa;Password=<DB_PASSWORD>;TrustServerCertificate=true
          host: Server=localhost,1433;...  (or mssql.db.dev.local)
Postgres  postgres://<DB_USERNAME>:<DB_PASSWORD>@db-postgres:5432/postgres
          host: localhost:5432; app databases use their own role, e.g. n8n:n8n@.../n8n
Mongo     mongodb://<DB_USERNAME>:<DB_PASSWORD>@db-mongo:27017
          host: localhost:27017
Cosmos    AccountEndpoint=https://db-cosmos:8081/;AccountKey=<cosmos_key>;
          host: https://localhost:8083/  (self-signed, trust the certificate or disable validation)
Azurite   BlobEndpoint=http://lab-account.blob.azurite.storage.dev.local:10000;AccountName=lab-account;
          AccountKey=<azurite_key>;  (same URL from the host)
RustFS    http://storage-s3:9000, access key <APP_USERNAME>, secret key <APP_PASSWORD>
          host: http://localhost:9000
Redis     redis://cache-redis:6379  (valkey: cache-valkey:6379), host: redis.cache.dev.local:6379
Kafka     broker.kafka:29092, host: ${HOST_ADDR}:9092
OTLP      http://otlp:4318 (HTTP), http://otlp:4317 (gRPC), host: localhost:4318 / localhost:4317
```

## Environment (`.env`)

`env.txt` is the single template for `.env`. Compose reads `.env` from this folder.

| Variable                       | Used for                                             |
|--------------------------------|------------------------------------------------------|
| `DOMAIN`                       | Base of every domain and certificate                 |
| `TZ`                           | Time zone passed to the containers                   |
| `HOST_ADDR`                    | Host advertised by Kafka/Redpanda                    |
| `APP_USERNAME`                 | Default user name of every service                   |
| `APP_PASSWORD`                 | Default password of every service, see below         |
| `APP_EMAIL`                    | Admin e-mail, default `${APP_USERNAME}@${DOMAIN}`    |
| `CERT_PASSWORD`                | Password of the generated `.pfx` files               |
| `DB_USERNAME`, `DB_PASSWORD`   | Admin login of MSSQL, Postgres and Mongo             |
| `SMTP_*`                       | Outgoing mail of NocoDB; when unset it uses MailCrab |
| `KAFKA_VERSION`, `ELK_VERSION` | Image tags of the Confluent and Elastic images       |
| `AZURITE_ACCOUNT`              | Azurite storage account name (default `lab-account`) |
| `COSMOS_DB_*`                  | Database created by `db-cosmos-init` (default `app`) |

## Passwords and secrets

### One password, scoped overrides

`APP_USERNAME` and `APP_PASSWORD` are the defaults for everything. A scope can override them with its own variable,
which always falls back to the `APP_*` value:

```yaml
environment:
  POSTGRES_USER: ${DB_USERNAME:-$APP_USERNAME}
  POSTGRES_PASSWORD: ${DB_PASSWORD:-$APP_PASSWORD}
```

So with only `APP_PASSWORD` in `.env` every service shares one password, and setting `DB_PASSWORD` changes just the
database servers. The scopes in use today:

| Scope       | Variables                    | Fallback                   | Services                           |
|-------------|------------------------------|----------------------------|------------------------------------|
| Default     | `APP_*`                      | -                          | all other services                 |
| Database    | `DB_USERNAME`, `DB_PASSWORD` | `APP_*`                    | MSSQL (user `sa`), Postgres, Mongo |
| Certificate | `CERT_PASSWORD`              | `APP_PASSWORD`             | `.pfx` files from `tool-cert`      |
| SMTP        | `SMTP_*`                     | MailCrab and a dummy login | NocoDB                             |

Where each password ends up:

| Service                   | Login                                                                 |
|---------------------------|-----------------------------------------------------------------------|
| MSSQL                     | `sa` / `DB_PASSWORD`                                                  |
| Postgres, Mongo           | `DB_USERNAME` / `DB_PASSWORD`                                         |
| Cosmos emulator, Azurite  | account key derived from the secret file, see below                   |
| RustFS                    | `APP_USERNAME` / `APP_PASSWORD` (access key / secret key)             |
| Elasticsearch, Kibana     | `elastic` / secret file; Kibana uses `kibana_system` / `APP_PASSWORD` |
| Rauthy, NocoDB            | `APP_EMAIL` / `APP_PASSWORD`                                          |
| Casdoor                   | `admin` and `APP_USERNAME` (organization `built-in`) / `APP_PASSWORD` |
| Rootprint                 | `${APP_USERNAME}@${DOMAIN}` / `APP_PASSWORD`                          |
| n8n, Seq, Aspire, Grafana | no lab login: create the account in the UI, or anonymous              |

### The `app_password` secret file

Some images only read a password from a file, so `docker-compose.yaml` declares a Compose secret:

```yaml
secrets:
  app_password:
    file: ./secrets/app_password.txt
```

A service mounts it at `/run/secrets/app_password`. Elasticsearch reads it through `ELASTIC_PASSWORD_FILE`, and the
`db-cosmos-setup` / `storage-azurite-setup` containers hash it into the emulator account keys:

```shell
tr -d '\n' < /run/secrets/app_password | openssl dgst -sha512 -binary | openssl base64 -A > /key/cosmos_key
```

The client side reads the same key file (`DB_ACCOUNT_KEY_FILE`, `BLOB_ACCOUNT_KEY_FILE`). To get the key for an app on
the host, run the same command on the host against `secrets/app_password.txt`.

**Keep `secrets/app_password.txt` equal to `APP_PASSWORD`.** Several services mix both (Elasticsearch is bootstrapped
from the file while `otlp-elastic-search-init` and Kibana use `APP_PASSWORD`), so a mismatch breaks them. The file is
tracked in git with the placeholder from `env.txt`; to keep your real password out of commits run
`git update-index --skip-worktree services/secrets/app_password.txt`.

### App databases use their own fixed login

Apps that store data in the lab Postgres (Casdoor, n8n, NocoDB, Rootprint) do not use `DB_PASSWORD`. Their role and
database are created by `init/postgres/<app>.sql` with a fixed password equal to the app name, because the value is
embedded in a connection string or URL. These are local-only credentials, not secrets.

### Password rules

- MSSQL refuses weak passwords: at least 8 characters from 3 of upper case, lower case, digits and symbols. When
  `DB_PASSWORD` is unset this applies to `APP_PASSWORD`.
- `APP_PASSWORD` is written into a double-quoted YAML string (Casdoor init data), so avoid `"` and `\`.
- Quote values with special characters in `.env` (`APP_PASSWORD="..."`), and avoid `$`, which Compose interpolates.

### Changing a password

Most images only apply the password when their data volume is empty (Postgres, Mongo, MSSQL, Elasticsearch, and the
bootstrapped admins of Rauthy, Casdoor and NocoDB). After changing it in `.env`, either change it inside the service as
well, or drop the volume and let it bootstrap again:

```shell
docker compose --profile db down
docker volume rm lab_postgres_data
docker compose --profile db up -d
```

The Cosmos and Azurite keys are written by their `*-setup` container each time it runs, so they follow the secret file
after the next `up`.

## Adding a service

Put it in the matching `<category>.compose.yaml`. For a new category create `<category>.compose.yaml` (a category with
extra config files gets its own sub folder), add it to `include:` in `docker-compose.yaml` and to `DOMAIN` of
`tool-cert`.

```yaml
services:
  db-example:
    image: example/example:1.2
    container_name: db.example
    labels:
      - dev.orbstack.domains=example.db.${DOMAIN}
      - dev.orbstack.http-port=8080
    volumes:
      - example_data:/data
    healthcheck:
      test: [ "CMD-SHELL", "wget -qO /dev/null http://127.0.0.1:8080/health" ]
      interval: 10s
      timeout: 3s
      retries: 5
      start_period: 10s
    restart: unless-stopped
    profiles: [ db ]
    environment:
      TZ: ${TZ}
      EXAMPLE_USER: ${DB_USERNAME:-$APP_USERNAME}
      EXAMPLE_PASSWORD: ${DB_PASSWORD:-$APP_PASSWORD}

volumes:
  example_data:
```

Checklist:

- Naming: service `<category>-<name>`, container `<category>.<name>`, volume `<name>_<volume>`.
- Profiles: always the category, plus a narrower one when useful (`sql`, `kafka`, `s3`).
- Domain: `dev.orbstack.domains=<name>.<category>.${DOMAIN}`; HTTP services also set `dev.orbstack.http-port`.
- Healthcheck: probe the service's own health endpoint on `127.0.0.1` with a tool the image ships (`curl`, `wget`,
  `bash` with `/dev/tcp`, `node -e "fetch(...)"`). For images without a shell, mount `tiny-hc_bin` at `/hc:ro`, depend
  on `tool-tiny-hc` with `service_completed_successfully`, and use
  `test: [ "CMD", "/hc/tiny-hc", "http://127.0.0.1:8080/health" ]`.
- Dependencies: wait with `condition: service_healthy`, or `service_completed_successfully` for one-shot services.
  Mark optional ones `required: false`.
- TLS: mount `certificate:/certs:ro` and depend on `tool-cert`.
- Credentials: never hard-code a real secret. Pick the narrowest existing scope (`${DB_PASSWORD:-$APP_PASSWORD}`), or
  use `${APP_PASSWORD}` directly.
- Needs its own Postgres database: add `init/postgres/<name>.sql` creating the role and database (copy `casdoor.sql`),
  and depend on `db-postgres-init` with `service_completed_successfully`. Per-database scripts go in
  `init/postgres/<name>/*.sql`.
- Preparation goes into one-shot services, never into the main service's entrypoint:
  - `<service>-setup` (pre): anything the service needs before it boots (keys, rendered config). Use
    `restart: no`, the same profiles, and make the service depend on it with `service_completed_successfully`.
  - `<service>-init` (post): anything that needs the running service (schemas, users, buckets, tokens). Depend on the
    service with `service_healthy`, keep the script idempotent, and put its files in `init/<name>/`.
  - Give them the full connection settings (URL, user, password or key file, TLS options) as environment variables,
    so they double as the reference for how to connect, see "Where to find connection strings".

### Adding a password scope

When a group of services needs a password different from `APP_PASSWORD`:

1. Add the variable to `env.txt` (the template) and to your `.env`, e.g. `BROKER_PASSWORD="..."`.
2. Reference it with the fallback everywhere in that scope: `${BROKER_PASSWORD:-$APP_PASSWORD}`. Do the same for the
   user name if it differs: `${BROKER_USERNAME:-$APP_USERNAME}`.
3. List it in the scope table above.

When the image only reads the password from a file, add a Compose secret next to `app_password` in
`docker-compose.yaml` and mount it in the service:

```yaml
# docker-compose.yaml
secrets:
  broker_password:
    file: ./secrets/broker_password.txt

# broker.compose.yaml
services:
  broker-example:
    secrets:
      - source: broker_password
        mode: 0400
    environment:
      EXAMPLE_PASSWORD_FILE: /run/secrets/broker_password
```

Commit only a placeholder in `secrets/` (the value from `env.txt`), and keep the file and the variable in sync. A key
derived from a password (like the Cosmos/Azurite keys) is computed by a one-shot `*-setup` container into a named volume
that the service and its clients mount read-only.