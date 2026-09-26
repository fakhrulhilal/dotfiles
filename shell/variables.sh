export DOTNET_CLI_TELEMETRY_OPTOUT=1
export ASPNETCORE_URLS="http://*:8080"
export ASPNETCORE_ENVIRONMENT="Development"

export EDITOR=vim
export OTEL_DENO=true
export WAKATIME_HOME="$HOME/.wakatime"
export DEV_DOMAIN="dev.local"
export SAN="DNS:*.blob.azurite.$DEV_DOMAIN, DNS:*.queue.azurite.$DEV_DOMAIN, DNS:*.table.azurite.$DEV_DOMAIN, DNS:*.$DEV_DOMAIN, DNS:$DEV_DOMAIN, DNS:*.lab, DNS:*.local, DNS:localhost, IP:127.0.0.1"
