# CatHole 架构设计

## 📐 设计理念

CatHole 采用**分层架构**设计，将核心功能与框架集成分离，实现**框架无关**的可复用组件。

---

## 🏗️ 架构层次

```
┌─────────────────────────────────────────────────────────┐
│           Application Layer (应用层)                     │
│  ┌─────────────────────┐  ┌──────────────────────────┐ │
│  │  Console App        │  │  ASP.NET Core / WinSvc  │ │
│  └─────────────────────┘  └──────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│       Integration Layer (集成层 - CatHole)              │
│  ┌──────────────────┐  ┌───────────────────────────┐  │
│  │ RelayService     │  │  RelayHostedService       │  │
│  │ (配置加载)        │  │  (IHostedService 实现)     │  │
│  └──────────────────┘  └───────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│         Core Layer (核心层 - CatHole.Core)              │
│  ┌──────────────────┐  ┌───────────────────────────┐  │
│  │  RelayManager    │  │    RelayFactory           │  │
│  │  (生命周期管理)   │  │    (工厂/构建器)           │  │
│  └──────────────────┘  └───────────────────────────┘  │
│  ┌──────────────────┐  ┌───────────────────────────┐  │
│  │  Relay           │  │    RelayOption            │  │
│  │  (转发实例)       │  │    (配置模型)              │  │
│  └──────────────────┘  └───────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

---

## 📦 核心组件

### 1. **Relay** - 转发实例
**职责**: 执行单个端口转发任务
- TCP/UDP 协议处理
- 连接管理和流复制
- 资源清理

**特点**:
- 独立运行
- 支持 TCP 半关闭
- UDP tunnel 自动超时

---

### 2. **RelayManager** - 管理器
**职责**: 管理多个 Relay 实例的生命周期
- 添加/移除/查询 Relay
- 批量操作（启动/停止/清理）
- 线程安全的并发管理

**特点**:
- ✅ 框架无关
- ✅ 实现 `IDisposable` 和 `IAsyncDisposable`
- ✅ 线程安全（使用 `ConcurrentDictionary` 和 `Lock`）

**API**:
```csharp
public class RelayManager : IDisposable, IAsyncDisposable
{
    bool AddRelay(RelayOption option)
    int AddRelays(IEnumerable<RelayOption> options)
    Task<bool> RemoveRelayAsync(string name)
    bool TryGetRelay(string name, out Relay? relay)
    void StartAll()
    Task StopAllAsync()
    Task ClearAsync()
    bool Contains(string name)
}
```

---

### 3. **RelayFactory** - 工厂类
**职责**: 创建和验证 Relay 实例
- 配置验证
- 实例创建
- 提供构建器模式

**特点**:
- ✅ 严格验证（端口格式、参数范围）
- ✅ 友好的错误提示
- ✅ Fluent API（链式调用）

**API**:
```csharp
public class RelayFactory
{
    Relay CreateRelay(RelayOption option)
    static void ValidateOption(RelayOption option)
    static RelayBuilder CreateBuilder(ILoggerFactory)
}

public class RelayBuilder
{
    RelayBuilder WithName(string name)
    RelayBuilder ListenOn(string host)
    RelayBuilder ForwardTo(string host)
    RelayBuilder EnableTCP(bool enabled = true)
    RelayBuilder EnableUDP(bool enabled = true)
    RelayBuilder TCPOnly()
    RelayBuilder UDPOnly()
    Relay Build()
}
```

---

### 4. **RelayOption** - 配置模型
**职责**: 封装 Relay 配置参数

**属性**:
```csharp
public class RelayOption
{
    string Name          // Relay 名称
    string ListenHost    // 监听地址 (IP:Port)
    string TargetHost    // 目标地址 (IP:Port)
    bool TCP             // 启用 TCP
    bool UDP             // 启用 UDP
    int BufferSize       // 缓冲区大小
    int Timeout          // 超时时间(ms)
}
```

---

### 5. **RelayService** - ASP.NET Core 服务
**职责**: ASP.NET Core 集成适配器
- 从 `IConfiguration` 加载配置
- 封装 `RelayManager`
- 提供便捷的启动/停止方法

**特点**:
- ✅ 自动从 `appsettings.json` 加载
- ✅ 暴露 `Manager` 属性供高级操作
- ✅ 简化的 API

---

### 6. **RelayHostedService** - IHostedService 实现
**职责**: 标准的 ASP.NET Core Hosted Service
- 应用启动时自动启动 Relay
- 应用关闭时优雅停止
- 完全集成生命周期

**特点**:
- ✅ 实现 `IHostedService`
- ✅ 自动生命周期管理
- ✅ 异常处理和日志

---

## 🎯 设计优势

### 1. **框架无关的核心**
```csharp
// CatHole.Core 不依赖任何特定框架
// 只依赖：
// - Microsoft.Extensions.Logging.Abstractions
// - System.Net.Sockets
// - System.Collections.Concurrent
```

**优势**: 可以在任何 .NET 应用中使用（Console、WinForms、WPF、Xamarin、Unity 等）

---

### 2. **职责分离**
| 组件 | 职责 | 依赖 |
|------|------|------|
| `Relay` | 转发逻辑 | 无 |
| `RelayManager` | 生命周期管理 | Relay |
| `RelayFactory` | 创建和验证 | Relay |
| `RelayService` | 配置加载 | RelayManager + IConfiguration |
| `RelayHostedService` | 生命周期集成 | RelayManager + IHostedService |

---

### 3. **灵活的使用方式**

#### 方式 A: 直接使用 Relay
```csharp
var relay = new Relay(option, logger);
relay.Start();
// ...
await relay.StopAsync();
```

#### 方式 B: 使用 RelayManager
```csharp
using var manager = new RelayManager(loggerFactory);
manager.AddRelay(option);
// ...
await manager.StopAllAsync();
```

#### 方式 C: 使用 Builder
```csharp
var relay = RelayFactory.CreateBuilder(loggerFactory)
    .WithName("Test")
    .ListenOn("127.0.0.1:8080")
    .ForwardTo("192.168.1.100:80")
    .TCPOnly()
    .Build();
