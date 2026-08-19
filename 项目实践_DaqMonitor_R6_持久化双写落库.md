# R6 · 持久化:内存 + SQLite 双写(数据不再怕重启)

> **定位**:车间断电/重启后,历史数据必须还在。这一篇做 **PointStore 双写**:内存索引管实时,SQLite 管历史,一条 `AddOrUpdate` 同时喂两头。
> **前置**:R5 全绿。**预计敲码**:90 分钟。
> **产出**:SensorRecord/AppDb/PointStore + TestDb,8 个新测试(累计 50)。

---

## 🎯 本篇交付物

```
src/DaqMonitor.Core/Store/
├─ SensorRecord.cs    # 持久化实体(struct SensorPoint 的 class 形态)
├─ AppDbContext.cs    # EF Core DbContext(R6 版只有 sensor_record 表;R9+ 加用户/审计/配方表)
└─ PointStore.cs      # 双写存储:内存索引(旧 API 不变)+ 异步串行落库 + 历史查询
src/DaqMonitor.Tests/
├─ TestDb.cs                      # 每实例独立 SQLite 文件的测试工厂
├─ PointStoreTests.cs             # 3 测试(内存路径)
├─ PointStorePersistenceTests.cs  # 3 测试(落库+时间窗查询)
├─ SerialDeviceTests.cs           # +1 穿管道测试(R4 埋的)
└─ ModbusDeviceTests.cs           # +1 穿管道测试
```

## 📋 需求单(先自己设计,再对照)

| # | 需求 | 验收 |
|---|---|---|
| FR6-1 | 持久化实体 SensorRecord:class、自增主键、(PointId, Time) 复合索引 | FromPoint/ToPoint 互转零丢失 |
| FR6-2 | DbContext(AppDb):sensor_record 表、State 存字符串、三个索引 | EnsureCreated 建库成功 |
| FR6-3 | PointStore 双写:**旧 API(AddOrUpdate/Get/GetAll/GetAlarms)语义不变**,既有调用方零改动;每次 AddOrUpdate 内存同步 + SQLite **异步追加**(不覆盖历史) | R5 的测试一行不改照样绿 |
| FR6-4 | 落库走 Channel 串行队列([双写](kp:dual-write)):采集线程不等盘;SQLite 单写者约束满足 | FlushAsync 后数据全在库 |
| FR6-5 | 落盘失败不丢内存数据、不打断采集(catch 静默,真实工程接日志) | 写库异常内存索引照常服务 |
| FR6-6 | 历史查询 QueryHistoryAsync(点位+时间窗,升序)走 SQLite | 乱序喂入按时间升序返回 |
| FR6-7 | 无参构造兼容:new PointStore() 内部自建临时库工厂——测试不用改 | PointStoreTests 3/3 绿 |
| FR6-8 | 穿管道证明:SerialDevice / ModbusDevice 经管道 BatchReady 写进 store | R4 埋的 2 个测试回填通过 |

**自己先想 15 分钟**:
1. 为什么不直接 `DbSet<SensorPoint>`?(struct 实体 EF Core 8 支持不顺,主键/变更跟踪都别扭——领域模型 ≠ 持久化模型)
2. AddOrUpdate 为什么不 await SaveChanges?(采集管道在 BatchReady 里同步调用,改 async 会污染整条链路;所以 fire-and-forget + 串行泵)
3. SQLite 为什么要串行写?(单写者数据库,并发写直接 locked 异常)
4. 测试库为什么用独立文件而不用 `:memory:`?(EF 每次 CreateDbContext 开新连接,:memory: 库随连接销毁,跨上下文丢表)

## 📚 本篇知识点

- [EF Core](kp:efcore) · [IDbContextFactory](kp:dbfactory) · [双写:内存+库](kp:dual-write) · [xUnit 单元测试](kp:unit-test)

## 🛠️ 参考实现

### ⓪ 装包(Core 和 Tests 都要)

```bash
dotnet add src/DaqMonitor.Core package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.10
dotnet add src/DaqMonitor.Tests package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.10
```
> 💡 断网车间友好:SQLite 单文件零安装。8.0.10 与参考工程对齐(8.0.x LTS 内任意补丁版都能跑)。

### ① SensorRecord —— 持久化实体

> 📂 `src/DaqMonitor.Core/Store/SensorRecord.cs` · namespace `DaqMonitor.Core.Store`
> 🔧 依赖 EF Sqlite(⓪ 已装)
> 💡 "领域模型 ↔ 持久化模型"分离:内存里跑 struct,库里存 class,互转只在 Store 边界

**第 1 步 · 类壳 + 六个属性:一张表的列**(新文件,整段贴)

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Store;

public class SensorRecord
{
    /// <summary>自增主键(持久化用,与业务 PointId 不是一回事)。</summary>
    public int Id { get; set; }

    /// <summary>点位 ID(业务键,来自 SensorPoint.Id)。</summary>
    public int PointId { get; set; }

    /// <summary>采样值。</summary>
    public double Value { get; set; }

    /// <summary>设备状态。</summary>
    public DeviceState State { get; set; }

    /// <summary>采样时间戳(来自 SensorPoint.Timestamp)。</summary>
    public DateTime Time { get; set; }
}
```

📚 **知识点**
- **Id ≠ PointId**:Id 是数据库自增主键(这一行数据的身份证),PointId 是业务键(哪个测点)。**前端类比**:表格渲染的 `key` 用数组下标,行里另有业务 id——两个"标识"各管各的,别混。
- **全是 `{ get; set; }` 可变属性**:EF Core 变更跟踪需要可写属性 + 引用语义。对比 R1 的 `SensorPoint`——readonly 字段的 struct,"出生即定型"。**领域模型要的是不变性,持久化模型要的是可变性**,所以分两个类。
- **属性名叫 Time 而不是 Timestamp**:列名跟着表走(`time` 列);存的是 UTC,SQLite 把 DateTime 存成字符串,UTC 字符串排序 = 时间排序。

**第 2 步 · FromPoint / ToPoint 互转对**(贴进类里,最后一个 `}` 之前)

```csharp
    /// <summary>从领域 struct 转换为持久化实体(纯映射,零副作用)。</summary>
    public static SensorRecord FromPoint(in SensorPoint p) => new()
    {
        PointId = p.Id,
        Value = p.Value,
        State = p.State,
        Time = p.Timestamp
    };

    /// <summary>从持久化实体还原为领域 struct。</summary>
    public SensorPoint ToPoint() => new()
    {
        Id = PointId,
        Value = Value,
        State = State,
        Timestamp = Time
    };
```

📚 **知识点**
- **`in SensorPoint p`**:按只读引用传 struct,避免 24 字节拷贝——高频采集路径的微优化。和 R5 里 `in` 出现的理由相同:struct 传参默认拷贝。
- **`=> new() { ... }` 目标类型 new 表达式**:返回类型已声明,构造处只写 `new()`。**前端类比**:TS 里函数返回类型标注后,返回对象字面量不用再写 `: SensorRecord`。
- **⚠️ 又是时间戳**:FromPoint 抄 `p.Timestamp → Time`,ToPoint 抄 `Time → Timestamp`,双向都手动搬运——R1 的铁律第三次出现。struct 没有自动映射,漏抄一条字段,历史曲线就缺一列,且**编译器不报错**。

<details markdown="1">
<summary>📄 完整文件 SensorRecord.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;

namespace DaqMonitor.Core.Store;

/// <summary>
/// SensorPoint 的持久化形态(EF Core 实体)。
/// SensorPoint 是 struct(高频采集场景下避免装箱开销),EF Core 对 struct 实体不友好:
///   - 无法方便地配置主键/索引;
///   - 跟踪变更语义不清晰。
/// 因此落地到 SQLite 时转成这个 class(经典"领域模型 ↔ 持久化模型"分离)。
///
/// 设计取舍:
///   - 自增主键 Id:避免把业务字段硬塞成主键,写入无锁竞争更小;
///   - 业务字段 PointId + Time 上加复合索引:历史查询"按点位 + 时间窗"最频繁;
///   - Time 用 UTC(SQLite 存字符串排序也正确)。
/// </summary>
public class SensorRecord
{
    /// <summary>自增主键(持久化用,与业务 PointId 不是一回事)。</summary>
    public int Id { get; set; }

    /// <summary>点位 ID(业务键,来自 SensorPoint.Id)。</summary>
    public int PointId { get; set; }

    /// <summary>采样值。</summary>
    public double Value { get; set; }

    /// <summary>设备状态。</summary>
    public DeviceState State { get; set; }

    /// <summary>采样时间戳(来自 SensorPoint.Timestamp)。</summary>
    public DateTime Time { get; set; }

    /// <summary>从领域 struct 转换为持久化实体(纯映射,零副作用)。</summary>
    public static SensorRecord FromPoint(in SensorPoint p) => new()
    {
        PointId = p.Id,
        Value = p.Value,
        State = p.State,
        Time = p.Timestamp
    };

    /// <summary>从持久化实体还原为领域 struct。</summary>
    public SensorPoint ToPoint() => new()
    {
        Id = PointId,
        Value = Value,
        State = State,
        Timestamp = Time
    };
}
```

