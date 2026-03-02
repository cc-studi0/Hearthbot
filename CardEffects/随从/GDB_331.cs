using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_GDB_331 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "GDB_331",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("summon", v: 0, atk: 4, hp: 4, n: 1, dur: 0, useSP: false),
                new EffectDef("summon", v: 0, atk: 4, hp: 4, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
