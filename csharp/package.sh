#!/usr/bin/env bash
# Publishes a file-based app as Native AOT once and packs that output two ways:
#   <out-dir>/<AssemblyName>-<target>.tar.gz (or .zip)  release archive that mise's github backend detects
#   <out-dir>/<PackageId>.<rid>.<Version>.nupkg          RID-specific .NET tool package
#   <out-dir>/<PackageId>.<Version>.nupkg                pointer package that `dotnet tool install` resolves to the RID
#                                                        package for the user's machine (--pointer only)
# The archive name uses the script's AssemblyName property (falls back to the file name), which is also the tool
# command. The files sit at the archive root, without debug symbols.
#
# Usage: package.sh [--pointer] <Script[.cs]> <rid> [out-dir]
#   rid: linux-x64 | win-x64 | win-x86 | osx-arm64
#   --pointer: also pack the pointer package; <rid> must then be this host's RID, because the SDK packs both at once.
set -euo pipefail

pointer=false
if [[ ${1:-} == --pointer ]]; then pointer=true; shift; fi
if [[ $# -lt 2 ]]; then
  echo "Usage: $0 [--pointer] <Script[.cs]> <rid> [out-dir]" >&2
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
id=$(prop PackageId)
[[ -n "$name" ]] || name=$(basename "$script" .cs)

# Every RID the pointer package lists. MSBuild splits a `-p:` value on ';', so it is passed from the environment.
export ToolPackageRuntimeIdentifiers='linux-x64;win-x64;win-x86;osx-arm64'
rid_args=(-r "$rid")
if $pointer; then
  host=$(prop NETCoreSdkPortableRuntimeIdentifier)
  [[ $host == "$rid" ]] || { echo "--pointer needs a $rid host, this one is $host" >&2; exit 2; }
  # Without -r, the SDK packs the pointer plus the RID packages this host can compile with Native AOT: its own.
  export RuntimeIdentifiers=$rid
  rid_args=()
fi

stage="$out/stage/$name-$target"
rm -rf "$stage"
mkdir -p "$out/stage"
publish_dir=$stage
# The package README is docs/<Script>.md with its relative links pointed at GitHub: ../csharp/... at the repository,
# Page.md at the wiki page that docs/ is published to.
doc=../docs/$(basename "$script" .cs).md
readme=
if [[ -f $doc ]]; then
  readme="$out/stage/$name-README.md"
  sed -E -e 's#\]\(\.\./#](https://github.com/fakhrulhilal/dotfiles/blob/master/#g' \
    -e 's#\]\(([A-Za-z0-9_-]+)\.md([#)])#](https://github.com/fakhrulhilal/dotfiles/wiki/\1\2#g' "$doc" > "$readme"
fi
# a native Windows dotnet can't read a Git Bash path inside -p:PublishDir=/d/...
if command -v cygpath > /dev/null; then
  publish_dir=$(cygpath -m "$stage")
  if [[ -n $readme ]]; then readme=$(cygpath -m "$readme"); fi
fi
# Pack publishes into PublishDir first, so the archive below reuses that build. The Web SDK turns packing off.
dotnet pack "$script" -c Release ${rid_args[@]+"${rid_args[@]}"} -o "$out" -p:PublishDir="$publish_dir/" \
  -p:IsPackable=true -p:CopyOutputSymbolsToPublishDirectory=false \
  -p:PackageReadmeSource="$readme" \
  -p:Authors="Fakhrulhilal Maktum" -p:PackageLicenseExpression=MIT \
  -p:RepositoryUrl="https://github.com/fakhrulhilal/dotfiles" -p:RepositoryType=git \
  -p:PackageProjectUrl="https://github.com/fakhrulhilal/dotfiles/tree/master/csharp"
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

nupkg=$id.$rid.$version.nupkg
packages=("$nupkg")
if $pointer; then packages+=("$id.$version.nupkg"); fi
for p in "${packages[@]}"; do
  [[ -f "$out/$p" ]] || { echo "$p was not created" >&2; exit 1; }
done

echo "$name $version ($rid): $out/$archive ${packages[*]}"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  {
    echo "name=$name"
    echo "id=$id"
    echo "version=$version"
    echo "target=$target"
    echo "archive=$out/$archive"
    echo "exe=$stage/$exe"
  } >> "$GITHUB_OUTPUT"
fi