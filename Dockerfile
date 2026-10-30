# SONA Depósito – servidor en contenedor (mismo núcleo que el panel de escritorio), con el panel web incluido.
# Construir y levantar:  docker compose up -d      Ver registro:  docker compose logs -f

# 1) panel web (React) -> servidor/panel-web
FROM node:20-alpine AS web
WORKDIR /repo/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2) servidor .NET 8
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS compilar
WORKDIR /repo
COPY servidor/ servidor/
COPY --from=web /repo/servidor/panel-web servidor/panel-web
RUN dotnet publish servidor/src/Sona.Servidor/Sona.Servidor.csproj -c Release -o /app

# 3) imagen final
FROM mcr.microsoft.com/dotnet/aspnet:8.0
ENV TZ=America/Argentina/Buenos_Aires \
    SONA_DATOS=/datos \
    SONA_PUERTO=8080 \
    ASPNETCORE_URLS=
RUN apt-get update && apt-get install -y --no-install-recommends tzdata wget && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilar /app .
VOLUME /datos
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s CMD wget -qO- http://localhost:8080/api/estado >/dev/null 2>&1 || exit 1
ENTRYPOINT ["dotnet", "SonaServidor.dll"]
