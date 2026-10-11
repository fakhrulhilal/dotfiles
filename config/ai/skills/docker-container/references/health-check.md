# Health checks

How to define a `healthcheck` for a Compose service. `depends_on` can only wait for readiness that a healthcheck
reports, so one missing or dishonest check turns into a startup race in every dependent.

## Contents

- [Rules](#rules)
- [Defining one, step by step](#defining-one-step-by-step)
- [Picking the probe](#picking-the-probe)
- [Images with a shell](#images-with-a-shell)
- [Images without a shell: tiny-hc](#images-without-a-shell-tiny-hc)
- [Timing](#timing)
- [What gets no healthcheck](#what-gets-no-healthcheck)
- [Dependents](#dependents)
- [When it stays unhealthy](#when-it-stays-unhealthy)

## Rules

- Every long-running service has a healthcheck where its image allows one.
- Probe the service's **own health endpoint**, from **inside its container**, on `127.0.0.1`.
    - Inside the container the check sees what the service itself sees. A probe from another container or the host
      also tests the network, a published port and DNS, and fails for reasons that aren't the service's.
    - `localhost` can resolve to `::1` while the service listens on IPv4 only, which makes a healthy service look
      dead.
    - Use the container port, not the published one.
- Match the response body when the status code alone says too little, such as an endpoint that answers 200 while the
  service is still starting.
- Set `interval`, `timeout` and `retries`, plus `start_period` for slow starters.
- Use what the image already ships. Don't install a tool or change the image just to probe it.
- An image without a shell or HTTP client (distroless, chiseled, scratch) uses tiny-hc, in exec form.
- Enable the health endpoint first when it is off by default, instead of probing a port that merely accepts
  connections.
- Never add a sidecar container only to probe another service. It reports its own health, not the service's, and
  nothing can depend on it meaningfully.
- One-shot services (`*-init`, `tool-*`) get no healthcheck.
- Dependents wait with `condition: service_healthy`, not `service_started`.

## Defining one, step by step

1. Find the health endpoint in the image's documentation (`/health`, `/healthz`, `/ready`, an admin port). Enable it
   if it is off by default.
2. Find out what the image ships and pick the probe from the table below:

   ```shell
   docker exec <container> <tool> --version                   # a running container
   docker run --rm --entrypoint <tool> <image> --version      # not started yet
   ```

   "executable file not found" for `sh` means the image has no shell at all: go straight to tiny-hc.
3. Write the `test` with `127.0.0.1`, the container port and a body match where a 200 alone proves little.
4. Set `interval`, `timeout`, `retries`, and `start_period` from how long the service really takes to boot.
5. Start it with `docker compose --profile <category> up -d --wait` and read the probe output:
   `docker inspect --format '{{json .State.Health}}' <container>`.
6. Switch its dependents from `service_started` to `service_healthy`.

## Picking the probe

| The image has                           | Probe                                                                     |
|-----------------------------------------|---------------------------------------------------------------------------|
| the service's own client                | `pg_isready`, `redis-cli ping`, `mongosh --eval`, `sqlcmd -Q "select 1"`  |
| `curl`                                  | `curl -fsS http://127.0.0.1:8080/health`                                  |
| `wget`                                  | `wget -qO- http://127.0.0.1:8080/health`                                  |
| `bash` only                             | HTTP over `/dev/tcp`                                                      |
| `node`                                  | `node -e "fetch(...)"`                                                    |
| nothing (distroless, chiseled, scratch) | tiny-hc                                                                   |

Prefer the service's own client when there is one: `pg_isready` knows the difference between "port open" and
"accepting connections", which an HTTP or TCP probe does not.

## Images with a shell

`CMD-SHELL` and the plain string form both run through `/bin/sh` in the container. Write `$$` for a dollar sign that
the container's shell should expand; a single `$` is expanded by Compose on the host, usually to an empty string.

The service's own client:

```yaml
    healthcheck:
      test: [ "CMD-SHELL", "pg_isready -h 127.0.0.1 -p 5432 -U \"$$POSTGRES_USER\"" ]
      interval: 10s
      timeout: 3s
      retries: 5
      start_period: 10s
```

A client whose exit code is not enough, so the answer is compared:

```yaml
    healthcheck:
      test: [ "CMD-SHELL", "[ \"$$(valkey-cli -h 127.0.0.1 ping)\" = 'PONG' ]" ]
      interval: 30s
      timeout: 5s
      retries: 3
```

`curl` or `wget`, with a body match:

```yaml
    healthcheck:
      test: [ "CMD-SHELL", "curl -fsS http://127.0.0.1:8080/health | grep -qi healthy" ]
      interval: 10s
      timeout: 3s
      retries: 3
```

`curl -f` turns an HTTP error status into a non-zero exit; without it a 503 page counts as success. BusyBox `wget`
(Alpine) already fails on an error status and supports only `-q`, `-O` and `-T`.

`bash` without an HTTP client, speaking HTTP over `/dev/tcp` and matching the body:

```yaml
    healthcheck:
      test: >
        bash -c '
          exec 3<>/dev/tcp/127.0.0.1/80 || exit 1;
          printf "GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3;
          grep -q "Seq node is in service" <&3 || exit 1;
        '
      interval: 10s
      timeout: 3s
      retries: 3
```

`/dev/tcp` is a bash feature. It does not exist in `sh`, `dash` or BusyBox `ash`, so call `bash -c` explicitly.

`node`, in exec form so no shell is involved:

```yaml
    healthcheck:
      test: [ "CMD", "node", "-e", "fetch('http://127.0.0.1:3000/health').then(r => process.exit(r.ok ? 0 : 1)).catch(() => process.exit(1))" ]
      interval: 10s
      timeout: 3s
      retries: 3
```

## Images without a shell: tiny-hc

[tiny-hc](https://github.com/fakhrulhilal/tiny-hc) is a static health check binary for images that have neither a
shell nor an HTTP client. It exits 0 when the response status is 2xx and 1 otherwise.

| Option                       | Effect                                                              |
|------------------------------|---------------------------------------------------------------------|
| `--expect-response <TEXT>`   | also require the body to contain TEXT as whole word(s), any case    |
| `-t`, `--timeout <SECONDS>`  | whole check timeout, default 5                                      |
| `--basic-auth <USER:PASS>`   | send HTTP basic authentication                                      |
| `-L`, `--location`           | follow redirects (`--max-redirs`, default 3)                        |

The URL must start with `http://` or `https://`; certificates are not verified. Run `tiny-hc --help` to confirm the
options of the version in use.

Always use the exec form (`"CMD"`), because there is no `/bin/sh` to run a string:

```yaml
    healthcheck:
      test: [ "CMD", "/hc/tiny-hc", "--expect-response", "healthy", "http://127.0.0.1:80/health" ]
      interval: 10s
      timeout: 3s
      retries: 5
      start_period: 10s
```

The exec form has no shell, so no variable is expanded inside the container. A value that must come from the
environment is interpolated by Compose instead (`"--basic-auth", "${APP_USERNAME}:${APP_PASSWORD}"`).

### Someone else's image: the shared volume

A one-shot `tool-tiny-hc` service seeds a named volume with the binary. The tiny-hc image is `FROM scratch` and has
no `cp`, so the trick is to mount the empty volume over `/bin`: Docker copies the image's `/bin/tiny-hc` into an
empty volume on first use.

```yaml
services:
  tool-tiny-hc:
    image: iroel/tiny-hc:latest
    container_name: tool.tiny-hc
    restart: no
    command: [ "--version" ]
    volumes:
      - tiny-hc_bin:/bin

volumes:
  tiny-hc_bin:
```

The service that needs it mounts the volume read-only at `/hc` and waits for the seeding to finish:

```yaml
  otlp-collector:
    depends_on:
      tool-tiny-hc:
        condition: service_completed_successfully
    volumes:
      - tiny-hc_bin:/hc:ro
    healthcheck:
      test: [ "CMD", "/hc/tiny-hc", "http://127.0.0.1:13133/" ]
      interval: 10s
      timeout: 3s
      retries: 5
      start_period: 10s
```

Docker only seeds an **empty** volume, so a new tiny-hc version is picked up only after the volume is removed:
`docker volume rm <project>_tiny-hc_bin` (`lab_tiny-hc_bin` in the dotfiles `services/` stack, where `otlp-aspire`
and `otlp-collector` are the working examples).

### Your own image: bake it in

```dockerfile
COPY --from=ghcr.io/fakhrulhilal/tiny-hc /bin/tiny-hc /bin/hc
```

```yaml
    healthcheck:
      test: [ "CMD", "/bin/hc", "--expect-response", "healthy", "http://127.0.0.1:8080/_health/ready" ]
```

No volume and no `tool-tiny-hc` dependency are needed then.

### A health endpoint that is off by default

Some services don't serve a health endpoint until it is configured. The OpenTelemetry Collector needs the
`health_check` extension in its config (`otlp.yml`):

```yaml
extensions:
  health_check:
    endpoint: 0.0.0.0:13133

service:
  extensions: [ health_check ]
```

Enable it, then probe it. A port that accepts connections says the process is alive, not that it is ready.

## Timing

| Key            | Meaning                                                    | Typical values                               |
|----------------|------------------------------------------------------------|----------------------------------------------|
| `interval`     | time between probes                                        | `10s`; `30s` for cheap, stable services      |
| `timeout`      | how long one probe may take before it counts as failed     | `3s` to `5s`, always below `interval`        |
| `retries`      | consecutive failures before the container is `unhealthy`   | `3` to `5`                                   |
| `start_period` | boot window in which failures don't count against `retries` | `10s` to `30s`; SQL Server and Kafka need `30s` or more |

A success during `start_period` marks the container healthy at once, so a generous value costs nothing for a service
that boots fast. Dependents wait up to roughly `start_period + interval * retries`; a long `interval` therefore slows
the whole stack's startup.

## What gets no healthcheck

- One-shot services: `*-init` (migrations, seeding) and `tool-*` (shared setup such as `tool-cert` and
  `tool-tiny-hc`). They exit, so there is nothing to probe. Their exit code is the signal.
- An image that has no tool to probe with and cannot take the tiny-hc volume. Say so in a comment above the service
  instead of adding a sidecar.

## Dependents

```yaml
    depends_on:
      db-postgres:
        condition: service_healthy                 # long-running: wait for its healthcheck
      db-postgres-init:
        condition: service_completed_successfully  # one-shot: wait for exit code 0
      cache-redis:
        condition: service_healthy
        required: false                            # may be absent when its profile is not active
```

`service_started` only means the process was launched, which is exactly the race a healthcheck exists to remove.

## When it stays unhealthy

Read the probe's own output before changing anything:

```shell
docker inspect --format '{{json .State.Health}}' <container>
```

| Output                                             | Cause                                         | Fix                                            |
|----------------------------------------------------|-----------------------------------------------|------------------------------------------------|
| `executable file not found`, exit 127              | the tool or the shell is not in the image     | pick another probe; no shell means tiny-hc     |
| `connection refused`                               | wrong port, `localhost` resolving to `::1`, or the service binds elsewhere | use `127.0.0.1` and the container port |
| healthy only after several failures                | the service is still booting                  | raise `start_period`                           |
| exit 1 with a 2xx answer                           | the body match doesn't fit                    | fetch the body by hand and correct the text    |
| empty user or password in the command              | `$VAR` expanded by Compose on the host        | write `$$VAR`                                  |
| `Health` is `null`                                 | no healthcheck is defined or it was disabled  | add one; check for `disable: true`             |

Then run the probe by hand with `docker exec <container> <probe>` and check its exit code, to confirm the fix before
restarting the stack.