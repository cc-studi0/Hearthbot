using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_BAR_330 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.BAR_330, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                CardEffectScriptHelpers.DrawRandomCardToHandByPredicate(
                    b,
                    CardEffectScriptHelpers.IsDeathrattleMinionCard,
                    s);
            });
        }
    }
}
