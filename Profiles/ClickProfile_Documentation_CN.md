# ClickProfile 系统 — 开发者指南

## 概述

**ClickProfile** 系统允许你编写逐次点击控制机器人的配置文件，而不依赖内置 AI 引擎。当选择 ClickProfile 时，机器人完全绕过 AI 后端。每次棋盘状态更新时，系统会调用你的 `GetNextClick()` 方法，你返回一条指令——一次点击、一个游戏操作或"等待"信号。机器人执行该指令后，会用更新的棋盘再次调用你。

轮询循环如下：

```
解析棋盘状态 → 调用 GetNextClick(board) → 执行指令 → 重新解析棋盘 → 重复
```

---

## 快速开始

在 `Profiles/` 目录中创建一个 `.cs` 文件。你的类必须实现 `ClickProfile` 接口：

```csharp
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class MyProfile : ClickProfile
{
    public ClickInstruction GetNextClick(Board board)
    {
        // 你的逻辑
        return ClickInstruction.End();
    }
}
```

文件在运行时编译——无需重新构建项目。它会和普通配置文件一起显示在下拉列表中。

---

## ClickInstruction API

`GetNextClick()` 必须返回一个 `ClickInstruction`。使用下面的静态工厂方法来创建。

### 三个控制级别

| 级别 | 方法 | 说明 |
|------|------|------|
| **原始点击** | `RawClick`、`RawClickAt` | 裸鼠标点击。你手动控制每一次点击。 |
| **高级操作** | `PlayCard`、`Attack`、`HeroPower`、`Choose`、`UseLocation`、`TradeMinion`、`ForgeCard` | 完整的游戏操作。一次调用 = 一个完整动作。 |
| **流程控制** | `End`、`ConcedeGame`、`Wait` | 流程控制——结束回合、投降或等待重新轮询。 |

你可以在同一个配置文件中自由混合所有级别。

---

### 原始点击方法

#### `ClickInstruction.RawClick(int entityId)`

通过实体 ID 点击游戏实体。这是最基本的操作——只是点击实体，不做任何解析。

用于手动多步操作序列。例如，用随从攻击：
- **第1次轮询**：`RawClick(attackerId)` — 拿起随从
- **第2次轮询**：`RawClick(targetId)` — 放到目标上，完成攻击

```csharp
// 点击指定实体
return ClickInstruction.RawClick(board.MinionFriend[0].Id);
```

#### `ClickInstruction.RawClickAt(int x, int y)`

点击屏幕上的指定位置（像素坐标）。

```csharp
// 点击屏幕坐标
return ClickInstruction.RawClickAt(500, 400);
```

---

### 高级游戏操作方法

这些方法在一条指令中执行完整的游戏操作。机器人在内部处理所有鼠标移动。

#### `ClickInstruction.PlayCard(int entityId, int boardIndex = 0, int targetEntityId = 0)`

从手牌中打出一张卡牌。
- `entityId` — 卡牌的 `Id`（来自 `board.Hand`）
- `boardIndex` — 随从放置的棋盘位置（从0开始，从左到右）
- `targetEntityId` — 指向性法术/战吼的目标实体 ID（0 = 无目标）

```csharp
// 将随从打到棋盘位置2
return ClickInstruction.PlayCard(card.Id, boardIndex: 2);

// 对敌方随从使用指向性法术
return ClickInstruction.PlayCard(spell.Id, targetEntityId: board.MinionEnemy[0].Id);

// 打出带指向性战吼的随从
return ClickInstruction.PlayCard(card.Id, boardIndex: 0, targetEntityId: board.HeroFriend.Id);
```

#### `ClickInstruction.Attack(int attackerEntityId, int targetEntityId)`

用随从或英雄武器进行攻击。

```csharp
// 用随从攻击敌方英雄
return ClickInstruction.Attack(board.MinionFriend[0].Id, board.HeroEnemy.Id);

// 攻击敌方随从
return ClickInstruction.Attack(board.MinionFriend[0].Id, board.MinionEnemy[0].Id);
```

