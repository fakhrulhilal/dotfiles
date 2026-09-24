# Source from your shell rc, e.g.:  . /path/to/ts_functions.sh
#
# Env vars:
#   TAILSCALE_AUTH_KEY   auth key — only needed for first-time registration
#   TAILSCALE_PORT       SOCKS5 proxy port (default 1055)
#   TS_DIR                state/socket/log dir (default /tmp/tailscale)

TS_DIR="${TS_DIR:-/tmp/tailscale}"

ts_connect() {
    local port="${TAILSCALE_PORT:-1055}"
    local socket="$TS_DIR/ts.sock"
    local state="$TS_DIR/ts.state"
    local pidfile="$TS_DIR/tailscaled.pid"
    local log="$TS_DIR/tailscaled.log"
    local host="${TAILSCALE_HOSTNAME:-$(hostname -s 2>/dev/null || hostname)}"
    local i

    mkdir -p "$TS_DIR"

    if [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile")" 2>/dev/null; then
        : # daemon already running
    else
        tailscaled --tun=userspace-networking \
            --socks5-server="localhost:$port" \
            --state="$state" \
            --socket="$socket" \
            >"$log" 2>&1 &
        echo "$!" > "$pidfile"

        i=0
        while [ ! -e "$socket" ] && [ "$i" -lt 10 ]; do
            sleep 1
            i=$((i + 1))
        done
    fi

    if [ -s "$state" ]; then
        tailscale --socket="$socket" up --hostname="$host"
    elif [ -n "${TAILSCALE_AUTH_KEY:-}" ]; then
        tailscale --socket="$socket" up --hostname="$host" --auth-key="$TAILSCALE_AUTH_KEY"
    else
        echo "No saved state in $state and TAILSCALE_AUTH_KEY is not set." >&2
        echo "First run: TAILSCALE_AUTH_KEY=tskey-auth-xxxx ts_connect" >&2
        return 1
    fi

    tailscale --socket="$socket" status
}

ts_exit() {
    local socket="$TS_DIR/ts.sock"
    local state="$TS_DIR/ts.state"
    local pidfile="$TS_DIR/tailscaled.pid"

    if [ "${1:-}" = "--logout" ]; then
        # Full deauth: removes the node from the tailnet. Next ts_connect
        # registers as a new device and may need re-approval.
        tailscale --socket="$socket" logout 2>/dev/null || true
        if [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile")" 2>/dev/null; then
            kill "$(cat "$pidfile")" 2>/dev/null || true
        fi
        rm -f "$socket" "$state" "$pidfile"
    else
        # Default: pause connectivity, keep identity. Next ts_connect
        # resumes without a new auth key or re-approval.
        tailscale --socket="$socket" down 2>/dev/null || true
    fi
}

ts_ssh() {
    local port="${TAILSCALE_PORT:-1055}"
    ssh -o ProxyCommand="nc -X 5 -x localhost:$port %h %p" "$@"
}