#!/usr/bin/env bash
# Publishes a file-based app as Native AOT and packs it into a release archive that mise's github backend detects:
#   <out-dir>/<AssemblyName>-<target>.tar.gz (or .zip for Windows)
# The archive name uses the script's AssemblyName property (falls back to the file name). The files sit at the
# archive root, without debug symbols.
#
# Usage: package.sh <Script[.cs]> <rid> [out-dir]
#   rid: linux-x64 | win-x64 | win-x86 | osx-arm64
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <Script[.cs]> <rid> [out-dir]" >&2
  exit 2
fi

script="${1%.cs}.cs"
rid=$2
mkdir -p "${3:-dist}"
out=$(cd "${3:-dist}" && pwd)
cd "$(dirname "$0")"

case "$rid" in
  linux-x64) target=x86_64-unknown-linux-gnu ;;
  win-x64) target=x86_64-pc-windows-msvc ;;
  win-x86) target=i686-pc-windows-msvc ;;
  osx-arm64) target=aarch64-apple-darwin ;;
  *) echo "unsupported rid: $rid" >&2; exit 2 ;;
esac

# MSBuild evaluation honours every #:property, including ones from #:include'd files
prop() { dotnet build "$script" -getProperty:"$1" | tr -d '\r'; }
name=$(prop AssemblyName)
version=$(prop Version)
[[ -n "$name" ]] || name=$(basename "$script" .cs)

stage="$out/stage/$name-$target"
rm -rf "$stage"
dotnet publish -c Release -r "$rid" -p:CopyOutputSymbolsToPublishDirectory=false -o "$stage" "$script"
find "$stage" \( -name '*.dbg' -o -name '*.pdb' -o -name '*.dSYM' \) -prune -exec rm -rf {} +

if [[ $rid == win-* ]]; then
  exe=$name.exe
  archive=$name-$target.zip
  rm -f "$out/$archive"
  if command -v 7z > /dev/null; then
    (cd "$stage" && 7z a -tzip "$out/$archive" . > /dev/null)
  else
    (cd "$stage" && zip -qr "$out/$archive" .)
  fi
else
  exe=$name
  archive=$name-$target.tar.gz
  tar -czf "$out/$archive" -C "$stage" .
fi

echo "$name $version ($rid): $out/$archive"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  {
    echo "name=$name"
    echo "version=$version"
    echo "target=$target"
    echo "archive=$out/$archive"
    echo "exe=$stage/$exe"
  } >> "$GITHUB_OUTPUT"
fi