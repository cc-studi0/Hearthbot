using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_EX1_259 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_EX1_259",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("dmg_enemy", v: 3, atk: 0, hp: 0, n: 1, dur: 0, useSP: true)
            )
            );
        }
    }
}
