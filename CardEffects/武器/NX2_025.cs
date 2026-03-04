using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_NX2_025 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.NX2_025, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                var pool = CardEffectScriptHelpers.GetAllOutcastCards();
                CardEffectScriptHelpers.AddDiscoveredCardToHand(b, pool, s);
            });
        }
    }
}
