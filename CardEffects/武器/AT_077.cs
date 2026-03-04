using System.Linq;
using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_AT_077 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.AT_077, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null) return;
                var friendMinions = b.FriendDeckCards.Where(CardEffectScriptHelpers.IsMinionCard).ToList();
                var enemyMinions = b.EnemyDeckCards.Where(CardEffectScriptHelpers.IsMinionCard).ToList();

                if (friendMinions.Count > 0 && enemyMinions.Count > 0)
                {
                    var friendCard = friendMinions[CardEffectDB.Rnd.Next(friendMinions.Count)];
                    var enemyCard = enemyMinions[CardEffectDB.Rnd.Next(enemyMinions.Count)];
                    var friendCost = CardEffectScriptHelpers.GetBaseCost(friendCard, 0);
                    var enemyCost = CardEffectScriptHelpers.GetBaseCost(enemyCard, 0);

                    if (friendCost > enemyCost)
                    {
                        var weapon = (s != null && !s.IsFriend) ? b.EnemyWeapon : b.FriendWeapon;
                        if (weapon != null) weapon.Durability += 1;
                    }
                }
            });
        }
    }
}
