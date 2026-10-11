---
name: docker-container
description: House standard for containers - writing Dockerfiles for your own apps (multi-stage, smallest runtime base such as scratch or chiseled, non-root, multi-arch, stripped symbols, OCI labels, tiny-hc baked in) and defining Docker Compose services (category naming, profiles, healthchecks on every service, depends_on conditions, one-shot init services, secrets from .env, OrbStack labels, shared TLS volume). Use this whenever the user wants to containerise or dockerize an app, write, review or shrink a Dockerfile, build a multi-arch image, add or change a service in a docker-compose / compose.yaml file, add or fix a healthcheck, make one service wait for another, or debug a container that is unhealthy, restarting or not starting, even if they only say "add X to the stack" or "run this in Docker". Covers .NET Native AOT and Rust images in detail; the general rules apply to any stack.
---

# Docker container

One standard for two jobs: building an image for an app you own, and wiring services into a Compose stack. Both aim at
the same result: a container that is small, runs unprivileged, reports its own health truthfully and starts in the
right order without sleeps or retries.

Read the project first. An existing Dockerfile or compose file, and the project's `AGENTS.md` / `CLAUDE.md`, win over
this skill wherever they are more specific. Match their naming and layout instead of converting them, and change only
what the task needs.

## Images

| Area         | Standard                                                                                               |
|--------------|--------------------------------------------------------------------------------------------------------|
| Structure    | `# syntax=docker/dockerfile:1`, multi-stage: a build stage with the SDK, a runtime stage with only the app |
| Runtime base | the smallest one the binary can run on: `scratch` for a static binary, a chiseled / distroless base for one that needs libc, ICU or tzdata. Alpine only when a shell is really needed at runtime |
| User         | non-root, set with a numeric `USER` in the runtime stage (`$APP_UID` on .NET bases, `65532:65532` on scratch) |
| Platforms    | `linux/amd64` and `linux/arm64` (arm64 is what Docker runs on Apple silicon). Map `TARGETARCH` to the toolchain's own name and fail on anything unmapped |
| Size         | no debug symbols, no SDK, no package manager caches, no source in the runtime stage                    |
| Build speed  | `RUN --mount=type=cache` for the package and build caches, `COPY --link`, a `.dockerignore`            |
| Entry point  | exec form (`ENTRYPOINT ["/bin/app"]`), one process, configuration through environment variables        |
| Labels       | `org.opencontainers.image.source` in the Dockerfile; title, description, licenses, revision and created at build time |
| Health       | the app serves a health endpoint, and the image carries a probe binary when it has no shell (see below) |

Why: a runtime image with no shell and no package manager has almost nothing to patch or exploit, and pulls in a
second. That same choice removes `curl` and `wget`, which is why the health probe needs separate thought. Failing on an
unknown architecture beats silently shipping an amd64 binary under an arm64 tag.

Details that are easy to miss:

- **Cache mounts hide their content from later layers.** Copy the build output out of the mounted directory in the same
  `RUN` that produces it.
- **Debug symbols go missing only if you delete them.** .NET Native AOT writes `*.dbg` next to the binary; remove them
  in the build stage (`rm /app/*.dbg`). Rust strips through the release profile (`strip = true`).
- **A named volume takes its ownership from the image directory it is mounted on.** A non-root app that writes to a
  volume needs that directory to exist in the image, owned by its user. On scratch there is no `mkdir`, so create the
  directory in the build stage and bring it over with `COPY --from=build --chown=65532:65532 /data /data`. Without
  this the volume is owned by root, and the usual "fix" is `user: root` in compose, which gives up the non-root rule.
- **scratch has no CA certificates and no time zone data.** Copy `/etc/ssl/certs/ca-certificates.crt` from the build
  stage when the app makes outbound TLS calls with the system trust store.
- **Don't put `HEALTHCHECK` in the Dockerfile.** Intervals and start periods depend on where the image runs, and
  Kubernetes ignores the instruction. Ship the probe binary in the image and define the check in compose.
- **Runtime labels differ from build labels.** Values that change per build (`revision`, `created`) are passed at
  build time: `build.labels` in compose, or `--label` on `docker buildx build`.

Stack references, each with a complete Dockerfile to start from:

- .NET Native AOT: read `references/dotnet-aot.md`.
- Rust: read `references/rust.md`.

For another stack, apply the table above and take the shape from whichever reference is closer (a static binary is
like Rust; a binary that needs a libc base is like .NET).

