using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_CS2_092 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_CS2_092",
            new TriggerDef(
                "Spell",
                "AnyMinion",
                new EffectDef("buff", v: 0, atk: 4, hp: 4, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
