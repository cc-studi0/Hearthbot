using System;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_GAME_005 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 幸运币：本回合获得 1 个法力水晶（模拟层按可用法力 +1 处理）
            db.Register(C.GAME_005, EffectTrigger.Spell, (b, s, t) =>
            {
                if (b == null) return;
                b.Mana = Math.Min(10, b.Mana + 1);
            }, BattlecryTargetType.None);
        }
    }
}
