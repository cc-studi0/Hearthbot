using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_EX1_046 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_EX1_046",
            new TriggerDef(
                "Battlecry",
                "AnyMinion",
                new EffectDef("buff", v: 0, atk: 2, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
