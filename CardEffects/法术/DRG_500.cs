using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DRG_500 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "DRG_500",
            new TriggerDef(
                "Spell",
                "AnyMinion",
                new EffectDef("dmg", v: 5, atk: 0, hp: 0, n: 1, dur: 0, useSP: true),
                new EffectDef("armor", v: 5, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
