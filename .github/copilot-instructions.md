# CatHole: TCP/UDP Relay Service - AI Coding Guide

## Architecture Overview

CatHole is a high-performance network relay service built on .NET 10 that forwards TCP/UDP traffic between endpoints. The solution follows a clean separation of concerns:

- **CatHole.Core**: Framework-agnostic core library containing relay logic (`CatHoleRelay`, `CatHoleRelayManager`, `CatHoleRelayFactory`)
- **CatHole**: ASP.NET Core hosted service wrapper with configuration integration and DI extensions

### Core Components & Relationships

1. **CatHoleRelay** (`CatHole.Core/CatHoleRelay.cs`): Individual relay instance managing bidirectional TCP/UDP forwarding between listen and target endpoints
2. **CatHoleRelayManager** (`CatHole.Core/CatHoleRelayManager.cs`): Thread-safe coordinator for multiple relay instances with lifecycle management
3. **CatHoleRelayOption** (`CatHole.Core/CatHoleRelayOption.cs`): Configuration model defining relay parameters (Name, ListenHost, TargetHost, TCP/UDP flags, BufferSize, Timeouts)
4. **RelayHostedService** (`CatHole/RelayHostedService.cs`): IHostedService implementation that loads relays from appsettings.json on startup
5. **ServiceCollectionExtensions** (`CatHole/ServiceCollectionExtensions.cs`): DI registration helpers (`AddRelayManager()`, `AddRelayHostedService()`)

**Data Flow**: `appsettings.json` → `RelayHostedService.StartAsync()` → `RelayManager.LoadFromConfiguration()` → Creates `CatHoleRelay` instances → Each relay starts TCP/UDP listeners → Bidirectional traffic forwarding

## Project-Specific Conventions

### Threading & Concurrency Patterns
- Use `Lock` (not `lock`) for .NET 10+ - see `CatHoleRelayManager._managementLock` and `CatHoleRelay._stateLock`
- Use `ConcurrentDictionary` for thread-safe collections - see `_relays` in manager and `_udpMap` in relay
- Use `Interlocked.CompareExchange` for atomic state flags - see `_isRunning` in `CatHoleRelay`
- Track async tasks with `ConcurrentBag<Task>` - see `_activeTasks` for client connection cleanup

### Error Handling Philosophy
- Validation in constructors and public entry points (throw `ArgumentNullException`, `ArgumentException`)
- Log warnings for expected failures (relay already exists, not found) but don't throw
- Log debug messages for transient operations (client connections, task completion)
- Always wrap cleanup in try-catch with debug logging - see `StopAsync()` implementations

### Configuration-Driven Design
- All relay configurations live in `appsettings.json` under `"Relays"` array
- Endpoint format: IPEndPoint string notation (e.g., `"127.0.0.1:5201"` or `"[::]:5050"` for IPv6)
- Default values in `CatHoleRelayOption` should be sensible for typical use cases
- Configuration loading happens automatically via `IHostedService` pattern

### Dependency Injection Setup
```csharp
// Standard registration in Program.cs:
services.AddRelayHostedService(); // Registers manager + hosted service
```

### Logging with Serilog
- Project uses Serilog with structured logging (configured in appsettings.json)
- Log lifecycle events at Information level (Started X relays, Stopping relay)
- Log operational details at Debug level (Accepted TCP client, Task completion)
- Log failures at Warning (connection failures) or Error (unexpected exceptions)
- Use log message templates with named properties: `_logger.LogInformation("Starting relay [{Name}] {ListenHost} -> {TargetHost}", ...)`

## Key Implementation Patterns

### Relay Lifecycle Management
- **Start**: Creates TcpListener/UdpClient, spawns forwarding tasks via `Task.Run()`
- **Stop**: Cancels CTS → Stops listeners → Waits for all forwarding + client tasks → Cleans up resources
- **Graceful Shutdown**: Must wait for `_activeTasks` (client connections) before disposing

### TCP Forwarding Pattern
```csharp
// Accept loop in StartTCPForwarding
while (!ct.IsCancellationRequested) {
    var client = await _tcpListener.AcceptTcpClientAsync(ct);
    var task = Task.Run(() => HandleTCPClient(client, ct), ct);
    _activeTasks.Add(task); // Track for cleanup
}
```

### UDP Session Mapping
- UDP is stateless; CatHole creates per-client "tunnels" tracked in `_udpMap`
- Each remote endpoint gets a dedicated `UdpClient` for target communication
- Tunnels expire after `UdpTunnelTimeout` seconds of inactivity

## Development Workflows

### Building
```bash
dotnet build
```

### Running Locally
```bash
dotnet run --project CatHole
```
Relays auto-load from `CatHole/appsettings.json` - modify the `"Relays"` section for testing.

### Docker Deployment
Project includes `DockerDefaultTargetOS=Linux` in csproj. Build container with standard .NET SDK image.

## Common Extension Points

### Adding New Relay Types
1. Extend `CatHoleRelayOption` with new properties
2. Implement handling in `CatHoleRelay.Start()` (new protocol branching)
3. Update `CatHoleRelayFactory.ValidateOption()` for new validation rules

### Custom Configuration Sources
- Use `RelayManager.AddRelays()` or `AddRelay()` directly with programmatic `CatHoleRelayOption` instances
- Extension method pattern: See `RelayManagerExtensions.LoadFromConfiguration()` as template

### Runtime Management API
- Access `CatHoleRelayManager` from DI container for runtime control:
  - `AddRelay(option)` / `RemoveRelayAsync(name)`
  - `TryGetRelay(name, out relay)` for inspection
  - `StopAllAsync()` / `ClearAsync()` for bulk operations

## Critical Implementation Details

### IPEndPoint Parsing
Always use `IPEndPoint.Parse()` to support both IPv4 ("127.0.0.1:8080") and IPv6 ("[::1]:8080") formats.

### Stream Shutdown Handshake
TCP forwarding uses `CopyStreamWithShutdownAsync()` to properly signal EOF when one side closes (prevents half-open connections).

### Buffer Sizing
Default `BufferSize: 128KB` is optimized for high-throughput scenarios. Adjust per relay in appsettings for low-latency vs. throughput trade-offs.

### Timeout Configuration
- `Timeout`: Socket send/receive timeout (default 1000ms) - prevents hung connections
- `UdpTunnelTimeout`: UDP session idle timeout (default 60s) - balances memory vs. connection reuse
