# 斩杀预测数据层重构设计

## 问题

`EnemyBoardLethalFinder` 在回合末预测敌方下回合是否能斩杀我方英雄，判定为必死则提前投降。
当前该功能失效，反编译游戏 DLL 后确认以下 bug：

### Bug 1: WINDFURY 读取丢失超级风怒
`GameReader.cs:1185`: `data.Windfury = _ctx.GetTagValue(entity, "WINDFURY") == 1`

游戏中 WINDFURY 标签值 > 1 表示超级风怒（4 次攻击）。`== 1` 判断导致超级风怒时返回 false，斩杀计算严重低估伤害。

**游戏证据**：反编译 `Assembly-CSharp.dll`（EntityBase 类）：
- `HasWindfury()` → `GetTag(GAME_TAG.WINDFURY) > 0`（使用 `> 0` 而非 `== 1`）
- `GAME_TAG.WINDFURY = 189`，`GAME_TAG.MEGA_WINDFURY = 1207`
- 游戏 UI 代码中有 `if (tag == GAME_TAG.WINDFURY && tagValue > 1)` 处理

### Bug 2: CANT_ATTACK 未过滤
`PrepareForEnemyTurn` 重置所有敌方随从为可攻击状态（`IsTired=false, CountAttack=0`），
但未检查 `CANT_ATTACK`（tag 227）。古树人等不能攻击的随从被错误计入攻击者。

### Bug 3: DORMANT 随从未排除
休眠随从（`DORMANT` tag 1518）在战场上但不能攻击，当前未过滤。

### Bug 4: 超级风怒攻击次数硬编码
`RemainingAttacks` 和 `CanAttack` 中 `maxAttacks = IsWindfury ? 2 : 1` 硬编码，不支持 4 次攻击。

## 方案：全链路数据层重构

### 数据流

```
游戏内存 → GameReader.ReadEntity() → EntityData
    → SeedBuilder.SerializeEntity() → Seed 字符串
    → Board.FromSeed() (SBAPI) → Board/Card
    → SimBoard.FromBoard() → SimEntity
    → EnemyBoardLethalFinder.Evaluate()
```

### 改动层级

#### 层 1: EntityData（GameStateData.cs）
- `bool Windfury` → `int WindfuryValue`（0=无, 1=风怒, >=2=超级风怒）
- 保留 `bool Windfury => WindfuryValue > 0` 向后兼容
- 新增 `bool CantAttack`（tag 227）
- 新增 `bool Dormant`（tag 1518）

#### 层 2: GameReader（GameReader.cs）
```
旧: data.Windfury = _ctx.GetTagValue(entity, "WINDFURY") == 1
新: data.WindfuryValue = _ctx.GetTagValue(entity, "WINDFURY")
新: data.CantAttack = _ctx.GetTagValue(entity, "CANT_ATTACK") == 1
新: data.Dormant = _ctx.GetTagValue(entity, "DORMANT") > 0
```

#### 层 3: SeedBuilder（SeedBuilder.cs）
```
p[16] = B(e.WindfuryValue > 0)   // 保持 True/False，SBAPI 向后兼容
p[38] = Tags 字典已包含完整 WINDFURY 值，无需额外改动
```

#### 层 4: SimEntity（SimEntity.cs）
- 保留 `bool IsWindfury`（向后兼容）
- 新增 `int WindfuryCount = 1`（最大攻击次数：1/2/4）
- 新增 `bool IsDormant`
- 新增 `bool CantAttack`
- `CanAttack` 属性新增 `if (IsDormant || CantAttack) return false`

#### 层 5: SimBoard.FromBoard（SimBoard.cs）
在 `ConvertCard` 中：
- 从 `c.GetTag(Card.GAME_TAG.WINDFURY)` 读取实际值
- `WindfuryCount = windfuryTag >= 2 ? 4 : windfuryTag == 1 ? 2 : 1`
- `IsWindfury = windfuryTag > 0`
- 读取 CANT_ATTACK 和 DORMANT tag

#### 层 6: EnemyBoardLethalFinder（EnemyBoardLethalFinder.cs）
- `RemainingAttacks`: `maxAttacks = entity.WindfuryCount`
- `GetAttackers`: 过滤 `IsDormant || CantAttack`
- `PrepareForEnemyTurn`: 不重置 CantAttack/Dormant 状态
- `BuildStateKey`: 包含 WindfuryCount

#### 层 7: BoardSimulator（BoardSimulator.cs）
- `attacker.CountAttack >= attacker.WindfuryCount` 判定耗尽（替代硬编码 2）

#### 层 8: 诊断日志
在 `TryConcedeBeforeEndTurnIfDeadNextTurn` 中输出完整棋盘快照：
每个敌方随从的 CardId、Atk、WindfuryCount、CantAttack、Dormant 状态。

### 向后兼容

- Seed 格式不变：p[16] 仍为 True/False，SBAPI 的 Board.FromSeed 不受影响
- 完整 tag 值通过 p[38] Tags 字典传递，SimBoard.FromBoard 从 Card.GetTag() 读取
- EntityData.Windfury bool 属性保留为计算属性
- SimEntity.IsWindfury 保留，不影响非斩杀场景的旧代码

### 不做的事

- 不重构 LethalFinder（我方斩杀搜索），本次只修敌方斩杀预测
- 不处理敌方手牌/法术斩杀（无法观测）
- 不处理 HEROPOWER_ADDITIONAL_ACTIVATIONS（英雄技能多次使用，极少见，后续再加）
