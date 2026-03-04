using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 行尸：嘲讽。如果你弃掉了这张随从牌，则会召唤它。
    internal sealed class Sim_RLK_532 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 确保打出时具备嘲讽
            db.Register(C.RLK_532, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s != null)
                    s.IsTaunt = true;
            }, BattlecryTargetType.None);
        }
    }
}
