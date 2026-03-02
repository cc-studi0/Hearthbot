using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TRL_151 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TRL_151",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("summon", v: 0, atk: 5, hp: 5, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
