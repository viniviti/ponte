# syntax=docker/dockerfile:1.7
# Um unico Dockerfile para todos os servicos .NET (contexto = raiz do repo):
#   docker build -f src/Dockerfile --build-arg PROJECT=src/Services/Ingestion/Ponte.Ingestion.Api/Ponte.Ingestion.Api.csproj \
#                --build-arg ASSEMBLY=Ponte.Ingestion.Api -t ponte-ingestion .

ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG PROJECT
WORKDIR /repo

# Arquivos de build compartilhados primeiro (mudam pouco: melhor cache de camada).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "${PROJECT}"

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "${PROJECT}" -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
ARG ASSEMBLY
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1 \
    DOTNET_TieredPGO=1 \
    APP_ASSEMBLY=${ASSEMBLY}

EXPOSE 8080

# Usuario sem privilegios (ja existe nas imagens oficiais do .NET 8).
USER app
COPY --from=build --chown=app:app /app .

ENTRYPOINT ["sh", "-c", "exec dotnet \"${APP_ASSEMBLY}.dll\""]
