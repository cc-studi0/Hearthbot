# 斩杀预测数据层重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 EnemyBoardLethalFinder 的数据读取层 bug，让斩杀预测功能正确处理超级风怒、不可攻击、休眠随从。

**Architecture:** 从数据源（EntityData）到消费端（EnemyBoardLethalFinder）全链路传递 WINDFURY int 值和 CANT_ATTACK/DORMANT 标志。保持 seed 格式和 SBAPI 向后兼容，通过 Tags 字典传递完整 tag 值。

**Tech Stack:** C# / .NET 8 / xUnit

---

### Task 1: EntityData 数据模型扩展

**Files:**
- Modify: `HearthstonePayload/GameStateData.cs:119` (EntityData.Windfury)

- [ ] **Step 1: 写失败测试 — WindfuryValue 字段存在且默认值正确**

在 `BotCore.Tests/EnemyBoardLethalFinderTests.cs` 文件末尾 `CreateEntity` 方法前添加：

```csharp
[Fact]
public void EntityData_WindfuryValue_DefaultsToZero()
{
    var e = new EntityData();
    Assert.Equal(0, e.WindfuryValue);
    Assert.False(e.Windfury);
}

[Fact]
public void EntityData_Windfury_ReflectsWindfuryValue()
{
    var e = new EntityData { WindfuryValue = 1 };
    Assert.True(e.Windfury);
    Assert.Equal(1, e.WindfuryValue);

    var e2 = new EntityData { WindfuryValue = 2 };
    Assert.True(e2.Windfury);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~EntityData_" -v n`
Expected: FAIL — `WindfuryValue` 不存在

- [ ] **Step 3: 修改 EntityData**

在 `HearthstonePayload/GameStateData.cs` 中，将 EntityData 类的 Windfury 字段改为：

```csharp
// 替换原来的 bool Windfury;
public int WindfuryValue;
public bool Windfury => WindfuryValue > 0;
public bool CantAttack;
public bool Dormant;
```

即删除第 119 行的 `public bool Windfury;`，在原位写上面四行。

- [ ] **Step 4: 修复编译错误 — GameReader**

在 `HearthstonePayload/GameReader.cs:1185`，将：
```csharp
data.Windfury = _ctx.GetTagValue(entity, "WINDFURY") == 1;
```
替换为：
```csharp
data.WindfuryValue = _ctx.GetTagValue(entity, "WINDFURY");
```

在 `data.Tags = ReadAllTags(entity);` 之前（约第 1212 行）新增两行：
```csharp
data.CantAttack = _ctx.GetTagValue(entity, "CANT_ATTACK") == 1;
data.Dormant = _ctx.GetTagValue(entity, "DORMANT") > 0;
```

- [ ] **Step 5: 修复编译错误 — SeedBuilder**

在 `HearthstonePayload/SeedBuilder.cs:165`，将：
```csharp
p[16] = B(e.Windfury);
```
替换为：
```csharp
p[16] = B(e.WindfuryValue > 0);
```

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~EntityData_" -v n`
Expected: 2 passed

- [ ] **Step 7: 运行全量测试确认无回归**

Run: `dotnet test BotCore.Tests -v n`
Expected: All passed

- [ ] **Step 8: 提交**

```bash
git add HearthstonePayload/GameStateData.cs HearthstonePayload/GameReader.cs HearthstonePayload/SeedBuilder.cs BotCore.Tests/EnemyBoardLethalFinderTests.cs
git commit -m "refactor: EntityData.Windfury 从 bool 改为 int，新增 CantAttack/Dormant"
```

---

### Task 2: SimEntity 扩展 WindfuryCount / CantAttack / IsDormant

**Files:**
- Modify: `BotMain/AI/SimEntity.cs:9,24-33`

- [ ] **Step 1: 写失败测试 — WindfuryCount 控制 CanAttack**

在 `BotCore.Tests/EnemyBoardLethalFinderTests.cs` 中添加：

