using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TIME_034 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 现场播报员：战吼，双方随机装备一把武器，并使你的武器获得 +1/+1
            db.Register(C.TIME_034, EffectTrigger.Battlecry, (b, s, t) =>
            {
                bool casterIsFriend = s == null || s.IsFriend;

                var myWeaponId = CardEffectScriptHelpers.PickWeaponFromFriendlyDeckOrPool(b, s);
                var enemyPool = CardEffectScriptHelpers.GenericWeaponPool;
                var enemyWeaponId = enemyPool.Length > 0
                    ? enemyPool[CardEffectScriptHelpers.PickIndex(enemyPool.Length, b, s, t)]
                    : (C)0;

                var myWeapon = CardEffectScriptHelpers.EquipWeaponFromCard(b, casterIsFriend, myWeaponId, 2, 2);
                CardEffectScriptHelpers.EquipWeaponFromCard(b, !casterIsFriend, enemyWeaponId, 2, 2);

                myWeapon.Atk += 1;
                myWeapon.Health += 1;
                myWeapon.MaxHealth += 1;
                if (casterIsFriend)
                {
                    if (b.FriendHero != null) b.FriendHero.Atk = myWeapon.Atk;
                }
                else
                {
                    if (b.EnemyHero != null) b.EnemyHero.Atk = myWeapon.Atk;
                }
            }, BattlecryTargetType.None);
        }
    }
}