```

#### 方式 D: ASP.NET Core HostedService
```csharp
builder.Services.AddHostedService<RelayHostedService>();
```

---

## 🔒 线程安全

### 并发保护措施

1. **RelayManager**
   - 使用 `ConcurrentDictionary<string, Relay>` 存储 Relay
   - 使用 `Lock` 保护管理操作
   - 所有公开方法线程安全

2. **Relay**
   - 使用 `ConcurrentBag<Task>` 跟踪任务
   - 使用 `ConcurrentDictionary<IPEndPoint, UdpTunnelInfo>` 管理 UDP tunnel
   - 使用 `Interlocked` 和 `Lock` 保护状态

---

## 📈 扩展性

### 未来扩展点

1. **自定义协议支持**
   ```csharp
   public interface IProtocolHandler
   {
       Task ForwardAsync(Stream source, Stream target, CancellationToken ct);
   }
   ```

2. **中间件管道**
   ```csharp
   relay.UseMiddleware<LoggingMiddleware>();
   relay.UseMiddleware<RateLimitMiddleware>();
   relay.UseMiddleware<EncryptionMiddleware>();
   ```

3. **插件系统**
   ```csharp
   public interface IRelayPlugin
   {
       void OnConnectionStart(ConnectionInfo info);
       void OnConnectionEnd(ConnectionInfo info);
       void OnDataTransfer(DataInfo info);
   }
   ```

4. **配置提供者**
   ```csharp
   public interface IRelayConfigProvider
   {
       Task<IEnumerable<RelayOption>> LoadAsync();
       Task SaveAsync(IEnumerable<RelayOption> options);
   }
   ```

---

## 🧪 可测试性

### 依赖注入友好

```csharp
// 单元测试
var loggerFactory = NullLoggerFactory.Instance;
using var manager = new RelayManager(loggerFactory);

// Mock 配置
var mockConfig = new Mock<IConfiguration>();
var service = new RelayService(mockConfig.Object, logger, loggerFactory);
```

### 接口分离

```csharp
// 未来可以提取接口
public interface IRelayManager
{
    bool AddRelay(RelayOption option);
    Task<bool> RemoveRelayAsync(string name);
    Task StopAllAsync();
}
```

---

## 📊 性能考虑

### 1. **零拷贝优化**（已实现）
- 使用 `Memory<byte>` 和 `ReadOnlyMemory<byte>`
- 避免不必要的内存分配

### 2. **任务池化**（未来）
- 使用 `ObjectPool<T>` 复用对象
- 减少 GC 压力

### 3. **缓冲区管理**（未来）
- 使用 `ArrayPool<byte>` 管理缓冲区
- 可配置的缓冲区大小

---

## 🎓 最佳实践

### 1. 使用依赖注入
```csharp
services.AddSingleton<RelayManager>();
services.AddHostedService<RelayHostedService>();
```

### 2. 正确处理生命周期
```csharp
// ✅ 推荐
await using var manager = new RelayManager(loggerFactory);

// ❌ 避免
var manager = new RelayManager(loggerFactory);
// 忘记 dispose
```

### 3. 错误处理
```csharp
try
{
    manager.AddRelay(option);
}
catch (ArgumentException ex)
{
    // 配置错误
    logger.LogError(ex, "Invalid configuration");
}
catch (Exception ex)
{
    // 其他错误
    logger.LogError(ex, "Failed to add relay");
}
```

---

## 📝 总结

CatHole 的架构设计遵循以下原则：

✅ **单一职责** - 每个组件职责明确  
✅ **开放封闭** - 易于扩展，不需修改核心  
✅ **依赖倒置** - 依赖抽象（ILogger），不依赖具体实现  
✅ **接口隔离** - 最小化依赖，框架无关  
✅ **里氏替换** - 组件可以独立使用或组合使用  

这使得 CatHole 成为一个**高度可复用、易于测试、便于维护**的端口转发库。
