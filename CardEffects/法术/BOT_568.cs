using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_BOT_568 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "BOT_568",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("draw", v: 0, atk: 0, hp: 0, n: 3, dur: 0, useSP: false)
            )
            );
        }
    }
}
