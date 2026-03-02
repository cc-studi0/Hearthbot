using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_GDB_124 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "GDB_124",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 6, hp: 6, n: 1, dur: 0, useSP: false),
                new EffectDef("summon", v: 0, atk: 6, hp: 6, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
