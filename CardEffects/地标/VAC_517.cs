using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    // TODO: 手写完善地标效果
    internal sealed class Sim_VAC_517 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "VAC_517",
                new TriggerDef(
                    "LocationActivation",
                    "None",
                    new EffectDef("noop")
                )
            );
        }
    }
}
