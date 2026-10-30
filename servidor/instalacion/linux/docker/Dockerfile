# SONA Deposito - servidor en Docker (usa el binario ya compilado de la carpeta de arriba)
# Construir y levantar desde la carpeta SONA_Servidor_Linux:  docker compose -f docker/docker-compose.yml up -d --build
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0
ENV TZ=America/Argentina/Buenos_Aires SONA_DATOS=/datos SONA_PUERTO=8080 DOTNET_BUNDLE_EXTRACT_BASE_DIR=/datos/.net
RUN apt-get update && apt-get install -y --no-install-recommends tzdata wget && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY SonaServidor ./
COPY wwwroot ./wwwroot
RUN chmod +x SonaServidor
VOLUME /datos
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s CMD wget -qO- http://localhost:8080/api/estado >/dev/null 2>&1 || exit 1
ENTRYPOINT ["/app/SonaServidor"]
