#!/usr/bin/env python3
"""Apply the rust-binary-project template to a Rust binary crate.

Creates missing files from assets/template (placeholders filled in) and adds
the missing pieces to Cargo.toml. Existing files are never overwritten unless
--force is given; files that differ from the template are reported (with
--diff, as a unified diff) so they can be merged by hand.

    apply_template.py --dest . --repo owner/name [--bin NAME] [--dry-run] [--diff]

Defaults come from Cargo.toml ([package] name/description/version), the git
remote (owner/name) and `git config user.name`. Run with --help for all
options. Prints a summary of what was created, kept, differs or was patched.
"""

import argparse
import datetime as dt
import difflib
import pathlib
import re
import stat
import subprocess
import sys
import tomllib

TEMPLATE = pathlib.Path(__file__).resolve().parent.parent / "assets" / "template"
DEFAULT_ICON = "https://cdn.jsdelivr.net/npm/lucide-static@1.48.0/icons/terminal.svg"
# Line-based files: missing lines are appended instead of reporting a conflict.
LINE_MERGED = {".gitignore", ".dockerignore"}

RELEASE_PROFILE = """[profile.release]
opt-level = "z"
lto = true
codegen-units = 1
panic = "abort"
strip = true
debug = false
incremental = false
"""

DIST_PROFILE = """# What scripts/build.sh ships: release plus a size-optimised std, built with
#   -Zbuild-std=std,panic_abort -Zbuild-std-features=optimize_for_size
# Panics abort without printing anything, so handle errors explicitly.
[profile.dist]
inherits = "release"
panic = "immediate-abort"
"""


def git(dest, *args):
    try:
        return subprocess.run(["git", "-C", str(dest), *args], capture_output=True, text=True, check=True).stdout.strip()
    except (subprocess.CalledProcessError, FileNotFoundError):
        return ""


