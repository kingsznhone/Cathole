# CatHole 使用指南

CatHole 是一个高性能的 TCP/UDP 端口转发库，支持多种使用场景。

## 📦 架构设计

```
CatHole.Core (核心库 - 框架无关)
├── Relay            - 单个转发实例
├── RelayOption      - 配置选项
├── RelayManager     - 核心管理器
└── RelayFactory     - 工厂和构建器

CatHole (ASP.NET Core 集成)
├── RelayService         - 配置加载服务
└── RelayHostedService   - IHostedService 实现
```

---

## 🚀 使用场景

### 场景 1: Native Console 应用

```csharp
using CatHole.Core;
using Microsoft.Extensions.Logging;

// 创建 logger factory
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// 创建 RelayManager
using var manager = new RelayManager(loggerFactory);

// 方式 1: 直接添加 relay
manager.AddRelay(new RelayOption
{
    Name = "WebProxy",
    ListenHost = "127.0.0.1:8080",
    TargetHost = "192.168.1.100:80",
    TCP = true,
    UDP = false,
    BufferSize = 128 * 1024,
    Timeout = 5000
});

// 方式 2: 使用 Builder
var relay = RelayFactory.CreateBuilder(loggerFactory)
    .WithName("GameServer")
    .ListenOn("0.0.0.0:25565")
    .ForwardTo("192.168.1.200:25565")
    .EnableTCP()
    .EnableUDP()
    .WithBufferSize(256 * 1024)
    .Build();

// 手动启动单个 relay
relay.Start();

Console.WriteLine("Press any key to stop...");
Console.ReadKey();

// 停止所有
await manager.StopAllAsync();
```

---

### 场景 2: ASP.NET Core - 使用 IHostedService (推荐)

**Program.cs**:
```csharp
using CatHole;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 配置 Serilog
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration));

// 注册 RelayHostedService
builder.Services.AddHostedService<RelayHostedService>();

var app = builder.Build();

app.MapGet("/", () => "CatHole Relay is running");

app.Run();
```

**appsettings.json**:
```json
{
  "Relays": [
    {
      "Name": "WebProxy",
      "ListenHost": "127.0.0.1:8080",
      "TargetHost": "192.168.1.100:80",
      "TCP": true,
      "UDP": false,
      "BufferSize": 131072,
      "Timeout": 5000
    },
    {
      "Name": "GameServer",
      "ListenHost": "0.0.0.0:25565",
      "TargetHost": "192.168.1.200:25565",
      "TCP": true,
      "UDP": true,
      "BufferSize": 262144,
      "Timeout": 1000
    }
  ]
}
```

---

### 场景 3: ASP.NET Core - 动态管理 API

```csharp
using CatHole.Core;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 注册为单例，便于在 API 中访问
builder.Services.AddSingleton<RelayManager>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new RelayManager(loggerFactory);
});

var app = builder.Build();

// 列出所有 relay
app.MapGet("/api/relays", (RelayManager manager) =>
{
    return Results.Ok(new
    {
        Count = manager.Count,
        Relays = manager.RelayNames
    });
});

// 添加新的 relay
app.MapPost("/api/relays", async (RelayOption option, RelayManager manager) =>
{
    try
    {
        var success = manager.AddRelay(option);
        return success 
            ? Results.Ok(new { Message = $"Relay '{option.Name}' added successfully" })
            : Results.Conflict(new { Message = $"Relay '{option.Name}' already exists" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
});

// 移除 relay
app.MapDelete("/api/relays/{name}", async (string name, RelayManager manager) =>
{
    var success = await manager.RemoveRelayAsync(name);
    return success 
        ? Results.Ok(new { Message = $"Relay '{name}' removed successfully" })
        : Results.NotFound(new { Message = $"Relay '{name}' not found" });
});

app.Run();
```

---

### 场景 4: Windows Service

```csharp
using CatHole.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService() // 配置为 Windows Service
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<RelayHostedService>();
    });

var host = builder.Build();
await host.RunAsync();
```

**安装和管理**:
```powershell
# 发布应用
dotnet publish -c Release -o ./publish

# 安装为 Windows Service
sc create CatHole binPath="C:\path\to\CatHole.exe"
sc start CatHole

# 停止和删除
sc stop CatHole
sc delete CatHole
```

---

### 场景 5: Docker 容器

**Dockerfile**:
```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY publish/ .

EXPOSE 8080
EXPOSE 25565/tcp
EXPOSE 25565/udp

ENTRYPOINT ["dotnet", "CatHole.dll"]
```

**docker-compose.yml**:
```yaml
version: '3.8'
services:
  cathole:
    build: .
    container_name: cathole-relay
    restart: always
    ports:
      - "8080:8080"
      - "25565:25565/tcp"
      - "25565:25565/udp"
    volumes:
      - ./appsettings.json:/app/appsettings.json:ro
      - ./logs:/app/logs
    environment:
      - DOTNET_ENVIRONMENT=Production
```

---

### 场景 6: 高级用法 - 运行时管理

