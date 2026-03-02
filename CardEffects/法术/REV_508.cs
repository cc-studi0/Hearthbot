using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_REV_508 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "REV_508",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("draw", v: 0, atk: 0, hp: 0, n: 2, dur: 0, useSP: false)
            )
            );
        }
    }
}
