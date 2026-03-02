using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_CS2_089 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_CS2_089",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("heal_hero", v: 8, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
