#!/bin/sh
# Publish .NET apps as Native AOT and print a Markdown report of where the binary size goes:
# one size row per app (with the step from the previous app) and a per-assembly breakdown
# taken from the ILCompiler map file.
#
#   analyze-dotnet-binary.sh -i csharp/ClockifyCli.cs -o report.md
#   analyze-dotnet-binary.sh -i csharp/ClockifyCli.cs -p InvariantGlobalization=true
#   analyze-dotnet-binary.sh -b none -i s0_blank.cs -i s1_http.cs -i s2_json.cs   # cumulative stages
#
# See docs/dotnet-binary-size-optimization.md for how to read the numbers.

set -u

usage() {
  cat <<EOF
Usage: $0 -i APP [-i APP ...] [options]

Options:
  -i, --input APP            App to publish: a file-based app (.cs), a project file (.csproj) or a folder
                             holding one project. Repeat it to compare apps; each row then shows the step
                             from the previous one, so pass them in order
  -o, --output FILE          Write the Markdown report to FILE (default: stdout)
  -p, --property NAME=VALUE  Extra MSBuild property for every publish (repeatable)
  -r, --runtime RID          Target runtime, e.g. linux-x64 (default: current machine)
  -b, --breakdown MODE       Per-assembly breakdown for: last, all or none (default: last)
  -t, --top N                Assemblies listed per breakdown, the rest are summed (default: 25)
  -k, --keep DIR             Keep publish output, logs and map files in DIR
  -h, --help                 Show this help

Progress and build errors go to stderr.
EOF
}

die() {
  printf 'error: %s\n' "$1" >&2
  exit 2
}

info() {
  printf '%s\n' "$1" >&2
}

is_positive_int() {
  case "$1" in
    '' | *[!0-9]* | 0) return 1 ;;
    *) return 0 ;;
  esac
}

# Prints a byte count as B, KB or MB (1024 based)
human() {
  awk -v n="$1" 'BEGIN {
    sign = n < 0 ? "-" : ""; a = n < 0 ? -n : n
    if (a >= 1048576) printf "%s%.2f MB", sign, a / 1048576
    else if (a >= 1024) printf "%s%d KB", sign, a / 1024 + 0.5
    else printf "%s%d B", sign, a
  }'
}

inputs=""
output=""
properties=""
runtime=""
breakdown="last"
top=25
keep=""

while [ $# -gt 0 ]; do
  case "$1" in
    -i | --input) [ $# -ge 2 ] || die "$1 needs a value"; inputs="$inputs$2
"; shift 2 ;;
    --input=*) inputs="$inputs${1#*=}
"; shift ;;
    -o | --output) [ $# -ge 2 ] || die "$1 needs a value"; output="$2"; shift 2 ;;
    --output=*) output="${1#*=}"; shift ;;
    -p | --property) [ $# -ge 2 ] || die "$1 needs a value"; properties="$properties$2
"; shift 2 ;;
    --property=*) properties="$properties${1#*=}
"; shift ;;
    -r | --runtime) [ $# -ge 2 ] || die "$1 needs a value"; runtime="$2"; shift 2 ;;
    --runtime=*) runtime="${1#*=}"; shift ;;
    -b | --breakdown) [ $# -ge 2 ] || die "$1 needs a value"; breakdown="$2"; shift 2 ;;
    --breakdown=*) breakdown="${1#*=}"; shift ;;
    -t | --top) [ $# -ge 2 ] || die "$1 needs a value"; top="$2"; shift 2 ;;
    --top=*) top="${1#*=}"; shift ;;
    -k | --keep) [ $# -ge 2 ] || die "$1 needs a value"; keep="$2"; shift 2 ;;
    --keep=*) keep="${1#*=}"; shift ;;
    -h | --help) usage; exit 0 ;;
    *) usage >&2; die "unknown option: $1" ;;
  esac
done

