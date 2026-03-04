using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_AT_034 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterFriendlyHeroPowerUsedWeapon(C.AT_034, b =>
            {
                if (b?.FriendWeapon == null || b.FriendWeapon.CardId != C.AT_034) return;
                b.FriendWeapon.Atk += 1;
            });
        }
    }
}
