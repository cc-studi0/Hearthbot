using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API;
using SmartBotAPI.Battlegrounds;
using SmartBot.Plugins.API.Actions;

/*
 * 狂野：蛋术（凯洛斯的蛋）主策略
 * - 目标：尽量实现“下蛋即破蛋”，避免裸下蛋吃沉默/变形
 * - tip: 项目是 c#6，不要用新语法
 */

namespace SmartBotProfiles
{
    [Serializable]
    public class WildEggWarlock : Profile
    {
        // 策略版本号：每次改动请递增（用于日志定位当前运行的是哪一版）
        private const string ProfileVersion = "2026-02-10.071";

        // 降噪开关：默认关闭；需要排查问题时可改为 true
        // 备注：不要用 const（SmartBot 可能把 if(false) 判为不可达代码并当作编译错误）
        private static readonly bool VerboseLog = false;

        private string _log = "";

        #region 英雄技能
        private const Card.Cards TheCoin = Card.Cards.GAME_005;
        private const Card.Cards Innervate = Card.Cards.CORE_EX1_169;
        private const Card.Cards LifeTap = Card.Cards.CS2_056_H1;
        private const Card.Cards LifeTapLegacy = Card.Cards.HERO_07bp;
        #endregion

        #region 卡牌处理方法（同卡牌逻辑收口）

        private void ProcessCard_TSC_908_Finley(ProfileParameters p, Board board,
            bool wantFinleySwap)
        {
            // 海中向导芬利：慢局且手牌缺关键牌时，主动“换手找 key”
            if (!wantFinleySwap) return;
            Card.Cards bestEggOnBoardId = GetBestEggOnBoardId(board);

            // 日志：芬利回合需要“看清楚换掉了什么/场面是不是允许再拖一回合”
            try
            {
                AddLog("[芬利] 触发换手：打印换手前手牌/场面快照");
                LogHandSnapshotOneLine(board);
                LogBoardSnapshotOneLine(board);
            }
            catch { }

            // 更硬一点，避免被分流/过牌/随手解牌抢节奏
            p.CastMinionsModifiers.AddOrUpdate(Finley, new Modifier(-520));
            p.PlayOrderModifiers.AddOrUpdate(Finley, new Modifier(2000));

            // 芬利回合：尽量不要用其他“找牌/解场/站场”抢先（先换手再重算决策）
            p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(450));
            p.CastSpellsModifiers.AddOrUpdate(MorriganRealm, new Modifier(450));
            p.CastSpellsModifiers.AddOrUpdate(Defile, new Modifier(450));
            p.CastSpellsModifiers.AddOrUpdate(Grimoire, new Modifier(450, bestEggOnBoardId));

            // 海星不该当节奏裸拍；芬利回合直接压住
            p.CastMinionsModifiers.AddOrUpdate(Starfish, new Modifier(9999));
            p.PlayOrderModifiers.AddOrUpdate(Starfish, new Modifier(-9999));

            AddLog(CardName(Finley) + " -520 | 顺序2000（慢局缺key且手牌臃肿：优先芬利换手找蛋/检索/安布拉）");
        }

