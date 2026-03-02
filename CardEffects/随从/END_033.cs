using System;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_END_033 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 先觉蜿变幼龙：若手牌中有其他龙牌，本牌法力值消耗减少（3）点
            db.Register(C.END_033, EffectTrigger.Aura, (b, s, t) =>
            {
                if (s == null) return;
                int baseCost = CardEffectScriptHelpers.GetBaseCost(C.END_033, s.Cost);
                bool hasOtherDragon = CardEffectScriptHelpers.HasDragonInHand(b, s);
                s.Cost = Math.Max(0, baseCost - (hasOtherDragon ? 3 : 0));
            });
        }
    }
}
