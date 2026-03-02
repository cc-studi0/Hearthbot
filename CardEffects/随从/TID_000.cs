using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TID_000 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TID_000",
            new TriggerDef(
                "EndOfTurn",
                "None",
                new EffectDef("buff", v: 0, atk: 1, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
