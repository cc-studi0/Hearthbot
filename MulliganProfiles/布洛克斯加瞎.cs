using SmartBot.Mulligan;
using SmartBot.Plugins.API;
using System.Collections.Generic;

namespace SmartBot.MulliganProfiles
{
    public class 布洛克斯加瞎 : MulliganProfile
    {
        // 布洛克斯加瞎 Mulligan 配置
        // 优先级：强留（S）> 常留（A）> 按形势留（B）> 丢弃（C）
        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass ownClass, Card.CClass oppClass)
        {
            var keep = new List<Card.Cards>();

            // 根据数据图示调整留牌优先级（优先保留能保证早期节奏或抽牌/触发的卡）
            // 强留（S） — 极其优先保留，用于保证前期节奏
            KeepIfExists(choices, keep, Card.Cards.EDR_840); // 恐怖收割：高保留率，提供抽牌/展开
            KeepIfExists(choices, keep, Card.Cards.VAC_933); // 飞行员帕奇斯：开场节奏
            KeepIfExists(choices, keep, Card.Cards.CORE_BAR_330); // 獠牙锥刃：稳定一费武器

            // 常留（A） — 场况通常保留
            KeepIfExists(choices, keep, Card.Cards.END_007); // 发挥优势：抽牌/小额伤害
            KeepIfExists(choices, keep, Card.Cards.TLC_902); // 虫害侵扰：低费补牌/占场

            // 按形势留（B） — 对控制或特定对手更倾向保留
            if (IsControlOpponent(oppClass))
            {
                KeepIfExists(choices, keep, Card.Cards.MIS_102); // 退货政策：对控制有更高价值
                KeepIfExists(choices, keep, Card.Cards.TLC_100); // 导航员伊莉斯：构筑地标的中期价值
            }

            // 低优先（C） — 一般不保留的卡（例如大费或对线价值低的法术）
            // 红牌（TOY_644）：起手不留（攻略要求），无论对局类型都不加入 keep

            // 一般不保留的大费卡（C）：布洛克斯加/奇利亚斯/无界空宇/调酒师等
            // 但若手牌内存在相关组合或需要特例，可由玩家手动调整。

            return keep;
        }

        private void KeepIfExists(List<Card.Cards> choices, List<Card.Cards> keep, Card.Cards card)
        {
            if (choices.Contains(card) && !keep.Contains(card))
                keep.Add(card);
        }

        private bool IsControlOpponent(Card.CClass oppClass)
        {
            // 简单归类：术士/骑士/萨满等可视为偏控制（调整可扩展）
            return oppClass == Card.CClass.WARLOCK || oppClass == Card.CClass.PALADIN || oppClass == Card.CClass.SHAMAN || oppClass == Card.CClass.MAGE || oppClass == Card.CClass.PRIEST;
        }
    }
}