#### `ClickInstruction.HeroPower(int targetEntityId = 0)`

使用英雄技能。对于指向性英雄技能（法师、牧师等）传入目标实体 ID，非指向性技能传入 0。

```csharp
// 非指向性英雄技能（圣骑士、术士等）
return ClickInstruction.HeroPower();

// 指向性英雄技能（法师火冲、牧师治疗等）
return ClickInstruction.HeroPower(board.MinionEnemy[0].Id);
```

#### `ClickInstruction.Choose(int entityId)`

通过实体 ID 选择一个发现或选择选项。

```csharp
return ClickInstruction.Choose(choiceEntityId);
```

#### `ClickInstruction.ChooseByIndex(int index)`

通过位置索引选择一个发现或选择选项（从0开始，从左到右）。

```csharp
// 选择第一个（最左边的）选项
return ClickInstruction.ChooseByIndex(0);
```

#### `ClickInstruction.UseLocation(int locationEntityId, int targetEntityId)`

对目标使用地标卡牌。

```csharp
return ClickInstruction.UseLocation(locationCard.Id, board.MinionEnemy[0].Id);
```

#### `ClickInstruction.TradeMinion(int entityId)`

交易一张卡牌（可交易关键词）。

```csharp
return ClickInstruction.TradeMinion(card.Id);
```

#### `ClickInstruction.ForgeCard(int entityId)`

锻造一张卡牌（锻造关键词）。

```csharp
return ClickInstruction.ForgeCard(card.Id);
```

---

### 流程控制方法

#### `ClickInstruction.End()`

结束你的回合。

```csharp
return ClickInstruction.End();
```

#### `ClickInstruction.ConcedeGame()`

投降。

```csharp
return ClickInstruction.ConcedeGame();
```

#### `ClickInstruction.Wait(int ms = 500)`

不做任何操作——等待指定毫秒数后重新轮询 `GetNextClick()` 并传入当前棋盘状态。当你在等待某些事情发生（动画、游戏状态更新等）时使用。

```csharp
// 等待1秒后再次被调用
return ClickInstruction.Wait(1000);
```

---

## Board 对象

`GetNextClick()` 接收一个 `Board` 对象，代表当前的游戏状态。以下是最重要的属性：

### 卡牌与实体

| 属性 | 类型 | 说明 |
|------|------|------|
| `Hand` | `List<Card>` | 你手中的卡牌 |
| `MinionFriend` | `List<Card>` | 你棋盘上的随从 |
| `MinionEnemy` | `List<Card>` | 敌方棋盘上的随从 |
| `HeroFriend` | `Card` | 你的英雄 |
| `HeroEnemy` | `Card` | 敌方英雄 |
| `WeaponFriend` | `Card` | 你装备的武器（无则为 null） |
| `WeaponEnemy` | `Card` | 敌方装备的武器（无则为 null） |
| `Ability` | `Card` | 你的英雄技能 |
| `EnemyAbility` | `Card` | 敌方英雄技能 |
| `Secret` | `List<Card.Cards>` | 你激活的奥秘 |
| `Deck` | `List<Card.Cards>` | 你牌库中已知的剩余卡牌 |

### 法力值与资源

| 属性 | 类型 | 说明 |
|------|------|------|
| `ManaAvailable` | `int` | 当前可用法力值 |
| `MaxMana` | `int` | 本回合最大法力值 |
| `LockedMana` | `int` | 锁定的（过载的）法力值 |
| `OverloadedMana` | `int` | 下回合将被锁定的法力值 |

### 游戏状态