```csharp
[Fact]
public void SimEntity_CanAttack_FalseWhenCantAttack()
{
    var e = new SimEntity { Atk = 3, Health = 3, Type = Card.CType.MINION, CantAttack = true };
    Assert.False(e.CanAttack);
}

[Fact]
public void SimEntity_CanAttack_FalseWhenDormant()
{
    var e = new SimEntity { Atk = 3, Health = 3, Type = Card.CType.MINION, IsDormant = true };
    Assert.False(e.CanAttack);
}

[Fact]
public void SimEntity_WindfuryCount_DefaultsToOne()
{
    var e = new SimEntity();
    Assert.Equal(1, e.WindfuryCount);
}

[Fact]
public void SimEntity_CanAttack_MegaWindfuryAllowsFourAttacks()
{
    var e = new SimEntity { Atk = 3, Health = 3, Type = Card.CType.MINION, IsWindfury = true, WindfuryCount = 4, CountAttack = 3 };
    Assert.True(e.CanAttack);

    e.CountAttack = 4;
    Assert.False(e.CanAttack);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~SimEntity_" -v n`
Expected: FAIL — `CantAttack`, `IsDormant`, `WindfuryCount` 不存在

- [ ] **Step 3: 修改 SimEntity**

在 `BotMain/AI/SimEntity.cs` 中：

1. 第 9 行，在 `public bool HasPoison;` 同行后面的第 12 行区域增加字段：
```csharp
public bool CantAttack;
public bool IsDormant;
public int WindfuryCount = 1;
```

2. 替换 `CanAttack` 属性（第 24-46 行）为：
```csharp
public bool CanAttack
{
    get
    {
        if (IsDormant || CantAttack) return false;

        var maxAttackCount = WindfuryCount;
        var localReady =
            Atk > 0
            && !IsFrozen
            && !IsTired
            && CountAttack < maxAttackCount;

        if (!UseBoardCanAttack)
            return localReady;

        if (Type == Card.CType.HERO)
            return BoardCanAttack || localReady;

        return BoardCanAttack && localReady;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~SimEntity_" -v n`
Expected: 4 passed

- [ ] **Step 5: 运行全量测试确认无回归**

Run: `dotnet test BotCore.Tests -v n`
Expected: All passed

- [ ] **Step 6: 提交**

```bash
git add BotMain/AI/SimEntity.cs BotCore.Tests/EnemyBoardLethalFinderTests.cs
git commit -m "feat: SimEntity 新增 WindfuryCount/CantAttack/IsDormant，CanAttack 使用 WindfuryCount"
```

---

### Task 3: SimBoard.FromBoard 读取完整 tag 值

**Files:**
- Modify: `BotMain/AI/SimBoard.cs:111-180` (ConvertCard)

- [ ] **Step 1: 写失败测试 — MegaWindfury 通过 seed 传递**

在 `BotCore.Tests/EnemyBoardLethalFinderTests.cs` 中添加：

