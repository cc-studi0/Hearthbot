using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TIME_444 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.TIME_444, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                var pool = CardEffectScriptHelpers.GetAllDemonCards();
                CardEffectScriptHelpers.AddDiscoveredCardToHand(b, pool, s);
            });
        }
    }
}
