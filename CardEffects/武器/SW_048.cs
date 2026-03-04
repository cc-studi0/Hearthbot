using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SW_048 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterFriendlyDivineShieldLostWeapon(C.SW_048, (b, lostShieldMinion) =>
            {
                if (b?.FriendWeapon == null || b.FriendWeapon.CardId != C.SW_048) return;
                CardEffectScriptHelpers.BuffAllMinionsInHand(b, 1, 1);
                b.FriendWeapon.Health -= 1;
            });
        }
    }
}
