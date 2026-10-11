# Rust images

A Rust binary linked statically against musl needs nothing from the image, so the runtime stage is `FROM scratch`: the
binary, optionally a probe binary and CA certificates, and nothing else.

Choose the build path by how the project ships:

- **The project follows the `rust-binary-project` skill** (release archives, `scripts/build.sh`, `dist/<target>/`):
  the image is assembled from those pre-built binaries. Use that skill's Dockerfile and workflows; don't add a
  second build path.
- **Anything else** (a service, an internal tool): build inside Docker with the Dockerfile below.

## Dockerfile (build inside Docker)

```dockerfile
# syntax=docker/dockerfile:1
FROM rust:1-alpine AS build
ARG BIN
WORKDIR /source
RUN apk add --no-cache musl-dev ca-certificates

COPY --link . .
RUN --mount=type=cache,sharing=locked,target=/usr/local/cargo/registry \
    --mount=type=cache,sharing=locked,target=/source/target \
    cargo build --release --locked --bin "$BIN" \
        && mkdir -p /out/bin /out/data \
        && cp "target/release/$BIN" /out/bin/app

FROM scratch
LABEL org.opencontainers.image.source="https://github.com/<owner>/<repo>"
COPY --from=build /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/
COPY --from=build --chmod=0755 /out/bin/app /bin/app
COPY --from=ghcr.io/fakhrulhilal/tiny-hc /bin/tiny-hc /bin/hc
COPY --from=build --chown=65532:65532 /out/data /data
USER 65532:65532
ENTRYPOINT ["/bin/app"]
```

- On Alpine the host target is `*-unknown-linux-musl`, and musl targets link statically by default, so plain
  `cargo build --release` produces a binary that runs on scratch. Confirm with `ldd` in the build stage if the
  project has C dependencies; "not a dynamic executable" is the answer you want.
- The build stage runs on the target platform, so `linux/arm64` on an amd64 host is built under emulation: slow, but
  with no cross linker to set up. A project that needs fast multi-arch builds cross-compiles outside Docker instead
  (the `rust-binary-project` way, with cargo-zigbuild).
- `target/` is a cache mount, so the binary is copied to `/out` in the same `RUN`.
- Strip and size settings belong in `Cargo.toml`, not in the Dockerfile:

  ```toml
  [profile.release]
  strip = true
  lto = true
  codegen-units = 1
  ```

- `65532` is the conventional `nonroot` id of distroless images. scratch has no `/etc/passwd`, so the user must be
  numeric.
- Drop the lines the app doesn't need: the CA bundle when it makes no outbound TLS calls (or uses `webpki-roots`),
  `/bin/hc` for a CLI that is never health checked, `/data` when it writes nothing.
- scratch has no `/tmp`. An app that needs scratch space gets a `tmpfs` mount in compose or a directory copied in the
  same way as `/data`.
- A crate that links OpenSSL makes static builds painful. Prefer `rustls` features (`reqwest` with `rustls-tls`,
  `sqlx` with `tls-rustls`).

## .dockerignore

```gitignore
target/
.git/
dist/
```

## Dockerfile (pre-built binaries)

The shape used by `rust-binary-project`: one stage per architecture picks the matching binary, and `.dockerignore`
lets only `dist/` into the build context.

```dockerfile
# syntax=docker/dockerfile:1
ARG TARGETARCH

FROM scratch AS bin-amd64
COPY dist/x86_64-unknown-linux-musl/app /
FROM scratch AS bin-386
COPY dist/i686-unknown-linux-musl/app /
FROM scratch AS bin-arm64
COPY dist/aarch64-unknown-linux-musl/app /

FROM bin-${TARGETARCH} AS bin

FROM scratch
COPY --from=bin --chmod=0755 / /bin/
USER 65532:65532
ENTRYPOINT ["/bin/app"]
```

An architecture without a `bin-<arch>` stage fails the build, which is the wanted behaviour.

## Health endpoint

A service exposes `/health/live` (no dependency checks) and `/health/ready` (all checks), as the `rust-backend` skill
sets up. Compose probes readiness:

```yaml
    healthcheck:
      test: [ "CMD", "/bin/hc", "--expect-response", "healthy", "http://127.0.0.1:8080/health/ready" ]
      interval: 10s
      timeout: 3s
      retries: 3
```

Bind the server to `0.0.0.0` inside the container. A server bound to `127.0.0.1` passes this healthcheck and is still
unreachable from every other service.