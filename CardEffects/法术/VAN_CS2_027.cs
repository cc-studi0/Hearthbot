using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_VAN_CS2_027 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "VAN_CS2_027",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 0, hp: 2, n: 1, dur: 0, useSP: false),
                new EffectDef("summon", v: 0, atk: 0, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
