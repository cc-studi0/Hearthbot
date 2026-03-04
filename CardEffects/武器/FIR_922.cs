using System.Linq;
using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_FIR_922 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.FIR_922, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null || s == null) return;
                var hasDarkGiftMinion = b.Hand.Any(c =>
                    CardEffectScriptHelpers.IsMinionCard(c.CardId)
                    && CardEffectScriptHelpers.HasDarkGiftKeyword(c.CardId));
                if (hasDarkGiftMinion)
                    s.Atk += 3;
            });
        }
    }
}
