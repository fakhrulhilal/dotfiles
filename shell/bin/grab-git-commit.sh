#!/bin/sh
# Find which git repositories mention the given keywords (e.g. issue keys) in
# their recent commit messages, and print a YAML report:
#
#   - "RepoName":
#       - "STORY-124"
#
# Query it later with yq, e.g.:
#   grab-git-commit.sh -k STORY-124,ISSUE-01 | yq '.[] | keys | .[]'
#   grab-git-commit.sh -k STORY-124,ISSUE-01 | yq '.[] | to_entries | .[] | select(.value[] == "STORY-124") | .key'

set -u

usage() {
  cat <<EOF
Usage: $0 -k KEYWORDS [options]

Options:
  -p, --path PATH          Folder holding the repositories, or a repository itself
                           (default: current directory)
  -k, --keywords LIST      Comma separated keywords to look for, e.g. STORY-124,ISSUE-01
  -b, --branches LIST      Comma separated branches to search (default: develop)
  -n, --limit-commit N     Number of latest commits per branch to look at (default: 100)
  -j, --jobs N             Repositories scanned in parallel (default: 8)
      --no-fetch           Do not fetch the branches from origin first
  -h, --help               Show this help

The YAML report goes to stdout; warnings go to stderr.
EOF
}

die() {
  printf 'error: %s\n' "$1" >&2
  exit 2
}

warn() {
  printf 'warn: %s\n' "$1" >&2
}

is_positive_int() {
  case "$1" in
    '' | *[!0-9]* | 0) return 1 ;;
    *) return 0 ;;
  esac
}

trim() {
  printf '%s' "$1" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//'
}

yaml_quote() {
  printf '"%s"' "$(printf '%s' "$1" | sed 's/[\\"]/\\&/g')"
}

# Escapes ERE metacharacters so a keyword is matched literally
regex_escape() {
  printf '%s' "$1" | sed 's/[][\\.*^$+?(){}|]/\\&/g'
}

path="."
keywords=""
branches="develop"
limit=100
jobs=8
fetch=1

while [ $# -gt 0 ]; do
  case "$1" in
    -p | --path) [ $# -ge 2 ] || die "$1 needs a value"; path="$2"; shift 2 ;;
    --path=*) path="${1#*=}"; shift ;;
    -k | --keywords) [ $# -ge 2 ] || die "$1 needs a value"; keywords="$2"; shift 2 ;;
    --keywords=*) keywords="${1#*=}"; shift ;;
    -b | --branches) [ $# -ge 2 ] || die "$1 needs a value"; branches="$2"; shift 2 ;;
    --branches=*) branches="${1#*=}"; shift ;;
    -n | --limit-commit) [ $# -ge 2 ] || die "$1 needs a value"; limit="$2"; shift 2 ;;
    --limit-commit=*) limit="${1#*=}"; shift ;;
    -j | --jobs) [ $# -ge 2 ] || die "$1 needs a value"; jobs="$2"; shift 2 ;;
    --jobs=*) jobs="${1#*=}"; shift ;;
    --no-fetch) fetch=0; shift ;;
    -h | --help) usage; exit 0 ;;
    *) usage >&2; die "unknown option: $1" ;;
  esac
done

[ -n "$keywords" ] || { usage >&2; die "--keywords is required"; }
[ -d "$path" ] || die "path not found: $path"
is_positive_int "$limit" || die "--limit-commit must be a positive integer: $limit"
is_positive_int "$jobs" || die "--jobs must be a positive integer: $jobs"
command -v git >/dev/null 2>&1 || die "git is not installed"

# Never block on a credential prompt while fetching in the background
GIT_TERMINAL_PROMPT=0
export GIT_TERMINAL_PROMPT

tmp="$(mktemp -d "${TMPDIR:-/tmp}/grab-git-commit.XXXXXX")" || die "cannot create temp folder"
trap 'rm -rf "$tmp"' EXIT
trap 'exit 130' INT TERM

# Writes the YAML entry of one repository to "$2.yml" when any keyword matches
scan_repo() {
  repo="$1"
  out="$2"
  name="$(basename "$(cd "$repo" && pwd)")"
  messages="$out.msg"
  : >"$messages"

  has_origin=0
  git -C "$repo" remote get-url origin >/dev/null 2>&1 && has_origin=1

  old_ifs="$IFS"
  IFS=,
  for branch in $branches; do
    IFS="$old_ifs"
    branch="$(trim "$branch")"
    [ -n "$branch" ] || continue

    if [ "$fetch" -eq 1 ] && [ "$has_origin" -eq 1 ]; then
      git -C "$repo" fetch --quiet origin "$branch" 2>/dev/null
    fi

    if git -C "$repo" rev-parse --verify --quiet "refs/remotes/origin/$branch" >/dev/null; then
      ref="refs/remotes/origin/$branch"
    elif git -C "$repo" rev-parse --verify --quiet "refs/heads/$branch" >/dev/null; then
      ref="refs/heads/$branch"
    else
      warn "$name: branch '$branch' not found, skipped"
      continue
    fi

    git -C "$repo" log -n "$limit" --format='%B' "$ref" >>"$messages" 2>/dev/null
  done
  IFS="$old_ifs"

  found=""
  IFS=,
  for keyword in $keywords; do
    IFS="$old_ifs"
    keyword="$(trim "$keyword")"
    [ -n "$keyword" ] || continue
    pattern="(^|[^[:alnum:]_])$(regex_escape "$keyword")([^[:alnum:]_]|$)"
    if grep -Eiq -- "$pattern" "$messages"; then
      found="$found    - $(yaml_quote "$keyword")
"
    fi
  done
  IFS="$old_ifs"

  if [ -n "$found" ]; then
    printf -- '- %s:\n%s' "$(yaml_quote "$name")" "$found" >"$out.yml"
  fi
}

# A path that is a repository itself is scanned alone, otherwise its subfolders are
if [ -e "$path/.git" ]; then
  set -- "$path"
else
  set -- "$path"/*/
fi

index=0
running=0
for dir in "$@"; do
  dir="${dir%/}"
  [ -d "$dir" ] && [ -e "$dir/.git" ] || continue

  index=$((index + 1))
  scan_repo "$dir" "$(printf '%s/%06d' "$tmp" "$index")" &
  running=$((running + 1))
  if [ "$running" -ge "$jobs" ]; then
    wait
    running=0
  fi
done
wait

report=""
for entry in "$tmp"/*.yml; do
  [ -f "$entry" ] && report="$report$(cat "$entry")
"
done

if [ -n "$report" ]; then
  printf '%s' "$report"
else
  printf '[]\n'
fi
