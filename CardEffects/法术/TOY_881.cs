using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TOY_881 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TOY_881",
            new TriggerDef(
                "Spell",
                "FriendlyMinion",
                new EffectDef("buff", v: 0, atk: 2, hp: 3, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
