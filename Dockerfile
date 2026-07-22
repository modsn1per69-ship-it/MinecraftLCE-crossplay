FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY relay/LegacyCrossplayRelay.csproj relay/
RUN dotnet restore relay/LegacyCrossplayRelay.csproj
COPY relay/LocalRelayServer.cs relay/
RUN dotnet publish relay/LegacyCrossplayRelay.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
RUN useradd --system --uid 10001 relay \
    && mkdir /data \
    && chown relay:relay /data
COPY --from=build --chown=relay:relay /app/publish/ ./
USER relay
ENV CONSOLE_LEGACY_RELAY_BIND_ADDRESS=0.0.0.0 \
    CONSOLE_LEGACY_RELAY_PORT=61000 \
    CONSOLE_LEGACY_RELAY_LOG_PATH=/data/relay.log
EXPOSE 61000/tcp
ENTRYPOINT ["dotnet", "LegacyCrossplayRelay.dll"]
