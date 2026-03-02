using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_LOOT_500 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "LOOT_500",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("buff", v: 0, atk: 4, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
