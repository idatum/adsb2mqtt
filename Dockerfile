# https://hub.docker.com/_/microsoft-dotnet-sdk/
FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine3.17 AS build

COPY /app /app
WORKDIR /app
# Label as build image
LABEL "build"="adsb2mqtt"
# copy csproj and restore as distinct layers
RUN dotnet restore .

# copy everything else and build
RUN dotnet publish -c Release -o out

# https://hub.docker.com/_/microsoft-dotnet-runtime/
FROM mcr.microsoft.com/dotnet/runtime:7.0-alpine3.17 AS runtime
RUN addgroup -g 1010 adsb2mqtt && \
    adduser -S -u 1010 -G adsb2mqtt -s /bin/sh adsb2mqtt
WORKDIR /app
COPY --from=build /app/out ./
RUN chown -R adsb2mqtt:adsb2mqtt /app
USER adsb2mqtt
ENTRYPOINT ["dotnet", "adsb2mqtt.dll"]
