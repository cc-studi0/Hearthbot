using System;
using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_AV_244 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.AV_244, (b, hero, target, ctx) =>
            {
                if (b == null || ctx == null || !ctx.HonorableKill) return;
                var weapon = hero == b.FriendHero ? b.FriendWeapon : b.EnemyWeapon;
                if (weapon != null)
                {
                    weapon.Atk += 1;
                    weapon.Durability += 1;
                }
            });
        }
    }
}
