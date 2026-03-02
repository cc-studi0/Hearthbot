using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_ETC_078 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "ETC_078",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("equip", v: 0, atk: 1, hp: 0, n: 1, dur: 2, useSP: false)
            )
            );
        }
    }
}
