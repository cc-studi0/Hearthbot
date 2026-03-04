using System.Linq;
using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SW_001 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterFriendlySpellCastWeapon(C.SW_001, (b, spellCard) =>
            {
                if (b?.FriendWeapon == null || b.FriendWeapon.CardId != C.SW_001) return;
                if (spellCard == null) return;

                b.FriendWeapon.EffectCounter1 += spellCard.Cost;
                if (b.FriendWeapon.EffectCounter1 >= 5)
                {
                    b.FriendWeapon.EffectCounter1 -= 5;
                    var spells = b.Hand.Where(c => CardEffectScriptHelpers.IsSpellCard(c.CardId)).ToList();
                    if (spells.Count > 0)
                    {
                        var target = spells[CardEffectDB.Rnd.Next(spells.Count)];
                        target.Cost = System.Math.Max(0, target.Cost - 5);
                    }
                    b.FriendWeapon.Health -= 1;
                }
            });
        }
    }
}
