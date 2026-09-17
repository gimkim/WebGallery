# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY WebGallery.csproj ./
RUN dotnet restore WebGallery.csproj --runtime linux-x64

COPY . ./
RUN dotnet publish WebGallery.csproj \
    --no-restore \
    -p:PublishProfile=Linux-Docker \
    -p:PublishDir=/app/publish/

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl ffmpeg libimage-exiftool-perl intel-media-va-driver-non-free libmfx-gen1.2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish/ ./
RUN install -d -o app -g app /data/database /data/cache /data/keys /gallery

ENV ASPNETCORE_ENVIRONMENT=Docker \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health > /dev/null || exit 1

ENTRYPOINT ["dotnet", "WebGallery.dll"]
