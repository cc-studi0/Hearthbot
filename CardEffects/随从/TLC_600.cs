using System;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TLC_600 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 乘风浮龙：战吼，造成 5 点伤害并获得 5 点护甲值
            db.Register(C.TLC_600, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 5);
                if (s != null && !s.IsFriend)
                {
                    if (b.EnemyHero != null) b.EnemyHero.Armor += 5;
                }
                else
                {
                    if (b.FriendHero != null) b.FriendHero.Armor += 5;
                }
            }, BattlecryTargetType.AnyCharacter);

            // Kindred：这里按“手牌中有其他龙牌时减费 3”近似处理
            db.Register(C.TLC_600, EffectTrigger.Aura, (b, s, t) =>
            {
                if (s == null) return;
                int baseCost = CardEffectScriptHelpers.GetBaseCost(C.TLC_600, s.Cost);
                bool hasOtherDragon = CardEffectScriptHelpers.HasDragonInHand(b, s);
                s.Cost = Math.Max(0, baseCost - (hasOtherDragon ? 3 : 0));
            });
        }
    }
}
