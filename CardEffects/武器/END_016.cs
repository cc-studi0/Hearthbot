using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 时空之爪（武器）：在你的英雄攻击后，弃掉你法力值消耗最高的牌。
    internal sealed class Sim_END_016 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.END_016, (b, hero, target, ctx) =>
            {
                if (b == null || hero != b.FriendHero) return;
                if (b.Hand.Count == 0) return;

                // 找到手牌中费用最高的牌并弃掉
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
            });
        }
    }
}
