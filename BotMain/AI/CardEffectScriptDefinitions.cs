namespace BotMain.AI
{
    /// <summary>
    /// 每张卡一个脚本类，所有效果只通过该入口注册。
    /// </summary>
    public interface ICardEffectScript
    {
        void Register(CardEffectDB db);
    }

    public sealed class EffectDef
    {
        public string Type { get; }
        public int V { get; }
        public int Atk { get; }
        public int Hp { get; }
        public int N { get; }
        public int Dur { get; }
        public bool UseSP { get; }

        public EffectDef(string type, int v = 0, int atk = 0, int hp = 0, int n = 1, int dur = 0, bool useSP = false)
        {
            Type = type ?? "";
            V = v;
            Atk = atk;
            Hp = hp;
            N = n;
            Dur = dur;
            UseSP = useSP;
        }
    }

    public sealed class TriggerDef
    {
        public string Trigger { get; }
        public string TargetType { get; }
        public EffectDef[] Effects { get; }

        public TriggerDef(string trigger, string targetType, params EffectDef[] effects)
        {
            Trigger = trigger ?? "";
            TargetType = string.IsNullOrWhiteSpace(targetType) ? "None" : targetType;
            Effects = effects ?? new EffectDef[0];
        }
    }
}
