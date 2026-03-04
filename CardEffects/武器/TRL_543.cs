using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TRL_543 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.TRL_543, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null) return;
                var hero = (s != null && !s.IsFriend) ? b.EnemyHero : b.FriendHero;
                if (hero != null) CardEffectDB.Dmg(b, hero, 5);
            });
        }
    }
}
