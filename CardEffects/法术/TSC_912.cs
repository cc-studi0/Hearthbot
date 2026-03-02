using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TSC_912 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TSC_912",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 3, hp: 3, n: 1, dur: 0, useSP: false),
                new EffectDef("summon", v: 0, atk: 3, hp: 3, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
