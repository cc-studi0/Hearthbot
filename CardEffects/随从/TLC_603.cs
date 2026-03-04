using System;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 栉龙：战吼：抽一张牌。亡语：弃掉该牌。
    internal sealed class Sim_TLC_603 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 战吼：抽一张牌
            db.Register(C.TLC_603, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            }, BattlecryTargetType.None);

            // 亡语：弃掉刚抽的牌（近似移除手牌中最后一张）
            db.Register(C.TLC_603, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.Hand.Count > 0)
                    b.Hand.RemoveAt(b.Hand.Count - 1);
            });
        }
    }
}
