using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_GDB_138 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "GDB_138",
            new TriggerDef(
                "Spell",
                "AnyMinion",
                new EffectDef("buff", v: 0, atk: 3, hp: 3, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
