using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    // TODO: 手写完善地标效果
    internal sealed class Sim_VAC_929 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "VAC_929",
                new TriggerDef(
                    "LocationActivation",
                    "None",
                    new EffectDef("noop")
                )
            );
        }
    }
}
