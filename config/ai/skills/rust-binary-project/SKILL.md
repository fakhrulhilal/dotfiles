---
name: rust-binary-project
description: Set up or align a Rust binary / CLI project with a standard release setup - size-optimised static builds for x86 32/64-bit Linux and Windows plus macOS arm64, release archives installable via direct download and mise with GitHub artifact attestations, a FROM-scratch Linux container image (linux/386, linux/amd64, linux/arm64) on ghcr.io and Docker Hub, GitHub Actions CI and release workflows, MIT license and a version-independent README. Use this whenever the user starts a new Rust CLI/tool, wants to publish, release, package, cross-compile or containerise a Rust binary, asks for GitHub Actions/CI/release workflows for a Rust project, wants it installable with mise, a smaller Rust binary, multi-arch Docker images for a Rust tool, or asks to bring an existing Rust repo "in line with the usual setup", even if they don't mention all of these parts.
---

# Rust binary project

A repeatable setup for small Rust command-line tools that ship as static
binaries and as a tiny container image. The template in `assets/template/` is
the reference implementation; `scripts/apply_template.py` applies it;
`scripts/verify_release.py` checks a published release;
`references/pitfalls.md` lists problems already hit once, with fixes.

## The standard (defaults)

Apply all of these unless the user opts out of a part.

| Area | Standard |
|---|---|
| Binary targets | `x86_64-unknown-linux-musl`, `i686-unknown-linux-musl` (static), `x86_64-pc-windows-msvc`, `i686-pc-windows-msvc` (static CRT), `aarch64-apple-darwin` |
| Container | Linux only, `FROM scratch`, `linux/amd64`, `linux/386`, `linux/arm64` (arm64 = Docker on Apple silicon; built from `aarch64-unknown-linux-musl`, which is *not* a release package) |
| Size | nightly pinned by date, `[profile.dist]` with `panic = "immediate-abort"`, `-Zbuild-std=std,panic_abort -Zbuild-std-features=optimize_for_size`, `opt-level = "z"`, LTO, strip; zig + cargo-zigbuild for Linux |
| Releases | one archive per target named `<bin>-<rust-target>.tar.gz/.zip` (mise auto-detects these), `SHA256SUMS`, signed build-provenance attestations verified before publishing, a check that all five packages exist |
| Images | tags `X.Y.Z`, `X.Y`, `X`, `latest` (no suffix tags) on ghcr.io and Docker Hub, no provenance/SBOM entries, OCI labels (title, description, version, revision, created, source, url, licenses, icons), index annotations, every tag verified to have every platform, image list appended to the GitHub release notes |
| License | MIT (`LICENSE`, `license = "MIT"`, image label) |
| README | changes only with features: `latest` in all examples, badges instead of tag lists, vertical tables, a Windows-container section (download the release zip, sample `VERSION` arg), build guide; no CI/workflow or exit-code sections |

Why these choices: health-check-style tools get copied into distroless and
scratch images, so static linking and a small size matter more than build
convenience; users install them with mise or by `curl | tar`, so asset names
must be predictable and verifiable; and the README is read by users, not
maintainers, so it must not go stale with every release.

## Working rules

- **Never commit, push, tag or publish until the user has tried the change and
  says go.** Stop at "built and tested locally", hand over the exact binary
  paths/commands to try, and wait. A release is public immediately (images,
  `latest`, mise); fixing a bad one costs another version.
- Verify claims with the real artifact before reporting them (see "Testing
  claims" in `references/pitfalls.md`). Report what was *not* tested (e.g.
  Windows-only parts on a Mac) explicitly.
- Keep the tool simple. It is usually a small utility: prefer `std` and hand
  written argument parsing over heavy crates unless the user wants them.

## Workflow

### 1. Inspect

- **New project:** `cargo new --bin <name>` (edition 2024), then continue.
- **Existing project:** read `Cargo.toml`, `git status`, existing
  `.github/workflows`, Dockerfile and README first. Do not overwrite the user's
  work: run the script with `--dry-run --diff` and explain what differs.

### 2. Apply the template

```sh
python3 <skill>/scripts/apply_template.py --dest <project> --repo <owner>/<name> \
  [--dockerhub-image <user>/<image>] [--description "..."] [--nightly nightly-YYYY-MM-DD]
```

- Defaults are inferred from `Cargo.toml`, the git remote and `git config
  user.name`. Ask the user for the GitHub `owner/name` and the Docker Hub image
  if they cannot be inferred — never guess a registry namespace.