| 属性 | 类型 | 说明 |
|------|------|------|
| `TurnCount` | `int` | 当前回合数 |
| `IsOwnTurn` | `bool` | 是否是你的回合 |
| `IsCombo` | `bool` | 连击是否激活（本回合已打出过卡牌） |
| `EnemyCardCount` | `int` | 敌方手牌数量 |
| `FriendDeckCount` | `int` | 你牌库剩余卡牌数 |
| `EnemyDeckCount` | `int` | 敌方牌库剩余卡牌数 |
| `SecretEnemy` | `bool` | 敌方是否有奥秘 |
| `SecretEnemyCount` | `int` | 敌方奥秘数量 |
| `FriendClass` | `Card.CClass` | 你的英雄职业 |
| `EnemyClass` | `Card.CClass` | 敌方英雄职业 |

### 辅助方法

| 方法 | 返回值 | 说明 |
|------|--------|------|
| `HasCardInHand(Card.Cards id)` | `bool` | 检查手牌中是否有指定卡牌 |
| `HasCardOnBoard(Card.Cards id, bool side)` | `bool` | 检查棋盘上是否有指定卡牌（side=true 为友方） |
| `HasWeapon(bool side)` | `bool` | 检查是否装备了武器 |
| `GetManaAvailableNextTurn()` | `int` | 预测下回合可用法力值 |

---

## Card 对象

每张卡牌（手牌、棋盘上、英雄、武器）都是一个 `Card` 对象，具有以下关键属性：

### 身份

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | `int` | 本局游戏中的唯一实体 ID（在 ClickInstruction 方法中使用此值） |
| `Template` | `CardTemplate` | 卡牌模板，包含静态卡牌数据 |
| `Type` | `Card.CType` | MINION（随从）、SPELL（法术）、WEAPON（武器）、HERO（英雄）、LOCATION（地标） |
| `Race` | `Card.CRace` | 随从种族（MURLOC 鱼人、DRAGON 龙、BEAST 野兽等） |

### 数值

| 属性 | 类型 | 说明 |
|------|------|------|
| `CurrentCost` | `int` | 当前法力值消耗 |
| `CurrentAtk` | `int` | 当前攻击力 |
| `CurrentHealth` | `int` | 当前生命值 |
| `MaxHealth` | `int` | 最大生命值 |
| `CurrentArmor` | `int` | 当前护甲值（英雄） |

### 状态与机制

| 属性 | 类型 | 说明 |
|------|------|------|
| `CanAttack` | `bool` | 该随从/英雄当前是否可以攻击 |
| `IsTaunt` | `bool` | 具有嘲讽 |
| `IsDivineShield` | `bool` | 具有圣盾 |
| `IsCharge` | `bool` | 具有冲锋 |
| `HasRush` | `bool` | 具有突袭 |
| `IsWindfury` | `bool` | 具有风怒 |
| `IsFrozen` | `bool` | 被冻结 |
| `IsStealth` | `bool` | 具有潜行 |
| `IsImmune` | `bool` | 免疫 |
| `HasPoison` | `bool` | 具有剧毒 |
| `IsLifeSteal` | `bool` | 具有吸血 |
| `HasDeathRattle` | `bool` | 具有亡语 |
| `HasReborn` | `bool` | 具有复生 |
| `IsSilenced` | `bool` | 已被沉默 |
| `IsTired` | `bool` | 刚打出（召唤失调） |
| `SpellPower` | `int` | 法术伤害加成 |
| `NumTurnsInPlay` | `int` | 该随从在场上的回合数 |
| `PlayedThisTurn` | `bool` | 本回合是否打出 |
| `IsFriend` | `bool` | 是否为友方实体 |

---

## 完整示例

### 示例1：简单打脸配置文件（高级操作）

打出所有卡牌，用所有随从攻击英雄，使用英雄技能。