</details>

### ② AppDb —— DbContext(R6 版)

> 📂 `src/DaqMonitor.Core/Store/AppDbContext.cs` · 类名是 `AppDb`
> 💡 参考工程的同名文件里还有用户/审计/配方表(R9+ 的内容),R6 版先只留 sensor_record——**R9+ 做到那篇时再往 OnModelCreating 里加表**,种子代码结构不变

**第 1 步 · 类壳 + 构造 + DbSet:表声明**(新文件,整段贴)

```csharp
using DaqMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Core.Store;

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    /// <summary>历史样本表(按时间追加,几乎不更新)。</summary>
    public DbSet<SensorRecord> Records => Set<SensorRecord>();
}
```

📚 **知识点**
- **DbContext 是"一场对话",不是"一个数据库"**:一个 AppDb 实例 = 一次打开的会话(带变更跟踪缓存),用完就丢。数据库本体是那个 .db 文件。**前端类比**:DbContext ≈ 一个打开的 WebSocket 连接,不是服务器本身;连接要短用短弃,文件才是持久的东西。
- **ctor 只收 `DbContextOptions<AppDb>`**:连什么库(连接串、驱动)由外部注入——本类不写死 `Data Source=...`,测试才能换文件、生产才能换路径。**依赖注入的"配置也走构造"**。
- **`DbSet<SensorRecord> Records => Set<SensorRecord>()`**:属性只是"这张表的门牌",表达式体 `=>` 每次取现成的 Set,不为每上下文存一份。有这个属性,后面 LINQ 查询才能写 `db.Records.Where(...)`。

**第 2 步 · OnModelCreating:一次说清表结构**(贴进类里,最后一个 `}` 之前)

```csharp
    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        var e = mb.Entity<SensorRecord>();
        e.ToTable("sensor_record");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();

        e.Property(x => x.PointId).HasColumnName("point_id").IsRequired();
        e.Property(x => x.Value).HasColumnName("value").IsRequired();
        e.Property(x => x.State).HasColumnName("state").HasConversion<string>().IsRequired();
        e.Property(x => x.Time).HasColumnName("time").IsRequired();

        // 主查询路径:按点位 + 时间窗。SQLite 的 ASC 索引同时支持 ASC / DESC 查询。
        e.HasIndex(x => new { x.PointId, x.Time }).HasDatabaseName("ix_record_point_time");
        e.HasIndex(x => x.PointId).HasDatabaseName("ix_record_point");
        e.HasIndex(x => x.Time).HasDatabaseName("ix_record_time");
    }
```

📚 **知识点**
- **Fluent API 是"建表图纸"**:这一段只在 `EnsureCreated()` 建库时执行一次,生成 SQL 的 `CREATE TABLE`。**前端类比**:像 Prisma 的 `schema.prisma` / TypeORM 的 entity 装饰器——ORM 的表定义,只是 C# 用链式调用写。
- **`HasConversion<string>()` 让枚举存字符串**:`state` 列存 `"OverRange"` 而不是 `3`。好处:用 sqlite3 手工查库能看懂;将来 DeviceState 中间插一个新枚举值,老数据不会错位。**前端类比**:常量存语义名而非 magic number,一个道理。
- **三个索引 = 三种查询姿势**:`(PointId, Time)` 复合索引服务主路径"按点位+时间窗查历史"(最左前缀:先定位点位,再在时间上二分);`PointId` 单列服务"按点位聚合";`Time` 单列服务"全点位扫时间段"。索引不是越多越好——每多一个,写入就多一份维护成本;这里写入频率 20 条/秒,三个索引的代价可以忽略。
- **匿名类型 `new { x.PointId, x.Time }` 声明复合索引**:C# 的匿名类型在这里只是"列清单"的载体,EF 拿反射读列名。

<details markdown="1">
<summary>📄 完整文件 AppDbContext.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Core.Store;

