using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_EDR_457 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 龙巢守护者：战吼，如果手牌中有龙牌，装备一把 2/2 的剑
            db.Register(C.EDR_457, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (!CardEffectScriptHelpers.HasDragonInHand(b, s)) return;

                bool isFriend = s == null || s.IsFriend;
                CardEffectScriptHelpers.EquipWeaponByStats(b, isFriend, (C)0, 2, 2);
            }, BattlecryTargetType.None);
        }
    }
}
