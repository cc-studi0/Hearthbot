using System.Linq;
using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_BAR_875 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.BAR_875, (b, hero, target, ctx) =>
            {
                if (b == null) return;
                var deck = (hero == b.FriendHero) ? b.FriendDeckCards : b.EnemyDeckCards;
                var secrets = deck.Where(id => CardEffectScriptHelpers.IsSpellCard(id)).ToList();
                if (secrets.Count > 0)
                {
                    var secretId = secrets[CardEffectDB.Rnd.Next(secrets.Count)];
                    deck.Remove(secretId);
                    var secretList = (hero == b.FriendHero) ? b.FriendSecrets : b.EnemySecrets;
                    if (secretList.Count < 5 && !secretList.Contains(secretId))
                        secretList.Add(secretId);
                }
            });
        }
    }
}