```csharp
[Fact]
public void SimBoard_FromBoard_ReadsMegaWindfuryFromTags()
{
    var state = new GameStateData
    {
        FriendlyPlayerId = 1,
        TurnCount = 1,
        MaxMana = 1,
        ManaAvailable = 1,
        HeroFriend = CreateEntity("HERO_06", 101),
        HeroEnemy = CreateEntity("HERO_08", 201),
        AbilityFriend = CreateEntity("HERO_06bp", 102),
        AbilityEnemy = CreateEntity("HERO_08bp", 202),
        MinionEnemy = new List<EntityData>
        {
            new EntityData
            {
                CardId = "CORE_CS2_231",
                EntityId = 301,
                Atk = 5,
                Health = 5,
                Cost = 1,
                WindfuryValue = 2,
                Tags = new Dictionary<int, int> { { 189, 2 } } // WINDFURY=2 (mega)
            }
        }
    };

    CardTemplate.INIT();
    var ok = SeedBuilder.TryBuild(state, out var seed, out _);
    Assert.True(ok);

    var board = Board.FromSeed(seed);
    var simBoard = SimBoard.FromBoard(board);

    Assert.Single(simBoard.EnemyMinions);
    Assert.True(simBoard.EnemyMinions[0].IsWindfury);
    Assert.Equal(4, simBoard.EnemyMinions[0].WindfuryCount);
}

[Fact]
public void SimBoard_FromBoard_ReadsCantAttackFromTags()
{
    var state = new GameStateData
    {
        FriendlyPlayerId = 1,
        TurnCount = 1,
        MaxMana = 1,
        ManaAvailable = 1,
        HeroFriend = CreateEntity("HERO_06", 101),
        HeroEnemy = CreateEntity("HERO_08", 201),
        AbilityFriend = CreateEntity("HERO_06bp", 102),
        AbilityEnemy = CreateEntity("HERO_08bp", 202),
        MinionEnemy = new List<EntityData>
        {
            new EntityData
            {
                CardId = "CORE_CS2_231",
                EntityId = 301,
                Atk = 4,
                Health = 5,
                Cost = 1,
                CantAttack = true,
                Tags = new Dictionary<int, int> { { 227, 1 } } // CANT_ATTACK=1
            }
        }
    };

    CardTemplate.INIT();
    var ok = SeedBuilder.TryBuild(state, out var seed, out _);
    Assert.True(ok);

    var board = Board.FromSeed(seed);
    var simBoard = SimBoard.FromBoard(board);

    Assert.Single(simBoard.EnemyMinions);
    Assert.True(simBoard.EnemyMinions[0].CantAttack);
}

[Fact]
public void SimBoard_FromBoard_ReadsDormantFromTags()
{
    var state = new GameStateData
    {
        FriendlyPlayerId = 1,
        TurnCount = 1,
        MaxMana = 1,
        ManaAvailable = 1,
        HeroFriend = CreateEntity("HERO_06", 101),
        HeroEnemy = CreateEntity("HERO_08", 201),
        AbilityFriend = CreateEntity("HERO_06bp", 102),
        AbilityEnemy = CreateEntity("HERO_08bp", 202),
        MinionEnemy = new List<EntityData>
        {
            new EntityData
            {
                CardId = "CORE_CS2_231",
                EntityId = 301,
                Atk = 4,
                Health = 5,
                Cost = 1,
                Dormant = true,
                Tags = new Dictionary<int, int> { { 1518, 1 } } // DORMANT=1
            }
        }
    };

    CardTemplate.INIT();
    var ok = SeedBuilder.TryBuild(state, out var seed, out _);
    Assert.True(ok);

    var board = Board.FromSeed(seed);
    var simBoard = SimBoard.FromBoard(board);

    Assert.Single(simBoard.EnemyMinions);
    Assert.True(simBoard.EnemyMinions[0].IsDormant);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~SimBoard_FromBoard_Reads" -v n`
Expected: FAIL — SimEntity 没有正确设置 WindfuryCount/CantAttack/IsDormant

- [ ] **Step 3: 修改 SimBoard.ConvertCard**

在 `BotMain/AI/SimBoard.cs` 的 `ConvertCard` 方法中（约第 146 行 `return new SimEntity` 块内）：

1. 将第 159 行：
```csharp
IsWindfury = c.IsWindfury,
```
替换为：
```csharp
IsWindfury = c.IsWindfury,
WindfuryCount = ReadWindfuryCount(c),
CantAttack = ReadCantAttack(c),
IsDormant = ReadDormant(c),
```

2. 在 `ConvertCard` 方法下面，`ReadCountAttackThisTurn` 方法前面新增三个方法：

```csharp
private static int ReadWindfuryCount(Card c)
{
    if (c == null) return 1;
    try
    {
        if (Enum.TryParse("WINDFURY", out Card.GAME_TAG tag))
        {
            var val = c.GetTag(tag);
            if (val >= 2) return 4; // mega windfury
            if (val == 1) return 2; // normal windfury
        }
    }
    catch { }
    return c.IsWindfury ? 2 : 1;
}

private static bool ReadCantAttack(Card c)
{
    if (c == null) return false;
    try
    {
        if (Enum.TryParse("CANT_ATTACK", out Card.GAME_TAG tag))
            return c.GetTag(tag) == 1;
    }
    catch { }
    return false;
}

private static bool ReadDormant(Card c)
{
    if (c == null) return false;
    try
    {
        if (Enum.TryParse("DORMANT", out Card.GAME_TAG tag))
            return c.GetTag(tag) > 0;
    }
    catch { }
    return false;
}
```