/// <summary>
/// EF Core DbContext:把 SensorPoint 历史样本落到 SQLite。
///
/// 为什么不直接 DbSet<SensorPoint>:
///   SensorPoint 是 struct,EF Core 8 对 owned struct / keyless struct 实体支持仍不顺畅,
///   配置索引与变更跟踪也麻烦。改用一个等价的 class SensorRecord 作持久化模型,
///   领域层(采集/报警/UI)继续用 struct,互转只在 Store 边界发生——仓储模式的好处。
///
/// 索引策略:
///   - (PointId, Time) 复合索引:覆盖"按点位 + 时间窗查历史"的主查询路径;
///   - PointId 单列索引:覆盖"按点位统计最新值/聚合"的次要路径;
///   - Time 单列索引:覆盖"全点位按时间窗扫描"。
/// </summary>
public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    /// <summary>历史样本表(按时间追加,几乎不更新)。</summary>
    public DbSet<SensorRecord> Records => Set<SensorRecord>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        var e = mb.Entity<SensorRecord>();
        e.ToTable("sensor_record");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();

        e.Property(x => x.PointId).HasColumnName("point_id").IsRequired();
        e.Property(x => x.Value).HasColumnName("value").IsRequired();
        e.Property(x => x.State).HasColumnName("state").HasConversion<string>().IsRequired();
        e.Property(x => x.Time).HasColumnName("time").IsRequired();

        // 主查询路径:按点位 + 时间窗。SQLite 的 ASC 索引同时支持 ASC / DESC 查询。
        e.HasIndex(x => new { x.PointId, x.Time }).HasDatabaseName("ix_record_point_time");
        e.HasIndex(x => x.PointId).HasDatabaseName("ix_record_point");
        e.HasIndex(x => x.Time).HasDatabaseName("ix_record_time");
    }
}
```

</details>

### ③ PointStore —— 双写存储(本篇主角)

> 📂 `src/DaqMonitor.Core/Store/PointStore.cs`
> 💡 三层人格:**旧 API 纯内存(实时永不阻塞)** + **Channel 串行写泵(满足 SQLite 单写者)** + **历史查询走库**
> 🗺️ **新手读码地图**(顺着 AddOrUpdate 一条数据的去向看):1. **第一层·内存**:`AddOrUpdate` 在 `lock` 里同步更新字典+列表——UI 实时表/报警判断只走这层,**永远不碰磁盘**,所以再高频也不会卡采集 2. **第二层·落盘**:同一方法里紧接着 `TryWrite` 把记录塞进 `_writeQueue`(Channel),塞完立刻返回,**不等写库**——这就是"fire-and-forget" 3. 谁来写库?构造时 `Task.Run(PumpWritesAsync)` 起的**单消费者写泵**,后台一条条取、一条条 SaveChanges。为什么必须串行:SQLite 同一时刻只允许一个写者,并发写会锁冲突——Channel 天然 FIFO + 单读者,比手工加锁稳 4. **第三层·历史查询**:`QueryHistoryAsync` 拿 EF 出一个新 DbContext 按"点位+时间窗"查库,和内存层互不干扰 5. `FlushAsync` 是关机前的好习惯:让队列收尾、等写泵把尾巴写完,防止最后几条丢在队列里。**前端类比**:内存层 ≈ 组件 state(毫秒级读写),写泵 ≈ 埋点上报的批处理队列(sentry/redux 持久化中间件都是"先入队、后台慢慢写"的双写套路)。

#### 🏗️ 为什么这样设计:为什么要"双写"(内存 + 数据库),而不是 UI 直接读写库?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| 直接读写库(UI 每次查询打 SQLite) | 一份数据零冗余 | 磁盘毫秒级 vs 实时表毫秒内刷新——100Hz 点位表直接卡死;SQLite 单写者,采集写+UI 读并发锁冲突 |
| 内存实时层 + 异步落库层(选定) | 多一份内存字典 | 两层要同步(靠同一个 AddOrUpdate 入口);进程崩了最多丢队列里几条 |

**为什么选它**:两条链路对数据的**延迟要求差三个数量级**——实时表/报警判断要"这一拍的值"(内存字典,微秒),历史查询/报表要"过去一个月"(数据库,毫秒无所谓)。硬用一层同时伺候两边,必有一边被拖死。双写把矛盾拆开:**写入路径**是"内存同步改 + Channel 入队即返回"(采集永不阻塞),**落库**由单消费者写泵慢慢消化(SQLite 单写者约束天然满足)。丢数据风险有界:崩机只丢队列里未落盘的几条,采集类数据可接受——真不可接受再加 WAL/批量事务,同样是隔离在写泵一处改。

**不这样会怎样**:UI 实时表每 200ms SELECT 一次,配采集线程不停 INSERT,SQLite 文件锁打架,界面卡、采集丢——两个都是"偶发、难复现"的现场故障。

**🎤 面试一句话**:"存储我做了双写:内存字典伺候实时表和报警(微秒级),Channel 写泵串行落 SQLite(毫秒级,天然满足单写者)。两条链路延迟要求差三个数量级,拆开各自满足;崩机最多丢队列尾巴几条,风险有界。"

#### 🏗️ 为什么这样设计:DbContext 为什么用 IDbContextFactory 每次现造,而不是做成单例复用?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| 单例 DbContext(一个实例到处用) | 省构造开销 | **DbContext 非线程安全**:写泵和历史查询并发用它,随机崩;变更跟踪缓存越积越大(内存泄漏) |
| `IDbContextFactory<AppDb>` 按需造、用完 Dispose(选定) | 多一层工厂 | 每次 new 有微小开销(毫秒以下) |

**为什么选它**:项目里至少**两处并发用库**:写泵在后台线程 INSERT,历史查询在 UI 发起的任务里 SELECT——DbContext 同时只伺候一个。工厂模式让"每次操作一个短命上下文"成为强制结构:线程安全(各用各的)、变更跟踪即用即弃(不泄漏)、SaveChanges 语义清晰。EF Core 官方在多线程场景就是推荐 factory——它不是性能优化,是**正确性**设计。前端类比:每次请求 new 一个请求作用域,而不是全局共享一个 store。

**不这样会怎样**:单例 DbContext 并发读写,抛 "A second operation started on this context"(不定时出现,跟线程调度有关)——现场表现为"跑半天突然崩一次",最难定位的那种。

**🎤 面试一句话**:"DbContext 我用 factory 按需创建用完即弃,因为写泵和历史查询是两个并发场景,而 DbContext 非线程安全;短命上下文还让变更跟踪即用即弃,不积累内存。单例上下文并发用会随机抛 second operation 异常。"

#### 🏗️ 为什么这样设计:为什么选 SQLite,而不是 SQL Server 或时序数据库?

**当时面临的选择**:

| 方案 | 优点 | 代价 |
|---|---|---|
| SQL Server / MySQL | 并发强、生态全 | **要装服务**——断网友装版上位机现场根本没有 DBA;License/运维成本 |
| InfluxDB/TimescaleDB 时序库 | 时序场景性能最强 | 又一个要安装维护的引擎;上位机交付物变得沉重 |
| SQLite 单文件嵌入(选定) | "零部署":一个 db 文件跟着 exe 走 | 单写者(靠写泵串行化解决);不适合多进程共享 |

**为什么选它**:上位机的典型交付形态是**拷个文件夹到工控机就能跑**——很多车间刻意断网隔离(air-gapped),现场没有数据库管理员,任何"先装个数据库服务"的方案都把部署复杂度翻倍。SQLite 是进程内嵌入库,数据一个文件,备份=拷文件,EF Core 支持完整。写入瓶颈(单写者)已被"单消费者写泵 + 批量"化解,而单机单应用的采集量(百点×1Hz~100Hz)远没到 SQLite 天花板。**选型跟着部署环境走,不是跟着跑分走**。

**🎤 面试一句话**:"落库选 SQLite:上位机交付到断网车间,要的是拷文件夹就能跑、备份就是拷文件——嵌入式零部署压倒一切。单写者限制用串行写泵化解,单机采集量离它的天花板还很远。哪天要多机共享数据,再换 PostgreSQL 是 EF Core 换个 provider 的事。"

**第 1 步 · 骨架:字段全家福 + 私有构造 + 写泵 + Dispose**(新文件,整段贴)

> 这一步把**生命周期核心**一次贴齐:私有构造函数里 `Task.Run(PumpWritesAsync)` 启动写泵(构造即启动,和 R5 管道同一个契约),所以构造和泵必须同贴;类声明了 `: IDisposable`,Dispose 也得当场兑现,否则编译不过。

```csharp
using DaqMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace DaqMonitor.Core.Store;

public class PointStore : IDisposable
{
    // ===== 内存索引(旧 API 用,保持原语义不变) =====
    private readonly List<SensorPoint> _points = new();
    private readonly Dictionary<int, SensorPoint> _byId = new();
    private readonly object _gate = new();

    // ===== SQLite 持久化 =====
    private readonly IDbContextFactory<AppDb> _dbFactory;
    private readonly bool _ownsFactory;
    // 串行化所有写库操作(SQLite 单写者)。Channel 比 lock + SemaphoreSlim 更稳:
    // 写任务不会堆积在 ThreadPool,且按 FIFO 顺序执行。
    private readonly System.Threading.Channels.Channel<SensorRecord> _writeQueue =
        System.Threading.Channels.Channel.CreateUnbounded<SensorRecord>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Task _writePump;
    private volatile bool _disposed;

    private PointStore(IDbContextFactory<AppDb> factory, bool ownsFactory)
    {
        _dbFactory = factory;
        _ownsFactory = ownsFactory;
        // 启动写库泵(单消费者,串行执行 SaveChanges,天然满足 SQLite 单写者)
        _writePump = Task.Run(PumpWritesAsync);
    }

    // ===================== 内部实现 =====================

