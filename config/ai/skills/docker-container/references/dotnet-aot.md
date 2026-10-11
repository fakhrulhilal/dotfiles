# .NET Native AOT images

A Native AOT app is one native executable. It needs no .NET runtime in the image, only the native libraries the
runtime links against, so the base is `runtime-deps`, never `aspnet` or `runtime`.

## Base images

| Stage   | Image                                                        | Notes                                              |
|---------|--------------------------------------------------------------|----------------------------------------------------|
| Build   | `mcr.microsoft.com/dotnet/sdk:10.0-noble-aot`                | the SDK plus clang and the native toolchain        |
| Runtime | `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled`  | no shell, no package manager, non-root by default  |
| Runtime | `...:10.0-noble-chiseled-extra`                              | adds ICU and tzdata, for culture-aware formatting and time zones |
| Alpine  | `sdk:10.0-alpine-aot` and `runtime-deps:10.0-alpine-extra`   | musl build with a shell, only when one is needed   |

Pick `chiseled-extra` and set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` when the app formats dates, numbers or
text by culture or converts time zones. Otherwise the plain chiseled image with invariant globalization is smaller.

The build and runtime stages must use the same libc: a `linux-x64` binary doesn't start on Alpine, and a
`linux-musl-x64` binary doesn't start on Ubuntu.

## Dockerfile

```dockerfile
# syntax=docker/dockerfile:1
ARG DISTRO=noble
FROM mcr.microsoft.com/dotnet/sdk:10.0-$DISTRO-aot AS build
ARG PROJECT
ARG TARGETARCH
WORKDIR /source

COPY --link . .
RUN --mount=type=cache,sharing=locked,target=/root/.nuget \
    --mount=type=cache,sharing=locked,target=/source/bin \
    --mount=type=cache,sharing=locked,target=/source/obj \
    RID=$(case "$TARGETARCH" in \
            amd64) echo linux-x64 ;; \
            arm64) echo linux-arm64 ;; \
            *) echo "unsupported arch: $TARGETARCH" >&2; exit 1 ;; \
          esac) && \
    dotnet publish -c Release -o /app $PROJECT /p:AssemblyName=launcher -r "$RID" \
        && rm /app/*.dbg

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-$DISTRO-chiseled-extra
LABEL org.opencontainers.image.source="https://github.com/<owner>/<repo>"
WORKDIR /home/app
COPY --link --from=build /app .
COPY --from=ghcr.io/fakhrulhilal/tiny-hc /bin/tiny-hc /bin/hc
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
USER $APP_UID
ENTRYPOINT ["./launcher"]
```

- `PROJECT` is a `.csproj` path or a file-based app (`WebhookCli.cs`). A file-based app publishes as Release and
  Native AOT by default; a `.csproj` needs `<PublishAot>true</PublishAot>`.
- `/p:AssemblyName=launcher` gives every app the same executable name, so one Dockerfile serves many apps and the
  `ENTRYPOINT` never changes.
- The cache mounts on `bin` and `obj` keep incremental build state between builds. Publishing to `/app`, outside the
  mounts, is what makes the output survive into the layer.
- `rm /app/*.dbg` removes the native debug symbols, which are several times the size of the binary. For web apps also
  set `StripSymbols=true`.
- For Alpine, change the RIDs to `linux-musl-x64` and `linux-musl-arm64`, and drop the tiny-hc copy if the image's
  `wget` is enough for the healthcheck.
- `$APP_UID` (1654) is defined by the Microsoft base images. The chiseled images already default to it; stating it
  keeps the intent visible and survives a base image change.
- ASP.NET Core listens on port 8080 in containers by default (`ASPNETCORE_HTTP_PORTS`). A non-root user cannot bind
  ports below 1024.

## .dockerignore

```gitignore
**/bin/
**/obj/
**/out/
.git/
.vs/
.idea/
*.user
```

Host `bin` and `obj` folders hold artifacts for the host's RID. Copying them in makes the publish pick up stale
state.

## Build

```shell
docker buildx build --load --platform linux/arm64 --build-arg PROJECT=src/Api/Api.csproj -t api:local .
```

In compose, pass the project and the per-build labels under `build`:

```yaml
    build:
      platforms:
        - linux/amd64
        - linux/arm64
      args:
        PROJECT: src/Api/Api.csproj
      labels:
        org.opencontainers.image.title: "Api"
        org.opencontainers.image.licenses: "MIT"
        org.opencontainers.image.revision: "${GIT_COMMIT_SHA:-unknown}"
        org.opencontainers.image.created: "${BUILD_TIMESTAMP:-unknown}"
```

The build stage runs on the target platform, so the foreign architecture is built under emulation. That is slow but
needs no cross toolchain. A trim or AOT warning (`IL2xxx`, `IL3xxx`) in the publish output is a real defect in the
image; fix it in the code instead of silencing it.

## Health endpoint

Expose a readiness endpoint that answers `Healthy` only when the app can serve requests, and probe it with the baked
in binary:

```yaml
    healthcheck:
      test: [ "CMD", "/bin/hc", "--expect-response", "healthy", "http://127.0.0.1:8080/_health/ready" ]
      start_period: 3s
      interval: 10s
      timeout: 2s
      retries: 3
```

## Writable data

The app runs as `$APP_UID` and cannot write to a root-owned volume. Create the data directory in the build stage and
copy it with ownership, so a fresh named volume mounted there inherits it:

```dockerfile
# build stage
RUN mkdir -p /data

# runtime stage
COPY --from=build --chown=$APP_UID:$APP_UID /data /var/app/data
```