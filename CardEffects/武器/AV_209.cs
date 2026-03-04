using System;
using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_AV_209 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.AV_209, (b, hero, target, ctx) =>
            {
                if (b == null || ctx == null || !ctx.HonorableKill) return;
                var damage = Math.Max(0, ctx.AttackDamage);
                if (damage <= 0) return;

                if (hero == b.FriendHero && b.EnemyHero != null)
                    CardEffectDB.Dmg(b, b.EnemyHero, damage);
                else if (hero == b.EnemyHero && b.FriendHero != null)
                    CardEffectDB.Dmg(b, b.FriendHero, damage);
            });
        }
    }
}
