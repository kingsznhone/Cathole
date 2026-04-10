# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CatFlap.Core/CatFlap.Core.csproj CatFlap.Core/
COPY CatFlap.Panel/CatFlap.Panel.csproj CatFlap.Panel/

RUN dotnet restore CatFlap.Panel/CatFlap.Panel.csproj

COPY CatFlap.Core/ CatFlap.Core/
COPY CatFlap.Panel/ CatFlap.Panel/

RUN dotnet publish CatFlap.Panel/CatFlap.Panel.csproj \
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

ENTRYPOINT ["dotnet", "CatFlap.Panel.dll"]
