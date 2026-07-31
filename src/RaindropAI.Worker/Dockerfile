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

VOLUME ["/data"]
ENTRYPOINT ["dotnet", "RaindropAI.Worker.dll"]
