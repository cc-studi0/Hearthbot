using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    // TODO: 手写完善地标效果
    internal sealed class Sim_TTN_090 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TTN_090",
                new TriggerDef(
                    "LocationActivation",
                    "None",
                    new EffectDef("noop")
                )
            );
        }
    }
}
