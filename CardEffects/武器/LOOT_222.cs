using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_LOOT_222 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterHeroImmuneWhileAttackingWeapon(C.LOOT_222);
        }
    }
}