    private async Task PumpWritesAsync()
    {
        var reader = _writeQueue.Reader;
        try
        {
            await foreach (var rec in reader.ReadAllAsync())
            {
                if (_disposed) break;
                try
                {
                    await using var db = await _dbFactory.CreateDbContextAsync();
                    db.Records.Add(rec);
                    await db.SaveChangesAsync();
                }
                catch
                {
                    // 落盘失败不阻断采集——实时路径已用内存索引服务。
                    // 真实工程可注入 ILogger 在此上报。
                }
            }
        }
        catch
        {
            // pump 异常不应冒泡到 finalizer
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 通知 pump 收尾
        _writeQueue.Writer.TryComplete();
        try { _writePump.Wait(TimeSpan.FromSeconds(2)); } catch { /* 忽略超时 */ }
        if (_ownsFactory && _dbFactory is IDisposable d) d.Dispose();
    }
}
```

📚 **知识点**
- **三份"家当"分区放**:①内存索引(`_points` 列表保序 + `_byId` 字典按 Id 秒查,一份数据两个索引——前端表格"行数组 + byId map"完全同构);②SQLite 持久化(`_dbFactory` 出 DbContext、`_writeQueue` 排队、`_writePump` 后台写);③`_gate` 锁保护内存区。
- **`SingleReader = true`**:向 Channel 声明"只有一个消费者",运行时可以省掉读者侧的竞争开销。**SQLite 单写者约束在类型系统层面就表达出来了**。
- **`Task.Run(PumpWritesAsync)` 起泵、Dispose 里 `TryComplete()` 关泵**:writer 完成后 `ReadAllAsync` 自然走到尽头,泵退出。这是 Channel 的标准关停姿势——不用 kill、不用 flag 轮询。前端类比:socket.io 的 graceful close,先 `end()` 再等 drain。
- **两层 catch 的"静默降级"**:内层 catch 兜"单条写失败"(磁盘满也只丢这一条,泵继续跑);外层 catch 兜"泵本身崩了"(不让异常冒泡到线程池把进程打挂)。真实工程在内层注入 ILogger 上报——这里留了口子。
- **`volatile bool _disposed`**:Dispose 在 UI 线程调用,`_disposed` 在泵线程读——volatile 保证读到的不是过期缓存。最简单的跨线程可见性工具。

**第 2 步 · 旧 API 四件套:第一层·内存**(贴进类里,最后一个 `}` 之前)

```csharp
    // ===================== 旧 API(不变) =====================

    /// <summary>更新或插入当前点位(内存索引同步刷新;SQLite 异步追加一行)。</summary>
    public void AddOrUpdate(SensorPoint p)
    {
        // 1) 内存索引同步更新(旧语义)
        lock (_gate)
        {
            _byId[p.Id] = p;
            var idx = _points.FindIndex(x => x.Id == p.Id);
            if (idx >= 0) _points[idx] = p;
            else _points.Add(p);
        }

        // 2) 异步落盘:仅追加(历史库要保留全部时序样本,不因"更新当前值"而丢历史)
        if (!_disposed)
        {
            _writeQueue.Writer.TryWrite(SensorRecord.FromPoint(p));
        }
    }

    public SensorPoint? Get(int id)
    {
        lock (_gate) return _byId.TryGetValue(id, out var p) ? p : null;
    }

    public IReadOnlyList<SensorPoint> GetAll()
    {
        lock (_gate) return _points.ToList();
    }

    /// <summary>返回超阈值的点(实时报警直接复用)。</summary>
    public IReadOnlyList<SensorPoint> GetAlarms(double threshold)
    {
        lock (_gate) return _points.Where(p => p.Value > threshold).ToList();
    }
```

📚 **知识点**
- **AddOrUpdate 是双写的"分叉点"**:上半段 `lock` 里同步刷内存(UI/报警毫秒级可见),下半段 `TryWrite` 塞队列后**立刻返回**——采集线程从头到尾没等过磁盘。这一行 `TryWrite` 就是 FR6-4"采集线程不等盘"的全部实现。
- **对内存是"更新",对库是"追加"**:`_byId[p.Id] = p` 覆盖当前值,但库里每个历史样本都留着——"当前值"和"历史序列"是两个概念,前者内存管,后者 SQLite 管。想看"温度探针昨天 3 点的值"?只能靠历史库。
- **双索引同步维护**:`_byId` 字典 + `_points` 列表在锁内同时更新,`FindIndex` 找到就替换(保住列表顺序),找不到就 Add。列表保序是 UI 表格的渲染顺序。
- **`Get` 返回 `SensorPoint?`**:可空值类型——"没这个点位"和"值是 0"必须区分。前端类比:接口返回 `null` vs `0` 的区别,坑过每个人的。
- **GetAll 的 `.ToList()` 是防逃逸快照**:锁内拷一份再放锁,调用方遍历时即使别的线程正在写内存区也不会崩——和 R2 设备快照 `ToList()` 同一招。

**第 3 步 · 历史查询 + FlushAsync:第三层·走库**(继续贴进类里)

```csharp
    // ===================== 新 API:历史查询走 SQLite =====================

    /// <summary>按点位 + 时间窗查历史(含闭区间 [from, to])。结果按时间升序返回。</summary>
    public async Task<List<SensorPoint>> QueryHistoryAsync(
        int pointId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Records.AsNoTracking()
            .Where(r => r.PointId == pointId && r.Time >= from && r.Time <= to)
            .OrderBy(r => r.Time)
            .ToListAsync(ct);
        return rows.ConvertAll(r => r.ToPoint());
    }

    /// <summary>同步版本(少量遗留调用方或测试用)。</summary>
    public List<SensorPoint> QueryHistory(int pointId, DateTime from, DateTime to)
        => QueryHistoryAsync(pointId, from, to).GetAwaiter().GetResult();

    /// <summary>把队列里残留的写任务全部落盘(停机 / 测试结束前可调用,避免丢点)。</summary>
    public Task FlushAsync(TimeSpan? timeout = null)
    {
        // 让 writer 完成 → pump 把剩余记录写完 → 等 pump 退出
        _writeQueue.Writer.TryComplete();
        return _writePump.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
    }
```

📚 **知识点**
- **查询走库不走内存**:历史在 SQLite,内存里只有"当前值"。`AsNoTracking()` 告诉 EF"只读不改",跳过变更跟踪缓存——纯查询场景的标准提速手段。**前端类比**:GET 请求带 no-cache,别把响应塞进缓存。
- **`await using`**:异步版 using——DbContext 释放连接可能涉及 IO,用 `await using var db` 确保异步释放。整个 LINQ 链(`Where/OrderBy/ToListAsync`)翻译成一条 SQL 发给 SQLite,`ToListAsync` 才真正执行——和 JS Promise 的"thenable 链不 await 不跑"神似。
- **`ConvertAll(r => r.ToPoint())`**:出边界时把 SensorRecord 还原回领域 struct——Store 的门里门外两种模型,转换只发生在门上(仓储模式的核心纪律)。
- **`FlushAsync` 三步收尾**:①`TryComplete()` 关闸(不再有新写入)→ ②泵把队列里剩的写完 → ③`WaitAsync` 等泵退出。测试里每次断言前都要 `await store.FlushAsync()`——**不等它,断言可能在数据落库前就跑了**(异步测试的头号假失败)。
- **`GetAwaiter().GetResult()` 同步堵**:给遗留同步调用方的桥。 UI 线程上别用它(死锁风险),新代码一律 async。

**第 4 步 · 两个公共构造 + 默认工厂:出生证明**(继续贴进类里;本步三个成员互相依赖,一起贴)

> 无参构造调用 `CreateDefaultFactory()`,而它 `new InlineFactory<AppDb>(...)`——三个成员绑在一辆车上,拆开贴中间态编译不过。

```csharp
    /// <summary>
    /// 兼容旧调用方的无参构造:内部建一个本地文件 SQLite 工厂。
    /// 既有测试都是 new PointStore(),不需要改一行就能继续跑——同时还免费拿到 SQLite 持久化能力。
    /// </summary>
    public PointStore()
        : this(CreateDefaultFactory(), ownsFactory: true)
    {
    }

    /// <summary>DI 构造:由 Bootstrapper 注入工厂(生产路径)。</summary>
    public PointStore(IDbContextFactory<AppDb> factory) : this(factory, ownsFactory: false) { }

    /// <summary>默认工厂:系统临时目录下的 daq-{guid}.db(并行测试不互相串库)。</summary>
    private static IDbContextFactory<AppDb> CreateDefaultFactory()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"daq-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDb>()
            .UseSqlite($"Data Source={path}")
            .Options;

        // 轻量工厂:每次 CreateDbContext 返回新实例(SQLite 连接随 DbContext 释放而关闭)
        var factory = new InlineFactory<AppDb>(() => new AppDb(options));

        // 建库(同步,构造期一次性——旧 API 是同步的,不能让 Get/GetAll 等测试因为库未建而失败)
        using (var init = factory.CreateDbContext())
        {
            init.Database.EnsureCreated();
        }
        return factory;
    }

    /// <summary>极简 IDbContextFactory 实现:用委托拼装(options 已闭包进委托里)。</summary>
    private sealed class InlineFactory<TCtx> : IDbContextFactory<TCtx> where TCtx : DbContext
    {
        private readonly Func<TCtx> _factory;
        public InlineFactory(Func<TCtx> factory) => _factory = factory;
        public TCtx CreateDbContext() => _factory();
    }
