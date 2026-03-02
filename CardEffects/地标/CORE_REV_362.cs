using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    // TODO: 手写完善地标效果
    internal sealed class Sim_CORE_REV_362 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_REV_362",
                new TriggerDef(
                    "LocationActivation",
                    "None",
                    new EffectDef("noop")
                )
            );
        }
    }
}