3. 在 `FromBoard` 方法中武器风怒继承部分（约第 88-91 行），追加 WindfuryCount 继承：

在第 89 行 `sb.FriendHero.IsWindfury = true;` 后追加：
```csharp
sb.FriendHero.WindfuryCount = Math.Max(sb.FriendHero.WindfuryCount, sb.FriendWeapon.WindfuryCount);
```

在第 91 行 `sb.EnemyHero.IsWindfury = true;` 后追加：
```csharp
sb.EnemyHero.WindfuryCount = Math.Max(sb.EnemyHero.WindfuryCount, sb.EnemyWeapon.WindfuryCount);
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~SimBoard_FromBoard_Reads" -v n`
Expected: 3 passed

- [ ] **Step 5: 运行全量测试确认无回归**

Run: `dotnet test BotCore.Tests -v n`
Expected: All passed

- [ ] **Step 6: 提交**

```bash
git add BotMain/AI/SimBoard.cs BotCore.Tests/EnemyBoardLethalFinderTests.cs
git commit -m "feat: SimBoard.FromBoard 读取 WINDFURY int 值和 CANT_ATTACK/DORMANT tag"
```

---

### Task 4: EnemyBoardLethalFinder 使用新字段

**Files:**
- Modify: `BotMain/AI/EnemyBoardLethalFinder.cs:65-106,397-425,438-444,453-502`

- [ ] **Step 1: 写失败测试 — CantAttack 随从不算斩杀**

在 `BotCore.Tests/EnemyBoardLethalFinderTests.cs` 中添加：

```csharp
[Fact]
public void Evaluate_CantAttackMinionIgnored()
{
    var board = NewBaseBoard(friendHealth: 3);
    board.EnemyMinions.Add(new SimEntity
    {
        Type = Card.CType.MINION, Atk = 8, Health = 8,
        IsFriend = false, CanAttackHeroes = true, CantAttack = true
    });

    var result = EnemyBoardLethalFinder.Evaluate(board);

    Assert.False(result.ShouldConcede);
    Assert.Equal("negative:not-lethal", result.Reason);
}

[Fact]
public void Evaluate_DormantMinionIgnored()
{
    var board = NewBaseBoard(friendHealth: 3);
    board.EnemyMinions.Add(new SimEntity
    {
        Type = Card.CType.MINION, Atk = 8, Health = 8,
        IsFriend = false, CanAttackHeroes = true, IsDormant = true
    });

    var result = EnemyBoardLethalFinder.Evaluate(board);

    Assert.False(result.ShouldConcede);
    Assert.Equal("negative:not-lethal", result.Reason);
}

[Fact]
public void Evaluate_MegaWindfuryLethal()
{
    // 3攻超级风怒 = 3*4 = 12 伤害，友方英雄10血 → 必死
    var board = NewBaseBoard(friendHealth: 10);
    board.EnemyMinions.Add(new SimEntity
    {
        Type = Card.CType.MINION, Atk = 3, Health = 3,
        IsFriend = false, CanAttackHeroes = true,
        IsWindfury = true, WindfuryCount = 4
    });

    var result = EnemyBoardLethalFinder.Evaluate(board);

    Assert.True(result.ShouldConcede);
    Assert.Equal("positive:deterministic-lethal", result.Reason);
}

[Fact]
public void Evaluate_MegaWindfuryNotLethalWhenDamageInsufficient()
{
    // 3攻超级风怒 = 12 伤害，友方英雄13血 → 不死
    var board = NewBaseBoard(friendHealth: 13);
    board.EnemyMinions.Add(new SimEntity
    {
        Type = Card.CType.MINION, Atk = 3, Health = 3,
        IsFriend = false, CanAttackHeroes = true,
        IsWindfury = true, WindfuryCount = 4
    });

    var result = EnemyBoardLethalFinder.Evaluate(board);

    Assert.False(result.ShouldConcede);
    Assert.Equal("negative:not-lethal", result.Reason);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~Evaluate_CantAttack|Evaluate_Dormant|Evaluate_MegaWindfury" -v n`
Expected: CantAttack 和 Dormant 测试 FAIL（当前会被当作可攻击随从），MegaWindfury 测试 FAIL（只算 2 次攻击）

