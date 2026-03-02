using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_REV_248 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "REV_248",
            new TriggerDef(
                "Spell",
                "AnyMinion",
                new EffectDef("buff", v: 0, atk: 0, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