```

📚 **知识点**
- **构造链:`this(...)` 串联**:两个公共构造(无参/DI)都委托给第 1 步的私有主构造——真正的初始化逻辑只有一份。无参构造传 `ownsFactory: true`,Dispose 时连工厂一起释放("我建的摊我收");DI 构造传 false(工厂归 DI 容器管)。**谁创建谁负责销毁**,和 React useEffect 的"谁订阅谁清理"同一条纪律。
- **无参构造 = 向后兼容的口子**:R5 的既有测试全是 `new PointStore()`,这一行 `: this(CreateDefaultFactory(), ...)` 让它们**一行不改**继续跑,还免费升级成"真的在写 SQLite"(FR6-7)。老代码零迁移成本是设计出来的,不是碰运气。
- **`daq-{Guid:N}.db` 唯一文件名**:xUnit 并行跑测试,共用一个库文件就是互相踩——GUID 文件名天然隔离。和 R4 模拟设备"每实例独立状态"同一思路。
- **`EnsureCreated()` 在构造期同步执行**:建库必须赶在第一次 Get/GetAll 之前——旧 API 是同步的,不能让老测试"库还没建就查"而翻车。构造函数里做重活通常是坏味道,但"一次性初始化"是合法例外。
- **`InlineFactory<TCtx>` 用委托实现接口**:`options` 已经闭包进 lambda,工厂类只存一个 `Func<TCtx>`。**前端类比**:库里要一个 class,你用一个箭头函数包一下交差——12 行搞定一个"自定义工厂"。

<details markdown="1">
<summary>📄 完整文件 PointStore.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace DaqMonitor.Core.Store;

/// <summary>
/// 点位存储:内存索引(列表 + 字典,UI / 报警实时用)+ SQLite 持久化(历史查询用)。
///
/// 设计要点:
///   - 旧 API(AddOrUpdate / Get / GetAll / GetAlarms)保持不变——既有调用方零改动。
///     这一层只读内存索引,保证实时路径永不阻塞在 IO 上。
///   - 每次 AddOrUpdate 同时把一条记录追加到 SQLite,采集线程不等待落盘(fire-and-forget
///     + 内部串行队列,避免并发写冲突)。SQLite 单写者特性决定了必须串行化写入。
///   - QueryHistoryAsync / QueryHistory 走 SQLite,支持"按点位 + 时间窗"历史回放。
///
/// 为什么写盘是异步串行而不是直接 await:
///   AddOrUpdate 是同步 API(采集管道在 BatchReady 里同步喂入),改成 Task 会污染整条链路;
///   用一个 Channel 把写任务串行化,既不阻塞采集,又满足 SQLite 的单写者约束。
/// </summary>
public class PointStore : IDisposable
{
    // ===== 内存索引(旧 API 用,保持原语义不变) =====
    private readonly List<SensorPoint> _points = new();
    private readonly Dictionary<int, SensorPoint> _byId = new();
    private readonly object _gate = new();

    // ===== SQLite 持久化 =====
    private readonly IDbContextFactory<AppDb> _dbFactory;
    private readonly bool _ownsFactory;
    // 串行化所有写库操作(SQLite 单写者)。Channel 比 lock + SemaphoreSlim 更稳:
    // 写任务不会堆积在 ThreadPool,且按 FIFO 顺序执行。
    private readonly System.Threading.Channels.Channel<SensorRecord> _writeQueue =
        System.Threading.Channels.Channel.CreateUnbounded<SensorRecord>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Task _writePump;
    private volatile bool _disposed;

    /// <summary>
    /// 兼容旧调用方的无参构造:内部建一个本地文件 SQLite 工厂。
    /// 既有测试都是 new PointStore(),不需要改一行就能继续跑——同时还免费拿到 SQLite 持久化能力。
    /// </summary>
    public PointStore()
        : this(CreateDefaultFactory(), ownsFactory: true)
    {
    }

    /// <summary>DI 构造:由 Bootstrapper 注入工厂(生产路径)。</summary>
    public PointStore(IDbContextFactory<AppDb> factory) : this(factory, ownsFactory: false) { }

    private PointStore(IDbContextFactory<AppDb> factory, bool ownsFactory)
    {
        _dbFactory = factory;
        _ownsFactory = ownsFactory;
        // 启动写库泵(单消费者,串行执行 SaveChanges,天然满足 SQLite 单写者)
        _writePump = Task.Run(PumpWritesAsync);
    }

    // ===================== 旧 API(不变) =====================

    /// <summary>更新或插入当前点位(内存索引同步刷新;SQLite 异步追加一行)。</summary>
    public void AddOrUpdate(SensorPoint p)
    {
        // 1) 内存索引同步更新(旧语义)
        lock (_gate)
        {
            _byId[p.Id] = p;
            var idx = _points.FindIndex(x => x.Id == p.Id);
            if (idx >= 0) _points[idx] = p;
            else _points.Add(p);
        }

        // 2) 异步落盘:仅追加(历史库要保留全部时序样本,不因"更新当前值"而丢历史)
        if (!_disposed)
        {
            _writeQueue.Writer.TryWrite(SensorRecord.FromPoint(p));
        }
    }

    public SensorPoint? Get(int id)
    {
        lock (_gate) return _byId.TryGetValue(id, out var p) ? p : null;
    }

    public IReadOnlyList<SensorPoint> GetAll()
    {
        lock (_gate) return _points.ToList();
    }

    /// <summary>返回超阈值的点(实时报警直接复用)。</summary>
    public IReadOnlyList<SensorPoint> GetAlarms(double threshold)
    {
        lock (_gate) return _points.Where(p => p.Value > threshold).ToList();
    }

    // ===================== 新 API:历史查询走 SQLite =====================

    /// <summary>按点位 + 时间窗查历史(含闭区间 [from, to])。结果按时间升序返回。</summary>
    public async Task<List<SensorPoint>> QueryHistoryAsync(
        int pointId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Records.AsNoTracking()
            .Where(r => r.PointId == pointId && r.Time >= from && r.Time <= to)
            .OrderBy(r => r.Time)
            .ToListAsync(ct);
        return rows.ConvertAll(r => r.ToPoint());
    }

    /// <summary>同步版本(少量遗留调用方或测试用)。</summary>
    public List<SensorPoint> QueryHistory(int pointId, DateTime from, DateTime to)
        => QueryHistoryAsync(pointId, from, to).GetAwaiter().GetResult();

    /// <summary>把队列里残留的写任务全部落盘(停机 / 测试结束前可调用,避免丢点)。</summary>
    public Task FlushAsync(TimeSpan? timeout = null)
    {
        // 让 writer 完成 → pump 把剩余记录写完 → 等 pump 退出
        _writeQueue.Writer.TryComplete();
        return _writePump.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
    }

    // ===================== 内部实现 =====================

    private async Task PumpWritesAsync()
    {
        var reader = _writeQueue.Reader;
        try
        {
            await foreach (var rec in reader.ReadAllAsync())
            {
                if (_disposed) break;
                try
                {
                    await using var db = await _dbFactory.CreateDbContextAsync();
                    db.Records.Add(rec);
                    await db.SaveChangesAsync();
                }
                catch
                {
                    // 落盘失败不阻断采集——实时路径已用内存索引服务。
                    // 真实工程可注入 ILogger 在此上报。
                }
            }
        }
        catch
        {
            // pump 异常不应冒泡到 finalizer
        }
    }

    /// <summary>默认工厂:系统临时目录下的 daq-{guid}.db(并行测试不互相串库)。</summary>
    private static IDbContextFactory<AppDb> CreateDefaultFactory()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"daq-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDb>()
            .UseSqlite($"Data Source={path}")
            .Options;

        // 轻量工厂:每次 CreateDbContext 返回新实例(SQLite 连接随 DbContext 释放而关闭)
        var factory = new InlineFactory<AppDb>(() => new AppDb(options));

        // 建库(同步,构造期一次性——旧 API 是同步的,不能让 Get/GetAll 等测试因为库未建而失败)
        using (var init = factory.CreateDbContext())
        {
            init.Database.EnsureCreated();
        }
        return factory;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 通知 pump 收尾
        _writeQueue.Writer.TryComplete();
        try { _writePump.Wait(TimeSpan.FromSeconds(2)); } catch { /* 忽略超时 */ }
        if (_ownsFactory && _dbFactory is IDisposable d) d.Dispose();
    }

    /// <summary>极简 IDbContextFactory 实现:用委托拼装(options 已闭包进委托里)。</summary>
    private sealed class InlineFactory<TCtx> : IDbContextFactory<TCtx> where TCtx : DbContext
    {
        private readonly Func<TCtx> _factory;
        public InlineFactory(Func<TCtx> factory) => _factory = factory;
        public TCtx CreateDbContext() => _factory();
    }
}
```