```csharp
using CatHole.Core;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
using var manager = new RelayManager(loggerFactory);

// 批量添加
var options = new[]
{
    new RelayOption { Name = "Web1", ListenHost = "127.0.0.1:8080", TargetHost = "192.168.1.10:80", TCP = true },
    new RelayOption { Name = "Web2", ListenHost = "127.0.0.1:8081", TargetHost = "192.168.1.11:80", TCP = true },
    new RelayOption { Name = "Web3", ListenHost = "127.0.0.1:8082", TargetHost = "192.168.1.12:80", TCP = true }
};

manager.AddRelays(options);

// 检查是否存在
if (manager.Contains("Web1"))
{
    Console.WriteLine("Web1 relay is running");
}

// 获取特定 relay
if (manager.TryGetRelay("Web1", out var relay))
{
    // 可以直接操作 relay
    Console.WriteLine("Found Web1 relay");
}

// 动态移除
await manager.RemoveRelayAsync("Web2");

// 列出所有
foreach (var name in manager.RelayNames)
{
    Console.WriteLine($"Active relay: {name}");
}

// 全部清理
await manager.ClearAsync();
```

---

### 场景 7: 使用 Factory 和验证

```csharp
using CatHole.Core;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var factory = new RelayFactory(loggerFactory);

try
{
    // 创建带验证的 relay
    var option = new RelayOption
    {
        Name = "TestRelay",
        ListenHost = "127.0.0.1:9999",
        TargetHost = "192.168.1.100:80",
        TCP = true,
        BufferSize = 64 * 1024,
        Timeout = 3000
    };

    // 手动验证
    RelayFactory.ValidateOption(option);

    // 创建
    var relay = factory.CreateRelay(option);
    relay.Start();

    Console.WriteLine("Relay started successfully");
}
catch (ArgumentException ex)
{
    Console.WriteLine($"Invalid configuration: {ex.Message}");
}
```

---

## 🔧 配置说明

### RelayOption 属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Name | string | "Unnamed" | Relay 名称，必须唯一 |
| ListenHost | string | "127.0.0.1:45678" | 监听地址（格式: IP:Port） |
| TargetHost | string | "127.0.0.1:45678" | 目标地址（格式: IP:Port） |
| TCP | bool | true | 是否启用 TCP 转发 |
| UDP | bool | true | 是否启用 UDP 转发 |
| BufferSize | int | 131072 (128KB) | 缓冲区大小（字节） |
| Timeout | int | 1000 | 超时时间（毫秒） |

---

## 📊 性能建议

### 缓冲区大小
- **低延迟场景**（游戏、实时通信）: 32-64 KB
- **高吞吐场景**（文件传输、视频流）: 128-256 KB
- **大数据传输**: 512 KB - 1 MB

### 超时设置
- **LAN 环境**: 1000-5000 ms
- **WAN 环境**: 5000-30000 ms
- **不稳定网络**: 30000-60000 ms

---

## 🛠️ 依赖注入集成

### 使用 Microsoft.Extensions.DependencyInjection

```csharp
services.AddSingleton<RelayManager>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new RelayManager(loggerFactory);
});

services.AddSingleton<RelayFactory>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new RelayFactory(loggerFactory);
});

// 或者使用 Hosted Service
services.AddHostedService<RelayHostedService>();
```

---

## 🧪 测试示例

```csharp
using CatHole.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class RelayManagerTests
{
    [Fact]
    public void AddRelay_ShouldSucceed()
    {
        // Arrange
        var loggerFactory = NullLoggerFactory.Instance;
        using var manager = new RelayManager(loggerFactory);
        
        var option = new RelayOption
        {
            Name = "Test",
            ListenHost = "127.0.0.1:9999",
            TargetHost = "127.0.0.1:8888",
            TCP = true
        };

        // Act
        var result = manager.AddRelay(option);

        // Assert
        Assert.True(result);
        Assert.Equal(1, manager.Count);
        Assert.Contains("Test", manager.RelayNames);
    }

    [Fact]
    public async Task RemoveRelay_ShouldSucceed()
    {
        // Arrange
        var loggerFactory = NullLoggerFactory.Instance;
        using var manager = new RelayManager(loggerFactory);
        
        manager.AddRelay(new RelayOption
        {
            Name = "Test",
            ListenHost = "127.0.0.1:9999",
            TargetHost = "127.0.0.1:8888",
            TCP = true
        });

        // Act
        var result = await manager.RemoveRelayAsync("Test");

        // Assert
        Assert.True(result);
        Assert.Equal(0, manager.Count);
    }
}
```

---

## 📝 最佳实践

1. **始终使用 using 处理 RelayManager**
   ```csharp
   using var manager = new RelayManager(loggerFactory);
   // 或
   await using var manager = new RelayManager(loggerFactory);
   ```

2. **捕获并处理异常**
   ```csharp
   try
   {
       manager.AddRelay(option);
   }
   catch (ArgumentException ex)
   {
       // 处理配置错误
   }
   ```

3. **使用唯一的 Relay 名称**
   ```csharp
   var option = new RelayOption
   {
       Name = $"Relay_{Guid.NewGuid():N}",
       // ...
   };
   ```

4. **优雅关闭**
   ```csharp
   // 优先使用异步方法
   await manager.StopAllAsync();
   
   // 或设置超时
   var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
   // 实现带超时的停止逻辑
   ```

---

## 🔍 故障排查

### 常见问题

1. **端口被占用**
   ```
   Error: Address already in use
   解决: 检查端口是否被其他程序占用
   ```

2. **无法连接目标**
   ```
   Error: Connection refused
   解决: 确认目标主机可达，防火墙规则正确
   ```

3. **UDP 转发不工作**
   ```
   检查: UDP 协议需要双向通信，确保防火墙允许 UDP
   ```

---

## 📚 更多资源

- [GitHub Repository](https://github.com/kingsznhone/Cathole)
- [性能优化文档](./RELAY_IMPROVEMENTS.md)
- [API 文档](./API.md)
