using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_WW_410 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "WW_410",
            new TriggerDef(
                "Spell",
                "AnyMinion",
                new EffectDef("dmg", v: 7, atk: 0, hp: 0, n: 1, dur: 0, useSP: true),
                new EffectDef("draw", v: 0, atk: 0, hp: 0, n: 7, dur: 0, useSP: false)
            )
            );
        }
    }
}
