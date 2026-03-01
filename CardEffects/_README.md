# CardEffects 目录说明

每张卡的效果存放在一个独立的 JSON 文件中，文件名为卡牌 ID。

## 文件格式

```json
{
  "触发器名": {
    "targetType": "目标类型（可选）",
    "effects": [
      { "type": "效果类型", ...参数 }
    ]
  }
}
```

## 触发器名（Trigger）

| 名称 | 说明 |
|------|------|
| `Battlecry` | 战吼（打出时触发） |
| `Spell` | 法术 / 地标激活 |
| `Deathrattle` | 亡语 |
| `EndOfTurn` | 回合结束 |
| `LocationActivation` | 地标专属激活层 |

## 目标类型（targetType）

| 值 | 说明 |
|----|------|
| `None` | 无目标（自动生效/AOE） |
| `AnyCharacter` | 任意角色（随从+英雄） |
| `AnyMinion` | 任意随从 |
| `EnemyOnly` | 仅敌方（随从+英雄） |
| `EnemyMinion` | 仅敌方随从 |
| `FriendlyOnly` | 仅友方（随从+英雄） |
| `FriendlyMinion` | 仅友方随从 |

## 效果类型（type）

| type | 参数 | 说明 |
|------|------|------|
| `dmg` | `v`, `useSP` | 对目标造成伤害 |
| `dmg_all` | `v`, `useSP` | AOE 伤害全体 |
| `dmg_enemy` | `v`, `useSP` | AOE 伤害敌方 |
| `heal` | `v` | 治疗目标 |
| `heal_hero` | `v` | 治疗己方英雄 |
| `buff` | `atk`, `hp` | 给目标 +攻/+血 |
| `buff_all` | `atk`, `hp` | 强化所有友方随从 |
| `buff_self` | `atk`, `hp` | 强化自身 |
| `draw` | `n` | 抽 n 张牌 |
| `armor` | `v` | 获得护甲 |
| `summon` | `atk`, `hp` | 召唤随从 |
| `destroy` | - | 消灭目标 |
| `silence` | - | 沉默目标 |
| `freeze` | - | 冻结目标 |
| `bounce` | - | 将目标弹回手牌 |
| `give_taunt` | - | 给予嘲讽 |
| `give_ds` | - | 给予圣盾 |
| `give_windfury` | - | 给予风怒 |
| `give_rush` | - | 给予突袭 |
| `give_lifesteal` | - | 给予溅射 |
| `give_poison` | - | 给予剧毒 |
| `hero_atk` | `v` | 英雄获得攻击力 |
| `equip` | `atk`, `dur` | 装备武器 |

## 示例

### 赎罪教堂（地标）
```json
{
  "Spell": {
    "targetType": "FriendlyMinion",
    "effects": [
      { "type": "buff", "atk": 2, "hp": 1 },
      { "type": "draw", "n": 1 }
    ]
  }
}
```

### 火焰元素（战吼：对一个随从造成3点伤害）
```json
{
  "Battlecry": {
    "targetType": "AnyCharacter",
    "effects": [
      { "type": "dmg", "v": 3 }
    ]
  }
}
```

### 狂乱（法术：使一个随从获得风怒并攻击随机敌人）
```json
{
  "Spell": {
    "targetType": "FriendlyMinion",
    "effects": [
      { "type": "give_windfury" }
    ]
  }
}
```

## 注意事项

- 一张卡可以有多个触发器（如战吼+亡语）
- effects 数组中可以有多个效果，会按顺序全部执行
- 复杂的条件性效果（如「如果你手中有X张牌」）无法用此格式表达，需要在 `CardEffectDB.cs` 的 `RegisterOverrides` 中用 C# 硬编码
- `RegisterOverrides` 中的注册**优先级最高**，会覆盖此目录中同卡牌的注册
