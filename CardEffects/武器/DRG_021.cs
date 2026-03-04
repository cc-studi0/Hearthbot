using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DRG_021 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.DRG_021, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null) return;
                // 简化实现：抽一张牌作为加拉克隆效果的近似
                var deck = (s != null && !s.IsFriend) ? b.EnemyDeckCards : b.FriendDeckCards;
                CardEffectDB.DrawCard(b, deck);
            });
        }
    }
}