- It creates missing files, appends missing lines to `.gitignore` and
  `.dockerignore`, adds only the missing parts of `Cargo.toml`
  (`cargo-features`, `license`, `[profile.release]`, `[profile.dist]`), and
  reports files that differ. `--force` overwrites; use it only when the user
  agrees.
- Pick a nightly that exists and works (`rustup toolchain install
  nightly-YYYY-MM-DD --profile minimal -c rust-src,rustfmt,clippy`). The
  script pins yesterday's nightly by default.

### 3. Make the binary fit the pipeline

- Support `--version` / `-V` (printing `<bin> <version>`) and `--help`: CI and
  the image tests run `--version` on every target and platform.
- Panics print nothing with `immediate-abort`, so turn every failure into a
  clear error message and a non-zero exit code.
- Generate `Cargo.lock` (`cargo generate-lockfile`) and commit it: every
  workflow uses `--locked`.

### 4. Adapt the project-specific parts

- `build.yml` → "Smoke test": add real checks (actual inputs, network calls)
  beyond `--version`; for TLS clients, add a local `openssl s_server` matrix.
- `docker.yml` → "Test": extend if the image needs more than `--version`.
- README: fill in "Usage" from the real `--help` output and describe the
  behaviour; keep examples on `latest`.
- If the user changes the target set, update all four places together: the
  `build.yml` matrix, the release "Check all packages" list, the Dockerfile
  stages and `PLATFORMS` in `docker.yml`.

### 5. Verify locally (before asking for a go)

```sh
export RUSTUP_TOOLCHAIN=<pinned nightly>     # if a global mise rust overrides the pin
cargo fmt --check && cargo clippy --locked --all-targets -- -D warnings && cargo test --locked
scripts/build.sh aarch64-apple-darwin          # native (on a Mac)
scripts/build.sh x86_64-unknown-linux-musl     # needs zig + cargo-zigbuild
scripts/build.sh i686-unknown-linux-musl
scripts/build.sh aarch64-unknown-linux-musl
docker buildx build --load --platform linux/arm64 -t <bin>:local . && docker run --rm <bin>:local --version
actionlint && shellcheck scripts/*.sh
```

Run the Linux binaries in `linux/amd64`, `linux/386` and `linux/arm64`
containers (e.g. `alpine` and `debian` with the binary mounted), not just the
native one. Windows builds can only be checked by CI unless a Windows host is
available — say so.

### 6. Repository settings (tell the user; they are not in the template)

- The GitHub repository **description** is used as the image description; the
  image job fails if it is empty.
- Docker Hub: repository **variable** `DOCKERHUB_USERNAME` (turns Docker Hub
  on), optional variable `DOCKERHUB_IMAGE`, and **secret** `DOCKERHUB_TOKEN`
  with **Read, Write, Delete** permission (Read & Write cannot update the
  Docker Hub description or delete tags).
- Attestations need a public repository (or GitHub Enterprise Cloud).

### 7. Release (only after the user says go)

1. Bump `version` in `Cargo.toml` (and the lock file via `cargo check`), commit,
   push, and wait for CI to pass.
2. `git tag -a vX.Y.Z -m "<bin> X.Y.Z" && git push origin vX.Y.Z`; watch the
   release run (`gh run watch <id> --exit-status`).
3. Check a `continue-on-error` step's real outcome in the log (e.g. the Docker
   Hub description step warns on HTTP 403) — a green job can hide it.
4. Verify what users get:

   ```sh
   python3 <skill>/scripts/verify_release.py --repo <owner>/<name> --version X.Y.Z \
     --image ghcr.io/<owner>/<name> --image docker.io/<user>/<image>
   ```

   Then pull and run `latest` on all three platforms, and install with
   `mise use --minimum-release-age 0 github:<owner>/<name>` in a scratch
   directory (mise hides releases younger than 24h by default).

## Opting out

- **No container:** delete `Dockerfile`, `.dockerignore`, `docker.yml`,
  `scripts/docker-tags.sh`, the `docker`/`release-notes` jobs in `release.yml`
  and `ci.yml`, the Docker badges and README sections, and the
  `aarch64-unknown-linux-musl` matrix entry.
- **Stable Rust instead of size optimisation:** use `channel = "stable"`,
  drop `cargo-features`, `[profile.dist]` and the `-Zbuild-std` flags in
  `scripts/build.sh` (build with `--release`, copy from `target/<t>/release`).
- **No attestations:** drop the attest and verify steps and the
  `id-token`/`attestations`/`artifact-metadata` permissions in `release.yml`,
  and the README "Verifying downloads" section.
