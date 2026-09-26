#!/usr/bin/env bash
# Smoke-tests a published binary: CLIs must print the expected version, web apps must pass their readiness check.
# Usage: smoke.sh <exe> <version>
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <exe> <version>" >&2
  exit 2
fi

exe=$1
expected=$2
name=$(basename "$exe" .exe)

case "$name" in
  dothook)
    port=18080
    data=$(mktemp -d)
    ASPNETCORE_URLS="http://127.0.0.1:$port" DB__Type=Sqlite DB__ConnectionString="Data Source=$data/webhook.db" \
      "$exe" > "$data/log.txt" 2>&1 &
    pid=$!
    trap 'kill "$pid" 2> /dev/null || true' EXIT
    for _ in $(seq 30); do
      if curl -fs "http://127.0.0.1:$port/_health/ready"; then
        echo
        echo "$name is ready"
        exit 0
      fi
      sleep 1
    done
    cat "$data/log.txt" >&2
    echo "$name did not become ready" >&2
    exit 1
    ;;
  *)
    actual=$("$exe" --version | tr -d '\r')
    # the informational version may carry a +<commit> suffix
    if [[ ${actual%%+*} != "$expected" ]]; then
      echo "$name --version printed '$actual', expected '$expected'" >&2
      exit 1
    fi
    echo "$name $actual"
    ;;
esac