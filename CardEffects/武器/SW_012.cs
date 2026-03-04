using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SW_012 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterFriendlySpellCastWeapon(C.SW_012, (b, spell) =>
            {
                if (b?.FriendWeapon == null || b.FriendWeapon.CardId != C.SW_012 || spell == null) return;
                if (!CardEffectScriptHelpers.IsShadowSpellCard(spell.CardId)) return;

                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 1);
                if (b.EnemyHero != null)
                    CardEffectDB.Dmg(b, b.EnemyHero, 1);

                b.FriendWeapon.Health -= 1;
            });
        }
    }
}
