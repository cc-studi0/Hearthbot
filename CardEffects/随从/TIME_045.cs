using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TIME_045 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 永恒雏龙：剧毒，复生（确保打出时具备关键字）
            db.Register(C.TIME_045, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                s.HasPoison = true;
                s.HasReborn = true;
            }, BattlecryTargetType.None);
        }
    }
}
