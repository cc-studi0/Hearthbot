using System;
using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_BT_781 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            RegisterBulwarkById(db, "BT_781");
            RegisterBulwarkById(db, "CORE_BT_781");
        }

        private static void RegisterBulwarkById(CardEffectDB db, string cardIdText)
        {
            if (!Enum.TryParse(cardIdText, true, out C weaponId))
                return;

            db.RegisterHeroDamageReplacement(weaponId, (b, hero, incomingDamage) =>
            {
                if (b == null || hero == null || incomingDamage <= 0)
                    return incomingDamage;

                SimEntity weapon = null;
                if (hero == b.FriendHero) weapon = b.FriendWeapon;
                else if (hero == b.EnemyHero) weapon = b.EnemyWeapon;

                if (weapon == null || weapon.Health <= 0)
                    return incomingDamage;

                // “你的英雄将受到伤害时：改为该武器失去1点耐久”
                weapon.Health -= 1;
                return 0;
            });
        }
    }
}
