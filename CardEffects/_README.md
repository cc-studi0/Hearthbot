# CardEffects（唯一卡牌效果入口）

本目录只存放卡牌效果 `.cs` 文件。  
项目已移除 JSON 卡牌效果配置，且不再允许其他模块写卡牌效果逻辑。

## 设计约束

- 一张卡一个文件，文件名使用卡牌 ID（如 `CORE_SW_066.cs`）。
- 所有效果脚本类必须在命名空间 `BotMain.AI.CardEffectsScripts`。
- 卡牌效果只允许出现在 `CardEffects/**/*.cs`。
- 统一加载入口只有一个：
  - `CardEffectDB.BuildDefault()`
  - `CardEffectScriptLoader.LoadAll(db)`
- `effects_data.json` / `CardEffectLoader` / `CardEffectFileLoader` 已废弃并删除。
- `BoardSimulator` 不再承担“未注册自动兜底卡牌效果”。

## 目录建议

- `随从/`
- `法术/`
- `武器/`
- `英雄/`
- `地标/`
- `_Support/`（仅通用帮助类，不写某张卡专属效果）

## 手写模板（推荐）

```csharp
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_SW_066 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.CORE_SW_066, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DoSilence(t);
            }, BattlecryTargetType.AnyMinion);
        }
    }
}
```

## 可用触发器（`EffectTrigger`）

- `Battlecry`
- `Deathrattle`
- `Spell`
- `EndOfTurn`
- `Aura`
- `LocationActivation`
- `AfterAttackMinion`
- `AfterDamaged`

## 目标类型（`BattlecryTargetType`）

- `None`
- `AnyCharacter`
- `AnyMinion`
- `EnemyOnly`
- `EnemyMinion`
- `FriendlyOnly`
- `FriendlyMinion`

## 实现原则

- 优先直接手写 `db.Register(...)`，把条件判断和结算都写清楚。
- 复杂机制（发现池、伪随机、模板属性读取）放到 `_Support` 帮助类复用。
- 禁止把单卡效果写回 `CardEffectDB`、`BoardSimulator` 或其它业务模块。
