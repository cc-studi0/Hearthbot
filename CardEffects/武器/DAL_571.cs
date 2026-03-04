using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DAL_571 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.DAL_571, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null) return;
                var secrets = (s != null && !s.IsFriend) ? b.EnemySecrets : b.FriendSecrets;
                if (secrets.Count > 0)
                {
                    var weapon = (s != null && !s.IsFriend) ? b.EnemyWeapon : b.FriendWeapon;
                    if (weapon != null) weapon.Atk += 1;
                }
            });
        }
    }
}
