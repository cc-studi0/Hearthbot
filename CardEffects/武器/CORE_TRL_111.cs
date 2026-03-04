using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_TRL_111 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.CORE_TRL_111, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null) return;
                var allies = (s != null && !s.IsFriend) ? b.EnemyMinions : b.FriendMinions;
                bool hasBeast = allies.Exists(m => CardEffectScriptHelpers.IsBeastMinion(m.CardId));
                if (hasBeast)
                {
                    var weapon = (s != null && !s.IsFriend) ? b.EnemyWeapon : b.FriendWeapon;
                    if (weapon != null) weapon.Durability += 1;
                }
            });
        }
    }
}
