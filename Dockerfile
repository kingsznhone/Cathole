# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY CatFlapRelay/CatFlapRelay.csproj CatFlapRelay/
COPY CatFlapRelay.Panel/CatFlapRelay.Panel.csproj CatFlapRelay.Panel/

RUN dotnet restore CatFlapRelay.Panel/CatFlapRelay.Panel.csproj

COPY CatFlapRelay/ CatFlapRelay/
COPY CatFlapRelay.Panel/ CatFlapRelay.Panel/

RUN dotnet publish CatFlapRelay.Panel/CatFlapRelay.Panel.csproj \
    -c Release \
    --no-self-contained \
    -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    libsqlite3-0 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "catflaprelay-panel.dll"]
