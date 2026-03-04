using System;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 维希度斯的窟穴：检视你手牌中的三张牌，选择一张弃掉。抽两张牌。
    internal sealed class Sim_WON_103 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.WON_103, EffectTrigger.LocationActivation, (b, s, t) =>
            {
                // 弃掉手牌中费用最高的一张（近似"选择一张弃掉"）
                if (b.Hand.Count > 0)
                {
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
                    b.Hand.RemoveAt(maxIdx);
                }

                // 抽两张牌
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
        }
    }
}