- [ ] **Step 3: 修改 EnemyBoardLethalFinder**

在 `BotMain/AI/EnemyBoardLethalFinder.cs` 中：

**3a. PrepareForEnemyTurn（第 65-106 行）**

在第 83 行 `clone.EnemyHero.IsWindfury = true;` 后追加：
```csharp
clone.EnemyHero.WindfuryCount = Math.Max(clone.EnemyHero.WindfuryCount, clone.EnemyWeapon.WindfuryCount);
```

**3b. RemainingAttacks（第 438-445 行）**

将第 443 行：
```csharp
var maxAttacks = entity.IsWindfury ? 2 : 1;
```
替换为：
```csharp
var maxAttacks = entity.WindfuryCount;
```

**3c. GetAttackers（第 397-425 行）**

在第 413 行：
```csharp
if (minion == null || minion.Type == Card.CType.LOCATION || !minion.CanAttack)
```
这已经够了——因为 `CanAttack` 属性现在会在 `IsDormant || CantAttack` 时返回 false。但为了 PrepareForEnemyTurn 里重置了 IsTired/CountAttack 的情况，需要在 `PrepareForEnemyTurn` 循环中保留 CantAttack/IsDormant 不被覆盖。

当前 `PrepareForEnemyTurn` 的 foreach 循环（第 93-99 行）重置了 `IsTired = false`，但不会触及 CantAttack/IsDormant 字段（它们在 SimEntity 中独立），所以 `CanAttack` 的新逻辑会自动过滤。不需要额外修改 PrepareForEnemyTurn。

**3d. BuildStateKey（第 453-502 行）**

在第 491 行 `.Append(entity.IsWindfury ? '1' : '0').Append(',')` 后追加：
```csharp
.Append(entity.WindfuryCount).Append(',')
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~Evaluate_CantAttack|Evaluate_Dormant|Evaluate_MegaWindfury" -v n`
Expected: 4 passed

- [ ] **Step 5: 运行全量测试确认无回归**

Run: `dotnet test BotCore.Tests -v n`
Expected: All passed

- [ ] **Step 6: 提交**

```bash
git add BotMain/AI/EnemyBoardLethalFinder.cs BotCore.Tests/EnemyBoardLethalFinderTests.cs
git commit -m "fix: EnemyBoardLethalFinder 支持超级风怒、过滤 CANT_ATTACK/DORMANT"
```

---

### Task 5: BoardSimulator 使用 WindfuryCount

**Files:**
- Modify: `BotMain/AI/BoardSimulator.cs:44-48`

- [ ] **Step 1: 写失败测试 — 超级风怒英雄可攻击 4 次**

在 `BotCore.Tests/EnemyBoardLethalFinderTests.cs` 中添加：

```csharp
[Fact]
public void BoardSimulator_HeroAttack_MegaWindfuryAllowsFourAttacks()
{
    CardTemplate.INIT();
    var sim = new BoardSimulator(CardEffectDB.BuildDefault());
    var board = NewBaseBoard(friendHealth: 30);
    board.EnemyHero.Atk = 3;
    board.EnemyHero.IsWindfury = true;
    board.EnemyHero.WindfuryCount = 4;
    board.EnemyHero.UseBoardCanAttack = false;

    // 第 1 次攻击
    sim.Attack(board, board.EnemyHero, board.FriendHero);
    Assert.True(board.EnemyHero.CanAttack);

    // 第 2 次攻击
    sim.Attack(board, board.EnemyHero, board.FriendHero);
    Assert.True(board.EnemyHero.CanAttack);

    // 第 3 次攻击
    sim.Attack(board, board.EnemyHero, board.FriendHero);
    Assert.True(board.EnemyHero.CanAttack);

    // 第 4 次攻击
    sim.Attack(board, board.EnemyHero, board.FriendHero);
    Assert.False(board.EnemyHero.CanAttack);

    // 总伤害 = 3 * 4 = 12
    Assert.Equal(18, board.FriendHero.Health); // 30 - 12 = 18
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~BoardSimulator_HeroAttack_Mega" -v n`
Expected: FAIL — 英雄在第 2 次攻击后就被标记 IsTired

