using System;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 灵魂之火：造成$4点伤害，随机弃一张牌。
    internal sealed class Sim_EX1_308 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.EX1_308, EffectTrigger.Spell, (b, s, t) =>
            {
                // 造成 4+SP 点伤害
                int dmg = 4 + CardEffectDB.SP(b);
                if (t != null) CardEffectDB.Dmg(b, t, dmg);

                // 随机弃一张手牌
                if (b.Hand.Count > 0)
                {
                    int idx = CardEffectScriptHelpers.PickIndex(b.Hand.Count, b, s);
                    b.Hand.RemoveAt(idx);
                }
            }, BattlecryTargetType.AnyCharacter);
        }
    }
}
