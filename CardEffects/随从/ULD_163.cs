using System;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 过期货物专卖商：战吼：弃掉你手牌中法力值消耗最高的牌。
    //                 亡语：将弃掉的牌的两张复制置入你的手牌。
    internal sealed class Sim_ULD_163 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 战吼：弃掉手牌中费用最高的牌
            db.Register(C.ULD_163, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.Hand.Count == 0) return;

                int maxCost = -1;
                int maxIdx = 0;
                for (int i = 0; i < b.Hand.Count; i++)
                {
                    if (b.Hand[i].Cost > maxCost)
                    {
                        maxCost = b.Hand[i].Cost;
                        maxIdx = i;
                    }
                }

                // 记录被弃掉的卡牌 ID 到 EffectCounter1
                if (s != null)
                    s.EffectCounter1 = (int)b.Hand[maxIdx].CardId;

                b.Hand.RemoveAt(maxIdx);
            }, BattlecryTargetType.None);

            // 亡语：将弃掉的牌的两张复制加入手牌
            db.Register(C.ULD_163, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (s == null || s.EffectCounter1 == 0) return;
                var discardedId = (C)s.EffectCounter1;

                for (int i = 0; i < 2; i++)
                {
                    if (b.Hand.Count >= 10) break;
                    var copy = CardEffectScriptHelpers.CreateCardInHand(discardedId, true, b);
                    if (copy != null)
                        b.Hand.Add(copy);
                    else
                        b.FriendCardDraw += 1;
                }
            });
        }
    }
}