        private void LogHandSnapshotOneLine(Board board)
        {
            try
            {
                if (board == null) return;

                var parts = new List<string>();
                if (board.Hand != null)
                {
                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;

                        string name = "";
                        try { name = !string.IsNullOrWhiteSpace(c.Template.NameCN) ? c.Template.NameCN : c.Template.Id.ToString(); }
                        catch { name = c.Template.Id.ToString(); }

                        parts.Add(name + "[" + c.CurrentCost + "]" + c.Template.Id);
                    }
                }

                string hand = parts.Count > 0 ? string.Join(";", parts) : "(空)";
                AddLog("[手牌快照] H=" + hand);
            }
            catch { }
        }

        private void LogBoardSnapshotOneLine(Board board)
        {
            try
            {
                if (board == null) return;

                string friendMinions = "";
                try
                {
                    var list = new List<string>();
                    if (board.MinionFriend != null)
                    {
                        foreach (var m in board.MinionFriend)
                        {
                            if (m == null || m.Template == null) continue;
                            string n = "";
                            try { n = !string.IsNullOrWhiteSpace(m.Template.NameCN) ? m.Template.NameCN : m.Template.Id.ToString(); }
                            catch { n = m.Template.Id.ToString(); }
                            list.Add(n + "[" + m.CurrentAtk + "/" + m.CurrentHealth + "]" + (m.CanAttack ? "A" : ""));
                        }
                    }
                    friendMinions = list.Count > 0 ? string.Join(";", list) : "(空)";
                }
                catch { friendMinions = "(解析失败)"; }

                string enemyMinions = "";
                try
                {
                    var list = new List<string>();
                    if (board.MinionEnemy != null)
                    {
                        foreach (var m in board.MinionEnemy)
                        {
                            if (m == null || m.Template == null) continue;
                            string n = "";
                            try { n = !string.IsNullOrWhiteSpace(m.Template.NameCN) ? m.Template.NameCN : m.Template.Id.ToString(); }
                            catch { n = m.Template.Id.ToString(); }
                            list.Add(n + "[" + m.CurrentAtk + "/" + m.CurrentHealth + "]" + (m.IsTaunt ? "T" : ""));
                        }
                    }
                    enemyMinions = list.Count > 0 ? string.Join(";", list) : "(空)";
                }
                catch { enemyMinions = "(解析失败)"; }

                AddLog("[场面快照] F=" + friendMinions + " | E=" + enemyMinions);
            }
            catch { }
        }

        private void ProcessCard_TLC_451_CurseTomb(ProfileParameters p, Board board,
            bool wantFinleySwap,
            bool hasEggOnBoard,
            bool canPopEggThisTurnIfPlayed,
            bool hasEggBreakerForExistingEgg,
            bool hasEggInHand,
            bool eggStillInDeck,
            bool hasEggbearerInHand,
            bool eggbearerPlayableNow)
        {
            // ==============================================
            // Card.Cards.TLC_451 - 咒怨之墓
            // 从颜射术口径移植：
            // - 使用条件：MaxMana>=3 或 (MaxMana>=2且有币)
            // - 硬门槛：ManaAvailable>=2
            // - 0费：极高优先
            // - 正常费用：优先发现
            // - 用户口径：非 combo（本回合没有 ComboModifier）时，咒怨之墓使用次序最优先
            // - 但要保留之前的条件/互斥（芬利回合、蛋线回合让位等）
            // ==============================================

            if (!board.HasCardInHand(Tomb)) return;

            try
            {
                var curse = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Tomb);
                if (curse == null) return;

                bool hasCoin = false;
                try { hasCoin = board.Hand.Any(x => x != null && x.Template != null && x.Template.Id == TheCoin); }
                catch { hasCoin = false; }

                bool canUseCurse = board.MaxMana >= 3 || (board.MaxMana >= 2 && hasCoin);
                if (!canUseCurse)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(999));
                    p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(-999));
                    AddLog("[咒怨之墓-条件限制] MaxMana=" + board.MaxMana + ", 有币=" + hasCoin + " => 禁止使用(999/-999)");
                    return;
                }

                // 保留原口径：硬门槛 ManaAvailable>=2
                if (board.ManaAvailable < 2)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(999));
                    p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(-999));
                    AddLog("[咒怨之墓-硬门槛] ManaAvailable=" + board.ManaAvailable + " < 2 => 禁止使用(999/-999)");
                    return;
                }

                if (curse.CurrentCost == 0 && board.ManaAvailable == 0)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(500));
                    p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(-999));
                    AddLog("[咒怨之墓-保护] 费用耗尽(0/" + board.MaxMana + ") => 抑制使用(500/-999)");
                    return;
                }

                // 蛋术口径补充：若本回合“能直接下蛋”（场上没蛋且手里有蛋且费用够），则墓不要抢先。
                // 典型场景：3费只有蛋+墓时，先下蛋比先开墓更重要（否则会出现“先墓不下蛋”的行为）。
                try
                {
                    var eggCard = board.Hand != null
                        ? board.Hand
                            .Where(x => x != null && x.Template != null && IsEggStage(x.Template.Id))
                            .OrderByDescending(x => GetEggStageRank(x.Template.Id))
                            .FirstOrDefault()
                        : null;
                    int eggCost = eggCard != null ? eggCard.CurrentCost : 99;
                    bool hasEggOnBoardLocal = board.MinionFriend != null
                        && board.MinionFriend.Any(x => x != null && x.Template != null && IsEggStage(x.Template.Id));
                    bool eggPlayableNow = eggCard != null && !hasEggOnBoardLocal && board.ManaAvailable >= eggCost;

                    if (eggPlayableNow)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(450));
                        p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(-9999));
                        AddLog("[咒怨之墓-互斥] 本回合可下蛋(" + CardName(eggCard.Template.Id) + ") => 暂缓使用咒怨之墓(450/-9999)，优先下蛋");
                        return;
                    }
                }
                catch { }

                // 蛋术口径补充：牌库仍有蛋且手/场无蛋，且布蛋者本回合可打出时，
                // 本回合直接禁用墓，避免“0费墓抢在布蛋者之前”。
                if (!hasEggInHand && !hasEggOnBoard && eggStillInDeck && hasEggbearerInHand && eggbearerPlayableNow)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(-9999));
                    AddLog("[咒怨之墓-互斥] 牌库仍有蛋且布蛋者可下 => 本回合禁用咒怨之墓(9999/-9999)，优先布蛋者检索蛋");
                    return;
                }

                // 蛋术专属互斥：若本回合存在更确定的“蛋线”，墓让位（墓更像防空过/找key）
                if (wantFinleySwap)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(350));
                    p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(-900));
                    AddLog("[咒怨之墓-互斥] 芬利回合 => 暂缓使用咒怨之墓(350/-900)");
                    return;
                }
                if (canPopEggThisTurnIfPlayed)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(350));
                    p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(-900));
                    AddLog("[咒怨之墓-互斥] 本回合可下蛋即破 => 暂缓使用咒怨之墓(350/-900)");
                    return;
                }
                if (hasEggOnBoard && hasEggBreakerForExistingEgg)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(350));
                    p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(-900));
                    AddLog("[咒怨之墓-互斥] 场上有蛋且可破 => 暂缓使用咒怨之墓(350/-900)");
                    return;
                }

                // 只在“非 combo 回合”把墓的使用次序顶到最优先；已有 combo 时不覆盖、不抢（避免打断更高优先连锁）。
                bool nonComboTurn = p.ComboModifier == null;

                if (curse.CurrentCost == 0)
                {
                    p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(nonComboTurn ? 9999 : 9999));
                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(-3000));

                    if (!p.ForcedResimulationCardList.Contains(Tomb))
                        p.ForcedResimulationCardList.Add(Tomb);

                    AddLog(nonComboTurn
                        ? "[咒怨之墓-优先级2/非combo最高] 0费 => 极高优先(9999/-3000)，添加到重算列表"
                        : "[咒怨之墓-优先级2] 0费 => 极高优先(9999/-3000)，添加到重算列表");
                    return;
                }

                if (curse.CurrentCost <= board.ManaAvailable)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(-150));
                    p.PlayOrderModifiers.AddOrUpdate(Tomb, new Modifier(nonComboTurn ? 9999 : 2000));

                    if (!p.ForcedResimulationCardList.Contains(Tomb))
                        p.ForcedResimulationCardList.Add(Tomb);

                    AddLog(nonComboTurn
                        ? "[咒怨之墓-优先级3/非combo最高] 正常费用(" + curse.CurrentCost + "费) => 使用次序最优先(-150/9999)，添加到重算列表"
                        : "[咒怨之墓-优先级3] 正常费用(" + curse.CurrentCost + "费) => 优先发现(-150/2000)，添加到重算列表");
                    return;
                }
            }
            catch { }
        }

        private void ProcessCard_LOOT_014_KoboldLibrarian(ProfileParameters p, Board board,
            bool wantFinleySwap,
            bool hasEggInHand,
            bool hasEggOnBoard,
            bool canPopEggThisTurnIfPlayed,
            bool isAggroLikely,
            bool isComboLikely,
            ref bool forceDisableLifeTap,
            ref string forceDisableLifeTapReason)
        {
            // ==============================================
            // 狗头人图书管理员：从颜射术口径移植“过牌优先级高于分流”，但蛋术专属护栏：
            // - 若本回合计划下蛋/破蛋，则狗头人不能抢在蛋之前
            // - 芬利回合：压住狗头人，避免先抽再换手
            // ==============================================

            if (!board.HasCardInHand(Librarian)) return;

            bool hasCoin = false;
            try { hasCoin = board.HasCardInHand(TheCoin); } catch { hasCoin = false; }
            int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);

            bool hasPlayableKobold = false;
            try
            {
                hasPlayableKobold = board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == Librarian
                    && c.CurrentCost <= effectiveMana);
            }
            catch { hasPlayableKobold = false; }

            if (!hasPlayableKobold) return;

            // 蛋术专属：无蛋阶段优先“神圣布蛋者检索蛋”，避免狗头人先抽导致节奏偏离
            // 触发条件尽量严格：手/场无蛋 + 坟场无蛋(推断蛋仍在牌库) + 手里有布蛋者且本回合可用
            try
            {
                if (!hasEggInHand && !hasEggOnBoard)
                {
                    int eggInGraveLocal = 0;
                    try
                    {
                        // SmartBot API: 己方坟场使用 FriendGraveyard（List<Cards>）
                        eggInGraveLocal = board.FriendGraveyard != null
                            ? board.FriendGraveyard.Count(x => IsEggStage(x))
                            : 0;
                    }
                    catch { eggInGraveLocal = 0; }

                    bool eggStillInDeckLocal = eggInGraveLocal == 0;
                    var eggbearerCard = board.Hand != null
                        ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Eggbearer)
                        : null;
                    int ebCost = eggbearerCard != null ? eggbearerCard.CurrentCost : 99;
                    bool eggbearerPlayableNow = eggbearerCard != null && ebCost <= board.ManaAvailable;

                    if (eggStillInDeckLocal && eggbearerPlayableNow)
                    {
                        // 不再硬禁用狗头人：只要 1 费能打狗头人就允许先抽一口；但布蛋者仍高优先级。
                        p.CastMinionsModifiers.AddOrUpdate(Librarian, new Modifier(-150));
                        p.PlayOrderModifiers.AddOrUpdate(Librarian, new Modifier(5000));

                        p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(-350));
                        p.PlayOrderModifiers.AddOrUpdate(Eggbearer, new Modifier(5200));

                        forceDisableLifeTap = true;
                        if (string.IsNullOrEmpty(forceDisableLifeTapReason))
                            forceDisableLifeTapReason = "无蛋且可布蛋者：优先检索蛋";

                        AddLog("[过牌互斥] 手/场/坟无蛋且布蛋者本回合可直接打出：布蛋者(-350)优先于狗头人(-150)，但不硬禁用狗头人；并禁用分流");
                    }
                }
            }
            catch { }

            if (wantFinleySwap)
            {
                p.CastMinionsModifiers.AddOrUpdate(Librarian, new Modifier(999));
                p.PlayOrderModifiers.AddOrUpdate(Librarian, new Modifier(-999));
                return;
            }

            bool shouldPrioritizeEggThisTurn = false;
            try
            {
                // 组合/慢速对局里：如果手里有蛋且本回合能下（哪怕不一定能破），优先把蛋落地。
                var eggCard = board.Hand
                    .Where(x => x != null && x.Template != null && IsEggStage(x.Template.Id))
                    .OrderByDescending(x => GetEggStageRank(x.Template.Id))
                    .FirstOrDefault();
                int eggCost = eggCard != null ? eggCard.CurrentCost : 99;
                bool eggPlayableNow = hasEggInHand && !hasEggOnBoard && board.ManaAvailable >= eggCost;
                shouldPrioritizeEggThisTurn = eggPlayableNow && !isAggroLikely && (canPopEggThisTurnIfPlayed || isComboLikely || board.MaxMana >= 3);
            }
            catch { shouldPrioritizeEggThisTurn = false; }

            if (shouldPrioritizeEggThisTurn)
            {
                // 硬压住狗头人，防止抢先抽一口导致“有蛋不下/优先分流”的旧问题回潮
                p.CastMinionsModifiers.AddOrUpdate(Librarian, new Modifier(350));
                p.PlayOrderModifiers.AddOrUpdate(Librarian, new Modifier(-999));

                forceDisableLifeTap = true;
                if (string.IsNullOrEmpty(forceDisableLifeTapReason))
                    forceDisableLifeTapReason = "手里可下蛋：优先下蛋而非分流";

                AddLog("[过牌优先级] 本回合优先蛋线 => 压制狗头人(350/-999)，并禁用分流");
                return;
            }

            // 默认：狗头人过牌高于分流
            p.PlayOrderModifiers.AddOrUpdate(Librarian, new Modifier(9500));
            p.CastMinionsModifiers.AddOrUpdate(Librarian, new Modifier(-1500));
            AddLog("[过牌优先级] 狗头人次高优先级(9500/-1500)，高于分流");

            // 当存在可用过牌随从时，进一步抑制分流，避免先分流后再打随从
            try
            {
                var friendAbility = ProfileCommon.GetFriendAbilityId(board, LifeTap);
                p.CastHeroPowerModifier.AddOrUpdate(friendAbility, new Modifier(800));
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(800));
                p.CastHeroPowerModifier.AddOrUpdate(LifeTapLegacy, new Modifier(800));
                AddLog("[过牌优先级] 抑制分流(800)，优先狗头人");
            }
            catch { }
        }

        private void ProcessCard_DINO_411_Eggbearer(ProfileParameters p, Board board,
            bool hasEggbearerInHand, bool hasEggOnBoard, bool hasEggInHand,
            bool canPopEggThisTurnIfPlayed,
            bool eggStillInDeck)
        {
            // 神圣布蛋者：更积极“先打出来找蛋”
            if (!hasEggbearerInHand) return;

            // 若本回合存在“下蛋即破”的强蛋线：不要让布蛋者抢掉回合优先级
            if (canPopEggThisTurnIfPlayed) return;

            if (!hasEggOnBoard && !hasEggInHand)
            {
                // 用户口径：如果牌库中还有蛋，且手上有神圣布蛋者，则优先使用（用 combo 强推）
                // 这里的“牌库里还有蛋”按 1 张构筑推断：手/场/坟都没有蛋阶段 => 仍在牌库（极少数被烧/变牌异常忽略）
                if (eggStillInDeck)
                {
                    try
                    {
                        var eggbearerCard = board.Hand != null
                            ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Eggbearer)
                            : null;
                        int ebCost = eggbearerCard != null ? eggbearerCard.CurrentCost : 99;

                        if (eggbearerCard != null && board.ManaAvailable >= ebCost)
                        {
                            // 口径：牌库有蛋 => 必出布蛋者。
                            // 实战修正：这里允许覆盖已有 ComboModifier，避免被旧连锁/低优先动作抢节奏（典型就是“墓先出”）。
                            if (p.ComboModifier != null)
                                AddLog("[COMBO] 覆盖已有ComboModifier：牌库仍有蛋，强制布蛋者优先检索蛋");
                            p.ComboModifier = new ComboSet(eggbearerCard.Id, eggbearerCard.Id);
                            AddLog("[COMBO] 强制布蛋者（牌库仍有蛋：优先检索蛋）");

                            // 关键：把布蛋者权重抬到“比狗头人更想打”（按数值口径：-350 优先用）
                            p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(-350));
                            p.PlayOrderModifiers.AddOrUpdate(Eggbearer, new Modifier(9800));
                            AddLog(CardName(Eggbearer) + " -350 | 顺序9800（无蛋且牌库仍有蛋：必出布蛋者检索蛋）");
                            return;
                        }
                    }
                    catch { }
                }

                if (!eggStillInDeck)
                {
                    // 牌库已无蛋：布蛋者价值大幅下降。
                    // 仅当本回合几乎“无牌可打”时，才作为兜底站场牌使用。
                    int manaNow = 0;
                    int eggbearerCostNow = 2;
                    bool hasCoin = false;
                    bool hasOtherPlayableCardNow = false;
                    try { manaNow = board.ManaAvailable; } catch { manaNow = 0; }

                    try
                    {
                        var ebCard = board.Hand != null
                            ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Eggbearer)
                            : null;
                        if (ebCard != null) eggbearerCostNow = ebCard.CurrentCost;
                    }
                    catch { eggbearerCostNow = 2; }

                    try { hasCoin = board != null && board.HasCardInHand(TheCoin); } catch { hasCoin = false; }

                    try
                    {
                        if (board != null && board.Hand != null)
                        {
                            hasOtherPlayableCardNow = board.Hand.Any(x =>
                                x != null && x.Template != null
                                && x.Template.Id != Eggbearer
                                && x.Template.Id != TheCoin
                                && x.CurrentCost <= manaNow);

                            // 若当前费用不够，但有硬币可把其它牌打出去，也视为“本回合有别的可用牌”。
                            if (!hasOtherPlayableCardNow && hasCoin)
                            {
                                int manaWithCoin = manaNow + 1;
                                hasOtherPlayableCardNow = board.Hand.Any(x =>
                                    x != null && x.Template != null
                                    && x.Template.Id != Eggbearer
                                    && x.Template.Id != TheCoin
                                    && x.CurrentCost <= manaWithCoin);
                            }
                        }
                    }
                    catch { hasOtherPlayableCardNow = false; }

                    bool canPlayEggbearerNow = manaNow >= eggbearerCostNow;
                    if (canPlayEggbearerNow && !hasOtherPlayableCardNow)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(-80));
                        p.PlayOrderModifiers.AddOrUpdate(Eggbearer, new Modifier(600));
                        AddLog(CardName(Eggbearer) + " -80 | 顺序600（牌库无蛋且本回合其余牌基本不可用：布蛋者兜底出）");
                    }
                    else
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(900));
                        p.PlayOrderModifiers.AddOrUpdate(Eggbearer, new Modifier(-500));
                        AddLog(CardName(Eggbearer) + " 900 | 顺序-500（牌库无蛋：大幅降权，优先其他可用牌）");
                    }
                    return;
                }

                p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(-520));
                p.PlayOrderModifiers.AddOrUpdate(Eggbearer, new Modifier(1200));
                AddLog(CardName(Eggbearer) + " -520 | 顺序1200（无蛋：优先打出布蛋者检索蛋，且牌库仍有蛋=>combo强推）");
            }

            // 关键修正：即使手里已经有蛋，布蛋者仍然可以作为“站场/卸手牌/补充蛋资源”的2费节奏牌。
            // 典型场景：手里有蛋但本回合打不出（剩2费）、或手牌偏满、或场上没随从需要先落地。
            if (!hasEggOnBoard && hasEggInHand)
            {
                // 实战口径：手里已有蛋时，没必要 1 费跳币去出神圣布蛋者（大多是浪费硬币）。
                // 只禁用“需要硬币才能出的那一下”，不影响后续正常 2 费拍布蛋者的站场用途。
                try
                {
                    bool hasCoin = false;
                    try { hasCoin = board != null && board.HasCardInHand(TheCoin); } catch { hasCoin = false; }

                    int ebCostCheck = 2;
                    try
                    {
                        var ebCardCheck = board.Hand != null
                            ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Eggbearer)
                            : null;
                        if (ebCardCheck != null) ebCostCheck = ebCardCheck.CurrentCost;
                    }
                    catch { ebCostCheck = 2; }

                    int manaNow = 0;
                    try { manaNow = board.ManaAvailable; } catch { manaNow = 0; }

                    bool needCoinToPlayEb = hasCoin && manaNow < ebCostCheck && (manaNow + 1) >= ebCostCheck;
                    if (manaNow == 1 && needCoinToPlayEb)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(9999));
                        p.PlayOrderModifiers.AddOrUpdate(Eggbearer, new Modifier(-9999));
                        AddLog(CardName(Eggbearer) + " 9999 | 顺序-9999（手里已有蛋且1费需跳币：禁用跳币出布蛋者，保留硬币）");
                        return;
                    }
                }
                catch { }

                int handCount = 0;
                int myMinions = 0;
                int eggCost = 3;
                int eggbearerCost = 2;

                try { handCount = board.Hand != null ? board.Hand.Count : 0; } catch { handCount = 0; }
                try { myMinions = board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { myMinions = 0; }

                try
                {
                    var eggCard = board.Hand != null
                        ? board.Hand.Where(x => x != null && x.Template != null && IsEggStage(x.Template.Id))
                            .OrderByDescending(x => GetEggStageRank(x.Template.Id))
                            .FirstOrDefault()
                        : null;
                    if (eggCard != null) eggCost = eggCard.CurrentCost;
                }
                catch { eggCost = 3; }

                try
                {
                    var ebCard = board.Hand != null
                        ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Eggbearer)
                        : null;
                    if (ebCard != null) eggbearerCost = ebCard.CurrentCost;
                }
                catch { eggbearerCost = 2; }

                bool eggNotPlayableNow = board.ManaAvailable < eggCost;
                bool handClogged = handCount >= 8;
                bool needBoard = myMinions == 0;

                if (board.ManaAvailable >= eggbearerCost && (eggNotPlayableNow || handClogged || needBoard))
                {
                    p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(-240));
                    p.PlayOrderModifiers.AddOrUpdate(Eggbearer, new Modifier(1100));
                    AddLog(CardName(Eggbearer) + " -240 | 顺序1100（有蛋仍可出：本回合难以下蛋/或手牌偏满/或需要站场）");
                }
                else
                {
                    // 用户口径：若手里已有蛋，则布蛋者常态降权到“找安布拉/找破蛋”之后（除非上面三个条件触发）。
                    p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(150));
                    p.PlayOrderModifiers.AddOrUpdate(Eggbearer, new Modifier(200));
                    AddLog(CardName(Eggbearer) + " 150 | 顺序200（手里已有蛋：布蛋者降权，优先找安布拉/破蛋组件）");
                }
            }
        }

        private void ProcessCard_WW_092_Fracking(ProfileParameters p, Board board,
            bool wantFinleySwap,
            bool isAggroLikely,
            bool hasEggInHand,
            bool hasEggOnBoard,
            bool canPopEggThisTurnIfPlayed,
            bool hasEggBreakerForExistingEgg,
            ref bool forceDisableLifeTap,
            ref string forceDisableLifeTapReason)
        {
            if (!board.HasCardInHand(Fracking)) return;

            // 备注：液力压裂(WW_092)可以直接打出（不需要目标）。
            // 因此这里的“可施放判定”仅以费用是否足够为准。
            Card frackingCardForCost = null;
            int frackingCostNow = 1;
            try
            {
                frackingCardForCost = board.Hand != null
                    ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Fracking)
                    : null;
                if (frackingCardForCost != null) frackingCostNow = frackingCardForCost.CurrentCost;
            }
            catch { frackingCostNow = 1; frackingCardForCost = null; }

            bool canPlayFrackingNow = false;
            try { canPlayFrackingNow = board.ManaAvailable >= frackingCostNow; } catch { canPlayFrackingNow = false; }

            // 液力压裂：检视牌库底3抽1并“摧毁其余牌”，属于强检索但有烧牌成本。
            // 目标：在慢局/找启动或找破蛋手段时积极用；在快攻/手满/牌库见底时保守。
            if (wantFinleySwap)
            {
                // 芬利回合：先换手再说（别提前把底牌烧掉）
                p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(450));
                p.PlayOrderModifiers.AddOrUpdate(Fracking, new Modifier(-200));
                return;
            }

            int handCount = 0;
            int deckCount = 0;
            try { handCount = board.Hand != null ? board.Hand.Count : 0; } catch { handCount = 0; }
            try { deckCount = board.FriendDeckCount; } catch { deckCount = 0; }

            if (isAggroLikely)
            {
                // 快攻对局优先站场/解场，避免“1费烧2牌”丢节奏
                p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(180));
                return;
            }

            if (handCount >= 10)
            {
                // 防爆牌：压裂抽1，10手会直接爆（且后续还有分流/吃小鬼等补牌）
                p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(250));
                return;
            }

            // 牌库见底时不乱烧牌：默认保守。
                // 例外：场上有蛋但当前没有任何破蛋手段时，压裂是“找破蛋/找安布拉”的关键牌，不能因为牌库少就直接回去点分流。
                if (deckCount > 0 && deckCount <= 8)
                {
                    bool emergencyNeedFindPop = hasEggOnBoard && !hasEggBreakerForExistingEgg;
                    if (!emergencyNeedFindPop)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(350));
                        return;
                    }

                    AddLog("[压裂例外] 牌库见底(" + deckCount + ")但场上有蛋且无破蛋手段：仍允许压裂找破蛋/安布拉");
                }

            bool wantFindStarterOrPop = (!hasEggInHand && !hasEggOnBoard)
                || (hasEggInHand && !canPopEggThisTurnIfPlayed)
                || (hasEggOnBoard && !hasEggBreakerForExistingEgg);

            if (wantFindStarterOrPop)
            {
                // 强推：找启动/找破蛋手段时，压裂优先级要足够高（但仅在可实际施放时才强推）。
                p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(canPlayFrackingNow ? -650 : 200));
                p.PlayOrderModifiers.AddOrUpdate(Fracking, new Modifier(canPlayFrackingNow ? 2600 : 0));
                AddLog(CardName(Fracking) + (canPlayFrackingNow
                    ? " -650 | 顺序2600（慢局：找启动/找破蛋手段时积极压裂）"
                    : " 200（慢局：需要压裂但当前不可施放(费用不足)）"));

                // 用户反馈：仍会出现“有压裂却不打/先点分流”的情况。
                // 这里做一个硬护栏：当【需要找启动或找破蛋手段】时，用 combo 强推先压裂，并硬禁分流。
                if (canPlayFrackingNow && handCount <= 9)
                {
                    try
                    {
                        var frackingCard = frackingCardForCost;
                        if (frackingCard != null)
                        {
                            // 用户反馈：存在 ComboModifier 时会导致压裂强推失效，最终跑去点分流。
                            // 修正：在非芬利换手、且不处于“本回合下蛋即破”这类更高优先级连锁时，允许覆盖 Combo。
                            bool allowOverrideCombo = !wantFinleySwap && !canPopEggThisTurnIfPlayed;
                            if (p.ComboModifier == null || allowOverrideCombo)
                            {
                                p.ComboModifier = new ComboSet(frackingCard.Id, frackingCard.Id);
                                AddLog("[COMBO] 强制压裂（需要找启动/找破蛋手段：避免先做别的" + (allowOverrideCombo ? "，允许覆盖已有Combo" : "") + "）");
                            }
                            else
                                AddLog("[COMBO] 压裂想强推但已存在ComboModifier：不覆盖（避免打断更高优先连锁）");
                        }
                    }
                    catch { }

                    // 仅在【无蛋】场景压住狗头人，避免“先抽一口再压裂/甚至不压裂”
                    if (!hasEggInHand && !hasEggOnBoard)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Librarian, new Modifier(350));
                        p.PlayOrderModifiers.AddOrUpdate(Librarian, new Modifier(-999));
                    }

                    // 只有在“压裂当前可打”时才禁用分流（避免费用不足导致空过）。
                    forceDisableLifeTap = true;
                    if (string.IsNullOrEmpty(forceDisableLifeTapReason))
                        forceDisableLifeTapReason = "手里有压裂且需要找启动/破蛋：优先压裂";
                }
            }
            else
            {
                p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(-180));
                p.PlayOrderModifiers.AddOrUpdate(Fracking, new Modifier(600));
                AddLog(CardName(Fracking) + " -180 | 顺序600（慢局：空余法力可用压裂找关键牌）");
            }
        }

        private void ProcessCard_DINO_410_Egg(ProfileParameters p, Board board,
            Card eggInHand, bool hasEggOnBoard,
            bool canPopEggThisTurnIfPlayed, bool hasUmbraInHand,
            bool isAggroLikely, bool isComboLikely,
            ref bool forceDisableLifeTap, ref string forceDisableLifeTapReason)
        {
            // 关键原则：尽量避免裸下蛋（防沉默/变形）
            if (eggInHand == null || eggInHand.Template == null) return;
            if (hasEggOnBoard) return;

            var eggId = eggInHand.Template.Id;
            bool hasEggInHand = IsEggStage(eggId);
            if (!hasEggInHand) return;

            int eggCost = eggInHand.CurrentCost;
            int umbraCostNow = 4;
            try
            {
                var umbraCardForCost = board.Hand != null
                    ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Umbra)
                    : null;
                if (umbraCardForCost != null) umbraCostNow = umbraCardForCost.CurrentCost;
            }
            catch { umbraCostNow = 4; }

            bool hasUmbraLine = hasUmbraInHand && board.ManaAvailable >= eggCost + umbraCostNow;

            // 用户口径：当“手里有安布拉+蛋且费用足够”时，优先安布拉->下蛋；
            // 即使也存在“下蛋即破”线路，也不应让蛋抢在安布拉前面。
            if (hasUmbraLine)
            {
                // 下蛋保持高优先，但必须排在安布拉之后
                p.CastMinionsModifiers.AddOrUpdate(eggId, new Modifier(-700));
                p.PlayOrderModifiers.AddOrUpdate(eggId, new Modifier(3050));
                AddLog(CardName(eggId) + " -700 | 顺序3050（安布拉同回合可连动：优先安布拉->下蛋，蛋后置）");

                try
                {
                    var umbraCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Umbra);
                    if (umbraCard != null)
                    {
                        if (p.ComboModifier != null)
                            AddLog("[COMBO] 覆盖已有ComboModifier：安布拉->下蛋为更高优先线");
                        p.ComboModifier = new ComboSet(umbraCard.Id, eggInHand.Id);
                        AddLog("[COMBO] 安布拉->下蛋：" + CardName(Umbra) + " => " + CardName(eggId));
                    }
                }
                catch { }

                forceDisableLifeTap = true;
                forceDisableLifeTapReason = "手里有蛋+安布拉且可同回合：优先安布拉->下蛋";
                AddLog("手里有蛋+安布拉：优先安布拉->下蛋连锁（压住其它动作并禁用分流）");

                p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(350));
                p.CastMinionsModifiers.AddOrUpdate(Librarian, new Modifier(350));
                p.CastMinionsModifiers.AddOrUpdate(DirtyRat, new Modifier(350));
                p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(450));
                p.CastSpellsModifiers.AddOrUpdate(MorriganRealm, new Modifier(450));
                p.CastSpellsModifiers.AddOrUpdate(Defile, new Modifier(450));
                return;
            }

            if (canPopEggThisTurnIfPlayed)
            {
                // 找到蛋且能“下蛋即破蛋”：本回合硬优先先下蛋
                // 关键：手里有蛋且可同回合破时，“下蛋”必须更硬，避免出现打脸后直接结束回合不下蛋的情况。
                p.CastMinionsModifiers.AddOrUpdate(eggId, new Modifier(-1200));
                p.PlayOrderModifiers.AddOrUpdate(eggId, new Modifier(9999));
                AddLog(CardName(eggId) + " -1200 | 顺序9999（可同回合破蛋/或安布拉线：强制极前置）");

                // 用 ComboSet 硬指定关键出牌顺序（用户口径：强推逻辑写成 combo）
                // - 可同回合破蛋：下蛋 -> 破蛋手段
                // - 安布拉线：安布拉 -> 下蛋（安布拉触发亡语）
                try
                {
                    if (canPopEggThisTurnIfPlayed)
                    {
                        Card popCard = null;
                        int mana = board.ManaAvailable;
                        int friendMinionsNow = 0;
                        try { friendMinionsNow = board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { friendMinionsNow = 0; }

                        int handCountNow = 0;
                        try { handCountNow = board.Hand != null ? board.Hand.Count : 0; } catch { handCountNow = 0; }

                        bool hasUmbraOnBoardNow = false;
                        try { hasUmbraOnBoardNow = board.HasCardOnBoard(Umbra); } catch { hasUmbraOnBoardNow = false; }
                        bool umbraStillInDeck = IsUmbraStillInDeck(board, hasUmbraInHand, hasUmbraOnBoardNow);

                        var ritualCard = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == Ritual)
                            .OrderBy(x => x.CurrentCost).FirstOrDefault();
                        var pactCard = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == DarkPact)
                            .OrderBy(x => x.CurrentCost).FirstOrDefault();
                        var eatImpCard = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == EatImp)
                            .OrderBy(x => x.CurrentCost).FirstOrDefault();
                        var grimoireCard = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == Grimoire)
                            .OrderBy(x => x.CurrentCost).FirstOrDefault();
                        var summonerCard = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == SacrificialSummoner)
                            .OrderBy(x => x.CurrentCost).FirstOrDefault();
                        var burnCard = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == Burn)
                            .OrderBy(x => x.CurrentCost).FirstOrDefault();
                        var defileCard = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == Defile)
                            .OrderBy(x => x.CurrentCost).FirstOrDefault();
                        // 亵渎：按“连续递增链”判断，不再要求固定1-7
                        bool canDefileChainIfPlayEgg = defileCard != null && HasDefileIncreasingChainAfterAddingEgg(board, 3);

                        bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Count > 0;
                        var consumeCard = enemyHasMinion
                            ? board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == ChaosConsume)
                                .OrderBy(x => x.CurrentCost).FirstOrDefault()
                            : null;

                        // 选择一个“能在当前法力下完成”的破蛋手段，优先级按常用顺序。
                        // 用户口径：献祭召唤者在触发蛋的各种手段中最优先（要求：当前随从位<=5，保证下蛋后还能下召唤者）
                        // 且：牌库里必须还有安布拉（不在手/不在场/不在坟场）
                        // 边界：若手里已有安布拉，则“导出安布拉”的价值下降，召唤者优先级降低。
                        bool shouldDeprioritizeSummonerBecauseUmbraInHand = hasUmbraInHand;

                        // 边界：吃小鬼会抽3，避免高扣手时作为“下蛋即破”首选导致爆牌。
                        bool eatImpWouldOverdraw = eatImpCard != null && WouldOverdrawAfterSequence(handCountNow, 2, 3); // 下蛋+吃鬼

                        if (!shouldDeprioritizeSummonerBecauseUmbraInHand && summonerCard != null && umbraStillInDeck && friendMinionsNow <= 5 && mana >= eggCost + summonerCard.CurrentCost) popCard = summonerCard;
                        else if (ritualCard != null && mana >= eggCost + ritualCard.CurrentCost) popCard = ritualCard;
                        else if (pactCard != null && mana >= eggCost + pactCard.CurrentCost) popCard = pactCard;
                        else if (!eatImpWouldOverdraw && eatImpCard != null && mana >= eggCost + eatImpCard.CurrentCost) popCard = eatImpCard;
                        else if (grimoireCard != null && mana >= eggCost + grimoireCard.CurrentCost) popCard = grimoireCard;
                        else if (burnCard != null && mana >= eggCost + burnCard.CurrentCost) popCard = burnCard;
                        else if (summonerCard != null
                            && mana >= eggCost + summonerCard.CurrentCost
                            && (!umbraStillInDeck || hasUmbraOnBoardNow || friendMinionsNow <= 5))
                            popCard = summonerCard;
                        else if (consumeCard != null && mana >= eggCost + consumeCard.CurrentCost) popCard = consumeCard;
                        else if (canDefileChainIfPlayEgg && mana >= eggCost + defileCard.CurrentCost) popCard = defileCard;

                        if (popCard != null)
                        {
                            p.ComboModifier = new ComboSet(eggInHand.Id, popCard.Id);
                            AddLog("[COMBO] 下蛋->破蛋：" + CardName(eggId) + " => " + (string.IsNullOrWhiteSpace(popCard.Template.NameCN) ? popCard.Template.Name : popCard.Template.NameCN));
                        }
                        else
                        {
                            // 兜底：至少强制先下蛋
                            p.ComboModifier = new ComboSet(eggInHand.Id, eggInHand.Id);
                            AddLog("[COMBO] 强制下蛋（未找到可用破蛋手段，兜底）");
                        }
                    }
                }
                catch { }

                if (canPopEggThisTurnIfPlayed)
                {
                    forceDisableLifeTap = true;
                    forceDisableLifeTapReason = "手里有蛋且可同回合破：优先下蛋进入破蛋逻辑";

                    // 复盘定位：在“应该下蛋但没下”的问题点，打印手牌（压缩一行）
                    try
                    {
                        AddLog("[关键] " + forceDisableLifeTapReason + "：打印手牌快照(一行)");
                        LogHandSnapshotOneLine(board);
                    }
                    catch { }

                    // 压住“先抽/先检索/先站场/先随手解”的倾向
                    p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(350));
                    p.CastMinionsModifiers.AddOrUpdate(Librarian, new Modifier(350));
                    p.CastMinionsModifiers.AddOrUpdate(DirtyRat, new Modifier(350));

                    p.CastSpellsModifiers.AddOrUpdate(Tomb, new Modifier(250));
                    p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(450));
                    p.CastSpellsModifiers.AddOrUpdate(MorriganRealm, new Modifier(450));
                    p.CastSpellsModifiers.AddOrUpdate(Defile, new Modifier(450));

                    AddLog(forceDisableLifeTapReason + "（压住抽牌/站场/解场，避免先做别的）");
                }
            }
            else
            {
                bool eggPlayableNow = board.ManaAvailable >= eggCost;
                int eggInGraveLocal = 0;
                try { eggInGraveLocal = board.FriendGraveyard != null ? board.FriendGraveyard.Count(x => IsEggStage(x)) : 0; } catch { eggInGraveLocal = 0; }
  									int enemyMinions = 0;
                    int friendMinions = 0;
                    try { enemyMinions = board.MinionEnemy != null ? board.MinionEnemy.Count : 0; } catch { enemyMinions = 0; }
                    try { friendMinions = board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { friendMinions = 0; }

                // 用户建议：手中有蛋“必出”增加两个硬开关。
                // A) 对面高概率沉默/变形窗口，且本回合无法破至少1层：暂停裸下蛋。
                // B) 若对面可能手牌破坏，且我方握有安布拉与破蛋工具：允许仍然落蛋做保底。
                bool enemyLikelyDisrupt = IsEnemyLikelySilenceOrTransform(board);
                bool enemyLikelyHandDisrupt = IsEnemyLikelyHandDisruption(board);

                // EggProgressLevel：用“已破层数”近似蛋进度（t2/t3/t4/t5 风险收益不同）
                int eggProgressLevel = eggInGraveLocal;

                bool hasLateStageEggInHand = false;
                try { hasLateStageEggInHand = GetEggStageRank(eggInHand.Template.Id) >= 4; } catch { hasLateStageEggInHand = false; }

                bool isLateEgg = hasLateStageEggInHand || eggProgressLevel >= 3;

                // 关键：只在“下蛋后本回合也无法破至少 1 层”时，才暂停裸下蛋（避免“能下蛋即破却怂着不下”）
                string popDetail = "";
                EggPopQuality popQ = EvaluatePopQualityIfPlayEggThisTurn(board, eggCost, enemyMinions, friendMinions, out popDetail);

                bool pauseEggBecauseDisrupt =
                    !isLateEgg
                    && eggProgressLevel <= 0
                    && enemyLikelyDisrupt
                    && !isAggroLikely
                    && popQ == EggPopQuality.None;
                bool allowEggBecauseHandDisruptException = enemyLikelyHandDisrupt && hasUmbraInHand && HasAnyEggPopToolInHand(board);

                // 明确规则：手里有蛋且本回合可下时，默认强推下蛋；
                // 例外：健康血线且暂无立即破蛋手段时，允许先分流找组件。
                if (eggPlayableNow)
                {
                    // 复盘关键：每回合输出脚本对“蛋线”的总体计划（避免复盘时反推一堆变量）
                    AddLog("[EggPlan] CanEggThisTurn=true PopQuality=" + popQ + (string.IsNullOrEmpty(popDetail) ? "" : (" (" + popDetail + ")"))
                        + " PauseNakedEgg=" + (pauseEggBecauseDisrupt ? "true" : "false")
                        + " DisruptWindow=" + (enemyLikelyDisrupt ? "Y" : "N")
                        + " HandDisrupt=" + (enemyLikelyHandDisrupt ? "Y" : "N")
                        + " EggProgress=" + eggProgressLevel
                        + " LateEgg=" + (isLateEgg ? "Y" : "N"));
                    if (pauseEggBecauseDisrupt && !allowEggBecauseHandDisruptException)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(eggId, new Modifier(9999));
                        p.PlayOrderModifiers.AddOrUpdate(eggId, new Modifier(-9999));
                        AddLog(CardName(eggId) + " 9999（暂停出蛋：对面高概率沉默/变形窗口且本回合无法破层(PopQuality=None)；优先找破蛋/安布拉/复活链）");
                        return;
                    }

                    if (pauseEggBecauseDisrupt && allowEggBecauseHandDisruptException)
                        AddLog("[例外] 对面可能手牌破坏且我方握有安布拉+破蛋工具：仍选择落蛋做保底");

                    // 分流护栏：默认“先下蛋再说”。
                    // 但若血线健康且本回合暂无破蛋手段，则允许先分流找组件（不再硬禁）。
                    bool healthyToTap = false;
                    int hpArmorNow = 0;
                    try
                    {
                        hpArmorNow = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                        healthyToTap = hpArmorNow >= 18 && !isAggroLikely;
                    }
                    catch
                    {
                        hpArmorNow = 0;
                        healthyToTap = false;
                    }

                    int enemyAtkNow = 0;
                    int enemyMinionsNow = 0;
                    try
                    {
                        enemyAtkNow = GetEnemyBoardAttack(board);
                        enemyMinionsNow = board.MinionEnemy != null ? board.MinionEnemy.Count : 0;
                    }
                    catch
                    {
                        enemyAtkNow = 0;
                        enemyMinionsNow = 0;
                    }

                    bool enemyFastClassHint = false;
                    try
                    {
                        enemyFastClassHint =
                            board.EnemyClass == Card.CClass.HUNTER
                            || board.EnemyClass == Card.CClass.DEMONHUNTER
                            || board.EnemyClass == Card.CClass.ROGUE
                            || board.EnemyClass == Card.CClass.PALADIN
                            || board.EnemyClass == Card.CClass.SHAMAN
                            || board.EnemyClass == Card.CClass.WARLOCK;
                    }
                    catch { enemyFastClassHint = false; }

                    // 分流只在“极安全窗口”放开：
                    // - 1/2费前期几乎无压力，或者
                    // - 3费慢速对局且对面空场、我方高血。
                    bool safeGreedyTapWindow =
                        ((board != null && board.MaxMana <= 2) && enemyMinionsNow <= 1 && enemyAtkNow <= 2)
                        || ((board != null && board.MaxMana == 3) && !enemyFastClassHint && enemyMinionsNow == 0 && enemyAtkNow == 0 && hpArmorNow >= 26);

                    // 用户规则：对快攻，裸下蛋不必限定“最大法力=3”。
                    // 只要“当前可用法力（含硬币）>=3”且暂无可破层手段，就优先下蛋而不是先分流。
                    int effectiveManaForEgg = 0;
                    bool hasCoinForEgg = false;
                    try
                    {
                        int manaNow = board != null ? board.ManaAvailable : 0;
                        hasCoinForEgg = board != null && board.HasCardInHand(TheCoin);
                        effectiveManaForEgg = manaNow + (hasCoinForEgg ? 1 : 0);
                    }
                    catch
                    {
                        effectiveManaForEgg = 0;
                        hasCoinForEgg = false;
                    }

                    bool forceNakedEggVsFastHint =
                        enemyFastClassHint
                        && popQ == EggPopQuality.None
                        && effectiveManaForEgg >= 3;

                    bool allowTapBeforeEgg = healthyToTap
                        && popQ == EggPopQuality.None
                        && (board != null && board.ManaAvailable >= 2)
                        && safeGreedyTapWindow
                        && !forceNakedEggVsFastHint;
                    if (!allowTapBeforeEgg)
                    {
                        if (forceNakedEggVsFastHint)
                            AddLog("[蛋节奏] 对手疑似快攻/抢节奏：可用法力(含硬币)=" + effectiveManaForEgg + ">=3，即使暂时不能破蛋也优先下蛋");

                        p.CastMinionsModifiers.AddOrUpdate(eggId, new Modifier(-650));
                        p.PlayOrderModifiers.AddOrUpdate(eggId, new Modifier(9800));
                        AddLog(CardName(eggId) + " -650 | 顺序9800（最高优先级：手中有蛋且本回合可下 => 必出蛋）");

                        // 关键修正：这里“必出蛋”是最高优先级，因此允许覆盖已有 ComboModifier。
                        if (p.ComboModifier != null)
                            AddLog("[COMBO] 手中有蛋必出：覆盖已有ComboModifier");
                        p.ComboModifier = new ComboSet(eggInHand.Id, eggInHand.Id);
                        AddLog("[COMBO] 强制下蛋（最高优先级：手中有蛋必出）");

                        // 关键修正：避免海星等杂随从抢在“必出蛋”之前裸拍。
                        p.CastMinionsModifiers.AddOrUpdate(Starfish, new Modifier(9999));
                        p.PlayOrderModifiers.AddOrUpdate(Starfish, new Modifier(-9999));
                        AddLog(CardName(Starfish) + " 9999（手中有蛋必出：禁用海星抢节奏，避免不下蛋）");

                        forceDisableLifeTap = true;
                        forceDisableLifeTapReason = "手里可下蛋：优先下蛋而非分流";
                    }
                    else
                    {
                        // 放宽“裸下蛋”强制：健康血线且无立即破蛋时，允许先分流找组件。
                        p.CastMinionsModifiers.AddOrUpdate(eggId, new Modifier(-180));
                        p.PlayOrderModifiers.AddOrUpdate(eggId, new Modifier(2200));
                        AddLog(CardName(eggId) + " -180 | 顺序2200（血线健康且暂无破蛋手段：允许先分流找组件，再择机下蛋）");
                        AddLog("手里可下蛋且血线健康：允许分流一口（不强制当回合裸下蛋）");
                    }

                    // 同时稍微压住其他低价值动作，保证蛋能被执行
                    p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(150));
                    p.CastSpellsModifiers.AddOrUpdate(MorriganRealm, new Modifier(120));
                    AddLog("手里有蛋：优先下蛋进入后续破蛋/安布拉逻辑" + (allowTapBeforeEgg ? "（分流放开）" : "（并禁用分流）"));
                }
                else
                {
                    // 没有启动手段时明显降权，避免把蛋丢在场上过夜
                    p.CastMinionsModifiers.AddOrUpdate(eggId, new Modifier(450));
                    p.PlayOrderModifiers.AddOrUpdate(eggId, new Modifier(-300));
                    AddLog(CardName(eggId) + " 450 | 顺序-300（避免裸下蛋：本回合难以立刻破蛋）");
                }
            }
        }

        private void ProcessEggPopToolsWhenEggOnBoard(ProfileParameters p, Board board,
            bool hasEggOnBoard, bool hasEggT5OnBoard,
            bool hasUmbraOnBoard, bool hasUmbraInHand,
            bool hasRitualInHand, bool hasDarkPactInHand, bool hasEatImpInHand, bool hasCruelGatheringInHand, bool hasGrimoireInHand,
            bool hasChaosConsumeInHand, int eggInGrave,
            ref bool forceDisableLifeTap, ref string forceDisableLifeTapReason)
        {
            if (!hasEggOnBoard) return;

            Card.Cards bestEggOnBoardId = GetBestEggOnBoardId(board);

            bool umbraHandledEgg = false; // 软return：安布拉分支处理后仍允许后续模块继续工作

            int enemyMinions = board.MinionEnemy != null ? board.MinionEnemy.Count : 0;
            int friendMinions = board.MinionFriend != null ? board.MinionFriend.Count : 0;
            int manaAvailable = 0;
            try { manaAvailable = board.ManaAvailable; } catch { manaAvailable = 0; }
            int enemyMaxAtk = (board.MinionEnemy != null && board.MinionEnemy.Count > 0)
                ? board.MinionEnemy.Max(x => x != null ? x.CurrentAtk : 0)
                : 0;

            bool enemyHasMinionForConsume = enemyMinions > 0;
            bool hasSummonerInHand = board.HasCardInHand(SacrificialSummoner);
            Func<Card.Cards, bool> canCastNow = id =>
            {
                try
                {
                    if (board == null || board.Hand == null) return false;
                    var c = board.Hand
                        .Where(x => x != null && x.Template != null && x.Template.Id == id)
                        .OrderBy(x => x.CurrentCost)
                        .FirstOrDefault();
                    return c != null && c.CurrentCost <= manaAvailable;
                }
                catch
                {
                    return false;
                }
            };
            bool canPlaySummonerNow = hasSummonerInHand && friendMinions <= 6 && canCastNow(SacrificialSummoner);

            bool umbraStillInDeck = IsUmbraStillInDeck(board, hasUmbraInHand, hasUmbraOnBoard);

            bool hasBurnInHand = board.HasCardInHand(Burn);
            bool hasDefileInHand = board.HasCardInHand(Defile);
            bool canDefileChainNow = hasDefileInHand && HasDefileIncreasingChain(board, 3);

            bool umbraSilenced = IsUmbraSilencedOnBoard(board);

            // 口径（用户实战反馈）：当场上有安布拉+蛋时，应该优先把“其他杂随从（含献祭召唤者）”送掉，
            // 再去破蛋展开，避免杂随从占格子/干扰后续节奏。
            bool hasOtherNonCoreMinion = false;
            try
            {
                hasOtherNonCoreMinion = board.MinionFriend != null && board.MinionFriend.Any(m =>
                    m != null && m.Template != null
                    && (m.Template.Id != Umbra || umbraSilenced)
                    && m.Template.Id != Khelos
                    && !IsEggStage(m.Template.Id));
            }
            catch { hasOtherNonCoreMinion = false; }

            // 口径：当场上有安布拉+蛋，且我方随从位已经很拥挤时，
            // 先用随从交换“送掉杂随从”（除安布拉/凯洛斯），避免立刻破蛋导致召唤被卡格子。
            // 实现：拥挤时延后破蛋法术/避免继续下随从，给攻击阶段留空间。
            bool boardCrowdedForUmbraEgg = hasUmbraOnBoard && friendMinions >= 6;
            if (boardCrowdedForUmbraEgg)
            {
                p.PlayOrderModifiers.AddOrUpdate(EatImp, new Modifier(-800));
                p.PlayOrderModifiers.AddOrUpdate(Ritual, new Modifier(-800));
                p.PlayOrderModifiers.AddOrUpdate(Grimoire, new Modifier(-800));
                p.PlayOrderModifiers.AddOrUpdate(DarkPact, new Modifier(-800));
                p.PlayOrderModifiers.AddOrUpdate(ChaosConsume, new Modifier(-800));
                p.PlayOrderModifiers.AddOrUpdate(Burn, new Modifier(-800));
                p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(-800));
                p.PlayOrderModifiers.AddOrUpdate(Defile, new Modifier(-800));

                // 同时压住“继续下杂随从”把格子塞满
                p.CastMinionsModifiers.AddOrUpdate(Librarian, new Modifier(450));
                p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(450));
                p.CastMinionsModifiers.AddOrUpdate(Finley, new Modifier(450));
                p.CastMinionsModifiers.AddOrUpdate(DirtyRat, new Modifier(450));
                p.CastMinionsModifiers.AddOrUpdate(Starfish, new Modifier(450));

                AddLog("安布拉+蛋且随从位拥挤(>=6)：先交换送掉杂随从（除安布拉/凯洛斯），延后破蛋法术");
            }

            bool hasEggBreaker = (hasRitualInHand && canCastNow(Ritual))
                || (hasDarkPactInHand && canCastNow(DarkPact))
                || (hasEatImpInHand && canCastNow(EatImp))
                || (hasGrimoireInHand && canCastNow(Grimoire))
                || canPlaySummonerNow
                || (hasChaosConsumeInHand && enemyHasMinionForConsume && canCastNow(ChaosConsume))
                || (hasBurnInHand && canCastNow(Burn))
                || (hasDefileInHand && canDefileChainNow && canCastNow(Defile))
                || (hasCruelGatheringInHand && canCastNow(CruelGathering));

            // A) 安布拉+蛋：不择手段触发蛋（尤其优先触发t5出20/20）
            if (hasUmbraOnBoard)
            {
                forceDisableLifeTap = true;
                forceDisableLifeTapReason = "场上有安布拉+蛋：不择手段触发蛋";

                if (umbraSilenced)
                {
                    AddLog("安布拉被沉默：允许作为牺牲目标");
                }

                // 若场上还有杂随从，则把所有破蛋手段延后一些，优先让攻击/交换“送掉杂随从”更容易发生。
                // 注：不会改变“最终要破蛋”的倾向，只是尽量别在满手段时抢着先法术破蛋。
                if (hasOtherNonCoreMinion)
                {
                    p.PlayOrderModifiers.AddOrUpdate(EatImp, new Modifier(-600));
                    p.PlayOrderModifiers.AddOrUpdate(Ritual, new Modifier(-600));
                    p.PlayOrderModifiers.AddOrUpdate(Grimoire, new Modifier(-600));
                    p.PlayOrderModifiers.AddOrUpdate(DarkPact, new Modifier(-600));
                    p.PlayOrderModifiers.AddOrUpdate(ChaosConsume, new Modifier(-600));
                    p.PlayOrderModifiers.AddOrUpdate(Burn, new Modifier(-600));
                    p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(-600));
                    p.PlayOrderModifiers.AddOrUpdate(Defile, new Modifier(-600));
                    AddLog("安布拉+蛋：场上有其他杂随从（含献祭召唤者），优先交换送杂；牺牲法术仍锁蛋（仅延后出手顺序）");
                }

                // 用户口径：在用法术破蛋前，杂随从“能交换送最好；送不掉就算”。
                // 关键修正：牺牲类法术（黑暗契约/末日仪式/吃小鬼/魔典/残酷集结）仍应优先打蛋，
                // 不应把献祭召唤者等杂随从当作牺牲目标。
                if (!umbraSilenced)
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Umbra, new Modifier(9000));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Khelos, new Modifier(9000));

                // 拥挤时：同样不把杂随从当牺牲法术目标；尽量通过交换送杂腾格子，法术继续优先锁蛋。
                if (boardCrowdedForUmbraEgg)
                {
                    // 用户口径修正：能送最好（靠交换），送不掉就算；不要强行用法术把杂随从杀掉。
                    // 因此：在“牺牲类法术”选目标时仍应优先打在蛋上，杂随从设为正值保护。
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Egg, new Modifier(-1600));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT2, new Modifier(-1600));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT3, new Modifier(-1600));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT4, new Modifier(-1600));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT5, new Modifier(-1800));

                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Eggbearer, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Librarian, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Finley, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(DirtyRat, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Starfish, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(1200));
                    AddLog("安布拉线(拥挤)：能交换送杂最好；不强行用法术送杂（法术仍优先破蛋）");
                }
                else
                {
                    // 非拥挤时同样不强行“法术送杂”，避免黑暗契约误吃献祭召唤者。
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Eggbearer, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Librarian, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Finley, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(DirtyRat, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Starfish, new Modifier(900));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(1200));
                    AddLog("安布拉线：能交换送杂最好；送不掉就算（牺牲法术仍优先破蛋）");
                }

                if (!boardCrowdedForUmbraEgg && friendMinions <= 6 && hasEggT5OnBoard)
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT5, new Modifier(-2000));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Egg, new Modifier(350));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT2, new Modifier(350));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT3, new Modifier(350));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT4, new Modifier(350));
                    AddLog("安布拉在场且随从位<=6：优先触发DINO_410t5（出20/20）");
                }
                else if (!boardCrowdedForUmbraEgg)
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Egg, new Modifier(-1400));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT2, new Modifier(-1400));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT3, new Modifier(-1400));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT4, new Modifier(-1400));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT5, new Modifier(-1600));
                }

                if (hasSummonerInHand)
                {
                    bool hasNonSummonerBreaker = hasRitualInHand || hasDarkPactInHand || hasEatImpInHand || hasGrimoireInHand
                        || (hasChaosConsumeInHand && enemyHasMinionForConsume)
                        || hasBurnInHand || canDefileChainNow;

                    if (hasNonSummonerBreaker)
                    {
                        // 已有法术/技能类破蛋手段时，压住献祭召唤者：它更像“杂随从”，容易占格子。
                        p.CastMinionsModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(450));
                        p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(-300));
                        AddLog(CardName(SacrificialSummoner) + " 450 | 顺序-300（安布拉+蛋：已有法术破蛋手段，压住献祭召唤者避免占格子）");
                    }
                    else
                    {
                        p.CastMinionsModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(-950));
                        p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(3200));
                    }
                }
                if (hasEatImpInHand)
                {
                    p.CastSpellsModifiers.AddOrUpdate(EatImp, new Modifier(-900, bestEggOnBoardId));
                    p.PlayOrderModifiers.AddOrUpdate(EatImp, new Modifier(3150));
                }
                if (hasRitualInHand)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Ritual, new Modifier(-900, bestEggOnBoardId));
                    p.PlayOrderModifiers.AddOrUpdate(Ritual, new Modifier(3100));
                }
                if (hasGrimoireInHand)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Grimoire, new Modifier(-850, bestEggOnBoardId));
                    p.PlayOrderModifiers.AddOrUpdate(Grimoire, new Modifier(3050));
                }
                if (hasCruelGatheringInHand)
                {
                    // 残酷集结：破蛋组件，场上有蛋时强制锁蛋
                    p.CastSpellsModifiers.AddOrUpdate(CruelGathering, new Modifier(-820, bestEggOnBoardId));
                    p.PlayOrderModifiers.AddOrUpdate(CruelGathering, new Modifier(3025));
                }
                if (hasDarkPactInHand)
                {
                    // 关键修正：黑暗契约必须优先点“蛋”，避免误点献祭召唤者等杂随从导致错失破蛋收益。
                    int myHpNow = 0;
                    int enemyAtkNow = 0;
                    try
                    {
                        myHpNow = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                        enemyAtkNow = GetEnemyBoardAttack(board);
                    }
                    catch { myHpNow = 0; enemyAtkNow = 0; }

                    bool urgentHealNow = enemyAtkNow >= Math.Max(5, myHpNow - 8);
                    int pactMod = urgentHealNow ? -980 : (myHpNow <= 22 ? -780 : -650);
                    int pactOrder = urgentHealNow ? 3340 : (myHpNow <= 22 ? 3200 : 3000);

                    p.CastSpellsModifiers.AddOrUpdate(DarkPact, new Modifier(pactMod, bestEggOnBoardId));
                    p.PlayOrderModifiers.AddOrUpdate(DarkPact, new Modifier(pactOrder));
                    if (urgentHealNow)
                        AddLog(CardName(DarkPact) + " -980 | 顺序3340（安布拉线：低血急救，黑暗契约优先破蛋回血）");
                    else if (myHpNow <= 22)
                        AddLog(CardName(DarkPact) + " -780 | 顺序3200（安布拉线：低血提高黑暗契约优先级）");
                }
                if (hasChaosConsumeInHand)
                {
                    int consumeMod = enemyHasMinionForConsume ? -650 : 9999;
                    p.CastSpellsModifiers.AddOrUpdate(ChaosConsume, new Modifier(consumeMod));
                    p.PlayOrderModifiers.AddOrUpdate(ChaosConsume, new Modifier(2950));
                }

                // 安布拉在场：不要硬return，避免后续（复活/补破/再下蛋等）被“早期口径”间接掐死。
                // 仍保持安布拉分支的强权重；这里只做标记，后续分支按需跳过，属于“软return”。
                umbraHandledEgg = true;
            }

            if (!umbraHandledEgg)
            {
            // B) 破蛋优先级（场上有蛋时尽量先触发）

            bool hasEggPopScoreDecision = false;
            Card.Cards bestEggPopTool = Ritual;

            // 硬规则：只要牌库仍有安布拉，且场上有蛋、献祭召唤者可用，
            // 则献祭召唤者就是最优先破蛋手段（跳过评分器的摇摆）。
            // 低血时同时抬高黑暗契约，优先作为后续补血组件。
            int myHpForLifeline = 0;
            int enemyAtkForLifeline = 0;
            int summonerCostNow = 99;
            int darkPactCostNow = 99;
            try
            {
                myHpForLifeline = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                enemyAtkForLifeline = GetEnemyBoardAttack(board);
                if (board.Hand != null)
                {
                    summonerCostNow = board.Hand
                        .Where(x => x != null && x.Template != null && x.Template.Id == SacrificialSummoner)
                        .Select(x => x.CurrentCost)
                        .DefaultIfEmpty(99)
                        .Min();
                    darkPactCostNow = board.Hand
                        .Where(x => x != null && x.Template != null && x.Template.Id == DarkPact)
                        .Select(x => x.CurrentCost)
                        .DefaultIfEmpty(99)
                        .Min();
                }
            }
            catch
            {
                myHpForLifeline = 0;
                enemyAtkForLifeline = 0;
                summonerCostNow = 99;
                darkPactCostNow = 99;
            }

            bool mustSummonerIfUmbraInDeck =
                !hasUmbraOnBoard
                && umbraStillInDeck
                && canPlaySummonerNow
                && summonerCostNow < 99
                && friendMinions <= 5;

            if (mustSummonerIfUmbraInDeck)
            {
                p.CastMinionsModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(-1200));
                p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(3400));

                bool lowHpNeedHealNow = myHpForLifeline <= 22 || enemyAtkForLifeline >= Math.Max(6, myHpForLifeline - 6);
                if (hasDarkPactInHand && darkPactCostNow < 99 && manaAvailable >= darkPactCostNow)
                {
                    int pactMod = lowHpNeedHealNow ? -920 : -620;
                    int pactOrder = lowHpNeedHealNow ? 3320 : 3180;
                    // 场上有蛋时，黑契仍锁蛋，避免误吃献祭召唤者导致破蛋断档。
                    p.CastSpellsModifiers.AddOrUpdate(DarkPact, new Modifier(pactMod, bestEggOnBoardId));
                    p.PlayOrderModifiers.AddOrUpdate(DarkPact, new Modifier(pactOrder));
                    AddLog(CardName(DarkPact) + (lowHpNeedHealNow
                        ? " -920 | 顺序3320（低血：提高黑暗契约作为破蛋+回血组件的优先级）"
                        : " -620 | 顺序3180（牌库有安布拉线：黑暗契约作为后续破蛋组件提权）"));
                }

                // 强制牺牲目标偏向蛋，避免误吃其他组件。
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Egg, new Modifier(-5000));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT2, new Modifier(-5000));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT3, new Modifier(-5000));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT4, new Modifier(-5000));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT5, new Modifier(-5000));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(-1800));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Umbra, new Modifier(9000));

                if (hasDarkPactInHand && darkPactCostNow < 99 && manaAvailable >= (summonerCostNow + darkPactCostNow) && lowHpNeedHealNow)
                    AddLog("[安布拉拉取线] 强制优先：献祭召唤者吃蛋（拉安布拉）-> 黑暗契约回血");
                else
                    AddLog("[安布拉拉取线] 牌库有安布拉且场上有蛋：献祭召唤者最优先破蛋");
                hasEggPopScoreDecision = true;
                bestEggPopTool = SacrificialSummoner;
            }

            // B.0) 多破蛋手段并存：先收集 -> 评分 -> 只强推一个（避免“写死顺序”在不同局面出错）
            // 说明：只有在“可选破蛋工具 >= 2”时启用；否则仍沿用后面原本的分支写法。
            if (!hasEggPopScoreDecision) try
            {
                var popCandidates = new List<Card.Cards>();

                int handCountNow = 0;
                try { handCountNow = board.Hand != null ? board.Hand.Count : 0; } catch { handCountNow = 0; }

                // 召唤者：需要随从位允许。
                // 额外口径：若目标是“吃蛋拉安布拉”，则必须随从位<=5（否则吃蛋后空位不足，可能拉不出安布拉）。
                bool blockSummonerBySlotForUmbraPull = !hasUmbraOnBoard && umbraStillInDeck && friendMinions > 5;
                if (canPlaySummonerNow && !blockSummonerBySlotForUmbraPull) popCandidates.Add(SacrificialSummoner);

                // 吃小鬼：会爆牌就不作为候选
                if (hasEatImpInHand && !WouldOverdrawAfterSequence(handCountNow, 2, 3))
                {
                    if (canCastNow(EatImp)) popCandidates.Add(EatImp);
                }

                if (hasDarkPactInHand && canCastNow(DarkPact)) popCandidates.Add(DarkPact);
                if (hasRitualInHand && canCastNow(Ritual)) popCandidates.Add(Ritual);
                if (hasCruelGatheringInHand && canCastNow(CruelGathering)) popCandidates.Add(CruelGathering);
                if (hasGrimoireInHand && canCastNow(Grimoire)) popCandidates.Add(Grimoire);

                if (hasBurnInHand && !WouldOverdrawAfterSequence(handCountNow, 2, 1) && canCastNow(Burn))
                    popCandidates.Add(Burn);

                if (hasChaosConsumeInHand && enemyHasMinionForConsume && canCastNow(ChaosConsume))
                    popCandidates.Add(ChaosConsume);

                if (hasDefileInHand && canDefileChainNow && canCastNow(Defile))
                    popCandidates.Add(Defile);

                if (popCandidates.Count >= 2)
                {
                    int myHp = 0;
                    try { myHp = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor; } catch { myHp = 0; }

                    int enemyAtk = GetEnemyBoardAttack(board);
                    bool isAggroPressure = enemyAtk >= 10 || enemyMinions >= 3;

                    Card.Cards best = popCandidates[0];
                    int bestScore = int.MinValue;

                    foreach (var t in popCandidates)
                    {
                        int s = GetEggPopToolScoreSimple(t, myHp, handCountNow, friendMinions, enemyMinions, enemyAtk, isAggroPressure, hasUmbraOnBoard);

                        // 用户口径：当安布拉不在牌库时，不必强走召唤者破蛋，可优先其它破蛋组件。
                        // 这里在评分层面对召唤者做额外降权，让“法术破蛋/吃小鬼”等在同质量时先手。
                        if (!umbraStillInDeck && t == SacrificialSummoner)
                        {
                            s -= 70;
                        }

                        if (s > bestScore)
                        {
                            bestScore = s;
                            best = t;
                        }
                    }

                    // 强推 best：负数更爱用；同时给顺序加权。其他候选轻微压住，避免“同回合抢前”。
                    foreach (var t in popCandidates)
                    {
                        int castMod = (t == best) ? -780 : 120;
                        int order = (t == best) ? 2900 : 2100;

                        if (t == SacrificialSummoner)
                            p.CastMinionsModifiers.AddOrUpdate(t, new Modifier(castMod));
                        else if (RequiresEggTargetForPopTool(t))
                            p.CastSpellsModifiers.AddOrUpdate(t, new Modifier(castMod, bestEggOnBoardId));
                        else
                            p.CastSpellsModifiers.AddOrUpdate(t, new Modifier(castMod));

                        p.PlayOrderModifiers.AddOrUpdate(t, new Modifier(order));
                    }

                    // 牺牲目标统一偏向蛋阶段
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Egg, new Modifier(-1600));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT2, new Modifier(-1600));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT3, new Modifier(-1600));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT4, new Modifier(-1600));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT5, new Modifier(-1800));

                    AddLog("[EggPopScore] choose=" + CardName(best) + " score=" + bestScore + " from=" + string.Join(",", popCandidates.Select(x => CardName(x))));

                    hasEggPopScoreDecision = true;
                    bestEggPopTool = best;
                }
            }
            catch { }
            if (!hasEggPopScoreDecision && canPlaySummonerNow)
            {
                // 用户口径：献祭召唤者只对蛋使用
                // 且在触发蛋的各种手段中最优先（随从位<=5时更硬）
                // 额外口径：需要牌库里还有安布拉，才给“最优先”待遇
                bool blockSummonerBySlotForUmbraPull = !hasUmbraOnBoard && umbraStillInDeck && friendMinions > 5;
                if (blockSummonerBySlotForUmbraPull)
                {
                    p.CastMinionsModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(450));
                    p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(-300));
                    AddLog(CardName(SacrificialSummoner) + " 450 | 顺序-300（牌库有安布拉但随从位>5：暂缓召唤者，避免吃蛋后拉不出安布拉）");
                }
                else
                {
                bool bestSummoner = friendMinions <= 5 && umbraStillInDeck && !hasUmbraInHand;
                bool hasUmbraAlready = hasUmbraInHand;

                bool hasOtherEggBreakerNowExcludingSummoner =
                    (hasEatImpInHand && canCastNow(EatImp))
                    || (hasDarkPactInHand && canCastNow(DarkPact))
                    || (hasRitualInHand && canCastNow(Ritual))
                    || (hasCruelGatheringInHand && canCastNow(CruelGathering))
                    || (hasGrimoireInHand && canCastNow(Grimoire))
                    || (hasChaosConsumeInHand && enemyHasMinionForConsume && canCastNow(ChaosConsume))
                    || (hasBurnInHand && canCastNow(Burn))
                    || (hasDefileInHand && canDefileChainNow && canCastNow(Defile));

                int summonerMod = hasUmbraAlready ? -260 : (bestSummoner ? -950 : -520);
                int summonerOrder = hasUmbraAlready ? 2400 : (bestSummoner ? 2900 : 2800);

                if (!umbraStillInDeck && hasOtherEggBreakerNowExcludingSummoner)
                {
                    summonerMod = 220;
                    summonerOrder = -250;
                }

                p.CastMinionsModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(summonerMod));
                p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(summonerOrder));
                if (bestSummoner)
                    AddLog(CardName(SacrificialSummoner) + " -950 | 顺序2900（场上有蛋且随从位<=5且牌库有安布拉：献祭召唤者最优先破蛋）");
                else if (hasUmbraAlready)
                    AddLog(CardName(SacrificialSummoner) + " -260 | 顺序2400（场上有蛋且手里已有安布拉：献祭召唤者降权）");
                else if (!umbraStillInDeck && hasOtherEggBreakerNowExcludingSummoner)
                    AddLog(CardName(SacrificialSummoner) + " 220 | 顺序-250（牌库无安布拉且有其它破蛋组件：让位其它破蛋手段）");

                // 强制牺牲目标偏向“蛋阶段”
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Egg, new Modifier(-1600));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT2, new Modifier(-1600));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT3, new Modifier(-1600));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT4, new Modifier(-1600));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT5, new Modifier(-1800));
                }
            }

            int handCountNowB = 0;
            try { handCountNowB = board.Hand != null ? board.Hand.Count : 0; } catch { handCountNowB = 0; }
            bool handSafeForEatImp = handCountNowB <= 7;
            if (!hasEggPopScoreDecision && hasEatImpInHand)
            {
                // 明确规则：手牌<=7 时吃小鬼优先级仅次于召唤者；手牌大时仍可作为破蛋手段但不抢前。
                bool eatImpWouldOverdraw = WouldOverdrawAfterSequence(handCountNowB, 1, 3);
                int eatImpMod = eatImpWouldOverdraw ? 9999 : (handSafeForEatImp ? -520 : -120);
                int eatImpOrder = handSafeForEatImp ? 2850 : 2200;
                p.CastSpellsModifiers.AddOrUpdate(EatImp, new Modifier(eatImpMod, bestEggOnBoardId));
                p.PlayOrderModifiers.AddOrUpdate(EatImp, new Modifier(eatImpOrder));
                if (eatImpWouldOverdraw)
                    AddLog(CardName(EatImp) + " 9999（场上有蛋：吃小鬼将导致爆牌，禁用）");
                else if (handSafeForEatImp)
                    AddLog(CardName(EatImp) + " -520 | 顺序2850（场上有蛋：手牌<=7，优先吃小鬼破蛋抽3）");
            }

            bool enemyMany = enemyMinions >= 3;
            bool enemySingleBigAtk = enemyMinions == 1 && enemyMaxAtk >= 7;
            bool lowPressure = enemyMinions <= 1 && enemyMaxAtk <= 4;
            bool wantRitualFor55 = lowPressure && friendMinions >= 5 && friendMinions < 7;

            if (!hasEggPopScoreDecision && hasRitualInHand)
            {
                int ritualMod = wantRitualFor55 ? -520 : -260;
                int ritualOrder = wantRitualFor55 ? 2650 : 2350;
                p.CastSpellsModifiers.AddOrUpdate(Ritual, new Modifier(ritualMod, bestEggOnBoardId));
                p.PlayOrderModifiers.AddOrUpdate(Ritual, new Modifier(ritualOrder));
                if (wantRitualFor55)
                    AddLog(CardName(Ritual) + " -520 | 顺序2650（场面无压力且我方随从5-6：末日仪式破蛋并出5/5）");
            }

            if (!hasEggPopScoreDecision && hasDarkPactInHand)
            {
                int myHp = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                // 明确规则：低血时，黑暗契约作为破蛋+回血组件要显著提权。
                int enemyAtk = GetEnemyBoardAttack(board);
                bool urgentHeal = enemyAtk >= (myHp - 8);
                int darkPactMod = urgentHeal ? -980 : (myHp <= 22 ? -700 : -220);
                int darkPactOrder = urgentHeal ? 3340 : (myHp <= 22 ? 3080 : 2380);
                // 关键修正：有蛋在场时，黑暗契约应当始终以“最高阶段的蛋”为目标。
                p.CastSpellsModifiers.AddOrUpdate(DarkPact, new Modifier(darkPactMod, bestEggOnBoardId));
                p.PlayOrderModifiers.AddOrUpdate(DarkPact, new Modifier(darkPactOrder));
                if (urgentHeal)
                    AddLog(CardName(DarkPact) + " -980 | 顺序3340（场上有蛋：估算敌方场攻=" + enemyAtk + "，低血急救优先黑暗契约）");
                else if (myHp <= 22)
                    AddLog(CardName(DarkPact) + " -700 | 顺序3080（场上有蛋：血量<=22，提高黑暗契约破蛋回血优先级）");
            }

            if (!hasEggPopScoreDecision && hasGrimoireInHand)
            {
                int grimoireMod = enemyMany ? -520 : (enemyMinions > 0 ? -220 : -120);
                int grimoireOrder = enemyMany ? 2760 : 2500;
                p.CastSpellsModifiers.AddOrUpdate(Grimoire, new Modifier(grimoireMod, bestEggOnBoardId));
                p.PlayOrderModifiers.AddOrUpdate(Grimoire, new Modifier(grimoireOrder));
                if (enemyMany)
                    AddLog(CardName(Grimoire) + " -520 | 顺序2760（对面随从多：优先魔典破蛋顺便清杂）");
            }

            if (!hasEggPopScoreDecision && hasChaosConsumeInHand)
            {
                int consumeMod = enemyHasMinionForConsume ? (enemySingleBigAtk ? -520 : -120) : 9999;
                int consumeOrder = enemySingleBigAtk ? 2680 : 2320;
                p.CastSpellsModifiers.AddOrUpdate(ChaosConsume, new Modifier(consumeMod));
                p.PlayOrderModifiers.AddOrUpdate(ChaosConsume, new Modifier(consumeOrder));
                if (enemySingleBigAtk)
                    AddLog(CardName(ChaosConsume) + " -520 | 顺序2680（对面单个高攻：优先混乱吞噬破蛋并解威胁）");
            }

            if (!hasEggPopScoreDecision && hasBurnInHand)
            {
                // 用户口径：焚烧也是触发蛋的手段
                // 说明：焚烧是定向伤害牌，靠“蛋的牺牲价值”引导优先打在蛋上。
                p.CastSpellsModifiers.AddOrUpdate(Burn, new Modifier(-260, bestEggOnBoardId));
                int handCount = 0;
                try { handCount = board.Hand != null ? board.Hand.Count : 0; } catch { handCount = 0; }
                bool enemyManyNow = enemyMinions >= 3;
                int burnOrder = handCount <= 9 ? (enemyManyNow ? 2680 : 2750) : 2250;
                p.PlayOrderModifiers.AddOrUpdate(Burn, new Modifier(burnOrder));
                if (handCount <= 9)
                    AddLog(CardName(Burn) + " -260 | 顺序" + burnOrder + "（场上有蛋：" + (enemyManyNow ? "对面铺场，焚烧后置于魔典" : "手牌<=9，焚烧作为破蛋手段") + "）");
                else
                    AddLog(CardName(Burn) + " -260 | 顺序2250（场上有蛋：焚烧可用但非优先）");

                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Egg, new Modifier(-1600));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT2, new Modifier(-1600));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT3, new Modifier(-1600));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT4, new Modifier(-1600));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT5, new Modifier(-1800));
            }

            if (!hasEggPopScoreDecision && hasDefileInHand)
            {
                if (canDefileChainNow)
                {
                    int chainNow = 0;
                    try { chainNow = GetDefileMaxContiguousHealthChain(board); } catch { chainNow = 0; }
                    p.CastSpellsModifiers.AddOrUpdate(Defile, new Modifier(-200));
                    p.PlayOrderModifiers.AddOrUpdate(Defile, new Modifier(2600));
                    AddLog(CardName(Defile) + " -200 | 顺序2600（亵渎链允许：当前连续链=1.." + chainNow + "，血量=" + GetAllMinionsHealthSnapshot(board) + "）");
                }
                else
                {
                    // 用户口径：不满足“连续递增链”则不使用亵渎
                    int chainNow = 0;
                    try { chainNow = GetDefileMaxContiguousHealthChain(board); } catch { chainNow = 0; }
                    p.CastSpellsModifiers.AddOrUpdate(Defile, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(Defile, new Modifier(-9999));
                    AddLog(CardName(Defile) + " 9999（亵渎链不足：当前连续链=1.." + chainNow + "，血量=" + GetAllMinionsHealthSnapshot(board) + " => 禁用亵渎）");
                }
            }

            }

            if (hasEggBreaker)
            {
                forceDisableLifeTap = true;
                forceDisableLifeTapReason = "场上有蛋且可破：优先破蛋";

                p.CastMinionsModifiers.AddOrUpdate(Eggbearer, new Modifier(350));
                p.CastMinionsModifiers.AddOrUpdate(Librarian, new Modifier(350));
                p.CastSpellsModifiers.AddOrUpdate(Fracking, new Modifier(250));
                p.CastSpellsModifiers.AddOrUpdate(MorriganRealm, new Modifier(250));

                // 额外护栏：避免“有蛋可破但先亵渎/先地狱烈焰”的顺序偏差
                // 亵渎若不满足递增链则已禁用；若满足链则允许作为破蛋手段，不再统一压死。
                p.CastSpellsModifiers.AddOrUpdate(Hellfire, new Modifier(450));

                AddLog("场上有蛋且可破：强制优先破蛋（避免先抽牌/先下随从再回头破）");
            }

            AddLog("场上有蛋：优先破蛋（下蛋即破蛋）");
        }

        private void ProcessCard_UNG_900_Umbra(ProfileParameters p, Board board,
            bool hasEggInHand, bool hasEggOnBoard,
            int eggInGrave)
        {
            // 安布拉：尽量留给爆发回合
            if (!board.HasCardInHand(Umbra)) return;

            // 用户口径补充：若手里同时有蛋+安布拉，且费用足够同回合打出，则可以直接走“安布拉->下蛋”连锁组合。
            // 说明：安布拉必须先下，后下蛋才能触发亡语连锁。
            if (hasEggInHand && !hasEggOnBoard)
            {
                try
                {
                    var umbraCard = board.Hand != null
                        ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Umbra)
                        : null;
                    var eggCard = board.Hand != null
                        ? board.Hand
                            .Where(x => x != null && x.Template != null && IsEggStage(x.Template.Id))
                            .OrderByDescending(x => GetEggStageRank(x.Template.Id))
                            .FirstOrDefault()
                        : null;

                    if (umbraCard != null && eggCard != null)
                    {
                        var eggId = eggCard.Template.Id;
                        int needMana = umbraCard.CurrentCost + eggCard.CurrentCost;
                        if (board.ManaAvailable >= needMana)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Umbra, new Modifier(-750));
                            p.PlayOrderModifiers.AddOrUpdate(Umbra, new Modifier(3100));

                            // 下蛋也抬顺序，但必须在安布拉之后
                            p.CastMinionsModifiers.AddOrUpdate(eggId, new Modifier(-650));
                            p.PlayOrderModifiers.AddOrUpdate(eggId, new Modifier(3050));

                            if (p.ComboModifier != null)
                                AddLog("[COMBO] 覆盖已有ComboModifier：安布拉->下蛋（手里有蛋+安布拉且费用够）");
                            p.ComboModifier = new ComboSet(umbraCard.Id, eggCard.Id);
                            AddLog("[COMBO] 安布拉->下蛋（手里有蛋+安布拉且费用够：直接连锁展开）");

                            AddLog(CardName(Umbra) + " -750 | 顺序3100（手里有蛋+安布拉且可同回合：优先安布拉->下蛋连锁，蛋=" + CardName(eggId) + "）");
                            return;
                        }
                    }
                }
                catch { }
            }

            // 用户口径：场上有蛋 + 手里有安布拉时，若下完安布拉后剩余费用不足以用法术立刻杀掉蛋，则安布拉暂时不出。
            if (hasEggOnBoard)
            {
                try
                {
                    var umbraCard = board.Hand != null
                        ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Umbra)
                        : null;
                    int umbraCost = umbraCard != null ? umbraCard.CurrentCost : 4;
                    int remaining = board.ManaAvailable - umbraCost;

                    bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Count > 0;
                    bool canDefileNow = board.HasCardInHand(Defile) && HasDefileIncreasingChain(board, 3);

                    int myHpArmor = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                    int handCount = 0;
                    try { handCount = board.Hand != null ? board.Hand.Count : 0; } catch { handCount = 0; }

                    // 只统计“能直接触发我方随从死亡”的手段（破蛋手段）
                    // - 末日仪式/黑暗契约/吃小鬼/牺牲魔典/残酷集结/焚烧/混乱吞噬/亵渎
                    bool canPopBySpell = false;
                    string bestName = "";
                    Card bestCard = null;
                    int bestCost = 99;

                    // 明确规则：场上有蛋时触发组件优先级（安布拉必须先手）
                    // 吃小鬼(手<=7) > 黑暗契约(血<=22) > 焚烧(手<=9) > 魔典 > 末日仪式 > 亵渎(递增链) > 混乱吞噬(有目标) > 兜底最低费
                    if (remaining >= 0 && board.Hand != null)
                    {
                        // 额外硬规则：0费末日仪式是最理想的“安布拉后触发”起手（不占法力窗口）
                        var ritual0 = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Ritual && x.CurrentCost == 0 && x.CurrentCost <= remaining);
                        if (ritual0 != null)
                        {
                            bestCard = ritual0;
                            bestName = CardName(Ritual) + "(" + Ritual + ")";
                        }

                        if (handCount <= 7)
                        {
                            if (bestCard == null)
                            {
                                bool wouldOverdraw = WouldOverdrawAfterSequence(handCount, 2, 3); // 安布拉+吃鬼
                                if (!wouldOverdraw)
                                {
                                    bestCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == EatImp && x.CurrentCost <= remaining);
                                    if (bestCard != null) bestName = CardName(EatImp) + "(" + EatImp + ")";
                                }
                                else
                                {
                                    AddLog("[安布拉-防爆牌] 安布拉+吃小鬼可能爆牌(手=" + handCount + ")，跳过吃小鬼优先级");
                                }
                            }
                        }
                        if (bestCard == null && myHpArmor <= 22)
                        {
                            bestCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == DarkPact && x.CurrentCost <= remaining);
                            if (bestCard != null) bestName = CardName(DarkPact) + "(" + DarkPact + ")";
                        }
                        if (bestCard == null && board.Hand != null)
                        {
                            bestCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == CruelGathering && x.CurrentCost <= remaining);
                            if (bestCard != null) bestName = CardName(CruelGathering) + "(" + CruelGathering + ")";
                        }
                        if (bestCard == null && handCount <= 9)
                        {
                            bestCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Burn && x.CurrentCost <= remaining);
                            if (bestCard != null) bestName = CardName(Burn) + "(" + Burn + ")";
                        }
                        if (bestCard == null)
                        {
                            bestCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Grimoire && x.CurrentCost <= remaining);
                            if (bestCard != null) bestName = CardName(Grimoire) + "(" + Grimoire + ")";
                        }
                        if (bestCard == null)
                        {
                            bestCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Ritual && x.CurrentCost <= remaining);
                            if (bestCard != null) bestName = CardName(Ritual) + "(" + Ritual + ")";
                        }
                        if (bestCard == null && canDefileNow)
                        {
                            bestCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Defile && x.CurrentCost <= remaining);
                            if (bestCard != null) bestName = CardName(Defile) + "(" + Defile + ")";
                        }
                        if (bestCard == null && enemyHasMinion)
                        {
                            bestCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == ChaosConsume && x.CurrentCost <= remaining);
                            if (bestCard != null) bestName = CardName(ChaosConsume) + "(" + ChaosConsume + ")";
                        }

                        // 兜底：最低费
                        if (bestCard == null)
                        {
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (c.Template.Id == Umbra) continue;
                                bool ok = false;
                                if (c.Template.Id == Ritual) ok = true;
                                else if (c.Template.Id == DarkPact) ok = true;
                                else if (c.Template.Id == EatImp) ok = true;
                                else if (c.Template.Id == Grimoire) ok = true;
                                else if (c.Template.Id == Burn) ok = true;
                                else if (c.Template.Id == CruelGathering) ok = true;
                                else if (c.Template.Id == ChaosConsume) ok = enemyHasMinion;
                                else if (c.Template.Id == Defile) ok = canDefileNow;

                                if (!ok) continue;
                                if (c.CurrentCost > remaining) continue;

                                if (c.CurrentCost < bestCost)
                                {
                                    bestCost = c.CurrentCost;
                                    bestName = CardName(c.Template.Id) + "(" + c.Template.Id + ")";
                                    bestCard = c;
                                }
                            }
                        }

                        if (bestCard != null) bestCost = bestCard.CurrentCost;

                        canPopBySpell = bestCard != null;
                    }

                    // 额外：献祭召唤者也算“可立即破蛋手段”（随从）
                    try
                    {
                        var summonerCard = board.Hand != null
                            ? board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == SacrificialSummoner)
                                .OrderBy(x => x.CurrentCost).FirstOrDefault()
                            : null;

                        if (summonerCard != null && summonerCard.CurrentCost <= remaining && bestCard == null)
                        {
                            canPopBySpell = true;
                            bestName = CardName(SacrificialSummoner) + "(" + SacrificialSummoner + ")";
                            bestCard = summonerCard;
                        }
                    }
                    catch { }

                    if (!canPopBySpell)
                    {
                        // 用户口径：只要场上有蛋，也允许先下安布拉占位，
                        // 不再要求“安布拉后本回合必须立刻有破蛋法术”这一硬门槛。
                        p.CastMinionsModifiers.AddOrUpdate(Umbra, new Modifier(-420));
                        p.PlayOrderModifiers.AddOrUpdate(Umbra, new Modifier(2200));
                        AddLog(CardName(Umbra) + " -420 | 顺序2200（场上有蛋：即使本回合无直连破蛋手段，也允许先下安布拉备战连锁）");
                        return;
                    }

                    // 关键线：场上有蛋且本回合能先下安布拉再接破蛋手段时，应优先安布拉（否则会浪费连锁爆发窗口）
                    p.CastMinionsModifiers.AddOrUpdate(Umbra, new Modifier(-650));
                    p.PlayOrderModifiers.AddOrUpdate(Umbra, new Modifier(2950));

                    // 把“破蛋手段”压到安布拉之后（保证先安布拉再破蛋）
                    try
                    {
                        if (bestCard != null && bestCard.Template != null)
                            p.PlayOrderModifiers.AddOrUpdate(bestCard.Template.Id, new Modifier(2900));
                    }
                    catch { }

                    // 若安布拉线可行且存在法术破蛋手段，则压住献祭召唤者，避免抢在安布拉前出牌。
                    try
                    {
                        bool bestIsSummoner = bestCard != null && bestCard.Template != null && bestCard.Template.Id == SacrificialSummoner;
                        if (!bestIsSummoner && board.HasCardInHand(SacrificialSummoner))
                        {
                            p.CastMinionsModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(450));
                            p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(-300));
                            AddLog("[安布拉护栏] 已有安布拉+法术破蛋线：压住献祭召唤者，避免抢节奏");
                        }
                    }
                    catch { }

                    // Combo 强推：安布拉 -> 破蛋手段
                    try
                    {
                        if (p.ComboModifier == null && umbraCard != null && bestCard != null)
                        {
                            p.ComboModifier = new ComboSet(umbraCard.Id, bestCard.Id);
                            AddLog("[COMBO] 安布拉->破蛋：" + CardName(Umbra) + " => " + (string.IsNullOrEmpty(bestName) ? "(未知)" : bestName));
                        }
                    }
                    catch { }

                    AddLog("[安布拉护栏] 场上有蛋：下安布拉后剩余法力=" + remaining + "，可用破蛋手段=" + (string.IsNullOrEmpty(bestName) ? "(未知)" : bestName) + "(<= " + bestCost + ") => 允许安布拉");
                    AddLog(CardName(Umbra) + " -650 | 顺序2950（场上有蛋且可安布拉后接破蛋：优先安布拉制造连锁）");
                    return;
                }
                catch
                {
                    // 解析失败时不做硬禁用，避免误伤
                }
            }

            if (hasEggInHand || hasEggOnBoard)
            {
                p.CastMinionsModifiers.AddOrUpdate(Umbra, new Modifier(-120));
                p.PlayOrderModifiers.AddOrUpdate(Umbra, new Modifier(300));
                AddLog(CardName(Umbra) + " -120 | 顺序300（有蛋时可考虑上安布拉）");
            }
            else
            {
                // 用户口径修正（实战反馈）：即使手/场都没蛋，只要坟场已有蛋阶段，也允许“先下安布拉备战”，
                // 以便后续通过尸身保护令/亡者复生等把蛋再召出来时立刻连锁。
                if (eggInGrave > 0)
                {
                    // 若能本回合直接连锁：安布拉 -> 尸身保护令（复活蛋触发安布拉）
                    try
                    {
                        var umbraCard = board.Hand != null
                            ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Umbra)
                            : null;
                        var corpseCard = board.Hand != null
                            ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == CorpseOrder)
                            : null;

                        int friendMinions = 0;
                        try { friendMinions = board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { friendMinions = 0; }

                        if (umbraCard != null && corpseCard != null
                            && friendMinions <= 4
                            && board.ManaAvailable >= (umbraCard.CurrentCost + corpseCard.CurrentCost))
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Umbra, new Modifier(-650));
                            p.PlayOrderModifiers.AddOrUpdate(Umbra, new Modifier(2950));

                            p.CastSpellsModifiers.AddOrUpdate(CorpseOrder, new Modifier(-240));
                            p.PlayOrderModifiers.AddOrUpdate(CorpseOrder, new Modifier(2900));

                            if (p.ComboModifier == null)
                            {
                                p.ComboModifier = new ComboSet(umbraCard.Id, corpseCard.Id);
                                AddLog("[COMBO] 安布拉->尸身保护令（坟场有蛋：复活蛋触发连锁）");
                            }

                            AddLog(CardName(Umbra) + " -650 | 顺序2950（坟场有蛋且可同回合接尸身保护令：安布拉先手备战连锁）");
                            return;
                        }
                    }
                    catch { }

                    // 同口径：随从<=4 且坟场有蛋时，允许安布拉->亡者复生
                    try
                    {
                        var umbraCard = board.Hand != null
                            ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Umbra)
                            : null;
                        var raiseDeadCard = board.Hand != null
                            ? board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == RaiseDead)
                            : null;

                        int friendMinions = 0;
                        try { friendMinions = board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { friendMinions = 0; }

                        if (umbraCard != null && raiseDeadCard != null
                            && friendMinions <= 4
                            && board.ManaAvailable >= (umbraCard.CurrentCost + raiseDeadCard.CurrentCost))
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Umbra, new Modifier(-650));
                            p.PlayOrderModifiers.AddOrUpdate(Umbra, new Modifier(2950));

                            p.CastSpellsModifiers.AddOrUpdate(RaiseDead, new Modifier(-240));
                            p.PlayOrderModifiers.AddOrUpdate(RaiseDead, new Modifier(2900));

                            if (p.ComboModifier == null)
                            {
                                p.ComboModifier = new ComboSet(umbraCard.Id, raiseDeadCard.Id);
                                AddLog("[COMBO] 安布拉->亡者复生（坟场有蛋：回收蛋触发连锁）");
                            }

                            AddLog(CardName(Umbra) + " -650 | 顺序2950（坟场有蛋且随从<=4：可同回合接亡者复生，安布拉先手备战连锁）");
                            return;
                        }
                    }
                    catch { }

                    // 否则：允许安布拉作为“准备动作”，但不强推，避免无意义裸下。
                    p.CastMinionsModifiers.AddOrUpdate(Umbra, new Modifier(150));
                    p.PlayOrderModifiers.AddOrUpdate(Umbra, new Modifier(500));
                    AddLog(CardName(Umbra) + " 150 | 顺序500（坟场有蛋：允许下安布拉备战复活/回收蛋连锁）");
                }
                else
                {
                    // 坟场也没蛋：安布拉确实不应裸下。
                    p.CastMinionsModifiers.AddOrUpdate(Umbra, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(Umbra, new Modifier(-9999));
                    AddLog(CardName(Umbra) + " 9999（场上/手里都没蛋且坟场无蛋：禁用安布拉）");
                }
            }
        }

        private void ProcessCard_MAW_002_CorpseOrder(ProfileParameters p, Board board,
            int eggInGrave,
            bool hasEggOnBoard)
        {
            // 尸身保护令：优先在坟场有蛋时使用
            if (!board.HasCardInHand(CorpseOrder)) return;

            int friendMinions = 0;
            try { friendMinions = board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { friendMinions = 0; }
            if (friendMinions >= 7)
            {
                // 用户口径：场上随从位满（=7）时，不使用尸身保护令（无法复活上场，且会导致策略动作浪费）
                p.CastSpellsModifiers.AddOrUpdate(CorpseOrder, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(CorpseOrder, new Modifier(-9999));
                AddLog(CardName(CorpseOrder) + " 9999 | 顺序-9999（我方随从位满7：禁用尸身保护令）");
                return;
            }

            if (eggInGrave > 0)
            {
                // 用户口径：当场上已有蛋、且坟场也有蛋资源时，应优先用尸身保护令（先补资源/铺垫），再继续破蛋。
                if (hasEggOnBoard)
                {
                    // 破蛋工具常用顺序多在 2850~2900 左右，这里更强硬抬升，确保 MAW_002 先手。
                    p.CastSpellsModifiers.AddOrUpdate(CorpseOrder, new Modifier(-900));
                    p.PlayOrderModifiers.AddOrUpdate(CorpseOrder, new Modifier(3050));
                    AddLog(CardName(CorpseOrder) + " -900 | 顺序3050（场上有蛋且坟场有蛋：优先尸身保护令，再继续破蛋）");
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(CorpseOrder, new Modifier(-200));
                    AddLog(CardName(CorpseOrder) + " -200（坟场有蛋：考虑复活触发亡语）");
                }
            }
            else
            {
                p.CastSpellsModifiers.AddOrUpdate(CorpseOrder, new Modifier(9999));
                AddLog(CardName(CorpseOrder) + " 9999（坟场无蛋：禁用尸身保护令）");
            }
        }

        private void ProcessCard_SCH_514_RaiseDead(ProfileParameters p, Board board,
            bool wantFinleySwap,
            int eggInGrave,
            bool shouldPrioritizeEggNow)
        {
            // 亡者复生：坟场有蛋时可以用来回收蛋/组件，但需要控制手牌空间。
            if (!board.HasCardInHand(RaiseDead)) return;

            // 用户口径：坟场没有蛋 => 不用亡者复生（避免自残回收杂牌、浪费节奏）
            if (eggInGrave <= 0)
            {
                p.CastSpellsModifiers.AddOrUpdate(RaiseDead, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(RaiseDead, new Modifier(-9999));
                AddLog(CardName(RaiseDead) + " 9999 | 顺序-9999（坟场无蛋：禁用亡者复生）");
                return;
            }

            // 蛋术专属护栏：当本回合“应该先下蛋”时，亡者复生不能抢动作（否则会出现你日志里的情况：先复生->蛋没下）
            if (shouldPrioritizeEggNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(RaiseDead, new Modifier(350));
                p.PlayOrderModifiers.AddOrUpdate(RaiseDead, new Modifier(-999));
                AddLog(CardName(RaiseDead) + " 350 | 顺序-999（本回合优先下蛋：暂缓亡者复生，避免抢掉下蛋动作）");
                return;
            }

            int handCount = 0;
            try { handCount = board.Hand != null ? board.Hand.Count : 0; } catch { handCount = 0; }

            int myHpArmor = 0;
            try { myHpArmor = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor; } catch { myHpArmor = 0; }

            if (wantFinleySwap)
            {
                // 芬利回合：先换手，避免先回收再被换走
                p.CastSpellsModifiers.AddOrUpdate(RaiseDead, new Modifier(450));
                p.PlayOrderModifiers.AddOrUpdate(RaiseDead, new Modifier(-200));
                return;
            }

            // 用户口径：坟场有蛋 && 手牌数<=8 => 可以使用亡者复生
            // 额外护栏：血甲<=3时不自杀
            if (eggInGrave > 0 && handCount <= 8 && myHpArmor > 3)
            {
                bool hasEggOnBoardNow = false;
                try
                {
                    hasEggOnBoardNow = board.MinionFriend != null
                        && board.MinionFriend.Any(m => m != null && m.Template != null && IsEggStage(m.Template.Id));
                }
                catch { hasEggOnBoardNow = false; }

                if (hasEggOnBoardNow)
                {
                    // 场上有蛋时，提高亡者复生顺序，优先补回组件后再继续破蛋。
                    // 仍低于尸身保护令(3050)，避免打断更强复活线。
                    p.CastSpellsModifiers.AddOrUpdate(RaiseDead, new Modifier(-320));
                    p.PlayOrderModifiers.AddOrUpdate(RaiseDead, new Modifier(3000));
                    AddLog(CardName(RaiseDead) + " -320 | 顺序3000（场上有蛋且坟场有蛋：提高优先级先亡者复生补组件，再继续破蛋）");
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(RaiseDead, new Modifier(-220));
                    p.PlayOrderModifiers.AddOrUpdate(RaiseDead, new Modifier(1200));
                    AddLog(CardName(RaiseDead) + " -220 | 顺序1200（坟场有蛋且手牌<=8：允许亡者复生补资源）");
                }
            }
            else
            {
                p.CastSpellsModifiers.AddOrUpdate(RaiseDead, new Modifier(250));
            }
        }

        private void ProcessCard_AV_317_Phylactery(ProfileParameters p, Board board,
            int eggInGrave, bool hasAnyFriendlyMinionOnBoard)
        {
            // 护命匣：需要坟场亡语，且需要己方有随从吃到亡语
            if (!board.HasCardInHand(Phylactery)) return;

            bool canUsePhylactery = eggInGrave > 0 && hasAnyFriendlyMinionOnBoard;
            p.CastSpellsModifiers.AddOrUpdate(Phylactery, new Modifier(canUsePhylactery ? -180 : 9999));
            if (canUsePhylactery)
                AddLog(CardName(Phylactery) + " -180（坟场有蛋且场上有随从：考虑护命匣铺亡语）");
            else
                AddLog(CardName(Phylactery) + " 9999（坟场无蛋或场上无随从：禁用护命匣）");
        }

        private void ProcessCard_TSC_926_Starfish(ProfileParameters p, Board board,
            bool hasEggOnBoard, bool wantFinleySwap)
        {
            if (!board.HasCardInHand(Starfish)) return;

            if (wantFinleySwap)
            {
                p.CastMinionsModifiers.AddOrUpdate(Starfish, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(Starfish, new Modifier(-9999));
                return;
            }

            if (hasEggOnBoard)
            {
                p.CastMinionsModifiers.AddOrUpdate(Starfish, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(Starfish, new Modifier(-9999));
                AddLog(CardName(Starfish) + " 9999（场上有蛋：禁用海星，避免把蛋沉默崩盘）");
            }
            else
            {
                p.CastMinionsModifiers.AddOrUpdate(Starfish, new Modifier(150));
            }
        }

        private void ProcessCard_ICC_041_Defile(ProfileParameters p, Board board,
            bool isAggroLikely, int myHpArmor,
            bool hasEggOnBoard)
        {
            if (!board.HasCardInHand(Defile)) return;

            // 用户口径：亵渎需要满足“场上血量能形成连续递增链”（不满足则禁用）
            // 备注：递增链按 1..N 连续覆盖，至少覆盖到 3（否则通常无法稳定清到3血蛋/也容易空亵渎）
            int chainNow = GetDefileMaxContiguousHealthChain(board);
            bool canDefileChain = chainNow >= 3;
            if (!canDefileChain)
            {
                p.CastSpellsModifiers.AddOrUpdate(Defile, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(Defile, new Modifier(-9999));
                AddLog(CardName(Defile) + " 9999（亵渎链不足：当前连续链=1.." + chainNow + "，血量=" + GetAllMinionsHealthSnapshot(board) + " => 禁用亵渎）");
                return;
            }

            int enemyMinions = board.MinionEnemy != null ? board.MinionEnemy.Count : 0;
            if (enemyMinions == 0 && !hasEggOnBoard)
            {
                p.CastSpellsModifiers.AddOrUpdate(Defile, new Modifier(9999));
            }
            else
            {
                // 有蛋在场时，亵渎也视为“破蛋手段之一”，不再强依赖对面随从数量。
                bool shouldDefile = hasEggOnBoard || enemyMinions >= 3 || (isAggroLikely && myHpArmor <= 18);
                p.CastSpellsModifiers.AddOrUpdate(Defile, new Modifier(shouldDefile ? -220 : 180));
            }
        }

        private static int GetDefileMaxContiguousHealthChain(Board board)
        {
            try
            {
                if (board == null) return 0;

                var healthSet = new HashSet<int>();
                if (board.MinionFriend != null)
                {
                    foreach (var m in board.MinionFriend)
                    {
                        if (m == null) continue;
                        int h = m.CurrentHealth;
                        if (h > 0) healthSet.Add(h);
                    }
                }
                if (board.MinionEnemy != null)
                {
                    foreach (var m in board.MinionEnemy)
                    {
                        if (m == null) continue;
                        int h = m.CurrentHealth;
                        if (h > 0) healthSet.Add(h);
                    }
                }

                int chain = 0;
                for (int need = 1; need <= 20; need++)
                {
                    if (!healthSet.Contains(need)) break;
                    chain = need;
                }

                return chain;
            }
            catch
            {
                return 0;
            }
        }

        private static int GetDefileMaxContiguousHealthChainAfterAddingEgg(Board board)
        {
            // 下蛋即破：此处按“当前场面血量 + 蛋的3血”估算亵渎连续链
            try
            {
                int chainNow = GetDefileMaxContiguousHealthChain(board);

                // 如果当前链已经 >=3，直接返回（下蛋后通常只会更好，不必再复杂估）
                if (chainNow >= 3) return chainNow;

                // 简化估算：把“3血蛋”加入健康集合，再重新计算连续链
                var healthSet = new HashSet<int>();
                if (board != null && board.MinionFriend != null)
                {
                    foreach (var m in board.MinionFriend)
                    {
                        if (m == null) continue;
                        int h = m.CurrentHealth;
                        if (h > 0) healthSet.Add(h);
                    }
                }
                if (board != null && board.MinionEnemy != null)
                {
                    foreach (var m in board.MinionEnemy)
                    {
                        if (m == null) continue;
                        int h = m.CurrentHealth;
                        if (h > 0) healthSet.Add(h);
                    }
                }
                healthSet.Add(3);

                int chain = 0;
                for (int need = 1; need <= 20; need++)
                {
                    if (!healthSet.Contains(need)) break;
                    chain = need;
                }
                return chain;
            }
            catch
            {
                return 0;
            }
        }

        private static bool HasDefileIncreasingChain(Board board, int minChainLen)
        {
            try
            {
                int chain = GetDefileMaxContiguousHealthChain(board);
                return chain >= minChainLen;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasDefileIncreasingChainAfterAddingEgg(Board board, int minChainLen)
        {
            try
            {
                int chain = GetDefileMaxContiguousHealthChainAfterAddingEgg(board);
                return chain >= minChainLen;
            }
            catch
            {
                return false;
            }
        }

        private string GetAllMinionsHealthSnapshot(Board board)
        {
            try
            {
                var list = new List<int>();
                if (board != null && board.MinionFriend != null)
                {
                    foreach (var m in board.MinionFriend)
                    {
                        if (m == null) continue;
                        if (m.CurrentHealth > 0) list.Add(m.CurrentHealth);
                    }
                }
                if (board != null && board.MinionEnemy != null)
                {
                    foreach (var m in board.MinionEnemy)
                    {
                        if (m == null) continue;
                        if (m.CurrentHealth > 0) list.Add(m.CurrentHealth);
                    }
                }

                if (list.Count == 0) return "(无随从)";
                list.Sort();
                return string.Join(",", list);
            }
            catch
            {
                return "(解析失败)";
            }
        }

        private bool IsUmbraStillInDeck(Board board, bool hasUmbraInHand, bool hasUmbraOnBoard)
        {
            // 口径：要求“牌库里还有安布拉”。在 SmartBot API 里未必能稳定读取牌库具体列表，
            // 这里用 1 张构筑的推断：不在手/不在场/不在坟场 => 仍在牌库（或被燃烧等极少数异常情况）。
            try
            {
                if (hasUmbraInHand) return false;
                if (hasUmbraOnBoard) return false;
                if (board != null && board.FriendGraveyard != null && board.FriendGraveyard.Contains(Umbra)) return false;
                return true;
            }
            catch
            {
                return true;
            }
        }

        private bool IsUmbraSilencedOnBoard(Board board)
        {
            try
            {
                if (board == null || board.MinionFriend == null) return false;
                return board.MinionFriend.Any(m => m != null && m.Template != null
                    && m.Template.Id == Umbra && m.IsSilenced);
            }
            catch
            {
                return false;
            }
        }

        private void ProcessCard_CORE_CS2_062_Hellfire(ProfileParameters p, Board board,
            bool isAggroLikely, int myHpArmor,
            bool hasEggOnBoard, bool hasUmbraOnBoard)
        {
            if (!board.HasCardInHand(Hellfire)) return;

            int enemyMinions = board.MinionEnemy != null ? board.MinionEnemy.Count : 0;
            int friendMinions = board.MinionFriend != null ? board.MinionFriend.Count : 0;

            if (enemyMinions == 0)
            {
                p.CastSpellsModifiers.AddOrUpdate(Hellfire, new Modifier(9999));
                AddLog(CardName(Hellfire) + " 9999（对面无随从：禁用，避免自伤/误烧我方）");
            }
            else if (enemyMinions <= 1 && (hasEggOnBoard || hasUmbraOnBoard) && friendMinions > 0)
            {
                p.CastSpellsModifiers.AddOrUpdate(Hellfire, new Modifier(9999));
                AddLog(CardName(Hellfire) + " 9999（对面随从少且我方关键随从在场：禁用防呆）");
            }
            else
            {
                bool shouldHellfire = enemyMinions >= 3 || (isAggroLikely && myHpArmor <= 18);
                p.CastSpellsModifiers.AddOrUpdate(Hellfire, new Modifier(shouldHellfire ? -80 : 200));
                if (shouldHellfire)
                    AddLog(CardName(Hellfire) + " -80（对面随从较多/快攻压力：允许作为清场）");
            }
        }

        private void ProcessCard_CORE_CFM_790_DirtyRat(ProfileParameters p, Board board,
            bool isComboLikely)
        {
            if (!board.HasCardInHand(DirtyRat)) return;
            p.CastMinionsModifiers.AddOrUpdate(DirtyRat, new Modifier(isComboLikely ? -80 : 120));
        }

        private void ProcessHeroPower_LifeTap(ProfileParameters p, Board board,
            bool wantFinleySwap,
            bool forceDisableLifeTap, string forceDisableLifeTapReason,
            bool isAggroLikely)
        {
            Card.Cards friendAbility = ProfileCommon.GetFriendAbilityId(board, LifeTap);

            try
            {
                bool lowHpAny = (board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) <= 10;
                bool lowHpVsAggro = (board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) <= 12;

                if (wantFinleySwap)
                {
                    // 用户口径：不要“硬禁用抽一口”，改为软禁用；若芬利因各种原因没能打出，允许分流兜底。
                    p.CastHeroPowerModifier.AddOrUpdate(friendAbility, new Modifier(130));
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(130));
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTapLegacy, new Modifier(130));
                    AddLog("英雄技能：芬利换手回合，分流软禁用(130)");
                }
                else if (forceDisableLifeTap)
                {
                    // 实战修正：如果本回合已经识别到“必须先做的关键动作”（例如：压裂/下蛋/破蛋），
                    // 仅靠软禁用仍可能被点分流打断，因此：
                    // - 有可执行动作时 => 硬禁用(9999)
                    // - 真的无牌可打时 => 仍保留软禁用(130)做兜底
                    bool hasLikelyPlayableAction = false;
                    try
                    {
                        if (p.ComboModifier != null) hasLikelyPlayableAction = true;
                        if (!hasLikelyPlayableAction && board != null && board.Hand != null)
                        {
                            int mana = 0;
                            try { mana = board.ManaAvailable; } catch { mana = 0; }

                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (c.Template.Id == TheCoin) continue;
                                if (c.CurrentCost > mana) continue;

                                hasLikelyPlayableAction = true;
                                break;
                            }
                        }
                    }
                    catch { hasLikelyPlayableAction = (p.ComboModifier != null); }

                    int tapMod = hasLikelyPlayableAction ? 9999 : 130;
                    p.CastHeroPowerModifier.AddOrUpdate(friendAbility, new Modifier(tapMod));
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(tapMod));
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTapLegacy, new Modifier(tapMod));
                    AddLog("英雄技能：" + (string.IsNullOrEmpty(forceDisableLifeTapReason) ? "本回合分流禁用" : forceDisableLifeTapReason)
                        + (tapMod >= 9000 ? "，硬禁用(9999)" : "，软禁用(130)"));
                }
                else if (lowHpAny)
                {
                    p.CastHeroPowerModifier.AddOrUpdate(friendAbility, new Modifier(450));
                    AddLog("英雄技能：血线偏低，降低分流优先级");
                }
                else if (isAggroLikely && lowHpVsAggro)
                {
                    p.CastHeroPowerModifier.AddOrUpdate(friendAbility, new Modifier(350));
                    AddLog("英雄技能：血线危险，降低分流优先级");
                }
                else
                {
                    // 分流作为“兜底行动”，不应压过可执行的关键节奏牌（例如：可下的蛋）。
                    p.CastHeroPowerModifier.AddOrUpdate(friendAbility, new Modifier(120));
                    AddLog("英雄技能：血线安全，但分流仅作兜底(120)");
                }
            }
            catch
            {
                // ignore
            }
        }

        #endregion

        #region 牌表关键卡（与 蛋术.md 一致）
        private const Card.Cards Tomb = Card.Cards.TLC_451;           // 咒怨之墓
        private const Card.Cards Ritual = Card.Cards.CS3_002;         // 末日仪式
        private const Card.Cards Finley = Card.Cards.TSC_908;         // 海中向导芬利爵士
        private const Card.Cards Fracking = Card.Cards.WW_092;        // 液力压裂
        private const Card.Cards ChaosConsume = Card.Cards.TTN_932;   // 混乱吞噬
        private const Card.Cards Burn = Card.Cards.FIR_954;           // 焚烧
        private const Card.Cards CruelGathering = Card.Cards.TRL_249; // 残酷集结
        private const Card.Cards Grimoire = Card.Cards.BAR_910;       // 牺牲魔典
        private const Card.Cards Librarian = Card.Cards.LOOT_014;     // 狗头人图书管理员
        private const Card.Cards MorriganRealm = Card.Cards.CORE_BOT_568; // 莫瑞甘的灵界
        private const Card.Cards DarkPact = Card.Cards.LOOT_017;      // 黑暗契约
        private const Card.Cards Defile = Card.Cards.ICC_041;         // 亵渎
        private const Card.Cards Hellfire = Card.Cards.CORE_CS2_062;  // 地狱烈焰（可能来自发现/临时牌）
        private const Card.Cards DirtyRat = Card.Cards.CORE_CFM_790;  // 卑劣的脏鼠
        private const Card.Cards EatImp = Card.Cards.VAC_939;         // 吃掉小鬼！
        private const Card.Cards Eggbearer = Card.Cards.DINO_411;     // 神圣布蛋者
        private const Card.Cards Egg = Card.Cards.DINO_410;           // 凯洛斯的蛋（阶段1）
        private const Card.Cards EggT2 = Card.Cards.DINO_410t2;       // 凯洛斯的蛋（阶段2）
        private const Card.Cards EggT3 = Card.Cards.DINO_410t3;       // 凯洛斯的蛋（阶段3）
        private const Card.Cards EggT4 = Card.Cards.DINO_410t4;       // 凯洛斯的蛋（阶段4）
        private const Card.Cards EggT5 = Card.Cards.DINO_410t5;       // 凯洛斯的蛋（阶段5，死亡后出20/20）
        private const Card.Cards Khelos = Card.Cards.DINO_410t;       // 凯洛斯（20/20嘲讽）
        private const Card.Cards CorpseOrder = Card.Cards.MAW_002;    // 尸身保护令
        private const Card.Cards Starfish = Card.Cards.TSC_926;       // 掩息海星
        private const Card.Cards SacrificialSummoner = Card.Cards.AV_312; // 献祭召唤者
        private const Card.Cards BoneOoze = Card.Cards.TLC_252;       // 蚀解软泥怪
        private const Card.Cards RaiseDead = Card.Cards.SCH_514;      // 亡者复生
        private const Card.Cards Phylactery = Card.Cards.AV_317;      // 塔姆辛的护命匣
        private const Card.Cards Umbra = Card.Cards.UNG_900;          // 灵魂歌者安布拉
        #endregion

        #region 蛋阶段工具
        private static readonly Card.Cards[] EggStages =
        {
            Egg,
            EggT2,
            EggT3,
            EggT4,
            EggT5,
        };

        private static bool IsEggStage(Card.Cards id)
        {
            return id == Egg || id == EggT2 || id == EggT3 || id == EggT4 || id == EggT5;
        }

        private static int GetEggStageRank(Card.Cards id)
        {
            if (id == EggT5) return 5;
            if (id == EggT4) return 4;
            if (id == EggT3) return 3;
            if (id == EggT2) return 2;
            if (id == Egg) return 1;
            return 0;
        }

        private static Card.Cards GetBestEggOnBoardId(Board board)
        {
            Card.Cards bestEggOnBoardId = Egg;
            try
            {
                var bestEggMinion = board.MinionFriend != null
                    ? board.MinionFriend
                        .Where(x => x != null && x.Template != null && IsEggStage(x.Template.Id))
                        .OrderByDescending(x => GetEggStageRank(x.Template.Id))
                        .FirstOrDefault()
                    : null;
                if (bestEggMinion != null && bestEggMinion.Template != null)
                    bestEggOnBoardId = bestEggMinion.Template.Id;
            }
            catch { bestEggOnBoardId = Egg; }

            return bestEggOnBoardId;
        }

        private static int GetEnemyBoardAttack(Board board)
        {
            try
            {
                if (board == null || board.MinionEnemy == null) return 0;
                int sum = 0;
                foreach (var m in board.MinionEnemy)
                {
                    if (m == null) continue;
                    sum += m.CurrentAtk;
                }
                return sum;
            }
            catch { return 0; }
        }

        private static bool IsEnemyLikelySilenceOrTransform(Board board)
        {
            // 启发式：不读对手具体手牌，只用“公开信息”判断是否处于高概率沉默/变形窗口。
            // 改动点：
            // - 仅按职业会过度保守（萨/法/牧并不总有沉默/变形）。
            // - 加入对手手牌数与下一回合法力门槛，降低“被自己怂死”的概率。
            try
            {
                if (board == null) return false;

                bool classRisk = board.EnemyClass == Card.CClass.SHAMAN
                    || board.EnemyClass == Card.CClass.MAGE
                    || board.EnemyClass == Card.CClass.PRIEST;

                if (!classRisk) return false;

                int enemyHand = 0;
                try { enemyHand = board.EnemyCardCount; } catch { enemyHand = 0; }

                int enemyNextMaxMana = 0;
                try
                {
                    // 估算：对手下一回合最大水晶 = 当前EnemyMaxMana + 1（上限10）
                    enemyNextMaxMana = board.EnemyMaxMana + 1;
                    if (enemyNextMaxMana > 10) enemyNextMaxMana = 10;
                }
                catch { enemyNextMaxMana = 0; }

                // 经验阈值：手牌>=4 且 下一回合maxMana>=4 才视为“窗口”。
                return enemyHand >= 4 && enemyNextMaxMana >= 4;
            }
            catch { return false; }
        }

        private static bool IsEnemyLikelyHandDisruption(Board board)
        {
            // 启发式：按职业判断“更可能携带脏鼠/偷牌/弃牌/手牌破坏”。
            try
            {
                if (board == null) return false;
                return board.EnemyClass == Card.CClass.PRIEST
                    || board.EnemyClass == Card.CClass.WARRIOR
                    || board.EnemyClass == Card.CClass.WARLOCK;
            }
            catch { return false; }
        }

        private static int GetExpectedDrawFromEggPopTool(Card.Cards cardId)
        {
            if (cardId == EatImp) return 3;
            if (cardId == Burn) return 1;
            return 0;
        }

        private static bool WouldOverdrawAfterSequence(int currentHandCount, int cardsPlayedFromHand, int expectedDraw)
        {
            // 简化估算：只防“本回合自己打出+抽牌”导致爆牌。手牌上限 10。
            int after = currentHandCount - cardsPlayedFromHand + expectedDraw;
            return after > 10;
        }


        private static int GetEggPopToolScoreSimple(Card.Cards toolId,
            int myHp, int myHandCount,
            int myMinionCount, int enemyMinionCount, int enemyBoardAttack,
            bool isAggroPressure,
            bool hasUmbraOnBoard)
        {
            // 评分越高越优先。
            // 目标：在不同局面下动态选择“更合适的破蛋手段”，避免固定顺序导致误判。
            int score = 0;

            // ===== 基础偏好（大致按稳定性/收益） =====
            if (toolId == SacrificialSummoner) score += 90;  // 通常最稳（强制吃蛋），但占格子风险后面再扣
            if (toolId == EatImp) score += 80;              // 抽3收益大，但手牌风险已在候选阶段过滤
            if (toolId == DarkPact) score += 70;            // 破蛋+回血，苟命价值高
            if (toolId == Ritual) score += 55;              // 破蛋并可能出5/5（需要场面允许）
            if (toolId == Grimoire) score += 50;            // 破蛋兼清杂
            if (toolId == ChaosConsume) score += 35;        // 需要对面有随从，收益波动
            if (toolId == Burn) score += 25;                // 纯工具，适中
            if (toolId == Defile) score += 20;              // 依赖链，候选阶段已确保可用

            // ===== 血线压力：优先黑暗契约 =====
            if (myHp <= 22 && toolId == DarkPact) score += 60;
            if (enemyBoardAttack >= (myHp - 8) && toolId == DarkPact) score += 40;

            // ===== 手牌溢出：惩罚“占格子/抽牌” =====
            if (myHandCount >= 9 && toolId == EatImp) score -= 40; // 即使没爆牌也容易逼近上限

            // ===== 场面：随从位拥挤时惩罚召唤者 =====
            if (myMinionCount >= 6 && toolId == SacrificialSummoner) score -= 60;

            // ===== 对面铺场：偏好清杂/链类 =====
            if (enemyMinionCount >= 3)
            {
                if (toolId == Grimoire) score += 25;
                if (toolId == Defile) score += 15;
            }

            // ===== 快攻压力：更偏“立刻稳住” =====
            if (isAggroPressure)
            {
                if (toolId == DarkPact) score += 20;
                if (toolId == Grimoire) score += 10;
                if (toolId == EatImp) score -= 10;
            }

            
            // ===== 安布拉在场：优先“低风险、高联动”的破蛋方式（避免占格子） =====
            if (hasUmbraOnBoard)
            {
                // 安布拉回合更偏法术破蛋，保证连锁稳定
                if (toolId == DarkPact) score += 15;
                if (toolId == Ritual) score += 12;
                if (toolId == Grimoire) score += 8;

                // 占格子风险：安布拉+蛋时不太想用召唤者
                if (toolId == SacrificialSummoner) score -= 25;
            }

// ===== 末日仪式做5/5窗口（友方随从5-6且对面压力低） =====
            bool ritual55Window = enemyMinionCount <= 1 && enemyBoardAttack <= 4 && myMinionCount >= 5 && myMinionCount < 7;
            if (toolId == Ritual && ritual55Window) score += 25;

            return score;
        }

        private static bool HasAnyEggPopToolInHand(Board board)
        {
            try
            {
                if (board == null) return false;
                bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Count > 0;
                bool canDefile = board.HasCardInHand(Defile) && HasDefileIncreasingChain(board, 3);
                return board.HasCardInHand(Ritual)
                    || board.HasCardInHand(DarkPact)
                    || board.HasCardInHand(EatImp)
                    || board.HasCardInHand(Grimoire)
                    || board.HasCardInHand(Burn)
                    || (enemyHasMinion && board.HasCardInHand(ChaosConsume))
                    || canDefile
                    || board.HasCardInHand(SacrificialSummoner);
            }
            catch { return false; }
        }
        
        private enum EggPopQuality
        {
            None = 0,
            Risky = 1,
            Normal = 2,
            Perfect = 3
        }

        /// <summary>
        /// 估算“如果本回合先下蛋，是否还能立刻破至少 1 层”，并给出质量分档。
        /// 只用于决策护栏（比如裸下蛋暂停）；不用于硬重写 PlayOrder。
        /// </summary>
        private EggPopQuality EvaluatePopQualityIfPlayEggThisTurn(Board board, int eggCost, int enemyMinions, int friendMinions, out string detail)
        {
            detail = "";
            try
            {
                if (board == null) return EggPopQuality.None;

                int mana = 0;
                try { mana = board.ManaAvailable; } catch { mana = 0; }

                int remain = mana - eggCost;
                if (remain < 0) return EggPopQuality.None;

                int handCountNow = 0;
                try { handCountNow = board.Hand != null ? board.Hand.Count : 0; } catch { handCountNow = 0; }

                // 1) 0费破蛋：Perfect（例如末日仪式被减到0）
                if (board.HasCardInHand(Ritual) && GetMinCostInHand(board, Ritual) == 0) { detail = "Ritual@0"; return EggPopQuality.Perfect; }

                // 2) 常规稳定破蛋：Normal
                if (board.HasCardInHand(DarkPact) && GetMinCostInHand(board, DarkPact) <= remain) { detail = "DarkPact"; return EggPopQuality.Normal; }
                if (board.HasCardInHand(Ritual) && GetMinCostInHand(board, Ritual) <= remain) { detail = "Ritual"; return EggPopQuality.Normal; }
                if (board.HasCardInHand(Grimoire) && GetMinCostInHand(board, Grimoire) <= remain) { detail = "Grimoire"; return EggPopQuality.Normal; }

                // 献祭召唤者：需要预留随从位（下蛋后仍需有位）
                if (board.HasCardInHand(SacrificialSummoner) && GetMinCostInHand(board, SacrificialSummoner) <= remain && friendMinions <= 5)
                { detail = "Summoner"; return EggPopQuality.Normal; }

                // 混乱吞噬：需要对面有随从目标
                if (board.HasCardInHand(ChaosConsume) && GetMinCostInHand(board, ChaosConsume) <= remain && enemyMinions > 0)
                { detail = "ChaosConsume"; return EggPopQuality.Normal; }

                // 3) 风险破蛋：Risky（有副作用或稳定性差）
                if (board.HasCardInHand(EatImp) && GetMinCostInHand(board, EatImp) <= remain && !WouldOverdrawAfterSequence(handCountNow, 2, 3))
                { detail = "EatImp"; return EggPopQuality.Risky; }

                if (board.HasCardInHand(Burn) && GetMinCostInHand(board, Burn) <= remain && !WouldOverdrawAfterSequence(handCountNow, 2, 1))
                { detail = "Burn"; return EggPopQuality.Risky; }

                // 亵渎：需要“下蛋后递增链>=3”
                if (board.HasCardInHand(Defile) && GetMinCostInHand(board, Defile) <= remain && HasDefileIncreasingChainAfterAddingEgg(board, 3))
                { detail = "DefileChain>=3"; return EggPopQuality.Risky; }

                return EggPopQuality.None;
            }
            catch
            {
                return EggPopQuality.None;
            }
        }

        private static int GetMinCostInHand(Board board, Card.Cards cardId)
        {
            try
            {
                if (board == null || board.Hand == null) return 10;
                int best = 10;
                foreach (var c in board.Hand)
                {
                    if (c == null || c.Template == null) continue;
                    if (c.Template.Id != cardId) continue;
                    if (c.CurrentCost < best) best = c.CurrentCost;
                }
                return best;
            }
            catch { return 10; }
        }

#endregion

        private bool RequiresEggTargetForPopTool(Card.Cards c)
        {
            // “只要场上有蛋就锁蛋”：这些破蛋组件必须优先指向最高阶段的蛋，避免误点杂随从导致崩盘
            return c == Ritual
                || c == Grimoire
                || c == DarkPact
                || c == EatImp
                || c == CruelGathering
                || c == Burn;
        }



        #region 英雄能力优先级（芬利）
        private readonly Dictionary<Card.Cards, int> _heroPowersPriorityTable = new Dictionary<Card.Cards, int>
        {
            {LifeTap, 8},
            {LifeTapLegacy, 8},
        };
        #endregion

        #region 敌方随从威胁值（优先解）
        // 说明：
        // - 基础威胁表统一引用公共表（ProfileThreatTables），避免各策略重复维护。
        // - 蛋术的“口径差异/覆盖”在这里追加即可（后写入的同名 key 会覆盖前面的值）。
        private static readonly List<KeyValuePair<Card.Cards, int>> _EnemyMinionsThreatTable =
            new List<KeyValuePair<Card.Cards, int>>(ProfileThreatTables.DefaultEnemyMinionsThreatTable)
            {
                new KeyValuePair<Card.Cards, int>(Card.Cards.GVG_075, 250), // 船载火炮（抢血/滚雪球）
                new KeyValuePair<Card.Cards, int>(Card.Cards.CS2_052, 200), // 空气之怒图腾（持续输出）
                new KeyValuePair<Card.Cards, int>(Starfish, 900), // 掩息海星：对蛋术而言属于“计划崩盘牌”，尽量优先解
            };
        #endregion

        public ProfileParameters GetParameters(Board board)
        {
            _log = "";
            var p = new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = -10 };

            if (ProfileCommon.TryRunPureLearningPlayExecutor(board, p))
                return p;

            // ===== 重新思考（ForcedResimulation） =====
            // 用户口径：每次“非硬币出牌”后都应重算，避免同一套旧评估连续把牌打错目标。
            // 实现：把当前手牌（除幸运币）全量加入 ForcedResimulationCardList。
            try
            {
                if (board != null && board.Hand != null && board.Hand.Count > 0)
                {
                    var excluded = new HashSet<Card.Cards> { TheCoin, Innervate };
                    var unique = new HashSet<Card.Cards>();

                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;
                        var id = c.Template.Id;
                        if (excluded.Contains(id)) continue;
                        if (!unique.Add(id)) continue;

                        p.ForcedResimulationCardList.Add(id);
                    }

                    if (unique.Count > 0)
                        AddLog("重新思考：ForcedResimulation(全量) 覆盖 +" + unique.Count + " 张手牌（除幸运币）");
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                int enemyHpArmor = BoardHelper.GetEnemyHealthAndArmor(board);
                int myHpArmor = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;

                AddLog($"================ 狂野蛋术 决策日志 v{ProfileVersion} ================");
                AddLog($"敌方血甲: {enemyHpArmor} | 我方血甲: {myHpArmor} | 法力:{board.ManaAvailable}/{board.MaxMana} | 手牌:{board.Hand.Count} | 牌库:{board.FriendDeckCount} | 对手:{board.EnemyClass}");

                if (VerboseLog)
                {
                    AddLog("手牌: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
                        .Select(x => $"{(string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN)}({x.Template.Id}){x.CurrentCost}")));
                }

                bool isAggroLikely = board.EnemyClass == Card.CClass.HUNTER
                    || board.EnemyClass == Card.CClass.DEMONHUNTER
                    || board.EnemyClass == Card.CClass.ROGUE
                    || board.EnemyClass == Card.CClass.PALADIN
                    || board.EnemyClass == Card.CClass.SHAMAN;

                bool isComboLikely = board.EnemyClass == Card.CClass.MAGE
                    || board.EnemyClass == Card.CClass.PRIEST
                    || board.EnemyClass == Card.CClass.DRUID;

                AddLog($"[思考] 对局粗分：AggroLikely={isAggroLikely} | ComboLikely={isComboLikely}");

                // 基础攻防倾向：默认偏稳健；对快攻更保守，对控制更激进
                p.GlobalAggroModifier = isAggroLikely ? 50 : 110;
                p.GlobalDefenseModifier = isAggroLikely ? 90 : 30;

                // ===== 状态判定 =====
                // 关键修正：手牌蛋按“蛋阶段”口径识别（DINO_410t2..t5 也算蛋）
                // 否则会出现：手里拿着 t5，却被判定“手牌无蛋”，从而先过牌/分流。
                Card eggInHand = null;
                bool hasEggInHand = false;
                try
                {
                    eggInHand = board.Hand
                        .Where(x => x != null && x.Template != null && IsEggStage(x.Template.Id))
                        .OrderByDescending(x => GetEggStageRank(x.Template.Id))
                        .FirstOrDefault();
                    hasEggInHand = eggInHand != null;
                }
                catch { eggInHand = null; hasEggInHand = false; }
                bool hasEggOnBoard = board.MinionFriend.Any(x => x != null && x.Template != null && IsEggStage(x.Template.Id));
                bool hasEggT5OnBoard = board.MinionFriend.Any(x => x != null && x.Template != null && x.Template.Id == EggT5);
                Card.Cards bestEggOnBoardId = GetBestEggOnBoardId(board);

                bool hasUmbraInHand = board.HasCardInHand(Umbra);
                bool hasUmbraOnBoard = board.HasCardOnBoard(Umbra);
                bool umbraSilencedOnBoard = IsUmbraSilencedOnBoard(board);

                bool hasFinleyInHand = board.HasCardInHand(Finley);
                bool hasEggbearerInHand = board.HasCardInHand(Eggbearer);
                bool hasTombInHand = board.HasCardInHand(Tomb);

                bool hasRitualInHand = board.HasCardInHand(Ritual);
                bool hasDarkPactInHand = board.HasCardInHand(DarkPact);
                bool hasEatImpInHand = board.HasCardInHand(EatImp);
                bool hasCruelGatheringInHand = board.HasCardInHand(CruelGathering);
                bool hasGrimoireInHand = board.HasCardInHand(Grimoire);
                bool hasChaosConsumeInHand = board.HasCardInHand(ChaosConsume);
                bool hasSummonerInHand = board.HasCardInHand(SacrificialSummoner);
                bool hasBurnInHand = board.HasCardInHand(Burn);
                bool hasDefileInHand = board.HasCardInHand(Defile);
                bool canDefileChainNow = hasDefileInHand && HasDefileIncreasingChain(board, 3);

                int eggInGrave = board.FriendGraveyard != null ? board.FriendGraveyard.Count(x => IsEggStage(x)) : 0;

                // 可牺牲目标：场上有任意随从即可（蛋本身也算）
                bool hasAnyFriendlyMinionOnBoard = board.MinionFriend != null && board.MinionFriend.Count > 0;

                // 估算：如果本回合能做到“下蛋+立刻破蛋”，则允许更积极下蛋
                bool canPopEggThisTurnIfPlayed = false;
                if (eggInHand != null)
                {
                    int eggCost = eggInHand.CurrentCost;
                    int mana = board.ManaAvailable;

                    int friendMinions = board.MinionFriend != null ? board.MinionFriend.Count : 0;

                    // 这些都能在“只有蛋作为友方随从”的情况下直接破掉蛋
                    int ritualCost = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == Ritual).Select(x => x.CurrentCost).DefaultIfEmpty(99).Min();
                    int darkPactCost = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == DarkPact).Select(x => x.CurrentCost).DefaultIfEmpty(99).Min();
                    int eatImpCost = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == EatImp).Select(x => x.CurrentCost).DefaultIfEmpty(99).Min();
                    int grimoireCost = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == Grimoire).Select(x => x.CurrentCost).DefaultIfEmpty(99).Min();
                    int summonerCost = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == SacrificialSummoner).Select(x => x.CurrentCost).DefaultIfEmpty(99).Min();
                    int burnCost = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == Burn).Select(x => x.CurrentCost).DefaultIfEmpty(99).Min();
                    int defileCost = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == Defile).Select(x => x.CurrentCost).DefaultIfEmpty(99).Min();

                    bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Count > 0;
                    int chaosConsumeCost = board.Hand.Where(x => x != null && x.Template != null && x.Template.Id == ChaosConsume).Select(x => x.CurrentCost).DefaultIfEmpty(99).Min();

                    if (mana >= eggCost + ritualCost && ritualCost < 99) canPopEggThisTurnIfPlayed = true;
                    if (mana >= eggCost + darkPactCost && darkPactCost < 99) canPopEggThisTurnIfPlayed = true;
                    if (mana >= eggCost + eatImpCost && eatImpCost < 99) canPopEggThisTurnIfPlayed = true;
                    if (mana >= eggCost + grimoireCost && grimoireCost < 99) canPopEggThisTurnIfPlayed = true;
                    if (mana >= eggCost + burnCost && burnCost < 99) canPopEggThisTurnIfPlayed = true;
                    // 献祭召唤者是随从：需要给“下蛋后再下召唤者”预留一个随从位
                    if (friendMinions <= 5 && mana >= eggCost + summonerCost && summonerCost < 99) canPopEggThisTurnIfPlayed = true;
                    if (enemyHasMinion && mana >= eggCost + chaosConsumeCost && chaosConsumeCost < 99) canPopEggThisTurnIfPlayed = true;

                    // 亵渎：仅在“下蛋后”场面血量可满足连续递增链（至少到3）时，才算作可下蛋即破
                    try
                    {
                        bool canDefileChainIfPlayEgg = board.HasCardInHand(Defile) && HasDefileIncreasingChainAfterAddingEgg(board, 3);
                        if (canDefileChainIfPlayEgg && mana >= eggCost + defileCost && defileCost < 99) canPopEggThisTurnIfPlayed = true;
                    }
                    catch { }
                }

                AddLog($"[思考] 蛋：手牌={hasEggInHand} | 场上={hasEggOnBoard} | 坟场蛋次数={eggInGrave} | 本回合可下蛋即破={canPopEggThisTurnIfPlayed}");

                bool hasKeyStarterInHand = hasEggInHand || hasEggbearerInHand || hasUmbraInHand || hasTombInHand;
                bool handIsClogged = board.Hand != null && board.Hand.Count >= 7;

                bool hasKhelosOnBoard = false;
                try
                {
                    hasKhelosOnBoard = board.MinionFriend != null
                        && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Khelos);
                }
                catch { hasKhelosOnBoard = false; }

                // 芬利换手并不会自伤；对慢速/组合对局里，哪怕低血也常常应该先换手找 key。
                // 因此这里不再用“血线>=16”这种过高门槛卡住芬利。
                // 额外口径：若场上已经有凯洛斯(20/20)作为稳定赢点，则不要芬利换手，避免浪费节奏并允许分流作为填充动作。
                bool wantFinleySwap = hasFinleyInHand && !isAggroLikely && !hasKeyStarterInHand && handIsClogged && !hasKhelosOnBoard;
                ProcessCard_TSC_908_Finley(p, board, wantFinleySwap);

                // 非换手回合：禁用芬利，避免把“蛋线组件/关键工具”换回牌库导致节奏崩坏。
                // 用户反馈场景：手里有神圣布蛋者时不应拍芬利。
                if (!wantFinleySwap && hasFinleyInHand)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Finley, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(Finley, new Modifier(-9999));

                    bool hasEggPlanPieces = hasEggInHand || hasEggbearerInHand || hasEggOnBoard
                        || hasTombInHand || hasUmbraInHand || canPopEggThisTurnIfPlayed;
                    AddLog(hasEggPlanPieces
                        ? "[芬利] 非换手回合且手里已有蛋线组件：禁用芬利(9999/-9999)"
                        : "[芬利] 未满足换手条件：禁用芬利(9999/-9999)");
                }

                if (!wantFinleySwap && hasFinleyInHand && handIsClogged && hasKhelosOnBoard)
                    AddLog("[芬利] 场上已有凯洛斯(20/20)：不触发换手，允许用分流补牌");

                // 本回合是否需要“硬禁用分流”（最终会在英雄技能段统一覆盖，避免被后续逻辑冲掉）
                bool forceDisableLifeTap = false;
                string forceDisableLifeTapReason = "";

                // ==================== 敌方随从威胁值（优先解） ====================
                ProfileCommon.ApplyEnemyThreatTable(p, _EnemyMinionsThreatTable);
                ProfileCommon.ApplyDynamicEnemyThreat(board, p, VerboseLog, AddLog);

                // ===== 硬规则：不解对面的“凯洛斯的蛋”系列 =====
                // 口径：这些随从本身通常不是输出来源，优先处理对面其他滚雪球点/直伤点更合理。
                // 实现：当场上出现任意蛋阶段时，将其目标优先级压到极低，避免随从/武器去撞蛋。
                try
                {
                    bool enemyHasEggStage = board.MinionEnemy != null
                        && board.MinionEnemy.Any(m => m != null && m.Template != null && IsEggStage(m.Template.Id));
                    if (enemyHasEggStage)
                    {
                        foreach (var id in EggStages)
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(id, new Modifier(-9999));
                        AddLog("[威胁值] 敌方有凯洛斯的蛋阶段(DINO_410*) => 不攻击不解(-9999)");
                    }
                }
                catch { }

                // ===== 友方随从价值：让 AI 更愿意“牺牲蛋” =====
                // 默认：所有阶段的蛋都偏向作为牺牲目标
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Egg, new Modifier(-800));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT2, new Modifier(-800));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT3, new Modifier(-800));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT4, new Modifier(-800));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT5, new Modifier(-900));

                // 全局护栏：不要送掉安布拉（除非已被沉默）
                if (hasUmbraOnBoard && !umbraSilencedOnBoard)
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Umbra, new Modifier(9000));
                    AddLog("全局护栏：安布拉未被沉默，禁止牺牲安布拉");
                }

                // 咒怨之墓/狗头人：按颜射术口径移植，并在蛋线回合做互斥保护
                int friendMinionsCount = 0;
                try { friendMinionsCount = board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { friendMinionsCount = 0; }
                Func<Card.Cards, bool> canPlayNowMain = id =>
                {
                    try
                    {
                        if (board == null || board.Hand == null) return false;
                        var c = board.Hand
                            .Where(x => x != null && x.Template != null && x.Template.Id == id)
                            .OrderBy(x => x.CurrentCost)
                            .FirstOrDefault();
                        return c != null && c.CurrentCost <= board.ManaAvailable;
                    }
                    catch
                    {
                        return false;
                    }
                };
                bool hasSacrificeSpellPlayableNow =
                    (hasRitualInHand && canPlayNowMain(Ritual))
                    || (hasDarkPactInHand && canPlayNowMain(DarkPact))
                    || (hasEatImpInHand && canPlayNowMain(EatImp))
                    || (hasGrimoireInHand && canPlayNowMain(Grimoire))
                    || (hasCruelGatheringInHand && canPlayNowMain(CruelGathering))
                    || (hasChaosConsumeInHand && canPlayNowMain(ChaosConsume) && board.MinionEnemy != null && board.MinionEnemy.Count > 0)
                    || (hasBurnInHand && canPlayNowMain(Burn))
                    || (hasDefileInHand && canDefileChainNow && canPlayNowMain(Defile));
                bool hasSummonerPlayableNow = hasSummonerInHand && friendMinionsCount <= 6 && canPlayNowMain(SacrificialSummoner);
                bool hasEggBreakerForExistingEgg = hasEggOnBoard && (hasSacrificeSpellPlayableNow || hasSummonerPlayableNow);
                // 提前推断“牌库仍有蛋”与布蛋者可用性，用于墓的互斥判断
                bool eggStillInDeck = false;
                try
                {
                    // 1 张构筑推断：手/场/坟都没有蛋阶段 => 仍在牌库（或被烧等极少数异常）
                    eggStillInDeck = !hasEggInHand && !hasEggOnBoard && eggInGrave == 0;
                }
                catch { eggStillInDeck = false; }

                bool eggbearerPlayableNow = false;
                try
                {
                    if (hasEggbearerInHand && board != null && board.Hand != null)
                    {
                        var eb = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Eggbearer);
                        if (eb != null) eggbearerPlayableNow = board.ManaAvailable >= eb.CurrentCost;
                    }
                }
                catch { eggbearerPlayableNow = false; }

                ProcessCard_TLC_451_CurseTomb(p, board, wantFinleySwap, hasEggOnBoard, canPopEggThisTurnIfPlayed,
                    hasEggBreakerForExistingEgg, hasEggInHand, eggStillInDeck, hasEggbearerInHand, eggbearerPlayableNow);
                ProcessCard_LOOT_014_KoboldLibrarian(p, board, wantFinleySwap, hasEggInHand, hasEggOnBoard, canPopEggThisTurnIfPlayed,
                    isAggroLikely, isComboLikely, ref forceDisableLifeTap, ref forceDisableLifeTapReason);

                ProcessCard_WW_092_Fracking(p, board, wantFinleySwap, isAggroLikely, hasEggInHand, hasEggOnBoard, canPopEggThisTurnIfPlayed,
                    hasEggBreakerForExistingEgg,
                    ref forceDisableLifeTap, ref forceDisableLifeTapReason);

                ProcessCard_DINO_411_Eggbearer(p, board, hasEggbearerInHand, hasEggOnBoard, hasEggInHand, canPopEggThisTurnIfPlayed, eggStillInDeck);

                ProcessCard_DINO_410_Egg(p, board, eggInHand, hasEggOnBoard, canPopEggThisTurnIfPlayed, hasUmbraInHand,
                    isAggroLikely, isComboLikely,
                    ref forceDisableLifeTap, ref forceDisableLifeTapReason);

                // 若蛋已经在场：积极“破蛋”，让自杀法术更愿意交出来
                ProcessEggPopToolsWhenEggOnBoard(p, board,
                    hasEggOnBoard, hasEggT5OnBoard,
                    hasUmbraOnBoard, hasUmbraInHand,
                    hasRitualInHand, hasDarkPactInHand, hasEatImpInHand, hasCruelGatheringInHand, hasGrimoireInHand,
                    hasChaosConsumeInHand, eggInGrave,
                    ref forceDisableLifeTap, ref forceDisableLifeTapReason);

                if (!hasEggOnBoard)
                {
                    // 保命例外：场上无蛋时，通常禁用触发组件。
                    // 但若血线危险且有可牺牲目标，允许黑暗契约先回血。
                    bool allowEmergencyDarkPactNoEgg = false;
                    Card.Cards darkPactEmergencyTarget = SacrificialSummoner;
                    try
                    {
                        int enemyAtkNow = GetEnemyBoardAttack(board);
                        bool hpDanger = myHpArmor <= 14 || enemyAtkNow >= Math.Max(5, myHpArmor - 4);

                        Card.Cards target = SacrificialSummoner;
                        bool hasTarget = false;
                        if (board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == SacrificialSummoner))
                        {
                            target = SacrificialSummoner;
                            hasTarget = true;
                        }
                        else if (board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Eggbearer))
                        {
                            target = Eggbearer;
                            hasTarget = true;
                        }
                        else if (board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Librarian))
                        {
                            target = Librarian;
                            hasTarget = true;
                        }
                        else if (board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Finley))
                        {
                            target = Finley;
                            hasTarget = true;
                        }
                        else
                        {
                            var fallback = board.MinionFriend != null
                                ? board.MinionFriend.FirstOrDefault(m => m != null && m.Template != null && (m.Template.Id != Umbra || umbraSilencedOnBoard))
                                : null;
                            if (fallback != null && fallback.Template != null)
                            {
                                target = fallback.Template.Id;
                                hasTarget = true;
                            }
                        }

                        if (hasDarkPactInHand && hpDanger && hasTarget)
                        {
                            allowEmergencyDarkPactNoEgg = true;
                            darkPactEmergencyTarget = target;
                        }
                    }
                    catch
                    {
                        allowEmergencyDarkPactNoEgg = false;
                        darkPactEmergencyTarget = SacrificialSummoner;
                    }

                    // 用户硬规则：触发组件尽量只对蛋使用；场上无蛋时一律禁用，避免乱吃杂随从。
                    p.CastSpellsModifiers.AddOrUpdate(Ritual, new Modifier(9999, bestEggOnBoardId));
                    p.PlayOrderModifiers.AddOrUpdate(Ritual, new Modifier(-9999));
                    AddLog(CardName(Ritual) + " 9999（场上无蛋：禁用末日仪式，触发组件仅对蛋使用）");

                    if (allowEmergencyDarkPactNoEgg)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(DarkPact, new Modifier(-420, darkPactEmergencyTarget));
                        p.PlayOrderModifiers.AddOrUpdate(DarkPact, new Modifier(2850));
                        AddLog(CardName(DarkPact) + " -420 | 顺序2850（场上无蛋：低血保命例外，允许牺牲" + CardName(darkPactEmergencyTarget) + "回血）");
                    }
                    else
                    {
                        p.CastSpellsModifiers.AddOrUpdate(DarkPact, new Modifier(9999, bestEggOnBoardId));
                        p.PlayOrderModifiers.AddOrUpdate(DarkPact, new Modifier(-9999));
                        AddLog(CardName(DarkPact) + " 9999（场上无蛋：禁用黑暗契约，触发组件仅对蛋使用）");
                    }

                    p.CastSpellsModifiers.AddOrUpdate(EatImp, new Modifier(9999, bestEggOnBoardId));
                    p.PlayOrderModifiers.AddOrUpdate(EatImp, new Modifier(-9999));
                    AddLog(CardName(EatImp) + " 9999（场上无蛋：禁用吃掉小鬼！，触发组件仅对蛋使用）");

                    p.CastSpellsModifiers.AddOrUpdate(Grimoire, new Modifier(9999, bestEggOnBoardId));
                    p.PlayOrderModifiers.AddOrUpdate(Grimoire, new Modifier(-9999));
                    AddLog(CardName(Grimoire) + " 9999（场上无蛋：禁用牺牲魔典，触发组件仅对蛋使用）");

                    p.CastSpellsModifiers.AddOrUpdate(CruelGathering, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(CruelGathering, new Modifier(-9999));
                    AddLog(CardName(CruelGathering) + " 9999（场上无蛋：禁用残酷集结，触发组件仅对蛋使用）");

                    // 混乱吞噬有时可用来牺牲小随从换关键解场（对快攻），不做过度禁用
                    if (isAggroLikely && hasAnyFriendlyMinionOnBoard && board.MinionEnemy.Count > 0)
                        p.CastSpellsModifiers.AddOrUpdate(ChaosConsume, new Modifier(-50));
                    else
                        p.CastSpellsModifiers.AddOrUpdate(ChaosConsume, new Modifier(150));
                }

                ProcessCard_UNG_900_Umbra(p, board, hasEggInHand, hasEggOnBoard, eggInGrave);

                ProcessCard_MAW_002_CorpseOrder(p, board, eggInGrave, hasEggOnBoard);

                bool shouldPrioritizeEggNow = false;
                try
                {
                    // 口径：慢局/组合对局里，只要手里有可打的蛋，就尽量先把蛋拍下去（避免被亡者复生等抢动作）
                    if (hasEggInHand && !hasEggOnBoard && eggInHand != null)
                    {
                        int eggCostNow = eggInHand.CurrentCost;
                        bool eggPlayableNow = board.ManaAvailable >= eggCostNow;
                        shouldPrioritizeEggNow = eggPlayableNow && !isAggroLikely && (isComboLikely || board.ManaAvailable >= 3);
                    }
                }
                catch { shouldPrioritizeEggNow = false; }

                ProcessCard_SCH_514_RaiseDead(p, board, wantFinleySwap, eggInGrave, shouldPrioritizeEggNow);

                ProcessCard_AV_317_Phylactery(p, board, eggInGrave, hasAnyFriendlyMinionOnBoard);

                // ===== 献祭召唤者 / 蚀解软泥怪：更偏向“有低费献祭素材”时使用 =====
                if (board.HasCardInHand(SacrificialSummoner) || board.HasCardInHand(BoneOoze))
                {
                    bool hasOneCostFodderOnBoard = board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Cost == 1);
                    bool hasTwoCostFodderOnBoard = board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Cost == 2);

                    // 用户口径：献祭召唤者只对“蛋”使用。
                    // 因此：场上没蛋阶段时直接禁用，避免吃布蛋者/杂随从。
                    if (board.HasCardInHand(SacrificialSummoner))
                    {
                        if (!hasEggOnBoard)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(9999));
                            p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(-9999));
                            AddLog(CardName(SacrificialSummoner) + " 9999（场上无蛋：禁用献祭召唤者，避免吃杂随从）");
                        }
                        else
                        {
                            // 场上有蛋：把献祭召唤者作为破蛋手段之一
                            // 关键修正：不要在这里把“最优先召唤者破蛋(-950/2700)”降级成 -420，避免被魔典等手段抢先。
                            int friendMinionsNow = 0;
                            try { friendMinionsNow = board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { friendMinionsNow = 0; }

                            // 若同时处于“安布拉+蛋且随从位拥挤”场景，则破蛋延后由专门护栏处理，这里不再覆盖其顺序/优先级。
                            bool crowdedUmbraEgg = false;
                            try { crowdedUmbraEgg = hasUmbraOnBoard && friendMinionsNow >= 6; } catch { crowdedUmbraEgg = false; }

                            if (!crowdedUmbraEgg)
                            {
                                bool umbraStillInDeckLocal = false;
                                try { umbraStillInDeckLocal = IsUmbraStillInDeck(board, hasUmbraInHand, hasUmbraOnBoard); } catch { umbraStillInDeckLocal = false; }

                                bool bestSummoner = friendMinionsNow <= 5 && umbraStillInDeckLocal;
                                bool hasOtherEggBreakerNowExcludingSummoner =
                                    (hasRitualInHand && canPlayNowMain(Ritual))
                                    || (hasDarkPactInHand && canPlayNowMain(DarkPact))
                                    || (hasEatImpInHand && canPlayNowMain(EatImp))
                                    || (hasCruelGatheringInHand && canPlayNowMain(CruelGathering))
                                    || (hasGrimoireInHand && canPlayNowMain(Grimoire))
                                    || (hasChaosConsumeInHand && canPlayNowMain(ChaosConsume) && board.MinionEnemy != null && board.MinionEnemy.Count > 0)
                                    || (hasBurnInHand && canPlayNowMain(Burn))
                                    || (hasDefileInHand && canDefileChainNow && canPlayNowMain(Defile));

                                int summonerMod = bestSummoner ? -1200 : -520;
                                int summonerOrder = bestSummoner ? 3400 : 2600;

                                if (!umbraStillInDeckLocal && hasOtherEggBreakerNowExcludingSummoner)
                                {
                                    summonerMod = 220;
                                    summonerOrder = -250;
                                }

                                p.CastMinionsModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(summonerMod));
                                p.PlayOrderModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(summonerOrder));
                                if (bestSummoner)
                                    AddLog(CardName(SacrificialSummoner) + " -1200 | 顺序3400（场上有蛋且牌库有安布拉：献祭召唤者硬优先破蛋）");
                                else if (!umbraStillInDeckLocal && hasOtherEggBreakerNowExcludingSummoner)
                                    AddLog(CardName(SacrificialSummoner) + " 220 | 顺序-250（场上有蛋且牌库无安布拉：让位其它破蛋组件）");
                            }

                            // 强制目标：优先吃蛋阶段，避免误吃其他组件
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Egg, new Modifier(-5000));
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT2, new Modifier(-5000));
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT3, new Modifier(-5000));
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT4, new Modifier(-5000));
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(EggT5, new Modifier(-5000));
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Eggbearer, new Modifier(1800));
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Librarian, new Modifier(1200));
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Finley, new Modifier(1200));
                        }
                    }

                    // 蚀解软泥怪：用户口径 —— 只对“凯洛斯(DINO_410t)”使用（一般用于叫杀）。
                    // 说明：如果不做硬限制，AI 会把它用在布蛋者/杂鱼上，收益极差。
                    bool hasKhelosOnBoardLocal = false;
                    try
                    {
                        hasKhelosOnBoardLocal = board.MinionFriend != null
                            && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Khelos);
                    }
                    catch { hasKhelosOnBoardLocal = false; }

                    bool hasAttackerNow = false;
                    try
                    {
                        hasAttackerNow = board.MinionFriend != null
                            && board.MinionFriend.Any(x => x != null && x.Template != null && x.CanAttack);
                    }
                    catch { hasAttackerNow = false; }

                    if (board.HasCardInHand(BoneOoze))
                    {
                        if (hasKhelosOnBoardLocal && hasAttackerNow)
                        {
                            // 强制目标：让 AI 明确“只吃凯洛斯”。
                            // 注意：这会影响同回合其它牺牲效果的目标选择，因此同时压住其它牺牲组件，先把软泥怪打出去。
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Khelos, new Modifier(-5000));

                            p.CastMinionsModifiers.AddOrUpdate(BoneOoze, new Modifier(-280));
                            p.PlayOrderModifiers.AddOrUpdate(BoneOoze, new Modifier(2700));

                            // 压住其它牺牲/破蛋手段，避免它们因 Khelos 价值被拉低而误吃凯洛斯
                            p.CastSpellsModifiers.AddOrUpdate(DarkPact, new Modifier(450, bestEggOnBoardId));
                            p.CastSpellsModifiers.AddOrUpdate(EatImp, new Modifier(450, bestEggOnBoardId));
                            p.CastSpellsModifiers.AddOrUpdate(Ritual, new Modifier(450, bestEggOnBoardId));
                            p.CastSpellsModifiers.AddOrUpdate(Grimoire, new Modifier(450, bestEggOnBoardId));
                            p.CastSpellsModifiers.AddOrUpdate(ChaosConsume, new Modifier(450));
                            p.CastMinionsModifiers.AddOrUpdate(SacrificialSummoner, new Modifier(450));

                            AddLog(CardName(BoneOoze) + " -280 | 顺序2700（仅对凯洛斯使用：叫杀用。临时压住其它牺牲效果防误吃）");
                        }
                        else
                        {
                            p.CastMinionsModifiers.AddOrUpdate(BoneOoze, new Modifier(9999));
                            p.PlayOrderModifiers.AddOrUpdate(BoneOoze, new Modifier(-9999));
                            AddLog(CardName(BoneOoze) + " 9999（只对凯洛斯使用：当前无凯洛斯或无可攻击随从）");
                        }
                    }
                }

                ProcessCard_TSC_926_Starfish(p, board, hasEggOnBoard, wantFinleySwap);

                ProcessCard_ICC_041_Defile(p, board, isAggroLikely, myHpArmor, hasEggOnBoard);

                ProcessCard_CORE_CS2_062_Hellfire(p, board, isAggroLikely, myHpArmor, hasEggOnBoard, hasUmbraOnBoard);

                ProcessCard_CORE_CFM_790_DirtyRat(p, board, isComboLikely);

                ProcessHeroPower_LifeTap(p, board, wantFinleySwap, forceDisableLifeTap, forceDisableLifeTapReason, isAggroLikely);

                Bot.Log(_log);
                ProfileCommon.ApplyLiveMemoryBias(board, p);
                return p;
            }
            catch (Exception e)
            {
                try
                {
                    Bot.Log($"[狂野蛋术] Profile异常已捕获，已回退默认策略。异常: {e}");
                    if (!string.IsNullOrEmpty(_log))
                        Bot.Log(_log);
                }
                catch
                {
                    // ignore
                }

                return new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = -10 };
            }
        }

        private void AddLog(string log)
        {
            ProfileCommon.AddLog(ref _log, log);
        }

        private static string CardName(Card.Cards id)
        {
            return ProfileCommon.CardName(id);
        }

        #region SmartBot 单文件编译兼容（内置公共工具/威胁表）
        // 说明：部分 SmartBot 环境会“按单文件编译每个 Profile”，导致同目录下的工具类文件不可见。
        // 因此在需要时，将公共工具/数据表内置到 Profile 内部，避免出现“does not exist in current context”。

        private static class ProfileCommon
        {
            public static bool TryRunPureLearningPlayExecutor(Board board, ProfileParameters p)
            {
                try
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var executorType = assembly.GetType("SmartBotProfiles.DecisionPlayExecutor", false);
                        if (executorType == null)
                            continue;

                        var method = executorType.GetMethod(
                            "TryRunPureLearningPlayExecutor",
                            new[] { typeof(Board), typeof(ProfileParameters) });
                        if (method == null)
                            continue;

                        object result = method.Invoke(null, new object[] { board, p });
                        return result is bool && (bool)result;
                    }
                }
                catch
                {
                    // ignore
                }

                return false;
            }

            public static void AddLog(ref string log, string line)
            {
                if (log == null) log = "";
                if (log.Length > 0)
                    log += "\r\n";
                log += line;
            }

            public static string CardName(Card.Cards id)
            {
                try
                {
                    var t = CardTemplate.LoadFromId(id);
                    var cn = t != null ? t.NameCN : null;
                    if (!string.IsNullOrWhiteSpace(cn))
                        return cn + "(" + id + ")";
                }
                catch
                {
                    // ignore
                }

                return id.ToString();
            }

            public static Card.Cards GetFriendAbilityId(Board board, Card.Cards fallback)
            {
                try
                {
                    if (board != null && board.Ability != null && board.Ability.Template != null)
                        return board.Ability.Template.Id;
                }
                catch
                {
                    // ignore
                }

                return fallback;
            }

            public static void ApplyEnemyThreatTable(ProfileParameters p, IEnumerable<KeyValuePair<Card.Cards, int>> table)
            {
                if (p == null || table == null) return;

                foreach (var kv in table)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(kv.Key, new Modifier(kv.Value));
            }

            public static void ApplyDynamicEnemyThreat(Board board, ProfileParameters p, bool enableLogs, Action<string> addLog)
            {
                if (board == null || p == null || board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                    return;

                foreach (var m in board.MinionEnemy)
                {
                    if (m == null || m.Template == null) continue;

                    if (m.IsTaunt)
                    {
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(m.Template.Id, new Modifier(150));
                        if (enableLogs && addLog != null)
                            addLog("[威胁] " + CardName(m.Template.Id) + " 嘲讽优先解");
                    }

                    if (m.CurrentAtk >= 6)
                    {
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(m.Template.Id, new Modifier(80));
                        if (enableLogs && addLog != null)
                            addLog("[威胁] " + CardName(m.Template.Id) + " 高攻" + m.CurrentAtk + "点（优先控场）");
                    }
                }
            }

            public static bool ApplyLiveMemoryBias(Board board, ProfileParameters p)
            {
                return false;
            }
        }

        private static class ProfileThreatTables
        {
            public static readonly List<KeyValuePair<Card.Cards, int>> DefaultEnemyMinionsThreatTable = new List<KeyValuePair<Card.Cards, int>>
            {
                new KeyValuePair<Card.Cards, int>(Card.Cards.VAC_927, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.VAC_938, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.ETC_355, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.WW_091, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.MIS_026, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.CORE_WON_065, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.WW_357, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.DEEP_999t2, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.CFM_039, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.WON_365, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.WW_364t, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TSC_026t, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.JAM_028, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.WW_415, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.CS3_014, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.YOG_516, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.NX2_033, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.JAM_004, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_330, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_729, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_812, 150),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_479, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_732, 250),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_466, 250),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_801, 350),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_833, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_730, 300),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_920, 300),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_856, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_907, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_071, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_078, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_843, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_415, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_960, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_800, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_721, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_429, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_858, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_075, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_092, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_903, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_862, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_737, 600),
                // 修正：TTN_800/TTN_415 维持 600（避免后续重复项误覆盖成低值）
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_800, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.ETC_355, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_415, 600),
                new KeyValuePair<Card.Cards, int>(Card.Cards.ETC_541, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.CORE_LOOT_231, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.ETC_339, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.ETC_833, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.NX2_006, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.ETC_105, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.ETC_522, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.RLK_121, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.RLK_539, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.RLK_061, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.RLK_824, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.CORE_EX1_012, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.TSC_074, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.RLK_607, 200),
                new KeyValuePair<Card.Cards, int>(Card.Cards.RLK_924, 200),
            };
        }
        #endregion

        // 芬利·莫格顿爵士技能选择
        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            if (choices == null || choices.Count == 0)
                return LifeTap;

            var filteredTable = _heroPowersPriorityTable.Where(x => choices.Contains(x.Key)).ToList();
            if (filteredTable.Count == 0)
                return choices[0];

            return filteredTable.First(x => x.Value == filteredTable.Max(y => y.Value)).Key;
        }

        // 卡扎库斯选择
        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : LifeTap;
        }

        // 保留必要的 BoardHelper（只用到血甲推断）
        public static class BoardHelper
        {
            public static int GetEnemyHealthAndArmor(Board board)
            {
                if (board == null || board.HeroEnemy == null) return 0;
                return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
            }
        }
    }
}
