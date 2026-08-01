FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/RaindropAI.Core/RaindropAI.Core.csproj src/RaindropAI.Core/
COPY src/RaindropAI.Infrastructure/RaindropAI.Infrastructure.csproj src/RaindropAI.Infrastructure/
COPY src/RaindropAI.Worker/RaindropAI.Worker.csproj src/RaindropAI.Worker/
RUN dotnet restore src/RaindropAI.Worker/RaindropAI.Worker.csproj

COPY src/ src/
RUN dotnet publish src/RaindropAI.Worker/RaindropAI.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# /data porte la base SQLite et les logs. Ce chown ne couvre que le cas d'un volume nommé : sur un
# bind mount (le cas de docker-compose.yml), c'est la propriété côté hôte qui prime — voir le README.
RUN mkdir -p /data/logs && chown -R $APP_UID:$APP_UID /data
VOLUME ["/data"]

# $APP_UID (1654) est l'utilisateur applicatif non-root fourni par l'image de base ; sans ce USER,
# le worker tourne en root avec /data monté depuis l'hôte.
USER $APP_UID
ENTRYPOINT ["dotnet", "RaindropAI.Worker.dll"]
