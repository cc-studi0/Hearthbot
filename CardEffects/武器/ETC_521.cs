using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_ETC_521 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterFriendlySpellCastWeapon(C.ETC_521, (b, spellCard) =>
            {
                if (b?.FriendWeapon == null || b.FriendWeapon.CardId != C.ETC_521) return;
                if (spellCard == null || b.FriendMinions.Count >= 7) return;

                var cost = spellCard.Cost;
                if (cost > 0)
                {
                    var elemental = new SimEntity
                    {
                        CardId = C.EX1_249,
                        IsFriend = true,
                        Type = SmartBot.Plugins.API.Card.CType.MINION,
                        Atk = cost,
                        Health = cost,
                        MaxHealth = cost
                    };
                    b.FriendMinions.Add(elemental);
                    b.FriendWeapon.Health -= 1;
                }
            });
        }
    }
}
