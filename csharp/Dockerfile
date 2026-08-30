# syntax=docker/dockerfile:1
ARG DISTRO=noble
FROM mcr.microsoft.com/dotnet/sdk:10.0-$DISTRO-aot AS build
ARG SCRIPT_FILE
WORKDIR /source

COPY --link . .
RUN --mount=type=cache,target=/root/.nuget \
    --mount=type=cache,target=/source/bin \
    --mount=type=cache,target=/source/obj \
    dotnet publish -o /app $SCRIPT_FILE /p:AssemblyName=launcher \
        && rm /app/*.dbg

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-$DISTRO-chiseled-extra
LABEL org.opencontainers.image.source="https://github.com/fakhrulhilal/dotfiles"
WORKDIR /home/app
COPY --link --from=build /app .
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
USER $APP_UID
ENTRYPOINT ["./launcher"]