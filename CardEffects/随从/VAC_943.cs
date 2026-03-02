using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_VAC_943 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "VAC_943",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("summon", v: 0, atk: 6, hp: 6, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
