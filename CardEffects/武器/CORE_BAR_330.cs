using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_BAR_330 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.CORE_BAR_330, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                CardEffectScriptHelpers.DrawRandomCardToHandByPredicate(
                    b,
                    CardEffectScriptHelpers.IsDeathrattleMinionCard,
                    s);
            });
        }
    }
}
