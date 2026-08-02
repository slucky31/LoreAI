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
COPY src/LoreAI.Worker/LoreAI.Worker.csproj src/LoreAI.Worker/
# BuildKit nomme l'architecture amd64/arm64, le SDK .NET attend x64/arm64.
RUN DOTNET_ARCH="$(echo "$TARGETARCH" | sed 's/amd64/x64/')" \
    && dotnet restore src/LoreAI.Worker/LoreAI.Worker.csproj -a "$DOTNET_ARCH"

COPY src/ src/
RUN DOTNET_ARCH="$(echo "$TARGETARCH" | sed 's/amd64/x64/')" \
    && dotnet publish src/LoreAI.Worker/LoreAI.Worker.csproj \
       -c Release -a "$DOTNET_ARCH" --self-contained false -o /app/publish --no-restore

# L'image finale est « chiselée » : ni shell, ni mkdir, ni chown — aucune instruction RUN n'y est
# possible. Le squelette de /data est donc préparé ici, puis copié avec la bonne propriété.
RUN mkdir -p /app/data-skeleton/logs

# Chiselée : pas de shell ni de gestionnaire de paquets (surface d'attaque réduite), et l'utilisateur
# applicatif non-root est déjà celui par défaut. Multi-arch, dont arm64 pour le Raspberry Pi.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .

# /data porte la base SQLite et les logs. Cette propriété ne vaut que pour un volume nommé : sur un
# bind mount (le cas de docker-compose.yml), c'est la propriété côté hôte qui prime — voir le README.
COPY --from=build --chown=$APP_UID:$APP_UID /app/data-skeleton /data
VOLUME ["/data"]

# Redondant avec le défaut de l'image chiselée, mais explicite : sans cela, une bascule vers une image
# de base non chiselée ferait silencieusement retomber le worker en root.
USER $APP_UID
ENTRYPOINT ["dotnet", "LoreAI.Worker.dll"]
