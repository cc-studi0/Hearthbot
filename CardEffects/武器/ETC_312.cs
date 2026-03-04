using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_ETC_312 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterWeaponAura(C.ETC_312, (b, weapon) =>
            {
                if (b?.HeroPower == null || weapon == null) return;
                if (weapon == b.FriendWeapon)
                    b.HeroPower.Cost = 0;
            });

            db.RegisterAfterFriendlyHeroPowerUsedWeapon(C.ETC_312, b =>
            {
                if (b?.FriendWeapon == null || b.FriendWeapon.CardId != C.ETC_312) return;
                b.FriendWeapon.Health -= 1;
            });
        }
    }
}