```csharp
using System;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class SimpleFace : ClickProfile
{
    public ClickInstruction GetNextClick(Board board)
    {
        // 从手牌中打出卡牌（最低费用优先）
        foreach (var card in board.Hand)
        {
            if (card.CurrentCost <= board.ManaAvailable)
                return ClickInstruction.PlayCard(card.Id, boardIndex: board.MinionFriend.Count);
        }

        // 用所有可攻击的随从攻击
        foreach (var minion in board.MinionFriend)
        {
            if (minion.CanAttack)
                return ClickInstruction.Attack(minion.Id, board.HeroEnemy.Id);
        }

        // 如果装备了武器则攻击
        if (board.WeaponFriend != null && board.HeroFriend.CanAttack)
            return ClickInstruction.Attack(board.HeroFriend.Id, board.HeroEnemy.Id);

        // 使用英雄技能
        if (board.Ability != null && board.Ability.CurrentCost <= board.ManaAvailable)
            return ClickInstruction.HeroPower();

        return ClickInstruction.End();
    }
}
```

### 示例2：原始点击 — 手动两步攻击

演示原始点击模式，每次鼠标点击都是一条独立指令。

```csharp
using System;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class RawClickAttacker : ClickProfile
{
    private bool _holding = false;
    private int _holdingId = 0;

    public ClickInstruction GetNextClick(Board board)
    {
        if (!_holding)
        {
            // 第1步：拿起随从
            foreach (var minion in board.MinionFriend)
            {
                if (minion.CanAttack)
                {
                    _holding = true;
                    _holdingId = minion.Id;
                    return ClickInstruction.RawClick(minion.Id);
                }
            }
            return ClickInstruction.End();
        }
        else
        {
            // 第2步：放到目标上
            _holding = false;
            return ClickInstruction.RawClick(board.HeroEnemy.Id);
        }
    }
}
```

### 示例3：交易配置文件

交易所有可交易的卡牌，然后结束回合。

```csharp
using System;
using System.Linq;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class TraderProfile : ClickProfile
{
    public ClickInstruction GetNextClick(Board board)
    {
        // 交易所有可交易的卡牌
        foreach (var card in board.Hand)
        {
            if (card.HasTag(Card.GAME_TAG.TRADEABLE) && card.GetTag(Card.GAME_TAG.TRADEABLE) == 1)
                return ClickInstruction.TradeMinion(card.Id);
        }

        // 然后打出剩余手牌
        foreach (var card in board.Hand)
        {
            if (card.CurrentCost <= board.ManaAvailable)
                return ClickInstruction.PlayCard(card.Id, boardIndex: 0);
        }

        return ClickInstruction.End();
    }
}
```

### 示例4：等待与轮询

演示等待游戏状态变化。

```csharp
using System;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class WaitingProfile : ClickProfile
{
    private int _pollCount = 0;

    public ClickInstruction GetNextClick(Board board)
    {
        _pollCount++;

        // 行动前等待2秒（每次500毫秒，共4次轮询）
        if (_pollCount <= 4)
            return ClickInstruction.Wait(500);

        // 现在行动
        _pollCount = 0;
        return ClickInstruction.End();
    }
}
```

---

## 重要提示

1. **实体 ID**：始终使用 `card.Id`（运行时实体 ID），而不是卡牌模板 ID。实体 ID 在每局游戏和每个棋盘状态中都是唯一的。

2. **调用间的状态保持**：你的配置文件实例在一局游戏的多次调用间持续存在，所以你可以使用实例字段在轮询间跟踪状态（如原始点击示例所示）。

3. **空值检查**：访问卡牌前始终检查 null。`board.WeaponFriend`、`board.WeaponEnemy` 等可能为 null。

4. **RawClick 时序**：`RawClick` 之后，机器人等待约 0.5 秒后重新轮询。高级操作（Attack、PlayCard 等）之后，机器人会等待游戏完全处理完该操作后才重新轮询。

5. **返回 null**：如果 `GetNextClick()` 返回 null，将被视为 `Wait(500)`。

6. **无需重新构建**：配置文件 `.cs` 文件在运行时编译。只需保存文件并在机器人界面中刷新配置文件即可。

7. **混合模式**：你可以在同一个配置文件的不同调用中自由混合原始点击、高级操作和流程控制指令。