</details>

### ④ TestDb —— 测试库工厂

> 📂 `src/DaqMonitor.Tests/TestDb.cs`
> 💡 用独立文件不用 `:memory:`:EF 每次 CreateDbContext 开新连接,:memory: 库随连接销毁,跨上下文丢表

**第 1 步 · 外壳:字段 + 私有构造 + 接口转发 + Dispose**(新文件,整段贴)

```csharp
using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Tests;

public sealed class TestDb : IDisposable, IDbContextFactory<AppDb>
{
    public Factory FactoryInstance { get; }

    private TestDb(string file, Factory factory)
    {
        _file = file;
        FactoryInstance = factory;
    }
    private readonly string _file;

    // 直接实现 IDbContextFactory<AppDb>,让 new PointStore(fixture) 不需要隐式转换
    public AppDb CreateDbContext() => FactoryInstance.CreateDbContext();
    public Task<AppDb> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => FactoryInstance.CreateDbContextAsync(cancellationToken);

    public void Dispose()
    {
        try { if (System.IO.File.Exists(_file)) System.IO.File.Delete(_file); } catch { /* 忽略 */ }
    }
}
```

📚 **知识点**
- **私有构造 + 静态 Create 命名工厂**:外界只能 `TestDb.Create()` 出生,保证"生成唯一文件名 → 建库 → 打包"这套流程不会被人绕过半截。**前端类比**:组件不暴露 constructor,只给一个 `createXxx()` 工厂函数,防止使用方 new 出一个没初始化完的半成品。
- **外壳直接实现 `IDbContextFactory<AppDb>`**:两个方法都转发给内部的 `FactoryInstance`——这样测试里 `new PointStore(fixture)` 直接传 fixture,不需要 `.FactoryInstance` 再取一次。外壳是**适配器**:对 PointStore 装作工厂,对内部包着真工厂。
- **Dispose 删临时文件**:测试不留垃圾。`try/catch` 兜底是因为 Windows 上文件可能还被 SQLite 连占着——删不掉就算了,在 %TEMP% 里不碍事。测试夹具"自清扫"是好习惯。

**第 2 步 · Create() + 嵌套 Factory:工厂本体**(贴进类里,最后一个 `}` 之前;两个成员互相引用,一起贴)

```csharp
    /// <summary>测试用工厂(每实例对应一个唯一文件 SQLite)。</summary>
    public sealed class Factory : IDbContextFactory<AppDb>
    {
        private readonly string _path;
        public Factory(string path)
        {
            _path = path;
            // 一次性建库建表
            using var init = CreateDbContext();
            init.Database.EnsureCreated();
        }
        public AppDb CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDb>()
                .UseSqlite($"Data Source={_path}")
                .Options;
            return new AppDb(options);
        }
        public Task<AppDb> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    public static TestDb Create()
    {
        var file = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"daq-test-{Guid.NewGuid():N}.db");
        var factory = new Factory(file);
        return new TestDb(file, factory);
    }
```

📚 **知识点**
- **Factory 的构造函数里就 `EnsureCreated()`**:工厂一出生库就建好——后面不管谁 CreateDbContext,表一定在。把"一次性初始化"压进构造,使用方零心智负担。
- **`CreateDbContextAsync` 用 `Task.FromResult` 包装**:`new AppDb(options)` 是纯内存操作(还没连库),不需要 async——但接口要求返回 Task。`Task.FromResult(同步结果)` = "同步值冒充已完成任务",最轻的桥接写法。
- **每个 TestDb 一个 `daq-test-{guid}.db`**:xUnit 默认并行跑不同测试类,共享一个库文件 = 互相覆盖。GUID 文件名 + Dispose 删除 = 并行安全 + 不留垃圾。
- **为什么不用 `:memory:`(FR 单里问过)**:EF 每次 `CreateDbContext` 开新连接,而 `:memory:` 库**随连接关闭而销毁**——上一个上下文建的表,下一个上下文里根本不存在。文件库才能跨上下文共享表结构。

<details markdown="1">
<summary>📄 完整文件 TestDb.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace DaqMonitor.Tests;

/// <summary>
/// 测试用 AppDb 工厂:本地文件 SQLite(每实例一个唯一文件,并行测试不互相串库)。
///
/// 用法:
///   using var fixture = TestDb.Create();
///   using var store = new PointStore(fixture);   // 走 DI 构造(TestDb 直接实现 IDbContextFactory)
/// </summary>
public sealed class TestDb : IDisposable, IDbContextFactory<AppDb>
{
    /// <summary>测试用工厂(每实例对应一个唯一文件 SQLite)。</summary>
    public sealed class Factory : IDbContextFactory<AppDb>
    {
        private readonly string _path;
        public Factory(string path)
        {
            _path = path;
            // 一次性建库建表
            using var init = CreateDbContext();
            init.Database.EnsureCreated();
        }
        public AppDb CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDb>()
                .UseSqlite($"Data Source={_path}")
                .Options;
            return new AppDb(options);
        }
        public Task<AppDb> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    public Factory FactoryInstance { get; }

    private TestDb(string file, Factory factory)
    {
        _file = file;
        FactoryInstance = factory;
    }
    private readonly string _file;

    public static TestDb Create()
    {
        var file = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"daq-test-{Guid.NewGuid():N}.db");
        var factory = new Factory(file);
        return new TestDb(file, factory);
    }

    // 直接实现 IDbContextFactory<AppDb>,让 new PointStore(fixture) 不需要隐式转换
    public AppDb CreateDbContext() => FactoryInstance.CreateDbContext();
    public Task<AppDb> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => FactoryInstance.CreateDbContextAsync(cancellationToken);

    public void Dispose()
    {
        try { if (System.IO.File.Exists(_file)) System.IO.File.Delete(_file); } catch { /* 忽略 */ }
    }
}
```

</details>

### ⑤ 测试(6 个新文件测试 + 2 个回填)

> 📂 `src/DaqMonitor.Tests/PointStoreTests.cs`(无参构造,纯内存语义)
> 测试文件天然适合搭积木:先立一个空测试类(能编译),再把测试方法一块块贴进去。

**第 1 步 · 空测试类 + 插入更新测试**(新文件,整段贴)

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Xunit;

namespace DaqMonitor.Tests;

public class PointStoreTests
{
    [Fact]
    public void AddOrUpdate_InsertsThenUpdatesSameId()
    {
        var store = new PointStore();
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 10 });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 99 });

        Assert.Single(store.GetAll());
        Assert.Equal(99d, store.GetAll().First().Value);
    }
}
```

📚 **知识点**
- **这个测试是 FR6-3 的守门员**:同 Id 两次 AddOrUpdate,`Assert.Single` 证明内存里**只有一行**(更新不增行),值是 99(覆盖成功)。R5 时代它就存在,R6 改完内核它**一行不改照样绿**——这就是"旧 API 语义不变"的验收方式:用测试钉死。
- **没写 `using var store`**:PointStore 实现 IDisposable,测试里不释放也能接受——无参构造建的是 %TEMP% 下的临时库,进程退出即弃。严格派可以加 `using`,不影响结果。

