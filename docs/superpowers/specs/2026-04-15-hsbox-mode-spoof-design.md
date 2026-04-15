# 盒子模式伪装：标准传说 → 狂野推荐

## 背景

网易炉石盒子（HSAng.exe）在**标准模式传说段位**不提供出牌建议，但**狂野模式**和**休闲模式**均正常提供。需要劫持盒子的模式识别，使其在标准传说时仍返回出牌推荐。

## 约束

- **绝不修改 Hearthstone.exe 游戏进程内存**
- **绝不修改炉石日志文件**
- 仅操作盒子进程（HSAng.exe）及其 CEF 内嵌浏览器

## 架构

两层策略，按优先级依次尝试：

```
┌─────────────────────────────────────────────┐
│  Layer 1: CDP Fetch 拦截                      │
│  通过已有CDP通道 → 启用Fetch域 →              │
│  拦截盒子CEF发出的API请求 →                    │
│  改写 format_type / game_type 参数            │
│  (纯网络层，不碰任何进程内存)                    │
├─────────────────────────────────────────────┤
│  Layer 2: Frida 盒子内存补丁 (仅HSAng.exe)     │
│  如果Layer1无效（推荐请求压根没发出）→           │
│  Frida注入HSAng.exe →                        │
│  FT_STANDARD → FT_WILD 内存替换              │
│  (只改盒子进程，不碰Hearthstone.exe)            │
└─────────────────────────────────────────────┘
```

**触发条件**：`BotService._modeIndex == 0`（标准）且当前为传说段位。狂野/休闲/竞技场不触发。

---

## Layer 1: CDP Fetch 拦截

### 原理

盒子的 CEF 浏览器通过 HTTP 请求与网易 API 通信（`hs-game-api.lushi.163.com`、`hs-web-match.lushi.163.com` 等）。Chrome DevTools Protocol 的 `Fetch` 域允许在请求发出前拦截并修改。

项目已有 CDP 连接基础设施（`HsBoxRecommendationProvider.cs` 通过 `127.0.0.1:9222` 连接盒子的 CDP 端口）。

### 工作流程

```
BotService 检测到 [标准 + 传说]
        │
        ▼
CDP 连接盒子 CEF (已有通道，127.0.0.1:9222)
        │
        ▼
发送 Fetch.enable → 注册请求拦截规则
  patterns: [{ urlPattern: "*lushi.163.com*" },
             { urlPattern: "*hsreplay*" }]
        │
        ▼
盒子发出 API 请求 → CDP 触发 Fetch.requestPaused 事件
        │
        ▼
检查请求 URL / Body 中是否含 FT_STANDARD / RANKED_STANDARD
        │
  ┌─────┴──────┐
  │ 有          │ 无
  ▼             ▼
改写为          原样放行
FT_WILD /      Fetch.continueRequest
RANKED_WILD
  │
  ▼
Fetch.continueRequest(修改后的请求)
```

### 核心 CDP 命令

| 命令 | 用途 |
|------|------|
| `Fetch.enable` | 注册拦截模式，指定 URL pattern |
| `Fetch.requestPaused` 事件 | 获取被暂停的请求详情（URL、headers、postData） |
| `Fetch.continueRequest` | 放行或以修改后的参数放行 |
| `Fetch.fulfillRequest` | 如需改写响应体时使用 |
| `Fetch.disable` | 对局结束时清理 |

### 替换规则

| 原始值 | 替换为 | 出现位置 |
|--------|--------|----------|
| `FT_STANDARD` | `FT_WILD` | 请求URL参数、请求体JSON |
| `RANKED_STANDARD` | `RANKED_WILD` | HSReplay API 的 GameType / game_type_filter |
| `format_type=2` | `format_type=1` | 网易API数值型格式参数（2=标准，1=狂野） |

### 实现

- 新增类 `HsBoxModeSpoofer.cs`（BotMain 项目）
- 在 `HsBoxRecommendationProvider` 建立 CDP 连接后调用
- 复用已有的 WebSocket CDP 通信基础（`SendCommand` / `ReceiveResponseById`）
- 通过异步事件监听处理 `Fetch.requestPaused`

### 生命周期

- 标准传说进入对局时：`Fetch.enable`
- 对局结束或模式切换时：`Fetch.disable`
- 盒子重启 / CDP 断开时：自动重连并重新启用

---

## Layer 2: Frida 盒子内存补丁

### 触发条件

Layer 1 启用后，连续 3 轮（约 15 秒）未拦截到任何含 `format_type` 的请求 → 判定模式检查在 C++ 层 → 自动降级。

### 原理

用 Frida 注入 HSAng.exe 进程，扫描其可写内存段，将 `FT_STANDARD` 字符串（ASCII + UTF-16LE）替换为 `FT_WILD`（补 null 对齐长度）。

### 工作流程

```
Layer 1 超时无效
      │
      ▼
启动 Frida CLI 子进程 → attach HSAng.exe (按进程名)
      │
      ▼
注入补丁脚本：
  1. 扫描 HSAng.exe 可写内存段（rw-）
  2. 查找 "FT_STANDARD" (ASCII: 46 54 5f 53 54 41 4e 44 41 52 44)
  3. 查找 "FT_STANDARD" (UTF-16LE: 46 00 54 00 5f 00 53 00 ...)
  4. 替换为 "FT_WILD\0\0\0\0" (补齐11字节→7字节的长度差)
      │
      ▼
补丁生效 → 盒子 C++ 层认为当前是狂野 → 发出推荐请求
      │
      ▼
Layer 1 的 CDP 钩子正常捕获推荐数据
```

### 实现

- 新增 `HsBoxFridaPatcher.cs`（BotMain 项目）
- 通过 `Process.Start("frida", "-p <pid> -l patch_script.js")` 调用
- 补丁脚本基于已有 `tools/archive/frida/frida_patch_mode.js`
- 增加定时刷新（每 30 秒重扫），防止盒子内部刷新字符串

### 安全约束

- `Process.getModuleByName("HSAng.exe")` 限定扫描范围
- 硬编码排除：`Hearthstone.exe`、`Unity*`、`mono*` 模块
- 仅在标准传说期间生效，对局结束后 `detach`

### 回退

- Frida 未安装 → 跳过，日志警告
- attach 失败 → 跳过，不阻塞主逻辑
- 全程 fallback，推荐系统正常降级到无建议模式

---

## 文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `BotMain/HsBoxModeSpoofer.cs` | 新增 | CDP Fetch 拦截核心 |
| `BotMain/HsBoxFridaPatcher.cs` | 新增 | Frida 补丁管理 |
| `BotMain/BotService.cs` | 修改 | 在标准传说时调用 ModeSpoofer |
| `BotMain/HsBoxRecommendationProvider.cs` | 修改 | 集成 ModeSpoofer 生命周期 |
| `tools/hsbox_mode_patch.js` | 新增 | Frida 补丁脚本（基于archive版本优化） |

## 测试计划

1. 标准传说对局 → 验证盒子是否返回出牌建议
2. 狂野对局 → 验证不触发伪装，推荐正常
3. 休闲对局 → 验证不触发伪装，推荐正常
4. 盒子重启 → 验证自动重连和重新启用
5. Layer 1 失败 → 验证自动降级到 Layer 2
