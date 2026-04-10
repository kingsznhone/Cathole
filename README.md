<div align="center">

# 🐱 Cat Flap Relay

<div align="center">
  <img src="./README/CatFlapRelay.png" width="128" />
</div>

**A modern, high-performance TCP/UDP port forwarding toolkit built with .NET 10**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![NuGet Version](https://img.shields.io/nuget/v/CatFlapRelay.svg?style=flat-square&logo=nuget&logoColor=white)](https://www.nuget.org/packages/CatFlapRelay/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CatFlapRelay.svg?style=flat-square&logo=nuget&logoColor=white)](https://www.nuget.org/packages/CatFlapRelay/)
[![Docker Pulls](https://img.shields.io/docker/pulls/kingsznhone/catflap-relay.svg?style=flat-square&logo=docker&logoColor=white)](https://hub.docker.com/r/kingsznhone/catflap-relay)
[![Docker Version](https://img.shields.io/docker/v/kingsznhone/catflap-relay?style=flat-square&sort=semver&logo=docker&logoColor=white&label=image)](https://hub.docker.com/r/kingsznhone/catflap-relay)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue.svg?style=flat-square)]()

**English | [简体中文](./README.zh-CN.md)**

</div>

---

Cat Flap Relay is a dual-stack (IPv4 & IPv6) port relay toolkit. It ships as two components:

| Component | Use Case |
|-----------|----------|
| **CatFlapRelay.CLI** | Lightweight command-line tool — spin up a TCP/UDP relay in seconds |
| **CatFlapRelay.Panel** | Full-featured management panel (Blazor + REST API) — manage multiple relays via browser or API, runs in Docker |

---

## NuGet Library

The core relay engine is available as a standalone NuGet package for embedding into your own .NET applications.

```bash
dotnet add package CatFlapRelay
```

Or via the Package Manager Console:

```powershell
Install-Package CatFlapRelay
```

### Usage

#### Step 1 — Choose a logger

Both APIs accept any `ILoggerFactory`. Pick whichever suits your project:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// No logging (silent) — zero dependencies
ILoggerFactory loggerFactory = NullLoggerFactory.Instance;

// Built-in console logger — no extra packages needed
ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole());

// Serilog — install Serilog.Extensions.Logging + a sink of your choice
var serilog = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();
ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddSerilog(serilog));

// ASP.NET Core / Generic Host — resolve from DI
// ILoggerFactory loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
```

#### Step 2A — Direct instantiation via `FlapRelayOption`

```csharp
using CatFlapRelay;

var factory = new FlapRelayFactory(loggerFactory);

var relay = factory.CreateRelay(new FlapRelayOption
{
    Name       = "MyRelay",
    ListenHost = "0.0.0.0:8080",
    TargetHost = "192.168.1.100:3389",
    TCP        = true,
    UDP        = true,
    BufferSize = 131072,
});

await relay.StartAsync();

// ...

await relay.StopAsync();
```

#### Step 2B — Fluent builder via `RelayBuilder`

```csharp
using CatFlapRelay;

var relay = FlapRelayFactory.CreateBuilder(loggerFactory)
    .WithName("MyRelay")
    .ListenOn("0.0.0.0:8080")
    .ForwardTo("192.168.1.100:3389")
    .WithBufferSize(131072)
    .WithSocketTimeout(TimeSpan.FromMilliseconds(1000))
    .EnableTCP()
    .EnableUDP()   // or .TCPOnly() / .UDPOnly()
    .Build();

await relay.StartAsync();

// ...

await relay.StopAsync();
```

> **Package page:** https://www.nuget.org/packages/CatFlapRelay/1.0.0

---

## Quick Start — CLI

### Install & Run

```bash
# Build from source
dotnet build CatFlapRelay.CLI -c Release

catflaprelay-cli --listen 0.0.0.0:8080 --target 192.168.1.100:3389
```

### Examples

```bash
# Basic TCP+UDP relay
catflaprelay-cli -l 0.0.0.0:25565 -t 10.0.0.5:25565

# TCP only, custom name
catflaprelay-cli -l 0.0.0.0:8080 -t 192.168.1.10:80 --no-udp -n "WebProxy"

# UDP only with larger buffer (512 KB)
catflaprelay-cli -l [::]:53 -t 8.8.8.8:53 --no-tcp -b 524288

# IPv6 listen → IPv4 target (dual-stack bridge)
catflaprelay-cli -l [::]:443 -t 127.0.0.1:8443

# Verbose logging for debugging
catflaprelay-cli -l 0.0.0.0:5000 -t 10.0.0.1:5000 -v
```

### CLI Options

| Flag | Short | Description | Default |
|------|-------|-------------|---------|
| `--listen` | `-l` | Listen endpoint (`host:port`) | *required* |
| `--target` | `-t` | Target endpoint (`host:port`) | *required* |
| `--name` | `-n` | Relay display name | `Relay_XXXX` |
| `--no-tcp` | | Disable TCP forwarding | `false` |
| `--no-udp` | `-U` | Disable UDP forwarding | `false` |
| `--buffer-size` | `-b` | I/O buffer size in bytes | `131072` (128 KB) |
| `--timeout` | | Socket timeout in milliseconds | `1000` |
| `--verbose` | `-v` | Enable debug logging | `false` |
| `--quiet` | `-q` | Suppress info logging | `false` |

---

## Management Panel (Docker)

The Panel provides a web-based dashboard with Ant Design UI and a Swagger-documented REST API for managing multiple relays.

<div align="center">
  <img src="./README/Demo.jpg"  />
</div>

### Deploy with Docker

```bash
docker run -d \
  --name catflap-panel \
  --network host \
  -v catflap-data:/app/data \
  -e Admin__UserName=admin \
  -e Admin__Password=YourSecurePassword \
  kingsznhone/catflap-relay:latest
```

> **Why `--network host`?** CatFlap Relay forwards traffic between arbitrary ports on the host. Bridge networking adds a NAT layer that breaks relay functionality — host network mode gives the container direct access to all host interfaces and ports.
> The panel web UI is accessible at `http://<host-ip>:8080`.

> If `Admin__Password` is not set, a random 16-character password will be generated and printed to the container logs.
> Check it with: `docker logs catflap-panel`

> **No persistent volume?** The Panel will fall back to an in-memory SQLite database automatically.
> All data (relay configs, users) will be lost on restart — useful for quick testing but not recommended for production.

### Docker Compose

```yaml
services:
  catflap:
    image: kingsznhone/catflap-relay:latest
    container_name: catflap-panel
    restart: unless-stopped
    network_mode: host
    volumes:
      - catflap-data:/app/data
    environment:
      - Admin__UserName=admin
      - Admin__Password=YourSecurePassword
      - JwtSettings__Key=YOUR_HEX_SECRET_KEY   # optional, auto-generated if omitted
      # - Cors__AllowedOrigins__0=https://your-domain.com
      # - PanelSettings__MaxRelays=256
      # - PanelSettings__MaxBufferSize=67108864

volumes:
  catflap-data:
```

### Reset Admin Password

Forgot your password? Set the `CATFLAP_RESET_ADMIN` environment variable:

```bash
# Reset with a new explicit password
docker run --rm \
  -v catflap-data:/app/data \
  -e CATFLAP_RESET_ADMIN=true \
  -e Admin__Password=MyNewPassword \
  kingsznhone/catflap-relay:latest

# Reset with an auto-generated password (check container logs)
docker run --rm \
  -v catflap-data:/app/data \
  -e CATFLAP_RESET_ADMIN=true \
  kingsznhone/catflap-relay:latest
```

> Remove `CATFLAP_RESET_ADMIN` from your normal deployment to prevent resetting on every restart.

### Panel Configuration

Configurable via `appsettings.json` or environment variables (`PanelSettings__*`):

| Key | Description | Default |
|-----|-------------|---------|
| `PanelSettings:MaxRelays` | Maximum number of relays | `256` |
| `PanelSettings:MaxNameLength` | Max relay name length | `128` |
| `PanelSettings:MaxEndpointLength` | Max host:port string length | `64` |
| `PanelSettings:MaxBufferSize` | Max I/O buffer size (bytes) | `67108864` (64 MB) |

### REST API Examples

```bash
# Obtain a JWT token
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"YourSecurePassword","expiryDays":30}' | jq -r '.token')

# Create a relay
curl -X POST http://localhost:8080/api/v1/relay \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"GameServer","listenHost":"0.0.0.0:25565","targetHost":"10.0.0.5:25565"}'

# List all relays
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/v1/relay

# Start all relays
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/v1/relay/start-all
```



## Architecture

```
┌──────────────────────────────────────────────────┐
│                  CatFlapRelay                    │
│  FlapRelay ─ FlapRelayManager ─ FlapRelayFactory │
│  TCP stream relay  │  UDP tunnel relay           │
└──────────┬──────────────────┬────────────────────┘
           │                  │
     ┌─────┴──────────┐ ┌─────┴──────────────────┐
     │CatFlapRelay.CLI│ │  CatFlapRelay.Panel    │
     │ (Console)      │ │ Blazor SSR + REST API  │
     │ Single         │ │ Multi-relay management │
     │ relay mode     │ │ SQLite + JWT Auth      │
     └────────────────┘ └────────────────────────┘
```

## Tech Stack

- **.NET 10** — Latest runtime with AOT-friendly serialization
- **System.CommandLine** — CLI parsing
- **Blazor Server + Ant Design** — Panel UI
- **ASP.NET Core Identity + JWT** — Authentication
- **Entity Framework Core + SQLite** — Persistence
- **Serilog** — Structured logging

## License

[MIT](LICENSE) © KingsZNHONE