### Check the image

Make sure a multi-platform builder exists, build every platform with it so a failure on any of them shows up before
anything is run, then load the image for the machine's own architecture and look at the real artifact:

```shell
# 1. a docker-container builder: reuse the first one found, create `multiarch` only when there is none
builder=$(docker buildx ls | awk '$2 == "docker-container" { sub(/\*$/, "", $1); print $1; exit }')
[ -n "$builder" ] || { builder=multiarch; docker buildx create --name "$builder" --driver docker-container; }
docker buildx inspect "$builder" --bootstrap | grep Platforms    # starts it; must list linux/amd64 and linux/arm64

# 2. every platform in one build; all must pass
docker buildx build --builder "$builder" --platform linux/amd64,linux/arm64 .

# 3. the current architecture, loaded from the same builder's cache
docker buildx build --builder "$builder" --load -t app:local .
docker run --rm app:local --version                              # or whatever proves it starts
docker image inspect app:local --format '{{.Config.User}} {{.Size}}'
```

An empty `User` means the image runs as root. Say which platforms were built, which one was run, and which were only
assumed.

- The builder is needed because the default `docker` build driver with the classic image store rejects
  `--platform linux/amd64,linux/arm64` ("Multi-platform build is not supported for the docker driver"). A
  `docker-container` builder runs BuildKit in its own container and has no such limit.
- Reuse before creating: machines collect builders (`docker buildx ls`), and one more adds a container and a separate
  cache. `docker buildx create` without `--use` leaves the user's default builder alone; pass `--builder` on each
  command instead of switching it. Tell the user when a builder was created or started.
- If `Platforms` lacks the foreign architecture, emulation is not installed. Docker Desktop and OrbStack ship it; on
  a plain Linux engine run `docker run --privileged --rm tonistiigi/binfmt --install all` once, after asking, because
  it changes the host.
- Step 2 prints "No output specified with docker-container driver". That is expected: the result stays in the build
  cache, which is all this step is for.
- Step 3 has no `--platform`, so it targets the machine it runs on, and it uses the same builder so the layers come
  from the cache step 2 filled.
- Never put the architecture in a tag (`app:amd64`, `app:1.2.0-arm64`). A tag names a version, and one tag covers
  every platform through its manifest list, so Docker picks the right one on each machine. The other platforms need
  no tag at all here: step 2 only proves they build.
- Publishing one tag for all platforms is the same build with `-t <registry>/<image>:<version> --push`. Leave it to
  the release pipeline unless the user asks, and say it was not run.
- `build.platforms` in a compose file makes `docker compose build` need such a builder too. Set it only where one is
  available (`docker compose build --builder <name>`), since a plain `docker compose build` fails with it elsewhere.

## Compose services

### Layout and naming

A stack with many services is one Compose project split by category: `docker-compose.yaml` holds the shared tools,
volumes, secrets and network, and `include`s one `<category>.compose.yaml` per category (`db`, `cache`, `broker`,
`otlp`, ...). A category that needs extra config files gets its own sub folder.

| Thing     | Name                    | Example             |
|-----------|-------------------------|---------------------|
| Service   | `<category>-<name>`     | `cache-valkey`      |
| Container | `<category>.<name>`     | `cache.valkey`      |
| Volume    | `<name>_<volume>`       | `postgres_data`     |
| Profile   | the category            | `profiles: [ db ]`  |
| One-shot  | `<service>-init`, `tool-<name>` | `db-postgres-init`, `tool-cert` |

Every service has the profile of its category, so `docker compose --profile db up -d` starts one category and nothing
else. Extra profiles are fine (`[ db, sql ]`). A project with a single app and a sidecar or two doesn't need
categories; plain service names are right there.

A service definition, in the usual key order:

```yaml
  cache-console:
    image: valkey/valkey-admin:1.2.0
    container_name: cache.console
    labels:
      - dev.orbstack.domains=console.cache.${DOMAIN}
      - dev.orbstack.http-port=8080
    restart: unless-stopped
    depends_on:
      tool-tiny-hc:
        condition: service_completed_successfully
      cache-valkey:
        condition: service_healthy
    volumes:
      - tiny-hc_bin:/hc:ro
    profiles: [ cache ]
    healthcheck:
      test: [ "CMD", "/hc/tiny-hc", "http://127.0.0.1:8080/" ]
      interval: 10s
      timeout: 3s
      retries: 5
      start_period: 10s
    environment:
      TZ: ${TZ}
      VALKEY_HOST: cache-valkey
```

