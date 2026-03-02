# CardEffects 目录说明

每张卡的效果存放在一个独立的 JSON 文件中，文件名为卡牌 ID（如 `CORE_REV_290.json`）。
支持按卡牌类型分子目录存放（`随从/`、`法术/`、`武器/`、`英雄/`、`地标/`、`其他/`）。

## 文件格式

```json
{
  "触发器名": {
    "targetType": "目标类型（可选，默认 None）",
    "effects": [
      { "type": "效果类型", ...参数 }
    ]
  }
}
```

## 触发器（Trigger）

| 名称 | 说明 |
|------|------|
| `Battlecry` | 战吼（随从打出时触发） |
| `Spell` | 法术施放 / 地标激活效果 |
| `Deathrattle` | 亡语（随从死亡时触发） |
| `EndOfTurn` | 回合结束时触发 |
| `Aura` | 光环（持续性效果） |
| `LocationActivation` | 地标专属激活层（优先于 Spell 层） |

## 目标类型（targetType）

| 值 | 说明 | 适用举例 |
|----|------|----------|
| `None` | 无目标（自动生效 / AOE / 自身增益） | 奥术智慧、神圣新星 |
| `AnyCharacter` | 任意角色（所有随从 + 双方英雄） | 火球术、暗影箭 |
| `AnyMinion` | 任意随从（不含英雄） | 血色深渊、月火术 |
| `EnemyOnly` | 仅敌方角色（敌方随从 + 敌方英雄） | 炎枪术（只打敌方） |
| `EnemyMinion` | 仅敌方随从（不含英雄） | 刺杀、暗言术：灭 |
| `FriendlyOnly` | 仅友方角色（友方随从 + 己方英雄） | 真言术：盾 |
| `FriendlyMinion` | 仅友方随从（不含英雄） | 赎罪教堂、力量祝福 |

> **注意：** 战吼目标不包含自身（打出的随从不能以自己为目标）。

## 效果类型（type）

### 单体伤害

| type | 参数 | 说明 |
|------|------|------|
| `dmg` | `v`, `useSP`(可选) | 对**目标**造成 v 点伤害，`useSP:true` 时加上法术伤害 |
| `dmg_face` | `v`, `useSP`(可选) | 对**敌方英雄**造成 v 点伤害 |
| `dmg_random` | `v`, `useSP`(可选) | 对**随机一个敌人**造成 v 点伤害 |

### AOE 伤害

| type | 参数 | 说明 |
|------|------|------|
| `dmg_all` | `v`, `useSP`(可选) | 对**所有角色**造成 v 点伤害（全场 AOE） |
| `dmg_enemy` | `v`, `useSP`(可选) | 对**所有敌方角色**（随从+英雄）造成 v 点伤害 |

### 治疗

| type | 参数 | 说明 |
|------|------|------|
| `heal` | `v` | 治疗**目标** v 点 |
| `heal_hero` | `v` | 治疗**己方英雄** v 点 |
| `heal_all` | `v` | 治疗**所有友方角色** v 点 |

### 增益 BUFF

| type | 参数 | 说明 |
|------|------|------|
| `buff` | `atk`, `hp` | 使**目标**获得 +atk/+hp |
| `buff_all` | `atk`, `hp` | 使**所有友方随从**获得 +atk/+hp |
| `buff_self` | `atk`, `hp` | 使**自身**（释放源）获得 +atk/+hp |

### 赋予关键字

| type | 说明 |
|------|------|
| `give_taunt` | 给予**嘲讽** |
| `give_ds` | 给予**圣盾** |
| `give_windfury` | 给予**风怒** |
| `give_rush` | 给予**突袭** |
| `give_lifesteal` | 给予**吸血** |
| `give_poison` | 给予**剧毒** |
| `give_reborn` | 给予**复生** |
| `give_immune` | 使**己方英雄**获得**免疫** |

### 控制效果

| type | 参数 | 说明 |
|------|------|------|
| `silence` | - | **沉默**目标（移除嘲讽/圣盾/剧毒/吸血/风怒/复生/法术伤害） |
| `freeze` | - | **冻结**目标 |
| `freeze_all` | - | **冻结所有敌方角色**（随从+英雄） |
| `bounce` | - | 将目标**弹回手牌** |

### 消灭

| type | 参数 | 说明 |
|------|------|------|
| `destroy` | - | **消灭**目标 |
| `destroy_all` | - | **消灭所有随从**（友方+敌方） |
| `destroy_weapon` | - | **摧毁**敌方武器 |
| `clear_board` | - | **清场**（移除所有随从） |

### 召唤 & 装备

| type | 参数 | 说明 |
|------|------|------|
| `summon` | `atk`, `hp` | 召唤一个 atk/hp 的随从 |
| `equip` | `atk`, `dur`(或`hp`) | 装备一把 atk/dur 的武器，英雄获得 atk 点攻击力 |

### 资源

| type | 参数 | 说明 |
|------|------|------|
| `draw` | `n`(默认1) | 抽 n 张牌 |
| `armor` | `v` | 获得 v 点护甲 |
| `hero_atk` | `v` | 英雄获得 +v 攻击力 |
| `add_mana` | `v` | 获得 v 点法力水晶 |

### 属性设置

| type | 参数 | 说明 |
|------|------|------|
| `set_hp` | `v` | 将目标的生命值和最大生命值设为 v |

### 特殊

| type | 说明 |
|------|------|
| `noop` | 空操作（占位用，不执行任何效果） |

## 示例

### 火球术（法术：对一个角色造成6点伤害）
```json
{
  "Spell": {
    "targetType": "AnyCharacter",
    "effects": [
      { "type": "dmg", "v": 6, "useSP": true }
    ]
  }
}
```

### 火焰元素（战吼：对一个角色造成3点伤害）
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

### 赎罪教堂（地标：使一个友方随从获得+2/+1，抽1张牌）
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

### 奉献（法术：对所有敌方角色造成2点伤害）
```json
{
  "Spell": {
    "targetType": "None",
    "effects": [
      { "type": "dmg_enemy", "v": 2, "useSP": true }
    ]
  }
}
```

### 暗言术：灭（法术：消灭一个敌方随从）
```json
{
  "Spell": {
    "targetType": "EnemyMinion",
    "effects": [
      { "type": "destroy" }
    ]
  }
}
```

### 骨魇（战吼+亡语：战吼给自身+atk/+hp，亡语召唤随从）
```json
{
  "Battlecry": {
    "targetType": "None",
    "effects": [
      { "type": "buff_self", "atk": 1, "hp": 1 }
    ]
  },
  "Deathrattle": {
    "effects": [
      { "type": "summon", "atk": 2, "hp": 2 }
    ]
  }
}
```

## 注意事项

- 一张卡可以有**多个触发器**（如战吼+亡语），在 JSON 顶层并列即可
- `effects` 数组中可以有**多个效果**，会按顺序全部执行
- 地标卡优先使用 `LocationActivation` 层，其次回退到 `Spell` 层
- 复杂的条件性效果（如「如果你手中有龙牌」）无法用此格式表达，需要在 `CardEffectDB.cs` 的 `RegisterOverrides` 中用 C# 硬编码
- `RegisterOverrides` 中的注册**优先级最高**，会覆盖此目录和 `effects_data.json` 中的同卡牌注册
- `useSP` 参数只对伤害类效果（`dmg`/`dmg_all`/`dmg_enemy`/`dmg_face`/`dmg_random`）有效
