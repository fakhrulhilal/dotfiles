# __BIN__

[![Release](https://img.shields.io/github/v/release/__REPO__?sort=semver&logo=github&label=release)](https://github.com/__REPO__/releases/latest)
[![Docker Hub](https://img.shields.io/docker/v/__DOCKERHUB_IMAGE__?sort=semver&logo=docker&label=Docker%20Hub)](https://hub.docker.com/r/__DOCKERHUB_IMAGE__)
[![ghcr.io](https://img.shields.io/badge/ghcr.io-__REPO_BADGE__-blue?logo=github)](https://github.com/__REPO__/pkgs/container/__BIN__)
[![Image size](https://img.shields.io/docker/image-size/__DOCKERHUB_IMAGE__?sort=semver&logo=docker&label=image%20size)](https://hub.docker.com/r/__DOCKERHUB_IMAGE__)
[![License](https://img.shields.io/github/license/__REPO__)](LICENSE)

__DESCRIPTION__

## Usage

```text
__BIN__ [OPTIONS] ...
```

<!-- Paste the output of `__BIN__ --help` here and describe the behaviour. -->

## Install

Each [release](https://github.com/__REPO__/releases/latest) has one archive
per platform, containing the `__BIN__` binary:

| Platform           | Asset                                      |
|--------------------|--------------------------------------------|
| Linux x86 64-bit   | `__BIN__-x86_64-unknown-linux-musl.tar.gz` |
| Linux x86 32-bit   | `__BIN__-i686-unknown-linux-musl.tar.gz`   |
| Windows x86 64-bit | `__BIN__-x86_64-pc-windows-msvc.zip`       |
| Windows x86 32-bit | `__BIN__-i686-pc-windows-msvc.zip`         |
| macOS arm64        | `__BIN__-aarch64-apple-darwin.tar.gz`      |

The Linux binaries are statically linked against musl, so the same file runs
on Debian, Ubuntu, Alpine, distroless or scratch — no libc needed from the
host. The x86 32-bit builds need a CPU with SSE2 (Pentium 4 or newer).
Windows builds link the C runtime statically. `SHA256SUMS` lists the checksums
of all assets.

### Manual download

Linux (use `i686` for 32-bit):

```sh
curl -fsSL https://github.com/__REPO__/releases/latest/download/__BIN__-x86_64-unknown-linux-musl.tar.gz \
  | sudo tar -xz -C /usr/local/bin __BIN__
```

macOS (Apple silicon):

```sh
curl -fsSL https://github.com/__REPO__/releases/latest/download/__BIN__-aarch64-apple-darwin.tar.gz \
  | sudo tar -xz -C /usr/local/bin __BIN__
```

Windows (PowerShell; use `i686` for 32-bit):

```powershell
$zip = "$env:TEMP\__BIN__.zip"
Invoke-WebRequest https://github.com/__REPO__/releases/latest/download/__BIN__-x86_64-pc-windows-msvc.zip -OutFile $zip
Expand-Archive $zip -DestinationPath "$env:LOCALAPPDATA\__BIN__" -Force
# then add %LOCALAPPDATA%\__BIN__ to PATH
```

### Verifying downloads

Every release archive, and every binary inside them, has a signed
[GitHub artifact attestation](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations)
(SLSA build provenance) proving it was built by this repository's release
workflow. mise checks it automatically on install. To check a file by hand
with the [GitHub CLI](https://cli.github.com):

```sh
gh attestation verify __BIN__-x86_64-unknown-linux-musl.tar.gz --repo __REPO__
gh attestation verify /usr/local/bin/__BIN__ --repo __REPO__
```

### mise

```sh
mise use -g github:__REPO__
```

mise picks the right asset for the OS and CPU, verifies its GitHub artifact
attestation, and puts `__BIN__` on the `PATH`. Or in `mise.toml`:

```toml
[tools]
"github:__REPO__" = "latest"
```

mise ignores releases younger than 24 hours by default (its
`minimum_release_age` setting), so right after a release it reports "no
versions found". To install a release published today:

```sh
mise use -g --minimum-release-age 0 github:__REPO__
```

### Docker

The image is published to
[Docker Hub](https://hub.docker.com/r/__DOCKERHUB_IMAGE__) (`__DOCKERHUB_IMAGE__`) and the
[GitHub Container Registry](https://github.com/__REPO__/pkgs/container/__BIN__)
(`ghcr.io/__REPO_LOWER__`), with the same tags on both, for:

| Platform    | Architecture                              |
|-------------|-------------------------------------------|
| linux/amd64 | x86 64-bit                                |
| linux/386   | x86 32-bit                                |
| linux/arm64 | ARM 64-bit (e.g. Docker on Apple silicon) |

The image is built `FROM scratch`: it contains `/bin/__BIN__` and nothing
else — no shell. Copy the binary into your own image — any Linux base works:

```dockerfile
# Any base works: debian, ubuntu, alpine, gcr.io/distroless/static, scratch, ...
FROM debian:trixie-slim
COPY --from=__DOCKERHUB_IMAGE__:latest /bin/__BIN__ /bin/__BIN__
```

The image also runs on its own (entrypoint `/bin/__BIN__`):

```sh
docker run --rm __DOCKERHUB_IMAGE__ --version
```

### Windows containers

There is no Windows image. Download the Windows x86 64-bit archive from the
[releases page](https://github.com/__REPO__/releases) into your image
instead; set `VERSION` to the release you want (`__SAMPLE_VERSION__` below is
only an example):

```dockerfile
# escape=`
FROM mcr.microsoft.com/windows/nanoserver:ltsc2022
ARG VERSION=__SAMPLE_VERSION__
USER ContainerAdministrator
ADD https://github.com/__REPO__/releases/download/v${VERSION}/__BIN__-x86_64-pc-windows-msvc.zip C:/__BIN__.zip
RUN mkdir C:\__BIN__ && tar -xf C:\__BIN__.zip -C C:\__BIN__ && del C:\__BIN__.zip
USER ContainerUser
```

## Build

### Requirements

- [rustup](https://rustup.rs). The nightly toolchain is pinned in
  `rust-toolchain.toml` and installed automatically (`rustup toolchain install`
  if your rustup does not). If you use mise, `mise.toml` pins the same toolchain
  (a global mise `rust` setting would otherwise override `rust-toolchain.toml`)
  plus zig.

### Development builds

```sh
cargo build --release        # -> target/release/__BIN__
cargo test
```

These use the prebuilt `std`, so the binary is bigger than the released one,
but it builds quickly.

### Release (size-optimised) builds

`scripts/build.sh <rust-target> [out-dir]` builds `__BIN__` for one target the
way releases are built (`--profile dist` plus `-Zbuild-std`, which rebuilds
`std` for size) into `dist/<rust-target>/`. Linux targets are built with
[cargo-zigbuild](https://github.com/rust-cross/cargo-zigbuild), which works
from macOS, Linux or Windows:

```sh
mise install                 # or install zig any other way
cargo install --locked cargo-zigbuild

scripts/build.sh x86_64-unknown-linux-musl
scripts/build.sh i686-unknown-linux-musl
scripts/build.sh aarch64-unknown-linux-musl  # Docker image only
scripts/build.sh aarch64-apple-darwin        # on a Mac
```

Windows targets (`x86_64-pc-windows-msvc`, `i686-pc-windows-msvc`) are built
on Windows with the MSVC build tools, running the script from Git Bash.

### Docker image

The Dockerfile does not compile anything; it packages the binaries from
`dist/`, so build those first:

```sh
scripts/build.sh x86_64-unknown-linux-musl
scripts/build.sh i686-unknown-linux-musl
scripts/build.sh aarch64-unknown-linux-musl
docker buildx build --load --platform linux/arm64 -t __BIN__ .
docker run --rm __BIN__ --version
```

## License

[MIT](LICENSE)
