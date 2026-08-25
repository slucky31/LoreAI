# --platform=$BUILDPLATFORM : le SDK tourne nativement sur le runner et c'est `dotnet publish -a` qui
# cible l'architecture voulue. Émuler tout le SDK sous QEMU pour produire l'arm64 serait bien plus lent,
# et la cross-compilation évite complètement l'émulation (l'étage final ne fait que des COPY).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# .editorconfig fait partie de la configuration de build : Directory.Build.props active les analyseurs
# .NET avec TreatWarningsAsErrors, et c'est .editorconfig qui porte leur paramétrage. L'oublier ici fait
# échouer la compilation dans le conteneur alors qu'elle passe en local.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/LoreAI.Core/LoreAI.Core.csproj src/LoreAI.Core/
COPY src/LoreAI.Infrastructure/LoreAI.Infrastructure.csproj src/LoreAI.Infrastructure/
COPY src/LoreAI.Mcp/LoreAI.Mcp.csproj src/LoreAI.Mcp/
# BuildKit nomme l'architecture amd64/arm64, le SDK .NET attend x64/arm64.
RUN DOTNET_ARCH="$(echo "$TARGETARCH" | sed 's/amd64/x64/')" \
    && dotnet restore src/LoreAI.Mcp/LoreAI.Mcp.csproj -a "$DOTNET_ARCH"

COPY src/ src/
RUN DOTNET_ARCH="$(echo "$TARGETARCH" | sed 's/amd64/x64/')" \
    && dotnet publish src/LoreAI.Mcp/LoreAI.Mcp.csproj \
       -c Release -a "$DOTNET_ARCH" --self-contained false -o /app/publish --no-restore

# L'image finale est « chiselée » : ni shell, ni mkdir, ni chown — aucune instruction RUN n'y est
# possible. Le squelette de /data est donc préparé ici, puis copié avec la bonne propriété.
RUN mkdir -p /app/data-skeleton/logs

# O4 (#71, lot 6) : /usr/share/zoneinfo n'est pas garanti présent sur l'image SDK Debian de base —
# installé explicitement ici plutôt que supposé, puis copié tel quel vers l'étage chiselé qui n'a pas
# de gestionnaire de paquets pour le faire lui-même.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*

# Chiselée, variante ASP.NET Core (contrairement à LoreAI.Worker : ce process héberge Kestrel, pas
# seulement le runtime .NET) : pas de shell ni de gestionnaire de paquets, utilisateur non-root par
# défaut. Multi-arch, dont arm64 pour le Raspberry Pi.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .

# O4 (#71, lot 6) : même traitement que LoreAI.Worker — seul l'horodatage Serilog (heure locale du
# process) change, aucune valeur UTC de domaine n'en dépend.
COPY --from=build /usr/share/zoneinfo /usr/share/zoneinfo
ENV TZ=Europe/Paris

# /data ne porte que les logs (pas de base ici, ADR 0009) — même convention que LoreAI.Worker. Sur un
# bind mount, c'est la propriété côté hôte qui prime, voir docs/deploiement-raspberry-pi.md.
COPY --from=build --chown=$APP_UID:$APP_UID /app/data-skeleton /data
VOLUME ["/data"]

USER $APP_UID
ENTRYPOINT ["dotnet", "LoreAI.Mcp.dll"]
