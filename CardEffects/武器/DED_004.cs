using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DED_004 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterTradeCard(C.DED_004, (b, traded) =>
            {
                CardEffectScriptHelpers.ReduceRandomSpellCostInHand(b, 1, traded);
            });
        }
    }
}
