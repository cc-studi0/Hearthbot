using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_RLK_226 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "RLK_226",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("summon", v: 0, atk: 3, hp: 3, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
