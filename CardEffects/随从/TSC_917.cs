using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TSC_917 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TSC_917",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("summon", v: 0, atk: 5, hp: 6, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
