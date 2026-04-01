# 云控系统设计文档

## 概述

为 HearthBot 多机部署场景增加云端集中管控能力。管理员通过网页控制台实时查看所有设备的脚本运行状态、账号段位、胜率，并可远程下发指令（开始/停止/切换卡组/切换账号/修改目标段位）。

## 技术栈

| 层 | 技术选型 | 说明 |
|---|---|---|
| 云端后端 | ASP.NET Core + SignalR | REST API + 实时双向通信 |
| 云端数据库 | SQLite | 设备状态、对局记录、指令队列 |
| 网页前端 | Vue 3 + Naive UI | 仪表盘、设备管理、对局记录 |
| 设备端 | SignalR .NET Client | 嵌入现有 BotMain |
| 告警 | Server酱 | 复用现有 `ServerChanChannel` 模块 |
| 认证 | JWT | 单管理员账号 |

## 架构

三层架构：设备端 → 云端服务器 → 网页控制台。

- **设备端**：每台机器跑一个炉石实例，新增 `CloudAgent` 模块通过 SignalR 长连接接入云端。
- **云端服务器**：部署在有公网 IP 的云服务器上，提供 REST API + SignalR Hub。
- **网页控制台**：Vue 3 SPA，通过 SignalR JS Client 实时接收状态推送。

通信方式为 WebSocket（SignalR 自动管理连接、断线重连、心跳）。

## 数据模型

### Devices 表（设备实时快照）

| 字段 | 类型 | 说明 |
|---|---|---|
| DeviceId | TEXT PK | 设备唯一标识（机器名+MAC哈希） |
| DisplayName | TEXT | 设备显示名（可自定义） |
| Status | TEXT | Online / Offline / InGame / Idle |
| CurrentAccount | TEXT | 当前运行的账号名 |
| CurrentRank | TEXT | 当前段位 |
| CurrentDeck | TEXT | 当前使用卡组 |
| CurrentProfile | TEXT | 当前策略文件 |
| GameMode | TEXT | Standard / Wild |
| SessionWins | INT | 本次会话胜场 |
| SessionLosses | INT | 本次会话负场 |
| LastHeartbeat | DATETIME | 最后心跳时间 |
| RegisteredAt | DATETIME | 首次注册时间 |

每次心跳覆盖更新，不存历史。

### GameRecords 表（对局记录）

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | INT PK AUTO | 自增主键 |
| DeviceId | TEXT FK | 关联设备 |
| AccountName | TEXT | 对局账号 |
| Result | TEXT | Win / Loss / Concede |
| MyClass | TEXT | 己方职业 |
| OpponentClass | TEXT | 对手职业 |
| DeckName | TEXT | 使用卡组 |
| ProfileName | TEXT | 使用策略 |
| DurationSeconds | INT | 对局时长（秒） |
| RankBefore | TEXT | 对局前段位 |
| RankAfter | TEXT | 对局后段位 |
| GameMode | TEXT | Standard / Wild |
| PlayedAt | DATETIME | 对局时间 |

追加写入，支持历史查询和按设备/账号/结果/时间筛选。

### PendingCommands 表（指令队列）

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | INT PK AUTO | 自增主键 |
| DeviceId | TEXT FK | 目标设备 |
| CommandType | TEXT | Start / Stop / ChangeDeck / ChangeAccount / ChangeTarget |
| Payload | TEXT (JSON) | 指令参数 |
| Status | TEXT | Pending / Delivered / Executed / Failed |
| CreatedAt | DATETIME | 创建时间 |
| ExecutedAt | DATETIME | 执行时间 |

保证指令可靠送达：设备在线时通过 SignalR 实时推送，离线时存库等设备重连后拉取。

## SignalR 通信协议

### 上行：设备端 → 云端

- **Register(DeviceInfo)** — 设备连接时注册，上报设备标识和可用卡组/策略列表。
- **Heartbeat(DeviceStatus)** — 每30秒上报实时状态（账号、段位、卡组、胜负等）。
- **ReportGame(GameRecord)** — 每局结束后上报对战记录。
- **CommandAck(commandId, success, message?)** — 指令执行结果回报。

### 下行：云端 → 设备端

- **ExecuteCommand(Command)** — 下发操作指令（Start / Stop / ChangeDeck / ChangeAccount / ChangeTarget），设备在当局结束后执行。
- **FetchPendingCommands → Command[]** — 设备重连后拉取离线期间积累的待执行指令。

### 推送：云端 → 网页端

- **DeviceUpdated(DeviceStatus)** — 设备状态变化时推送，实时刷新仪表盘。
- **DeviceOnline(deviceId) / DeviceOffline(deviceId)** — 设备上下线事件。
- **NewGameRecord(GameRecord)** — 新对局记录实时追加。
- **CommandStatusChanged(commandId, status)** — 指令状态更新。

### 连接生命周期

