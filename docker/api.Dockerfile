# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/TaskMan.Domain/TaskMan.Domain.csproj src/TaskMan.Domain/
COPY src/TaskMan.Infrastructure/TaskMan.Infrastructure.csproj src/TaskMan.Infrastructure/
COPY src/TaskMan.Api/TaskMan.Api.csproj src/TaskMan.Api/

RUN dotnet restore src/TaskMan.Api/TaskMan.Api.csproj

COPY src/ src/

RUN dotnet publish src/TaskMan.Api/TaskMan.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN groupadd --gid 1001 taskman \
    && useradd --uid 1001 --gid taskman --create-home --shell /usr/sbin/nologin taskman \
    && mkdir -p /data/attachments /data/keys \
    && chown -R taskman:taskman /data

COPY --from=build --chown=taskman:taskman /app/publish .

USER taskman

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "TaskMan.Api.dll"]
