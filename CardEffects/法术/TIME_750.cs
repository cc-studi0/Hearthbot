using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TIME_750 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 先行打击：造成 3 点伤害；若手牌中有法力值消耗 >=5 的随从牌，抽一张随从牌
            db.Register(C.TIME_750, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 3 + CardEffectDB.SP(b));

                bool hasBigMinionInHand = b.Hand.Exists(c => c.Type == Card.CType.MINION && c.Cost >= 5);
                if (!hasBigMinionInHand) return;

                CardEffectScriptHelpers.DrawRandomMinionToHand(b, s);
            }, BattlecryTargetType.AnyCharacter);
        }
    }
}