[ -n "$inputs" ] || { usage >&2; die "--input is required"; }
case "$breakdown" in
  last | all | none) ;;
  *) die "--breakdown must be last, all or none: $breakdown" ;;
esac
is_positive_int "$top" || die "--top must be a positive integer: $top"
command -v dotnet >/dev/null 2>&1 || die "dotnet is not installed"

# The inputs become the positional parameters from here on
set --
while IFS= read -r app; do
  [ -n "$app" ] && set -- "$@" "$app"
done <<EOF
$inputs
EOF
for app in "$@"; do
  [ -e "$app" ] || die "app not found: $app"
done

if [ -n "$keep" ]; then
  mkdir -p "$keep" || die "cannot create $keep"
  work="$(cd "$keep" && pwd)"
else
  work="$(mktemp -d "${TMPDIR:-/tmp}/analyze-dotnet-binary.XXXXXX")" || die "cannot create temp folder"
  trap 'rm -rf "$work"' EXIT
fi
trap 'exit 130' INT TERM

if [ -n "$output" ]; then
  output_dir="$(dirname "$output")"
  [ -d "$output_dir" ] || die "output folder not found: $output_dir"
fi

# Publishes app $1 into folder $2 with the map file in $2/native; returns the publish exit code
publish() {
  dir="$2"
  rm -rf "$dir"
  mkdir -p "$dir"
  set -- "$1" -c Release -o "$dir/out" \
    -p:CopyOutputSymbolsToPublishDirectory=false \
    -p:IlcGenerateMapFile=true \
    "-p:NativeIntermediateOutputPath=$dir/native/"
  [ -n "$runtime" ] && set -- "$@" -r "$runtime"
  while IFS= read -r property; do
    [ -n "$property" ] && set -- "$@" "-p:$property"
  done <<EOF
$properties
EOF
  dotnet publish "$@" >"$dir/publish.log" 2>&1
}

# Prints the size of the largest file in the publish folder, which is the native binary
binary_size() {
  find "$1" -maxdepth 1 -type f ! -name '*.dbg' ! -name '*.pdb' -exec wc -c {} + 2>/dev/null \
    | awk '$2 != "total" && $1 > max { max = $1 } END { print max + 0 }'
}

