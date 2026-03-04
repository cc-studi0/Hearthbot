using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_WON_318 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterFriendlyHeroPowerUsedWeapon(C.WON_318, b =>
            {
                if (b?.FriendWeapon == null || b.FriendWeapon.CardId != C.WON_318) return;
                b.FriendWeapon.Atk += 1;
            });
        }
    }
}
