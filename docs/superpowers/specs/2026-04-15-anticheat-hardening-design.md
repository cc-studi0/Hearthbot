# 反作弊加固方案A：最小改动加固

**日期**: 2026-04-15
**范围**: HearthstonePayload 反作弊防御体系升级
**方案**: 在现有 BepInEx + Harmony 架构上补全检测缺口

---

## 背景

游戏于 2026-04-15 更新后，需要对 bot 的反作弊防御进行全面审计和加固。当前系统存在以下风险：

| 检测向量 | 当前防御 | 风险等级 |
|----------|----------|----------|
| AC SDK 上报 | AntiCheatPatches 硬编码拦截6个方法 | 高 |
| 内存特征扫描 | 无防御，HideManagerGameObject=false | 高 |
| Telemetry 遥测上报 | 无拦截 | 中高 |
| 日志泄露 | 无管理，payload_error.log 91MB | 中 |
| 方法变更感知 | 无，更新后patch失效无告警 | 高 |

---

## 设计节1：AntiCheatPatches 动态化

### 问题
`AntiCheatPatches.cs` 硬编码 6 个方法名（`OnLoginComplete`, `TryCallSDK`, `ReportActivity`, `ReportCheat`, `Initialize`, `InitSDK`）。游戏更新新增或重命名方法时 patch 失效，反作弊静默上报。

### 方案
将硬编码方法列表改为全量动态枚举：

1. 通过反射获取 `AntiCheatManager` 的全部 `public/nonpublic/instance/static` 方法
2. **白名单排除**：跳过 `System.Object` 基础方法（`ToString`, `GetHashCode`, `Equals`, `GetType`, `Finalize`）以及构造函数（`.ctor`, `.cctor`）
3. **全部拦截**：剩余方法一律应用 prefix patch
   - 返回 `bool` 的方法 → `ReturnFalse`
   - 返回 `void` 的方法 → `ReturnVoid`（prefix 返回 false 跳过原方法）
   - 其他返回值类型 → `ReturnDefault`（返回该类型默认值）
4. **类名模糊匹配**：如果 `AntiCheatManager` 类名查不到（返回 null），扫描 Assembly-CSharp 中全部类型，查找包含 `ReportCheat`、`InitSDK`、`TryCallSDK` 等特征方法名的类作为候选
5. 记录 patch 的方法数量和名称到 startup log

### 修改文件
- `HearthstonePayload/AntiCheatPatches.cs` — 重写 `Apply()` 方法

---

## 设计节2：Telemetry 上报链路拦截

### 问题
`Blizzard.Telemetry.*` 系列 DLL 在游戏进程中自由运行，可上报 bot 异常行为数据（如后台运行、非正常输入模式）。项目代码零拦截。

### 方案
与 AntiCheatManager 同策略 — 扫描并全量 patch：

1. 枚举 Assembly-CSharp 中类名包含 `Telemetry`、`Analytics`、`Reporting` 关键字的所有类型
2. 对这些类型的全部非基础方法应用 prefix patch（按返回值类型自动选择 ReturnFalse/ReturnVoid/ReturnDefault）
3. 记录发现的类/方法清单到 startup log

### 修改文件
- `HearthstonePayload/AntiCheatPatches.cs` — 在同一文件中新增 Telemetry patch 逻辑（复用 prefix 方法）

---

## 设计节3：BepInEx 指纹消除

### 问题
`HideManagerGameObject = false` 导致 BepInEx 管理器 GameObject 可被游戏内枚举发现。Harmony 日志和 BepInEx 磁盘日志留下额外痕迹。

### 方案
修改 BepInEx 配置：

| 配置项 | 改前 | 改后 | 原因 |
|--------|------|------|------|
| `Chainloader.HideManagerGameObject` | false | true | 隐藏 BepInEx 管理器 GameObject |
| `Harmony.Logger.LogChannels` | Warn, Error | None | 关闭 Harmony 日志减少内存字符串痕迹 |
| `Logging.Disk.Enabled` | true | false | 不写 BepInEx 的 LogOutput.log |
| `Logging.Console.Enabled` | false | false | 保持不变 |

### 修改文件
- `H:\Hearthstone\BepInEx\config\BepInEx.cfg`

---

## 设计节4：日志安全管理

### 问题
`payload_error.log` 91MB 无限增长，`payload_startup.log` 同样无轮转。大体积日志文件是明显的取证指纹。

### 方案
在 `Entry.Awake()` 中、Harmony patch 应用之前调用日志清理：

1. 新增 `LogManager` 静态类，提供 `CleanupLogs()` 方法
2. 启动时检查日志文件大小：
   - `payload_error.log` 超过 5MB → 截断保留最后 1MB，旧内容丢弃
   - `payload_startup.log` 超过 2MB → 同样截断保留最后 512KB
3. 轮转策略：超限时重命名为 `.old`（覆盖已有 `.old`），只保留 1 个备份
4. 确保 `CleanupLogs()` 在 Awake 最早期执行，先于任何新日志写入

### 修改文件
- `HearthstonePayload/LogManager.cs` — 新增文件
- `HearthstonePayload/Entry.cs` — Awake 中添加 `LogManager.CleanupLogs()` 调用

---

## 设计节5：AC SDK 方法变更监控

### 目的
更新后第一次启动就能发现反作弊系统的变化，无需人工比对。

### 方案
1. 首次运行时将 AntiCheatManager + Telemetry 相关类的方法签名列表序列化写入 `ac_method_snapshot.json`（位于 BepInEx/plugins/ 目录）
2. 后续启动时与快照比对：
   - **新增方法** → 自动 patch + 写告警到 startup log（标记 `[AC_CHANGE]`）
   - **方法消失** → 可能类名被改，触发全 Assembly-CSharp 扫描
   - **类名消失** → 触发模糊搜索（按方法特征匹配候选类）
3. 比对结果写入 `payload_startup.log`，BotMain 端可通过读取日志展示变更

### 快照格式
```json
{
  "snapshotDate": "2026-04-15T12:00:00",
  "classes": {
    "AntiCheatManager": ["OnLoginComplete", "TryCallSDK", "ReportActivity", ...],
    "TelemetryManager": ["SendEvent", "Initialize", ...]
  }
}
```

### 修改文件
- `HearthstonePayload/AntiCheatPatches.cs` — 在 Apply 末尾添加快照写入/比对逻辑

---

## 修改文件汇总

| 文件 | 操作 | 说明 |
|------|------|------|
| `HearthstonePayload/AntiCheatPatches.cs` | 重写 | 动态枚举 + Telemetry拦截 + 快照监控 |
| `HearthstonePayload/LogManager.cs` | 新增 | 日志轮转和清理 |
| `HearthstonePayload/Entry.cs` | 修改 | Awake 中添加 LogManager.CleanupLogs() |
| `BepInEx/config/BepInEx.cfg` | 修改 | 隐藏指纹配置 |

## 不修改的文件

- `InactivityPatch.cs` — 当前实现正确，无需变更
- `InputHook.cs` — 虚拟输入方案无需调整
- `ActionExecutor.cs` — 日志写入路径将通过 LogManager 的轮转间接管理