- Long-running services use `restart: unless-stopped`. One-shot services use `restart: no`.
- Services reach each other by service name on the project network. Publish a port only when a tool on the host needs
  it (a database client, an SDK under development). HTTP UIs are reached through the OrbStack domain: set
  `dev.orbstack.domains` and, next to it, `dev.orbstack.http-port` with the container port.
- A service without its own authentication stays unpublished, and a comment above it says so.
- Values that differ per machine or are secret come from `.env` as `${VAR}`, with `${VAR:-$FALLBACK}` for an optional
  override. Add every new variable to the env template (`env.txt`) with a harmless sample value. Real secrets never go
  in a compose file or the template.
- Write `$$` for a dollar sign that the container's shell should see (`"$$POSTGRES_USER"` in a healthcheck).
  A single `$` is expanded by Compose on the host, usually to an empty string.
- Explain a non-obvious choice in a short comment above the service: why an entrypoint is overridden, why a platform
  is forced, why no port is published.
- YAML style: two spaces, no value alignment, inner spaces in flow collections (`[ db, sql ]`, `{ a: 1 }`).

### Healthchecks

Every long-running service gets one where the image allows it. `depends_on` can only wait for readiness that a
healthcheck reports, so one missing check turns into a startup race in every dependent.

Read `references/health-check.md` before writing or fixing a healthcheck. It has the step-by-step procedure, a probe
for each kind of image, the tiny-hc wiring, timing values and a table for diagnosing an unhealthy container. The rules
in short:

- Probe the service's own health endpoint from inside its container, on `127.0.0.1` and the container port. Match the
  body when the status code alone says too little.
- Set `interval`, `timeout` and `retries`, plus `start_period` for slow starters.
- Use what the image already ships: the service's own client, `curl`, `wget`, `bash` with `/dev/tcp`, or
  `node -e "fetch(...)"`. Check with `docker exec <container> <tool> --version`.
- An image without a shell or HTTP client (distroless, chiseled, scratch) uses
  [tiny-hc](https://github.com/fakhrulhilal/tiny-hc) in exec form: baked into your own image as `/bin/hc`, or shared
  into someone else's through the `tiny-hc_bin` volume mounted read-only at `/hc`.

  ```yaml
      healthcheck:
        test: [ "CMD", "/hc/tiny-hc", "--expect-response", "healthy", "http://127.0.0.1:80/health" ]
        interval: 10s
        timeout: 3s
        retries: 5
        start_period: 10s
  ```

- Enable the health endpoint first when it is off by default.
- Never add a sidecar container only to probe another service. One-shot services (`*-init`, `tool-*`) get no
  healthcheck.

### Start order

- A dependent waits with `condition: service_healthy`. `service_started` only means the process was launched, which is
  the race the healthcheck exists to remove.
- One-shot services (`*-init` for migrations and seeding, `tool-*` for shared setup) get no healthcheck. Their
  dependents wait with `condition: service_completed_successfully`, and a non-zero exit stops the chain.
- An init service runs the same image as the service it prepares when that image has the client it needs, mounts its
  scripts read-only, and depends on that service being healthy.
- Add `required: false` to a dependency that may be absent because its profile is not active.

### TLS

Certificates come from one shared volume, filled once by a `tool-cert` one-shot (root CA, intermediate CA, and one
certificate per category domain). A service mounts that volume read-only at `/certs` and depends on `tool-cert` with
`service_completed_successfully`. A new category is added to the domain list of `tool-cert`. Don't generate
certificates per service, and don't commit them.

### Check the stack

```shell
docker compose config -q                                  # syntax and interpolation
docker compose --profile <category> up -d --wait          # returns once everything is healthy, fails if not
docker compose ps --format 'table {{.Name}}\t{{.Status}}'
docker inspect --format '{{json .State.Health}}' <container>   # the output of the last probes
```

When a container stays `starting` or turns `unhealthy`, read the probe output from `docker inspect` before changing
anything. It tells apart a missing tool (exit 127, "executable file not found"), a wrong address or port (connection
refused), a service that is still booting (raise `start_period`) and a body that doesn't match. Then run the probe by
hand with `docker exec` to confirm the fix.

Report what was actually started and seen healthy, and what was only written.