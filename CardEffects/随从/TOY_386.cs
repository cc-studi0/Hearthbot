using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TOY_386 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 礼盒雏龙：战吼，如果手牌中有龙牌，使该龙牌和本随从获得 +1/+1
            db.Register(C.TOY_386, EffectTrigger.Battlecry, (b, s, t) =>
            {
                var dragonInHand = CardEffectScriptHelpers.PickHandDragon(b, s);
                if (dragonInHand == null) return;

                CardEffectScriptHelpers.Buff(dragonInHand, 1, 1);
                CardEffectScriptHelpers.Buff(s, 1, 1);
            }, BattlecryTargetType.None);
        }
    }
}
