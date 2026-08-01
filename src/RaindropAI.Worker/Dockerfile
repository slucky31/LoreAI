FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/RaindropAI.Core/RaindropAI.Core.csproj src/RaindropAI.Core/
COPY src/RaindropAI.Infrastructure/RaindropAI.Infrastructure.csproj src/RaindropAI.Infrastructure/
COPY src/RaindropAI.Worker/RaindropAI.Worker.csproj src/RaindropAI.Worker/
RUN dotnet restore src/RaindropAI.Worker/RaindropAI.Worker.csproj

COPY src/ src/
RUN dotnet publish src/RaindropAI.Worker/RaindropAI.Worker.csproj -c Release -o /app/publish --no-restore

# L'image finale est « chiselée » : ni shell, ni mkdir, ni chown — aucune instruction RUN n'y est
# possible. Le squelette de /data est donc préparé ici, puis copié avec la bonne propriété.
RUN mkdir -p /app/data-skeleton/logs

# Chiselée : pas de shell ni de gestionnaire de paquets (surface d'attaque réduite), et l'utilisateur
# applicatif non-root est déjà celui par défaut. Multi-arch, dont arm64 pour le Raspberry Pi.
FROM mcr.microsoft.com/dotnet/runtime:9.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .

# /data porte la base SQLite et les logs. Cette propriété ne vaut que pour un volume nommé : sur un
# bind mount (le cas de docker-compose.yml), c'est la propriété côté hôte qui prime — voir le README.
COPY --from=build --chown=$APP_UID:$APP_UID /app/data-skeleton /data
VOLUME ["/data"]

# Redondant avec le défaut de l'image chiselée, mais explicite : sans cela, une bascule vers une image
# de base non chiselée ferait silencieusement retomber le worker en root.
USER $APP_UID
ENTRYPOINT ["dotnet", "RaindropAI.Worker.dll"]
