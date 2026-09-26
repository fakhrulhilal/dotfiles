#!/usr/bin/env python3
"""Verify a published release of a rust-binary-project repository.

Checks what users actually get, not just that CI passed:
  - the GitHub release has every package (x86 64/32-bit Linux and Windows,
    macOS arm64) plus SHA256SUMS, and the checksums match
  - every package has a GitHub artifact attestation from release.yml
  - every image tag (<version>, <major>.<minor>, <major>, latest) on every
    registry has exactly the expected platforms, and the version label matches

    verify_release.py --repo owner/name --version 1.2.3 [--bin NAME]
                      [--image ghcr.io/owner/name --image docker.io/user/name]

Needs `gh` (authenticated, e.g. GH_TOKEN) for the release and attestations.
Registries are queried anonymously, exactly as the public sees them.
Exit code 0 when everything passes.
"""

import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys
import tempfile
import urllib.request

PACKAGES = [
    "x86_64-unknown-linux-musl.tar.gz",
    "i686-unknown-linux-musl.tar.gz",
    "x86_64-pc-windows-msvc.zip",
    "i686-pc-windows-msvc.zip",
    "aarch64-apple-darwin.tar.gz",
]
PLATFORMS = {"linux/amd64", "linux/386", "linux/arm64"}
ACCEPT = ", ".join([
    "application/vnd.oci.image.index.v1+json",
    "application/vnd.docker.distribution.manifest.list.v2+json",
    "application/vnd.oci.image.manifest.v1+json",
    "application/vnd.docker.distribution.manifest.v2+json",
])

failures = []


def check(ok, what):
    print(f"  {'ok  ' if ok else 'FAIL'}  {what}")
    if not ok:
        failures.append(what)


def get_json(url, token=None, accept=None):
    req = urllib.request.Request(url)
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    if accept:
        req.add_header("Accept", accept)
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.load(r)


def registry(image):
    """Returns (base URL, anonymous pull token) for ghcr.io or Docker Hub."""
    host, _, repo = image.partition("/")
    if host == "ghcr.io":
        token = get_json(f"https://ghcr.io/token?scope=repository:{repo}:pull")["token"]
        return f"https://ghcr.io/v2/{repo}", token
    if host in ("docker.io", "index.docker.io"):
        token = get_json(f"https://auth.docker.io/token?service=registry.docker.io&scope=repository:{repo}:pull")["token"]
        return f"https://registry-1.docker.io/v2/{repo}", token
    sys.exit(f"unsupported registry in {image}")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--repo", required=True, help="GitHub owner/name")
    p.add_argument("--version", required=True, help="released version, with or without the leading v")
    p.add_argument("--bin", help="binary name [repository name]")
    p.add_argument("--image", action="append", default=[], help="image to check (repeatable) [ghcr.io/<repo>]")
    a = p.parse_args()

    version = a.version.lstrip("v")
    tag = f"v{version}"
    bin_name = a.bin or a.repo.split("/")[1]
    images = a.image or [f"ghcr.io/{a.repo.lower()}"]

    print(f"== GitHub release {tag}")
    with tempfile.TemporaryDirectory() as tmp:
        dl = subprocess.run(["gh", "release", "download", tag, "-R", a.repo, "-D", tmp], capture_output=True, text=True)
        check(dl.returncode == 0, f"download release assets ({dl.stderr.strip() or 'gh release download'})")
        files = {f.name for f in pathlib.Path(tmp).iterdir()}
        expected = {f"{bin_name}-{p}" for p in PACKAGES} | {"SHA256SUMS"}
        for name in sorted(expected):
            check(name in files, f"asset {name}")
        for extra in sorted(files - expected):
            print(f"  note  unexpected asset {extra}")
        sums = pathlib.Path(tmp, "SHA256SUMS")
        if sums.exists():
            for line in sums.read_text().splitlines():
                digest, name = line.split(maxsplit=1)
                path = pathlib.Path(tmp, name.lstrip("*"))
                check(path.exists() and hashlib.sha256(path.read_bytes()).hexdigest() == digest, f"checksum {path.name}")
        for name in sorted(f for f in files if f != "SHA256SUMS"):
            v = subprocess.run(["gh", "attestation", "verify", str(pathlib.Path(tmp, name)), "--repo", a.repo,
                                "--signer-workflow", f"{a.repo}/.github/workflows/release.yml"], capture_output=True, text=True)
            check(v.returncode == 0, f"attestation {name}")

    tags = [version]
    m = re.fullmatch(r"(\d+)\.(\d+)\.\d+", version)
    if m:
        tags += [f"{m[1]}.{m[2]}", m[1], "latest"]
    for image in images:
        print(f"== {image}")
        base, token = registry(image)
        for t in tags:
            try:
                idx = get_json(f"{base}/manifests/{t}", token, ACCEPT)
            except urllib.error.HTTPError as e:
                check(False, f"{t}: HTTP {e.code}")
                continue
            got = {f"{e['platform']['os']}/{e['platform']['architecture']}" for e in idx.get("manifests", [])}
            check(got == PLATFORMS, f"{t}: platforms {' '.join(sorted(got))}")
        idx = get_json(f"{base}/manifests/{version}", token, ACCEPT)
        first = get_json(f"{base}/manifests/{idx['manifests'][0]['digest']}", token, ACCEPT)
        labels = get_json(f"{base}/blobs/{first['config']['digest']}", token)["config"].get("Labels") or {}
        check(labels.get("org.opencontainers.image.version") == version,
              f"label org.opencontainers.image.version = {labels.get('org.opencontainers.image.version')}")
        for key in ("title", "description", "source", "licenses", "revision", "created"):
            check(f"org.opencontainers.image.{key}" in labels, f"label org.opencontainers.image.{key}")

    print(f"\n{'ALL CHECKS PASSED' if not failures else f'{len(failures)} CHECK(S) FAILED'}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
