using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_GVG_061 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_GVG_061",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("equip", v: 0, atk: 1, hp: 0, n: 1, dur: 4, useSP: false)
            )
            );
        }
    }
}
