using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SW_457 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterFriendlyMinionDiedWeapon(C.SW_457, (b, dead) =>
            {
                if (b?.FriendWeapon == null || b.FriendWeapon.CardId != C.SW_457 || dead == null) return;
                if (!CardEffectScriptHelpers.IsBeastCard(dead.CardId)) return;

                b.FriendWeapon.EffectCounter1 += 1;
                while (b.FriendWeapon.EffectCounter1 >= 3 && b.FriendWeapon.Health > 0)
                {
                    b.FriendWeapon.EffectCounter1 -= 3;
                    CardEffectScriptHelpers.DrawRandomCardToHandByPredicate(
                        b,
                        CardEffectScriptHelpers.IsBeastCard,
                        dead,
                        buffAtk: 1,
                        buffHp: 1);
                    b.FriendWeapon.Health -= 1;
                }
            });
        }
    }
}
