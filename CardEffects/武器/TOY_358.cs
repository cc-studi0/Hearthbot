using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TOY_358 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.TOY_358, (b, hero, target, ctx) =>
            {
                if (b == null) return;
                var side = (hero == b.FriendHero) ? b.FriendMinions : b.EnemyMinions;
                if (side.Count < 7)
                {
                    var hound = new SimEntity
                    {
                        CardId = C.CS2_boar,
                        IsFriend = (hero == b.FriendHero),
                        Type = SmartBot.Plugins.API.Card.CType.MINION,
                        Atk = 1,
                        Health = 1,
                        MaxHealth = 1
                    };
                    side.Add(hound);
                }
            });
        }
    }
}
