# Pitfalls (learned the hard way)

Each entry: the symptom you will see, the cause, and what to do. Check this list
when something in the build, CI or a release behaves unexpectedly.

## Toolchain and local builds

- **Symptom:** the pinned nightly in `rust-toolchain.toml` is ignored; cargo
  complains that `cargo-features` needs nightly.
  **Cause:** a global mise `rust = "latest"` exports `RUSTUP_TOOLCHAIN`, which
  overrides `rust-toolchain.toml`.
  **Fix:** keep `mise.toml` in the project pinning the same nightly (the
  template does). In a shell that was started before, export
  `RUSTUP_TOOLCHAIN=<pinned nightly>` explicitly.

- **Symptom:** `panic_immediate_abort is now a real panic strategy!`
  **Cause:** newer nightlies replaced the `panic_immediate_abort` std feature.
  **Fix:** use `panic = "immediate-abort"` in `[profile.dist]` plus
  `cargo-features = ["panic-immediate-abort"]` (the template does), and
  `-Zbuild-std-features=optimize_for_size` only.

- **Symptom:** a loop like `for a in "$A"` / `set -- $A` passes all words as one
  argument ("invalid reference format", "unknown option: -t 30 -L ...").
  **Cause:** zsh (the macOS default shell) does not word-split `$var`.
  **Fix:** run such snippets with `bash -c '...'`, or use `${=var}` in zsh.

- **Symptom:** `timeout: command not found` on macOS.
  **Fix:** start the process in the background and `kill` it, or use
  `gtimeout` if coreutils is installed.

## CI (GitHub Actions)

- **Symptom:** `permission denied` running a binary downloaded with
  `actions/download-artifact`.
  **Cause:** artifacts do not keep the executable bit.
  **Fix:** `chmod +x` after download (docker.yml does). The Dockerfile uses
  `COPY --chmod=0755`, so the image itself is fine.

- **Symptom:** `failed to connect to the docker API at npipe:////./pipe/docker_engine`
  on a `windows-2025` runner.
  **Cause:** the Docker service is occasionally not running on fresh Windows
  runners. Only matters if you build Windows images (the template does not).
  **Fix:** a pwsh step `Start-Service docker` and wait for `docker version`.

- **Symptom:** a test that asserts a timeout message passes on Linux/macOS but
  fails on Windows (`os error 10060`).
  **Cause:** socket timeouts are `WouldBlock` on Unix but `TimedOut` with a
  long OS message on Windows.
  **Fix:** map both kinds to one message in the I/O wrapper.

- **Symptom:** "Node.js 20 is deprecated" warnings.
  **Fix:** bump actions to their current majors (check each release's notes
  for breaking changes first). Some (e.g. `mlugg/setup-zig`) have no Node 24
  release yet; that warning can only be fixed upstream.

## Container images

- **Symptom:** registry pages show `unknown/unknown` next to the real
  platforms, and people think amd64 is missing.
  **Cause:** buildx attaches provenance/SBOM attestation manifests by default.
  **Fix:** `--provenance=false --sbom=false` (docker.yml does), and verify every
  pushed tag has exactly the expected platforms.

- **Symptom:** after `docker buildx imagetools create`, Docker Hub still lists
  `unknown/unknown` entries.
  **Cause:** on Docker Hub, imagetools carries over attestations linked to the
  source images. `docker manifest create` does not (it also keeps Windows
  `os.version`, which imagetools drops).
  **Fix:** use `docker manifest create --amend` + `docker manifest push --purge`
  when you must rewrite an index by hand.

- **Symptom:** Docker Hub tag deletion or repository description update returns
  HTTP 403.
  **Cause:** the Docker Hub access token has Read & Write only.
  **Fix:** the `DOCKERHUB_TOKEN` secret needs **Read, Write, Delete**. Never
  hide such a failure behind `continue-on-error`: report it as a warning.

- **Symptom:** you cannot push to or delete from ghcr.io locally with the
  user's token.
  **Cause:** fine-grained GitHub tokens cannot access the container registry.
  **Fix:** do registry maintenance inside a workflow (the job's `GITHUB_TOKEN`
  with `packages: write` can), e.g. a one-off `workflow_dispatch` workflow with
  a dry-run input, then delete that workflow.

- **Symptom:** a secret value (e.g. a username stored as a secret) makes a job
  output empty.
  **Cause:** GitHub drops job outputs that contain secret values.
  **Fix:** non-secret settings (Docker Hub username, image name) are repository
  *variables*; only the token is a secret.

- **Symptom:** a Dockerfile example breaks when copied.
  **Cause:** Dockerfiles do not allow trailing comments on instruction lines.
  **Fix:** put the comment on its own line.

## Releases and installs

- **Symptom:** `mise use github:owner/repo` says "no versions found" right after
  a release.
  **Cause:** mise's `minimum_release_age` (24h by default) hides new releases;
  mise also caches the version list.
  **Fix:** nothing is wrong with the release. Bypass with
  `mise use --minimum-release-age 0 ...` (and `mise cache clear` if a cached
  list is stale). The README template documents this.

- **Symptom:** re-running a failed job of a release run re-runs every job of a
  reusable workflow, including the one that uploads release assets.
  **Fix:** afterwards, verify the assets are unchanged (checksums) and still
  attested (`scripts/verify_release.py`).

- **Symptom:** a release went out before the user tried the change.
  **Fix:** tag only after the user explicitly says go. A bad release is
  public at once (images, `latest`, mise); fixing it costs another version.

## Testing claims

- A check that pipes through `tail`/`grep` reports the *pipe's* exit status,
  not the tool's, and can hide failures. Capture the output and the exit code
  separately before saying "all ok".
- Rebuild before testing. A `dist/` built before a fix makes a passing or
  failing test meaningless.
- Test against more than a few public sites. If the tool talks TLS, run a
  local `openssl s_server` matrix (certificate types × TLS versions × groups).
  Popular sites share one configuration (e.g. ECDSA + TLS 1.3) and hide gaps.