def repo_from_remote(dest):
    url = git(dest, "remote", "get-url", "origin")
    m = re.search(r"github\.com[:/]([^/]+)/(.+?)(?:\.git)?$", url)
    return f"{m.group(1)}/{m.group(2)}" if m else ""


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--dest", default=".", help="project directory (must contain Cargo.toml)")
    p.add_argument("--bin", help="binary name [Cargo [package].name]")
    p.add_argument("--repo", help="GitHub owner/name [git remote origin]")
    p.add_argument("--dockerhub-image", help="Docker Hub image [<owner>/<bin>, lowercase]")
    p.add_argument("--description", help="one-line description [Cargo description]")
    p.add_argument("--author", help="copyright holder for LICENSE [git config user.name]")
    p.add_argument("--nightly", help="pinned toolchain, e.g. nightly-2026-09-24 [yesterday's nightly]")
    p.add_argument("--icon", default=DEFAULT_ICON, help="image icon URL (Docker Desktop / OrbStack labels)")
    p.add_argument("--sample-version", help="version used in README examples [Cargo version]")
    p.add_argument("--force", action="store_true", help="overwrite files that differ from the template")
    p.add_argument("--diff", action="store_true", help="print a unified diff for files that differ")
    p.add_argument("--dry-run", action="store_true", help="report only, write nothing")
    a = p.parse_args()

    dest = pathlib.Path(a.dest).resolve()
    cargo_path = dest / "Cargo.toml"
    if not cargo_path.is_file():
        sys.exit(f"{cargo_path} not found: run `cargo init --bin` first")
    cargo_text = cargo_path.read_text()
    pkg = tomllib.loads(cargo_text).get("package", {})

    bin_name = a.bin or pkg.get("name")
    repo = a.repo or repo_from_remote(dest)
    if not bin_name or not repo or "/" not in repo:
        sys.exit("need --bin and --repo owner/name (could not infer them)")
    owner = repo.split("/")[0]
    values = {
        "__BIN__": bin_name,
        "__REPO__": repo,
        "__REPO_LOWER__": repo.lower(),
        "__REPO_BADGE__": repo.replace("-", "--").replace("/", "%2F"),
        "__DOCKERHUB_IMAGE__": (a.dockerhub_image or f"{owner}/{bin_name}").lower(),
        "__DESCRIPTION__": a.description or pkg.get("description") or f"{bin_name} command-line tool.",
        "__AUTHOR__": a.author or git(dest, "config", "user.name") or owner,
        "__YEAR__": str(dt.date.today().year),
        "__NIGHTLY__": a.nightly or f"nightly-{dt.date.today() - dt.timedelta(days=1)}",
        "__ICON__": a.icon,
        "__SAMPLE_VERSION__": a.sample_version or pkg.get("version") or "0.1.0",
    }

    report = {"created": [], "merged": [], "unchanged": [], "differs": [], "overwritten": [], "cargo": []}
    for src in sorted(TEMPLATE.rglob("*")):
        if not src.is_file():
            continue
        rel = src.relative_to(TEMPLATE)
        text = src.read_text()
        for k, v in values.items():
            text = text.replace(k, v)
        dst = dest / rel
        if dst.exists() and rel.name in LINE_MERGED:
            current = dst.read_text()
            missing = [l for l in text.splitlines() if l.strip() and l not in current.splitlines()]
            if not missing:
                report["unchanged"].append(str(rel))
                continue
            report["merged"].append(f"{rel} (+{len(missing)} lines)")
            if not a.dry_run:
                dst.write_text(current.rstrip("\n") + "\n" + "\n".join(missing) + "\n")
            continue
        if dst.exists():
            current = dst.read_text()
            if current == text:
                report["unchanged"].append(str(rel))
                continue
            if not a.force:
                report["differs"].append(str(rel))
                if a.diff:
                    sys.stdout.writelines(difflib.unified_diff(
                        current.splitlines(True), text.splitlines(True), f"yours/{rel}", f"template/{rel}"))
                continue
            report["overwritten"].append(str(rel))
        else:
            report["created"].append(str(rel))
        if not a.dry_run:
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_text(text)
            if src.stat().st_mode & stat.S_IXUSR or rel.suffix == ".sh":
                dst.chmod(dst.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    # Cargo.toml: add only what is missing.
    new = cargo_text
    if "panic-immediate-abort" not in new:
        m = re.match(r'cargo-features\s*=\s*\[(.*?)\]\s*\n', new)
        if m:
            items = [i.strip() for i in m.group(1).split(",") if i.strip()] + ['"panic-immediate-abort"']
            new = f'cargo-features = [{", ".join(items)}]\n' + new[m.end():]
        else:
            new = 'cargo-features = ["panic-immediate-abort"]\n\n' + new
        report["cargo"].append("added cargo-features = [\"panic-immediate-abort\"] (nightly)")
    if "license" not in pkg and "license-file" not in pkg:
        new = re.sub(r"(\[package\][^\[]*?)(\n\n|\n(?=\[)|\Z)", lambda m: m.group(1) + '\nlicense = "MIT"' + m.group(2), new, count=1)
        report["cargo"].append('added license = "MIT"')
    if not re.search(r"^\[profile\.release\]", new, re.M):
        new = new.rstrip("\n") + "\n\n" + RELEASE_PROFILE
        report["cargo"].append("added [profile.release] (size-optimised)")
    else:
        section = re.search(r"^\[profile\.release\](.*?)(?=^\[|\Z)", new, re.M | re.S).group(1)
        missing = [l.split(" =")[0] for l in RELEASE_PROFILE.splitlines()[1:] if l.replace(" ", "") not in section.replace(" ", "")]
        if missing:
            report["cargo"].append(f"kept existing [profile.release], but it differs from the template in: {', '.join(missing)}")
    if not re.search(r"^\[profile\.dist\]", new, re.M):
        new = new.rstrip("\n") + "\n\n" + DIST_PROFILE
        report["cargo"].append("added [profile.dist] (immediate-abort)")
    if new != cargo_text and not a.dry_run:
        cargo_path.write_text(new)

    print(f"{'DRY RUN - ' if a.dry_run else ''}template applied to {dest}")
    for k, v in values.items():
        if k in ("__BIN__", "__REPO__", "__DOCKERHUB_IMAGE__", "__NIGHTLY__", "__AUTHOR__", "__SAMPLE_VERSION__"):
            print(f"  {k.strip('_').lower():16} {v}")
    for key, label in (("created", "created"), ("merged", "merged (missing lines appended)"), ("overwritten", "overwritten (--force)"), ("unchanged", "already identical"),
                       ("differs", "DIFFERS from template, kept yours (merge by hand; rerun with --diff)")):
        if report[key]:
            print(f"{label}:")
            for f in report[key]:
                print(f"  {f}")
    if report["cargo"]:
        print("Cargo.toml:")
        for c in report["cargo"]:
            print(f"  {c}")


if __name__ == "__main__":
    main()