# Prints "A<TAB>bytes<TAB>assembly<TAB>source", "K<TAB>bytes<TAB>kind" and "R<TAB>runtime pack" lines from map
# file $2, using the assembly list in rsp file $1. The source is "app", ".NET", "runtime" or "nuget:<id>@<version>".
aggregate() {
  awk -v rsp="$1" '
    # The first input in the rsp is the app itself; references come from the runtime pack or NuGet packages
    function source_of(path, is_app,    parts, n, i) {
      if (is_app) return "app"
      n = split(path, parts, "/")
      for (i = n - 1; i >= 2; i--) {
        if (parts[i] ~ /^[0-9]+\.[0-9]+/ && parts[i + 1] ~ /^(lib|runtimes|ref|tools|build|analyzers)$/) {
          if (parts[i - 1] ~ /^microsoft\.(netcore|aspnetcore|windowsdesktop)\.app\./ \
            || parts[i - 1] ~ /(^|\.)microsoft\.dotnet\.ilcompiler($|\.)/) {
            if (parts[i - 1] ~ /^microsoft\.netcore\.app\.runtime\./) runtime_pack = parts[i - 1] " " parts[i]
            return ".NET"
          }
          return "nuget:" parts[i - 1] "@" parts[i]
        }
      }
      return ".NET"
    }
    function add_assembly(path, is_app,    name, key) {
      sub(/^-r:/, "", path)
      gsub(/\\/, "/", path)
      name = path
      sub(/.*\//, "", name)
      sub(/\.(dll|exe)$/, "", name)
      key = name
      sub(/^System\.Private\./, "S.P.", key)
      gsub(/\./, "_", key)
      if (key in assembly_of) return
      assembly_of[key] = name
      source[name] = source_of(path, is_app)
      keys[++key_count] = key
    }
    BEGIN {
      app_seen = 0
      while ((getline line < rsp) > 0) {
        sub(/\r$/, "", line)
        if (line ~ /^-r:/) add_assembly(line, 0)
        else if (line !~ /^-/ && line ~ /\.(dll|exe)$/) { add_assembly(line, !app_seen); app_seen = 1 }
      }
      close(rsp)
      # Longest key first, so System_Net_Http_Json wins over System_Net_Http
      for (i = 2; i <= key_count; i++) {
        k = keys[i]
        for (j = i - 1; j >= 1 && length(keys[j]) < length(k); j--) keys[j + 1] = keys[j]
        keys[j + 1] = k
      }
      wrapper = "^(_ZTV[0-9]+|__writableData|__GCSTATICS|__NONGCSTATICS|__TypeThreadStaticIndex|__ThreadStaticIndex_" \
        "|__GetGCStaticBase_|__GetNonGCStaticBase_|__GenericDict_|__GenericInstance_|__DelegateCtor_" \
        "|__InterfaceDispatchMap_|__SealedVTable_|__GenericLookupFromType_|__GenericLookupFromDict_" \
        "|__MethodGenericDict_|__ReadyToRunGenericHelper_|__eagerCctor_|__PInvoke_|__DynamicInvoke_" \
        "|__Array<|_associatedData_|<Boxed>|Boxed_|unbox_)+"
      runtime = "(runtime tables and other)"
      source[runtime] = "runtime"
    }
    match($0, /^ *<[A-Za-z]+ Name="[^"]*" Length="[0-9]+"/) {
      node = substr($0, RSTART, RLENGTH)
      kind = node
      sub(/^ *</, "", kind)
      sub(/ .*/, "", kind)
      name = node
      sub(/^[^"]*"/, "", name)
      sub(/".*/, "", name)
      length_text = node
      sub(/.*Length="/, "", length_text)
      bytes = length_text + 0
      gsub(/&lt;/, "<", name)
      gsub(/&gt;/, ">", name)
      gsub(/&amp;/, "\\&", name)
      sub(wrapper, "", name)

      owner = runtime
      for (i = 1; i <= key_count; i++) {
        if (index(name, keys[i]) == 1) {
          owner = assembly_of[keys[i]]
          break
        }
      }
      by_assembly[owner] += bytes
      by_kind[kind] += bytes
    }
    END {
      for (a in by_assembly) printf "A\t%d\t%s\t%s\n", by_assembly[a], a, source[a]
      for (k in by_kind) printf "K\t%d\t%s\n", by_kind[k], k
      if (runtime_pack != "") printf "R\t%s\n", runtime_pack
    }
  ' "$2"
}

# Prints the Markdown breakdown of one app from its aggregate file $2
print_breakdown() {
  printf '\n## Breakdown: %s\n' "$1"
  awk -F '\t' -v top="$top" '
    function human(n) {
      if (n >= 1048576) return sprintf("%.2f MB", n / 1048576)
      if (n >= 1024) return sprintf("%d KB", n / 1024 + 0.5)
      return sprintf("%d B", n)
    }
    function share(n) { return sprintf("%.1f%%", 100 * n / total) }
    # Sorts names[1..count] by size[name], largest first
    function sort_by_size(names, count, size,    i, j, v) {
      for (i = 2; i <= count; i++) {
        v = names[i]
        for (j = i - 1; j >= 1 && size[names[j]] < size[v]; j--) names[j + 1] = names[j]
        names[j + 1] = v
      }
    }
    # Table rows are tab separated cells in rows[1..row_count]; align holds "l" or "r" per column
    function table_start(header, alignment) { row_count = 0; align = alignment; add_row(header) }
    function add_row(cells) { rows[++row_count] = cells }
    function table_print(    r, c, n, cell, width, line, pad) {
      for (r = 1; r <= row_count; r++) {
        n = split(rows[r], cell, "\t")
        for (c = 1; c <= n; c++) if (length(cell[c]) > width[c]) width[c] = length(cell[c])
      }
      for (r = 1; r <= row_count; r++) {
        n = split(rows[r], cell, "\t")
        line = "|"
        for (c = 1; c <= n; c++) {
          pad = width[c] - length(cell[c])
          if (substr(align, c, 1) == "r" && r > 1) line = line " " sprintf("%" pad "s", "") cell[c] " |"
          else line = line " " cell[c] sprintf("%" pad "s", "") " |"
        }
        print line
        if (r == 1) {
          line = "|"
          for (c = 1; c <= n; c++) line = line sprintf("%" (width[c] + 2) "s", "") "|"
          gsub(/ /, "-", line)
          print line
        }
      }
      delete width
      print ""
    }
    function source_label(src) {
      if (src == "app") return "App code"
      if (src == ".NET") return ".NET libraries"
      if (src == "runtime") return ".NET runtime tables and other"
      return "NuGet packages"
    }
    $1 == "A" {
      total += $2
      assembly_size[$3] = $2
      assembly_source[$3] = $4
      assemblies[++assembly_count] = $3
      group = source_label($4)
      if (!(group in group_size)) groups[++group_count] = group
      group_size[group] += $2
      if ($4 ~ /^nuget:/) {
        pkg = substr($4, 7)
        if (!(pkg in package_size)) packages[++package_count] = pkg
        package_size[pkg] += $2
        package_assemblies[pkg] = package_assemblies[pkg] (package_assemblies[pkg] == "" ? "" : ", ") $3
      }
    }
    $1 == "K" { kind_size[$3] = $2; kinds[++kind_count] = $3 }
    $1 == "R" { runtime_pack = $2 }
    END {
      print ""
      if (runtime_pack != "") printf "Runtime pack: `%s`.\n", runtime_pack
      printf "Mapped total: %s. The map counts some runtime data before compression, ", human(total)
      print "so it is larger than the binary.\n"

      print "### By source\n"
      sort_by_size(groups, group_count, group_size)
      table_start("Source\tSize\tShare", "lrr")
      for (i = 1; i <= group_count; i++) {
        g = groups[i]
        add_row(g "\t" human(group_size[g]) "\t" share(group_size[g]))
      }
      table_print()

      print "### NuGet packages\n"
      if (package_count == 0) print "None.\n"
      else {
        sort_by_size(packages, package_count, package_size)
        table_start("Package\tVersion\tAssemblies\tSize\tShare", "lllrr")
        for (i = 1; i <= package_count; i++) {
          pkg = packages[i]
          at = index(pkg, "@")
          add_row(substr(pkg, 1, at - 1) "\t" substr(pkg, at + 1) "\t" package_assemblies[pkg] \
            "\t" human(package_size[pkg]) "\t" share(package_size[pkg]))
        }
        table_print()
        print "A package that is mostly a source generator only counts its small runtime assembly here; the code it"
        print "generates is part of the app code.\n"
      }

      print "### Per assembly\n"
      sort_by_size(assemblies, assembly_count, assembly_size)
      table_start("Assembly\tSource\tSize\tShare", "llrr")
      for (i = 1; i <= assembly_count; i++) {
        a = assemblies[i]
        src = assembly_source[a]
        label = src ~ /^nuget:/ ? "NuGet " substr(src, 7, index(src, "@") - 7) : source_label(src)
        if (i <= top) add_row(a "\t" label "\t" human(assembly_size[a]) "\t" share(assembly_size[a]))
        else { rest += assembly_size[a]; rest_count++ }
      }
      if (rest_count) add_row(rest_count " other assemblies\t\t" human(rest) "\t" share(rest))
      table_print()

      print "### By node kind\n"
      sort_by_size(kinds, kind_count, kind_size)
      table_start("Kind\tSize\tShare", "lrr")
      for (i = 1; i <= kind_count && i <= 8; i++) {
        k = kinds[i]
        add_row(k "\t" human(kind_size[k]) "\t" share(kind_size[k]))
      }
      table_print()
    }
  ' "$2"
}

count=$#
index=0
rows=""
previous=""
failed=0
for app in "$@"; do
  index=$((index + 1))
  label="$(basename "$app")"
  dir="$work/$(printf '%02d' "$index")-$label"
  info "[$index/$count] publishing $app"

  if ! publish "$app" "$dir"; then
    info "publish failed for $app, last lines of $dir/publish.log:"
    tail -n 20 "$dir/publish.log" >&2
    rows="$rows| $label | failed | | |
"
    failed=1
    previous=""
    continue
  fi

  size="$(binary_size "$dir/out")"
  warnings="$(grep -Eo 'warning (IL|AOT)[0-9]+[^[]*' "$dir/publish.log" | sort -u | wc -l | tr -d ' ')"
  if [ -n "$previous" ]; then
    step="$(human $((size - previous)))"
    case "$step" in -*) ;; *) step="+$step" ;; esac
  else
    step="(first)"
  fi
  rows="$rows| $label | $(human "$size") ($size B) | $step | $warnings |
"
  previous="$size"

  if [ "$breakdown" = all ] || { [ "$breakdown" = last ] && [ "$index" -eq "$count" ]; }; then
    map="$(find "$dir/native" -name '*.map.xml' | head -n 1)"
    rsp="$(find "$dir/native" -name '*.ilc.rsp' | head -n 1)"
    if [ -n "$map" ] && [ -n "$rsp" ]; then
      aggregate "$rsp" "$map" >"$dir/breakdown.tsv"
    else
      info "no map file for $app; is PublishAot disabled?"
    fi
  fi
done

if [ -n "$output" ]; then
  exec >"$output" || die "cannot write $output"
fi

printf '# .NET binary size report\n\n'
printf -- '- Runtime: %s\n' "${runtime:-$(dotnet --info 2>/dev/null | awk '/RID:/ { print $2; exit }')}"
printf -- '- SDK: %s\n' "$(dotnet --version 2>/dev/null)"
if [ -n "$properties" ]; then
  extra="$(printf '%s' "$properties" | awk 'NF { printf "%s`%s`", sep, $0; sep = ", " }')"
  printf -- '- Extra properties: %s\n' "$extra"
fi
printf '\n## Size\n\n'
{
  printf '| App | Binary | Step | Trim/AOT warnings |\n'
  printf '|---|---|---|---|\n'
  printf '%s' "$rows"
} | awk -F '|' '
  # Pads every column to its widest cell
  { lines[NR] = $0; for (i = 2; i < NF; i++) { cell = $i; gsub(/^ +| +$/, "", cell); c[NR, i] = cell
      if (cell !~ /^-+$/ && length(cell) > w[i]) w[i] = length(cell) } ; n[NR] = NF }
  END {
    for (r = 1; r <= NR; r++) {
      line = "|"
      for (i = 2; i < n[r]; i++) {
        if (c[r, i] ~ /^-+$/) { dash = ""; for (j = 0; j < w[i] + 2; j++) dash = dash "-"; line = line dash "|" }
        else line = line sprintf(" %-" w[i] "s |", c[r, i])
      }
      print line
    }
  }
'

index=0
for app in "$@"; do
  index=$((index + 1))
  label="$(basename "$app")"
  data="$work/$(printf '%02d' "$index")-$label/breakdown.tsv"
  [ -s "$data" ] && print_breakdown "$label" "$data"
done

[ -n "$output" ] && info "report written to $output"
[ -n "$keep" ] && info "kept publish output, logs and map files in $work"
exit "$failed"