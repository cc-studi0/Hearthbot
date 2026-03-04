using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_LOE_118 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterHeroDamageReplacement(C.LOE_118, (b, hero, incoming) =>
            {
                if (incoming <= 0) return 0;
                if (b?.FriendHero != null && hero == b.FriendHero) return incoming * 2;
                if (b?.EnemyHero != null && hero == b.EnemyHero) return incoming * 2;
                return incoming;
            });
        }
    }
}
