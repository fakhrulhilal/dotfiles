# Prints "owner/repo" when the URL points at a GitHub repository root, fails otherwise
github_repo() (
  path=${1#http://}
  path=${path#https://}
  case "${path%%/*}" in
    github.com|www.github.com) ;;
    *) exit 1 ;;
  esac

  path=${path#*/}
  path=${path%/}
  path=${path%.git}
  case "$path" in
    */*/*|/*|*/) exit 1 ;;
    ?*/?*) printf '%s\n' "$path" ;;
    *) exit 1 ;;
  esac
)

# Same line syntax as meta_parse, the URL has to be a GitHub repository root.
# Example: eval "github_asset $(github_parse "https://github.com/wakatime/macos-wakatime (format=zip)")"
github_parse() {
  set -- "$1" "$(printf '%s' "$1" | sed 's/^[[:space:]]*//; s/[[:space:]].*//')"
  if ! github_repo "$2" >/dev/null; then
    echo "❌ Not a GitHub repository URL: $2" >&2
    return 1
  fi

  meta_parse "$1"
}

# Prints the download URL of a GitHub release asset, resolved by meta_asset from the GitHub releases API.
# Usage: github_asset --url <https://github.com/owner/repo> [--os] [--version] [--arch] [--format] [meta_asset options]
#   --os:      macos (default), linux, windows
#   --version: latest (default) or a release tag, with or without the leading "v"
#   --arch:    arm64/aarch64 (default), x64/x86_64/amd64
#   --format:  file extension; dmg (default) on macos, tar.gz on linux, zip on windows
# Other options go to meta_asset as is. --query-url replaces the asset matching below, and an Authorization
# --header replaces the default one built from $GITHUB_TOKEN (or $GH_TOKEN).
github_asset() (
  url=""
  os=macos
  version=latest
  arch=arm64
  format=""
  rest=""
  has_query=""
  has_auth=""
  while [ $# -gt 0 ]; do
    if [ $# -lt 2 ]; then
      echo "❌ Missing value for $1" >&2
      exit 1
    fi

    case "$1" in
      --url) url=$2 ;;
      --os) os=$2 ;;
      --version) version=$2 ;;
      --arch) arch=$2 ;;
      --format) format=$2 ;;
      *)
        [ "$1" = --query-url ] && has_query=1
        case "$1:$2" in
          --header:[Aa]uthorization:*) has_auth=1 ;;
        esac
        rest="$rest $(_shell_quote "$1") $(_shell_quote "$2")"
        ;;
    esac
    shift 2
  done

  if ! repo=$(github_repo "$url"); then
    echo "❌ Not a GitHub repository URL: $url" >&2
    exit 1
  fi

  mac_re='mac|macos|darwin|osx|apple'
  win_re='win|windows|win32|win64'
  case "$os" in
    mac|macos|darwin|osx) os_re=$mac_re; other_os_re="$win_re|linux"; format=${format:-dmg} ;;
    linux) os_re='linux'; other_os_re="$mac_re|$win_re"; format=${format:-tar.gz} ;;
    win|windows) os_re=$win_re; other_os_re="$mac_re|linux"; format=${format:-zip} ;;
    *) echo "❌ Unknown OS: $os" >&2; exit 1 ;;
  esac

  arm_re='arm64|aarch64'
  x64_re='x64|x86_64|amd64|intel'
  case "$arch" in
    arm64|aarch64) arch_re=$arm_re; other_arch_re=$x64_re ;;
    x64|x86_64|amd64) arch_re=$x64_re; other_arch_re=$arm_re ;;
    *) echo "❌ Unknown arch: $arch" >&2; exit 1 ;;
  esac

  # Narrow down step by step, and only apply a step when it leaves something:
  # format -> OS named in file name (or at least no other OS) -> arch (or universal, or no other arch)
  # yq (mikefarah) takes values through env vars and Go RE2 regexes, "(?i)" makes them case-insensitive
  GH_FORMAT_RE="(?i)\\.$(printf '%s' "$format" | sed 's/\./\\./g')\$"
  GH_OS_RE="(?i)(^|[^a-z])($os_re)([^a-z]|\$)"
  GH_OTHER_OS_RE="(?i)(^|[^a-z])($other_os_re)([^a-z]|\$)"
  GH_ARCH_RE="(?i)($arch_re)"
  GH_OTHER_ARCH_RE="(?i)($other_arch_re)"
  export GH_FORMAT_RE GH_OS_RE GH_OTHER_OS_RE GH_ARCH_RE GH_OTHER_ARCH_RE
  query='
    [.assets[] | select(.name | test(strenv(GH_FORMAT_RE)))]
    | (map(select(.name | test(strenv(GH_OS_RE))))) as $by_os
    | (if ($by_os | length) > 0 then $by_os else map(select(.name | test(strenv(GH_OTHER_OS_RE)) | not)) end)
    | (map(select(.name | test(strenv(GH_ARCH_RE))))) as $by_arch
    | (map(select(.name | test("(?i)universal")))) as $universal
    | (if ($by_arch | length) > 0 then $by_arch
      elif ($universal | length) > 0 then $universal
      else map(select(.name | test(strenv(GH_OTHER_ARCH_RE)) | not))
      end)
    | .[0].browser_download_url'
  [ -n "$has_query" ] || rest=" --query-url $(_shell_quote "$query")$rest"
  if [ -z "$has_auth" ]; then
    token='$GITHUB_TOKEN'
    [ -z "$GITHUB_TOKEN" ] && [ -n "$GH_TOKEN" ] && token='$GH_TOKEN'
    rest=" --header $(_shell_quote "Authorization: Bearer $token")$rest"
  fi

  release() {
    eval "meta_asset --url \"\$1\" --os \"\$os\" --version \"\$version\" --arch \"\$arch\" --format \"\$format\" \
      --header 'Accept: application/vnd.github+json' $rest"
  }

  api="https://api.github.com/repos/$repo/releases"
  if [ "$version" = latest ]; then
    release "$api/latest"
  else
    release "$api/tags/v${version#v}" || release "$api/tags/${version#v}"
  fi
)