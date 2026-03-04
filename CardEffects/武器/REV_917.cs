using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_REV_917 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.REV_917, (b, hero, target, ctx) =>
            {
                if (b == null) return;
                var side = (hero == b.FriendHero) ? b.FriendMinions : b.EnemyMinions;
                if (side.Count < 7)
                {
                    var totems = new[] { C.CS2_050, C.CS2_051, C.CS2_052, C.NEW1_009 };
                    var totemId = totems[CardEffectDB.Rnd.Next(totems.Length)];
                    CardEffectDB.Summon(b, side, totemId);
                }
            });
        }
    }
}