**第 2 步 · Get 未知 Id + GetAlarms 过滤**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public void Get_ReturnsNullForUnknownId()
    {
        var store = new PointStore();
        Assert.Null(store.Get(999));
    }

    [Fact]
    public void GetAlarms_ReturnsOnlyAboveThreshold()
    {
        var store = new PointStore();
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 10 });
        store.AddOrUpdate(new SensorPoint { Id = 2, Value = 200 });

        var alarms = store.GetAlarms(100);
        Assert.Single(alarms);
        Assert.Equal(2, alarms[0].Id);
    }
```

📚 **知识点**
- **`Assert.Null(store.Get(999))` 钉住可空语义**:"查无此点"返回 null 而不是抛异常——UI 层拿到 null 显示"--",拿到异常直接崩。接口契约用测试写死,后来者不敢改。
- **GetAlarms 测试喂了 2 条只中 1 条**:阈值 100,值 10 的不中、200 的中。测试数据必须"有中有不中",全中的数据证明不了过滤逻辑。**前端类比**:测 filter 函数不能只喂全过的数组。

<details markdown="1">
<summary>📄 完整文件 PointStoreTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Xunit;

namespace DaqMonitor.Tests;

public class PointStoreTests
{
    [Fact]
    public void AddOrUpdate_InsertsThenUpdatesSameId()
    {
        var store = new PointStore();
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 10 });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 99 });

        Assert.Single(store.GetAll());
        Assert.Equal(99d, store.GetAll().First().Value);
    }

    [Fact]
    public void Get_ReturnsNullForUnknownId()
    {
        var store = new PointStore();
        Assert.Null(store.Get(999));
    }

    [Fact]
    public void GetAlarms_ReturnsOnlyAboveThreshold()
    {
        var store = new PointStore();
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 10 });
        store.AddOrUpdate(new SensorPoint { Id = 2, Value = 200 });

        var alarms = store.GetAlarms(100);
        Assert.Single(alarms);
        Assert.Equal(2, alarms[0].Id);
    }
}
```

</details>

> 📂 `src/DaqMonitor.Tests/PointStorePersistenceTests.cs`(注入 TestDb,验持久化)

**第 1 步 · 空测试类 + 时间窗查询测试**(新文件,整段贴)

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Xunit;

namespace DaqMonitor.Tests;

public class PointStorePersistenceTests
{
    [Fact]
    public async Task QueryHistoryAsync_Returns_PersistedSamples_InTimeOrder()
    {
        // Arrange:用 TestDb(每实例独立 SQLite 文件),并行测试不互相串库
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);   // 走 DI 构造(注入工厂)

        var t0 = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
        var windowStart = t0.AddSeconds(-1);
        var windowEnd = t0.AddMinutes(10);

        // 三条样本:1 条在窗外、2 条在窗内(乱序喂入,验证结果按时间升序)
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 1.0, Timestamp = t0 });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 3.0, Timestamp = t0.AddSeconds(2) });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 0.5, Timestamp = t0.AddSeconds(-30) }); // 窗外

        // 等异步写库泵把队列写完
        await store.FlushAsync();

        // Act
        var history = await store.QueryHistoryAsync(1, windowStart, windowEnd);

        // Assert:2 条窗内样本,按时间升序
        Assert.Equal(2, history.Count);
        Assert.Equal(1.0d, history[0].Value);
        Assert.Equal(3.0d, history[1].Value);
    }
}
```

📚 **知识点**
- **这是本篇的主测试**:一条数据走完"AddOrUpdate → Channel → 泵 → SQLite → QueryHistoryAsync 查回"整个环。它绿了,FR6-3/4/6 三个需求同时有证据。
- **`await store.FlushAsync()` 是异步测试的"对表"**:AddOrUpdate 是 fire-and-forget,断言时数据可能还在队列里。FlushAsync 关闸等泵写完——不加这行,测试偶尔绿偶尔红(flap),最难查的那种。
- **测试数据故意"乱序喂 + 一条窗外"**:喂入顺序 1.0 → 3.0 → 0.5(时间乱序),断言 `history[0]=1.0, history[1]=3.0`(升序)——证明 ORDER BY 在干活;窗外的 0.5 查不回——证明 WHERE 过滤在干活。**喂法即考点**。
- **固定时刻 `new DateTime(2026, 8, 4, ...)` 而不是 UtcNow**:时间窗测试用绝对时间,窗内窗外清清楚楚;用"现在"当基准,边界样本会随执行时刻漂移。

**第 2 步 · 点位过滤 + 双写一致性**(贴进类里,最后一个 `}` 之前)

```csharp
    [Fact]
    public async Task QueryHistoryAsync_FiltersByPointId()
    {
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);

        var t = DateTime.UtcNow;
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 10, Timestamp = t });
        store.AddOrUpdate(new SensorPoint { Id = 2, Value = 20, Timestamp = t });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 30, Timestamp = t.AddSeconds(1) });
        await store.FlushAsync();

        var onlyPoint1 = await store.QueryHistoryAsync(1, t.AddSeconds(-1), t.AddSeconds(10));
        Assert.Equal(2, onlyPoint1.Count);
        Assert.All(onlyPoint1, p => Assert.Equal(1, p.Id));
    }

    [Fact]
    public async Task Memory_And_Sqlite_StayConsistent_AfterAddOrUpdate()
    {
        // 内存索引(实时)与 SQLite(历史)的值要一致——这是双写模式的基本契约
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);

        var t = DateTime.UtcNow;
        store.AddOrUpdate(new SensorPoint { Id = 5, Value = 42.5, Timestamp = t });
        await store.FlushAsync();

        var memCurrent = store.Get(5);
        Assert.NotNull(memCurrent);
        Assert.Equal(42.5d, memCurrent!.Value.Value);

        var hist = await store.QueryHistoryAsync(5, t.AddSeconds(-1), t.AddSeconds(1));
        Assert.Single(hist);
        Assert.Equal(42.5d, hist[0].Value);
    }
```

📚 **知识点**
- **`Assert.All(onlyPoint1, p => Assert.Equal(1, p.Id))`**:对集合每个元素断言,任何一个不满足整个测试红。比手写 foreach + 标志位干净。**前端类比**:jest 的 `forEach(expect(...))` 升级版。
- **第三个测试是双写契约的"对账"**:同一条数据,内存查(`Get(5)`)和库里查(`QueryHistoryAsync`)必须给出同一个值 42.5。双写最大的隐患就是"两边各说各话"(写了一边漏了另一边),对账测试把这个隐患钉死。
- **`memCurrent!.Value.Value` 双重解引用**:`Get` 返回 `SensorPoint?`(可空包装),属性 `.Value` 拆出 struct,再 `.Value` 是 struct 的字段。两个 `!`/`.Value` 各拆一层——可空值类型用起来啰嗦,但换来"null 和 0 不混"的精确性。

<details markdown="1">
<summary>📄 完整文件 PointStorePersistenceTests.cs(对答案 / 整体粘贴用)</summary>

```csharp
using DaqMonitor.Core.Models;
using DaqMonitor.Core.Store;
using Xunit;

namespace DaqMonitor.Tests;

/// <summary>
/// PointStore 持久化路径测试:验证 AddOrUpdate 后,QueryHistoryAsync 能从 SQLite 查到历史样本。
/// 覆盖"双写(内存 + SQLite)+ 时间窗查询"的核心契约。
/// </summary>
public class PointStorePersistenceTests
{
    [Fact]
    public async Task QueryHistoryAsync_Returns_PersistedSamples_InTimeOrder()
    {
        // Arrange:用 TestDb(每实例独立 SQLite 文件),并行测试不互相串库
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);   // 走 DI 构造(注入工厂)

        var t0 = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
        var windowStart = t0.AddSeconds(-1);
        var windowEnd = t0.AddMinutes(10);

