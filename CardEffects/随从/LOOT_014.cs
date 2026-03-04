using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 狗头人图书管理员：战吼：抽一张牌。对你的英雄造成2点伤害。
    internal sealed class Sim_LOOT_014 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.LOOT_014, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 抽一张牌
                CardEffectDB.DrawCard(b, b.FriendDeckCards);

                // 对己方英雄造成 2 点伤害
                if (b.FriendHero != null)
                    CardEffectDB.Dmg(b, b.FriendHero, 2);
            }, BattlecryTargetType.None);
        }
    }
}
