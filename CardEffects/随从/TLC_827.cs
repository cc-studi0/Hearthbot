using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TLC_827 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TLC_827",
            new TriggerDef(
                "EndOfTurn",
                "None",
                new EffectDef("buff", v: 0, atk: 1, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
