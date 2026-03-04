using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DINO_408 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.DINO_408, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null || b.Hand.Count == 0) return;
                var leftmost = b.Hand[0];
                var deck = (s != null && !s.IsFriend) ? b.EnemyDeckCards : b.FriendDeckCards;
                deck.Add(leftmost.CardId);
                b.Hand.RemoveAt(0);
            });

            db.Register(C.DINO_408, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b == null) return;
                var deck = (s != null && !s.IsFriend) ? b.EnemyDeck : b.FriendDeck;
                CardEffectDB.DrawCard(b, deck);
                CardEffectDB.DrawCard(b, deck);
            });
        }
    }
}
