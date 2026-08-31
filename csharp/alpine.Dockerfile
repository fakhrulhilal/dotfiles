# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine-aot AS build
ARG SCRIPT_FILE
ARG TARGETARCH
WORKDIR /source

COPY --link . .
RUN --mount=type=cache,sharing=locked,target=/root/.nuget \
    --mount=type=cache,sharing=locked,target=/source/bin \
    --mount=type=cache,sharing=locked,target=/source/obj \
    RID=$(case "$TARGETARCH" in \
            amd64) echo linux-musl-x64 ;; \
            arm64) echo linux-musl-arm64 ;; \
            *) echo "unsupported arch: $TARGETARCH" >&2; exit 1 ;; \
          esac) && \
    dotnet publish -o /app $SCRIPT_FILE /p:AssemblyName=launcher -r "$RID" \
        && rm /app/*.dbg

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine-extra
LABEL org.opencontainers.image.source="https://github.com/fakhrulhilal/dotfiles"
WORKDIR /home/app
COPY --link --from=build /app .
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
USER $APP_UID
ENTRYPOINT ["./launcher"]