1. 设备启动 → 建立 SignalR 连接 → 调用 Register()
2. 连接成功 → FetchPendingCommands() 补执行离线指令
3. 运行中 → 每30秒 Heartbeat() + 每局结束 ReportGame()
4. 断线 → SignalR 自动重连（指数退避: 0s, 2s, 10s, 30s, 上限60s）
5. 云端检测心跳超时（90s无心跳） → 标记离线 → 触发 Server酱告警

## 网页控制台

### 页面1：仪表盘总览

- 顶部统计卡片：在线设备数、今日总胜率、今日对局数、异常设备数
- 设备实时状态表格：设备名、状态（在线/离线/对局中/空闲）、当前账号、段位、胜/负、胜率、卡组、操作入口
- 所有数据通过 SignalR 实时更新，无需手动刷新

### 页面2：设备管理（弹窗）

- 左侧：设备当前状态详情（状态、账号、段位、卡组、策略、模式、胜负、最后心跳）
- 右侧：远程操作面板
  - 切换卡组（下拉选择，从设备注册时上报的可用列表中选取）
  - 修改目标段位
  - 开始 / 停止按钮
- 所有指令当局结束后生效

### 页面3：对局记录

- 筛选条件：设备、账号、结果（胜/负/投降）、时间范围
- 记录列表：时间、设备、账号、结果、己方职业、对手职业、卡组、用时、段位变化
- 分页展示

### 认证

单管理员账号，JWT Token 认证。登录后 Token 存 localStorage，REST API 和 SignalR 连接均携带 Token。

## 设备端集成

### 新增文件

在 `BotMain/Cloud/` 目录下新增4个文件：

- **CloudAgent.cs** — 核心类，管理 SignalR 连接、心跳定时器、指令接收回调。
- **CloudConfig.cs** — 配置类，包含云端地址、设备ID、认证Token。配置为空时不启动云控。
- **DeviceStatusCollector.cs** — 从 BotService / AccountEntry 采集当前状态，组装 Heartbeat 数据。
- **CommandExecutor.cs** — 接收云端指令，缓存到内存队列，在当局结束后调用 BotService 现有方法执行。

### 集成点

| 触发时机 | 现有代码位置 | CloudAgent 行为 |
|---|---|---|
| 程序启动 | MainViewModel 构造 | 初始化 CloudAgent，建立连接，Register() |
| 主循环每轮 | BotService 主循环 | 采集状态，30秒节流后 Heartbeat() |
| 对局结束 | BotService 胜负统计处 | ReportGame() + 检查并执行待处理指令 |
| 收到指令 | CloudAgent 回调 | CommandExecutor 缓存指令 |
| 账号切换 | AccountQueue 切换逻辑 | 上报新账号信息，重置 Session 统计 |
| 程序退出 | App.OnExit | 断开 SignalR |

### 设计原则

- **非侵入式** — CloudAgent 通过事件/回调与 BotService 交互，不修改核心决策逻辑。
- **可选启用** — CloudConfig 中配置云端地址，留空则不启动云控功能。
- **容错** — 云控断线不影响本地脚本正常运行。
- **最小权限** — CloudAgent 只读取状态 + 修改账号配置，不触碰游戏交互层。

## 告警

复用现有 `BotMain/Notification/ServerChanChannel.cs` 模块。云端服务检测到以下情况时触发告警推送至微信：

- 设备掉线（90秒无心跳）
- 脚本异常（设备上报错误状态）

## 云端项目结构

```
HearthBot.Cloud/                    # 新建 ASP.NET Core 项目
├── Program.cs                      # 入口，配置服务和中间件
├── appsettings.json                # 配置（端口、JWT密钥、Server酱Key等）
├── Data/
│   ├── CloudDbContext.cs           # SQLite 上下文
│   └── Migrations/                 # EF Core 迁移
├── Models/
│   ├── Device.cs
│   ├── GameRecord.cs
│   └── PendingCommand.cs
├── Hubs/
│   └── BotHub.cs                   # SignalR Hub（设备连接）
├── Controllers/
│   ├── AuthController.cs           # 登录、JWT 签发
│   ├── DeviceController.cs         # 设备查询 REST API
│   ├── GameRecordController.cs     # 对局记录查询
│   └── CommandController.cs        # 指令下发
├── Services/
│   ├── DeviceManager.cs            # 设备状态管理、超时检测
│   ├── AlertService.cs             # Server酱告警
│   └── AuthService.cs              # 认证逻辑
└── wwwroot/                        # Vue 前端构建产物（或独立部署）
```

## 前端项目结构

```
hearthbot-web/                      # Vue 3 项目
├── src/
│   ├── views/
│   │   ├── Login.vue               # 登录页
│   │   ├── Dashboard.vue           # 仪表盘总览
│   │   ├── DeviceManage.vue        # 设备管理弹窗
│   │   └── GameRecords.vue         # 对局记录
│   ├── composables/
│   │   ├── useSignalR.ts           # SignalR 连接管理
│   │   └── useAuth.ts              # 认证状态
│   ├── api/
│   │   └── index.ts                # REST API 封装
│   ├── router/
│   │   └── index.ts
│   └── App.vue
├── package.json
└── vite.config.ts
```