        // 三条样本:1 条在窗外、2 条在窗内(乱序喂入,验证结果按时间升序)
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 1.0, Timestamp = t0 });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 3.0, Timestamp = t0.AddSeconds(2) });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 0.5, Timestamp = t0.AddSeconds(-30) }); // 窗外

        // 等异步写库泵把队列写完
        await store.FlushAsync();

        // Act
        var history = await store.QueryHistoryAsync(1, windowStart, windowEnd);

        // Assert:2 条窗内样本,按时间升序
        Assert.Equal(2, history.Count);
        Assert.Equal(1.0d, history[0].Value);
        Assert.Equal(3.0d, history[1].Value);
    }

    [Fact]
    public async Task QueryHistoryAsync_FiltersByPointId()
    {
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);

        var t = DateTime.UtcNow;
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 10, Timestamp = t });
        store.AddOrUpdate(new SensorPoint { Id = 2, Value = 20, Timestamp = t });
        store.AddOrUpdate(new SensorPoint { Id = 1, Value = 30, Timestamp = t.AddSeconds(1) });
        await store.FlushAsync();

        var onlyPoint1 = await store.QueryHistoryAsync(1, t.AddSeconds(-1), t.AddSeconds(10));
        Assert.Equal(2, onlyPoint1.Count);
        Assert.All(onlyPoint1, p => Assert.Equal(1, p.Id));
    }

    [Fact]
    public async Task Memory_And_Sqlite_StayConsistent_AfterAddOrUpdate()
    {
        // 内存索引(实时)与 SQLite(历史)的值要一致——这是双写模式的基本契约
        using var fixture = TestDb.Create();
        using var store = new PointStore(fixture);

        var t = DateTime.UtcNow;
        store.AddOrUpdate(new SensorPoint { Id = 5, Value = 42.5, Timestamp = t });
        await store.FlushAsync();

        var memCurrent = store.Get(5);
        Assert.NotNull(memCurrent);
        Assert.Equal(42.5d, memCurrent!.Value.Value);

        var hist = await store.QueryHistoryAsync(5, t.AddSeconds(-1), t.AddSeconds(1));
        Assert.Single(hist);
        Assert.Equal(42.5d, hist[0].Value);
    }
}
```

</details>

> 📂 `SerialDeviceTests.cs` — **贴进 SerialDeviceTests 类里**(最后一个 `}` 之前)追加 R4 埋的穿管道测试;文件头同步补 `using DaqMonitor.Core.Acquisition; using DaqMonitor.Core.Models; using DaqMonitor.Core.Store;`

```csharp
    [Fact]
    public async Task SerialDevice_ThroughPipeline_ProducesPoints_InStore()
    {
        // 最强证明:SerialDevice 直接挂到统一采集管道 + 存储,整条链路不碰 UI、不碰真实硬件
        var ch = new LoopbackSerialChannel();
        var dev = new SerialDevice(1, "SER", ch);
        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var store = new PointStore();

        pipeline.Register(dev);
        dev.Connect();
        ch.Write(FrameParser.Build(1, 123.5));

        var done = new TaskCompletionSource<bool>();
        pipeline.BatchReady += (_, b) =>
        {
            foreach (var p in b) store.AddOrUpdate(p);
            if (b.Count > 0) done.TrySetResult(true);
        };

        await Task.WhenAny(done.Task, Task.Delay(2000));
        dev.Disconnect();

        Assert.True(store.GetAll().Any(p => p.Id == 1 && Math.Abs(p.Value - 123.5) < 1e-6),
            "SerialDevice 经管道写入存储失败——'换设备 UI 零改动'未成立");
    }
```

📚 **知识点**
- **这是 R4 埋的"证据链最后一环"**:R4 只验证设备吐点位,R6 有了 PointStore 才能验证**完整生产链**:`Loopback 通道写帧 → SerialDevice 解析 → 管道攒批 → BatchReady → store 落双写`。整条链不碰 UI、不碰真实硬件——"每一层都可测"的兑现。
- **`ch.Write(FrameParser.Build(1, 123.5))` 模拟"设备说话"**:回环通道写进去什么,设备就读到什么。测试里扮设备的是测试代码自己——R2 的替身思路一路用到底。
- **`Math.Abs(p.Value - 123.5) < 1e-6` 浮点比较**:永远不要 `== 123.5`,浮点经过字节序转换有微损。**前端类比**:金额比较用 `Math.abs(a-b) < epsilon`,不是 `a === b`。

> 📂 `ModbusDeviceTests.cs` — **同样贴进 ModbusDeviceTests 类里**(最后一个 `}` 之前)追加穿管道测试;using 同步补 `using DaqMonitor.Core.Acquisition; using DaqMonitor.Core.Store;`

```csharp
    [Fact]
    public async Task ModbusDevice_ThroughPipeline_ProducesPoints_InStore()
    {
        var dev = new ModbusDevice(1, "MB", slave: 1,
            new[] { new ModbusDevice.RegisterMap(1, 0, "float") }, simulate: true);
        using var pipeline = new AcquisitionPipeline(TimeSpan.FromMilliseconds(50));
        var store = new PointStore();

        pipeline.Register(dev);
        dev.Connect();

        var done = new TaskCompletionSource<bool>();
        pipeline.BatchReady += (_, b) =>
        {
            foreach (var p in b) store.AddOrUpdate(p);
            if (b.Count > 0) done.TrySetResult(true);
        };

        await Task.WhenAny(done.Task, Task.Delay(2000));
        dev.Disconnect();

        Assert.True(store.GetAll().Any(p => p.Id == 1), "ModbusDevice 经管道写入存储失败——'换设备 UI 零改动'未成立");
    }
```

📚 **知识点**
- **和上面 Serial 那条是"双保险"**:两种协议(自定义帧 / Modbus 寄存器)走同一条管道进同一个 store——证明**换设备不改管道、不改存储**,分层解耦的收益在测试上兑现。
- **`RegisterMap(1, 0, "float")` 走 R5 的工程换算**:`"float"` 类型让 Modbus 寄存器值按浮点字节序解析再标定——R3 的字节序 + R5 的 EngineeringConverter 在真实设备路径上串起来了。

## ✅ 验证(必做)

```bash
dotnet build
dotnet test
```
**期望输出(关键行)**:
```
已成功生成。 → 0 个警告 0 个错误
已通过! - 失败: 0,通过: 50 ... DaqMonitor.Tests.dll
```
(50 = 之前 42 + 本篇 8)

## ✅ 验收清单

- [ ] build 0 错 0 警,test 50/50 绿
- [ ] 能回答:为什么 SensorPoint 是 struct 而 SensorRecord 是 class?各自服务谁?(采集高频/GC;EF 持久化)
- [ ] 能回答:AddOrUpdate 里"内存同步、SQLite 异步",为什么这么分?(实时路径不能等盘;历史允许秒级延迟)
- [ ] 能回答:为什么 AddOrUpdate 对 SQLite 是"追加"不是"更新"?(历史库保留全部时序样本,当前值更新只发生在内存索引)
- [ ] 打开 %TEMP% 目录能看到 daq-*.db 文件——用 `sqlite3` 打开看 sensor_record 表(可选,装了 sqlite 工具的话)
- [ ] git commit -m "R6: 持久化双写 EF Core+SQLite+8测试"

## 🎤 面试怎么讲这一篇

> "存储层是双写:PointStore 的实时 API 只碰内存索引,UI 和报警永不阻塞在 IO;同时每条样本进 Channel 串行队列,由后台泵逐条落 SQLite——串行是关键,SQLite 是单写者数据库,并发写会锁冲突。领域模型 SensorPoint 是 struct,持久化模型 SensorRecord 是 class,EF Core 对 struct 实体支持不好,这层转换放在仓储边界。查询按点位加时间窗走 (PointId,Time) 复合索引。落库失败静默降级到只有内存,不让磁盘问题打断采集。测试用独立临时文件库,不用 :memory:,因为 EF 每次开新连接而内存库随连接销毁。"

**✅ 打卡[ ]**