- [ ] **Step 3: 修改 BoardSimulator**

在 `BotMain/AI/BoardSimulator.cs` 第 44-48 行，将：
```csharp
attacker.CountAttack++;
if (attacker.Type == Card.CType.HERO)
{
    if (!attacker.IsWindfury || attacker.CountAttack >= 2)
        attacker.IsTired = true;
}
```
替换为：
```csharp
attacker.CountAttack++;
if (attacker.Type == Card.CType.HERO)
{
    if (attacker.CountAttack >= attacker.WindfuryCount)
        attacker.IsTired = true;
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test BotCore.Tests --filter "FullyQualifiedName~BoardSimulator_HeroAttack_Mega" -v n`
Expected: PASS

- [ ] **Step 5: 运行全量测试确认无回归**

Run: `dotnet test BotCore.Tests -v n`
Expected: All passed

- [ ] **Step 6: 提交**

```bash
git add BotMain/AI/BoardSimulator.cs BotCore.Tests/EnemyBoardLethalFinderTests.cs
git commit -m "fix: BoardSimulator 使用 WindfuryCount 替代硬编码攻击次数"
```

---

### Task 6: 诊断日志

**Files:**
- Modify: `BotMain/BotService.cs:7592-7651`

- [ ] **Step 1: 修改 TryConcedeBeforeEndTurnIfDeadNextTurn 增加棋盘快照日志**

在 `BotMain/BotService.cs` 中，找到 `TryConcedeBeforeEndTurnIfDeadNextTurn` 方法（第 7592 行）。

将第 7626-7629 行：
```csharp
var friendHp = simBoard?.FriendHero != null ? simBoard.FriendHero.Health + simBoard.FriendHero.Armor : -1;
var enemyMinionCount = simBoard?.EnemyMinions?.Count ?? 0;
var enemyAtk = simBoard?.EnemyHero?.Atk ?? 0;
var friendSecrets = simBoard?.FriendSecrets?.Count ?? 0;
```
替换为：
```csharp
var friendHp = simBoard?.FriendHero != null ? simBoard.FriendHero.Health + simBoard.FriendHero.Armor : -1;
var enemyMinionCount = simBoard?.EnemyMinions?.Count ?? 0;
var enemyAtk = simBoard?.EnemyHero?.Atk ?? 0;
var friendSecrets = simBoard?.FriendSecrets?.Count ?? 0;

if (simBoard?.EnemyMinions != null)
{
    for (int i = 0; i < simBoard.EnemyMinions.Count; i++)
    {
        var m = simBoard.EnemyMinions[i];
        Log($"[ConcedeWhenLethal] enemy[{i}] id={m.CardId} atk={m.Atk} hp={m.Health} wf={m.WindfuryCount} cant={m.CantAttack} dorm={m.IsDormant} taunt={m.IsTaunt} frozen={m.IsFrozen}");
    }
}
```

- [ ] **Step 2: 运行全量测试确认无回归**

Run: `dotnet test BotCore.Tests -v n`
Expected: All passed

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 斩杀检测增加敌方随从诊断日志"
```

---

### Task 7: 最终集成测试

- [ ] **Step 1: 运行全量测试套件**

Run: `dotnet test BotCore.Tests -v n`
Expected: All passed, no regressions

- [ ] **Step 2: 构建整个项目确认编译**

Run: `dotnet build BotMain/BotMain.csproj -c Release --no-restore`
Expected: Build succeeded

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj -c Release --no-restore`
Expected: Build succeeded

- [ ] **Step 3: 检查所有修改的文件列表**

Run: `git diff --stat HEAD~6`
Expected output 包含：
- `HearthstonePayload/GameStateData.cs`
- `HearthstonePayload/GameReader.cs`
- `HearthstonePayload/SeedBuilder.cs`
- `BotMain/AI/SimEntity.cs`
- `BotMain/AI/SimBoard.cs`
- `BotMain/AI/EnemyBoardLethalFinder.cs`
- `BotMain/AI/BoardSimulator.cs`
- `BotMain/BotService.cs`
- `BotCore.Tests/EnemyBoardLethalFinderTests.cs`
