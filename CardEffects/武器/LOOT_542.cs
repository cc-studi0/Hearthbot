using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_LOOT_542 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.LOOT_542, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b == null) return;
                // “保留附魔”在当前模拟器未建模，这里实现核心行为：洗回牌库。
                if (s != null && !s.IsFriend)
                    b.EnemyDeckCards.Add(C.LOOT_542);
                else
                    b.FriendDeckCards.Add(C.LOOT_542);
            });
        }
    }
}
