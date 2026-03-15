using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API;
using SmartBotAPI.Battlegrounds;
using SmartBot.Plugins.API.Actions;

namespace SmartBotProfiles
{
    /// <summary>
    /// 狂野颜射术 - 重构版
    /// 说明：将所有相同卡牌的逻辑集中到一个区域，按顺序判断避免冲突
    /// </summary>
    [Serializable]
    public class WildYanSheWarlockRefactored : Profile
    {
        // 策略版本号：格式为 日期.总修改次数（总修改次数不重置）
        // 留痕规则：每次代码改动必须同步更新 ProfileVersion，并在“卡组攻略和修改记录/狂野/颜射术/日志.md”追加同版本变更记录。
        private const string ProfileVersion = "2026-02-14.806";
        private bool lethalThisTurn = false;

        #region 全局变量缓存
        // ... 保留原有的全局变量 ...
        #endregion

        #region 辅助方法 - 见文件底?有实?
        // GetTag, IsTemporaryCard, GetDiscardComponentsConsideringHand 等方法已在文件后定义
        #endregion

        #region 卡牌处理方法

        private bool IsDreadCorsairCard(Card card)
        {
            if (card == null || card.Template == null) return false;
            try
            {
                if (card.Template.Id == Card.Cards.CORE_NEW1_022) return true;
            }
            catch { }

            try
            {
                var idText = card.Template.Id.ToString();
                if (!string.IsNullOrEmpty(idText)
                    && idText.IndexOf("NEW1_022", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch { }

            try
            {
                var nameCn = card.Template.NameCN;
                if (!string.IsNullOrEmpty(nameCn) && nameCn == "恐怖海盗") return true;
            }
            catch { }

            return false;
        }

        private List<Card> GetDreadCorsairCardsInHand(Board board)
        {
            try
            {
                if (board == null || board.Hand == null) return new List<Card>();
                return board.Hand.Where(IsDreadCorsairCard).ToList();
            }
            catch
            {
                return new List<Card>();
            }
        }

        private int CountDreadCorsairInHand(Board board)
        {
            try { return GetDreadCorsairCardsInHand(board).Count; }
            catch { return 0; }
        }

        /// <summary>
        /// 兼容版：魂火吸血威胁放行判定（放在文件前段，避免特定编译器在长文件末段丢失符号）。
        /// </summary>
        private bool ShouldAllowSoulfireVsLifestealThreatCompat(
            Board board,
            out string reason,
            out int preferredEnemyTargetId)
        {
            reason = null;
            preferredEnemyTargetId = 0;

            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;

                bool hasPlayableSoulfire = board.Hand.Any(c => c != null
                    && c.Template != null
                    && IsSoulfireCardVariant(c)
                    && c.CurrentCost <= board.ManaAvailable);
                if (!hasPlayableSoulfire) return false;

                var enemyMinions = board.MinionEnemy != null
                    ? board.MinionEnemy.Where(m => m != null).ToList()
                    : new List<Card>();
                if (enemyMinions.Count == 0) return false;

                var killableLifesteal = enemyMinions
                    .Where(m => m != null && m.IsLifeSteal && m.CurrentHealth <= 4)
                    .OrderByDescending(m => m.IsTaunt ? 1 : 0)
                    .ThenByDescending(m => Math.Max(0, m.CurrentAtk))
                    .ThenByDescending(m => Math.Max(0, m.CurrentHealth))
                    .FirstOrDefault();
                if (killableLifesteal == null) return false;

                int myHpArmor = 0;
                try
                {
                    myHpArmor = board.HeroFriend != null
                        ? Math.Max(0, board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor)
                        : 0;
                }
                catch { myHpArmor = 0; }

                bool hasMultiLifestealThreat = false;
                try { hasMultiLifestealThreat = enemyMinions.Count(m => m != null && m.IsLifeSteal) >= 2; } catch { hasMultiLifestealThreat = false; }

                bool shouldForceClear = myHpArmor <= 24
                    || killableLifesteal.IsTaunt
                    || killableLifesteal.CurrentAtk >= 4
                    || hasMultiLifestealThreat;
                if (!shouldForceClear) return false;

                preferredEnemyTargetId = killableLifesteal.Id;
                reason = "敌方吸血威胁可被魂火击杀(id=" + killableLifesteal.Id
                    + ",攻/血=" + killableLifesteal.CurrentAtk + "/" + killableLifesteal.CurrentHealth
                    + ",Taunt=" + (killableLifesteal.IsTaunt ? "Y" : "N") + ")";
                return true;
            }
            catch
            {
                reason = null;
                preferredEnemyTargetId = 0;
                return false;
            }
        }

        // 统一“规则集硬禁用(>=9000)”判断，避免局部 lambda 作用域问题导致编译失败。
        private bool isDisabledInRulesSet(RulesSet rules, Card.Cards cardId, int instanceId)
        {
            try
            {
                if (rules == null) return false;

                Rule r = null;
                try { r = rules.RulesCardIds != null ? rules.RulesCardIds[cardId] : null; } catch { r = null; }
                if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                try { r = rules.RulesIntIds != null ? rules.RulesIntIds[(int)cardId] : null; } catch { r = null; }
                if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                try { r = rules.RulesIntIds != null ? rules.RulesIntIds[instanceId] : null; } catch { r = null; }
                if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;
            }
            catch { }
            return false;
        }

        private bool isDisabledInRulesSet(RulesSet rules, Card.Cards cardId)
        {
            return isDisabledInRulesSet(rules, cardId, (int)cardId);
        }

        private bool isDisabledInRulesSet(RulesSet rules, Card card)
        {
            try
            {
                if (card == null || card.Template == null) return false;
                return isDisabledInRulesSet(rules, card.Template.Id, card.Id);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 统一处理某张＄的所有?昏緫鍒?柇
        /// </summary>
        private void ProcessCard_END_016_TimeWarpClaw(ProfileParameters p, Board board, 
            bool hasDiscardComponentAtMax, bool canTapNow, bool hasNoDrawLeft, 
            bool lethalThisTurn, int enemyHp, int maxCostInHand, bool merchantCanSacrifice, bool hasOtherRealActions,
            HashSet<Card.Cards> discardComponents)
        {
            // ==============================================
            // Card.Cards.END_016 - 鏃剁?之爪（武器）
            // 说明：所有关于时空之爪的?都在这里，按优先级高到?
            // ==============================================
            
            bool hasClawInHand = board.HasCardInHand(Card.Cards.END_016);
            bool hasClawEquipped = board.WeaponFriend != null 
                && board.WeaponFriend.Template != null 
                && board.WeaponFriend.Template.Id == Card.Cards.END_016
                && board.WeaponFriend.CurrentDurability > 0;
            int equippedClawDurability = 0;
            try { equippedClawDurability = hasClawEquipped ? board.WeaponFriend.CurrentDurability : 0; } catch { equippedClawDurability = 0; }
            Card clawInHandCard = null;
            bool canReequipClawNow = false;
            bool reEquipOpensDiscardAtMax = false;
            bool reEquipHasDiscardAtThree = false;
            int reEquipNextMaxCost = 0;
            try
            {
                if (hasClawInHand && board.Hand != null)
                {
                    clawInHandCard = board.Hand.FirstOrDefault(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.END_016
                        && c.CurrentCost <= board.ManaAvailable);
                }

                canReequipClawNow = hasClawEquipped
                    && equippedClawDurability <= 1
                    && clawInHandCard != null;

                if (canReequipClawNow)
                {
                    var discardSetForReequip = discardComponents != null && discardComponents.Count > 0
                        ? new HashSet<Card.Cards>(discardComponents)
                        : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));

                    var handAfterReequip = GetHandCardsForDiscardLogic(board)
                        .Where(h => h != null && h.Template != null && h.Id != clawInHandCard.Id)
                        .ToList();

                    if (handAfterReequip.Count > 0)
                    {
                        reEquipNextMaxCost = handAfterReequip.Max(h => h.CurrentCost);
                        reEquipOpensDiscardAtMax = handAfterReequip.Any(h => h.CurrentCost == reEquipNextMaxCost
                            && discardSetForReequip.Contains(h.Template.Id));
                        reEquipHasDiscardAtThree = handAfterReequip.Any(h => h.CurrentCost == 3
                            && discardSetForReequip.Contains(h.Template.Id));
                    }
                }
            }
            catch
            {
                canReequipClawNow = false;
                reEquipOpensDiscardAtMax = false;
                reEquipHasDiscardAtThree = false;
                reEquipNextMaxCost = 0;
            }
            bool heroCanAttackWithClaw = false;
            try
            {
                heroCanAttackWithClaw = hasClawEquipped
                    && board.HeroFriend != null
                    && (board.HeroFriend.CanAttack
                        || (!board.HeroFriend.IsFrozen && board.HeroFriend.CountAttack == 0));
            }
            catch
            {
                heroCanAttackWithClaw = hasClawEquipped
                    && board.HeroFriend != null
                    && !board.HeroFriend.IsFrozen;
            }
            int handCountForClawRule = 0;
            try { handCountForClawRule = board.Hand != null ? board.Hand.Count : 0; } catch { handCountForClawRule = 0; }
            bool emptyHandForClawRule = handCountForClawRule <= 0;
            var highestCards = GetHighestCostCardsInHand(board, maxCostInHand);
            bool highestOnlyClaw = AreAllHighestCards(highestCards, Card.Cards.END_016);
            bool highestOnlySoulBarrage = AreAllHighestCards(highestCards, Card.Cards.RLK_534);
            bool highestSoulBarrageAlreadyCheap = false;
            try
            {
                highestSoulBarrageAlreadyCheap = highestOnlySoulBarrage
                    && highestCards != null
                    && highestCards.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.RLK_534
                        && h.CurrentCost <= 1);
            }
            catch { highestSoulBarrageAlreadyCheap = false; }
            bool coyoteUnlocksDiscardWindow = false;
            try { coyoteUnlocksDiscardWindow = CanCoyoteUnlockClawDiscardWindow(board, discardComponents); } catch { coyoteUnlocksDiscardWindow = false; }
            bool felwingUnlocksDiscardWindow = false;
            try { felwingUnlocksDiscardWindow = CanFelwingUnlockClawDiscardWindow(board, discardComponents); } catch { felwingUnlocksDiscardWindow = false; }
            bool faceDiscountUnlocksDiscardWindow = coyoteUnlocksDiscardWindow || felwingUnlocksDiscardWindow;

            // 用户口径补充：
            // 已装备时空之爪时，若“手牌地标可直接拍出”且“当前最高费命中被弃组件”，
            // 应先拍手牌地标，再考虑挥刀，避免先挥刀把手牌地标误弃掉。
            bool canPlayCaveFromHandNowForClaw = false;
            try
            {
                canPlayCaveFromHandNowForClaw = board.Hand != null
                    && GetFriendlyBoardSlotsUsed(board) < 7
                    && board.Hand.Any(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.WON_103
                        && c.CurrentCost <= board.ManaAvailable);
            }
            catch { canPlayCaveFromHandNowForClaw = false; }

            if (!lethalThisTurn
                && hasClawEquipped
                && hasDiscardComponentAtMax
                && canPlayCaveFromHandNowForClaw)
            {
                try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9999)); } catch { }
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2200)); } catch { }
                try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2200)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9800)); } catch { }
                try
                {
                    foreach (var cave in board.Hand.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103))
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(cave.Id, new Modifier(-2200)); } catch { }
                        try { p.LocationsModifiers.AddOrUpdate(cave.Id, new Modifier(-2200)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(cave.Id, new Modifier(9800)); } catch { }
                    }
                }
                catch { }

                p.ForceResimulation = true;
                AddLog("[时空之爪-让位手牌地标] 手牌地标可拍且最高费命中被弃组件 => 暂缓挥刀(9999)，先拍地标(-2200/9800)");
                return;
            }

            // 修复：board.HeroFriend.CanAttack 鍦?分时序下会误?负 N，导致?禁攻?规则”不生效而出现莫名其妙去 A銆?
            // 鍙ｅ：只要?已装备爪子】且【最高费不含被弃组件】且【非?】，无论 CanAttack 鍙ｅ如何，都应强制禁攻??
            if (hasClawEquipped && !lethalThisTurn && !hasDiscardComponentAtMax && !emptyHandForClawRule)
            {
                bool forceReequipByOnlyClawNoDraw = canReequipClawNow
                    && highestOnlyClaw
                    && hasNoDrawLeft
                    && reEquipHasDiscardAtThree;
                bool shouldReequipNow = (canReequipClawNow && reEquipOpensDiscardAtMax)
                    || forceReequipByOnlyClawNoDraw;

                if (shouldReequipNow && !faceDiscountUnlocksDiscardWindow)
                {
                    int castVal = forceReequipByOnlyClawNoDraw ? -3200 : -2600;
                    int orderVal = forceReequipByOnlyClawNoDraw ? 9950 : 9800;

                    try { p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(castVal)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(orderVal)); } catch { }
                    try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(850)); } catch { }
                    try
                    {
                        if (clawInHandCard != null)
                        {
                            try { p.CastWeaponsModifiers.AddOrUpdate(clawInHandCard.Id, new Modifier(castVal)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(clawInHandCard.Id, new Modifier(orderVal)); } catch { }
                        }
                    }
                    catch { }
                    p.ForceResimulation = true;
                    if (forceReequipByOnlyClawNoDraw)
                    {
                        AddLog("[时空之爪-换刀硬放行] 最高费仅刀+无过牌+旧刀耐久=" + equippedClawDurability
                            + " +3费有被弃组件 => 先新刀换旧刀(" + castVal + "/" + orderVal + ")，再重算");
                    }
                    else
                    {
                        AddLog("[时空之爪-换刀放行] 旧刀耐久=" + equippedClawDurability
                            + " 且换刀后最高费可命中被弃组件(maxCost=" + reEquipNextMaxCost
                            + ") => 先新刀换旧刀(" + castVal + "/" + orderVal + ")，再重算后处理挥刀");
                    }
                    return;
                }

                if (faceDiscountUnlocksDiscardWindow)
                {
                    AddLog("[时空之爪-硬规则] 已装备且最高费不含被弃组件，但可通过郊狼/邪翼蝠压费转化最高费为被弃组件 => 暂不强推分流，保留先A后挥刀线");
                }
                else
                {
                    BlockWeaponAttackForAllTargets(p, board, Card.Cards.END_016, 9999);
                    TryPrioritizeLifeTapForClawBlock(p, board, canTapNow,
                        "[时空之爪-硬规则] 已装备且最高费不含被弃组件 => 强制禁攻(9999)");
                    AddLog("[时空之爪-硬规则] 已装备且最高费不含被弃组件 => 禁止攻击(9999) | maxCost="
                        + maxCostInHand + " | highest=[" + GetCardIdsForLog(highestCards) + "]");
                    return;
                }
            }

            if (hasClawEquipped && !lethalThisTurn && !hasDiscardComponentAtMax && emptyHandForClawRule)
            {
                try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2400)); } catch { }
                AddLog("[时空之爪-空手放行] 手牌为空且时空之爪可攻击，且无被弃组件 => 放行挥刀(-2400)");
            }

            // 复盘用：打印“刀A不A”的关键?快照（仅?际装备了时空之爪时打印，避免无武?眬闈?埛灞忥級
            try
            {
                if (hasClawEquipped)
                {
                    int handCountLocal = 0;
                    try { handCountLocal = board.Hand != null ? board.Hand.Count : 0; } catch { handCountLocal = 0; }

                bool enemyHasTaunt = false;
                try { enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { enemyHasTaunt = false; }

                string highestAll = "";
                string highestDiscard = "";
                try
                {
                    highestAll = highestCards.Count > 0
                        ? string.Join(",", highestCards.Select(h => h.Template.Id.ToString()))
                        : "(空)";

                    if (discardComponents != null && discardComponents.Count > 0)
                    {
                        var d = highestCards.Where(h => discardComponents.Contains(h.Template.Id)).ToList();
                        highestDiscard = d.Count > 0 ? string.Join(",", d.Select(h => h.Template.Id.ToString())) : "(无";
                    }
                    else
                    {
                        highestDiscard = "(组件未算/空)";
                    }
                }
                catch
                {
                    highestAll = "(解析失败)";
                    highestDiscard = "(解析失败)";
                }

                    AddLog("[时空之爪-判定] equipped=" + (hasClawEquipped ? "Y" : "N")
                        + " heroCanAttack=" + (heroCanAttackWithClaw ? "Y" : "N")
                        + " lethal=" + (lethalThisTurn ? "Y" : "N")
                        + " mana=" + board.ManaAvailable
                        + " canTapNow=" + (canTapNow ? "Y" : "N")
                        + " hand=" + handCountLocal
                        + " maxCost=" + maxCostInHand
                        + " hasDiscardAtMax=" + (hasDiscardComponentAtMax ? "Y" : "N")
                        + " highest=[" + highestAll + "]"
                        + " highestDiscard=[" + highestDiscard + "]"
                        + " enemyTaunt=" + (enemyHasTaunt ? "Y" : "N")
                        + " merchantSac=" + (merchantCanSacrifice ? "Y" : "N")
                        + " otherActionsExClaw=" + (hasOtherRealActions ? "Y" : "N"));
                }
            }
            catch { }

            // 鍙ｅ緞血：当场上窟穴可用且?最高费仅灵魂弹幕?时，优先点窟穴弃牌（比?垁鏇寸?更不浪费??锛夈??
            // 娉?：时空之爪?昏緫鍦?獰绌撮?辑之前运行；若这里直接 return，会导致后续窟穴逻辑无法覆盖?
            try
            {
                bool canClickCaveLocal = false;
                try
                {
                    var clickableCaveOnBoard = board.MinionFriend?.FirstOrDefault(x => x != null && x.Template != null
                        && x.Template.Id == Card.Cards.WON_103 && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
                    canClickCaveLocal = clickableCaveOnBoard != null;
                }
                catch { canClickCaveLocal = false; }

                if (!lethalThisTurn
                    && heroCanAttackWithClaw
                    && hasDiscardComponentAtMax
                    && canClickCaveLocal
                    && highestOnlySoulBarrage
                    && !highestSoulBarrageAlreadyCheap
                    && !hasClawInHand)
                {
                    p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-999));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999));
                    p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(850));
                    p.ForceResimulation = true;
                    AddLog("[时空之爪-改优先级] 场上窟穴可点且最高费仅灵魂弹幕=> 先点窟穴(-999/9999)，延后挥刀(850)");
                    return;
                }
                if (!lethalThisTurn
                    && heroCanAttackWithClaw
                    && hasDiscardComponentAtMax
                    && canClickCaveLocal
                    && highestOnlySoulBarrage
                    && !highestSoulBarrageAlreadyCheap
                    && hasClawInHand)
                {
                    try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2500)); } catch { }
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(900)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-700)); } catch { }
                    p.ForceResimulation = true;
                    AddLog("[时空之爪-改优先级] 手上有时空之爪且最高费仅灵魂弹幕 => 先挥刀，再点窟穴(-2500/900)");
                    return;
                }
                if (!lethalThisTurn
                    && heroCanAttackWithClaw
                    && hasDiscardComponentAtMax
                    && canClickCaveLocal
                    && highestSoulBarrageAlreadyCheap)
                {
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-999)); } catch { }
                    AddLog("[时空之爪-改优先级] 场上窟穴可点但最高费灵魂弹幕已<=1费 => 本轮不点窟穴，保留挥刀/其他动作");
                }
            }
            catch { }

            // 銆愮?规则】已装备时空之爪 + 手牌最高费不包含被弃组件=> 禁止时空之爪攻击
            // 说明：按你新ｅ緞锛岃繖鏉?则不区分?潃/非斩杀，只要条件成立就不允许挥?銆?
            // 鑻?回合可以分流，则仍然鼓励先分流以刷新手牌℃（分流后会重算，最高费变为被弃组件则会自动放行）??
            if (heroCanAttackWithClaw && !hasDiscardComponentAtMax && !emptyHandForClawRule && !faceDiscountUnlocksDiscardWindow)
            {
                BlockWeaponAttackForAllTargets(p, board, Card.Cards.END_016, 9999);

                try
                {
                    if (!lethalThisTurn && canTapNow && !coyoteUnlocksDiscardWindow)
                    {
                        TryPrioritizeLifeTapForClawBlock(p, board, canTapNow,
                            "[时空之爪-硬规则] 满足禁攻条件");
                    }
                }
                catch { }

                AddLog("[时空之爪-硬规则] 已装备且最高费不包含被弃组件=> 禁止攻击(9999) | maxCost="
                    + maxCostInHand + " | highest=[" + GetCardIdsForLog(highestCards) + "]");
                return;
            }
            if (heroCanAttackWithClaw && !hasDiscardComponentAtMax && !emptyHandForClawRule && faceDiscountUnlocksDiscardWindow)
            {
                AddLog("[时空之爪-硬规则] 满足禁攻条件，但可通过郊狼/邪翼蝠压费转化最高费为被弃组件 => 暂不禁攻");
            }

            // 【优先级0 - 鏈?楂樸?能分流时：先分流再?垁
            // 目的：先抽牌再决定是??鐢?埅瀛愯?发弃牌（避免“先?垁 -> 抽到弹幕/收益后已无法再挥?鈥濓級
            // 护栏：非斩杀窗口 + 手牌不爆(<=9),当前最高费℃被弃组件
            if (!lethalThisTurn && heroCanAttackWithClaw && canTapNow&& !hasDiscardComponentAtMax && !emptyHandForClawRule && !faceDiscountUnlocksDiscardWindow)
            {
                int handCountLocal = 0;
                try { handCountLocal = board.Hand != null ? board.Hand.Count : 0; } catch { handCountLocal = 0; }

                try
                {
                    Card caveOnly;
                    Card pirateOnly;
                    if (IsOnlyCaveAndSpacePirateHand(board, out caveOnly, out pirateOnly))
                    {
                        bool hasCoinLocal = false;
                        try { hasCoinLocal = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinLocal = false; }
                        int effectiveManaLocal = board.ManaAvailable + (hasCoinLocal ? 1 : 0);
                        bool cavePlayableNow = caveOnly != null
                            && caveOnly.CurrentCost <= effectiveManaLocal
                            && GetFriendlyBoardSlotsUsed(board) < 7;
                        if (cavePlayableNow)
                        {
                            AddLog("[时空之爪-优先级] 手里仅地标+太空海盗 => 不先分流，保留地标节奏");
                            return;
                        }
                    }
                }
                catch { }

                if (handCountLocal <= 9)
                {
                    // 延后挥刀，强推先分流
                    p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9999));
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(-2000)); } catch { }
                    p.ForceResimulation = true;
                    AddLog("[时空之爪-优先级] 可分流且可挥刀(手牌" + handCountLocal + ") => 先分流后挥刀(延后攻击900, 分流-2000)");
                    return;
                }
                else
                {
                    AddLog("[时空之爪-优先级] 可分流但手牌接近爆牌(手牌" + handCountLocal + ") => 不强推先分流");
                }
            }

            // 【优先级1 - 鏈?楂樸?戞柀鏉?窗口：先法术后武?敾鍑?
            if (lethalThisTurn && heroCanAttackWithClaw 
                && board.Hand.Any(h => h != null && h.Template != null 
                    && h.Type == Card.CType.SPELL && h.CurrentCost <= board.ManaAvailable))
            {
                // 鏂?时：法术优先，爪子攻击延?
                p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(650));
                AddLog("[时空之爪-优先级] 斩杀窗口 => 后置爪子攻击(650)，优先施放法术");
                return; // 走完这个?就结束，不再?下走
            }

            // 【优先级2】场上有可?死的过期货?卖商且装备了爪子：暂缓武?击，先让专卖商?佹?
            else if (merchantCanSacrifice && hasClawEquipped && heroCanAttackWithClaw)
            {
                p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9999));
                AddLog("[时空之爪-优先级] 场上有可送死的过期货物专卖商 => 暂缓武器攻击(9999)，先让专卖商送死过牌");
                return;
            }
            
            // 【优先级3】已装备爪子且手中还有重复的爪子：禁?浛鎹?紙闄?潪当前最高费是爪子且无过牌）
            else if (hasClawEquipped && hasClawInHand)
            {
                // 妫?鏌?槸鍚?足特殊条件：最高费是爪子且无过牌手?
                bool highestCostIsClaw = highestCards.Any(x => x != null
                    && x.Template != null && x.Template.Id == Card.Cards.END_016);
                bool allowReequip = highestCostIsClaw && hasNoDrawLeft;
                if (!allowReequip && canReequipClawNow && reEquipOpensDiscardAtMax)
                {
                    allowReequip = true;
                }
                
                if (allowReequip)
                {
                    try { p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9700)); } catch { }
                    try
                    {
                        if (clawInHandCard != null)
                        {
                            try { p.CastWeaponsModifiers.AddOrUpdate(clawInHandCard.Id, new Modifier(-2200)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(clawInHandCard.Id, new Modifier(9700)); } catch { }
                        }
                    }
                    catch { }
                    p.ForceResimulation = true;

                    if (canReequipClawNow && reEquipOpensDiscardAtMax)
                    {
                        AddLog("[时空之爪-优先级a] 旧刀耐久<=1且换刀后最高费命中被弃组件 => 允许新刀换旧刀(-2200/9700)");
                    }
                    else
                    {
                        // 允许顶刀?理手牌高费点
                        AddLog("[时空之爪-优先级a] 当前最高费包含爪子且无过牌 => 允许顶刀清理手牌");
                    }
                }
                else
                {
                    // 不替换新刀（使?00而非999锛?
                    p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(800));
                    AddLog("[时空之爪-优先级b] 已装备爪子且有耐久 => 不替换(800)");
                }
                
                // 额外?鏌?如果无被弃组件且非斩杀窗口，禁?敾鍑?
                if (!hasDiscardComponentAtMax && !lethalThisTurn && heroCanAttackWithClaw && !faceDiscountUnlocksDiscardWindow)
                {
                    p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9999));
                    AddLog("[时空之爪-优先级c] 无被弃组件=> 禁止攻击(9999)");
                }
                else if (!hasDiscardComponentAtMax && !lethalThisTurn && heroCanAttackWithClaw && faceDiscountUnlocksDiscardWindow)
                {
                    AddLog("[时空之爪-优先级c] 当前无被弃组件，但可通过郊狼/邪翼蝠压费转化 => 暂不禁攻");
                }
                // 鑻最高费存在被弃组件，且当前可攻击：鼓励先挥?瑙?弃牌（攻击后强制重算?
                else if (hasDiscardComponentAtMax && !lethalThisTurn && hasClawEquipped)
                {
                    // 注意：board.HeroFriend.CanAttack 鍦?分时序下可能误判?N；这里不依赖它来“安排?濇尌鍒?銆?
                    p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2500));
                    p.ForceResimulation = true;
                    ApplyClawFaceBiasWhenHighestOnlySoulBarrage(p, board, highestOnlySoulBarrage, "[时空之爪-优先级d]");

                    AddLog("[时空之爪-优先级d] 当前最高费有被弃组件=> 强推武器攻击触发弃牌(-2500)，并强制重算");
                }
                
                return;
            }

            // 【优先级4銆戞墜涓?装备爪子 + 当前最高费有古尔丹之手 + 手牌?：提升爪子优先级
            else if ((hasClawInHand || heroCanAttackWithClaw) 
                && board.Hand.Any(h => h != null && h.Template != null 
                    && h.Template.Id == Card.Cards.BT_300 && h.CurrentCost == maxCostInHand)
                && discardComponents != null && discardComponents.Contains(Card.Cards.BT_300))
            {
                // 抑制分流，提升爪子优先级
                // 注：英雄?能修ｉ要使?astAbilitiesModifiers（如果API支持?
                
                if (hasClawInHand)
                {
                    p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(7500));
                }
                if (heroCanAttackWithClaw)
                {
                    p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-200));
                }
                AddLog("[时空之爪-优先级] 配合古尔丹之手 => 提升爪子优先级");
                return;
            }

            // 【优先级4b】已装备爪子且最高费有被弃组件：优先攻击?弃牌
            else if (hasClawEquipped && hasDiscardComponentAtMax && !lethalThisTurn)
            {
                // 注意：board.HeroFriend.CanAttack 鍦?分时序下可能误判?N；这里不依赖它来“安排?濇尌鍒?銆?
                p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2500));
                p.ForceResimulation = true;
                ApplyClawFaceBiasWhenHighestOnlySoulBarrage(p, board, highestOnlySoulBarrage, "[时空之爪-优先级b]");

                AddLog("[时空之爪-优先级b] 当前最高费有被弃组件=> 强推武器攻击触发弃牌(-2500)，并强制重算");
                return;
            }

            // 【优先级5】未装备爪子时的提刀?彂鍣?紙鍔??侊級
            else if (!hasClawEquipped && hasClawInHand)
            {
                var claw = board.Hand.FirstOrDefault(x => x != null && x.Template != null
                    && x.Template.Id == Card.Cards.END_016);

                bool hasCoinLocal = false;
                try { hasCoinLocal = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinLocal = false; }
                int effectiveManaLocal = board.ManaAvailable + (hasCoinLocal ? 1 : 0);

                // 触发条件1：最高费存在被弃组件
                bool reasonHighestHasDiscard = hasDiscardComponentAtMax;

                // 触发条件2：己方场上有过期?专卖商，且其?命中被弃组件
                bool reasonMerchantDrHasDiscard = false;
                try
                {
                    var discardSetForMerchant = discardComponents != null && discardComponents.Count > 0
                        ? new HashSet<Card.Cards>(discardComponents)
                        : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));

                    if (board.MinionFriend != null && discardSetForMerchant.Count > 0)
                    {
                        foreach (var m in board.MinionFriend)
                        {
                            if (m == null || m.Template == null || m.Template.Id != Card.Cards.ULD_163) continue;

                            var ench = m.Enchantments != null
                                ? m.Enchantments.FirstOrDefault(x => x != null
                                    && x.EnchantCard != null && x.EnchantCard.Template != null
                                    && x.EnchantCard.Template.Id == Card.Cards.ETC_424)
                                : null;
                            if (ench == null || ench.DeathrattleCard == null || ench.DeathrattleCard.Template == null) continue;

                            var drId = ench.DeathrattleCard.Template.Id;
                            if (discardSetForMerchant.Contains(drId))
                            {
                                reasonMerchantDrHasDiscard = true;
                                break;
                            }
                        }
                    }
                }
                catch { reasonMerchantDrHasDiscard = false; }

                // 触发条件3：没有其他可?姩浣?
                bool reasonNoOtherActions = !hasOtherRealActions;

                // 触发条件4：手里有恐?海盗，且己方随从位<=6
                bool reasonPirateWindow = false;
                bool reasonDoublePirateWindow = false;
                int dreadCorsairCount = 0;
                try
                {
                    dreadCorsairCount = CountDreadCorsairInHand(board);
                    int usedSlots = GetFriendlyBoardSlotsUsed(board);
                    // 单海盗窗ｏ只要手里?张恐怖海盗，就把提刀纳入可?冭檻动作锛堟槸鍚?尌鍒?由后续规则决定）
                    reasonPirateWindow = dreadCorsairCount >= 1;
                    // 双海盗窗ｏ仍要求场位足够，确保提刀后可?刻连?                    reasonDoublePirateWindow = dreadCorsairCount >= 2 && usedSlots <= 5;
                }
                catch
                {
                    reasonPirateWindow = false;
                    reasonDoublePirateWindow = false;
                    dreadCorsairCount = 0;
                }

                bool shouldEquipClaw = reasonHighestHasDiscard
                    || reasonMerchantDrHasDiscard
                    || reasonNoOtherActions
                    || reasonPirateWindow
                    || reasonDoublePirateWindow;

                // 让位速写：若牌库仍有灵魂弹幕且速写本回合可打，优先速写过牌，不抢提刀窗口。
                // 仅在非海盗提刀窗口生效，避免破坏恐怖海盗连段。
                bool shouldYieldToSketchForBarrage = false;
                int barrageRemainForYield = 0;
                Card sketchForYield = null;
                try
                {
                    if (!lethalThisTurn && shouldEquipClaw && !reasonPirateWindow && !reasonDoublePirateWindow)
                    {
                        sketchForYield = board.Hand.FirstOrDefault(x => x != null
                            && x.Template != null
                            && x.Template.Id == Card.Cards.TOY_916);

                        bool canPlaySketchNow = sketchForYield != null
                            && sketchForYield.CurrentCost <= effectiveManaLocal
                            && (board.Hand != null ? board.Hand.Count : 0) <= 9;

                        if (canPlaySketchNow)
                        {
                            barrageRemainForYield = GetRemainingBarrageCountInDeck(board);
                            shouldYieldToSketchForBarrage = barrageRemainForYield > 0;
                        }
                    }
                }
                catch
                {
                    shouldYieldToSketchForBarrage = false;
                    barrageRemainForYield = 0;
                    sketchForYield = null;
                }

                if (shouldYieldToSketchForBarrage)
                {
                    try { p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-9999)); } catch { }
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-1700)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(9800)); } catch { }
                    try
                    {
                        if (sketchForYield != null)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(sketchForYield.Id, new Modifier(-1700)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(sketchForYield.Id, new Modifier(9800)); } catch { }
                        }
                    }
                    catch { }
                    p.ForceResimulation = true;
                    AddLog("[时空之爪-让位速写] 牌库有灵魂弹幕(est=" + barrageRemainForYield
                        + ")且速写可打 => 暂不提刀，先速写(-1700/9800)");
                    return;
                }

                if (shouldEquipClaw)
                {
                    if (claw != null && claw.CurrentCost <= effectiveManaLocal)
                    {
                        bool needCoinForClaw = hasCoinLocal
                            && claw.CurrentCost > board.ManaAvailable
                            && claw.CurrentCost <= effectiveManaLocal;

                        int castMod = -350;
                        int orderMod = 7000;

                        if (reasonNoOtherActions && !reasonHighestHasDiscard && !reasonMerchantDrHasDiscard && !reasonPirateWindow)
                        {
                            castMod = -150;
                            orderMod = 5500;
                        }

                        if (reasonHighestHasDiscard || reasonMerchantDrHasDiscard)
                        {
                            castMod = -450;
                            orderMod = 8600;
                        }

                        if (reasonPirateWindow)
                        {
                            castMod = -550;
                            orderMod = 9200;
                        }

                        // 双海盗窗口：提刀后可立刻连拍两个0费恐怖海盗，强制提刀优先
                        if (reasonDoublePirateWindow)
                        {
                            castMod = -2600;
                            orderMod = 9800;
                        }

                        if (highestOnlySoulBarrage && (reasonHighestHasDiscard || reasonMerchantDrHasDiscard))
                        {
                            castMod = -2600;
                            orderMod = 9800;
                        }

                        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(castMod));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(orderMod));

                        bool coinThenClaw = TrySetCoinThenCardComboWithoutResim(p, board, claw, needCoinForClaw, "时空之爪-优先级");
                        if (!coinThenClaw && highestOnlySoulBarrage && (reasonHighestHasDiscard || reasonMerchantDrHasDiscard))
                        {
                            try
                            {
                                if (p.ComboModifier == null)
                                {
                                    p.ComboModifier = new ComboSet(claw.Id);
                                }
                            }
                            catch { }
                        }

                        // 提刀触发器命中时，直接锁定“本步提刀”，避免被太空海盗/过牌随从等动作抢跑。
                        if (!coinThenClaw && (reasonHighestHasDiscard || reasonMerchantDrHasDiscard || reasonPirateWindow || reasonDoublePirateWindow))
                        {
                            try
                            {
                                if (p.ComboModifier == null)
                                {
                                    p.ComboModifier = new ComboSet(claw.Id);
                                    AddLog("[时空之爪-强制连段] 命中提刀触发器 => 锁定本步提刀(ComboSet)");
                                }
                            }
                            catch { }
                        }

                        // 关键：?甯?>提刀连段时，不应?币后重算打断连段
                        p.ForceResimulation = !coinThenClaw;
                        AddLog("[时空之爪-优先级] 未装备爪子且命中提刀触发器 => 提刀("
                            + orderMod + "/" + castMod + ")"
                            + " | 原因:highestDiscard=" + (reasonHighestHasDiscard ? "Y" : "N")
                            + ",merchantDRDiscard=" + (reasonMerchantDrHasDiscard ? "Y" : "N")
                            + ",noOtherActions=" + (reasonNoOtherActions ? "Y" : "N")
                            + ",pirateInHand=" + (reasonPirateWindow ? "Y" : "N")
                            + ",piratePair=" + (reasonDoublePirateWindow ? "Y" : "N")
                            + ",pirateCount=" + dreadCorsairCount
                            + (needCoinForClaw ? ",coin=Y" : ",coin=N"));
                    }
                    else
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(130));
                        AddLog("[时空之爪-优先级] 满足提刀触发器但当前不可提刀 => 暂不提刀(130)");
                    }
                    return;
                }

                if (!lethalThisTurn)
                {
                    p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(130));
                    AddLog("[时空之爪-优先级a] 未命中提刀触发器且非斩杀窗口 => 禁用提刀(130)");
                    return;
                }
            }

            // 【优先级8】装备爪子且手上有弹?被弃组件：延后攻?
            // 

            // 【优先级9 - 默认】正常情况：不做特殊处理
            else
            {
                AddLog("[时空之爪-默认] 无特殊条件触发，使用默认");
            }
        }

        /// <summary>
        /// 处理灵魂之火的所有?昏緫
        /// </summary>
        private void ProcessCard_EX1_308_Soulfire(ProfileParameters p, Board board, 
            bool hasDiscardComponentInHand, int payoffCount, int handCount, 
            bool hasOtherActions, bool has0CostMinions, bool hasPhotonInHand, int maxMana,
            bool lethalThisTurn)
        {
            // ==============================================
            // Card.Cards.EX1_308 - 灵魂之火（法术）
            // 说明：所有关于灵魂之火的?都在这里，按优先级高到?
            // ==============================================

            if (!board.HasCardInHand(Card.Cards.EX1_308)) return;

            // 统一ｅ：魂火始终后置（?后），无例外?
            try
            {
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-9999));
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                if (board.Hand != null)
                {
                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;
                        if (c.Template.Id != Card.Cards.EX1_308) continue;
                        try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                        try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(8000)); } catch { }
                    }
                }
            }
            catch { }

            // 仅两种可?満鏅?細
            // 1) 当回合斩?锛?
            // 2) 闄?瓊鐏?硬币外，手牌?被弃组件=
            bool allowByAllDiscardComponents = false;
            int nonSoulfireNonCoinCount = 0;
            int discardNonSoulfireNonCoinCount = 0;
            try
            {
                var discardSet = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                var nonSoulfireNonCoinCards = GetHandCardsForDiscardLogic(board, true)
                    .Where(c => c != null && c.Template != null
                        && c.Template.Id != Card.Cards.EX1_308)
                    .ToList();

                nonSoulfireNonCoinCount = nonSoulfireNonCoinCards.Count;
                if (nonSoulfireNonCoinCount > 0 && discardSet.Count > 0)
                {
                    discardNonSoulfireNonCoinCount = nonSoulfireNonCoinCards.Count(c => discardSet.Contains(c.Template.Id));
                    allowByAllDiscardComponents = discardNonSoulfireNonCoinCount == nonSoulfireNonCoinCount;
                }
            }
            catch { allowByAllDiscardComponents = false; }

            bool allowByLethal = lethalThisTurn;
            bool allowByStarterOverride = false;
            bool allowByNoStarterHighDiscard = false;
            int starterOverrideDiscardCount = 0;
            int starterOverrideTotalCount = 0;
            string starterOverrideReason = null;
            int noStarterHighDiscardDiscardCount = 0;
            int noStarterHighDiscardTotalCount = 0;
            string noStarterHighDiscardReason = null;
            bool onlyOneSoulfireInHand = false;
            bool allowByEmergencyDefense = false;
            string emergencyDefenseReason = null;
            int emergencyDefenseTargetId = 0;
            int emergencyEnemyAttack = 0;
            int emergencyMyHp = 0;
            bool allowByLifestealDefense = false;
            string lifestealDefenseReason = null;
            int lifestealDefenseTargetId = 0;
            try
            {
                var discardSetForSoulfire = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                allowByStarterOverride = ShouldAllowSoulfireStarterOverride(board, discardSetForSoulfire, lethalThisTurn,
                    out starterOverrideReason, out starterOverrideDiscardCount, out starterOverrideTotalCount);
                allowByNoStarterHighDiscard = ShouldAllowSoulfireNoReadyStarterHighDiscard(
                    board, discardSetForSoulfire,
                    out noStarterHighDiscardReason, out noStarterHighDiscardDiscardCount, out noStarterHighDiscardTotalCount);

                int soulfireCountInHand = board.Hand != null
                    ? board.Hand.Count(c => c != null && c.Template != null && IsSoulfireCardVariant(c))
                    : 0;
                onlyOneSoulfireInHand = soulfireCountInHand == 1;

                allowByEmergencyDefense = ShouldAllowSoulfireEmergencyDefense(board,
                    out emergencyDefenseReason, out emergencyDefenseTargetId, out emergencyEnemyAttack, out emergencyMyHp);
                allowByLifestealDefense = ShouldAllowSoulfireVsLifestealThreatCompat(board,
                    out lifestealDefenseReason, out lifestealDefenseTargetId);
            }
            catch
            {
                allowByStarterOverride = false;
                allowByNoStarterHighDiscard = false;
                starterOverrideDiscardCount = 0;
                starterOverrideTotalCount = 0;
                starterOverrideReason = null;
                noStarterHighDiscardDiscardCount = 0;
                noStarterHighDiscardTotalCount = 0;
                noStarterHighDiscardReason = null;
                onlyOneSoulfireInHand = false;
                allowByEmergencyDefense = false;
                emergencyDefenseReason = null;
                emergencyDefenseTargetId = 0;
                emergencyEnemyAttack = 0;
                emergencyMyHp = 0;
                allowByLifestealDefense = false;
                lifestealDefenseReason = null;
                lifestealDefenseTargetId = 0;
            }

            bool allowByDoubleSoulfireGamble = false;
            string doubleSoulfireGambleReason = null;
            bool allowBySingleSoulfireGamble = false;
            string singleSoulfireGambleReason = null;
            try
            {
                allowByDoubleSoulfireGamble = ShouldAllInDoubleSoulfireLethalGamble(board, lethalThisTurn, out doubleSoulfireGambleReason);
                if (allowByDoubleSoulfireGamble)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-9999)); } catch { }
                    try
                    {
                        var dreadPiratesForBlock = GetDreadCorsairCardsInHand(board);
                        foreach (var pirate in dreadPiratesForBlock)
                        {
                            if (pirate == null || pirate.Template == null) continue;
                            try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-9999)); } catch { }
                        }
                    }
                    catch { }

                    AddLog("[灵魂之火-双魂火赌博] " + (doubleSoulfireGambleReason ?? "命中双魂火斩杀赌博窗口")
                        + " => 暂禁恐怖海盗，保留双魂火斩杀线");
                }

                allowBySingleSoulfireGamble = ShouldAllInSingleSoulfireDiscardLethalGamble(board, lethalThisTurn, out singleSoulfireGambleReason);
            }
            catch
            {
                allowByDoubleSoulfireGamble = false;
                doubleSoulfireGambleReason = null;
                allowBySingleSoulfireGamble = false;
                singleSoulfireGambleReason = null;
            }

            if (allowBySingleSoulfireGamble)
            {
                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-3600)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9950)); } catch { }
                try
                {
                    if (board.Hand != null)
                    {
                        foreach (var sf in board.Hand.Where(c => c != null && c.Template != null && IsSoulfireCardVariant(c)))
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(sf.Id, new Modifier(-3600)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(sf.Id, new Modifier(9950)); } catch { }
                        }
                    }
                }
                catch { }

                AddLog("[灵魂之火-单魂火赌博] " + (singleSoulfireGambleReason ?? "命中单魂火弃牌斩杀窗口")
                    + " => 强推魂火前置(Cast=-3600,Order=9950)");
                return;
            }

            // 用户口径：若本回合可稳定打出“商贩弃弹幕”连段，则魂火必须继续后置，不允许抢先。
            bool forceDelaySoulfireByMerchantBarrage = false;
            try
            {
                bool hasBoardSlotForMerchant = GetFriendlyBoardSlotsUsed(board) < 7;
                bool merchantPlayableNow = hasBoardSlotForMerchant
                    && board.Hand != null
                    && board.Hand.Any(c => c != null && c.Template != null
                        && c.Template.Id == Card.Cards.ULD_163
                        && c.CurrentCost <= board.ManaAvailable);
                bool hasBarrageNow = board.Hand != null
                    && board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);

                bool highestOnlyBarrageAfterMerchant = false;
                if (merchantPlayableNow && hasBarrageNow)
                {
                    var handWithoutMerchant = GetHandCardsForDiscardLogic(board, true)
                        .Where(c => c != null && c.Template != null && c.Template.Id != Card.Cards.ULD_163)
                        .ToList();
                    if (handWithoutMerchant.Count > 0)
                    {
                        int maxCostAfterMerchant = handWithoutMerchant.Max(c => c.CurrentCost);
                        var highestAfterMerchant = handWithoutMerchant.Where(c => c.CurrentCost == maxCostAfterMerchant).ToList();
                        highestOnlyBarrageAfterMerchant = highestAfterMerchant.Count > 0
                            && highestAfterMerchant.All(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);
                    }
                }

                forceDelaySoulfireByMerchantBarrage = !lethalThisTurn
                    && merchantPlayableNow
                    && hasBarrageNow
                    && highestOnlyBarrageAfterMerchant;
            }
            catch { forceDelaySoulfireByMerchantBarrage = false; }

            if (forceDelaySoulfireByMerchantBarrage)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                AddLog("[灵魂之火-让位商贩弹幕] 可稳定商贩弃弹幕 => 暂缓魂火(Cast=8000,Order最后)，先打商贩连段");
                return;
            }

            // 用户口径：手上地标可直接拍时，先地标后魂火（非斩杀回合）。
            bool forceDelaySoulfireByHandCave = false;
            bool forceCaveThenSoulfireBarrageByHand = false;
            Card cavePlayableNow = null;
            int barrageCountInHandForCaveSoulfire = 0;
            try
            {
                bool hasBoardSlotForCave = GetFriendlyBoardSlotsUsed(board) < 7;
                cavePlayableNow = hasBoardSlotForCave && board.Hand != null
                    ? board.Hand.FirstOrDefault(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.WON_103
                        && c.CurrentCost <= board.ManaAvailable)
                    : null;
                bool canCastSoulfireNowForCaveCombo = board.Hand != null
                    && board.Hand.Any(c => c != null && c.Template != null
                        && IsSoulfireCardVariant(c)
                        && c.CurrentCost <= board.ManaAvailable);
                barrageCountInHandForCaveSoulfire = board.Hand != null
                    ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534)
                    : 0;

                forceCaveThenSoulfireBarrageByHand = !lethalThisTurn
                    && cavePlayableNow != null
                    && canCastSoulfireNowForCaveCombo
                    && barrageCountInHandForCaveSoulfire > 0;
                forceDelaySoulfireByHandCave = !lethalThisTurn && cavePlayableNow != null && !forceCaveThenSoulfireBarrageByHand;
            }
            catch
            {
                forceDelaySoulfireByHandCave = false;
                forceCaveThenSoulfireBarrageByHand = false;
                cavePlayableNow = null;
                barrageCountInHandForCaveSoulfire = 0;
            }

            if (forceCaveThenSoulfireBarrageByHand)
            {
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2600)); } catch { }
                try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2600)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9990)); } catch { }
                try
                {
                    if (cavePlayableNow != null)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(cavePlayableNow.Id, new Modifier(-2600)); } catch { }
                        try { p.LocationsModifiers.AddOrUpdate(cavePlayableNow.Id, new Modifier(-2600)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(cavePlayableNow.Id, new Modifier(9990)); } catch { }
                    }
                }
                catch { }

                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-1900)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9750)); } catch { }
                try
                {
                    if (board.Hand != null)
                    {
                        foreach (var sf in board.Hand.Where(c => c != null && c.Template != null && IsSoulfireCardVariant(c)))
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(sf.Id, new Modifier(-1900)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(sf.Id, new Modifier(9750)); } catch { }
                        }
                    }
                }
                catch { }

                // 防止地标魂火连段被超光子/分流抢先打断。
                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(2600)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-2600)); } catch { }
                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(2200)); } catch { }
                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(2200)); } catch { }

                p.ForceResimulation = true;
                AddLog("[灵魂之火-地标弃弹幕连段] 手牌可拍地标且可用魂火，且有灵魂弹幕x"
                    + barrageCountInHandForCaveSoulfire
                    + " => 锁定先地标后魂火(地标9990/-2600,魂火9750/-1900)，并压后超光子/分流");
                return;
            }

            if (forceDelaySoulfireByHandCave)
            {
                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000)); } catch { }
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800)); } catch { }
                try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9800)); } catch { }
                try
                {
                    if (cavePlayableNow != null)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(cavePlayableNow.Id, new Modifier(-1800)); } catch { }
                        try { p.LocationsModifiers.AddOrUpdate(cavePlayableNow.Id, new Modifier(-1800)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(cavePlayableNow.Id, new Modifier(9800)); } catch { }
                    }
                }
                catch { }

                AddLog("[灵魂之火-让位地标] 手牌地标可直接使用 => 暂缓魂火(Cast=8000,Order最后)，先拍地标");
                return;
            }

            // 低费提纯后魂火：先打狗头人/栉龙等低费非被弃组件，再用魂火尝试丢弹幕。
            // 目的：避免郊狼抢先导致错过“提纯后魂火”的高收益弃牌线。
            Card soulfirePrepCard = null;
            int soulfirePrepDiscardCount = 0;
            int soulfirePrepTotalCount = 0;
            string soulfirePrepReason = null;
            bool allowSoulfireByLowCostPrep = false;
            try
            {
                allowSoulfireByLowCostPrep = TryGetLowCostPrepForSoulfireBarrageWindow(
                    board,
                    out soulfirePrepCard,
                    out soulfirePrepDiscardCount,
                    out soulfirePrepTotalCount,
                    out soulfirePrepReason);
            }
            catch
            {
                allowSoulfireByLowCostPrep = false;
                soulfirePrepCard = null;
                soulfirePrepDiscardCount = 0;
                soulfirePrepTotalCount = 0;
                soulfirePrepReason = null;
            }

            bool blockCheapPrepSoulfireByClawWindow = false;
            string blockCheapPrepSoulfireReason = null;
            try
            {
                blockCheapPrepSoulfireByClawWindow =
                    !lethalThisTurn
                    && allowSoulfireByLowCostPrep
                    && ShouldBlockSoulfirePrepForImmediateClawWindow(
                        board,
                        soulfirePrepCard,
                        lethalThisTurn,
                        out blockCheapPrepSoulfireReason);
            }
            catch
            {
                blockCheapPrepSoulfireByClawWindow = false;
                blockCheapPrepSoulfireReason = null;
            }

            if (blockCheapPrepSoulfireByClawWindow)
            {
                allowSoulfireByLowCostPrep = false;
                soulfirePrepCard = null;
                soulfirePrepDiscardCount = 0;
                soulfirePrepTotalCount = 0;
                soulfirePrepReason = null;
                AddLog("[灵魂之火-提纯弃弹幕线让位提刀] "
                    + (string.IsNullOrEmpty(blockCheapPrepSoulfireReason)
                        ? "命中提刀窗口且本回合可提刀"
                        : blockCheapPrepSoulfireReason)
                    + " => 关闭“先提纯后魂火”，优先提刀");
            }

            if (!lethalThisTurn && allowSoulfireByLowCostPrep && soulfirePrepCard != null && soulfirePrepCard.Template != null)
            {
                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-1800)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9600)); } catch { }
                try
                {
                    if (board.Hand != null)
                    {
                        foreach (var sf in board.Hand.Where(c => c != null && c.Template != null && IsSoulfireCardVariant(c)))
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(sf.Id, new Modifier(-1800)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(sf.Id, new Modifier(9600)); } catch { }
                        }
                    }
                }
                catch { }

                try
                {
                    if (soulfirePrepCard.Type == Card.CType.MINION)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(soulfirePrepCard.Template.Id, new Modifier(-2600));
                        p.CastMinionsModifiers.AddOrUpdate(soulfirePrepCard.Id, new Modifier(-2600));
                    }
                    else if (soulfirePrepCard.Type == Card.CType.SPELL)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(soulfirePrepCard.Template.Id, new Modifier(-2600));
                        p.CastSpellsModifiers.AddOrUpdate(soulfirePrepCard.Id, new Modifier(-2600));
                    }
                    else if (soulfirePrepCard.Type == Card.CType.WEAPON)
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(soulfirePrepCard.Template.Id, new Modifier(-2600));
                        p.CastWeaponsModifiers.AddOrUpdate(soulfirePrepCard.Id, new Modifier(-2600));
                    }
                    else
                    {
                        p.LocationsModifiers.AddOrUpdate(soulfirePrepCard.Template.Id, new Modifier(-2600));
                        p.LocationsModifiers.AddOrUpdate(soulfirePrepCard.Id, new Modifier(-2600));
                    }
                    p.PlayOrderModifiers.AddOrUpdate(soulfirePrepCard.Template.Id, new Modifier(9800));
                    p.PlayOrderModifiers.AddOrUpdate(soulfirePrepCard.Id, new Modifier(9800));
                }
                catch { }

                // 该窗口下明确后置郊狼，避免“先压费后抢先落郊狼”打断魂火线。
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(2600)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-3600)); } catch { }
                try
                {
                    if (board.Hand != null)
                    {
                        foreach (var coy in board.Hand.Where(c => c != null && c.Template != null && IsCoyoteCardVariant(c)))
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(coy.Id, new Modifier(2600)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(coy.Id, new Modifier(-3600)); } catch { }
                        }
                    }
                }
                catch { }

                // 同时后置墓，避免0费动作覆盖“提纯->魂火”顺序。
                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(1600)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-1200)); } catch { }

                AddLog("[灵魂之火-提纯弃弹幕线] " + (soulfirePrepReason ?? ("弃牌密度=" + soulfirePrepDiscardCount + "/" + soulfirePrepTotalCount))
                    + " => 先提纯(含灰烬)再魂火尝试弃弹幕(Prep=9800/-2600,Soulfire=9600/-1800,郊狼后置)");
                return;
            }

            // 可击杀吸血威胁时，魂火应前置解场，避免被吸血反打拉开血差。
            if (allowByLifestealDefense)
            {
                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-4200)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9900)); } catch { }
                try
                {
                    if (board.Hand != null)
                    {
                        foreach (var sf in board.Hand.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.EX1_308))
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(sf.Id, new Modifier(-4200)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(sf.Id, new Modifier(9900)); } catch { }
                        }
                    }
                }
                catch { }

                try
                {
                    var target = board.MinionEnemy != null
                        ? board.MinionEnemy.FirstOrDefault(m => m != null && m.Id == lifestealDefenseTargetId)
                        : null;
                    if (target != null && target.Template != null)
                    {
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(target.Template.Id, new Modifier(9999));
                    }
                }
                catch { }

                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }

                AddLog("[灵魂之火-吸血威胁放行] " + (lifestealDefenseReason ?? "可击杀吸血威胁")
                    + " => 强推魂火先解场(Cast=-4200,Order=9900,target=" + lifestealDefenseTargetId + ")");
                return;
            }

            // 新口径：单魂火不再单独放行，必须命中斩杀/全被弃组件/启动放行/保命放行其一。
            bool allowSoulfireNow = allowByLethal || allowByAllDiscardComponents || allowByStarterOverride
                || allowByNoStarterHighDiscard
                || allowByEmergencyDefense || allowByLifestealDefense
                || allowByDoubleSoulfireGamble || allowBySingleSoulfireGamble;

            if (!allowSoulfireNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9999));
                AddLog("[灵魂之火-硬规则] 非斩杀且不满足“除魂火/硬币外全是被弃组件”("
                    + discardNonSoulfireNonCoinCount + "/" + nonSoulfireNonCoinCount
                    + ",放宽窗口=" + noStarterHighDiscardDiscardCount + "/" + noStarterHighDiscardTotalCount
                    + (onlyOneSoulfireInHand ? ",单魂火=Y(不单独放行)" : "")
                    + ") => 禁用(Cast=9999,Order最后)");
                return;
            }

            if (allowByEmergencyDefense)
            {
                bool emergencyMustFrontSoulfire = false;
                try
                {
                    emergencyMustFrontSoulfire = emergencyMyHp > 0
                        && (emergencyEnemyAttack >= emergencyMyHp || emergencyMyHp <= 8);
                }
                catch { emergencyMustFrontSoulfire = false; }

                if (emergencyMustFrontSoulfire)
                {
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-4200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9900)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var sf in board.Hand.Where(c => c != null && c.Template != null && IsSoulfireCardVariant(c)))
                            {
                                try { p.CastSpellsModifiers.AddOrUpdate(sf.Id, new Modifier(-4200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(sf.Id, new Modifier(9900)); } catch { }
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        var target = board.MinionEnemy != null
                            ? board.MinionEnemy.FirstOrDefault(m => m != null && m.Id == emergencyDefenseTargetId)
                            : null;
                        if (target != null && target.Template != null)
                        {
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(target.Template.Id, new Modifier(9999));
                        }
                    }
                    catch { }

                    AddLog("[灵魂之火-保命前置] " + (emergencyDefenseReason ?? "血线危险")
                        + " => 先魂火解场(Cast=-4200,Order=9900,target=" + emergencyDefenseTargetId + ")");
                    return;
                }

                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                try
                {
                    bool canPlayFistNow = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.AT_022
                        && h.CurrentCost <= board.ManaAvailable);
                    if (canPlayFistNow)
                    {
                        try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(9800)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(-9800)); } catch { }
                        foreach (var c in board.Hand)
                        {
                            if (c == null || c.Template == null) continue;
                            if (c.Template.Id != Card.Cards.AT_022) continue;
                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(9800)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9800)); } catch { }
                        }
                        AddLog("[灵魂之火-保命放行-压拳] 血线危险且手里有拳 => 魂火优先于拳(Cast拳=9800,Order=-9800)");
                    }
                }
                catch { }
                AddLog("[灵魂之火-保命放行] " + (emergencyDefenseReason ?? "血线危险")
                    + " => 仅放行使用，仍保持最后出牌(Cast=8000,Order=-9999)");
                return;
            }

            // 鐢?埛鍙ｅ：本回合可打超光子且非斩杀时，不要先手?瓊鐏???
            // 目的：优先用超光子压?解场/铺垫连段，避免魂火过?弃关键组件??
            bool canPlayPhotonNow = false;
            try
            {
                canPlayPhotonNow = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.TIME_027
                    && h.CurrentCost <= board.ManaAvailable);
            }
            catch { canPlayPhotonNow = false; }

            if (!lethalThisTurn && canPlayPhotonNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                AddLog("[灵魂之火-让位超光子] 可用超光子且非斩杀 => 后置魂火(Cast=8000)，优先超光子");
                return;
            }

            // 鐢?埛鍙ｅ緞锛氶潪鏂?潃鏃讹紝鑻?回合可先上宝藏经销商，则不要先手交魂火?
            bool canPlayToyDealerNow = false;
            Card toyDealerPlayable = null;
            try
            {
                bool hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7;
                toyDealerPlayable = board.Hand != null
                    ? board.Hand.FirstOrDefault(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.TOY_518
                        && h.CurrentCost <= board.ManaAvailable)
                    : null;
                canPlayToyDealerNow = hasBoardSlot && toyDealerPlayable != null;
            }
            catch
            {
                canPlayToyDealerNow = false;
                toyDealerPlayable = null;
            }

            if (!lethalThisTurn && canPlayToyDealerNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-1300)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(7600)); } catch { }
                try { p.CastMinionsModifiers.AddOrUpdate(toyDealerPlayable.Id, new Modifier(-1300)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(toyDealerPlayable.Id, new Modifier(7600)); } catch { }
                AddLog("[灵魂之火-让位经销商] 可先上宝藏经销商 => 暂缓魂火(1500)，先上经销商");
                return;
            }

            // 顺序规则：魂火优先级必』低于0费邪翼蝠?费郊狼??
            try
            {
                bool hasSlot = GetFriendlyBoardSlotsUsed(board) < 7;
                var zeroFelwing = hasSlot && board.Hand != null
                    ? board.Hand.FirstOrDefault(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.YOD_032
                        && h.CurrentCost == 0
                        && h.CurrentCost <= board.ManaAvailable)
                    : null;
                var zeroCoyote = hasSlot && board.Hand != null
                    ? board.Hand.FirstOrDefault(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.TIME_047
                        && h.CurrentCost == 0
                        && h.CurrentCost <= board.ManaAvailable)
                    : null;

                if (zeroFelwing != null || zeroCoyote != null)
                {
                    if (zeroFelwing != null)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-9999)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(9999)); } catch { }
                        try { p.CastMinionsModifiers.AddOrUpdate(zeroFelwing.Id, new Modifier(-9999)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(zeroFelwing.Id, new Modifier(9999)); } catch { }
                    }

                    if (zeroCoyote != null)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-9999)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(9999)); } catch { }
                        try { p.CastMinionsModifiers.AddOrUpdate(zeroCoyote.Id, new Modifier(-9999)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(zeroCoyote.Id, new Modifier(9999)); } catch { }
                    }

                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                    AddLog("[灵魂之火-顺序规则] 存在0费邪翼蝠/郊狼 => 先下0费连段，再考虑魂火(魂火Order最后)");
                    return;
                }
            }
            catch { }

            // 鍦?彲鐢?獥鍙ｄ允许魂火，但仍保持?最后出”??
            if (allowByLethal)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                AddLog("[灵魂之火-放行] 斩杀回合 => 允许使用，但仍保持最后出牌(Cast=8000,Order=-9999)");
                return;
            }

            if (allowByStarterOverride)
            {
                bool hasOtherPlayableNonSoulfireAction = false;
                try
                {
                    bool hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7;
                    hasOtherPlayableNonSoulfireAction = board.Hand != null
                        && board.Hand.Any(c => c != null
                            && c.Template != null
                            && c.Template.Id != Card.Cards.EX1_308
                            && c.Template.Id != Card.Cards.GAME_005
                            && c.CurrentCost <= board.ManaAvailable
                            && (c.Type != Card.CType.MINION || hasBoardSlot));
                }
                catch { hasOtherPlayableNonSoulfireAction = false; }

                if (!lethalThisTurn && !hasOtherPlayableNonSoulfireAction)
                {
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-2600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9800)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var sf in board.Hand.Where(c => c != null && c.Template != null
                                && IsSoulfireCardVariant(c)
                                && c.CurrentCost <= board.ManaAvailable))
                            {
                                try { p.CastSpellsModifiers.AddOrUpdate(sf.Id, new Modifier(-2600)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(sf.Id, new Modifier(9800)); } catch { }
                            }
                        }
                    }
                    catch { }

                    AddLog("[灵魂之火-启动前置] 非斩杀且魂火为当前可用启动，且无其它可打动作 => 先魂火避免空过(Cast=-2600,Order=9800)");
                    return;
                }

                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                AddLog("[灵魂之火-放行] 非斩杀但魂火为当前可用启动：" + (starterOverrideReason ?? "无其它启动替代")
                    + ",弃牌密度=" + starterOverrideDiscardCount + "/" + starterOverrideTotalCount
                    + ") => 仅放行使用，仍保持最后出牌(Cast=8000,Order=-9999)");
                return;
            }

            if (allowByNoStarterHighDiscard)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                AddLog("[灵魂之火-放宽放行] 无现成启动组件且被弃组件>2("
                    + noStarterHighDiscardDiscardCount + "/" + noStarterHighDiscardTotalCount
                    + ",原因=" + (noStarterHighDiscardReason ?? "无")
                    + ") => 放行魂火，仍保持最后出牌(Cast=8000,Order=-9999)");
                return;
            }

            if (allowByDoubleSoulfireGamble)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                AddLog("[灵魂之火-放行] " + (doubleSoulfireGambleReason ?? "命中双魂火斩杀赌博窗口")
                    + " => 放行魂火，仍保持最后出牌(Cast=8000,Order=-9999)");
                return;
            }

            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
            AddLog("[灵魂之火-放行] 除魂火/硬币外全是被弃组件"
                + discardNonSoulfireNonCoinCount + "/" + nonSoulfireNonCoinCount
                + ") => 允许使用，仍保持最后出牌(Cast=8000,Order=-9999)");
        }

        /// <summary>
        /// 处理超光子弹幕的?鏈夐?昏緫
        /// </summary>
        private void ProcessCard_TIME_027_HyperBeam(ProfileParameters p, Board board, 
            bool isTemporary, int lowHealthEnemyCount, bool lethalThisTurn)
        {
            // ==============================================
            // Card.Cards.TIME_027 - 超光子弹幕（法术?
            // 说明：所有关于超光子弹幕的判断都?里，按优先级从高到低
            // ==============================================

            if (!board.HasCardInHand(Card.Cards.TIME_027)) return;

            bool canPlayPhoton = board.Hand.Any(h => h != null && h.Template != null 
                && h.Template.Id == Card.Cards.TIME_027 && h.CurrentCost <= board.ManaAvailable);
            
            if (!canPlayPhoton) return;

            // 地标+魂火弃弹幕连段窗口：当可拍地标且可打魂火，且手里有灵魂弹幕时，
            // 超光子必须让位，避免打断“先地标后魂火”的高收益弃牌线。
            try
            {
                bool hasBoardSlotForCave = GetFriendlyBoardSlotsUsed(board) < 7;
                var cavePlayableNow = hasBoardSlotForCave && board.Hand != null
                    ? board.Hand.FirstOrDefault(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.WON_103
                        && c.CurrentCost <= board.ManaAvailable)
                    : null;
                bool canCastSoulfireNow = board.Hand != null
                    && board.Hand.Any(c => c != null
                        && c.Template != null
                        && IsSoulfireCardVariant(c)
                        && c.CurrentCost <= board.ManaAvailable);
                int barrageCountInHand = board.Hand != null
                    ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534)
                    : 0;

                bool forceYieldPhotonToCaveSoulfire = !lethalThisTurn
                    && cavePlayableNow != null
                    && canCastSoulfireNow
                    && barrageCountInHand > 0;
                if (forceYieldPhotonToCaveSoulfire)
                {
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-3200)); } catch { }
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2600)); } catch { }
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9990)); } catch { }
                    try
                    {
                        if (cavePlayableNow != null)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(cavePlayableNow.Id, new Modifier(-2600)); } catch { }
                            try { p.LocationsModifiers.AddOrUpdate(cavePlayableNow.Id, new Modifier(-2600)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(cavePlayableNow.Id, new Modifier(9990)); } catch { }
                        }
                    }
                    catch { }
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-1900)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9750)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(2200)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(2200)); } catch { }
                    p.ForceResimulation = true;
                    AddLog("[超光子弹幕-让位地标魂火] 手牌地标可拍+魂火可用+灵魂弹幕x" + barrageCountInHand
                        + " => 超光子后置(3200/-3200)，先地标后魂火");
                    return;
                }
            }
            catch { }

            bool hasFelwingOrCoyoteInHand = false;
            Card nonZeroCoyoteInHand = null;
            try
            {
                hasFelwingOrCoyoteInHand = board.HasCardInHand(Card.Cards.YOD_032) || board.HasCardInHand(Card.Cards.TIME_047);
                nonZeroCoyoteInHand = board.Hand != null
                    ? board.Hand.Where(h => h != null && h.Template != null && IsCoyoteCardVariant(h) && h.CurrentCost > 0)
                        .OrderByDescending(h => h.CurrentCost)
                        .FirstOrDefault()
                    : null;
            }
            catch
            {
                hasFelwingOrCoyoteInHand = false;
                nonZeroCoyoteInHand = null;
            }

            int enemyMinionsCount = 0;
            int enemyMinionsTotalHealth = 0;
            try
            {
                if (board.MinionEnemy != null)
                {
                    foreach (var m in board.MinionEnemy)
                    {
                        if (m == null || m.Template == null) continue;
                        enemyMinionsCount++;
                        enemyMinionsTotalHealth += Math.Max(0, m.CurrentHealth);
                    }
                }
            }
            catch
            {
                enemyMinionsCount = 0;
                enemyMinionsTotalHealth = 0;
            }

            // 速写让位：非斩杀时，如果速写可打且牌库仍有灵魂弹幕，并且当前被弃组件密度较高，
            // 则先速写补弹幕，再考虑超光子。
            try
            {
                bool hasBoardSlotForSketch = GetFriendlyBoardSlotsUsed(board) < 7;
                var sketchPlayableNow = board.Hand.FirstOrDefault(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.TOY_916
                    && h.CurrentCost <= board.ManaAvailable);
                int barrageRemainingForSketch = GetRemainingBarrageCountInDeck(board);

                var discardSetNow = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                var handForDiscardNow = GetHandCardsForDiscardLogic(board)
                    .Where(h => h != null && h.Template != null && h.Template.Id != Card.Cards.GAME_005)
                    .ToList();
                int discardCountNow = handForDiscardNow.Count(h => discardSetNow.Contains(h.Template.Id));

                bool shouldYieldPhotonToSketch = !lethalThisTurn
                    && hasBoardSlotForSketch
                    && sketchPlayableNow != null
                    && barrageRemainingForSketch > 0
                    && discardCountNow >= 3
                    && enemyMinionsCount <= 1;

                if (shouldYieldPhotonToSketch)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(1800));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-1800));
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-1500));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(9750));
                    try { p.ComboModifier = new ComboSet(sketchPlayableNow.Id); } catch { }
                    AddLog("[超光子弹幕-让位速写] 非斩杀且速写可打，牌库有灵魂弹幕(est=" + barrageRemainingForSketch
                        + ")，手牌被弃组件=" + discardCountNow + " => 先速写过牌，再考虑超光子");
                    return;
                }
            }
            catch { }

            // 空场防呆：非斩杀且无邪翼蝠/郊狼联动时，不在前中期直接拍超光子。
            // 目的：避免“2费空场交超光子”导致关键节奏丢失（提刀/过牌/地标线被打断）。
            if (!lethalThisTurn
                && enemyMinionsCount == 0
                && !hasFelwingOrCoyoteInHand)
            {
                int enemyHpArmor = 30;
                int aggroNow = 100;
                try { enemyHpArmor = board.HeroEnemy != null ? (board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor) : 30; } catch { enemyHpArmor = 30; }
                try { aggroNow = p != null && p.GlobalAggroModifier != null ? p.GlobalAggroModifier.Value : 100; } catch { aggroNow = 100; }

                // 空场推进窗口：中后期且敌方血线已进入推进区时，不再机械后置超光子。
                // 目的：避免“空场->分流/空过”错失伤害推进，尤其是敌方血量已被压低时。
                bool emptyBoardPushWindow = board.MaxMana >= 4
                    && (enemyHpArmor <= 18 || (aggroNow >= 105 && enemyHpArmor <= 24));
                if (emptyBoardPushWindow)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-900));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9250));
                    AddLog("[超光子弹幕-空场推进] 敌方空场且命中推进窗口(敌血=" + enemyHpArmor
                        + ",Aggro=" + aggroNow + ",MaxMana=" + board.MaxMana + ") => 前置超光子(9250/-900)");
                    return;
                }

                bool hasBetterCurvePlan = false;
                try
                {
                    hasBetterCurvePlan = board.HasCardInHand(Card.Cards.END_016)
                        || board.HasCardInHand(Card.Cards.TOY_916)
                        || board.HasCardInHand(Card.Cards.WON_103)
                        || board.HasCardInHand(Card.Cards.ULD_163);
                }
                catch { hasBetterCurvePlan = false; }

                if (board.MaxMana <= 3 && hasBetterCurvePlan)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-9999));
                    AddLog("[超光子弹幕-空场禁用] 前中期空场且手里有提刀/速写/地标/商贩节奏线 => 禁用超光子(9999/-9999)");
                    return;
                }

                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(2200));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-1500));
                AddLog("[超光子弹幕-空场后置] 敌方空场且无邪翼蝠/郊狼联动 => 延后超光子(2200/-1500)");
                return;
            }

            // 【优先级2銆戞柀鏉?窗口：提升优先级
            if (lethalThisTurn)
            {
                // 斩杀时：优先使用2费伤的超光子，避免先丢魂火/灵魂弹幕导致“同?但更费?的顺序
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-2500));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9800));

                // 鍦?光子可打时，临时抑制其他直伤（让引擎“先打超光子”落地）
                if (board.Hand.Any(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.EX1_308 && h.CurrentCost <= board.ManaAvailable))
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(800));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-9999));
                }
                if (board.Hand.Any(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.RLK_534 && h.CurrentCost <= board.ManaAvailable))
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(800));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-999));
                }

                // 斩杀护栏：若“爪子可稳定弃弹幕”则禁止直拍灵魂弹幕，固定走“超光子 -> 武器打脸”
                try
                {
                    bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                    bool hasClawEquipped = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0;
                    bool heroAlreadyAttacked = board.HeroFriend != null && board.HeroFriend.CountAttack > 0;
                    bool heroFrozen = board.HeroFriend != null && board.HeroFriend.IsFrozen;
                    bool clawLikelyCanSwing = hasClawEquipped && !heroAlreadyAttacked && !heroFrozen;

                    bool highestOnlySoulBarrage = false;
                    var handForDiscard = GetHandCardsForDiscardLogic(board);
                    if (handForDiscard != null && handForDiscard.Count > 0)
                    {
                        int maxCost = handForDiscard.Max(h => h != null ? h.CurrentCost : -1);
                        var highest = handForDiscard.Where(h => h != null && h.Template != null && h.CurrentCost == maxCost).ToList();
                        highestOnlySoulBarrage = highest.Count > 0
                            && highest.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                    }

                    if (clawLikelyCanSwing && highestOnlySoulBarrage && !enemyHasTaunt)
                    {
                        SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);

                        if (board.HeroEnemy != null)
                        {
                            try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, -2800, board.HeroEnemy.Id); } catch { }
                        }

                        if (board.MinionEnemy != null)
                        {
                            foreach (var m in board.MinionEnemy)
                            {
                                if (m == null || m.Template == null || m.IsTaunt) continue;
                                try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, 1200, m.Id); } catch { }
                            }
                        }

                        AddLog("[超光子弹幕优先级] 斩杀护栏：爪子可稳定弃弹幕(最高费仅RLK_534) => 禁止直拍灵魂弹幕(9999)，先超光子后武器打脸");
                    }
                }
                catch { }

                // 关键修正：斩杀窗口优先打出超光子，避免被?过牌随?0费铺场?等更高优先级作抢跑，导致错斩?
                try
                {
                    // 过牌随从
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(1200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-999));
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(1200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-999));

                    // 速写美术?
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(1200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-999));

                    // 0费恐怖海?铺场动作
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(1200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-999));
                }
                catch { }

                // 打完超光子后强制重算（下?姝?决定魂火/灵魂弹幕?
                p.ForceResimulation = true;
                AddLog("[超光子弹幕优先级] 斩杀窗口 => 强制先打超光子(9800)，暂时抑制魂火/灵魂弹幕");
                return;
            }

            // 銆愪簰鏂??鍒欍?若本回合可稳定打出“过期货?卖商 + 弃灵魂弹幕?，则超光子必』璁?綅銆?
            // 诉求：避免超光子抢节奏，导致错过“商贩弃弹幕 -> 后续?弹幕复制”的核心连段?
            try
            {
                bool hasMerchant = board.HasCardInHand(Card.Cards.ULD_163);
                bool hasBarrage = board.HasCardInHand(Card.Cards.RLK_534);
                if (hasMerchant && hasBarrage)
                {
                    var handForDiscardLogicLocal = GetHandCardsForDiscardLogic(board);
                    bool hasCoin = false;
                    try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }
                    int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);

                    var merchant = handForDiscardLogicLocal.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.ULD_163);

                    bool merchantPlayableNow = merchant != null
                        && merchant.CurrentCost <= effectiveMana
                        && GetFriendlyBoardSlotsUsed(board) < 7;

                    if (merchantPlayableNow)
                    {
                        var handWithoutMerchant = handForDiscardLogicLocal
                            .Where(h => h != null && h.Template != null && h.Template.Id != Card.Cards.ULD_163).ToList();

                        if (handWithoutMerchant != null && handWithoutMerchant.Count > 0)
                        {
                            int maxCost = handWithoutMerchant.Max(h => h.CurrentCost);
                            var highest = handWithoutMerchant.Where(h => h.CurrentCost == maxCost).ToList();
                            bool highestOnlySoulBarrage = highest.Count > 0 && highest.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);

                            if (highestOnlySoulBarrage)
                            {
                                var photon = handForDiscardLogicLocal.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.TIME_027);
                                bool photonPlayableNow = photon != null && photon.CurrentCost <= effectiveMana;
                                bool canPhotonThenMerchantSameTurn = photonPlayableNow
                                    && merchant != null
                                    && merchant.CurrentCost <= effectiveMana
                                    && photon.CurrentCost + merchant.CurrentCost <= effectiveMana;

                                // 修正：若本回合可以“超光子 + 商贩”同回合完成，则不再禁用超光子；
                                // 尤其敌方有场面时，先超光子清压再接商贩更稳。
                                if (canPhotonThenMerchantSameTurn && enemyMinionsCount > 0)
                                {
                                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-1800));
                                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9650));
                                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-1400));
                                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9550));
                                    AddLog("[超光子弹幕互斥修正] 可同回合完成超光子+商贩弃弹幕(敌方有场) => 先超光子后商贩");
                                    return;
                                }

                                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9999));
                                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-999));
                                AddLog("[超光子弹幕互斥] 本回合可稳定商贩弃最高费灵魂弹幕 => 禁用超光子(9999)，让位商贩弹幕连段");
                                return;
                            }
                        }
                    }
                }
            }
            catch { }

            // 【优先级2a】超光子先于郊狼：当手里有非0费郊狼且超光子可打，优先超光子压费后再重算??
            // 目的：避免被狗?浜?栉龙/攻击先后逻辑抢节奏，错过“弹幕先压费 -> 郊狼后续低费/0费落地?濄??
            if (nonZeroCoyoteInHand != null)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-1200));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9950));

                // 同轮内压后高频抢节奏?綔锛岀‘淇濃?先打超光子”落地??
                try
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(1000));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-999));
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(1000));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-999));
                    AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(1000));
                    AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(1000));
                }
                catch { }

                p.ForceResimulation = true;
                AddLog("[超光子弹幕优先级a] 手牌有非0费郊狼" + nonZeroCoyoteInHand.CurrentCost + "费 => 强制先打超光子压费(9950)并重算");
                return;
            }

            // 【优先级3a】手牌有超光?+（邪翼蝠/郊狼），且敌方随从?血量≤3鎴栫?场：提高超光子优先级，用于打脸?发减?节奏
            if (hasFelwingOrCoyoteInHand && (enemyMinionsCount == 0 || enemyMinionsTotalHealth <= 3))
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-450));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9000));
                AddLog("[超光子弹幕优先级a] 手牌有邪翼蝠/郊狼，且敌方总血=" + enemyMinionsTotalHealth + "(随从=" + enemyMinionsCount + ") => 提高优先级9000)");
                return;
            }

            // 【优先级3】敌方有多个低血量随?鈮?涓?墹3血)：AOE更有?
            if (lowHealthEnemyCount >= 2)
            {
                // 提升到高于过牌随?鐙楀?浜?9500)，避免?先过牌后打超光子?导致节?鏂?线错过??
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-450));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9700));
	                AddLog("[超光子弹幕优先级] 敌方有" + lowHealthEnemyCount + "个低血量随从 => AOE优先(9700)");
                return;
            }

            // 【优先级4 - 默认】正常持有：提高优先级不极?
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-150));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(8000));
                AddLog("[超光子弹幕默认] 正常持有 => 提高优先级8000)");
            }
        }

        /// <summary>
        /// 处理掩息海星（默认禁用，仅在沉默收益窗口放行）
        /// </summary>
        private void ProcessCard_TSC_926_Starfish(ProfileParameters p, Board board, bool lethalThisTurn)
        {
            if (p == null || board == null || board.Hand == null) return;

            var starfishes = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.TSC_926)
                .ToList();
            if (starfishes.Count == 0) return;

            bool hasBoardSlot = false;
            try { hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7; } catch { hasBoardSlot = false; }

            int minStarfishCost = 99;
            try { minStarfishCost = starfishes.Min(c => Math.Max(0, c.CurrentCost)); } catch { minStarfishCost = 99; }
            bool canPlayStarfishNow = hasBoardSlot && minStarfishCost <= board.ManaAvailable;
            if (!canPlayStarfishNow)
            {
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TSC_926, new Modifier(300)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TSC_926, new Modifier(-1800)); } catch { }
                return;
            }

            var enemyMinions = board.MinionEnemy != null
                ? board.MinionEnemy.Where(m => m != null).ToList()
                : new List<Card>();
            int enemyTauntCount = 0;
            int enemyLifestealCount = 0;
            int enemyBoardAttack = 0;
            try
            {
                enemyTauntCount = enemyMinions.Count(m => m.IsTaunt);
                enemyLifestealCount = enemyMinions.Count(m => m.IsLifeSteal);
                enemyBoardAttack = enemyMinions.Sum(m => Math.Max(0, m.CurrentAtk));
            }
            catch
            {
                enemyTauntCount = 0;
                enemyLifestealCount = 0;
                enemyBoardAttack = 0;
            }

            int enemyHpArmor = 0;
            int myHpArmor = 0;
            try { enemyHpArmor = board.HeroEnemy != null ? Math.Max(0, board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor) : 0; } catch { enemyHpArmor = 0; }
            try { myHpArmor = board.HeroFriend != null ? Math.Max(0, board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) : 0; } catch { myHpArmor = 0; }

            int friendlyReadyAttack = 0;
            try
            {
                friendlyReadyAttack = board.MinionFriend != null
                    ? board.MinionFriend.Where(m => m != null && m.CanAttack).Sum(m => Math.Max(0, m.CurrentAtk))
                    : 0;
            }
            catch { friendlyReadyAttack = 0; }
            try
            {
                if (board.HeroFriend != null && board.HeroFriend.CanAttack)
                    friendlyReadyAttack += Math.Max(0, board.HeroFriend.CurrentAtk);
            }
            catch { }

            int manaAfterStarfish = Math.Max(0, board.ManaAvailable - minStarfishCost);
            int handDamageAfterStarfish = 0;
            try { handDamageAfterStarfish = EstimateHandFaceDamageWithinManaExcluding(board, manaAfterStarfish, Card.Cards.TSC_926); } catch { handDamageAfterStarfish = 0; }

            bool canOpenLethalBySilence = enemyTauntCount > 0
                && (friendlyReadyAttack + handDamageAfterStarfish >= enemyHpArmor)
                && enemyHpArmor > 0;
            bool defensiveSilenceNeed = enemyLifestealCount > 0
                && myHpArmor > 0
                && enemyBoardAttack >= Math.Max(6, myHpArmor - 2);
            bool tauntBreakDiscardSetupNeed = false;
            string tauntBreakDiscardSetupReason = null;
            try
            {
                tauntBreakDiscardSetupNeed = ShouldPrioritizeStarfishForTauntBreakDiscardSetup(
                    board, lethalThisTurn, enemyTauntCount, friendlyReadyAttack, out tauntBreakDiscardSetupReason);
            }
            catch
            {
                tauntBreakDiscardSetupNeed = false;
                tauntBreakDiscardSetupReason = null;
            }

            bool shouldUseStarfish = canOpenLethalBySilence
                || (lethalThisTurn && enemyTauntCount > 0)
                || defensiveSilenceNeed
                || tauntBreakDiscardSetupNeed;

            if (!shouldUseStarfish)
            {
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TSC_926, new Modifier(9999)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TSC_926, new Modifier(-9999)); } catch { }
                foreach (var s in starfishes)
                {
                    if (s == null) continue;
                    try { p.CastMinionsModifiers.AddOrUpdate(s.Id, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(s.Id, new Modifier(-9999)); } catch { }
                }
                AddLog("[掩息海星-硬规则] 未命中沉默收益窗口(敌嘲讽=" + enemyTauntCount
                    + ",敌吸血=" + enemyLifestealCount
                    + ",可开斩=" + (canOpenLethalBySilence ? "Y" : "N")
                    + ",弃弹幕推进线=" + (tauntBreakDiscardSetupNeed ? "Y" : "N")
                    + ",敌血=" + enemyHpArmor + ") => 禁止使用(9999/-9999)");
                return;
            }

            int starfishCastMod = tauntBreakDiscardSetupNeed ? -2600 : -2200;
            int starfishOrderMod = tauntBreakDiscardSetupNeed ? 9950 : 9800;
            try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TSC_926, new Modifier(starfishCastMod)); } catch { }
            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TSC_926, new Modifier(starfishOrderMod)); } catch { }
            foreach (var s in starfishes)
            {
                if (s == null) continue;
                try { p.CastMinionsModifiers.AddOrUpdate(s.Id, new Modifier(starfishCastMod)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(s.Id, new Modifier(starfishOrderMod)); } catch { }
            }

            if (tauntBreakDiscardSetupNeed)
            {
                try { SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999); } catch { }
                p.ForceResimulation = true;
                AddLog("[掩息海星-放行] 命中弃弹幕推进窗口("
                    + (string.IsNullOrEmpty(tauntBreakDiscardSetupReason) ? "解嘲讽走脸压费" : tauntBreakDiscardSetupReason)
                    + ",敌嘲讽=" + enemyTauntCount
                    + ",可走脸伤害=" + friendlyReadyAttack
                    + ") => 先海星解嘲讽，再走脸压费/挥刀；禁用直拍灵魂弹幕(9999)");
                return;
            }

            AddLog("[掩息海星-放行] 命中沉默收益窗口(敌嘲讽=" + enemyTauntCount
                + ",敌吸血=" + enemyLifestealCount
                + ",可开斩=" + (canOpenLethalBySilence ? "Y" : "N")
                + ",敌血=" + enemyHpArmor + ") => 前置沉默(-2200/9800)");
        }

        /// <summary>
        /// 处理狂暴邪翼蝠的?鏈夐?昏緫
        /// </summary>
        private void ProcessCard_YOD_032_Felwing(ProfileParameters p, Board board, 
            bool canAttackFace, bool felwingNotDiscountedYet, int felwingMinCost, bool lethalThisTurn)
        {
            // ==============================================
            // Card.Cards.YOD_032 - 狂暴邪翼蝠（随从）
            // 说明：修正“伪可执行动作”误判，避免减费邪翼蝠在无动作局空过。
            // ==============================================

            if (board == null || board.Hand == null) return;

            var felwings = board.Hand.Where(h => IsFelwingCardVariant(h)).ToList();
            if (felwings.Count == 0) return;

            bool hasBoardSlot = false;
            try { hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7; } catch { hasBoardSlot = false; }

            // 对齐郊狼口径：非斩杀时，若可先下狗头人/栉龙过牌，则邪翼蝠让位（含0费）。
            bool canPlayDrawMinionNowForFelwing = false;
            bool felwingUnlocksClawDiscardWindowForDrawYield = false;
            try
            {
                canPlayDrawMinionNowForFelwing = !lethalThisTurn
                    && hasBoardSlot
                    && board.Hand.Any(h => h != null
                        && h.Template != null
                        && h.CurrentCost <= board.ManaAvailable
                        && (h.Template.Id == Card.Cards.LOOT_014 || h.Template.Id == Card.Cards.TLC_603)
                        && !IsForeignInjectedCardForDiscardLogic(board, h));

                if (canPlayDrawMinionNowForFelwing)
                {
                    var discardSetForDrawYield = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                    felwingUnlocksClawDiscardWindowForDrawYield = CanFelwingUnlockClawDiscardWindow(board, discardSetForDrawYield);
                }
            }
            catch
            {
                canPlayDrawMinionNowForFelwing = false;
                felwingUnlocksClawDiscardWindowForDrawYield = false;
            }

            // 先挥刀窗口护栏：
            // 已装备时空之爪且本回合可攻击，且当前最高费组含灵魂弹幕时，
            // 邪翼蝠一律让位先挥刀，等待弃牌后重算再决定是否落地。
            bool shouldYieldFelwingToClawSwing = false;
            try
            {
                bool clawEquippedNow = board.WeaponFriend != null
                    && board.WeaponFriend.Template != null
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                    && board.WeaponFriend.CurrentDurability > 0;
                bool heroCanSwingNow = clawEquippedNow
                    && board.HeroFriend != null
                    && board.HeroFriend.CanAttack;

                if (heroCanSwingNow)
                {
                    int maxCostNow = GetMaxCostInHand(board);
                    var highestNow = GetHighestCostCardsInHand(board, maxCostNow);
                    var discardSet = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                    bool hasDiscardAtMaxForWeaponNow = highestNow.Any(c => c != null && c.Template != null && discardSet.Contains(c.Template.Id));
                    bool hasSoulBarrageAtMaxNow = highestNow.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);
                    shouldYieldFelwingToClawSwing = hasDiscardAtMaxForWeaponNow && hasSoulBarrageAtMaxNow;
                }
            }
            catch { shouldYieldFelwingToClawSwing = false; }

            if (shouldYieldFelwingToClawSwing)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-9999));
                foreach (var f in felwings)
                {
                    if (f == null) continue;
                    try { p.CastMinionsModifiers.AddOrUpdate(f.Id, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(f.Id, new Modifier(-9999)); } catch { }
                }
                AddLog("[狂暴邪翼蝠-让位挥刀] 已装备时空之爪且可攻击，最高费含灵魂弹幕 => 先挥刀弃牌后再决定是否出邪翼蝠(9999/-9999)");
                return;
            }

            // 0费：保持高优先；场位满时不判定为可执行动作
            var zeroFelwing = felwings.FirstOrDefault(f => hasBoardSlot && f.CurrentCost == 0 && f.CurrentCost <= board.ManaAvailable);
            if (zeroFelwing != null)
            {
                if (canPlayDrawMinionNowForFelwing && !felwingUnlocksClawDiscardWindowForDrawYield)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(3200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-3800));
                    try { p.CastMinionsModifiers.AddOrUpdate(zeroFelwing.Id, new Modifier(3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(zeroFelwing.Id, new Modifier(-3800)); } catch { }
                    AddLog("[狂暴邪翼蝠-0费让位过牌] 可先打狗头人/栉龙且未命中提刀转化窗口 => 暂缓邪翼蝠(-3800/3200)");
                    return;
                }

                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-550));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(8500));
                AddLog("[狂暴邪翼蝠-优先级] 0费邪翼蝠 => 最高优先级(8500/-550)");
                return;
            }

            var nonZeroAny = felwings
                .Where(f => f.CurrentCost > 0)
                .OrderBy(f => f.CurrentCost)
                .FirstOrDefault();
            if (nonZeroAny == null) return;

            // 场位校验：场位已满时，不把邪翼蝠视为“当前可打”
            var nonZeroPlayable = felwings
                .Where(f => hasBoardSlot && f.CurrentCost > 0 && f.CurrentCost <= board.ManaAvailable)
                .OrderBy(f => f.CurrentCost)
                .FirstOrDefault();

            bool canTapNow = false;
            bool canClickCave = false;
            bool canClickCaveExecutable = false;
            bool hasOtherPlayableAction = false;
            var otherPlayableActionIds = new HashSet<Card.Cards>();

            try { canTapNow = CanUseLifeTapNow(board); }
            catch { canTapNow = false; }

            try
            {
                var clickableCaveOnBoard = board.MinionFriend?.FirstOrDefault(x => x != null && x.Template != null
                    && x.Template.Id == Card.Cards.WON_103 && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
                canClickCave = clickableCaveOnBoard != null;
            }
            catch { canClickCave = false; }

            // “可点地标”改为“本回合真实可执行地标动作”：
            // 至少覆盖手里无被弃组件时的硬禁口径，避免 cave=Y 误判导致邪翼蝠空过。
            try
            {
                var discardSetForCave = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                int handCountNow = handForDiscardLogic.Count;
                bool hasDiscardComponentForCave = handForDiscardLogic.Any(h => h != null && h.Template != null
                    && h.Template.Id != Card.Cards.WON_103
                    && h.Template.Id != Card.Cards.GAME_005
                    && discardSetForCave.Contains(h.Template.Id));
                bool caveBlockedByNoDiscard = handCountNow > 0 && !hasDiscardComponentForCave;
                canClickCaveExecutable = canClickCave && !caveBlockedByNoDiscard;
            }
            catch { canClickCaveExecutable = canClickCave; }

            // 只统计“真实可执行动作”：
            // - 随从要求场位；
            // - 排除邪翼蝠/郊狼/硬币/魂火/被弃组件；
            // - 排除当前口径会被禁用的4费恐怖海盗。
            try
            {
                var discardSet = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                hasOtherPlayableAction = handForDiscardLogic.Any(h =>
                {
                    if (h == null || h.Template == null) return false;
                    if (h.Id == nonZeroAny.Id) return false;
                    if (IsFelwingCardVariant(h)) return false;
                    if (IsCoyoteCardVariant(h)) return false;
                    if (h.Template.Id == Card.Cards.GAME_005) return false;
                    if (h.Template.Id == Card.Cards.EX1_308) return false;
                    if (discardSet.Contains(h.Template.Id)) return false;
                    if (h.Template.Id == Card.Cards.CORE_NEW1_022 && h.CurrentCost >= 4) return false;
                    if (h.CurrentCost > board.ManaAvailable) return false;
                    if (h.Type == Card.CType.MINION && !hasBoardSlot) return false;
                    try { otherPlayableActionIds.Add(h.Template.Id); } catch { }
                    return true;
                });
            }
            catch { hasOtherPlayableAction = false; }

            bool executableFace = canAttackFace;
            bool executableTap = canTapNow;
            bool executableCave = canClickCaveExecutable;
            bool executableOther = hasOtherPlayableAction;
            bool otherPlayableOnlyLowTempo = hasOtherPlayableAction
                && otherPlayableActionIds.Count > 0
                && otherPlayableActionIds.All(id => id == Card.Cards.ULD_163 || id == Card.Cards.DRG_056);

            AddLog("[邪翼蝠-动作判定] executableFace=" + (executableFace ? "Y" : "N")
                + ",executableTap=" + (executableTap ? "Y" : "N")
                + ",executableCave=" + (executableCave ? "Y" : "N")
                + ",executableOther=" + (executableOther ? "Y" : "N"));

            bool hasDirectDiscountWindow = executableFace || executableCave;
            bool hasFurtherActionWindow = hasDirectDiscountWindow || felwingNotDiscountedYet || executableTap || executableOther;

            // 当前还打不出，但后续可能压费：先预后置，避免同一动作里刚降费就直拍。
            if (nonZeroPlayable == null)
            {
                if (hasFurtherActionWindow)
                {
                    int preDelayCast = hasDirectDiscountWindow ? 2600 : 1500;
                    int preDelayOrder = hasDirectDiscountWindow ? -3200 : -2400;
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(preDelayCast));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(preDelayOrder));
                    AddLog("[狂暴邪翼蝠-预后置] 非0费(" + nonZeroAny.CurrentCost + "费)当前不可打，且后续仍可压费/有其它动作"
                        + "(face=" + (executableFace ? "Y" : "N")
                        + ",tap=" + (executableTap ? "Y" : "N")
                        + ",cave=" + (executableCave ? "Y" : "N")
                        + ",other=" + (executableOther ? "Y" : "N")
                        + ") => 预先强后置(" + preDelayOrder + "/" + preDelayCast + ")");
                }
                return;
            }

            bool isDiscountedPlayable = false;
            try
            {
                isDiscountedPlayable = nonZeroPlayable.Template != null
                    && nonZeroPlayable.CurrentCost < nonZeroPlayable.Template.Cost;
            }
            catch { isDiscountedPlayable = false; }

            bool deadTurnRisk = !executableFace && !executableTap && !executableCave && !executableOther;
            bool shouldStrongDelayNow = hasFurtherActionWindow && !deadTurnRisk;

            // 用户口径：减费后的邪翼蝠“能打就打”，避免被通用后置吞掉。
            // 仅在当前不存在可继续压费动作（走脸/点地标）时强前置；
            // 若仍有压费窗口，则继续保留后置等待更优减费。
            bool shouldFrontloadDiscountedPlayable = false;
            bool shouldYieldDiscountedFelwingToPhoton = false;
            try
            {
                bool photonPlayableNow = board.Hand != null
                    && board.Hand.Any(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.TIME_027
                        && h.CurrentCost <= board.ManaAvailable);
                bool enemyHasMinionsNow = board.MinionEnemy != null && board.MinionEnemy.Any(e => e != null);
                shouldYieldDiscountedFelwingToPhoton = photonPlayableNow && enemyHasMinionsNow;

                bool hasFurtherDiscountAction = executableFace || executableCave;
                bool relaxedAfterAttack = isDiscountedPlayable
                    && nonZeroPlayable.CurrentCost <= board.ManaAvailable
                    && !hasFurtherDiscountAction
                    && !executableFace
                    && !executableCave
                    && !shouldYieldDiscountedFelwingToPhoton;

                shouldFrontloadDiscountedPlayable = isDiscountedPlayable
                    && nonZeroPlayable.CurrentCost <= board.ManaAvailable
                    && !hasFurtherDiscountAction
                    && !shouldYieldDiscountedFelwingToPhoton
                    && ((!hasOtherPlayableAction || otherPlayableOnlyLowTempo) || relaxedAfterAttack);
            }
            catch { shouldFrontloadDiscountedPlayable = false; }

            if (shouldFrontloadDiscountedPlayable)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-1400));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(9300));
                try { p.CastMinionsModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(-1400)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(9300)); } catch { }
                AddLog("[狂暴邪翼蝠-减费可打前置] 已减费(" + nonZeroPlayable.CurrentCost + "/"
                    + (nonZeroPlayable.Template != null ? nonZeroPlayable.Template.Cost : nonZeroPlayable.CurrentCost)
                    + ")且无进一步压费动作(face/cave=N) => 前置使用(9300/-1400)");
                return;
            }

            if (canPlayDrawMinionNowForFelwing && !felwingUnlocksClawDiscardWindowForDrawYield)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(3200));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-3800));
                try { p.CastMinionsModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(3200)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(-3800)); } catch { }
                AddLog("[狂暴邪翼蝠-让位过牌] 可先打狗头人/栉龙且未命中提刀转化窗口 => 暂缓邪翼蝠(-3800/3200)");
                return;
            }
            if (isDiscountedPlayable && shouldYieldDiscountedFelwingToPhoton)
            {
                AddLog("[狂暴邪翼蝠-让位超光子] 已减费可打，但本回合超光子可打且敌方有场 => 邪翼蝠不抢先");
            }

            bool shouldFrontloadFullCostPlayable = false;
            try
            {
                bool isFullCostPlayable = nonZeroPlayable.Template != null
                    && nonZeroPlayable.CurrentCost == nonZeroPlayable.Template.Cost
                    && nonZeroPlayable.CurrentCost <= board.ManaAvailable
                    && nonZeroPlayable.Template.Cost >= 4;
                bool noFurtherDiscountWindow = !executableFace && !executableTap && !executableCave;
                shouldFrontloadFullCostPlayable = isFullCostPlayable
                    && noFurtherDiscountWindow
                    && (!hasOtherPlayableAction || otherPlayableOnlyLowTempo);
            }
            catch { shouldFrontloadFullCostPlayable = false; }

            if (shouldFrontloadFullCostPlayable)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-1050));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(9150));
                try { p.CastMinionsModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(-1050)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(9150)); } catch { }
                AddLog("[狂暴邪翼蝠-四费可打前置] 无压费窗口(face/tap/cave=N)且其它仅低价值动作 => 直接前置(9150/-1050)");
                return;
            }

            bool nonZeroPlayableIsTemporary = false;
            try { nonZeroPlayableIsTemporary = IsTemporaryCard(board, nonZeroPlayable); } catch { nonZeroPlayableIsTemporary = false; }
            if (nonZeroPlayableIsTemporary)
            {
                // 临时牌仅在“确有后续真实动作”时后置；否则直接打，避免回合末消失。
                if (!(deadTurnRisk && isDiscountedPlayable))
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(3200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-3600));
                    try { p.CastMinionsModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(-3600)); } catch { }
                    AddLog("[狂暴邪翼蝠-临时后置] 临时牌且非0费(" + nonZeroPlayable.CurrentCost + "费) => 后置(-3600/3200)，等待压费窗口");
                    return;
                }
            }

            // 防空过扩展：减费邪翼蝠不限1费，只要“无真实动作”就强推出。
            if (deadTurnRisk && isDiscountedPlayable)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-900));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(9200));
                try { p.CastMinionsModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(-900)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(9200)); } catch { }
                AddLog("[邪翼蝠-防空过] 已减费(" + nonZeroPlayable.CurrentCost + "/" + (nonZeroPlayable.Template != null ? nonZeroPlayable.Template.Cost : nonZeroPlayable.CurrentCost)
                    + ")且无真实动作 => 强推出牌(9200/-900)");
                return;
            }

            // 非0费：默认后置；有压费/其他真实动作时强后置
            if (shouldStrongDelayNow)
            {
                int delayCast = hasDirectDiscountWindow ? 2600 : 1200;
                int delayOrder = hasDirectDiscountWindow ? -3200 : -2200;
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(delayCast));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(delayOrder));
                try { p.CastMinionsModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(delayCast)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(nonZeroPlayable.Id, new Modifier(delayOrder)); } catch { }
                AddLog("[狂暴邪翼蝠-后置] 非0费(" + nonZeroPlayable.CurrentCost + "费)且仍可压费/有其它动作"
                    + "(face=" + (executableFace ? "Y" : "N")
                    + ",tap=" + (executableTap ? "Y" : "N")
                    + ",cave=" + (executableCave ? "Y" : "N")
                    + ",other=" + (executableOther ? "Y" : "N")
                    + ") => 强后置(" + delayOrder + "/" + delayCast + ")");
                return;
            }

            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(500));
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-1000));
            AddLog("[狂暴邪翼蝠-默认后置] 非0费(" + nonZeroPlayable.CurrentCost + "费) => 稍后使用(-1000/500)");
        }

        /// <summary>
        /// 处理维希?的窟穴的?鏈夐?昏緫
        /// </summary>
        private void ProcessCard_WON_103_Cave(ProfileParameters p, Board board, 
            int discardPayoffCount, bool hasOtherRealActions, int handCount, bool hasMerchantOnBoard,
            HashSet<Card.Cards> discardComponents)
        {
            // ==============================================
            // Card.Cards.WON_103 - 维希?的窟穴（地标?
            // 说明：所有关于窟穴的?都在这里，按优先级高到?
            // ==============================================

            bool hasCoin = false;
            try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { }
            int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);

            var caveInHand = board.Hand.FirstOrDefault(c => c != null && c.Template != null 
                && c.Template.Id == Card.Cards.WON_103);
            bool canPlayCave = caveInHand != null && caveInHand.CurrentCost <= effectiveMana;
            bool needCoinToPlayCave = caveInHand != null && hasCoin
                && caveInHand.CurrentCost > board.ManaAvailable && caveInHand.CurrentCost <= effectiveMana;

            var clickableCaveOnBoard = board.MinionFriend?.FirstOrDefault(x => x != null && x.Template != null
                && x.Template.Id == Card.Cards.WON_103 && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
            bool canClickCaveByTag = clickableCaveOnBoard != null;
            bool canClickCave = canClickCaveByTag;
            string caveExhaustedTagSnapshot = "";

            // 銆愮?规则】牌库剩?=2：彻底不用窟穴（不点场上、不从手拍）
            // 说明：此时窟穴发现价值极低，且弃牌可能导致关键资源损失；按你的口径直??姝???
            try
            {
                if (board.FriendDeckCount <= 2)
                {
                    if (canClickCave)
                    {
                        p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999));
                        canClickCave = false;
                    }

                    // 从手拍窟穴的路径主要依赖 ComboSet；这里额外加?灞傗?强禁用”权重防?粯璁??辑误拍?
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-999)); } catch { }

                    AddLog("[维希度斯的窟穴硬规则] 牌库剩余<=2(当前" + board.FriendDeckCount + ") => 场上地标与手牌地标都不用(9999)");
                    return;
                }
            }
            catch { }

            // 若场上地标可点，但仍有可攻击随从，则等随从先攻击完再点地标。
            try
            {
                if (canClickCave && board.MinionFriend != null)
                {
                    bool hasFriendlyAttacker = board.MinionFriend.Any(m => m != null && m.CanAttack);
                    if (hasFriendlyAttacker)
                    {
                        try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(900)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-700)); } catch { }
                        p.ForceResimulation = true;
                        AddLog("[维希度斯的窟穴让位攻击] 场上有可攻击随从 => 先攻击再点地标");
                        return;
                    }
                }
            }
            catch { }

            bool hasAnyCaveOnBoard = false;
            try
            {
                hasAnyCaveOnBoard = board.MinionFriend != null && board.MinionFriend.Any(x => x != null && x.Template != null
                    && x.Template.Id == Card.Cards.WON_103);
            }
            catch { hasAnyCaveOnBoard = false; }

            // 诊断：场上有窟穴但不可点（?氬父鏄?EXHAUSTED!=0 或引擎未把它暴露成可点目标）
            try
            {
                var cavesOnBoard = board.MinionFriend?.Where(x => x != null && x.Template != null
                    && x.Template.Id == Card.Cards.WON_103).ToList();
                int cavesOnBoardCount = cavesOnBoard == null ? 0 : cavesOnBoard.Count;
                if (cavesOnBoardCount > 0 && !canClickCaveByTag)
                {
                    string exhaustedList = "";
                    try
                    {
                        exhaustedList = string.Join(",", cavesOnBoard.Select(c => GetTag(c, Card.GAME_TAG.EXHAUSTED).ToString()));
                    }
                    catch { exhaustedList = "?"; }

                    caveExhaustedTagSnapshot = exhaustedList;
                    AddLog("[维希度斯的窟穴诊断] 场上窟穴x" + cavesOnBoardCount + " 但当前不可点(原始EXHAUSTED=" + exhaustedList + ")");
                }
            }
            catch { }

            // 容错：部分对局中 EXHAUSTED 标签会出现滞后，导致“场上窟穴可点”被误判。
            // 当场上有窟穴且手里存在被弃组件时，放行一次“尝试点击场上窟穴”的路径。
            try
            {
                if (!canClickCave && hasAnyCaveOnBoard)
                {
                    var discardSetForProbe = discardComponents != null && discardComponents.Count > 0
                        ? new HashSet<Card.Cards>(discardComponents)
                        : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                    var handForProbe = GetHandCardsForDiscardLogic(board).Where(c => c != null && c.Template != null
                        && c.Template.Id != Card.Cards.WON_103
                        && c.Template.Id != Card.Cards.GAME_005).ToList();
                    int probeTotal = handForProbe.Count;
                    int probeDiscard = handForProbe.Count(c => discardSetForProbe.Contains(c.Template.Id));

                    if (probeDiscard > 0)
                    {
                        canClickCave = true;
                        AddLog("[维希度斯的窟穴探测放行] 场上有窟穴且手里有被弃组件(" + probeDiscard + "/" + probeTotal
                            + ")，EXHAUSTED标签=" + (string.IsNullOrEmpty(caveExhaustedTagSnapshot) ? "?" : caveExhaustedTagSnapshot)
                            + " => 优先尝试点击场上窟穴");
                    }
                }
            }
            catch { }

            if (!canPlayCave && !canClickCave) return;

            // 前置?：如果本回合可以使用时空之爪(从手装备)，且当前“最高费组?包含被弃组件，则优先时空之爪，不从手拍窟穴銆?
            // 说明：该?仅限制?从手拍地标”，不影响场上窟穴点击?昏緫銆?
            try
            {
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                bool hasClawEquippedNow = board.WeaponFriend != null && board.WeaponFriend.Template != null
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016;

                bool canPlayClawNow = !hasClawEquippedNow
                    && handForDiscardLogic.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.END_016 && h.CurrentCost <= effectiveMana);

                bool highestHasDiscardComponent = false;
                if (canPlayClawNow && handForDiscardLogic.Count > 0 && discardComponents != null && discardComponents.Count > 0)
                {
                    int maxCost = handForDiscardLogic.Max(h => h != null ? h.CurrentCost : 0);
                    highestHasDiscardComponent = handForDiscardLogic.Any(h => h != null && h.Template != null
                        && h.CurrentCost == maxCost
                        && discardComponents.Contains(h.Template.Id));
                }

                if (canPlayCave && canPlayClawNow && highestHasDiscardComponent)
                {
                    canPlayCave = false;
                    AddLog("[维希度斯的窟穴前置] 可用时空之爪且最高费组包含被弃组件=> 优先时空之爪，本回合不从手拍窟穴");
                }
            }
            catch { }


            // 璁＄“手里是?被弃组件”：
            // 新口径：只要手里有被弃组件（排除地标本身/硬币），就允许使?标；不再依赖占比阈?笺??
            int caveCountInHand = 0;
            int nonCaveNonCoinCount = 0;
            int discardComponentNonCaveNonCoinCount = 0;
            int nonSoulfireNonCaveNonCoinCount = 0;
            int discardNonSoulfireNonCaveNonCoinCount = 0;
            bool allNonSoulfireNonCaveNonCoinAreDiscard = false;
            bool hasLowCostDrawOrKeyForCavePass = false; // <=4费含狗头人/栉龙/咒怨/速写
            try
            {
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                caveCountInHand = handForDiscardLogic.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103);
                nonCaveNonCoinCount = handForDiscardLogic.Count(c => c != null && c.Template != null
                    && c.Template.Id != Card.Cards.WON_103
                    && c.Template.Id != Card.Cards.GAME_005
                    && !ShouldIgnoreCardForCaveDiscardRatio(c));
                discardComponentNonCaveNonCoinCount = handForDiscardLogic.Count(c => c != null && c.Template != null
                    && c.Template.Id != Card.Cards.WON_103
                    && c.Template.Id != Card.Cards.GAME_005
                    && !ShouldIgnoreCardForCaveDiscardRatio(c)
                    && discardComponents != null && discardComponents.Contains(c.Template.Id));

                nonSoulfireNonCaveNonCoinCount = handForDiscardLogic.Count(c => c != null && c.Template != null
                    && c.Template.Id != Card.Cards.WON_103
                    && c.Template.Id != Card.Cards.GAME_005
                    && c.Template.Id != Card.Cards.EX1_308
                    && !ShouldIgnoreCardForCaveDiscardRatio(c));
                discardNonSoulfireNonCaveNonCoinCount = handForDiscardLogic.Count(c => c != null && c.Template != null
                    && c.Template.Id != Card.Cards.WON_103
                    && c.Template.Id != Card.Cards.GAME_005
                    && c.Template.Id != Card.Cards.EX1_308
                    && !ShouldIgnoreCardForCaveDiscardRatio(c)
                    && discardComponents != null && discardComponents.Contains(c.Template.Id));
                allNonSoulfireNonCaveNonCoinAreDiscard = nonSoulfireNonCaveNonCoinCount > 0
                    && discardNonSoulfireNonCaveNonCoinCount == nonSoulfireNonCaveNonCoinCount;

                hasLowCostDrawOrKeyForCavePass = handForDiscardLogic.Any(c => c != null
                    && c.Template != null
                    && c.CurrentCost <= 4
                    && (c.Template.Id == Card.Cards.LOOT_014
                        || c.Template.Id == Card.Cards.TLC_603
                        || c.Template.Id == Card.Cards.TLC_451
                        || c.Template.Id == Card.Cards.TOY_916));
            }
            catch { }

            bool hasDiscardComponentForCave = discardComponentNonCaveNonCoinCount > 0;
            bool hasOnlyCaveInHand = caveCountInHand > 0 && nonCaveNonCoinCount == 0;
            double discardRatioForCave = nonCaveNonCoinCount > 0
                ? (double)discardComponentNonCaveNonCoinCount / Math.Max(1, nonCaveNonCoinCount)
                : 0.0;
            bool lowDiscardRatioForCave = nonCaveNonCoinCount > 0 && discardRatioForCave < 0.2;
 
            // 用户口径：被弃组件占比>=20%即可优先地标（点场上优先）
            if (discardRatioForCave >= 0.2 && canClickCave)
            {
                try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                try { LogHandCards(board); } catch { }
                p.ForceResimulation = true;
                AddLog("[维希度斯的窟穴优先级] 被弃占比>=20%(" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount
                    + ") => 优先点场上地标(9999/-1800)");
                return;
            }

            // 低被弃占比：当手上被弃组件占比 <20% 且手里有咒怨之墓可用时，
            // 暂缓手上窟穴，优先用墓去找被弃组件（避免早拍地标浪费抽弃窗口）。
            try
            {
                var tombInHand = board.Hand != null
                    ? board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.TLC_451)
                    : null;
                bool canPlayTombNow = tombInHand != null && tombInHand.CurrentCost <= effectiveMana;

                if (!BoardHelper.IsLethalPossibleThisTurn(board)
                    && canPlayCave
                    && canPlayTombNow
                    && lowDiscardRatioForCave)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(1800)); } catch { }
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(1800)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800)); } catch { }
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-2200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(9850)); } catch { }
                    try
                    {
                        if (tombInHand != null)
                        {
                            p.CastSpellsModifiers.AddOrUpdate(tombInHand.Id, new Modifier(-2200));
                            p.PlayOrderModifiers.AddOrUpdate(tombInHand.Id, new Modifier(9850));
                        }
                        if (caveInHand != null)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(caveInHand.Id, new Modifier(1800));
                            p.LocationsModifiers.AddOrUpdate(caveInHand.Id, new Modifier(1800));
                            p.PlayOrderModifiers.AddOrUpdate(caveInHand.Id, new Modifier(-1800));
                        }
                    }
                    catch { }
                    p.ForceResimulation = true;
                    AddLog("[维希度斯的窟穴让位墓] 手牌被弃占比<20%("
                        + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount
                        + ")且墓可用 => 暂缓手上地标，优先咒怨之墓(-2200/9850)");
                    return;
                }
            }
            catch { }

            // 保命抢伤特判（加强版）：
            // 命中以下硬条件时，直接优先点场上窟穴，不再先过牌随从后点地标：
            // - canClickCave == true
            // - 手牌无被弃组件(!hasDiscardComponentForCave)
            // - 本回合非已斩杀(!BoardHelper.IsLethalPossibleThisTurn(board))
            // - 我方血甲 <= 6
            // - 敌方血甲 <= 10
            // - 敌方场攻 >= 3
            try
            {
                int myHpArmorForCave = 0;
                int enemyHpArmorForCave = 99;
                int enemyPressureForCave = 0;
                try
                {
                    myHpArmorForCave = (board.HeroFriend != null ? board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor : 0);
                    enemyHpArmorForCave = (board.HeroEnemy != null ? board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor : 99);
                    enemyPressureForCave = board.MinionEnemy != null
                        ? board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk))
                        : 0;
                }
                catch
                {
                    myHpArmorForCave = 0;
                    enemyHpArmorForCave = 99;
                    enemyPressureForCave = 0;
                }

                bool forceCaveForDesperateDamageDig = canClickCave
                    && !hasDiscardComponentForCave
                    && !BoardHelper.IsLethalPossibleThisTurn(board)
                    && myHpArmorForCave <= 6
                    && enemyHpArmorForCave <= 10
                    && enemyPressureForCave >= 3;

                if (forceCaveForDesperateDamageDig)
                {
                    // 强推场上地标点击（模板+实例双写），避免后续规则反压。
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                    try
                    {
                        if (clickableCaveOnBoard != null)
                        {
                            try { p.LocationsModifiers.AddOrUpdate(clickableCaveOnBoard.Id, new Modifier(-3200)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(clickableCaveOnBoard.Id, new Modifier(9999)); } catch { }
                        }
                    }
                    catch { }

                    // 同步压后狗头人与分流，防止“先过牌/先分流”抢跑。
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(2600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-2600)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(1800)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(1800)); } catch { }
                    p.ForceResimulation = true;
                    AddLog("[维希度斯的窟穴保命抢伤-硬前置] 命中硬条件(canClick=Y,discard=0,nonLethal=Y,hp="
                        + myHpArmorForCave
                        + ",enemyHp=" + enemyHpArmorForCave
                        + ",enemyPressure=" + enemyPressureForCave
                        + ") => 直接优先点场上窟穴，并压后狗头人/分流(-3200/9999)");
                    return;
                }
            }
            catch { }

            // 顺序修正：可点场上地标时，若本回合可先下过牌随从（优先栉龙，其次狗头人），
            // 则先下过牌随从再点地标，避免地标前置导致低费回合动作断档/空过。
            try
            {
                bool hasBoardSlotForDrawMinion = GetFriendlyBoardSlotsUsed(board) < 7;
                var playableDrawMinions = board.Hand
                    .Where(c => c != null
                        && c.Template != null
                        && c.Type == Card.CType.MINION
                        && c.CurrentCost <= board.ManaAvailable
                        && hasBoardSlotForDrawMinion
                        && (c.Template.Id == Card.Cards.TLC_603 || c.Template.Id == Card.Cards.LOOT_014)
                        && !isDisabledInRulesSet(p.CastMinionsModifiers, c))
                    .ToList();

                bool lowManaTurn = board.MaxMana <= 3;
                bool lightDiscardDensity = nonCaveNonCoinCount > 0
                    && ((double)discardComponentNonCaveNonCoinCount / nonCaveNonCoinCount) <= 0.25;
                bool preferDrawBeforeCave = canClickCave
                    && board.ManaAvailable >= 1
                    && playableDrawMinions.Count > 0
                    && !BoardHelper.IsLethalPossibleThisTurn(board)
                    && (lowManaTurn || lightDiscardDensity);

                if (preferDrawBeforeCave)
                {
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(900)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-700)); } catch { }

                    foreach (var dm in playableDrawMinions)
                    {
                        if (dm == null || dm.Template == null) continue;
                        if (dm.Template.Id == Card.Cards.TLC_603)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(dm.Id, new Modifier(-2400)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(dm.Id, new Modifier(9999)); } catch { }
                        }
                        else
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(dm.Id, new Modifier(-1700)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(dm.Id, new Modifier(9500)); } catch { }
                        }
                    }

                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(900)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(900)); } catch { }
                    p.ForceResimulation = true;
                    AddLog("[维希度斯的窟穴让位过牌] 可先下过牌随从(" + GetCardIdsForLog(playableDrawMinions)
                        + "),且当前应先过牌(被弃占比=" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount
                        + ",MaxMana=" + board.MaxMana + ") => 先过牌后点地标");
                    return;
                }
            }
            catch { }

            // 新口径：有刀（手上有刀或已装备）且本回合可挥刀、最高费命中被弃组件时，
            // 场上地标点击让位挥刀，避免“先点地标打乱挥刀弃牌窗口”。
            try
            {
                bool hasClawInHandNow = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == Card.Cards.END_016);
                bool hasClawEquippedNow = board.WeaponFriend != null
                    && board.WeaponFriend.Template != null
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                    && board.WeaponFriend.CurrentDurability > 0;

                bool heroFrozenNow = false;
                bool heroAlreadyAttackedNow = false;
                try
                {
                    heroFrozenNow = board.HeroFriend != null && board.HeroFriend.IsFrozen;
                    heroAlreadyAttackedNow = board.HeroFriend != null && board.HeroFriend.CountAttack > 0;
                }
                catch { heroFrozenNow = false; heroAlreadyAttackedNow = false; }

                bool clawLikelyCanSwingNow = hasClawEquippedNow && !heroFrozenNow && !heroAlreadyAttackedNow;

                bool highestHasDiscardForWeaponNow = false;
                int highestDiscardCountForWeaponNow = 0;
                int highestTotalCountForWeaponNow = 0;
                string highestForWeaponNowLog = "(空))";
                try
                {
                    var handForWeapon = GetHandCardsForDiscardLogic(board);
                    if (handForWeapon.Count > 0 && discardComponents != null && discardComponents.Count > 0)
                    {
                        int maxCostForWeapon = handForWeapon.Max(h => h != null ? h.CurrentCost : 0);
                        var highestForWeapon = handForWeapon.Where(h => h != null && h.Template != null && h.CurrentCost == maxCostForWeapon).ToList();
                        highestForWeaponNowLog = GetCardIdsForLog(highestForWeapon);
                        highestTotalCountForWeaponNow = highestForWeapon.Count;

                        var discardSetForWeapon = new HashSet<Card.Cards>(discardComponents.Where(id => id != Card.Cards.WON_103));
                        highestDiscardCountForWeaponNow = highestForWeapon.Count(h => h != null && h.Template != null && discardSetForWeapon.Contains(h.Template.Id));
                        highestHasDiscardForWeaponNow = highestDiscardCountForWeaponNow > 0;
                    }
                }
                catch
                {
                    highestHasDiscardForWeaponNow = false;
                    highestDiscardCountForWeaponNow = 0;
                    highestTotalCountForWeaponNow = 0;
                    highestForWeaponNowLog = "(解析失败)";
                }

                bool shouldYieldBoardCaveToClawSwing = canClickCave
                    && hasDiscardComponentForCave
                    && hasClawEquippedNow
                    && clawLikelyCanSwingNow
                    && highestHasDiscardForWeaponNow;

                if (shouldYieldBoardCaveToClawSwing)
                {
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                    try
                    {
                        if (board.MinionFriend != null)
                        {
                            foreach (var cave in board.MinionFriend.Where(c => c != null
                                && c.Template != null
                                && c.Template.Id == Card.Cards.WON_103))
                            {
                                try { p.LocationsModifiers.AddOrUpdate(cave.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(cave.Id, new Modifier(-9999)); } catch { }
                            }
                        }
                    }
                    catch { }

                    try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2600)); } catch { }
                    p.ForceResimulation = true;
                    AddLog("[维希度斯的窟穴让位挥刀] 有刀可挥且最高费命中被弃组件(最高费=[" + highestForWeaponNowLog
                        + "],被弃=" + highestDiscardCountForWeaponNow + "/" + highestTotalCountForWeaponNow
                        + ",手上有刀=" + (hasClawInHandNow ? "Y" : "N")
                        + ") => 先挥刀，再点场上地标");
                    return;
                }
            }
            catch { }

            // 特判：手里仅“窟穴 + 太空海盗”时，不要先分流，直接走地标节奏。
            try
            {
                Card caveOnly;
                Card spacePirateOnly;
                bool onlyCaveAndSpacePirate = IsOnlyCaveAndSpacePirateHand(board, out caveOnly, out spacePirateOnly);
                if (onlyCaveAndSpacePirate
                    && canPlayCave
                    && caveInHand != null
                    && !canClickCave
                    && GetFriendlyBoardSlotsUsed(board) < 7)
                {
                    bool coinThenCave = TrySetCoinThenCardComboWithoutResim(p, board, caveInHand, needCoinToPlayCave, "维希度斯的窟穴-海盗联动");
                    if (!coinThenCave)
                    {
                        p.ComboModifier = new ComboSet(caveInHand.Id);
                        p.ForceResimulation = true;
                    }

                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }

                    if (spacePirateOnly != null)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(-220)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(7200)); } catch { }
                        try { p.CastMinionsModifiers.AddOrUpdate(spacePirateOnly.Id, new Modifier(-220)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(spacePirateOnly.Id, new Modifier(7200)); } catch { }
                    }

                    AddLog("[维希度斯的窟穴联动] 手牌仅地标+太空海盗 => 先拍地标，再海盗；本步不先分流");
                    return;
                }
            }
            catch { }

            // 新口径：手里无被弃组件时，地标应让位给其他可执行动作（先出其他牌，再考虑地标）。
            // 仅在“确实存在其他动作”时生效，不影响“只剩地标可用”的兜底回合。
            // 用户口径：让位强度提高到 -2500，并强制重算，避免被后续链路误覆盖。
            bool hasOnlySoulfireAsOtherAction = false;
            try
            {
                bool hasBoardSlotNowForHandAction = GetFriendlyBoardSlotsUsed(board) < 7;
                var playableNonCaveNonCoinActions = board.Hand != null
                    ? board.Hand.Where(c => c != null
                        && c.Template != null
                        && c.Template.Id != Card.Cards.WON_103
                        && c.Template.Id != Card.Cards.GAME_005
                        && c.CurrentCost <= board.ManaAvailable
                        && (c.Type != Card.CType.MINION || hasBoardSlotNowForHandAction))
                        .ToList()
                    : new List<Card>();

                bool hasPlayableSoulfireAction = playableNonCaveNonCoinActions.Any(c => c != null && c.Template != null
                    && c.Template.Id == Card.Cards.EX1_308);
                bool hasPlayableNonSoulfireAction = playableNonCaveNonCoinActions.Any(c => c != null && c.Template != null
                    && c.Template.Id != Card.Cards.EX1_308);

                bool canTapNowLocal = false;
                try { canTapNowLocal = CanUseLifeTapNow(board); }
                catch { canTapNowLocal = false; }

                hasOnlySoulfireAsOtherAction = hasOtherRealActions
                    && hasPlayableSoulfireAction
                    && !hasPlayableNonSoulfireAction
                    && !canTapNowLocal;
            }
            catch { hasOnlySoulfireAsOtherAction = false; }

            // 新口径细化：
            // - 若“除魂火外其余手牌均为被弃组件”，则魂火应先于地标动作。
            // - 其它情况下仍保持先地标后魂火。
            try
            {
                // 互斥优先：若商贩本回合可稳定弃到灵魂弹幕，则地标让位商贩连段。
                bool stableMerchantBarrageNowForCave = false;
                bool stabilizeMerchantByCoyoteForCave = false;
                string coyoteMerchantCaveReason = null;
                try
                {
                    var merchantInHandNow = board.Hand != null
                        ? board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163)
                        : null;
                    bool merchantPlayableNow = merchantInHandNow != null
                        && merchantInHandNow.CurrentCost <= effectiveMana
                        && GetFriendlyBoardSlotsUsed(board) < 7;

                    if (merchantPlayableNow)
                    {
                        bool stableByDiscardLogic = false;
                        bool stableByRawHand = false;

                        var handWithoutMerchantAndCoin = GetHandCardsForDiscardLogic(board)
                            .Where(h => h != null
                                && h.Template != null
                                && h.Template.Id != Card.Cards.ULD_163
                                && h.Template.Id != Card.Cards.GAME_005)
                            .ToList();
                        if (handWithoutMerchantAndCoin.Count > 0)
                        {
                            int maxCostNow = handWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                            var highestNow = handWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxCostNow).ToList();
                            stableByDiscardLogic = highestNow.Count > 0
                                && highestNow.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                        }

                        var rawHandWithoutMerchantAndCoin = (board.Hand ?? new List<Card>())
                            .Where(h => h != null
                                && h.Template != null
                                && h.Template.Id != Card.Cards.ULD_163
                                && h.Template.Id != Card.Cards.GAME_005)
                            .ToList();
                        if (rawHandWithoutMerchantAndCoin.Count > 0)
                        {
                            int maxRawCostNow = rawHandWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                            var highestRawNow = rawHandWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxRawCostNow).ToList();
                            stableByRawHand = highestRawNow.Count > 0
                                && highestRawNow.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                        }

                        stableMerchantBarrageNowForCave = stableByDiscardLogic || stableByRawHand;
                    }
                }
                catch { stableMerchantBarrageNowForCave = false; }
                try
                {
                    stabilizeMerchantByCoyoteForCave = CanStabilizeMerchantBarrageAfterCoyoteFaceDiscount(
                        board,
                        effectiveMana,
                        out coyoteMerchantCaveReason);
                }
                catch
                {
                    stabilizeMerchantByCoyoteForCave = false;
                    coyoteMerchantCaveReason = null;
                }

                if ((canPlayCave || canClickCave) && (stableMerchantBarrageNowForCave || stabilizeMerchantByCoyoteForCave))
                {
                    // 硬让位：当商贩可稳定弃弹幕时，本轮直接禁用地标（手拍+场点），防止被后续分支反向覆盖。
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                    try
                    {
                        if (caveInHand != null)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(caveInHand.Id, new Modifier(9999)); } catch { }
                            try { p.LocationsModifiers.AddOrUpdate(caveInHand.Id, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(caveInHand.Id, new Modifier(-9999)); } catch { }
                        }
                        if (board.MinionFriend != null)
                        {
                            foreach (var caveOnBoard in board.MinionFriend.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103))
                            {
                                try { p.LocationsModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(-9999)); } catch { }
                            }
                        }
                    }
                    catch { }

                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-2600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9950)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var m in board.Hand.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163))
                            {
                                try { p.CastMinionsModifiers.AddOrUpdate(m.Id, new Modifier(-2600)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(m.Id, new Modifier(9950)); } catch { }
                            }
                        }
                    }
                    catch { }

                    AddLog("[维希度斯的窟穴互斥] 商贩+灵魂弹幕连段可达成("
                        + (stableMerchantBarrageNowForCave ? "已稳定" : (string.IsNullOrEmpty(coyoteMerchantCaveReason) ? "可压费转稳定" : coyoteMerchantCaveReason))
                        + ") => 禁用地标(手拍/场点)，优先商贩");
                    return;
                }

                bool hasSoulfireInHand = board.Hand != null && board.Hand.Any(c => c != null
                    && c.Template != null
                    && c.Template.Id == Card.Cards.EX1_308
                    && c.CurrentCost <= board.ManaAvailable);

                if (canPlayCave && hasSoulfireInHand && allNonSoulfireNonCaveNonCoinAreDiscard)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                    try
                    {
                        if (caveInHand != null)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(caveInHand.Id, new Modifier(9999)); } catch { }
                            try { p.LocationsModifiers.AddOrUpdate(caveInHand.Id, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(caveInHand.Id, new Modifier(-9999)); } catch { }
                        }
                    }
                    catch { }

                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9900)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var sf in board.Hand.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.EX1_308))
                            {
                                try { p.CastSpellsModifiers.AddOrUpdate(sf.Id, new Modifier(-3200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(sf.Id, new Modifier(9900)); } catch { }
                            }
                        }
                    }
                    catch { }

                    AddLog("[维希度斯的窟穴-让位魂火] 除魂火外全是被弃组件(" + discardNonSoulfireNonCaveNonCoinCount + "/" + nonSoulfireNonCaveNonCoinCount
                        + ") => 暂缓地标，先魂火");
                    return;
                }

                if (canPlayCave && hasSoulfireInHand)
                {
                    bool coinThenCave = TrySetCoinThenCardComboWithoutResim(p, board, caveInHand, needCoinToPlayCave, "维希度斯的窟穴-让位魂火");
                    if (!coinThenCave && caveInHand != null)
                    {
                        p.ComboModifier = new ComboSet(caveInHand.Id);
                        p.ForceResimulation = true;
                    }
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1600)); } catch { }
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9700)); } catch { }
                    AddLog("[维希度斯的窟穴-让位修正] 手牌含可用魂火且地标可拍 => 先地标后魂火");
                    return;
                }
            }
            catch { }

            // 低手牌放行：场上有可点窟穴、手牌<=4 且当前无被弃组件时，允许点地标弃牌过牌。
            // 目的：避免“无被弃组件=强禁点”导致前中期断过牌、直接空过。
            if (!hasDiscardComponentForCave
                && canClickCave
                && handCount > 0
                && handCount <= 4
                && !hasLowCostDrawOrKeyForCavePass)
            {
                try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1200)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9400)); } catch { }
                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(900)); } catch { }
                p.ForceResimulation = true;
                AddLog("[维希度斯的窟穴放行] 手牌<=4且无被弃组件(" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount + ")，且<=4费区间无狗头人/栉龙/咒怨/速写 => 允许点击场上地标弃牌过牌(-1200/9400)");
                return;
            }

            if (!hasDiscardComponentForCave && hasOtherRealActions && !hasOnlySoulfireAsOtherAction && !canClickCave)
            {
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2500)); } catch { }
                try
                {
                    if (canClickCave)
                    {
                        p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(900));
                    }
                }
                catch { }
                p.ForceResimulation = true;
                AddLog("[维希度斯的窟穴让位] 手牌无被弃组件(" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount + ")且有其他动作 => 暂缓手上地标(-2500)并强制重算");
                return;
            }

            if (!hasDiscardComponentForCave && hasOnlySoulfireAsOtherAction)
            {
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1400)); } catch { }
                try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1400)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9600)); } catch { }
                AddLog("[维希度斯的窟穴-不让位魂火] 其他动作仅魂火 => 本轮先地标后魂火");
            }

            // 互斥?：当手里?张灵魂弹幕，且专卖商本回合可打并能?定弃到这张弹幕时，优先专卖商，不要让窟穴(从手/鐐?鎶?厛鎵嬨??
            bool merchantWillDiscardSingleSoulBarrage = false;
            try
            {
                int soulBarrageCount = board.Hand.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                if (soulBarrageCount == 1)
                {
                    var merchantInHand = board.Hand.FirstOrDefault(h => h != null && h.Template != null && h.Template.Id == Card.Cards.ULD_163);
                    bool canPlayMerchantNow = merchantInHand != null
                        && merchantInHand.CurrentCost <= effectiveMana
                        && GetFriendlyBoardSlotsUsed(board) < 7;

                    if (canPlayMerchantNow)
                    {
                        var handWithoutMerchantAndCoin = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                            && h.Template.Id != Card.Cards.ULD_163
                            && h.Template.Id != Card.Cards.GAME_005).ToList();

                        if (handWithoutMerchantAndCoin.Count > 0)
                        {
                            int maxCost = handWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                            var highest = handWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxCost).ToList();
                            merchantWillDiscardSingleSoulBarrage = highest.Count == 1
                                && highest[0] != null && highest[0].Template != null
                                && highest[0].Template.Id == Card.Cards.RLK_534;
                        }
                    }
                }
            }
            catch { merchantWillDiscardSingleSoulBarrage = false; }

            if (merchantWillDiscardSingleSoulBarrage)
            {
                AddLog("[维希度斯的窟穴互斥] 手里仅1张灵魂弹幕，且专卖商可稳定弃到它 => 优先专卖商，延后窟穴(PlayOrder-900)");
                return;
            }

                        // 如果场上?费随从且场面仍有空位(<=6格已?锛岄粯璁?点场上地标；
                        // 但当手里已有被弃组件时，不做?制，避免错过“应优先点地标?的窗口?
                        if (board.MinionFriend.Count(x => x != null && x.Template != null && x.CurrentCost == 0) > 0
                            && GetFriendlyBoardSlotsUsed(board) <= 6
                            && !hasDiscardComponentForCave
                            && handCount > 4)
						{
								canClickCave = false;
								AddLog("[维希度斯的窟穴] 场上有0费随从且随从数<=6，且手里无被弃组件=> 禁止点击场上地标");
						}

                // 銆愮?规则】场上地标在“手里无被弃组件”时不允许点击（即使无其他动作也不白点）
                if (canClickCave && !hasDiscardComponentForCave
								// 手牌不为?
								&& handCount > 4
								)
                {
                    p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999));
                    AddLog("[维希度斯的窟穴硬规则] 手牌无被弃组件" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount + ") => 禁止点击场上地标(9999)");
                    canClickCave = false;
                }

            // 【优先级1】手牌为空：若时空之爪可攻击，先挥刀；否则白点地标抽2张
            if (handCount ==0 && canClickCave)
            {
                bool clawCanAttackNow = false;
                try
                {
                    clawCanAttackNow = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;
                }
                catch { clawCanAttackNow = false; }

                if (clawCanAttackNow)
                {
                    try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2200)); } catch { }
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(900)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-700)); } catch { }
                    AddLog("[维希度斯的窟穴让位挥刀] 手牌为空且时空之爪可攻击 => 先挥刀，再点地标");
                    return;
                }

                p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-999));
                
                // 压制分流
                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(999)); } catch { }
                
                p.ForceResimulation = true;
                AddLog("[维希度斯的窟穴优先级] 手牌为空且场上可点=> 强制优先点窟穴9800/-999)");
                return;
            }

            // 【优先级2】手里除地标外没牌：允许直接拍下/白点
            else if (hasOnlyCaveInHand)
            {
                // 若场上可点，则优先点场上；否则再考虑拍下
                if (canClickCave)
                {
                    // p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-800));
                    bool coinThenCave = TrySetCoinThenCardComboWithoutResim(p, board, caveInHand, needCoinToPlayCave, "维希度斯的窟穴优先级");
                    if (!coinThenCave)
                    {
                        p.ComboModifier = new ComboSet(caveInHand.Id);
                        p.ForceResimulation = true;
                    }
                    string caveOnlyActionMsg;
                    if (coinThenCave)
                    {
                        caveOnlyActionMsg = "优先硬币->地标(不重算)";
                    }
                    else
                    {
                        caveOnlyActionMsg = "优先出手上地标并强制重算";
                    }
                    AddLog("[维希度斯的窟穴优先级] 手牌除地标外无牌且场上可点=> " + caveOnlyActionMsg + " 禁用场上地标点击");
                    return;
                }

                bool coinThenCaveOnly = TrySetCoinThenCardComboWithoutResim(p, board, caveInHand, needCoinToPlayCave, "维希度斯的窟穴优先级");
                if (!coinThenCaveOnly)
                {
                    p.ComboModifier = new ComboSet(caveInHand.Id);
                    p.ForceResimulation = true;
                }
                return;
            }

            // 【优先级3】手里有被弃组件（排?湴鏍?硬币）：允许使用
            else if (hasDiscardComponentForCave)
            {
                // 鑻?上可点且当前还能先?花费法力?打宝藏经销商，则先?经销商，避免?点地标打乱费?埄鐢???
                bool canPlayToyDealerBeforeClickCave = false;
                try
                {
                    var toyDealer = board.Hand.FirstOrDefault(x => x != null && x.Template != null
                        && x.Template.Id == Card.Cards.TOY_518);
                    canPlayToyDealerBeforeClickCave = toyDealer != null
                        && toyDealer.CurrentCost <= board.ManaAvailable
                        && board.ManaAvailable > 0
                        && GetFriendlyBoardSlotsUsed(board) < 7;
                }
                catch { canPlayToyDealerBeforeClickCave = false; }

                if (canClickCave && canPlayToyDealerBeforeClickCave)
                {
                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(900)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-700)); } catch { }
                    AddLog("[维希度斯的窟穴让位经销商] 场上可点且可先下宝藏经销商(法力=" + board.ManaAvailable + ") => 本轮先打经销商，再点地标");
                    return;
                }

                // 顺序修正：若可先打1费非被弃组件随从并提纯弃牌池，则先下随从再点窟穴。
                // 目标：提高窟穴命中被弃组件的概率，避免“先点窟穴”吃到低价值牌。
                try
                {
                    bool hasBoardSlotForThin = GetFriendlyBoardSlotsUsed(board) < 7;
                    int thinTotal = nonCaveNonCoinCount;
                    int thinDiscard = discardComponentNonCaveNonCoinCount;
                    bool canImproveDensityByPlayingOneDrop = hasBoardSlotForThin
                        && thinTotal >= 2
                        && thinDiscard > 0
                        && thinDiscard < thinTotal;

                    if (canClickCave && canImproveDensityByPlayingOneDrop && board.Hand != null)
                    {
                        var oneCostNonDiscardMinions = board.Hand
                            .Where(c => c != null
                                && c.Template != null
                                && c.Type == Card.CType.MINION
                                && c.CurrentCost == 1
                                && c.CurrentCost <= board.ManaAvailable
                                && c.Template.Id != Card.Cards.WON_103
                                && c.Template.Id != Card.Cards.GAME_005
                                && (discardComponents == null || !discardComponents.Contains(c.Template.Id))
                                && !ShouldIgnoreCardForCaveDiscardRatio(c)
                                && !isDisabledInRulesSet(p.CastMinionsModifiers, c))
                            .ToList();

                        if (oneCostNonDiscardMinions.Count > 0)
                        {
                            try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(900)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-700)); } catch { }
                            foreach (var oneDrop in oneCostNonDiscardMinions)
                            {
                                if (oneDrop == null) continue;
                                try { p.CastMinionsModifiers.AddOrUpdate(oneDrop.Id, new Modifier(-2200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(oneDrop.Id, new Modifier(9800)); } catch { }
                            }
                            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(999)); } catch { }
                            p.ForceResimulation = true;
                            AddLog("[维希度斯的窟穴顺序修正] 可先下1费非被弃组件提纯手牌(" + thinDiscard + "/" + thinTotal + "->"
                                + thinDiscard + "/" + (thinTotal - 1) + ") => 先下1费随从[" + GetCardIdsForLog(oneCostNonDiscardMinions)
                                + "]，再点窟穴");
                            return;
                        }
                    }
                }
                catch { }

                // 若场上可点，则优先点场上
                if (canClickCave)
                {
                    p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800));
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                    try
                    {
                        foreach (var cave in board.MinionFriend.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103))
                        {
                            if (cave == null) continue;
                            p.LocationsModifiers.AddOrUpdate(cave.Id, new Modifier(-1800));
                            p.PlayOrderModifiers.AddOrUpdate(cave.Id, new Modifier(9999));
                        }
                    }
                    catch { }
                    // 场上地标?使用?欢锛氱?鐢?娊涓?鍙?
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(999)); } catch { }
                    p.ForceResimulation = true;
                    AddLog("[维希度斯的窟穴优先级] 手牌有被弃组件" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount + ")且场上可点=> 强制优先点窟穴9999/-1800)");
                    return;
                }

                // 场上不可点，但手里可拍下：需要显式给出?从手拍下?的倾向，否则只打日志不出牌?
                if (canPlayCave && caveInHand != null && GetFriendlyBoardSlotsUsed(board) < 7)
                {
                    // 新口径：从手拍窟穴前，先?鏌?槸鍚?瓨鍦?高优先级?combo锛堥?熷啓/商贩/爪子/火炮/澧?弹幕连段等）?
                    // 说明：不影响“点击场上窟穴?，只限制?从手拍下窟穴?濄??
                    string delayReason = null;
                    if (ShouldDelayCaveFromHandBecauseComboAvailable(board, effectiveMana, handCount, discardComponents, out delayReason))
                    {
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-900)); } catch { }
                        AddLog("[维希度斯的窟穴互斥] " + delayReason + " => 本回合不从手拍窟穴PlayOrder-900)");
                        return;
                    }

                    bool coinThenCave = TrySetCoinThenCardComboWithoutResim(p, board, caveInHand, needCoinToPlayCave, "维希度斯的窟穴优先级");
                    if (!coinThenCave)
                    {
                        p.ComboModifier = new ComboSet(caveInHand.Id);
                        p.ForceResimulation = true;
                    }

                    // 抑制分流/延后武器攻击，避免先抽牌/先挥?稀释弃牌池或抢走节?
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(999)); } catch { }
                    try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(850)); } catch { }

	                    string caveComboMsg;
	                    if (coinThenCave)
	                    {
	                        caveComboMsg = "，硬币后不重算";
	                    }
	                    else
	                    {
	                        caveComboMsg = "并强制重算";
	                    }
	                    AddLog("[维希度斯的窟穴优先级] 手牌有被弃组件" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount + ")且可从手打出 => 强推先拍地标(ComboSet)" + caveComboMsg);
                    return;
                }

                AddLog("[维希度斯的窟穴优先级] 手牌有被弃组件" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount + ") => 允许使用(但当前不可点/不可拍下)");
                return;
            }

            // 【优先级4】无其他?：允许白?白点
            else if (!hasOtherRealActions && canPlayCave && caveInHand != null && GetFriendlyBoardSlotsUsed(board) < 7)
            {
                // 新口径：即使“看起来无其他动作?，只要存在更高优先级combo，也不要?拍窟穴??
                string delayReason = null;
                if (ShouldDelayCaveFromHandBecauseComboAvailable(board, effectiveMana, handCount, discardComponents, out delayReason))
                {
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-900)); } catch { }
                    AddLog("[维希度斯的窟穴互斥] " + delayReason + " => 本回合不从手拍窟穴PlayOrder-900)");
                    return;
                }

                // p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-200));
                bool coinThenCave = TrySetCoinThenCardComboWithoutResim(p, board, caveInHand, needCoinToPlayCave, "维希?的窟穴优先级");
                if (!coinThenCave)
                {
                    p.ComboModifier = new ComboSet(caveInHand.Id);
                    p.ForceResimulation = true;
                }
                
                
                AddLog("[维希度斯的窟穴优先级] 无其他动作=> 允许白用" + (coinThenCave ? "（硬币后不重算）" : ""));
                return;
            }

            // 【优先级4b】铺场例外：手里无被弃组件但场上无窟穴时，允许先从手拍下做铺垫（不点、不?、不强制连段?
            else if (!hasAnyCaveOnBoard
                && canPlayCave
                && caveInHand != null
                && caveInHand.CurrentCost <= board.ManaAvailable
                && GetFriendlyBoardSlotsUsed(board) < 7
                && board.MaxMana >= 5)
            {
                // 新口径：铺场例外也要?给更高优?combo銆?
                string delayReason = null;
                if (ShouldDelayCaveFromHandBecauseComboAvailable(board, effectiveMana, handCount, discardComponents, out delayReason))
                {
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-900)); } catch { }
                    AddLog("[维希度斯的窟穴互斥] " + delayReason + " => 本回合不从手拍窟穴PlayOrder-900)");
                    return;
                }

                bool coinThenCave = TrySetCoinThenCardComboWithoutResim(p, board, caveInHand, needCoinToPlayCave, "维希?的窟穴铺场例外");
                if (!coinThenCave)
                {
                    p.ComboModifier = new ComboSet(caveInHand.Id);
                    p.ForceResimulation = true;
                }
	                AddLog("[维希度斯的窟穴铺场例外] 手牌无被弃组件" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount + "),但场上无窟穴且MaxMana=" + board.MaxMana + " => 允许从手先铺窟穴(ComboSet)" + (coinThenCave ? "，硬币后不重算" : ""));
                return;
            }

            // 【优先级4 - 默认】既无收益又有其他动作：?使用
            else
            {
                // p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(130));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-800));
                AddLog("[维希度斯的窟穴默认] 手牌无被弃组件" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount + "),且有其他动作 => 不使用(130)");
            }
        }

        /// <summary>
        /// 当目标牌?要跳币才能打出时，设置?滅?甯?>目标牌?连段，且?币后不?发重算??
        /// 返回 true 血?已成功设置连段??
        /// </summary>
        private bool TrySetCoinThenCardComboWithoutResim(ProfileParameters p, Board board, Card targetCard, bool needCoin, string logTag)
        {
            try
            {
                if (!needCoin || p == null || board == null || targetCard == null) return false;

                bool comboAlreadySet = false;
                try { comboAlreadySet = p.ComboModifier != null; } catch { comboAlreadySet = false; }
                if (comboAlreadySet) return false;

                var coinCard = board.Hand != null
                    ? board.Hand.FirstOrDefault(h => h != null && h.Template != null && h.Template.Id == Card.Cards.GAME_005)
                    : null;
                if (coinCard == null) return false;

                bool coinActuallyNeeded = false;
                try
                {
                    coinActuallyNeeded = targetCard.CurrentCost > board.ManaAvailable
                        && targetCard.CurrentCost <= board.ManaAvailable + 1;
                }
                catch { coinActuallyNeeded = needCoin; }

                if (!coinActuallyNeeded) return false;

                p.ComboModifier = new ComboSet(coinCard.Id, targetCard.Id);
                p.ForceResimulation = false;
                string targetTemplateId = "?";
                try { targetTemplateId = targetCard.Template != null ? targetCard.Template.Id.ToString() : "?"; } catch { targetTemplateId = "?"; }
                AddLog("[" + logTag + "] 需要跳币=> 设置硬币->" + targetTemplateId + "(ComboSet)，硬币后不重算");
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 为灵魂弹幕RLK_534)同时设置“模板ID + 实例ID”的出牌修正，避免仅℃修正?别时序被绕过?
        /// </summary>
        private void SetSoulBarrageDirectPlayModifier(ProfileParameters p, Board board, int castValue, int orderValue)
        {
            // SmartBot modifier range is [-10000, 10000] (practical safe range: -9999..9999).
            int effectiveCast = Math.Max(-9999, Math.Min(9999, castValue));
            int effectiveOrder = Math.Max(-9999, Math.Min(9999, orderValue));

            try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(effectiveCast)); } catch { }
            try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(effectiveCast)); } catch { }
            try { p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(effectiveCast)); } catch { }
            try { p.LocationsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(effectiveCast)); } catch { }
            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(effectiveOrder)); } catch { }

            try
            {
                if (board != null && board.Hand != null)
                {
                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;
                        if (c.Template.Id != Card.Cards.RLK_534) continue;
                        try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(effectiveCast)); } catch { }
                        try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(effectiveCast)); } catch { }
                        try { p.CastWeaponsModifiers.AddOrUpdate(c.Id, new Modifier(effectiveCast)); } catch { }
                        try { p.LocationsModifiers.AddOrUpdate(c.Id, new Modifier(effectiveCast)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(effectiveOrder)); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 古尔丹之手优先走“非商贩”弃牌启动器：
        /// - 手上有刀
        /// - 已装备刀
        /// - 手牌有地标且手牌<=6
        /// - 场上有可点击地标且手牌<=5
        /// </summary>
        private bool HasPreferredNonMerchantStarterForGuldan(Board board, int handCount, out string starterReason)
        {
            starterReason = null;
            if (board == null) return false;

            try
            {
                bool hasClawInHand = false;
                try
                {
                    hasClawInHand = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                        && c.Template.Id == Card.Cards.END_016);
                }
                catch { hasClawInHand = false; }
                if (hasClawInHand)
                {
                    starterReason = "手上有刀";
                    return true;
                }

                bool hasClawEquipped = false;
                try
                {
                    hasClawEquipped = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0;
                }
                catch { hasClawEquipped = false; }
                if (hasClawEquipped)
                {
                    starterReason = "已装备时空之爪";
                    return true;
                }

                bool hasCaveInHandUnderCap = false;
                try
                {
                    bool hasSpace = GetFriendlyBoardSlotsUsed(board) < 7;
                    hasCaveInHandUnderCap = handCount <= 6
                        && hasSpace
                        && board.Hand != null
                        && board.Hand.Any(c => c != null && c.Template != null
                            && c.Template.Id == Card.Cards.WON_103);
                }
                catch { hasCaveInHandUnderCap = false; }
                if (hasCaveInHandUnderCap)
                {
                    starterReason = "手上有地标且手牌<=6";
                    return true;
                }

                bool hasClickableCaveUnderCap = false;
                try
                {
                    hasClickableCaveUnderCap = handCount <= 5
                        && board.MinionFriend != null
                        && board.MinionFriend.Any(c => c != null
                            && c.Template != null
                            && c.Template.Id == Card.Cards.WON_103
                            && GetTag(c, Card.GAME_TAG.EXHAUSTED) != 1);
                }
                catch { hasClickableCaveUnderCap = false; }
                if (hasClickableCaveUnderCap)
                {
                    starterReason = "场上有可点击地标且手牌<=5";
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 判断是否存在“先走脸压费郊狼 -> 稳定商贩弃灵魂弹幕”的连段。
        /// 用于让位咒怨之墓/提刀等非必需动作，避免抢掉更优弃弹幕节奏。
        /// </summary>
        private bool CanStabilizeMerchantBarrageAfterCoyoteFaceDiscount(Board board, int effectiveMana, out string reason)
        {
            reason = null;
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;

                bool hasSpaceForMerchant = false;
                try { hasSpaceForMerchant = GetFriendlyBoardSlotsUsed(board) < 7; } catch { hasSpaceForMerchant = false; }
                if (!hasSpaceForMerchant) return false;

                var merchant = board.Hand.FirstOrDefault(c => c != null
                    && c.Template != null
                    && c.Template.Id == Card.Cards.ULD_163
                    && c.CurrentCost <= effectiveMana);
                if (merchant == null) return false;

                var barrages = board.Hand.Where(c => c != null
                    && c.Template != null
                    && c.Template.Id == Card.Cards.RLK_534).ToList();
                if (barrages.Count == 0) return false;

                var coyotes = board.Hand.Where(c => c != null
                    && c.Template != null
                    && IsCoyoteCardVariant(c)
                    && c.CurrentCost > 0).ToList();
                if (coyotes.Count == 0) return false;

                bool enemyHasTaunt = false;
                try { enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { enemyHasTaunt = false; }
                if (enemyHasTaunt) return false;

                int potentialFaceDamage = 0;
                try
                {
                    if (board.MinionFriend != null)
                    {
                        potentialFaceDamage += board.MinionFriend
                            .Where(m => m != null && m.CanAttack)
                            .Sum(m => Math.Max(0, m.CurrentAtk));
                    }
                }
                catch { }
                try
                {
                    if (board.HeroFriend != null && board.HeroFriend.CanAttack)
                        potentialFaceDamage += Math.Max(0, board.HeroFriend.CurrentAtk);
                }
                catch { }
                if (potentialFaceDamage <= 0) return false;

                int highestBarrageCost = 0;
                try { highestBarrageCost = barrages.Max(x => Math.Max(0, x.CurrentCost)); } catch { highestBarrageCost = 0; }

                bool coyoteBlocksNow = false;
                bool coyoteCanDropBelowBarrage = false;
                try
                {
                    coyoteBlocksNow = coyotes.Any(c => c != null && c.CurrentCost >= highestBarrageCost);
                    coyoteCanDropBelowBarrage = coyotes.Any(c => c != null
                        && Math.Max(0, c.CurrentCost - potentialFaceDamage) < highestBarrageCost);
                }
                catch
                {
                    coyoteBlocksNow = false;
                    coyoteCanDropBelowBarrage = false;
                }
                if (!coyoteBlocksNow || !coyoteCanDropBelowBarrage) return false;

                // 模拟“走脸压费后”的最高费结构（商贩战吼目标池排除商贩自身）。
                var handWithoutMerchant = GetHandCardsForDiscardLogic(board)
                    .Where(c => c != null
                        && c.Template != null
                        && c.Template.Id != Card.Cards.ULD_163).ToList();
                if (handWithoutMerchant.Count == 0) return false;

                int projectedHighest = int.MinValue;
                bool highestHasBarrage = false;
                bool highestHasNonBarrage = false;
                foreach (var c in handWithoutMerchant)
                {
                    if (c == null || c.Template == null) continue;
                    int projectedCost = Math.Max(0, c.CurrentCost);
                    if (IsCoyoteCardVariant(c))
                        projectedCost = Math.Max(0, projectedCost - potentialFaceDamage);

                    if (projectedCost > projectedHighest)
                    {
                        projectedHighest = projectedCost;
                        highestHasBarrage = c.Template.Id == Card.Cards.RLK_534;
                        highestHasNonBarrage = c.Template.Id != Card.Cards.RLK_534;
                    }
                    else if (projectedCost == projectedHighest)
                    {
                        if (c.Template.Id == Card.Cards.RLK_534) highestHasBarrage = true;
                        else highestHasNonBarrage = true;
                    }
                }

                bool projectedStableMerchantDiscard = projectedHighest >= 0 && highestHasBarrage && !highestHasNonBarrage;
                if (!projectedStableMerchantDiscard) return false;

                reason = "可走脸压费郊狼并转化为“商贩稳定弃弹幕”(可走脸伤害=" + potentialFaceDamage + ")";
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从手拍窟穴WON_103)前的“combo 互斥?爮鈥濄??
        /// 目标：当回合存在更高优先级combo 时，不要?拍窟穴??
        /// </summary>
        private bool ShouldDelayCaveFromHandBecauseComboAvailable(Board board, int effectiveMana, int handCount,
            HashSet<Card.Cards> discardComponents, out string reason, bool ignoreTombCombo = false)
        {
            reason = null;

            try
            {
                if (board == null || board.Hand == null) return false;

                bool hasSpaceForMinion = GetFriendlyBoardSlotsUsed(board) < 7;
                var handForDiscardLogicLocal = GetHandCardsForDiscardLogic(board);

                Func<Card.Cards, bool> hasPlayableMinion = (id) =>
                {
                    try
                    {
                        if (!hasSpaceForMinion) return false;
                        return handForDiscardLogicLocal.Any(h => h != null && h.Template != null && h.Template.Id == id && h.CurrentCost <= effectiveMana);
                    }
                    catch { return false; }
                };

                Func<Card.Cards, bool> hasPlayableSpell = (id) =>
                {
                    try { return handForDiscardLogicLocal.Any(h => h != null && h.Template != null && h.Template.Id == id && h.CurrentCost <= effectiveMana); }
                    catch { return false; }
                };

                Func<Card.Cards, bool> hasCardInHand = (id) =>
                {
                    try { return handForDiscardLogicLocal.Any(h => h != null && h.Template != null && h.Template.Id == id); }
                    catch { return false; }
                };

                // 鍙栤?最高费组?（排除硬币/窟穴），用于判断商贩/爪子能否稳定弃到目标?
                List<Card> relevant = null;
                try
                {
                    relevant = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                        && h.Template.Id != Card.Cards.GAME_005
                        && h.Template.Id != Card.Cards.WON_103).ToList();
                }
                catch { relevant = new List<Card>(); }

                int maxCost = 0;
                try { maxCost = relevant.Count > 0 ? relevant.Max(h => h.CurrentCost) : 0; } catch { maxCost = 0; }
                List<Card> highest = null;
                try { highest = relevant.Where(h => h.CurrentCost == maxCost).ToList(); } catch { highest = new List<Card>(); }

                Func<Card.Cards, bool> highestAllAre = (id) =>
                {
                    try
                    {
                        if (highest == null || highest.Count == 0) return false;
                        return highest.All(h => h != null && h.Template != null && h.Template.Id == id);
                    }
                    catch { return false; }
                };

                Func<HashSet<Card.Cards>, bool> highestAllInSet = (set) =>
                {
                    try
                    {
                        if (highest == null || highest.Count == 0) return false;
                        if (set == null || set.Count == 0) return false;
                        return highest.All(h => h != null && h.Template != null && set.Contains(h.Template.Id));
                    }
                    catch { return false; }
                };

                Func<Card.Cards, bool> highestHas = (id) =>
                {
                    try
                    {
                        if (highest == null || highest.Count == 0) return false;
                        return highest.Any(h => h != null && h.Template != null && h.Template.Id == id);
                    }
                    catch { return false; }
                };

                // 璁＄“牌库仍可能有灵魂弹幕??
                int remainingBarrageInDeck = 0;
                try { remainingBarrageInDeck = GetRemainingBarrageCountInDeck(board); } catch { remainingBarrageInDeck = 0; }
                bool deckHasBarrage = remainingBarrageInDeck > 0;

                // 1) 牌库有灵魂弹幕：速写?优先
                if (deckHasBarrage && hasPlayableMinion(Card.Cards.TOY_916))
                {
                    bool shouldYieldSketchToDoubleTomb = false;
                    try
                    {
                        int tombCountInHand = handForDiscardLogicLocal.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.TLC_451);
                        bool hasPlayableTombNow = handForDiscardLogicLocal.Any(h => h != null && h.Template != null
                            && h.Template.Id == Card.Cards.TLC_451
                            && h.CurrentCost <= board.ManaAvailable);
                        bool hasCoinNow = false;
                        try { hasCoinNow = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinNow = false; }

                        // 与速写“跳币让位双墓”一致：2费有币且双墓在手时，墓不再让位速写。
                        shouldYieldSketchToDoubleTomb = board.ManaAvailable == 2
                            && hasCoinNow
                            && tombCountInHand >= 2
                            && hasPlayableTombNow;
                    }
                    catch { shouldYieldSketchToDoubleTomb = false; }

                    if (shouldYieldSketchToDoubleTomb)
                    {
                        // 不在这里直接返回 delay=true；继续往下评估其它互斥项。
                    }
                    else
                    {
                    reason = "牌库仍可能有灵魂弹幕(est=" + remainingBarrageInDeck + ")且本回合可用速写美术家";
                    return true;
                    }
                }

                // 2) 专卖?+ 灵魂弹幕（需“最高费稳定=弹幕”才算该 combo锛?
                if (hasPlayableMinion(Card.Cards.ULD_163) && hasCardInHand(Card.Cards.RLK_534) && highestAllAre(Card.Cards.RLK_534))
                {
                    reason = "本回合可打专卖商且最高费稳定=灵魂弹幕 => 优先专卖商弃弹幕";
                    return true;
                }

                // 3) 爪子 + 灵魂弹幕（需已装备且可攻击，且最高费稳定=弹幕?
                bool clawEquipped = false;
                bool heroCanAttackNow = false;
                try
                {
                    clawEquipped = board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.Template.Id == Card.Cards.END_016;
                    heroCanAttackNow = board.HeroFriend != null && board.HeroFriend.CanAttack;
                }
                catch { clawEquipped = false; heroCanAttackNow = false; }

                if (clawEquipped && heroCanAttackNow && hasCardInHand(Card.Cards.RLK_534) && highestAllAre(Card.Cards.RLK_534))
                {
                    reason = "已装备时空之爪且可攻击，最高费稳定=灵魂弹幕 => 优先弃弹幕";
                    return true;
                }

                // 4) 爪子 + 古尔丹之手（手牌<=8锛屼笖当前最高费稳定=鍙?墜锛?
                if (handCount <= 8 && clawEquipped && heroCanAttackNow && hasCardInHand(Card.Cards.BT_300) && highestAllAre(Card.Cards.BT_300))
                {
                    reason = "已装备时空之爪且可攻击，手牌<=8且最高费稳定=古尔丹之手=> 优先弃古手";
                    return true;
                }

                // 5) 船载火炮 + 海盗
                bool hasPirateInHandOrBoard = false;
                try
                {
                    bool piratesInHand = board.Hand.Any(h => h != null && h.Template != null && h.IsRace(Card.CRace.PIRATE));
                    bool piratesOnBoard = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.Template != null && m.IsRace(Card.CRace.PIRATE));
                    hasPirateInHandOrBoard = piratesInHand || piratesOnBoard;
                }
                catch { hasPirateInHandOrBoard = false; }

                if (hasPirateInHandOrBoard && (hasPlayableMinion(Card.Cards.CORE_NEW1_023) || hasPlayableMinion(Card.Cards.GVG_075)))
                {
                    reason = "本回合可打船载火炮且有海盗联动=> 优先火炮连段";
                    return true;
                }

                bool caveHasDiscardPayoffIgnoringZeroMinion = false;
                try
                {
                    var discardSetForCaveDelay = discardComponents != null && discardComponents.Count > 0
                        ? new HashSet<Card.Cards>(discardComponents)
                        : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));

                    int caveDiscardCountIgnoringZeroMinion = handForDiscardLogicLocal.Count(h => h != null && h.Template != null
                        && h.Template.Id != Card.Cards.WON_103
                        && h.Template.Id != Card.Cards.GAME_005
                        && !ShouldIgnoreCardForCaveDiscardRatio(h)
                        && discardSetForCaveDelay.Contains(h.Template.Id));
                    caveHasDiscardPayoffIgnoringZeroMinion = caveDiscardCountIgnoringZeroMinion > 0;
                }
                catch { caveHasDiscardPayoffIgnoringZeroMinion = false; }

                // 6) 可用费用>=3：优先使?拻鎬?箣澧?
                try
                {
                    if (!ignoreTombCombo
                        && board.ManaAvailable >= 3
                        && hasPlayableSpell(Card.Cards.TLC_451)
                        && !caveHasDiscardPayoffIgnoringZeroMinion)
                    {
                        reason = "本回合法力>=3且可用咒怨之墓";
                        return true;
                    }
                }
                catch { }

                bool hasPreferredNonMerchantStarterForGuldan = false;
                try
                {
                    string starterReason = null;
                    hasPreferredNonMerchantStarterForGuldan = HasPreferredNonMerchantStarterForGuldan(board, handCount, out starterReason);
                }
                catch { hasPreferredNonMerchantStarterForGuldan = false; }

                // 7) 专卖?+ 古尔丹之手（手牌<=8锛屼笖当前最高费稳定=鍙?墜锛?
                if (handCount <= 8
                    && hasPlayableMinion(Card.Cards.ULD_163)
                    && hasCardInHand(Card.Cards.BT_300)
                    && highestAllAre(Card.Cards.BT_300)
                    && !hasPreferredNonMerchantStarterForGuldan)
                {
                    reason = "本回合可打专卖商且手牌<=8，最高费稳定=古尔丹之手=> 优先专卖商弃古手";
                    return true;
                }

                // 8) 场上/手上爪子 + 其它被弃组件（排?脊骞?鍙?墜锛?
                if (clawEquipped && heroCanAttackNow && discardComponents != null && discardComponents.Count > 0)
                {
                    var allowed = new HashSet<Card.Cards>(discardComponents);
                    allowed.Remove(Card.Cards.RLK_534);
                    allowed.Remove(Card.Cards.BT_300);

                    if (allowed.Count > 0 && highestAllInSet(discardComponents) && highest.Any(h => h != null && h.Template != null && allowed.Contains(h.Template.Id)))
                    {
                        reason = "已装备时空之爪且可攻击，最高费稳定为被弃组件排除弹幕/古手) => 优先弃组件";
                        return true;
                    }
                }

                // 9) 专卖?+ 其它被弃组件（排?脊骞?鍙?墜锛?
                if (hasPlayableMinion(Card.Cards.ULD_163) && discardComponents != null && discardComponents.Count > 0)
                {
                    var allowed = new HashSet<Card.Cards>(discardComponents);
                    allowed.Remove(Card.Cards.RLK_534);
                    allowed.Remove(Card.Cards.BT_300);

                    if (allowed.Count > 0 && highestAllInSet(discardComponents) && highest.Any(h => h != null && h.Template != null && allowed.Contains(h.Template.Id)))
                    {
                        reason = "本回合可打专卖商且最高费稳定为被弃组件排除弹幕/古手) => 优先专卖商弃组件";
                        return true;
                    }
                }

                // 10) 超光子弹幕+ 邪翼蝠/郊狼（能打出弹幕且手里有连段随从?
                if (hasPlayableSpell(Card.Cards.TIME_027) && (hasCardInHand(Card.Cards.YOD_032) || hasCardInHand(Card.Cards.TIME_047)))
                {
                    reason = "本回合可打超光子弹幕且手里有邪翼蝠/郊狼 => 优先弹幕连段";
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        /// <summary>
        /// 处理灵魂弹幕的所有?昏緫
        /// </summary>
        private void ProcessCard_RLK_534_SoulBarrage(ProfileParameters p, Board board,
            bool hasClawEquipped, bool hasDiscardComponentAtMax, bool canClickCave, bool canPlayCaveFromHand, bool lethalThisTurn)
        {
            // ==============================================
            // Card.Cards.RLK_534 - 灵魂弹幕（法术）
            // 说明：所有关于灵魂弹幕的?都在这里，按优先级高到?
            // ==============================================

            if (!board.HasCardInHand(Card.Cards.RLK_534)) return;

            // 预计算：启动组件（弃牌?发器?
            bool canPlaySoulfire = board.Hand.Any(h => h != null && h.Template != null
                && h.Template.Id == Card.Cards.EX1_308 && h.CurrentCost <= board.ManaAvailable);
            var discardSetForStarters = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
            bool soulfireStarterByAllDiscard = false;
            int soulfireStarterDiscardCount = 0;
            int soulfireStarterTotalCount = 0;
            try
            {
                soulfireStarterByAllDiscard = IsNonSoulfireAllDiscardComponents(
                    board, discardSetForStarters, out soulfireStarterDiscardCount, out soulfireStarterTotalCount);
            }
            catch
            {
                soulfireStarterByAllDiscard = false;
                soulfireStarterDiscardCount = 0;
                soulfireStarterTotalCount = 0;
            }
            bool canPlaySoulfireStarter = canPlaySoulfire && soulfireStarterByAllDiscard;

            bool clawCanAttackNow = false;
            try { clawCanAttackNow = hasClawEquipped && board.HeroFriend != null && board.HeroFriend.CanAttack; } catch { clawCanAttackNow = false; }

            // 应急窗口：低血且本回合无法分流时，放行灵魂弹幕直拍，避免被“先挥刀/先启动件”硬锁而空过。
            try
            {
                int myHpArmorNow = GetFriendlyHpArmor(board);
                int enemyBoardAttackNow = 0;
                try
                {
                    if (board.MinionEnemy != null) enemyBoardAttackNow += board.MinionEnemy.Where(m => m != null).Sum(m => m.CurrentAtk);
                    if (board.WeaponEnemy != null) enemyBoardAttackNow += board.WeaponEnemy.CurrentAtk;
                }
                catch { enemyBoardAttackNow = 0; }

                bool canPlayBarrageNow = board.Hand.Any(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.RLK_534
                    && h.CurrentCost <= board.ManaAvailable);
                bool lifeTapUnavailableNow = !CanUseLifeTapNow(board);
                bool lowHpEmergencyNow = myHpArmorNow <= 1 || (myHpArmorNow <= 4 && enemyBoardAttackNow >= myHpArmorNow);

                if (!lethalThisTurn && canPlayBarrageNow && lifeTapUnavailableNow && lowHpEmergencyNow)
                {
                    SetSoulBarrageDirectPlayModifier(p, board, -2600, 9920);
                    try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(800)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }

                    AddLog("[灵魂弹幕-应急放行] 我方血线危险且本回合不可分流(我血=" + myHpArmorNow
                        + ",敌攻=" + enemyBoardAttackNow + ") => 放行直拍灵魂弹幕(-2600/9920)，不再强制先挥刀");
                    return;
                }
            }
            catch { }

            // 嘲讽阻断时，若“海星解嘲讽 -> 走脸压费 -> 稳定挥刀弃弹幕”成立，则强制让位海星。
            try
            {
                int enemyTauntCountNow = 0;
                int friendlyReadyAttackNow = 0;
                try
                {
                    enemyTauntCountNow = board.MinionEnemy != null
                        ? board.MinionEnemy.Count(m => m != null && m.IsTaunt)
                        : 0;
                }
                catch { enemyTauntCountNow = 0; }

                try
                {
                    friendlyReadyAttackNow = board.MinionFriend != null
                        ? board.MinionFriend.Where(m => m != null && m.CanAttack).Sum(m => Math.Max(0, m.CurrentAtk))
                        : 0;
                }
                catch { friendlyReadyAttackNow = 0; }
                try
                {
                    if (board.HeroFriend != null && board.HeroFriend.CanAttack)
                        friendlyReadyAttackNow += Math.Max(0, board.HeroFriend.CurrentAtk);
                }
                catch { }

                string starfishYieldReason = null;
                bool shouldYieldToStarfish = ShouldPrioritizeStarfishForTauntBreakDiscardSetup(
                    board, lethalThisTurn, enemyTauntCountNow, friendlyReadyAttackNow, out starfishYieldReason);

                if (shouldYieldToStarfish)
                {
                    SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TSC_926, new Modifier(-2600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TSC_926, new Modifier(9950)); } catch { }

                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;
                        if (c.Template.Id != Card.Cards.TSC_926) continue;
                        try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(-2600)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9950)); } catch { }
                    }

                    p.ForceResimulation = true;
                    AddLog("[灵魂弹幕-让位海星] "
                        + (string.IsNullOrEmpty(starfishYieldReason) ? "命中解嘲讽推进线" : starfishYieldReason)
                        + " => 禁止直拍灵魂弹幕(9999)，先海星沉默嘲讽后走脸压费/挥刀");
                    return;
                }
            }
            catch { }

            // 斩杀压拳：当灵魂弹幕与加拉克苏斯之拳同回合都可用时，优先灵魂弹幕（更高伤害上限）。
            try
            {
                int enemyHpArmorNow = 0;
                try
                {
                    enemyHpArmorNow = board.HeroEnemy != null
                        ? (board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor)
                        : 0;
                }
                catch { enemyHpArmorNow = 0; }

                bool canPlayBarrageNow = board.Hand.Any(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.RLK_534
                    && h.CurrentCost <= board.ManaAvailable);
                bool canPlayFistNow = board.Hand.Any(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.AT_022
                    && h.CurrentCost <= board.ManaAvailable);

                if (lethalThisTurn && enemyHpArmorNow > 0 && enemyHpArmorNow <= 6 && canPlayBarrageNow && canPlayFistNow)
                {
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(9950)); } catch { }
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(1200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(-1200)); } catch { }

                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;
                        if (c.Template.Id == Card.Cards.RLK_534)
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-3200)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9950)); } catch { }
                        }
                        else if (c.Template.Id == Card.Cards.AT_022)
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(1200)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-1200)); } catch { }
                        }
                    }

                    AddLog("[灵魂弹幕-斩杀压拳] 斩杀窗口且敌方血池=" + enemyHpArmorNow
                        + "，同回合可打灵魂弹幕+加拉克苏斯之拳 => 优先灵魂弹幕，延后加拉克苏斯之拳");
                }
            }
            catch { }

            // 斩杀护栏：已装备时空之爪且最高费仅灵魂弹幕，并且本回合可打超光子时，
            // 禁止灵魂弹幕直拍，避免错过“超光子 -> 武器打脸(触发弃弹幕)”的稳定斩杀线。
            try
            {
                bool canPlayPhotonNow = board.Hand.Any(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.TIME_027 && h.CurrentCost <= board.ManaAvailable);
                bool enemyHasTauntNow = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                bool heroAlreadyAttackedNow = board.HeroFriend != null && board.HeroFriend.CountAttack > 0;
                bool heroFrozenNow = board.HeroFriend != null && board.HeroFriend.IsFrozen;
                bool clawLikelyCanSwingNow = hasClawEquipped && !heroAlreadyAttackedNow && !heroFrozenNow;

                bool highestOnlySoulBarrageNow = false;
                var handForDiscardNow = GetHandCardsForDiscardLogic(board);
                if (handForDiscardNow != null && handForDiscardNow.Count > 0)
                {
                    int maxCostNow = handForDiscardNow.Max(h => h != null ? h.CurrentCost : -1);
                    var highestNow = handForDiscardNow.Where(h => h != null && h.Template != null && h.CurrentCost == maxCostNow).ToList();
                    highestOnlySoulBarrageNow = highestNow.Count > 0
                        && highestNow.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                }

                if (lethalThisTurn && canPlayPhotonNow && clawLikelyCanSwingNow && highestOnlySoulBarrageNow && !enemyHasTauntNow)
                {
                    SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9950)); } catch { }
                    if (board.HeroEnemy != null)
                    {
                        try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, -2800, board.HeroEnemy.Id); } catch { }
                    }
                    if (board.MinionEnemy != null)
                    {
                        foreach (var m in board.MinionEnemy)
                        {
                            if (m == null || m.Template == null || m.IsTaunt) continue;
                            try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, 1200, m.Id); } catch { }
                        }
                    }
                    p.ForceResimulation = true;
                    AddLog("[灵魂弹幕-斩杀护栏] 已装备时空之爪且最高费仅灵魂弹幕(可稳定弃牌)，且超光子可打 => 禁止直拍灵魂弹幕(9999)，先超光子后武器打脸");
                    return;
                }
            }
            catch { }

            // 【优先级0 - 鏈?楂樸?手里能下窟穴且最高费有被弃组件：不要直拍弹幕，先?标处理弃?
            // 目的：避免?弹幕抢先打?-> 失去弃牌收益/节奏”的情况
            bool hasMerchantOnBoard = false;
            try
            {
                hasMerchantOnBoard = board.MinionFriend != null && board.MinionFriend.Any(m => m != null
                    && m.Template != null && m.Template.Id == Card.Cards.ULD_163);
            }
            catch { }

            // 【优先级0 - 鏈?楂樸?若“商?稳定弃到灵魂弹幕”，则绝不直拍灵魂弹幕
            // 鍙ｅ：无论是?柀鏉?，只要本回合可?瀹氳?发，就让位给商贩?
            bool stableByMerchantNow = false;
            bool merchantPlayableNowForStarter = false;
            bool needCoinForMerchantNow = false;
            Card merchantForBarrage = null;
            try
            {
                bool hasSpace = board.MinionFriend == null || board.MinionFriend.Count < 7;
                bool hasCoinNow = false;
                try { hasCoinNow = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinNow = false; }
                int effectiveManaNow = board.ManaAvailable + (hasCoinNow ? 1 : 0);

                merchantForBarrage = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163);
                merchantPlayableNowForStarter = merchantForBarrage != null && hasSpace && merchantForBarrage.CurrentCost <= effectiveManaNow;
                needCoinForMerchantNow = merchantForBarrage != null
                    && hasCoinNow
                    && merchantForBarrage.CurrentCost > board.ManaAvailable
                    && merchantForBarrage.CurrentCost <= effectiveManaNow;

                if (merchantPlayableNowForStarter)
                {
                    var handWithoutMerchantAndCoin = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                        && h.Template.Id != Card.Cards.ULD_163
                        && h.Template.Id != Card.Cards.GAME_005).ToList();

                    if (handWithoutMerchantAndCoin.Count > 0)
                    {
                        int maxCost = handWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                        var highest = handWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxCost).ToList();
                        stableByMerchantNow = highest.Count > 0
                            && highest.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                    }
                }
            }
            catch
            {
                stableByMerchantNow = false;
                merchantPlayableNowForStarter = false;
                needCoinForMerchantNow = false;
                merchantForBarrage = null;
            }

            if (stableByMerchantNow && merchantForBarrage != null)
            {
                SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-2500)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9300)); } catch { }
                try { p.CastMinionsModifiers.AddOrUpdate(merchantForBarrage.Id, new Modifier(-2500)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(merchantForBarrage.Id, new Modifier(9300)); } catch { }

                bool coinThenMerchant = TrySetCoinThenCardComboWithoutResim(p, board, merchantForBarrage, needCoinForMerchantNow, "灵魂弹幕-优先级");
                if (!coinThenMerchant)
                {
                    try { p.ComboModifier = new ComboSet(merchantForBarrage.Id); } catch { }
                }

                p.ForceResimulation = true;
	                AddLog("[灵魂弹幕-优先级] 商贩可稳定弃弹幕 => 绝对禁用直拍(9999)，强制让位商贩"
	                    + (needCoinForMerchantNow ? "(需硬币)" : ""));
                return;
            }

            // 【优先级0 - 鏈?楂樸?戝晢费前尚不?定，但手里有可压费郊狼且可走脸：先走脸压费再重算，仍禁止直拍弹幕
            // 目的：避免?滃厛A脸把郊狼降费后本可?定商?弹幕，却?悓涓?轮旧方案中把弹幕直拍掉?濄??
            bool merchantPotentialAfterCoyoteDiscount = false;
            try
            {
                bool hasSpace = board.MinionFriend == null || board.MinionFriend.Count < 7;
                bool hasCoinNow = false;
                try { hasCoinNow = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinNow = false; }
                int effectiveManaNow = board.ManaAvailable + (hasCoinNow ? 1 : 0);

                bool merchantPlayableNow = merchantForBarrage != null && hasSpace && merchantForBarrage.CurrentCost <= effectiveManaNow;
                bool hasNonZeroCoyote = board.Hand.Any(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.TIME_047
                    && h.CurrentCost > 0);
                bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                bool hasFaceAttacker = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
                merchantPotentialAfterCoyoteDiscount = merchantPlayableNow && hasNonZeroCoyote && hasFaceAttacker && !enemyHasTaunt;
            }
            catch { merchantPotentialAfterCoyoteDiscount = false; }

            if (merchantPotentialAfterCoyoteDiscount)
            {
                SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);
                AddLog("[灵魂弹幕-优先级] 商贩暂不稳定，且郊狼可压费并可走脸 => 暂禁直拍弹幕(9999)，先走脸压费后再重算");
                return;
            }

            // 鑻?狼当前无法压费，则允许??=50%弃到弹幕”的商贩概率线，并禁?拍弹幕抢节奏?
            bool merchantGambleByProbWhenCoyoteLocked = false;
            int gambleHighestCount = 0;
            int gambleBarrageCount = 0;
            bool needCoinForMerchantGamble = false;
            try
            {
                bool hasSpace = board.MinionFriend == null || board.MinionFriend.Count < 7;
                bool hasCoinNow = false;
                try { hasCoinNow = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinNow = false; }
                int effectiveManaNow = board.ManaAvailable + (hasCoinNow ? 1 : 0);

                bool merchantPlayableNow = merchantForBarrage != null && hasSpace && merchantForBarrage.CurrentCost <= effectiveManaNow;
                needCoinForMerchantGamble = merchantForBarrage != null
                    && hasCoinNow
                    && merchantForBarrage.CurrentCost > board.ManaAvailable
                    && merchantForBarrage.CurrentCost <= effectiveManaNow;

                bool hasNonZeroCoyote = board.Hand.Any(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.TIME_047
                    && h.CurrentCost > 0);
                bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                bool hasFaceAttacker = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
                bool heroCanFaceAttack = false;
                try { heroCanFaceAttack = board.HeroFriend != null && board.HeroFriend.CanAttack; } catch { heroCanFaceAttack = false; }
                bool canReduceCoyoteNow = !enemyHasTaunt && (hasFaceAttacker || heroCanFaceAttack);

                if (merchantPlayableNow && hasNonZeroCoyote && !canReduceCoyoteNow)
                {
                    var handWithoutMerchantAndCoin = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                        && h.Template.Id != Card.Cards.ULD_163
                        && h.Template.Id != Card.Cards.GAME_005).ToList();
                    if (handWithoutMerchantAndCoin.Count > 0)
                    {
                        int maxCost = handWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                        var highest = handWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxCost).ToList();
                        gambleHighestCount = highest.Count;
                        gambleBarrageCount = highest.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                        merchantGambleByProbWhenCoyoteLocked = gambleHighestCount > 0
                            && gambleBarrageCount * 2 >= gambleHighestCount;
                    }
                }
            }
            catch
            {
                merchantGambleByProbWhenCoyoteLocked = false;
                gambleHighestCount = 0;
                gambleBarrageCount = 0;
                needCoinForMerchantGamble = false;
            }

            if (merchantGambleByProbWhenCoyoteLocked && merchantForBarrage != null)
            {
                SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-900)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(6200)); } catch { }
                try { p.CastMinionsModifiers.AddOrUpdate(merchantForBarrage.Id, new Modifier(-900)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(merchantForBarrage.Id, new Modifier(6200)); } catch { }

                bool coinThenMerchant = TrySetCoinThenCardComboWithoutResim(p, board, merchantForBarrage, needCoinForMerchantGamble, "灵魂弹幕-优先级");
                if (!coinThenMerchant)
                {
                    try
                    {
                        if (p.ComboModifier == null)
                        {
                            p.ComboModifier = new ComboSet(merchantForBarrage.Id);
                        }
                    }
                    catch { }
                }

                p.ForceResimulation = true;
                AddLog("[灵魂弹幕-优先级] 郊狼当前不可压费，商贩弃弹幕概率="
                    + gambleBarrageCount + "/" + gambleHighestCount
                    + " >= 1/2 => 禁用直拍弹幕(9999)，允许赌商贩"
                    + (needCoinForMerchantGamble ? "(硬币起手)" : ""));
                return;
            }

            // 【优先级0 - 鏈?楂樸?只要有“启?粍浠垛?可用：禁止直拍灵魂弹幕
            // 诉求：有启动组件（魂?地标/爪子）时，灵魂弹幕应作为“被弃组件=被弃掉吃收益，而不是直?姳费费打出??
            bool highestDiscardRatioHalfOrMore = false;
            int highestStarterDiscardCount = 0;
            int highestStarterTotalCount = 0;
            try
            {
                highestDiscardRatioHalfOrMore = IsHighestCostDiscardRatioAtLeastHalf(
                    board, discardSetForStarters, out highestStarterDiscardCount, out highestStarterTotalCount);
            }
            catch
            {
                highestDiscardRatioHalfOrMore = false;
                highestStarterDiscardCount = 0;
                highestStarterTotalCount = 0;
            }
            bool merchantStarterAvailable = merchantPlayableNowForStarter && highestDiscardRatioHalfOrMore;
            bool caveStarterAvailable = canClickCave || (canPlayCaveFromHand && !hasMerchantOnBoard);

            // 额外护栏：手里有刀且当前最高费仅灵魂弹幕时，禁止直拍弹幕，优先提刀弃牌。
            try
            {
                bool hasCoinForClaw = false;
                try { hasCoinForClaw = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinForClaw = false; }
                int effectiveManaForClaw = board.ManaAvailable + (hasCoinForClaw ? 1 : 0);
                var clawInHandNow = board.Hand != null ? board.Hand.FirstOrDefault(h => h != null && h.Template != null
                    && h.Template.Id == Card.Cards.END_016) : null;
                bool hasClawEquippedNow = board.WeaponFriend != null && board.WeaponFriend.Template != null
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                    && board.WeaponFriend.CurrentDurability > 0;
                bool canEquipClawNow = clawInHandNow != null
                    && !hasClawEquippedNow
                    && clawInHandNow.CurrentCost <= effectiveManaForClaw;

                bool highestOnlySoulBarrageNow = false;
                try
                {
                    var handForHighest = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                        && h.Template.Id != Card.Cards.GAME_005).ToList();
                    if (handForHighest.Count > 0)
                    {
                        int maxCostNow = handForHighest.Max(h => h.CurrentCost);
                        var highestNow = handForHighest.Where(h => h.CurrentCost == maxCostNow).ToList();
                        highestOnlySoulBarrageNow = highestNow.Count > 0
                            && highestNow.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                    }
                }
                catch { highestOnlySoulBarrageNow = false; }

                if (!lethalThisTurn && canEquipClawNow && highestOnlySoulBarrageNow)
                {
                    SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);
                    bool needCoinForClaw = clawInHandNow != null && hasCoinForClaw
                        && clawInHandNow.CurrentCost > board.ManaAvailable
                        && clawInHandNow.CurrentCost <= effectiveManaForClaw;
                    bool coinThenClaw = TrySetCoinThenCardComboWithoutResim(p, board, clawInHandNow, needCoinForClaw, "灵魂弹幕-优先级");
                    if (!coinThenClaw)
                    {
                        try { p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2800)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9300)); } catch { }
                        try { p.CastWeaponsModifiers.AddOrUpdate(clawInHandNow.Id, new Modifier(-2800)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(clawInHandNow.Id, new Modifier(9300)); } catch { }
                    }
                    p.ForceResimulation = true;
                    AddLog("[灵魂弹幕-优先级] 手上有刀且最高费仅灵魂弹幕 => 禁止直拍弹幕(9999)，优先提刀");
                    return;
                }
            }
            catch { }

            // 直拍放行口径：
            // 当牌库为空，且当前无刀线、无地标线、无商贩稳定线时，不再强制“先启动组件”，允许直接拍灵魂弹幕。
            bool allowDirectBarrageByNoDeckNoStarter = false;
            try
            {
                bool deckEmptyNow = board != null && board.FriendDeckCount <= 0;
                bool hasClawEquippedNow = board.WeaponFriend != null
                    && board.WeaponFriend.Template != null
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                    && board.WeaponFriend.CurrentDurability > 0;
                bool hasClawInHandNow = board.Hand != null && board.Hand.Any(h => h != null
                    && h.Template != null
                    && h.Template.Id == Card.Cards.END_016);
                bool hasAnyCaveOnBoardNow = board.MinionFriend != null && board.MinionFriend.Any(m => m != null
                    && m.Template != null
                    && m.Template.Id == Card.Cards.WON_103);
                bool hasAnyCaveInHandNow = board.Hand != null && board.Hand.Any(h => h != null
                    && h.Template != null
                    && h.Template.Id == Card.Cards.WON_103);
                bool canPlayBarrageNowByNoDeckRule = board.Hand != null && board.Hand.Any(h => h != null
                    && h.Template != null
                    && h.Template.Id == Card.Cards.RLK_534
                    && h.CurrentCost <= board.ManaAvailable);

                allowDirectBarrageByNoDeckNoStarter = !lethalThisTurn
                    && deckEmptyNow
                    && canPlayBarrageNowByNoDeckRule
                    && !hasClawEquippedNow
                    && !hasClawInHandNow
                    && !hasAnyCaveOnBoardNow
                    && !hasAnyCaveInHandNow
                    && !merchantStarterAvailable;
            }
            catch { allowDirectBarrageByNoDeckNoStarter = false; }

            if (allowDirectBarrageByNoDeckNoStarter)
            {
                SetSoulBarrageDirectPlayModifier(p, board, -2600, 9920);
                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(1400)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(-1200)); } catch { }
                AddLog("[灵魂弹幕-直拍放行] 牌库为空且无刀/无地标/无商贩稳定线 => 允许直接使用灵魂弹幕(-2600/9920)");
                return;
            }

            // 新口径：有启?件时【绝对禁??灵魂弹幕直拍（不区分斩?/非斩杀锛夈??
            if (canPlaySoulfireStarter || clawCanAttackNow || caveStarterAvailable || merchantStarterAvailable)
            {
                SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);
                AddLog("[灵魂弹幕-优先级] 有启动组件(魂火=" + (canPlaySoulfireStarter ? "Y" : "N")
                    + ",窟穴=" + (caveStarterAvailable ? "Y" : "N")
                    + ",爪子可攻=" + (clawCanAttackNow ? "Y" : "N")
                    + ",商贩=" + (merchantStarterAvailable ? "Y" : "N")
                    + ",魂火密度=" + soulfireStarterDiscardCount + "/" + soulfireStarterTotalCount
                    + ") => 绝对禁用直拍灵魂弹幕(9999)，优先启动组件");

                // 顺带轻推魂火作为启动器（避免出现“禁?幕后仍不启动”）
                if (canPlaySoulfireStarter)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-9999));
                }
                if (merchantStarterAvailable && merchantForBarrage != null)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-900));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(6200));
                    p.CastMinionsModifiers.AddOrUpdate(merchantForBarrage.Id, new Modifier(-900));
                    p.PlayOrderModifiers.AddOrUpdate(merchantForBarrage.Id, new Modifier(6200));
                    AddLog("[灵魂弹幕-优先级] 商贩启动条件满足(当前最高费被弃占比="
                        + highestStarterDiscardCount + "/" + highestStarterTotalCount + ">=1/2) => 让位商贩");
                }
                p.ForceResimulation = true;
                return;
            }

            if (!hasMerchantOnBoard && canPlayCaveFromHand && hasDiscardComponentAtMax)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(800));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-800));
                AddLog("[灵魂弹幕-优先级] 手牌可下窟穴且最高费有被弃组件=> 禁用直拍(800)，先下地标");
                return;
            }

            // 【优先级0 - 鏈?楂樸?戞柀鏉?且手中有魂火（且当前无可打超光子）：先魂火后弹幕
            // 目的：在℃超光子可打时，保证『序为“魂?-> 灵魂弹幕”，避免弹幕抢先
            bool canPlayPhoton = board.Hand.Any(h => h != null && h.Template != null
                && h.Template.Id == Card.Cards.TIME_027 && h.CurrentCost <= board.ManaAvailable);
            if (lethalThisTurn && !canPlayPhoton && canPlaySoulfire)
            {
                AddLog("[灵魂弹幕-优先级] 斩杀且手中有魂火(无超光子可打) => 魂火仍保持最后，不额外前置魂火");
            }

            // 【优先级1 - 鏈?楂樸?装备爪子且有被弃组件：禁止直拍，走弃牌?彂
            if (hasClawEquipped && hasDiscardComponentAtMax)
            {
                // 若爪子本回合可攻击，则严格禁止直拍弹幕，先挥刀触发弃牌。
                if (clawCanAttackNow)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-9999));
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(800));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-800));
                }

                // 鑻?上窟穴可点，则优先点窟穴弃牌（更稳定，也避免浪费?櫒浼?去打不该打的目标?
                if (canClickCave)
                {
                    p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-999));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999));
                    p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(850));
                    p.ForceResimulation = true;
                    AddLog("[灵魂弹幕-优先级] 装备爪子且待弃收益就绪，且窟穴可点=> 先点窟穴(-999/9999)，延后挥刀(850)");
                    return;
                }

                p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(clawCanAttackNow ? -2500 : -250));
                p.ForceResimulation = true;
                AddLog("[灵魂弹幕-优先级] 装备爪子且待弃收益就绪"
                    + (clawCanAttackNow ? "且可攻击" : "")
                    + "=> 禁用直拍(" + (clawCanAttackNow ? "9999" : "800")
                    + ")，优先武器攻击触发弃牌(" + (clawCanAttackNow ? "-2500" : "-250") + ")并强制重算");
            }

            // 【优先级2銆戞柀鏉?窗口且地标可用：延后弹幕，优先等地标弃掉
            else if (lethalThisTurn && canClickCave)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(800));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-800));
                AddLog("[灵魂弹幕-优先级] 斩杀且地标可点=> 延后直拍，等地标弃掉");
                return;
            }

            // 【优先级3】有超光子弹幕：若同伤害/更优则强制优先超光子
            else if (board.Hand.Any(h => h != null && h.Template != null
                && h.Template.Id == Card.Cards.TIME_027 && h.CurrentCost <= board.ManaAvailable))
            {
                int spellPowerNow = 0;
                int enemyTargetsNow = 1;
                int photonEstimatedDamage = 0;
                int barrageEstimatedDamage = 0;
                try
                {
                    spellPowerNow = board.MinionFriend != null
                        ? board.MinionFriend.Where(m => m != null).Sum(m => m.SpellPower)
                        : 0;
                }
                catch { spellPowerNow = 0; }
                try
                {
                    enemyTargetsNow = Math.Max(1, 1 + (board.MinionEnemy != null
                        ? board.MinionEnemy.Count(m => m != null)
                        : 0));
                }
                catch { enemyTargetsNow = 1; }
                try
                {
                    photonEstimatedDamage = GetEstimatedFaceDamageForMode(Card.Cards.TIME_027, spellPowerNow, enemyTargetsNow);
                    barrageEstimatedDamage = GetEstimatedFaceDamageForMode(Card.Cards.RLK_534, spellPowerNow, enemyTargetsNow);
                }
                catch
                {
                    photonEstimatedDamage = 0;
                    barrageEstimatedDamage = 0;
                }

                if (photonEstimatedDamage >= barrageEstimatedDamage)
                {
                    SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-1800)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9800)); } catch { }
                    AddLog("[超光子/灵魂弹幕-等伤取舍] 超光子预估=" + photonEstimatedDamage
                        + " >= 灵魂弹幕预估=" + barrageEstimatedDamage
                        + " => 优先超光子，禁用直拍灵魂弹幕(9999)");
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(350));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-50));
                    AddLog("[灵魂弹幕-优先级] 手中有超光子弹幕但预估伤害较低(" + photonEstimatedDamage
                        + "<" + barrageEstimatedDamage + ") => 仅后置灵魂弹幕");
                }
                return;
            }

            // 【优先级4 - 默认】正常情况：允许使用
            else
            {
                AddLog("[灵魂弹幕-默认] 无特殊限制，允许正常使用");
            }
        }

        /// <summary>
        /// 处理恶魔之种（SW_091）与终章衍生（SW_091t4）优先级。
        /// 口径：
        /// 1. 手牌可用 SW_091t4 时最优先使用。
        /// 2. 手牌可用 SW_091 时直接使用（高优先）。
        /// </summary>
        private void ProcessCard_SW_091_Questline(ProfileParameters p, Board board)
        {
            if (p == null || board == null || board.Hand == null || board.Hand.Count == 0) return;

            bool hasBoardSlot = true;
            try { hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7; } catch { hasBoardSlot = true; }

            var t4InHand = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id.ToString().Equals("SW_091t4", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var t4Playable = t4InHand
                .Where(c => c != null
                    && c.CurrentCost <= board.ManaAvailable
                    && (c.Type != Card.CType.MINION || hasBoardSlot))
                .ToList();

            if (t4Playable.Count > 0)
            {
                foreach (var c in t4Playable)
                {
                    if (c == null) continue;
                    try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(-6000)); } catch { }
                    try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-6000)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(10000)); } catch { }
                }

                try
                {
                    var parsed = (Card.Cards)Enum.Parse(typeof(Card.Cards), "SW_091t4", false);
                    try { p.CastMinionsModifiers.AddOrUpdate(parsed, new Modifier(-6000)); } catch { }
                    try { p.CastSpellsModifiers.AddOrUpdate(parsed, new Modifier(-6000)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(parsed, new Modifier(10000)); } catch { }
                }
                catch { }

                AddLog("[恶魔之种-终章优先] 手牌有SW_091t4且可用 => 最优先使用");
                return;
            }

            if (t4InHand.Count > 0 && !hasBoardSlot)
            {
                AddLog("[恶魔之种-终章优先] 手牌有SW_091t4但场位已满 => 待腾场后优先使用");
            }

            var sw091Playable = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == Card.Cards.SW_091
                    && c.CurrentCost <= board.ManaAvailable)
                .ToList();

            if (sw091Playable.Count <= 0) return;

            foreach (var c in sw091Playable)
            {
                if (c == null) continue;
                try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-4200)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9850)); } catch { }
            }

            try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.SW_091, new Modifier(-4200)); } catch { }
            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.SW_091, new Modifier(9850)); } catch { }

            AddLog("[恶魔之种-任务优先] 手牌有SW_091且可用 => 直接使用");
        }

        /// <summary>
        /// 处理栉龙和狗头人的过牌优先级
        /// </summary>
        private void ProcessCard_DrawMinions(ProfileParameters p, Board board)
        {
            // ==============================================
            // 过牌随从优先级：栉龙 > 鐙楀?浜?> 分流
            // ==============================================

            if (board.Hand == null || board.Hand.Count == 0) return;

            // 妫?鏌?槸鍚?硬币，扩展可?硶鍔?
            bool hasCoin = false;
            try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { }
            int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);

            // 找出?有可?牌随?
            var dragons = board.Hand.Where(c => c != null && c.Template != null 
                && c.Template.Id == Card.Cards.TLC_603 && c.CurrentCost <= effectiveMana).ToList();
            var kobolds = board.Hand.Where(c => c != null && c.Template != null 
                && c.Template.Id == Card.Cards.LOOT_014 && c.CurrentCost <= effectiveMana).ToList();

            // 新口径：牌库为空时，不使用狗头人图书管理员。
            // 例外：若 SW_091t4（疲劳转伤）已生效，则允许继续过牌。
            bool deckEmptyNow = IsDeckEmptyNow(board);
            bool fatigueRedirectByQuest = HasPlayedBlightbornTamsin(board);

              // 口径：T1手里?费海盗时，暂时禁?头人，避免先下狗头人亏节奏??
              bool suppressKoboldThisTurn = false;
              try
              {
                  bool isTurn1 = board.MaxMana == 1;
                bool hasPlayableOneCostPirate = false;
                try
                {
                    // 任意“海盗?随从，且当前费?=1（并且本回合能打出来?
                    hasPlayableOneCostPirate = board.Hand.Any(c => c != null && c.Template != null
                        && c.Type == Card.CType.MINION
                        && c.CurrentCost <= 1
                        && c.CurrentCost <= effectiveMana
                        && (c.IsRace(Card.CRace.PIRATE) || c.IsRace(Card.CRace.ALL)));
                }
                catch { hasPlayableOneCostPirate = false; }

                bool coinUsableAndNotDisabled = false;
                try
                {
                    if (hasCoin)
                    {
                        var coinCard = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.GAME_005);
                        if (coinCard != null)
                        {
                            bool coinHardDisabled = false;
                            try
                            {
                                Func<RulesSet, Card.Cards, int, bool> isDisabledInRulesSet = (rules, cardId, instanceId) =>
                                {
                                    try
                                    {
                                        if (rules == null) return false;

                                        Rule r = null;
                                        try { r = rules.RulesCardIds != null ? rules.RulesCardIds[cardId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                        try { r = rules.RulesIntIds != null ? rules.RulesIntIds[(int)cardId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                        try { r = rules.RulesCardIds != null ? rules.RulesCardIds[(Card.Cards)instanceId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                        try { r = rules.RulesIntIds != null ? rules.RulesIntIds[instanceId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;
                                    }
                                    catch { }
                                    return false;
                                };

                                coinHardDisabled = isDisabledInRulesSet(p.CastSpellsModifiers, Card.Cards.GAME_005, coinCard.Id);
                            }
                            catch { coinHardDisabled = false; }

                            coinUsableAndNotDisabled = !coinHardDisabled;
                        }
                    }
                }
                catch { coinUsableAndNotDisabled = false; }

                bool hasSketchInHandForCoinOpen = false;
                bool hasTlc451InHandForCoinOpen = false;
                try { hasSketchInHandForCoinOpen = board.HasCardInHand(Card.Cards.TOY_916); } catch { hasSketchInHandForCoinOpen = false; }
                try { hasTlc451InHandForCoinOpen = board.HasCardInHand(Card.Cards.TLC_451); } catch { hasTlc451InHandForCoinOpen = false; }

                bool preferKoboldBeforePirateByCoin = isTurn1
                    && kobolds.Count > 0
                    && hasPlayableOneCostPirate
                    && coinUsableAndNotDisabled
                    && !hasSketchInHandForCoinOpen
                    && !hasTlc451InHandForCoinOpen;

                suppressKoboldThisTurn = isTurn1
                    && kobolds.Count > 0
                    && hasPlayableOneCostPirate
                    && !preferKoboldBeforePirateByCoin;

                  if (suppressKoboldThisTurn)
                  {
                      p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9999));
                      p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-999));
                      AddLog("[过牌优先级] T1手里有1费海盗 => 暂禁狗头人(9999)，优先铺海盗");
                  }
                  else if (preferKoboldBeforePirateByCoin)
                  {
                      AddLog("[过牌优先级] T1有可用硬币+1费海盗，且无速写/无墓 => 放行狗头人先手，再海盗");
                  }
              }
              catch { suppressKoboldThisTurn = false; }
 
              // 新口径：手里有空降歹徒且有可用1费海盗时，优先海盗拉空降歹徒，狗头人让位。
              try
              {
                  bool hasParachuteInHand = board.HasCardInHand(Card.Cards.DRG_056);
                  bool hasPlayableOneCostPirateNow = board.Hand.Any(c => c != null && c.Template != null
                      && c.Type == Card.CType.MINION
                      && c.CurrentCost <= board.ManaAvailable
                      && (c.IsRace(Card.CRace.PIRATE) || c.IsRace(Card.CRace.ALL)));
 
                  if (hasParachuteInHand && hasPlayableOneCostPirateNow)
                  {
                      suppressKoboldThisTurn = true;
                      p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9999));
                      p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-999));
                      AddLog("[过牌优先级] 手牌有空降歹徒且有可用1费海盗 => 暂禁狗头人(9999)，先海盗拉空降歹徒");
                  }
              }
              catch { }

              if (deckEmptyNow && kobolds.Count > 0)
              {
                if (fatigueRedirectByQuest)
                {
                    AddLog("[过牌优先级-牌库空例外] deck=0 且SW_091t4已生效 => 放行狗头人过牌");
                }
                else
                {
                    suppressKoboldThisTurn = true;
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-999)); } catch { }
                    try { kobolds.Clear(); } catch { }
                    AddLog("[过牌优先级-牌库空] deck=0 => 禁用狗头人图书管理员(9999)");
                }
            }

            if (dragons.Count == 0 && kobolds.Count == 0) return;

            // 【栉龙?最高优先级
            if (dragons.Count > 0)
            {
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(9999));
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-2000));
                AddLog("[过牌优先级] 栉龙最高优先级(9999/-2000)");
            }

            // 【狗头人】次高优先级
            if (kobolds.Count > 0 && !suppressKoboldThisTurn)
            {
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9500));
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-1500));
                AddLog("[过牌优先级] 狗头人次高优先级(9500/-1500)");
            }

            // 【分流?后置于?随从
            if (dragons.Count > 0 || kobolds.Count > 0)
            {
                try
                {
                    AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(800));
                    AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(800));
                    AddLog("[过牌优先级] 抑制分流(800)，优先过牌随从");
                }
                catch { }
            }
        }

        /// <summary>
        /// 处理异教低阶牧师（CORE_SCH_713）在“剩余2费”场景下的余费利用兜底。
        /// 目标：避免打完主线动作后剩2费直接空过。
        /// </summary>
        private void ProcessCard_CORE_SCH_713_Neophyte(ProfileParameters p, Board board, bool lethalThisTurn)
        {
            try
            {
                if (p == null || board == null || board.Hand == null || board.Hand.Count == 0) return;
                if (lethalThisTurn) return;

                var neophytes = board.Hand
                    .Where(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.CORE_SCH_713)
                    .ToList();
                if (neophytes.Count == 0) return;

                // 仅在“本回合剩余正好2费”时触发，作为余费利用兜底，不干扰其它主连段。
                if (board.ManaAvailable != 2) return;

                bool hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7;
                if (!hasBoardSlot) return;

                bool canPlayNeophyteNow = neophytes.Any(c => c.CurrentCost <= board.ManaAvailable);
                if (!canPlayNeophyteNow) return;

                // 场上可点地标时让位地标，不覆盖地标优先线。
                bool canClickCaveNow = false;
                try
                {
                    canClickCaveNow = board.MinionFriend != null
                        && board.MinionFriend.Any(c => c != null
                            && c.Template != null
                            && c.Template.Id == Card.Cards.WON_103
                            && GetTag(c, Card.GAME_TAG.EXHAUSTED) != 1);
                }
                catch { canClickCaveNow = false; }
                if (canClickCaveNow) return;

                // 可稳定商贩连段时让位商贩，不抢核心弃牌线。
                bool hasPlayableMerchantNow = false;
                try
                {
                    hasPlayableMerchantNow = board.Hand.Any(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.ULD_163
                        && c.CurrentCost <= board.ManaAvailable);
                }
                catch { hasPlayableMerchantNow = false; }
                if (hasPlayableMerchantNow) return;

                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_SCH_713, new Modifier(8600));
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_SCH_713, new Modifier(-1200));
                foreach (var n in neophytes.Where(c => c != null && c.CurrentCost <= board.ManaAvailable))
                {
                    try { p.PlayOrderModifiers.AddOrUpdate(n.Id, new Modifier(8600)); } catch { }
                    try { p.CastMinionsModifiers.AddOrUpdate(n.Id, new Modifier(-1200)); } catch { }
                }

                AddLog("[异教低阶牧师-余费利用] 剩余法力=2且可下牧师，且未命中地标/商贩关键线 => 优先落牧师(8600/-1200)");
            }
            catch { }
        }

        /// <summary>
        /// 处理过期?专卖商的?鏈夐?昏緫
        /// </summary>
        private void ProcessCard_ULD_163_Merchant(ProfileParameters p, Board board,
            bool hasMerchantOnBoard, int handCount, HashSet<Card.Cards> discardComponents)
        {
            // ==============================================
            // Card.Cards.ULD_163 - 过期?专卖商（随从?
            // 说明?
            // 1) 专卖商在场时优先攻击送死
            // 2) 专卖商在手且能?定弃到关键被弃组件时，按?强推下专卖商
            // ==============================================

            // 【优先级1】场上有专卖商：提高攻击优先级
            if (hasMerchantOnBoard)
            {
                // 硬限制：手牌>8 鏃堕?死会高概率?（亡语给2张）=> 禁止送死
                // 说明：这里的 handCount 鏄??当前策?角手牌数”，不?虑即将弃牌/抽牌的变化，按保守策??鐞嗐??
                if (handCount > 8)
                {
                    p.AttackOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-350));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(350));
                    AddLog("[过期货物专卖商-在场] 手牌>8(当前" + handCount + ") => 禁止送死，避免爆牌(攻序-350,价值+350)");
                    return;
                }

                // 在场商贩默认目标：优先撞敌方可还手随从（尽量当回合送掉，触发亡语复制）。
                bool hasSuicideTarget = false;
                try
                {
                    if (board.MinionEnemy != null)
                    {
                        foreach (var e in board.MinionEnemy.Where(x => x != null && x.Template != null))
                        {
                            if (e.CurrentAtk < 1) continue;
                            hasSuicideTarget = true;
                            try
                            {
                                int targetPrio = e.IsTaunt ? 9800 : 9300;
                                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(e.Template.Id, new Modifier(targetPrio));
                            }
                            catch { }
                        }
                    }
                }
                catch { hasSuicideTarget = false; }

                // 浜?识别：?氳繃 ETC_424 鐨?DeathrattleCard 拿到“被弃的ｅ紶鐗屸??
                // 鍙ｅ緞锛?
                // - 鑻?语里是灵魂弹幕RLK_534)：优先级死拿两张弹幕复制??
                // - 鑻?语里是古尔丹之手(BT_300)锛?
                //   当手里仍有灵魂弹幕且有弃牌启?件时，暂时不送（避免先拿?堆古手堵手影响弃弹幕?垝锛夛紱
                //   等到手里?脊骞?鎴?手牌<=4 鍐嶉?併??

                bool drIsSoulBarrage = false;
                bool drIsGuldanHand = false;
                bool hasDrInfo = false;
                try
                {
                    if (board.MinionFriend != null)
                    {
                        foreach (var m in board.MinionFriend.ToArray())
                        {
                            if (m == null || m.Template == null) continue;
                            if (m.Template.Id != Card.Cards.ULD_163) continue;

                            try
                            {
                                var ench = m.Enchantments != null
                                    ? m.Enchantments.FirstOrDefault(x => x != null
                                        && x.EnchantCard != null && x.EnchantCard.Template != null
                                        && x.EnchantCard.Template.Id == Card.Cards.ETC_424)
                                    : null;
                                if (ench != null && ench.DeathrattleCard != null && ench.DeathrattleCard.Template != null)
                                {
                                    hasDrInfo = true;
                                    var drId = ench.DeathrattleCard.Template.Id;
                                    if (drId == Card.Cards.RLK_534) drIsSoulBarrage = true;
                                    if (drId == Card.Cards.BT_300) drIsGuldanHand = true;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                // 璁＄畻鈥滃惎鍔?粍浠垛?濇槸鍚?瓨鍦?紙鐢?簬鍙?丹之手亡语时的暂缓?佹?锛?
                bool hasSoulBarrageInHand = false;
                bool hasStarterComponents = false;
                try
                {
                    hasSoulBarrageInHand = board.HasCardInHand(Card.Cards.RLK_534);
                    var discardSetForStarter = discardComponents != null && discardComponents.Count > 0
                        ? new HashSet<Card.Cards>(discardComponents)
                        : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                    bool hasCoin = false;
                    try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }
                    int effectiveManaStarter = board.ManaAvailable + (hasCoin ? 1 : 0);

                    bool canPlaySoulfire = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.EX1_308 && h.CurrentCost <= board.ManaAvailable);
                    bool soulfireStarterByAllDiscard = false;
                    try
                    {
                        int tmpDiscard = 0, tmpTotal = 0;
                        soulfireStarterByAllDiscard = IsNonSoulfireAllDiscardComponents(board, discardSetForStarter, out tmpDiscard, out tmpTotal);
                    }
                    catch { soulfireStarterByAllDiscard = false; }
                    bool canPlaySoulfireStarter = canPlaySoulfire && soulfireStarterByAllDiscard;

                    bool clawCanAttackNow = false;
                    try
                    {
                        clawCanAttackNow = board.WeaponFriend != null && board.WeaponFriend.Template != null
                            && board.WeaponFriend.Template.Id == Card.Cards.END_016
                            && board.HeroFriend != null && board.HeroFriend.CanAttack;
                    }
                    catch { clawCanAttackNow = false; }
                    bool highestDiscardRatioHalfOrMore = false;
                    try
                    {
                        int tmpHighestDiscard = 0, tmpHighestTotal = 0;
                        highestDiscardRatioHalfOrMore = IsHighestCostDiscardRatioAtLeastHalf(board, discardSetForStarter, out tmpHighestDiscard, out tmpHighestTotal);
                    }
                    catch { highestDiscardRatioHalfOrMore = false; }
                    bool hasClawInHandAndReady = false;
                    try
                    {
                        hasClawInHandAndReady = board.Hand != null && effectiveManaStarter >= 4
                            && board.Hand.Any(c => c != null && c.Template != null
                                && c.Template.Id == Card.Cards.END_016
                                && c.CurrentCost <= effectiveManaStarter);
                    }
                    catch { hasClawInHandAndReady = false; }
                    bool clawStarterByHighestHalf = highestDiscardRatioHalfOrMore && (clawCanAttackNow || hasClawInHandAndReady);

                    bool canClickCaveNow = false;
                    try
                    {
                        var clickableCaveOnBoardForStarter = board.MinionFriend?.FirstOrDefault(x => x != null && x.Template != null
                            && x.Template.Id == Card.Cards.WON_103 && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
                        canClickCaveNow = clickableCaveOnBoardForStarter != null;
                    }
                    catch { canClickCaveNow = false; }

                    bool canPlayCaveFromHandForStarter = false;
                    try
                    {
                        var caveInHand = board.Hand != null ? board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103) : null;
                        canPlayCaveFromHandForStarter = caveInHand != null
                            && handCount <= 8
                            && effectiveManaStarter >= 3
                            && caveInHand.CurrentCost <= effectiveManaStarter
                            && GetFriendlyBoardSlotsUsed(board) < 7;
                    }
                    catch { canPlayCaveFromHandForStarter = false; }

                    bool merchantStarterNow = false;
                    try
                    {
                        bool hasSpace = GetFriendlyBoardSlotsUsed(board) < 7;
                        var merchant = board.Hand != null ? board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163) : null;
                        bool merchantPlayableNow = merchant != null && hasSpace && merchant.CurrentCost <= effectiveManaStarter;
                        merchantStarterNow = merchantPlayableNow && highestDiscardRatioHalfOrMore;
                    }
                    catch { merchantStarterNow = false; }

                    hasStarterComponents = canPlaySoulfireStarter || clawStarterByHighestHalf || canClickCaveNow || canPlayCaveFromHandForStarter || merchantStarterNow;
                }
                catch { }

                // 分流：亡?灵魂弹幕 => 寮洪?侊紱亡语=古尔丹之手=> 有弹?启动组件且手牌>4 时暂?
                if (hasDrInfo && drIsSoulBarrage)
                {
                    p.AttackOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9999));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-1800));
                    AddLog("[过期货物专卖商-在场] 亡语=灵魂弹幕 => 强制优先送死(攻序9999,价值-1800,target=" + (hasSuicideTarget ? "Y" : "N") + ")");
                    return;
                }

                if (hasDrInfo && drIsGuldanHand)
                {
                    bool shouldDelaySacrifice = hasSoulBarrageInHand && hasStarterComponents && handCount > 4;
                    if (shouldDelaySacrifice)
                    {
                        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-350));
                        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(350));
                        AddLog("[过期货物专卖商-在场] 亡语=古尔丹之手，且手里有弹幕+启动组件且手牌>4 => 暂缓送死(攻序-350,价值+350)");
                        return;
                    }

                    p.AttackOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9800));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-1200));
                    AddLog("[过期货物专卖商-在场] 亡语=古尔丹之手且已放行 => 优先送死(9800/-1200,target=" + (hasSuicideTarget ? "Y" : "N") + ")");
                    return;
                }

                // 默认：保持原有?在场更愿意送死”口?
                p.AttackOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9600));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-900));
                AddLog("[过期货物专卖商-在场] 默认放行 => 优先送死(9600/-900,target=" + (hasSuicideTarget ? "Y" : "N") + ")");
                return;
            }

            // 【优先级2】手里有专卖商：当能稳定弃到指定被弃组件时，强推下专卖商
            // 注：专卖商战吼弃“最高费”牌；若最高费有并列，则随机弃其中之一?
            // 为避免误弃非目标牌：仅在“最高费牌全?于允许的被弃组件集合”时才?鍙戙??
            try
            {
                if (board.Hand == null || board.Hand.Count == 0) return;
                if (!board.HasCardInHand(Card.Cards.ULD_163)) return;

                bool hasCoin = false;
                try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }
                int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);

                var merchant = board.Hand.FirstOrDefault(x => x != null && x.Template != null
                    && x.Template.Id == Card.Cards.ULD_163);
                if (merchant == null) return;
                if (merchant.CurrentCost > effectiveMana) return;
                if (board.MinionFriend != null && board.MinionFriend.Count >= 7) return;

                // 场上地标可点时，优先点地标再考虑商贩（避免商贩抢先打断地标弃牌节奏）。
                try
                {
                    bool canClickCaveNow = false;
                    try
                    {
                        var clickableCaveOnBoard = board.MinionFriend?.FirstOrDefault(x => x != null && x.Template != null
                            && x.Template.Id == Card.Cards.WON_103 && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
                        canClickCaveNow = clickableCaveOnBoard != null;
                    }
                    catch { canClickCaveNow = false; }

                    if (!lethalThisTurn && canClickCaveNow)
                    {
                        bool hasDiscardComponentInHand = false;
                        try
                        {
                            var discardSet = discardComponents != null && discardComponents.Count > 0
                                ? new HashSet<Card.Cards>(discardComponents)
                                : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                            hasDiscardComponentInHand = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null
                                && h.Template.Id != Card.Cards.WON_103
                                && h.Template.Id != Card.Cards.GAME_005
                                && discardSet.Contains(h.Template.Id));
                        }
                        catch { hasDiscardComponentInHand = false; }

                        if (hasDiscardComponentInHand)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(350));
                            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-900));
                            AddLog("[过期货物专卖商-让位地标] 场上窟穴可点且手里有被弃组件 => 先点地标，商贩后置");
                            return;
                        }
                    }
                }
                catch { }

                // 说明：商贩安全判定必须按“全手牌”看最高费，不能只看弃牌过滤手牌，
                // 否则会出现“肉眼最高费非被弃组件，但逻辑误放行商贩”的问题。
                var handWithoutMerchant = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                    && h.Template.Id != Card.Cards.ULD_163).ToList();
                var handWithoutMerchantRaw = board.Hand.Where(h => h != null && h.Template != null
                    && h.Template.Id != Card.Cards.ULD_163).ToList();
                if (handWithoutMerchantRaw.Count == 0) return;
                if (handWithoutMerchant.Count == 0) return;

                // “尽量只给灵魂弹幕用”的ｅ緞锛?
                // - 只有当?最高费的牌仅包含灵魂弹幕RLK_534)】时，才强烈优先打出专卖商??
                // - 其它情况：若当前还有其它可用?（其它可打牌/分流），则延后专卖商?
                //            实在?法（无其它动作）才作为兜底?冭檻銆?
                bool canTapNowLocal = false;
                try { canTapNowLocal = CanUseLifeTapNow(board); }
                catch { canTapNowLocal = false; }

                bool hasOtherPlayableCardNow = false;
                try
                {
                    hasOtherPlayableCardNow = handWithoutMerchant.Any(h => h != null && h.Template != null
                        && h.CurrentCost <= effectiveMana
                        && !IsForeignInjectedCardForDiscardLogic(board, h)
                        && h.Template.Id != Card.Cards.GAME_005);
                }
                catch { hasOtherPlayableCardNow = false; }

                bool hasOtherActionsNow = canTapNowLocal || hasOtherPlayableCardNow;

                int maxCost = handWithoutMerchant.Max(h => h.CurrentCost);
                var highestCostCards = handWithoutMerchant.Where(h => h.CurrentCost == maxCost).ToList();
                if (highestCostCards.Count == 0) return;

                int maxCostRaw = handWithoutMerchantRaw.Max(h => h.CurrentCost);
                var highestCostCardsRaw = handWithoutMerchantRaw.Where(h => h.CurrentCost == maxCostRaw).ToList();
                if (highestCostCardsRaw.Count == 0) return;

                // 当前最高费?含非“被弃组件=，则跳过（避免随机弃错 / 璇??鍙戯級
                bool highestAllAllowed = highestCostCardsRaw.All(h => h != null && h.Template != null
                    && discardComponents != null && discardComponents.Contains(h.Template.Id));

                bool canStabilizeMerchantAfterCoyoteDiscount = false;
                string coyoteMerchantReason = null;
                try
                {
                    canStabilizeMerchantAfterCoyoteDiscount = CanStabilizeMerchantBarrageAfterCoyoteFaceDiscount(
                        board,
                        effectiveMana,
                        out coyoteMerchantReason);
                }
                catch
                {
                    canStabilizeMerchantAfterCoyoteDiscount = false;
                    coyoteMerchantReason = null;
                }

                // 硬规则：商贩只在“最高费组命中被弃组件”时才允许使用；
                // 否则直接禁用，避免把商贩当普通2费随从打掉关键弃牌窗口。
                if (!highestAllAllowed)
                {
                    if (canStabilizeMerchantAfterCoyoteDiscount)
                    {
                        AddLog("[过期货物专卖商-硬规则放行] "
                            + (string.IsNullOrEmpty(coyoteMerchantReason) ? "可走脸压费后稳定弃弹幕" : coyoteMerchantReason)
                            + " => 暂不禁用商贩，允许压费后商贩线");
                    }
                    else
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9999));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-9999));
                        string highestRawIds = string.Join(",", highestCostCardsRaw
                            .Where(h => h != null && h.Template != null)
                            .Select(h => h.Template.Id.ToString()));
                        AddLog("[过期货物专卖商-硬规则] 当前最高费组无被弃组件(按全手牌,highest=[" + highestRawIds + "]) => 禁用使用(9999/-9999)");
                        return;
                    }
                }

                bool highestOnlySoulBarrage = false;
                try
                {
                    highestOnlySoulBarrage = highestCostCards.All(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.RLK_534);
                }
                catch { highestOnlySoulBarrage = false; }

                bool highestOnlyGuldanHand = false;
                try
                {
                    highestOnlyGuldanHand = highestCostCards.All(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.BT_300);
                }
                catch { highestOnlyGuldanHand = false; }

                // 最高优先：只在“最高费仅灵魂弹幕?时强推
                if (highestOnlySoulBarrage)
                {
                    int barrageCount = 0;
                    try
                    {
                        barrageCount = handWithoutMerchant.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                    }
                    catch { barrageCount = 0; }

                    // 用户口径：过期货物专卖商复制弹幕为最优先（无论张数），优先启动组件
                    int merchantCastMod = -9999;
                    int merchantOrderMod = 9999;

                    // 鑻?渶瑕佺?币启动：强制 coin->merchant
                    if (merchant.CurrentCost > board.ManaAvailable && hasCoin)
                    {
                        var coin = board.Hand.FirstOrDefault(h => h != null && h.Template != null && h.Template.Id == Card.Cards.GAME_005);
                        if (coin != null)
                        {
                            p.ComboModifier = new ComboSet(coin.Id, merchant.Id);
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(merchantCastMod));
                            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(merchantOrderMod));
                            AddLog("[过期货物专卖商-优先级a] 当前最高费仅灵魂弹幕费用" + maxCost + ")且需硬币 => 强推 硬币->商贩(ComboSet + " + merchantOrderMod + "/" + merchantCastMod + ")");
                            return;
                        }
                    }

                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(merchantCastMod));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(merchantOrderMod));
                    try
                    {
                        if (p.ComboModifier == null)
                        {
                            p.ComboModifier = new ComboSet(merchant.Id);
                        }
                    }
                    catch { }
                    AddLog("[过期货物专卖商-优先级a] 当前最高费仅灵魂弹幕费用" + maxCost + ") => 复制弹幕最优先(" + merchantOrderMod + "/" + merchantCastMod + ", ComboSet=商贩)");
                    return;
                }

                // 古手联动口径：
                // 当最高费仅古尔丹之手，且存在“非商贩”的弃牌启动器时，优先其它组件触发古手过牌，
                // 商贩后置，尽量留给灵魂弹幕。
                if (highestOnlyGuldanHand)
                {
                    bool hasPreferredNonMerchantStarter = false;
                    string starterReason = null;
                    try
                    {
                        hasPreferredNonMerchantStarter = HasPreferredNonMerchantStarterForGuldan(
                            board, handCount, out starterReason);
                    }
                    catch
                    {
                        hasPreferredNonMerchantStarter = false;
                    }

                    if (hasPreferredNonMerchantStarter)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(650));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-1200));
                        AddLog("[过期货物专卖商-古手联动] 最高费仅古尔丹之手，且存在其它弃牌启动("
                            + (string.IsNullOrEmpty(starterReason) ? "未知" : starterReason)
                            + ") => 优先其它组件触发古手过牌，商贩后置保留给灵魂弹幕");
                        return;
                    }
                }

                // 压费稳定线：当前可走脸压费并将最高费稳定转为灵魂弹幕时，
                // 直接前置商贩，并让位郊狼/邪翼蝠，避免抢费打断连段。
                if (canStabilizeMerchantAfterCoyoteDiscount)
                {
                    int merchantCastMod = -2200;
                    int merchantOrderMod = 9800;

                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(merchantCastMod));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(merchantOrderMod));
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-3600)); } catch { }
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-3600)); } catch { }
                    try
                    {
                        if (p.ComboModifier == null)
                        {
                            p.ComboModifier = new ComboSet(merchant.Id);
                        }
                    }
                    catch { }

                    p.ForceResimulation = true;
                    AddLog("[过期货物专卖商-压费稳定线] "
                        + (string.IsNullOrEmpty(coyoteMerchantReason) ? "可走脸压费后稳定弃弹幕" : coyoteMerchantReason)
                        + " => 前置商贩(" + merchantOrderMod + "/" + merchantCastMod + ")，并让位郊狼/邪翼蝠");
                    return;
                }

                // 概率口径：若手里有非0费郊?弹幕+商贩，且本回合无法?过走脸?给郊狼压费，
                // 只要“商?到弹幕?的概率 >= 1/2，也允许赌商贩(??
                bool hasNonZeroCoyote = false;
                bool canReduceCoyoteNow = false;
                int barrageAtHighestCount = 0;
                int highestAtCount = 0;
                try
                {
                    hasNonZeroCoyote = board.Hand.Any(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.TIME_047
                        && h.CurrentCost > 0);

                    highestAtCount = highestCostCards.Count;
                    barrageAtHighestCount = highestCostCards.Count(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.RLK_534);

                    if (hasNonZeroCoyote)
                    {
                        bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                        bool hasFaceAttacker = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
                        bool heroCanFaceAttack = false;
                        try { heroCanFaceAttack = board.HeroFriend != null && board.HeroFriend.CanAttack; } catch { heroCanFaceAttack = false; }
                        canReduceCoyoteNow = !enemyHasTaunt && (hasFaceAttacker || heroCanFaceAttack);
                    }
                }
                catch
                {
                    hasNonZeroCoyote = false;
                    canReduceCoyoteNow = false;
                    barrageAtHighestCount = 0;
                    highestAtCount = 0;
                }

                bool canGambleMerchantForBarrage = hasNonZeroCoyote
                    && !canReduceCoyoteNow
                    && highestAtCount > 0
                    && barrageAtHighestCount * 2 >= highestAtCount;

                if (canGambleMerchantForBarrage)
                {
                    bool needCoinForMerchant = merchant.CurrentCost > board.ManaAvailable && hasCoin;
                    int merchantCastMod = (barrageAtHighestCount == highestAtCount) ? -2000 : -900;
                    int merchantOrderMod = (barrageAtHighestCount == highestAtCount) ? 9000 : 6200;

                    bool coinThenMerchant = TrySetCoinThenCardComboWithoutResim(p, board, merchant, needCoinForMerchant, "过期货物专卖商概率放行");
                    if (!coinThenMerchant)
                    {
                        try
                        {
                            if (p.ComboModifier == null)
                            {
                                p.ComboModifier = new ComboSet(merchant.Id);
                            }
                        }
                        catch { }
                    }

                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(merchantCastMod));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(merchantOrderMod));
                    p.ForceResimulation = true;
                    AddLog("[过期货物专卖商概率放行] 郊狼当前不可压费，商贩弃弹幕概率="
                        + barrageAtHighestCount + "/" + highestAtCount
                        + " >= 1/2 => 允许赌商贩(" + merchantOrderMod + "/" + merchantCastMod + ")"
                        + (needCoinForMerchant ? "，优先级硬币->商贩" : ""));
                    return;
                }

                // 非理想情况：有其它动作就先做其它动作，延后专卖商
                if (hasOtherActionsNow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(350));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-900));
                    AddLog("[过期货物专卖商-延后] 当前最高费非仅灵魂弹幕，且有其它动作可分流=" + (canTapNowLocal ? "Y" : "N")
                        + ",其它可打=" + (hasOtherPlayableCardNow ? "Y" : "N") + ") => 延后使用");
                    return;
                }

                // 兜底：无其它?时才考虑使用；仍尽量保证最高费?被弃组件，避免随机弃错??
                if (!highestAllAllowed)
                {
                    // 无其它动作但不安全：不强推，?擎自行权?等价于?滃疄鍦?办法才用”）
                    return;
                }

                // 无其它动作且最高费均为被弃组件（但不是仅弹幕）：允许兜底使?
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-150));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(2500));
                AddLog("[过期货物专卖商-兜底] 无其它动作，最高费为被弃组件费用" + maxCost + ")但非仅弹幕=> 允许兜底使用(2500/-150)");
                return;
            }
            catch { }
        }

        /// <summary>
        /// 处理恐?海盗的?鏈夐?昏緫
        /// </summary>
        private void ProcessCard_CORE_NEW1_022_TerrrorPirate(ProfileParameters p, Board board, 
            bool hasClawEquipped, bool hasDiscardComponentAtMax, bool lethalThisTurn, bool canTapNow)
        {
            // ==============================================
            // Card.Cards.CORE_NEW1_022 - 鎭愭?海盗（随从?
            // 说明?费时?优先出，4费时大幅后置
            // ==============================================

            var pirates = GetDreadCorsairCardsInHand(board);
            if (pirates.Count == 0) return;

            var zeroCostPirates = pirates.Where(p => p.CurrentCost == 0).ToList();
            var fourCostPirates = pirates.Where(p => p.CurrentCost == 4).ToList();

            try
            {
                bool allInDoubleSoulfire = ShouldAllInDoubleSoulfireLethalGamble(board, lethalThisTurn, out var gambleReason);
                bool allInSingleSoulfire = ShouldAllInSingleSoulfireDiscardLethalGamble(board, lethalThisTurn, out var singleGambleReason);
                if (allInDoubleSoulfire || allInSingleSoulfire)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-9999)); } catch { }
                    foreach (var pirate in pirates)
                    {
                        if (pirate == null || pirate.Template == null) continue;
                        try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(9999)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-9999)); } catch { }
                    }

                    AddLog("[恐怖海盗-魂火赌博后置] " + (allInDoubleSoulfire
                            ? (gambleReason ?? "命中双魂火斩杀赌博窗口")
                            : (singleGambleReason ?? "命中单魂火弃牌斩杀窗口"))
                        + " => 暂禁恐怖海盗(9999/-9999)");
                    return;
                }
            }
            catch { }

            // 新口径：恐怖海盗通常留给时空之爪联动。
            // 仅在“手里有刀”或“已装备刀”时后置；无刀联动时允许4费正常拍下。
            try
            {
                bool hasClawInHand = board.Hand != null
                    && board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.END_016);
                bool hasClawEquippedNow = hasClawEquipped
                    || (board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0);

                // 新口径：无刀联动时，若本回合可分流且手里弃牌收益密度高，则4费恐怖海盗必须让位分流。
                // 目的：避免“先拍4费海盗”错过分流后再找启动件（如第二张魂火/刀/地标）的窗口。
                int soulBarrageCount = 0;
                try
                {
                    soulBarrageCount = board.Hand != null
                        ? board.Hand.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534)
                        : 0;
                }
                catch { soulBarrageCount = 0; }

                bool canTapNowRobust = canTapNow;
                try { canTapNowRobust = canTapNow || CanUseLifeTapNow(board); }
                catch { canTapNowRobust = canTapNow; }

                bool shouldYieldToLifeTap = !lethalThisTurn
                    && canTapNowRobust
                    && fourCostPirates.Count > 0
                    && zeroCostPirates.Count == 0
                    && !hasClawInHand
                    && !hasClawEquippedNow
                    && (hasDiscardComponentAtMax || soulBarrageCount >= 2);

                if (shouldYieldToLifeTap)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-9999)); } catch { }
                    foreach (var pirate in pirates)
                    {
                        if (pirate == null || pirate.Template == null) continue;
                        try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(9999)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-9999)); } catch { }
                    }

                    AddLog("[恐怖海盗-让位分流] 无刀联动且可分流(弃牌收益密度=" + (hasDiscardComponentAtMax ? "Y" : "N")
                        + ",弹幕x" + soulBarrageCount + ") => 暂禁4费恐怖海盗(9999/-9999)，优先抽一口");
                    return;
                }

                if (hasClawInHand || hasClawEquippedNow)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-9999)); } catch { }
                    foreach (var pirate in pirates)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(9999)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-9999)); } catch { }
                    }

                    string reason = hasClawInHand ? "手里有时空之爪" : "已装备时空之爪";
                    AddLog("[恐怖海盗-联动后置] " + reason + " => 暂缓直接打恐怖海盗(9999/-9999)");
                    return;
                }
            }
            catch { }

            // 【优先级0 - 连段前置】若可先上宝藏经销商且还能?费海盗，优先 经销?-> 海盗?
            // 鍙ｅ緞锛氱粡閿?商先落地可让随后下的0费恐怖海盗吃到增益，避免直接拍海盗损失收益??
            try
            {
                if (zeroCostPirates.Count > 0)
                {
                    var toyDealer = board.Hand.FirstOrDefault(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.TOY_518
                        && h.CurrentCost <= board.ManaAvailable);
                    int usedSlots = GetFriendlyBoardSlotsUsed(board);
                    bool hasTwoSlots = usedSlots <= 5;

                    if (toyDealer != null && hasTwoSlots)
                    {
                        var pirate = zeroCostPirates.First();
                        try { p.ComboModifier = new ComboSet(toyDealer.Id, pirate.Id); } catch { }
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-1500)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(9800)); } catch { }
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-900)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9400)); } catch { }
                        try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(-900)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(9400)); } catch { }
                        AddLog("[恐怖海盗连段前置] 可先上宝藏经销商且场位足够 => 强制 经销商->0费海盗(ComboSet)");
                        return;
                    }
                }
            }
            catch { }

            // 【优先级1 - 鏈?楂樸??费海盗：?无嚭
            if (zeroCostPirates.Count > 0 && GetFriendlyBoardSlotsUsed(board) <= 6)
            {
                var pirate = zeroCostPirates.First();
                p.ComboModifier = new ComboSet(pirate.Id);
                AddLog("[恐怖海盗优先级] 0费海盗 => 必须出(ComboSet)");
                return;
            }

            // 【优先级2】4费海盗：无刀联动时允许正常落地（可前置抢节奏）
            else if (fourCostPirates.Count > 0 && zeroCostPirates.Count == 0)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-1200));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9000));
                foreach (var pirate in fourCostPirates)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(-1200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(9000)); } catch { }
                }
                AddLog("[恐怖海盗优先级] 4费海盗且无刀联动 => 允许前置使用(9000/-1200)");
                return;
            }

            // 【优先级3 - 默认】其他情?
            else
            {
								p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(250));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-350));
                foreach (var pirate in pirates)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(250)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-350)); } catch { }
                }
                AddLog("[恐怖海盗默认] 无特殊条件");
            }
        }

        /// <summary>
        /// 处理列车机务?WW_044)鍦??手上无被弃组件”时的?死优先级
        /// </summary>
        private void ProcessCard_WW_044_TramMechanic(ProfileParameters p, Board board,
            int handDiscardPayoffCount, bool lethalThisTurn)
        {
            if (board == null || board.MinionFriend == null) return;
            if (lethalThisTurn) return;

            // 鍙ｅ：仅当?手上没有被弃组件=时，才主动把机?拿去?，尽快转成淤?《资源?
            if (handDiscardPayoffCount > 0) return;

            try
            {
                var tramOnBoard = board.MinionFriend.Where(m => m != null
                    && m.Template != null
                    && m.Template.Id == Card.Cards.WW_044).ToList();
                if (tramOnBoard.Count == 0) return;

                bool tramCanAttack = tramOnBoard.Any(m => m.CanAttack);
                bool enemyHasMinions = board.MinionEnemy != null && board.MinionEnemy.Any(e => e != null);
                if (!tramCanAttack || !enemyHasMinions) return;

                p.AttackOrderModifiers.AddOrUpdate(Card.Cards.WW_044, new Modifier(9999));
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_044, new Modifier(-650));
                AddLog("[列车机务工-送死] 手牌无被弃组件(0) => 强推机务工攻击送死(攻序9999,价值+650)");
            }
            catch { }
        }

        #endregion

        #region 主方?GetParameters
        
        public ProfileParameters GetParameters(Board board)
        {
            // [强制] 每轮?始前重置投降?关，防止上轮残留导致引擎?窇
            try { Bot.SetConcedeWhenLethal(false); } catch { }
            
            // 初始?
            var p = new ProfileParameters(BaseProfile.Rush);
            p.DiscoverSimulationValueThresholdPercent = -10;
            
            // 确保 ForcedResimulationCardList 不为 null
            if (p.ForcedResimulationCardList == null)
                p.ForcedResimulationCardList = new List<Card.Cards>();
            
            if (board == null)
                return p;

            _log = "";
            try { ResetCompactLogContext(board); } catch { }

            // ==============================================
            // 投降?：敌方护?> 100 直接投降
            // ==============================================
            try
            {
                if (board.HeroEnemy != null && board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor> 100)
                {
                    AddLog("[投降] 敌方护甲=" + board.HeroEnemy.CurrentArmor + " > 100 => 直接投降");
                    try { Bot.Concede(); } catch { }
                    return p;
                }
            }
            catch { }

            // 更新“临时牌实体ID”追踪（?墓发?速写等临时牌的可靠识别）
            try { UpdateTemporaryHandTracking(board); } catch { }
            // 开局记录牌库来源：弃牌逻辑默认忽略非牌库来源手牌（敌方塞牌/外来牌）。
            try { UpdateOpeningDeckSnapshot(board); } catch { }
            
            // ==============================================
            // 全局策略基线 - 后续会按对局态势动态修正
            // ==============================================
            double aggroBase = 100.0;
            if (board.EnemyClass == Card.CClass.WARRIOR || board.EnemyClass == Card.CClass.PRIEST)
            {
                aggroBase += 50;
            }
            else if (board.EnemyClass == Card.CClass.WARLOCK || board.EnemyClass == Card.CClass.HUNTER)
            {
                aggroBase -= 50;
            }
            p.GlobalAggroModifier = new Modifier((int)aggroBase);

            // 濂??鎶?：敌方有?且我方下回合面临致死则转?満
            bool secretEmergencyMode = false;
            try
            {
                bool enemyHasSecret = board.SecretEnemyCount > 0 || board.SecretEnemy;
                if (enemyHasSecret)
                {                    int enemyHpWithArmor = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                    int myHp = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                    int enemyAtk = 0;
                    if (board.MinionEnemy != null)
                    {
                        enemyAtk = board.MinionEnemy.Sum(x => x != null ? x.CurrentAtk : 0);
                    }
                    if (board.WeaponEnemy != null)
                    {
                        enemyAtk += board.WeaponEnemy.CurrentAtk;
                    }
                    
                    // 鍒?下回合是?能直?打死
                    if (enemyAtk >= myHp)
                    {
                        secretEmergencyMode = true;
                        p.GlobalAggroModifier.Value = 50;  // 显著降低攻击?
                        p.GlobalDefenseModifier.Value = 900; // 显著提高防御
	                        AddLog("[奥秘护栏触发] 敌方有奥秘且我方下回合面临致死场攻" + enemyAtk + " >= 血量" + myHp + ") => 强转控场模式");
                    }
                }
            }
            catch { }

            // ==============================================
            // 鍏?变量初始?
            // ==============================================
            bool hasDiscardComponentAtMax = false;
            bool hasDiscardComponentAtMaxForWeapon = false;
            bool canTapNow = false;
            bool hasNoDrawLeft = false;
            bool lethalThisTurn = false;
            int enemyHp = 0;
            int maxCostInHand = 0;
            int payoffCount = 0;
            int handCount = board.Hand?.Count ?? 0;
            bool has0CostMinions = false;
            bool hasPhotonInHand = false;
            bool hasOtherActions = false;
            int discardPayoffCount = 0;
            int handDiscardPayoffCount = 0;
            bool hasOtherRealActions = false;
            bool hasOtherRealActionsExcludingClaw = false;
            bool hasOtherRealActionsExcludingClawAndCave = false;
            bool canAttackFace = false;
            bool felwingNotDiscountedYet = false;
            int felwingMinCost = 99;
            bool isTemporaryPhoton = false;
            int lowHealthEnemyCount = 0;
            bool hasClawEquipped = false;
            bool canClickCave = false;
            bool canPlayCaveFromHand = false;
            bool hasMerchantOnBoard = false;
            bool merchantCanSacrifice = false;
            bool hasCoin = false;
            int effectiveMana = 0;
            bool heroPowerUsedThisTurn = false;
            bool holdClawForMerchantBarrage = false;
            HashSet<Card.Cards> discardComponents = null;
            bool merchantHasDeathrattleCard = false;
            Card.Cards merchantDeathrattleCardId = default(Card.Cards);
            bool lethalLikelyNextTurn = false;
            bool survivalStabilizeMode = false;
            int myHpArmorForStabilize = 0;
            int enemyPotentialNextTurnDamage = 0;
            bool avoidLifeTapForDiscardCounterplay = false;
            bool forceLifeTapLastChance = false;
            bool keepForceResimulationForAttackSequencing = false;
            bool keepForceResimulationForDiscountPlan = false;
            bool disableResimForEarlyCoin = false;
            int enemyMinionCountForMode = 0;
            int friendlyMinionCountForMode = 0;
            int enemyBoardAttackForMode = 0;
            int friendlyBoardAttackForMode = 0;
            int handDamagePotentialForMode = 0;

            // ==============================================
            // 全局计算逻辑
            // ==============================================
            try
            {
                // 璁＄敌方?閲?
                enemyHp = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;

                // 计算斩杀
                lethalThisTurn = BoardHelper.IsLethalPossibleThisTurn(board);
                this.lethalThisTurn = lethalThisTurn;

                // 璁＄被弃组件
                discardComponents = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                if (handForDiscardLogic.Count > 0)
                {
                    maxCostInHand = handForDiscardLogic.Max(h => h.CurrentCost);
                    hasDiscardComponentAtMax = handForDiscardLogic.Any(h => h.CurrentCost == maxCostInHand
                        && discardComponents.Contains(h.Template.Id));
                    var discardComponentsForWeapon = new HashSet<Card.Cards>(discardComponents
                        .Where(id => id != Card.Cards.WON_103));
                    hasDiscardComponentAtMaxForWeapon = handForDiscardLogic.Any(h => h != null
                        && h.Template != null
                        && h.CurrentCost == maxCostInHand
                        && discardComponentsForWeapon.Contains(h.Template.Id));
                    payoffCount = handForDiscardLogic.Count(h => discardComponents.Contains(h.Template.Id));
                    handDiscardPayoffCount = payoffCount;
                    discardPayoffCount = payoffCount;
                }

                // 妫?鏌?备状?
                hasClawEquipped = board.WeaponFriend != null && board.WeaponFriend.Template != null 
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016;

                // 硬币与有效费用（?“跳币下地标/下牌”类?柇锛?
                try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }
                bool hasSketchInHandForCoinBlock = false;
                bool hasTombInHandForCoinBlock = false;
                try { hasSketchInHandForCoinBlock = board.HasCardInHand(Card.Cards.TOY_916); } catch { hasSketchInHandForCoinBlock = false; }
                try { hasTombInHandForCoinBlock = board.HasCardInHand(Card.Cards.TLC_451); } catch { hasTombInHandForCoinBlock = false; }

                bool blockCoinAtTurn1ForSketchOrTomb = hasCoin
                    && board.MaxMana == 1
                    && (hasSketchInHandForCoinBlock || hasTombInHandForCoinBlock);

                if (blockCoinAtTurn1ForSketchOrTomb)
                {
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-9999)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (c.Template.Id != Card.Cards.GAME_005) continue;
                                try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                            }
                        }
                    }
                    catch { }

                    AddLog("[全局硬规则-硬币] MaxMana=1 且手里有速写美术家/咒怨之墓 => 禁止使用硬币(9999/-9999)");
                }

                bool coinUsableForEffectiveMana = hasCoin && !blockCoinAtTurn1ForSketchOrTomb;
                effectiveMana = board.ManaAvailable + (coinUsableForEffectiveMana ? 1 : 0);
                disableResimForEarlyCoin = board.MaxMana < 3 && coinUsableForEffectiveMana;
                if (disableResimForEarlyCoin)
                {
                    AddLog("[全局硬规则-硬币] MaxMana=" + board.MaxMana + " 且手里有硬币 => 前期交币不思考");
                }

                // 妫?鏌?费随?
                has0CostMinions = board.Hand != null && board.Hand.Any(h => h != null 
                    && h.Template != null && h.CurrentCost == 0 && h.Type == Card.CType.MINION);

                // 妫?鏌?光子弹幕
                hasPhotonInHand = board.HasCardInHand(Card.Cards.TIME_027);
                if (hasPhotonInHand && board.Hand != null)
                {
                    var photon = board.Hand.FirstOrDefault(h => h != null && h.Template != null 
                        && h.Template.Id == Card.Cards.TIME_027);
                    if (photon != null)
                    {
                        isTemporaryPhoton = IsTemporaryCard(board, photon);
                    }
                }

                // 璁＄敌方低血量随?
                if (board.MinionEnemy != null)
                {
                    lowHealthEnemyCount = board.MinionEnemy.Count(m => m != null && m.CurrentHealth <= 3);
                }

                // 妫?鏌?彲鍚?蛋鑴?
                bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                bool hasAttacker = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.CanAttack);
                canAttackFace = hasAttacker && !enemyHasTaunt;

                // 妫?鏌?翼蝠减费状??
                if (board.Hand != null)
                {
                    var felwings = board.Hand.Where(h => h != null && h.Template != null 
                        && h.Template.Id == Card.Cards.YOD_032).ToList();
                    if (felwings.Count > 0)
                    {
                        felwingMinCost = felwings.Min(f => f.CurrentCost);
                        felwingNotDiscountedYet = felwings.Any(f => f.CurrentCost >= f.Template.Cost);
                    }
                }

                // 妫?鏌?卖商?満
                hasMerchantOnBoard = board.MinionFriend != null && board.MinionFriend.Any(m => m != null 
                    && m.Template != null && m.Template.Id == Card.Cards.ULD_163);
                
                // 妫?鏌?卖商是否可以送死（可攻击 + 敌方?攻以上随从）
                if (hasMerchantOnBoard)
                {
                    bool merchantCanAttack = false;
                    bool enemyHasAttackableMinions = board.MinionEnemy != null && board.MinionEnemy.Any(e => e != null && e.CurrentAtk >= 1);

                    bool drIsSoulBarrage = false;
                    bool drIsGuldanHand = false;
                    bool hasDrInfo = false;

                    try
                    {
                        foreach (var m in board.MinionFriend.ToArray())
                        {
                            if (m == null || m.Template == null) continue;
                            if (m.Template.Id != Card.Cards.ULD_163) continue;
                            if (m.CanAttack) merchantCanAttack = true;

                            try
                            {
                                var ench = m.Enchantments != null
                                    ? m.Enchantments.FirstOrDefault(x => x != null
                                        && x.EnchantCard != null && x.EnchantCard.Template != null
                                        && x.EnchantCard.Template.Id == Card.Cards.ETC_424)
                                    : null;
                                if (ench != null && ench.DeathrattleCard != null && ench.DeathrattleCard.Template != null)
                                {
                                    hasDrInfo = true;
                                    var drId = ench.DeathrattleCard.Template.Id;
                                    merchantHasDeathrattleCard = true;
                                    merchantDeathrattleCardId = drId;
                                    if (drId == Card.Cards.RLK_534) drIsSoulBarrage = true;
                                    if (drId == Card.Cards.BT_300) drIsGuldanHand = true;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    bool hasSoulBarrageInHand = false;
                    bool hasStarterComponents = false;
                    try
                    {
                        hasSoulBarrageInHand = board.HasCardInHand(Card.Cards.RLK_534);
                        var discardSetForStarter = discardComponents != null && discardComponents.Count > 0
                            ? new HashSet<Card.Cards>(discardComponents)
                            : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                        bool hasCoinStarter = false;
                        try { hasCoinStarter = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinStarter = false; }
                        int effectiveManaStarter = board.ManaAvailable + (hasCoinStarter ? 1 : 0);

                        bool canPlaySoulfire = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null
                            && h.Template.Id == Card.Cards.EX1_308 && h.CurrentCost <= board.ManaAvailable);
                        bool soulfireStarterByAllDiscard = false;
                        try
                        {
                            int tmpDiscard = 0, tmpTotal = 0;
                            soulfireStarterByAllDiscard = IsNonSoulfireAllDiscardComponents(board, discardSetForStarter, out tmpDiscard, out tmpTotal);
                        }
                        catch { soulfireStarterByAllDiscard = false; }
                        bool canPlaySoulfireStarter = canPlaySoulfire && soulfireStarterByAllDiscard;

                        bool clawCanAttackNow = false;
                        try
                        {
                            clawCanAttackNow = board.WeaponFriend != null && board.WeaponFriend.Template != null
                                && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                && board.HeroFriend != null && board.HeroFriend.CanAttack;
                        }
                        catch { clawCanAttackNow = false; }
                        bool highestDiscardRatioHalfOrMore = false;
                        try
                        {
                            int tmpHighestDiscard = 0, tmpHighestTotal = 0;
                            highestDiscardRatioHalfOrMore = IsHighestCostDiscardRatioAtLeastHalf(board, discardSetForStarter, out tmpHighestDiscard, out tmpHighestTotal);
                        }
                        catch { highestDiscardRatioHalfOrMore = false; }
                        bool hasClawInHandAndReady = false;
                        try
                        {
                            hasClawInHandAndReady = board.Hand != null && effectiveManaStarter >= 4
                                && board.Hand.Any(c => c != null && c.Template != null
                                    && c.Template.Id == Card.Cards.END_016
                                    && c.CurrentCost <= effectiveManaStarter);
                        }
                        catch { hasClawInHandAndReady = false; }
                        bool clawStarterByHighestHalf = highestDiscardRatioHalfOrMore && (clawCanAttackNow || hasClawInHandAndReady);

                        bool canClickCaveNow = false;
                        try
                        {
                            var clickableCaveOnBoardForStarter = board.MinionFriend?.FirstOrDefault(x => x != null && x.Template != null
                                && x.Template.Id == Card.Cards.WON_103 && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
                            canClickCaveNow = clickableCaveOnBoardForStarter != null;
                        }
                        catch { canClickCaveNow = false; }

                        bool canPlayCaveFromHandForStarter = false;
                        try
                        {
                            var caveInHand = board.Hand != null
                                ? board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103)
                                : null;
                            canPlayCaveFromHandForStarter = caveInHand != null
                                && handCount <= 8
                                && effectiveManaStarter >= 3
                                && caveInHand.CurrentCost <= effectiveManaStarter
                                && GetFriendlyBoardSlotsUsed(board) < 7;
                        }
                        catch { canPlayCaveFromHandForStarter = false; }

                        bool merchantStarterNow = false;
                        try
                        {
                            bool hasSpace = GetFriendlyBoardSlotsUsed(board) < 7;
                            var merchant = board.Hand != null ? board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163) : null;
                            bool merchantPlayableNow = merchant != null && hasSpace && merchant.CurrentCost <= effectiveManaStarter;
                            merchantStarterNow = merchantPlayableNow && highestDiscardRatioHalfOrMore;
                        }
                        catch { merchantStarterNow = false; }

                        hasStarterComponents = canPlaySoulfireStarter || clawStarterByHighestHalf || canClickCaveNow || canPlayCaveFromHandForStarter || merchantStarterNow;
                    }
                    catch { }

                    bool shouldDelaySacrificeBecauseGuldanHand = (hasDrInfo && !drIsSoulBarrage && drIsGuldanHand
                        && hasSoulBarrageInHand && hasStarterComponents && handCount > 4);

                    // 硬限制：手牌>8 鏃堕?死可能?（亡语给2张）=> 不视为?可送死?
                    merchantCanSacrifice = merchantCanAttack
                        && enemyHasAttackableMinions
                        && !shouldDelaySacrificeBecauseGuldanHand
                        && handCount <= 8;

                    // 闇?求：如果场上有可??掉的专卖商，且其亡语里携带被弃组件，则也视为?手上有被弃组件”??
                    try
                    {
                        if (merchantCanSacrifice
                            && merchantHasDeathrattleCard
                            && discardComponents != null
                            && discardComponents.Contains(merchantDeathrattleCardId))
                        {
                            bool drAlreadyInHand = false;
                            try
                            {
                                drAlreadyInHand = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == merchantDeathrattleCardId);
                            }
                            catch { drAlreadyInHand = false; }

                            if (!drAlreadyInHand)
                            {
                                payoffCount += 1;
                                discardPayoffCount += 1;
                            }
                        }
                    }
                    catch { }
                }
                
                // 妫?鏌?槸鍚?其他实际可用?綔
                hasOtherRealActions = false;
                try
                {
                    // 是否可分流：
                    // 优先按 Ability.EXHAUSTED 判断；若 Ability 状态缺失（部分时序会出现 null），
                    // 在法力>=2时保守视为“可尝试分流”，避免出现“先拍空降歹徒再分流”的错序。
                    bool abilityStateKnown = board.Ability != null;
                    bool usedByTag = abilityStateKnown && GetTag(board.Ability, Card.GAME_TAG.EXHAUSTED) == 1;

                    canTapNow = CanUseLifeTapNow(board);

                    // 本回合是否已经用过英雄技能（用于“弃牌组件放行”口径）
                    heroPowerUsedThisTurn = usedByTag;

                    // 妫?鏌?槸鍚?可用过牌/血源动?
                    bool hasDrawCards = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null
                        && (h.Template.Id == Card.Cards.LOOT_014
                            || h.Template.Id == Card.Cards.TLC_603
                            || h.Template.Id == Card.Cards.TOY_916
                            || h.Template.Id == Card.Cards.TLC_451)
                        && h.CurrentCost <= effectiveMana);

                    // 妫?鏌?槸鍚?可打?
                    // 娉?：这里用“除时空之爪?的可打牌”来?是否还有其他?綔锛?
                    // 鍚?会导致?只有爪子能打?时也被误判为?有其他?綔鈥欙紝浠庤?永远禁?彁鍒?例外逻辑?
                    bool hasPlayableCardsOtherThanClaw = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null
                        && h.CurrentCost <= effectiveMana
                        && (h.Template.Type == Card.CType.MINION || h.Template.Type == Card.CType.SPELL
                            || h.Template.Type == Card.CType.WEAPON)
                        && h.Template.Id != Card.Cards.END_016
                        && h.Template.Id != Card.Cards.GAME_005);

                    // “给地标自己留口子?：当只有地标能出时，也应允许拍地标下去
                    bool hasPlayableCardsOtherThanClawOrCave = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null
                        && h.CurrentCost <= effectiveMana
                        && (h.Template.Type == Card.CType.MINION || h.Template.Type == Card.CType.SPELL
                            || h.Template.Type == Card.CType.WEAPON)
                        && h.Template.Id != Card.Cards.END_016
                        && h.Template.Id != Card.Cards.WON_103
                        && h.Template.Id != Card.Cards.GAME_005);

                    hasOtherRealActionsExcludingClaw = canTapNow || hasDrawCards || hasPlayableCardsOtherThanClaw;
                    hasOtherRealActionsExcludingClawAndCave = canTapNow || hasDrawCards || hasPlayableCardsOtherThanClawOrCave;
                    hasOtherRealActions = hasOtherRealActionsExcludingClaw;
                    hasNoDrawLeft = !canTapNow && !hasDrawCards;
                }
                catch { }

                // 妫?鏌?湴鏍?
                var clickableCaveOnBoard = board.MinionFriend?.FirstOrDefault(x => x != null && x.Template != null 
                    && x.Template.Id == Card.Cards.WON_103 && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
                canClickCave = clickableCaveOnBoard != null;

                // 手牌是否可下窟穴（包含?跳币?濓級
                try
                {
                    var caveInHand = board.Hand != null ? board.Hand.FirstOrDefault(c => c != null && c.Template != null
                        && c.Template.Id == Card.Cards.WON_103) : null;
                    canPlayCaveFromHand = caveInHand != null && caveInHand.CurrentCost <= effectiveMana;
                }
                catch { canPlayCaveFromHand = false; }

                // “无过牌手段”口径?全：地标（场上可?手里可拍）也视为过牌?簮
                hasNoDrawLeft = hasNoDrawLeft && !canClickCave && !canPlayCaveFromHand;

                // 对局动态模式：按敌我场面、敌我血量、手牌伤害实时修正攻防倾向
                try
                {
                    myHpArmorForStabilize = board.HeroFriend != null
                        ? board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor
                        : 0;
                    enemyPotentialNextTurnDamage = EstimateEnemyPotentialDamageNextTurn(board);
                    lethalLikelyNextTurn = IsLikelyLethalNextTurn(board);

                    enemyMinionCountForMode = board.MinionEnemy != null ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0) : 0;
                    friendlyMinionCountForMode = board.MinionFriend != null ? board.MinionFriend.Count(m => m != null && m.CurrentHealth > 0) : 0;
                    enemyBoardAttackForMode = board.MinionEnemy != null ? board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk)) : 0;
                    friendlyBoardAttackForMode = board.MinionFriend != null
                        ? board.MinionFriend.Where(m => m != null && m.CanAttack && !m.IsFrozen).Sum(m => Math.Max(0, m.CurrentAtk))
                        : 0;
                    handDamagePotentialForMode = EstimateHandFaceDamagePotentialThisTurn(board);

                    ApplyDynamicCombatMode(
                        p,
                        board,
                        lethalThisTurn,
                        enemyHp,
                        myHpArmorForStabilize,
                        enemyMinionCountForMode,
                        friendlyMinionCountForMode,
                        enemyBoardAttackForMode,
                        friendlyBoardAttackForMode,
                        handDamagePotentialForMode,
                        enemyPotentialNextTurnDamage,
                        secretEmergencyMode);

                    bool hpUnhealthy = myHpArmorForStabilize > 0 && myHpArmorForStabilize <= 14;
                    bool underPressure = enemyPotentialNextTurnDamage >= Math.Max(3, myHpArmorForStabilize - 2);

                    survivalStabilizeMode = !lethalThisTurn && lethalLikelyNextTurn && hpUnhealthy && underPressure;
                    if (survivalStabilizeMode)
                    {
                        try
                        {
                            if (p.GlobalAggroModifier != null)
                                p.GlobalAggroModifier.Value = Math.Min(p.GlobalAggroModifier.Value, 35);
                            else
                                p.GlobalAggroModifier = new Modifier(35);
                        }
                        catch { }

                        try
                        {
                            if (p.GlobalDefenseModifier != null)
                                p.GlobalDefenseModifier.Value = Math.Max(p.GlobalDefenseModifier.Value, 1200);
                            else
                                p.GlobalDefenseModifier = new Modifier(1200);
                        }
                        catch { }

                        AddLog("[保命护栏] 预计下回合可分流且血线危险(血量=" + myHpArmorForStabilize
                            + ",敌方潜在伤害=" + enemyPotentialNextTurnDamage
                            + ") => 降低进攻，优先级保命");
                    }
                }
                catch { }

                AddLog("全局策略：GlobalAggroModifier=" + (p.GlobalAggroModifier != null ? p.GlobalAggroModifier.Value.ToString() : "null"));

            // 高置??必死局”投降：敌方场攻??且即便按?乐观去?场仍明显超伤?
            try
            {
                int enemyMinionCount = board.MinionEnemy != null
                    ? board.MinionEnemy.Count(m => m != null)
                    : 0;

                bool friendlyTauntOnBoard = board.MinionFriend != null
                    && board.MinionFriend.Any(m => m != null && m.IsTaunt && m.CurrentHealth > 0);

                int optimisticRemovalCapacity = EstimateFriendlyImmediateRemovalCapacity(board);
                int enemyDamageAfterOptimisticRemoval = Math.Max(0, enemyPotentialNextTurnDamage - optimisticRemovalCapacity);

                bool overwhelmingBoard = enemyMinionCount >= 5;
                bool guaranteedDeadByGap = myHpArmorForStabilize > 0
                    && enemyDamageAfterOptimisticRemoval >= (myHpArmorForStabilize + 4);

                if (!lethalThisTurn && overwhelmingBoard && !friendlyTauntOnBoard && guaranteedDeadByGap)
                {
                    bool hasClawEquippedNow = false;
                    bool hasPlayableMerchantNow = false;
                    bool hasDiscardStarterNow = false;
                    try
                    {
                        hasClawEquippedNow = board.WeaponFriend != null
                            && board.WeaponFriend.Template != null
                            && board.WeaponFriend.Template.Id == Card.Cards.END_016
                            && board.WeaponFriend.CurrentDurability > 0;
                    }
                    catch { hasClawEquippedNow = false; }
                    try
                    {
                        hasPlayableMerchantNow = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                            && c.Template.Id == Card.Cards.ULD_163
                            && c.CurrentCost <= board.ManaAvailable);
                    }
                    catch { hasPlayableMerchantNow = false; }
                    try
                    {
                        hasDiscardStarterNow = hasDiscardComponentAtMax
                            && (hasClawEquippedNow || hasPlayableMerchantNow || canClickCave || canPlayCaveFromHand);
                    }
                    catch { hasDiscardStarterNow = false; }

                    if (hasDiscardStarterNow)
                    {
                        avoidLifeTapForDiscardCounterplay = true;
                        AddLog("[投降-必死] 敌方下回合潜在伤害" + enemyPotentialNextTurnDamage
                            + ",我方血量=" + myHpArmorForStabilize
                            + ",乐观解场能力=" + optimisticRemovalCapacity
                            + ",解后仍剩=" + enemyDamageAfterOptimisticRemoval
                            + ",敌方随从=" + enemyMinionCount
                            + " => 存在弃牌反打(爪子/商贩/窟穴)，本回合不分流，优先尝试反打");
                    }
                    else
                    {
                    bool canLifeTapForLastChance = false;
                    try
                    {
                        canLifeTapForLastChance = CanUseLifeTapNow(board) && handCount <= 9;
                    }
                    catch { canLifeTapForLastChance = false; }

                    if (canLifeTapForLastChance)
                    {
                        forceLifeTapLastChance = true;
                        AddLog("[投降-必死] 敌方下回合潜在伤害" + enemyPotentialNextTurnDamage
                            + ",我方血量=" + myHpArmorForStabilize
                            + ",乐观解场能力=" + optimisticRemovalCapacity
                            + ",解后仍剩=" + enemyDamageAfterOptimisticRemoval
                            + ",敌方随从=" + enemyMinionCount
                            + " => 高置信必死，但可分流(手牌=" + handCount + ")，先抽一口找翻盘");
                    }
                    else
                    {
                        AddLog("[投降-必死] 敌方下回合潜在伤害" + enemyPotentialNextTurnDamage
                            + ",我方血量=" + myHpArmorForStabilize
                            + ",乐观解场能力=" + optimisticRemovalCapacity
                            + ",解后仍剩=" + enemyDamageAfterOptimisticRemoval
                            + ",敌方随从=" + enemyMinionCount
                            + " => 高置信必死且无法分流，直接投降");
                        try { Bot.Concede(); } catch { }
                        return p;
                    }
                    }
                }
            }
            catch { }

            // 当场上有可用地标时：强制每次?后重算（“去思?冣?），避免错过点地标/点完再继续等情况
            if (canClickCave)
            {
                p.ForceResimulation = true;
                AddLog("[全局规则] 场上有可点地标 => 强制重算(ForceResimulation)");
                }

                AddLog("================ 颜射术狂野-重构版 v" + ProfileVersion + " ================");
                AddLog("回合信息: 法力=" + board.ManaAvailable + "/" + board.MaxMana + " | 手牌:" + handCount + " | 敌方HP:" + enemyHp);
                AddLog("状态 lethal=" + lethalThisTurn + " | 当前最高费有被弃组件=" + hasDiscardComponentAtMax + " | 挥刀判定最高费有被弃组件=" + hasDiscardComponentAtMaxForWeapon + " | 被弃组件=" + payoffCount);
                LogHandCards(board);
                LogDiscardComponentsInHand(board, discardComponents, merchantCanSacrifice);
            }
            catch (Exception ex)
            {
                AddLog("全局计算异常: " + ex.Message);
            }

            // ==============================================
            // 鍏?强制重算：使?任意手牌后都重新思??
            // 说明：?氳繃 ForcedResimulationCardList 实现“打出该类手牌后自动重算”??
            // 注意：默认排除硬币，避免打断显式 ComboSet(硬币->X) 的连续出牌；如需连?币也重算可以再加?
            // ==============================================
            try
            {
                if (board.Hand != null)
                {
                    try { p.ForcedResimulationCardList.Clear(); } catch { }

                    int addedCount = 0;
                    if (disableResimForEarlyCoin)
                    {
                        AddLog("[全局强制重算] 3费前且手里有硬币 => 清空重算列表，避免交币后思考");
                    }
                    else
                    {
                        foreach (var card in board.Hand)
                        {
                            if (card == null || card.Template == null) continue;
                            if (card.Template.Id == Card.Cards.GAME_005) continue; // 排除硬币

                            if (!p.ForcedResimulationCardList.Contains(card.Template.Id))
                            {
                                p.ForcedResimulationCardList.Add(card.Template.Id);
                                addedCount++;
                            }
                        }

                        // Fallback: keep WON_103 in forced-resim list so location use never stalls.
                        if (!p.ForcedResimulationCardList.Contains(Card.Cards.WON_103))
                        {
                            p.ForcedResimulationCardList.Add(Card.Cards.WON_103);
                        }
                    }

		                    AddLog("[全局强制重算] 启用：任意手牌（硬币除外）使用后强制重算，已登记" + addedCount + "类");
                }
            }
            catch (Exception ex)
            {
                AddLog("全局强制重算异常: " + ex.Message);
            }

            // ==============================================
            // 按卡牌分区处理所有?辑（每个卡牌只?个地方设置）
            // ==============================================
            
            try
            {
                // 处理恶魔之种（任务与终章衍生）
                ProcessCard_SW_091_Questline(p, board);

                // 处理过牌随从（栉龙?狗头人?
                ProcessCard_DrawMinions(p, board);

                // 处理异教低阶牧师（2费余量利用兜底）
                ProcessCard_CORE_SCH_713_Neophyte(p, board, lethalThisTurn);
                
                // 处理时空之爪
                ProcessCard_END_016_TimeWarpClaw(p, board, hasDiscardComponentAtMaxForWeapon, canTapNow, 
                    hasNoDrawLeft, lethalThisTurn, enemyHp, maxCostInHand, merchantCanSacrifice, hasOtherRealActionsExcludingClaw,
                    discardComponents);
                
                // 处理灵魂之火
                ProcessCard_EX1_308_Soulfire(p, board, hasDiscardComponentAtMax, payoffCount, 
                    handCount, hasOtherActions, has0CostMinions, hasPhotonInHand, board.MaxMana, lethalThisTurn);
                
                // 处理超光子弹幕
                ProcessCard_TIME_027_HyperBeam(p, board, isTemporaryPhoton, lowHealthEnemyCount, lethalThisTurn);

                // 超光子压费后的重算保护：当可打超光子且手里有可被压费的郊狼/邪翼蝠时，
                // 保留 ForceResimulation，避免“3费前硬币模式”收口把重算关闭，导致错过后续可打随从。
                try
                {
                    bool photonPlayableNow = board.Hand != null && board.Hand.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.TIME_027
                        && h.CurrentCost <= board.ManaAvailable);

                    bool hasDiscountTarget = board.Hand != null && board.Hand.Any(h => h != null
                        && h.Template != null
                        && (h.Template.Id == Card.Cards.TIME_047 || h.Template.Id == Card.Cards.YOD_032)
                        && h.CurrentCost > board.ManaAvailable);

                    if (!lethalThisTurn && photonPlayableNow && hasDiscountTarget)
                    {
                        keepForceResimulationForDiscountPlan = true;
                        AddLog("[全局规则] 超光子可打且有可被压费随从 => 保留 ForceResimulation");
                    }
                }
                catch { }

                // 处理掩息海星（默认禁用，仅命中沉默收益窗口放行）
                ProcessCard_TSC_926_Starfish(p, board, lethalThisTurn);
                
                // 处理灵魂弹幕
                ProcessCard_RLK_534_SoulBarrage(p, board, hasClawEquipped, hasDiscardComponentAtMax, 
                    canClickCave, canPlayCaveFromHand, lethalThisTurn);
                
                // 处理狂暴邪翼蝠
                ProcessCard_YOD_032_Felwing(p, board, canAttackFace, felwingNotDiscountedYet, felwingMinCost, lethalThisTurn);
                
                // 处理维希?的窟穴
                // 传入“排?标自身后的其他动作?，保证：即使无被弃组件，但当前℃其他?，也允许把地标拍下去
                ProcessCard_WON_103_Cave(p, board, discardPayoffCount, hasOtherRealActionsExcludingClawAndCave, handCount, hasMerchantOnBoard,
                    discardComponents);
                
                // 处理过期?专卖?
                ProcessCard_ULD_163_Merchant(p, board, hasMerchantOnBoard, handCount, discardComponents);

                // 处理列车机务?手上无被弃组件时优先送死?
                ProcessCard_WW_044_TramMechanic(p, board, handDiscardPayoffCount, lethalThisTurn);
                
                // 处理恐?栨捣鐩?
                ProcessCard_CORE_NEW1_022_TerrrorPirate(p, board, hasClawEquipped, hasDiscardComponentAtMax, lethalThisTurn, canTapNow);
                
                // 处理咒??箣澧?
                ProcessCard_TLC_451_CurseTomb(p, board);
                
                // 处理船载火炮
                ProcessCard_CORE_NEW1_023_ShipCannon(p, board);
                
                // 处理速写美术?
                ProcessCard_TOY_916_SketchArtist(p, board);
                
                // 处理宝藏经销?
                ProcessCard_TOY_518_ToyDealer(p, board);
                
                // 处理太?海盗
                ProcessCard_GDB_333_SpacePirate(p, board);
                
                // 处理乐器?甯?
                ProcessCard_ETC_418_InstrumentTech(p, board);
                
                // 处理郊狼
                ProcessCard_TIME_047_Coyote(p, board, canAttackFace, lethalThisTurn);
                
                // 处理空降歹徒
                ProcessCard_DRG_056_Parachute(p, board, canTapNow);
            }
            catch (Exception ex)
            {
                AddLog("卡牌处理异常: " + ex.Message);
            }

            // ==============================================
            // 全局临时牌优先级处理
            // ==============================================
            try
            {
                if (board.Hand != null)
                {
                    // 鍏?临时牌提权排?表：这些牌属于被弃组件关键收益牌，应继续由各自逻辑?埗
                    // 注：这里仅影响?滃叏灞?临时牌优先级处理”，不影响各＄自己的专属?昏緫
                    var temporaryPriorityExclusion = discardComponents != null
                        ? new HashSet<Card.Cards>(discardComponents)
                        : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                    temporaryPriorityExclusion.Add(Card.Cards.GAME_005);       // 幸运?
                    temporaryPriorityExclusion.Add(Card.Cards.YOD_032);        // 狂暴邪翼蝠
                    temporaryPriorityExclusion.Add(Card.Cards.TIME_047);       // 郊狼
                    temporaryPriorityExclusion.Add(Card.Cards.CORE_NEW1_022);  // 恐怖海盗
                    temporaryPriorityExclusion.Add(Card.Cards.TLC_451);         // 鍜掓??墓：必』由专属?昏緫鎺?（避免临时牌提权覆盖?费前禁用”等硬门槛）
                    temporaryPriorityExclusion.Add(Card.Cards.DRG_056);         // 空降歹徒：由专属逻辑处理与墓/分流互斥
                    temporaryPriorityExclusion.Add(Card.Cards.TSC_926);         // 掩息海星：由专属沉默窗口逻辑控制

                    // 场上可点窟穴且手牌有被弃组件时，不让“临时商贩”覆盖窟穴点击优先级。
                    bool suppressTempMerchantBoostForCaveClick = false;
                    try
                    {
                        bool hasClickableCaveNow = board.MinionFriend != null && board.MinionFriend.Any(x => x != null && x.Template != null
                            && x.Template.Id == Card.Cards.WON_103 && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
                        if (hasClickableCaveNow)
                        {
                            var discardSetForCaveTemp = discardComponents != null && discardComponents.Count > 0
                                ? discardComponents
                                : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                            var handForDiscardCaveTemp = GetHandCardsForDiscardLogic(board);
                            bool hasDiscardForCaveNow = handForDiscardCaveTemp != null && handForDiscardCaveTemp.Any(h => h != null
                                && h.Template != null
                                && h.Template.Id != Card.Cards.GAME_005
                                && h.Template.Id != Card.Cards.WON_103
                                && discardSetForCaveTemp.Contains(h.Template.Id));
                            suppressTempMerchantBoostForCaveClick = hasDiscardForCaveNow;
                        }
                    }
                    catch { suppressTempMerchantBoostForCaveClick = false; }

                    // 商贩硬保护：若“去掉商贩后”的最高费组不含被弃组件，
                    // 临时商贩不允许被全局临时牌提权顶到最前。
                    bool suppressTempMerchantBoostForNoDiscardAtHighest = false;
                    try
                    {
                        var merchantInHandTemp = board.Hand != null
                            ? board.Hand.FirstOrDefault(h => h != null && h.Template != null && h.Template.Id == Card.Cards.ULD_163)
                            : null;
                        if (merchantInHandTemp != null)
                        {
                            var handWithoutMerchantTemp = board.Hand
                                .Where(h => h != null && h.Template != null && h.Template.Id != Card.Cards.ULD_163)
                                .ToList();
                            if (handWithoutMerchantTemp.Count > 0)
                            {
                                var discardSetTemp = discardComponents != null && discardComponents.Count > 0
                                    ? discardComponents
                                    : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                                int maxCostTemp = handWithoutMerchantTemp.Max(h => h.CurrentCost);
                                var highestTemp = handWithoutMerchantTemp.Where(h => h.CurrentCost == maxCostTemp).ToList();
                                bool highestAllDiscardTemp = highestTemp.Count > 0
                                    && highestTemp.All(h => h != null && h.Template != null && discardSetTemp.Contains(h.Template.Id));
                                suppressTempMerchantBoostForNoDiscardAtHighest = !highestAllDiscardTemp;
                            }
                        }
                    }
                    catch { suppressTempMerchantBoostForNoDiscardAtHighest = false; }

                    // 收集?有临时牌实例ID锛堢粺涓?鍙ｅ緞锛歵ag/追踪/右侧兜底/shift附魔?
                    var temporaryCardIds = new HashSet<int>();
                    foreach (var c in board.Hand)
                    {
                        bool isTemp = false;
                        try { isTemp = (c != null && c.Template != null && IsTemporaryCard(board, c)); } catch { isTemp = false; }
                        if (isTemp)
                        {
                            temporaryCardIds.Add(c.Id);
                        }
                    }

                    // 如果有临时牌，提高其优先级
                    if (temporaryCardIds.Count > 0)
                    {
                        foreach (var c in board.Hand)
                        {
                            if (c != null && c.Template != null && temporaryCardIds.Contains(c.Id))
                            {
                                // 排除：被弃组件关键牌不做全?提权（避免干扰弃牌收益链与爆牌控制）
                                // 额外ｅ：临时的郊狼/邪翼蝠（含同名异ID变体）也不走?提权，必须走各自专属后置逻辑?
                                if (temporaryPriorityExclusion.Contains(c.Template.Id)
                                    || IsCoyoteCardVariant(c)
                                    || IsFelwingCardVariant(c))
                                {
                                    AddLog("[全局临时牌] " + c.Template.NameCN + " => 命中排除列表，跳过全局提权");
                                    continue;
                                }

                                if ((suppressTempMerchantBoostForCaveClick || suppressTempMerchantBoostForNoDiscardAtHighest)
                                    && c.Template.Id == Card.Cards.ULD_163)
                                {
                                    if (suppressTempMerchantBoostForCaveClick)
                                        AddLog("[全局临时牌] 过期货物专卖商(临时) => 场上窟穴可点且手里有被弃组件，跳过全局提权让位点地标");
                                    else
                                        AddLog("[全局临时牌] 过期货物专卖商(临时) => 去掉商贩后最高费组无被弃组件，跳过全局提权");
                                    continue;
                                }

                                // 临时牌：极高优先级
                                if (c.Type == Card.CType.MINION)
                                {
                                    p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(-9999));
                                    p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9500));
                                    AddLog("[全局临时牌] " + c.Template.NameCN + "(随从) => 极高优先级-999/9500)");
                                }
                                else if (c.Type == Card.CType.SPELL)
                                {
                                    p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-9999));
                                    p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9500));
                                    AddLog("[全局临时牌] " + c.Template.NameCN + "(法术) => 极高优先级-999/9500)");
                                }
                                else if (c.Type == Card.CType.WEAPON)
                                {
                                    p.CastWeaponsModifiers.AddOrUpdate(c.Id, new Modifier(-9999));
                                    p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9500));
                                    AddLog("[全局临时牌] " + c.Template.NameCN + "(武器) => 极高优先级-999/9500)");
                                }else{
																		// 其他类型（如果有的话），也统?处理
                                    p.LocationsModifiers.AddOrUpdate(c.Id, new Modifier(-9999));
																		p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9500));
																		AddLog("[全局临时牌] " + c.Template.NameCN + "(其他) => 极高优先级9500)");
																}
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("全局临时牌处理异常:  " + ex.Message);
            }

            // ==============================================
            // 全局硬规则：去掉商贩后，若最高费组无被弃组件 => 禁止使用商贩
            // 说明：放在“全局临时牌提权”之后，确保不会被后续提权反向覆盖。
            // ==============================================
            try
            {
                if (board.Hand != null && board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163))
                {
                    var handWithoutMerchantGlobal = board.Hand
                        .Where(h => h != null && h.Template != null && h.Template.Id != Card.Cards.ULD_163)
                        .ToList();
                    if (handWithoutMerchantGlobal.Count > 0)
                    {
                        var discardSetForMerchantGlobal = discardComponents != null && discardComponents.Count > 0
                            ? discardComponents
                            : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));

                        int maxCostMerchantGlobal = handWithoutMerchantGlobal.Max(h => h.CurrentCost);
                        var highestMerchantGlobal = handWithoutMerchantGlobal
                            .Where(h => h.CurrentCost == maxCostMerchantGlobal)
                            .ToList();

                        bool highestAllDiscardMerchantGlobal = highestMerchantGlobal.Count > 0
                            && highestMerchantGlobal.All(h => h != null && h.Template != null
                                && discardSetForMerchantGlobal.Contains(h.Template.Id));

                        bool canStabilizeMerchantGlobalByCoyote = false;
                        string coyoteMerchantGlobalReason = null;
                        try
                        {
                            bool hasCoinForGlobalMerchant = false;
                            try { hasCoinForGlobalMerchant = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinForGlobalMerchant = false; }
                            int effectiveManaForGlobalMerchant = board.ManaAvailable + (hasCoinForGlobalMerchant ? 1 : 0);
                            canStabilizeMerchantGlobalByCoyote = CanStabilizeMerchantBarrageAfterCoyoteFaceDiscount(
                                board,
                                effectiveManaForGlobalMerchant,
                                out coyoteMerchantGlobalReason);
                        }
                        catch
                        {
                            canStabilizeMerchantGlobalByCoyote = false;
                            coyoteMerchantGlobalReason = null;
                        }

                        if (!highestAllDiscardMerchantGlobal && !canStabilizeMerchantGlobalByCoyote)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-9999)); } catch { }

                            foreach (var m in board.Hand.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163))
                            {
                                try { p.CastMinionsModifiers.AddOrUpdate(m.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(m.Id, new Modifier(-9999)); } catch { }
                            }

                            string highestGlobalIds = string.Join(",", highestMerchantGlobal
                                .Where(h => h != null && h.Template != null)
                                .Select(h => h.Template.Id.ToString()));
                            AddLog("[全局硬规则商贩] 去掉商贩后最高费组无被弃组件(highest=[" + highestGlobalIds + "]) => 禁用商贩(9999/-9999)");
                        }
                        else if (!highestAllDiscardMerchantGlobal && canStabilizeMerchantGlobalByCoyote)
                        {
                            AddLog("[全局硬规则商贩-放行] "
                                + (string.IsNullOrEmpty(coyoteMerchantGlobalReason) ? "可走脸压费后稳定弃弹幕" : coyoteMerchantGlobalReason)
                                + " => 本轮不禁用商贩");
                        }
                    }
                }
            }
            catch { }

            // ==============================================
            // 攻击顺序逻辑 - 触发牌强制先攻击
            // ==============================================
            try
            {
                // 妫?测场上有可攻击随?+ 手里有触发牌(郊狼/邪翼?海盗) => 先攻击，?鍚疐orceResimulation
                bool hasAttackableMinions = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.CanAttack);
                
                if (hasAttackableMinions && board.Hand != null)
                {
                    bool hasFelwingInHand = board.Hand.Any(h => IsFelwingCardVariant(h));
                    bool hasCoyoteInHandAnyCost = board.Hand.Any(h => IsCoyoteCardVariant(h));
                    bool hasPirateInHand = board.Hand.Any(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.CORE_NEW1_022);
                    bool hasDiscountAttackTriggerInHand = hasFelwingInHand || hasCoyoteInHandAnyCost || hasPirateInHand;
                    bool hasBarrageTradePreserveTrigger = false;
                    int barrageTradeAttackerCount = 0;
                    int barrageTradeTargetCount = 0;
                    try
                    {
                        bool canPlayBarrageNowForTrade = board.Hand.Any(h => h != null
                            && h.Template != null
                            && h.Template.Id == Card.Cards.RLK_534
                            && h.CurrentCost <= board.ManaAvailable);
                        bool barrageHardDisabled = false;
                        try
                        {
                            Rule r = null;
                            try { r = p.CastSpellsModifiers != null && p.CastSpellsModifiers.RulesCardIds != null ? p.CastSpellsModifiers.RulesCardIds[Card.Cards.RLK_534] : null; } catch { r = null; }
                            if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) barrageHardDisabled = true;
                        }
                        catch { barrageHardDisabled = false; }

                        if (!lethalThisTurn
                            && canPlayBarrageNowForTrade
                            && !barrageHardDisabled
                            && board.MinionEnemy != null
                            && board.MinionEnemy.Any(e => e != null))
                        {
                            var tradeAttackerIds = new HashSet<int>();
                            var tradeTargetIds = new HashSet<int>();
                            if (board.MinionFriend != null)
                            {
                                foreach (var fm in board.MinionFriend.Where(m => m != null && m.CanAttack))
                                {
                                    var target = board.MinionEnemy.FirstOrDefault(em => em != null
                                        && Math.Max(0, fm.CurrentAtk) >= Math.Max(1, em.CurrentHealth)
                                        && fm.CurrentHealth > em.CurrentAtk);
                                    if (target == null) continue;
                                    tradeAttackerIds.Add(fm.Id);
                                    tradeTargetIds.Add(target.Id);
                                }
                            }

                            barrageTradeAttackerCount = tradeAttackerIds.Count;
                            barrageTradeTargetCount = tradeTargetIds.Count;
                            hasBarrageTradePreserveTrigger = barrageTradeAttackerCount > 0 && barrageTradeTargetCount > 0;
                        }
                    }
                    catch
                    {
                        hasBarrageTradePreserveTrigger = false;
                        barrageTradeAttackerCount = 0;
                        barrageTradeTargetCount = 0;
                    }
                    bool hasAttackResimTriggerInHand = hasDiscountAttackTriggerInHand || hasBarrageTradePreserveTrigger;
                    bool hasCoinForAttackResim = false;
                    try { hasCoinForAttackResim = board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.GAME_005); } catch { hasCoinForAttackResim = false; }
                    bool allowAttackResimAfterTurn3 = board.MaxMana >= 4; // 第三回合之后
                    bool allowEarlyAttackResimForFelwing = hasFelwingInHand && !hasCoinForAttackResim;
                    bool enableAttackSequencingResimulation = hasBarrageTradePreserveTrigger
                        || (hasDiscountAttackTriggerInHand && (allowAttackResimAfterTurn3 || allowEarlyAttackResimForFelwing));

                    // 妫?鏌?里是?湁0费触发牌
                    bool has0CostFelwing = board.Hand.Any(h => IsFelwingCardVariant(h) && h.CurrentCost == 0);
                    bool has0CostCoyote = board.Hand.Any(h => IsCoyoteCardVariant(h) && h.CurrentCost == 0);
                    bool has0CostPirate = board.Hand.Any(h => h != null && h.Template != null 
                        && h.Template.Id == Card.Cards.CORE_NEW1_022 && h.CurrentCost == 0);

                    bool hasTriggerCard = has0CostFelwing || has0CostCoyote || has0CostPirate;

                    if (hasTriggerCard && enableAttackSequencingResimulation && !hasBarrageTradePreserveTrigger)
                    {
                        // 强制?鍚疐orceResimulation，确保攻击后重算
                        p.ForceResimulation = true;
                        keepForceResimulationForAttackSequencing = true;

                        // 提高?有随从的攻击优先级
                        if (board.MinionFriend != null)
                        {
                            foreach (var minion in board.MinionFriend)
                            {
                                if (minion != null && minion.Template != null && minion.CanAttack)
                                {
                                    // 商贩在“可送死”窗口时，保留其专属高优先级，不被通用攻击顺序覆盖。
                                    if (merchantCanSacrifice && minion.Template.Id == Card.Cards.ULD_163)
                                    {
                                        continue;
                                    }

                                    // 特殊处理：?写美术家走脸且?费邪翼蝠 => 速写优先攻击
                                    if (minion.Template.Id == Card.Cards.TOY_916 && has0CostFelwing)
                                    {
                                        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(999));
                                        AddLog("[攻击顺序] 速写美术家优先攻击(999)，触发邪翼蝠减费");
                                    }
                                    else
                                    {
                                        // 其他随从也提高攻击优先级
                                        p.AttackOrderModifiers.AddOrUpdate(minion.Template.Id, new Modifier(350));
                                    }
                                }
                            }
                        }

                        // 瑙?牌在攻击后出（?过强制重算实现?
                        if (has0CostFelwing)
                        {
                            AddLog("[全局规则] 有可攻击随从+0费邪翼蝠 => 先攻击后出牌(ForceResimulation)");
                        }
                        if (has0CostCoyote)
                        {
                            AddLog("[全局规则] 有可攻击随从+0费郊狼=> 先攻击后出牌(ForceResimulation)");
                        }
                        if (has0CostPirate)
                        {
                            AddLog("[全局规则] 有可攻击随从+0费海盗=> 先攻击后出牌(ForceResimulation)");
                        }
                    }
                    else if (enableAttackSequencingResimulation)
                    {
                        if (hasBarrageTradePreserveTrigger)
                        {
                            p.ForceResimulation = true;
                            keepForceResimulationForAttackSequencing = true;

                            var barrageTradeAttackers = new List<Card>();
                            var barrageTradeTargets = new List<Card>();
                            try
                            {
                                if (board.MinionFriend != null && board.MinionEnemy != null)
                                {
                                    foreach (var fm in board.MinionFriend.Where(m => m != null && m.CanAttack))
                                    {
                                        var target = board.MinionEnemy.FirstOrDefault(em => em != null
                                            && Math.Max(0, fm.CurrentAtk) >= Math.Max(1, em.CurrentHealth)
                                            && fm.CurrentHealth > em.CurrentAtk);
                                        if (target == null) continue;
                                        barrageTradeAttackers.Add(fm);
                                        barrageTradeTargets.Add(target);
                                    }
                                }
                            }
                            catch { }

                            foreach (var fm in barrageTradeAttackers)
                            {
                                if (fm == null || fm.Template == null) continue;
                                try { p.AttackOrderModifiers.AddOrUpdate(fm.Template.Id, new Modifier(920)); } catch { }
                            }
                            foreach (var em in barrageTradeTargets)
                            {
                                if (em == null || em.Template == null) continue;
                                try { p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(em.Template.Id, new Modifier(8800)); } catch { }
                            }

                            SetSoulBarrageDirectPlayModifier(p, board, 700, -900);
                            AddLog("[攻击顺序-灵魂弹幕保伤] 存在不亏交换(" + barrageTradeAttackerCount + "攻手/" + barrageTradeTargetCount
                                + "目标)且灵魂弹幕可打 => 先随从交换再考虑灵魂弹幕，减少伤害浪费");
                        }
                        else
                        {
                        // 非0费郊狼压费窗口：
                        // 鑻??郊狼费?>= 灵魂弹幕费用”会干扰时空之爪稳定弃弹幕，则先?上随从走脸压费，再进行出牌??
                        var coyoteNeedDiscount = board.Hand
                            .Where(h => h != null && h.Template != null
                                && IsCoyoteCardVariant(h)
                                && h.CurrentCost > 0)
                            .OrderByDescending(h => h.CurrentCost)
                            .FirstOrDefault();

                        var felwingNeedDiscount = board.Hand
                            .Where(h => h != null && h.Template != null
                                && IsFelwingCardVariant(h)
                                && h.CurrentCost > 0)
                            .OrderByDescending(h => h.CurrentCost)
                            .FirstOrDefault();

                        var barragesInHand = board.Hand
                            .Where(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534)
                            .ToList();
                        int highestBarrageCost = barragesInHand.Count > 0 ? barragesInHand.Max(h => h.CurrentCost) : -1;

                        bool hasClawPlan = false;
                        try
                        {
                            bool clawInHand = board.Hand.Any(h => h != null
                                && h.Template != null
                                && h.Template.Id == Card.Cards.END_016
                                && !IsForeignInjectedCardForDiscardLogic(board, h));
                            bool clawEquipped = board.WeaponFriend != null
                                && board.WeaponFriend.Template != null
                                && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                && board.WeaponFriend.CurrentDurability > 0;
                            hasClawPlan = clawInHand || clawEquipped;
                        }
                        catch { hasClawPlan = false; }

                        bool coyoteBlocksStableDiscard = coyoteNeedDiscount != null
                            && highestBarrageCost >= 0
                            && coyoteNeedDiscount.CurrentCost >= highestBarrageCost;

                        bool hasFaceDamageAttacker = false;
                        try
                        {
                                hasFaceDamageAttacker = board.MinionFriend != null
                                && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
                        }
                        catch { hasFaceDamageAttacker = false; }

                        // 鑻?上窟穴可点且当前手里有被弃组件，优先点窟穴，不启??先攻击压费”??
                        bool preferCaveBeforeAttackForDiscard = false;
                        int caveDiscardCntForAttackGate = 0;
                        int caveTotalCntForAttackGate = 0;
                        try
                        {
                            bool hasCaveOnBoardNow = board.MinionFriend != null && board.MinionFriend.Any(c => c != null
                                && c.Template != null
                                && c.Template.Id == Card.Cards.WON_103);
                            bool canClickCaveNowByTag = hasCaveOnBoardNow && board.MinionFriend.Any(c => c != null
                                && c.Template != null
                                && c.Template.Id == Card.Cards.WON_103
                                && GetTag(c, Card.GAME_TAG.EXHAUSTED) != 1);
                            bool caveStateUncertain = hasCaveOnBoardNow && !canClickCaveNowByTag;

                            if (hasCaveOnBoardNow && board.Hand != null)
                            {
                                var discardSetForCaveGate = discardComponents != null && discardComponents.Count > 0
                                    ? new HashSet<Card.Cards>(discardComponents)
                                    : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                                var nonCaveNonCoin = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                                    && h.Template.Id != Card.Cards.WON_103
                                    && h.Template.Id != Card.Cards.GAME_005).ToList();
                                caveTotalCntForAttackGate = nonCaveNonCoin.Count;
                                caveDiscardCntForAttackGate = nonCaveNonCoin.Count(h => discardSetForCaveGate.Contains(h.Template.Id));
                                bool hasDiscardForCave = caveDiscardCntForAttackGate > 0;
                                bool clawEquippedNowForAttackGate = board.WeaponFriend != null
                                    && board.WeaponFriend.Template != null
                                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                    && board.WeaponFriend.CurrentDurability > 0;
                                bool heroFrozenNowForAttackGate = board.HeroFriend != null && board.HeroFriend.IsFrozen;
                                bool heroAlreadyAttackedForAttackGate = board.HeroFriend != null && board.HeroFriend.CountAttack > 0;
                                bool shouldSwingClawBeforeCave = clawEquippedNowForAttackGate
                                    && !heroFrozenNowForAttackGate
                                    && !heroAlreadyAttackedForAttackGate
                                    && hasDiscardComponentAtMaxForWeapon;

                                preferCaveBeforeAttackForDiscard = !lethalThisTurn
                                    && hasDiscardForCave
                                    && !shouldSwingClawBeforeCave
                                    && (canClickCaveNowByTag || caveStateUncertain);

                                if (preferCaveBeforeAttackForDiscard)
                                {
                                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                    if (caveStateUncertain && !canClickCaveNowByTag)
                                    {
                                        AddLog("[攻击顺序-地标探测前置] 场上有窟穴且手里有被弃组件(" + caveDiscardCntForAttackGate + "/" + caveTotalCntForAttackGate
                                            + ")，但EXHAUSTED标签不可点 => 本轮先不攻击压费，优先尝试点地标");
                                    }
                                }
                                else if (!lethalThisTurn && hasDiscardForCave && shouldSwingClawBeforeCave)
                                {
                                    AddLog("[攻击顺序-地标让位挥刀] 已装备时空之爪且最高费有被弃组件 => 不走地标前置，先挥刀");
                                }
                            }
                        }
                        catch
                        {
                            preferCaveBeforeAttackForDiscard = false;
                            caveDiscardCntForAttackGate = 0;
                            caveTotalCntForAttackGate = 0;
                        }

                        // 若当前可打超光子，且目标窗口明确（郊狼压费/邪翼蝠压费），则优先法术，不先攻击压费。
                        bool preferPhotonBeforeAttackForCoyote = false;
                        bool preferPhotonBeforeAttackForFelwing = false;
                        try
                        {
                            bool photonPlayableNow = board.Hand.Any(h => h != null && h.Template != null
                                && h.Template.Id == Card.Cards.TIME_027
                                && h.CurrentCost <= board.ManaAvailable);
                            bool enemyHasMinionsNow = board.MinionEnemy != null
                                && board.MinionEnemy.Any(e => e != null);
                            preferPhotonBeforeAttackForCoyote = !lethalThisTurn
                                && photonPlayableNow
                                && coyoteNeedDiscount != null
                                && coyoteNeedDiscount.CurrentCost > 0;
                            preferPhotonBeforeAttackForFelwing = !lethalThisTurn
                                && photonPlayableNow
                                && enemyHasMinionsNow
                                && felwingNeedDiscount != null
                                && felwingNeedDiscount.CurrentCost > 0;
                        }
                        catch
                        {
                            preferPhotonBeforeAttackForCoyote = false;
                            preferPhotonBeforeAttackForFelwing = false;
                        }

                        bool holdCoyoteDiscountForSoulfirePrep = false;
                        string holdCoyoteReason = null;
                        int holdCoyoteDiscardCount = 0;
                        int holdCoyoteTotalCount = 0;
                        try
                        {
                            if (!lethalThisTurn)
                            {
                                Card prepCardForAttack = null;
                                holdCoyoteDiscountForSoulfirePrep = TryGetLowCostPrepForSoulfireBarrageWindow(
                                    board,
                                    out prepCardForAttack,
                                    out holdCoyoteDiscardCount,
                                    out holdCoyoteTotalCount,
                                    out holdCoyoteReason);
                            }
                        }
                        catch
                        {
                            holdCoyoteDiscountForSoulfirePrep = false;
                            holdCoyoteReason = null;
                            holdCoyoteDiscardCount = 0;
                            holdCoyoteTotalCount = 0;
                        }

                        // 涓撻」寮鸿?发：为了“时空之爪?定弃弹幕”，先走脸压郊狼费用?
                        bool shouldAttackFirstForCoyoteDiscardPlan = !lethalThisTurn
                            && canAttackFace
                            && coyoteBlocksStableDiscard
                            && hasClawPlan
                            && !holdCoyoteDiscountForSoulfirePrep
                            && !preferCaveBeforeAttackForDiscard
                            && !preferPhotonBeforeAttackForCoyote;

                        // 通用?：手里有?费郊狼，己方有可走脸?且对?嘲讽时，先A脸压费??
                        bool shouldAttackFirstForCoyoteGeneral = !lethalThisTurn
                            && canAttackFace
                            && coyoteNeedDiscount != null
                            && coyoteNeedDiscount.CurrentCost > 0
                            && hasFaceDamageAttacker
                            && !holdCoyoteDiscountForSoulfirePrep
                            && !preferCaveBeforeAttackForDiscard
                            && !preferPhotonBeforeAttackForCoyote;

                        bool shouldAttackFirstForCoyoteDiscount = shouldAttackFirstForCoyoteDiscardPlan
                            || shouldAttackFirstForCoyoteGeneral;

                        int potentialFaceDiscountFromAttacks = 0;
                        try
                        {
                            if (canAttackFace)
                            {
                                if (board.MinionFriend != null)
                                {
                                    potentialFaceDiscountFromAttacks += board.MinionFriend
                                        .Where(m => m != null && m.CanAttack)
                                        .Sum(m => Math.Max(0, m.CurrentAtk));
                                }

                                if (board.HeroFriend != null && board.HeroFriend.CanAttack)
                                {
                                    potentialFaceDiscountFromAttacks += Math.Max(0, board.HeroFriend.CurrentAtk);
                                }
                            }
                        }
                        catch { potentialFaceDiscountFromAttacks = 0; }

                        bool felwingCanBecomePlayableAfterAttack = false;
                        try
                        {
                            if (felwingNeedDiscount != null && felwingNeedDiscount.CurrentCost > 0)
                            {
                                int currentFelwingCost = felwingNeedDiscount.CurrentCost;
                                bool felwingPlayableNow = currentFelwingCost <= board.ManaAvailable;
                                int projectedFelwingCost = Math.Max(0, currentFelwingCost - potentialFaceDiscountFromAttacks);

                                felwingCanBecomePlayableAfterAttack = !felwingPlayableNow
                                    && potentialFaceDiscountFromAttacks > 0
                                    && projectedFelwingCost <= board.ManaAvailable;
                            }
                        }
                        catch { felwingCanBecomePlayableAfterAttack = false; }

                        bool shouldAttackFirstForFelwingDiscount = !lethalThisTurn
                            && canAttackFace
                            && felwingNeedDiscount != null
                            && felwingNeedDiscount.CurrentCost > 0
                            && !preferCaveBeforeAttackForDiscard
                            && !preferPhotonBeforeAttackForFelwing
                            && hasFaceDamageAttacker
                            && felwingCanBecomePlayableAfterAttack;

                        bool shouldAttackFirstForDiscount = shouldAttackFirstForCoyoteDiscount
                            || shouldAttackFirstForFelwingDiscount;

                        if (shouldAttackFirstForDiscount)
                        {
                            p.ForceResimulation = true;
                            keepForceResimulationForAttackSequencing = true;

                            // 低费回合保护：邪翼蝠压费连段在部分局面会出现“先攻击后空过”。
                            // 这里放宽后续动作的延后强度，保留“先攻击”倾向，同时避免把可执行动作全部压没。
                            bool softenPostAttackDelayForFelwing = false;
                            try
                            {
                                softenPostAttackDelayForFelwing = shouldAttackFirstForFelwingDiscount
                                    && board != null
                                    && board.ManaAvailable <= 3
                                    && board.Hand != null
                                    && board.Hand.Any(h => h != null
                                        && h.Template != null
                                        && h.CurrentCost <= board.ManaAvailable
                                        && h.Template.Id != Card.Cards.GAME_005
                                        && h.Template.Id != Card.Cards.CORE_NEW1_022
                                        && !IsCoyoteCardVariant(h)
                                        && !IsFelwingCardVariant(h));
                            }
                            catch { softenPostAttackDelayForFelwing = false; }

                            int postAttackDelayCast = softenPostAttackDelayForFelwing ? 300 : 1200;
                            int postAttackDelayOrder = softenPostAttackDelayForFelwing ? -900 : -2200;
                            int heroPowerDelayAfterAttack = softenPostAttackDelayForFelwing ? 300 : 1200;

                            // 过牌优先保护：当手里已有可用狗头人/栉龙时，压费后应优先过牌，不应被郊狼抢先。
                            bool preserveDrawMinionPriorityAfterAttack = false;
                            try
                            {
                                preserveDrawMinionPriorityAfterAttack = !lethalThisTurn
                                    && board.Hand != null
                                    && board.Hand.Any(h => h != null
                                        && h.Template != null
                                        && (h.Template.Id == Card.Cards.LOOT_014 || h.Template.Id == Card.Cards.TLC_603)
                                        && h.CurrentCost <= board.ManaAvailable
                                        && !IsForeignInjectedCardForDiscardLogic(board, h));
                            }
                            catch { preserveDrawMinionPriorityAfterAttack = false; }

                            if (softenPostAttackDelayForFelwing)
                            {
                                AddLog("[攻击顺序-邪翼蝠压费] 低费回合放宽后续动作延后，避免先攻击后空过");
                            }

                            if (shouldAttackFirstForFelwingDiscount)
                            {
                                bool shouldYieldFelwingPreloadToPhoton = false;
                                try
                                {
                                    bool photonPlayableNow = board.Hand != null
                                        && board.Hand.Any(h => h != null && h.Template != null
                                            && h.Template.Id == Card.Cards.TIME_027
                                            && h.CurrentCost <= board.ManaAvailable);
                                    bool enemyHasMinionsNow = board.MinionEnemy != null
                                        && board.MinionEnemy.Any(e => e != null);
                                    shouldYieldFelwingPreloadToPhoton = photonPlayableNow && enemyHasMinionsNow;
                                }
                                catch { shouldYieldFelwingPreloadToPhoton = false; }

                                if (!shouldYieldFelwingPreloadToPhoton)
                                {
                                    // 邪翼蝠压费窗：预置“压费后前置”，避免攻击后被通用后置吞掉。
                                    // 说明：当前不可打时这些权重不生效；一旦攻击后变可打，将直接优先落地。
                                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-1200)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(9300)); } catch { }
                                    try
                                    {
                                        foreach (var f in board.Hand.Where(h => h != null && h.Template != null
                                            && IsFelwingCardVariant(h)
                                            && h.CurrentCost > 0))
                                        {
                                            p.CastMinionsModifiers.AddOrUpdate(f.Id, new Modifier(-1200));
                                            p.PlayOrderModifiers.AddOrUpdate(f.Id, new Modifier(9300));
                                        }
                                    }
                                    catch { }
                                }
                                else
                                {
                                    AddLog("[攻击顺序-邪翼蝠压费] 本回合超光子可打且敌方有场 => 仅保留先A压费，不预置邪翼蝠抢先");
                                }
                            }

                            // 先提高攻击『序，尽量先走完可攻击随从?
                            foreach (var minion in board.MinionFriend)
                            {
                                if (minion != null && minion.Template != null && minion.CanAttack)
                                {
                                    // 商贩在“可送死”窗口时，保留其专属高优先级，不被通用攻击顺序覆盖。
                                    if (merchantCanSacrifice && minion.Template.Id == Card.Cards.ULD_163)
                                    {
                                        continue;
                                    }

                                    p.AttackOrderModifiers.AddOrUpdate(minion.Template.Id, new Modifier(700));
                                }
                            }

                            // 本次思?内临时压后非?发牌，避免被速写/分流/提刀等动作抢节奏?
                            foreach (var h in board.Hand)
                            {
                                if (h == null || h.Template == null) continue;
                                if (h.Template.Id == Card.Cards.GAME_005) continue;      // 幸运?
                                if (IsCoyoteCardVariant(h)) continue;                    // 郊狼本体/变体
                                if (IsFelwingCardVariant(h)) continue;                   // 邪翼蝠本?变体
                                if (h.Template.Id == Card.Cards.CORE_NEW1_022) continue; // 恐怖海盗
                                if ((preferPhotonBeforeAttackForCoyote || preferPhotonBeforeAttackForFelwing) && h.Template.Id == Card.Cards.TIME_027) continue; // 压费窗口下，保留超光子优先
                                if (preserveDrawMinionPriorityAfterAttack
                                    && (h.Template.Id == Card.Cards.LOOT_014 || h.Template.Id == Card.Cards.TLC_603))
                                {
                                    // 保留过牌随从的既有高优先级，不做“压费后统一后置”。
                                    continue;
                                }

                                if (h.Type == Card.CType.MINION)
                                {
                                    p.CastMinionsModifiers.AddOrUpdate(h.Id, new Modifier(postAttackDelayCast));
                                }
                                else if (h.Type == Card.CType.SPELL)
                                {
                                    p.CastSpellsModifiers.AddOrUpdate(h.Id, new Modifier(postAttackDelayCast));
                                }
                                else if (h.Type == Card.CType.WEAPON)
                                {
                                    p.CastWeaponsModifiers.AddOrUpdate(h.Id, new Modifier(postAttackDelayCast));
                                }
                                else
                                {
                                    p.LocationsModifiers.AddOrUpdate(h.Id, new Modifier(postAttackDelayCast));
                                }

                                p.PlayOrderModifiers.AddOrUpdate(h.Id, new Modifier(postAttackDelayOrder));
                            }

                            if (shouldAttackFirstForCoyoteDiscount && preserveDrawMinionPriorityAfterAttack)
                            {
                                // 压费窗口下，明确让位过牌随从（狗头人/栉龙），避免郊狼在重算前抢先落地。
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(1800)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-2600)); } catch { }
                                try
                                {
                                    foreach (var c in board.Hand.Where(h => h != null && h.Template != null && IsCoyoteCardVariant(h)))
                                    {
                                        p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(1800));
                                        p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-2600));
                                    }
                                }
                                catch { }
                                AddLog("[攻击顺序-郊狼让位过牌] 本轮先A压费，但手里有可用狗头人/栉龙 => 郊狼后置(-2600/1800)，先过牌");
                            }

                            if (shouldAttackFirstForFelwingDiscount && preserveDrawMinionPriorityAfterAttack)
                            {
                                // 与郊狼对齐：压费窗口下，邪翼蝠也必须让位过牌随从。
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(1800)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-2600)); } catch { }
                                try
                                {
                                    foreach (var f in board.Hand.Where(h => h != null && h.Template != null && IsFelwingCardVariant(h)))
                                    {
                                        p.CastMinionsModifiers.AddOrUpdate(f.Id, new Modifier(1800));
                                        p.PlayOrderModifiers.AddOrUpdate(f.Id, new Modifier(-2600));
                                    }
                                }
                                catch { }
                                AddLog("[攻击顺序-邪翼蝠让位过牌] 本轮先A压费，但手里有可用狗头人/栉龙 => 邪翼蝠后置(-2600/1800)，先过牌");
                            }

                            // 分流也延后，避免??先攻击压费”窗ｈ抽牌?插队?
                            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(heroPowerDelayAfterAttack)); } catch { }
                            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(heroPowerDelayAfterAttack)); } catch { }

                            // 注意：不要把 ComboModifier 缃?null銆?
                            // 鍦?儴鍒?SmartBot 版本中，ProfileParameters.ToJson()/SetToNull 会对其做非?调用，置空会?彂 NRE銆?

                            if (shouldAttackFirstForCoyoteDiscount)
                            {
                                if (shouldAttackFirstForCoyoteDiscardPlan)
                                {
                                    AddLog("[攻击顺序-郊狼压费] 可走脸且郊狼(" + coyoteNeedDiscount.CurrentCost
                                        + "费与灵魂弹幕同/高费(弹幕=" + highestBarrageCost + "), 有时空之爪计划=> 先攻击压费后出牌(ForceResimulation)");
                                }
                                else
                                {
                                    AddLog("[攻击顺序-郊狼压费] 手里有非0费郊狼且己方场上有可走脸随从(无嘲讽) => 先A脸压费后出牌(ForceResimulation)");
                                }
                            }

                            if (shouldAttackFirstForFelwingDiscount)
                            {
                                AddLog("[攻击顺序-邪翼蝠压费] 攻击后可将非0费邪翼蝠压到本回合可用=> 先A脸压费后出牌(ForceResimulation)");
                            }
                        }
                        else if (preferCaveBeforeAttackForDiscard)
                        {
                            AddLog("[攻击顺序-地标前置] 场上窟穴可点且手里有被弃组件(" + caveDiscardCntForAttackGate + "/" + caveTotalCntForAttackGate
                                + ") => 本轮不先攻击压费，优先点地标");
                        }
                        else if (holdCoyoteDiscountForSoulfirePrep)
                        {
                            AddLog("[攻击顺序-郊狼让位魂火] 命中低费提纯窗口(" + holdCoyoteDiscardCount + "/" + holdCoyoteTotalCount
                                + (string.IsNullOrEmpty(holdCoyoteReason) ? "" : ("," + holdCoyoteReason))
                                + ") => 不启用郊狼压费先攻，先低费提纯后魂火");
                        }
                        else if (preferPhotonBeforeAttackForCoyote)
                        {
                            AddLog("[攻击顺序-郊狼压费] 可打超光子且手里有非0费郊狼=> 本轮不先攻击，优先超光子压费后重算");
                        }
                        else if (preferPhotonBeforeAttackForFelwing)
                        {
                            AddLog("[攻击顺序-邪翼蝠压费] 可打超光子且敌方有场 => 不先攻击压费，优先超光子后再看邪翼蝠");
                        }
                        }
                    }
                    else if (hasAttackResimTriggerInHand && !allowAttackResimAfterTurn3 && !allowEarlyAttackResimForFelwing)
                    {
                        AddLog("[攻击顺序] MaxMana=" + board.MaxMana + " (前期回合) => 不启用攻击后重算，避免打乱硬币节奏");
                    }
                }
            }
            catch (Exception ex)
            {
               AddLog("攻击顺序异常: " + ex.Message);
            }

            // ==============================================
            // 新口径?规则汇?（放在决策完成前，保证?后覆盖）
            // 1) 装备?且手?=9：暂缓武?击，先出其他?
            // 2) 被弃组件：仅??手上全是被弃组件+ 本回合已?英雄?鑳解?时才允许使用；其他情况彻底禁用
            // ==============================================
            try
            {
                // 复盘用：任意?櫒鈥滄槸鍚?许攻击?的关键?快照
                try
                {
                    bool hasEquippedWeaponSnap = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.CurrentDurability > 0;
                    bool heroCanAttackWithWeaponSnap = hasEquippedWeaponSnap
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;

                    string weaponName = "(无";
                    string weaponIdStr = "(无";
                    int weaponAtk = 0;
                    int weaponDur = 0;
                    try
                    {
                        if (hasEquippedWeaponSnap)
                        {
                            weaponName = board.WeaponFriend.Template.NameCN;
                            weaponIdStr = board.WeaponFriend.Template.Id.ToString();
                            weaponAtk = board.WeaponFriend.CurrentAtk;
                            weaponDur = board.WeaponFriend.CurrentDurability;
                        }
                    }
                    catch { }

                    // 注意：board.HeroFriend.CanAttack 鍦?些时序下可能出现误判（日志里见过 N 但实际仍发生?攻击）??
                    // 这里?block 鍙ｅ緞鎸夆?已装备?”来算，更贴近?滅瓥鐣?害鏉熲?濄??
                    bool hand9ButDiscardAtMaxException = !lethalThisTurn
                        && hasEquippedWeaponSnap
                        && handCount >= 9
                        && hasDiscardComponentAtMaxForWeapon;
                    bool blockByHand9 = !lethalThisTurn
                        && hasEquippedWeaponSnap
                        && handCount >= 9
                        && !hasDiscardComponentAtMaxForWeapon;
                    bool blockByNoDiscardAtMax = hasEquippedWeaponSnap && !hasDiscardComponentAtMaxForWeapon;

                    if (hasEquippedWeaponSnap)
                    {
                        AddLog("[武器-判定] hasWeapon=" + (hasEquippedWeaponSnap ? "Y" : "N")
                            + " heroCanAttack=" + (heroCanAttackWithWeaponSnap ? "Y" : "N")
                            + " lethal=" + (lethalThisTurn ? "Y" : "N")
                            + " hand=" + handCount
                            + " maxCost=" + maxCostInHand
                            + " hasDiscardAtMax=" + (hasDiscardComponentAtMax ? "Y" : "N")
                            + " hasDiscardAtMaxForWeapon=" + (hasDiscardComponentAtMaxForWeapon ? "Y" : "N")
                            + " weapon=" + weaponIdStr + "(" + weaponName + ")"
                            + " atk=" + weaponAtk + " dur=" + weaponDur
                            + " blockHand9=" + (blockByHand9 ? "Y" : "N")
                            + " hand9DiscardException=" + (hand9ButDiscardAtMaxException ? "Y" : "N")
                            + " blockNoDiscardAtMax=" + (blockByNoDiscardAtMax ? "Y" : "N")
                        );
                    }
                }
                catch { }

                // (0) 先挥?窗口：已装备时空之爪且?最高费组?定为被弃组件”时，先?垁瑙?弃牌，再做过牌动?
                // 目的：避免栉?狗头/速写/分流?湪鎸?垁鍓嶏紝稀释或改变最高费弃牌池??
                try
                {
                    bool hasEquippedClawNow = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0;

                    bool heroAlreadyAttackedThisTurn = false;
                    bool heroFrozenNow = false;
                    try
                    {
                        heroAlreadyAttackedThisTurn = board.HeroFriend != null && board.HeroFriend.CountAttack > 0;
                        heroFrozenNow = board.HeroFriend != null && board.HeroFriend.IsFrozen;
                    }
                    catch { heroAlreadyAttackedThisTurn = false; heroFrozenNow = false; }

                    bool highestAllDiscardNow = false;
                    bool highestHasSingleDiscardNow = false;
                    bool highestSingleDiscardIsCheapSoulBarrageNow = false;
                    int highestDiscardCountNow = 0;
                    int highestTotalCountNow = 0;
                    string highestNowForLog = "(空)";
                    try
                    {
                        var highestNow = GetHighestCostCardsInHand(board, maxCostInHand);
                        highestNowForLog = GetCardIdsForLog(highestNow);
                        highestTotalCountNow = highestNow != null ? highestNow.Count : 0;
                        if (highestNow != null
                            && highestNow.Count > 0
                            && discardComponents != null
                            && discardComponents.Count > 0)
                        {
                            var discardComponentsForWeaponNow = new HashSet<Card.Cards>(
                                discardComponents.Where(id => id != Card.Cards.WON_103));
                            var highestDiscardCardsNow = highestNow.Where(h => h != null
                                && h.Template != null
                                && discardComponentsForWeaponNow.Contains(h.Template.Id)).ToList();
                            highestDiscardCountNow = highestDiscardCardsNow.Count;
                            highestAllDiscardNow = highestDiscardCountNow == highestTotalCountNow;
                            highestHasSingleDiscardNow = highestDiscardCountNow == 1;
                            highestSingleDiscardIsCheapSoulBarrageNow = highestDiscardCardsNow.Count == 1
                                && highestDiscardCardsNow[0] != null
                                && highestDiscardCardsNow[0].Template != null
                                && highestDiscardCardsNow[0].Template.Id == Card.Cards.RLK_534
                                && highestDiscardCardsNow[0].CurrentCost <= 1;
                        }
                    }
                    catch
                    {
                        highestAllDiscardNow = false;
                        highestHasSingleDiscardNow = false;
                        highestSingleDiscardIsCheapSoulBarrageNow = false;
                        highestDiscardCountNow = 0;
                        highestTotalCountNow = 0;
                        highestNowForLog = "(解析失败)";
                    }

                    // 你的口径：
                    // 当最高费组里仅有1张被弃组件，且已装备时空之爪可攻击时，
                    // 暂时禁用“场上窟穴点击”，优先先挥刀触发弃牌。
                    bool clawCanSwingNow = hasEquippedClawNow
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack
                        && !heroAlreadyAttackedThisTurn
                        && !heroFrozenNow;
                    bool clawLikelyCanSwingNow = hasEquippedClawNow
                        && !heroAlreadyAttackedThisTurn
                        && !heroFrozenNow;
                    bool blockBoardCaveForSingleDiscardClawSwing = !lethalThisTurn
                        && canClickCave
                        && highestHasSingleDiscardNow
                        && (clawCanSwingNow || (clawLikelyCanSwingNow && highestSingleDiscardIsCheapSoulBarrageNow));

                    if (blockBoardCaveForSingleDiscardClawSwing)
                    {
                        try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                        try
                        {
                            if (board.MinionFriend != null)
                            {
                                foreach (var cave in board.MinionFriend.Where(x => x != null
                                    && x.Template != null
                                    && x.Template.Id == Card.Cards.WON_103
                                    && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1))
                                {
                                    try { p.LocationsModifiers.AddOrUpdate(cave.Id, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(cave.Id, new Modifier(-9999)); } catch { }
                                }
                            }
                        }
                        catch { }

                        canClickCave = false;
                        AddLog("[全局硬规则-窟穴让位挥刀] 已装备时空之爪且最高费组仅1张被弃组件(最高费=["
                            + highestNowForLog + "],被弃=" + highestDiscardCountNow + "/" + highestTotalCountNow
                            + (highestSingleDiscardIsCheapSoulBarrageNow ? ",灵魂弹幕<=1费=Y" : "")
                            + ") => 暂禁场上窟穴(9999/-9999)，优先挥刀");
                    }

                    bool lockDrawBeforeClawAttack = !lethalThisTurn
                        && hasEquippedClawNow
                        && hasDiscardComponentAtMaxForWeapon
                        && highestAllDiscardNow
                        && !heroAlreadyAttackedThisTurn
                        && !heroFrozenNow;

                    bool handCavePlayableBeforeClawAttack = false;
                    try
                    {
                        handCavePlayableBeforeClawAttack = !lethalThisTurn
                            && hasEquippedClawNow
                            && hasDiscardComponentAtMaxForWeapon
                            && !heroAlreadyAttackedThisTurn
                            && !heroFrozenNow
                            && board.Hand != null
                            && GetFriendlyBoardSlotsUsed(board) < 7
                            && board.Hand.Any(c => c != null
                                && c.Template != null
                                && c.Template.Id == Card.Cards.WON_103
                                && c.CurrentCost <= board.ManaAvailable);
                    }
                    catch { handCavePlayableBeforeClawAttack = false; }

                    if (handCavePlayableBeforeClawAttack)
                    {
                        lockDrawBeforeClawAttack = false;
                        try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9999)); } catch { }
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2200)); } catch { }
                        try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2200)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9800)); } catch { }
                        try
                        {
                            foreach (var cave in board.Hand.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103))
                            {
                                try { p.CastMinionsModifiers.AddOrUpdate(cave.Id, new Modifier(-2200)); } catch { }
                                try { p.LocationsModifiers.AddOrUpdate(cave.Id, new Modifier(-2200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(cave.Id, new Modifier(9800)); } catch { }
                            }
                        }
                        catch { }

                        p.ForceResimulation = true;
                        AddLog("[全局硬规则-手牌地标前置] 已装备时空之爪且最高费命中被弃组件，且手牌地标可拍 => 暂缓挥刀(9999)，先拍手牌地标");
                    }

                    // 解场优先：若地狱烈焰可打且场面压力高，先解场再考虑挥刀弃牌。
                    try
                    {
                        bool hellfirePlayableNow = board.Hand != null && board.Hand.Any(c => c != null
                            && c.Template != null
                            && c.Template.Id == Card.Cards.CORE_CS2_062
                            && c.CurrentCost <= board.ManaAvailable);
                        if (hellfirePlayableNow)
                        {
                            int enemyMinionsCountNow = board.MinionEnemy != null
                                ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0)
                                : 0;
                            int enemyAttackNow = board.MinionEnemy != null
                                ? board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk))
                                : 0;
                            int myHpArmorNow = board.HeroFriend != null
                                ? Math.Max(0, board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor)
                                : 0;

                            bool shouldClearWithHellfire = enemyMinionsCountNow >= 3
                                || enemyAttackNow >= 8
                                || myHpArmorNow <= 18;
                            if (shouldClearWithHellfire)
                            {
                                lockDrawBeforeClawAttack = false;
                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_CS2_062, new Modifier(-2400)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_CS2_062, new Modifier(9800)); } catch { }
                                try
                                {
                                    foreach (var hf in board.Hand.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.CORE_CS2_062))
                                    {
                                        try { p.CastSpellsModifiers.AddOrUpdate(hf.Id, new Modifier(-2400)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(hf.Id, new Modifier(9800)); } catch { }
                                    }
                                }
                                catch { }
                                try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(1600)); } catch { }
                                p.ForceResimulation = true;
                                AddLog("[全局硬规则-先解场] 地狱烈焰可打且场压高(敌从=" + enemyMinionsCountNow
                                    + ",敌攻=" + enemyAttackNow + ",我血甲=" + myHpArmorNow
                                    + ") => 先地狱烈焰解场，暂缓挥刀");
                            }
                        }
                    }
                    catch { }

                    if (lockDrawBeforeClawAttack)
                    {
                        var drawMinionIds = new[]
                        {
                            Card.Cards.TLC_603, // 栉龙
                            Card.Cards.LOOT_014, // 狗头人图?理员
                            Card.Cards.TOY_916, // 速写美术?
                            Card.Cards.TOY_518, // 宝藏经销商
                        };

                        foreach (var id in drawMinionIds)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(id, new Modifier(-9999)); } catch { }
                        }

                        // 先挥?窗口?睍閿侊細
                        // 避免 0 费恐怖海?邪翼蝠/郊狼?湪鎸?前打出，导致错过“先?垁瑙?弃牌”的稳定连段?
                        var preSwingLockMinionIds = new[]
                        {
                            Card.Cards.CORE_NEW1_022, // 恐怖海盗
                            Card.Cards.YOD_032,       // 狂暴邪翼蝠
                            Card.Cards.TIME_047,      // 鐙?的郊?
                            Card.Cards.DRG_056,       // 空降歹徒
                        };
                        foreach (var id in preSwingLockMinionIds)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(id, new Modifier(-9999)); } catch { }
                        }

                        // 先挥刀窗口：除“手牌地标前置”分支外，统一暂缓其余可执行手牌，
                        // 防止先下低价值随从（如异教低阶牧师）打断“挥刀弃弹幕”重算链。
                        try
                        {
                            var discardSetForPreSwing = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                            int friendlyUsedNow = 0;
                            try { friendlyUsedNow = GetFriendlyBoardSlotsUsed(board); } catch { friendlyUsedNow = 7; }
                            bool hasBoardSlotNow = friendlyUsedNow < 7;

                            if (board.Hand != null)
                            {
                                foreach (var c in board.Hand)
                                {
                                    if (c == null || c.Template == null) continue;
                                    if (c.CurrentCost > board.ManaAvailable) continue;
                                    if (c.Template.Id == Card.Cards.GAME_005) continue; // 硬币不额外处理
                                    if (c.Template.Id == Card.Cards.WON_103) continue;   // 手牌地标由上方分支单独处理
                                    if (c.Template.Id == Card.Cards.TLC_451) continue;   // 咒怨之墓由墓前置/优先级分支单独处理，避免被先挥刀误锁
                                    if (discardSetForPreSwing.Contains(c.Template.Id)) continue; // 被弃组件沿用后续硬规则
                                    if (c.Type == Card.CType.MINION && !hasBoardSlotNow) continue;

                                    if (c.Type == Card.CType.MINION)
                                    {
                                        try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                    }
                                    else if (c.Type == Card.CType.SPELL)
                                    {
                                        try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                    }
                                    else if (c.Type == Card.CType.WEAPON)
                                    {
                                        try { p.CastWeaponsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                    }
                                    else
                                    {
                                        try { p.LocationsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                    }

                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                                }
                            }
                        }
                        catch { }

                        if (board.Hand != null)
                        {
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (drawMinionIds.Contains(c.Template.Id) || preSwingLockMinionIds.Contains(c.Template.Id))
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                                }
                            }
                        }

                        if (!holdClawForMerchantBarrage)
                        {
                            // 分流同样禁用，避免先抽牌改变“?定弃牌?目标??
                            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }
                            // 强化?攻击优先级紝鏄庣‘鈥滃厛鎸?垁鈥濄??
                            try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-3200)); } catch { }

                            p.ForceResimulation = true;
                            AddLog("[全局硬规则先挥刀] 已装备时空之爪且最高费组判定为被弃组件(当前最高费=[" + highestNowForLog
                                + "])，且本回合尚未挥? => 暂禁过牌/低价值先手动作，并锁定邪翼蝠/郊狼/空降歹徒，先挥刀触发弃牌");
                        }
                    }
                }
                catch { }

                // (0) 单张弹幕保留给商贩：手里仅1张灵魂弹幕且有商贩时，暂缓挥刀，留给商贩复制
                try
                {
                    int barrageCountNow = board.Hand != null
                        ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534)
                        : 0;
                    if (!lethalThisTurn && hasClawEquipped && barrageCountNow == 1)
                    {
                        var merchantInHand = board.Hand != null
                            ? board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163)
                            : null;
                        bool hasSpaceNow = GetFriendlyBoardSlotsUsed(board) < 7;
                        if (merchantInHand != null && hasSpaceNow)
                        {
                            holdClawForMerchantBarrage = true;
                            AddLog("[全局硬规则先挥刀-让位商贩] 手里仅1张灵魂弹幕且有商贩 => 暂缓挥刀，留给商贩复制");
                        }
                    }
                }
                catch { holdClawForMerchantBarrage = false; }

                // (0) 速写美术家可?：先出美术家，再决定是否??鍣?换（避免先挥?导致节奏偏?锛?
                try
                {
                    bool hasEquippedWeapon = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.CurrentDurability > 0;

                    if (!lethalThisTurn && hasEquippedWeapon && !hasDiscardComponentAtMaxForWeapon && board.Hand != null)
                    {
                        var sketch = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.TOY_916);
                        if (sketch != null && sketch.CurrentCost <= board.ManaAvailable)
                        {
                            var weaponId = board.WeaponFriend.Template.Id;
                            BlockWeaponAttackForAllTargets(p, board, weaponId, 9999);
	                            AddLog("[全局硬规则武器] 手里有速写美术家且可用(费" + sketch.CurrentCost + ") 且最高费无被弃组件=> 先出美术家，暂缓武器攻击(9999)");
                        }
                    }
                }
                catch { }

                // (1) 装备?櫒 + 手牌>=9锛氫粎鍦??最高费无被弃组件=时才禁攻??
                //     鑻最高费有被弃组件（?叾当前最高费仅灵魂弹幕），应允许?垁瑙?弃牌，不要被“防爆牌”一?切拦住??
                try
                {
                    bool hasEquippedWeapon = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.CurrentDurability > 0;
                    bool heroCanAttackWithWeapon = hasEquippedWeapon
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;

                    bool highestOnlySoulBarrageNow = false;
                    try
                    {
                        var highestNow = GetHighestCostCardsInHand(board, maxCostInHand);
                        highestOnlySoulBarrageNow = AreAllHighestCards(highestNow, Card.Cards.RLK_534);
                    }
                    catch { highestOnlySoulBarrageNow = false; }

                    if (!lethalThisTurn && hasEquippedWeapon && handCount >= 9 && !hasDiscardComponentAtMaxForWeapon)
                    {
                        var weaponId = board.WeaponFriend.Template.Id;
                        BlockWeaponAttackForAllTargets(p, board, weaponId, 9999);
                        AddLog("[全局硬规则武器] 装备武器且手牌=9(" + handCount + ")，且当前最高费无被弃组件=> 暂缓武器攻击(9999)，先出其他牌");
                    }
                    else if (!lethalThisTurn && hasEquippedWeapon && handCount >= 9 && hasDiscardComponentAtMaxForWeapon)
                    {
	                        AddLog("[全局硬规则武器] 装备武器且手牌=9(" + handCount + ")，但当前最高费有被弃组件"
	                            + (highestOnlySoulBarrageNow ? "(仅灵魂弹幕" : "")
                            + " => 放行武器攻击，不做手牌上限禁攻");
                    }
                }
                catch { }

                // (2) 被弃组件使用ｅ緞
                try
                {
                    if (discardComponents != null && discardComponents.Count > 0 && board.Hand != null && board.Hand.Count > 0)
                    {
                        // 鈥滆瘹淇?家格里伏塔??VAC_959) 浜?的护?会塞手牌，但它们不应参与“全手被弃组件=濈殑鍒?畾銆?
                        // 鍚?会误?负鈥滈潪鍏?组件”，导致被弃组件窗口被错误关闭??
                        var handForAllCheck = board.Hand
                            .Where(c => c != null
                                && c.Template != null
                                && !IsHonestMerchantCharm(c.Template.Id)
                                && !IsForeignInjectedCardForDiscardLogic(board, c))
                            .ToList();
                        bool handAllDiscardComponents = handForAllCheck.Count > 0
                            && handForAllCheck.All(c => discardComponents.Contains(c.Template.Id));
                        bool allowUseDiscardComponentsNow = handAllDiscardComponents && heroPowerUsedThisTurn;
                        bool allowUseDiscardComponentsByEmergency = false;
                        string discardEmergencyReason = null;
                        bool allowClawBarragePairLine = false;
                        bool allowCaveSpacePirateDirectLine = false;
                        Card clawForPriority = null;
                        bool coinThenClawForPriority = false;
                        bool needCoinForClawPriority = false;
                        try
                        {
                            int usedBoardSlotsNow = 0;
                            try { usedBoardSlotsNow = GetFriendlyBoardSlotsUsed(board); } catch { usedBoardSlotsNow = 0; }

                            bool hasPlayableDiscardComponentNow = board.Hand.Any(c => c != null
                                && c.Template != null
                                && discardComponents.Contains(c.Template.Id)
                                && c.Template.Id != Card.Cards.GAME_005
                                && c.CurrentCost <= board.ManaAvailable
                                && (c.Type != Card.CType.MINION || usedBoardSlotsNow < 7));

                            // 低血放行收紧：仅低血不再直接放行，需至少有一定对面压力/抢血需求。
                            // 目的：避免“可分流且场压=0”时错误前置拳头/行尸，优先分流保留连段。
                            bool hpDangerNow = myHpArmorForStabilize > 0
                                && myHpArmorForStabilize <= 16
                                && (enemyPotentialNextTurnDamage >= 3 || enemyHp <= 12);
                            bool pressureDangerNow = myHpArmorForStabilize > 0
                                && enemyPotentialNextTurnDamage >= Math.Max(6, myHpArmorForStabilize - 4);
                            bool raceDangerNow = enemyHp <= 10
                                && myHpArmorForStabilize > 0
                                && myHpArmorForStabilize <= 20
                                && enemyPotentialNextTurnDamage >= 5;
                            bool preserveTapWindowNow = canTapNow
                                && enemyPotentialNextTurnDamage <= 0
                                && enemyHp >= 18;

                            allowUseDiscardComponentsByEmergency = !lethalThisTurn
                                && hasPlayableDiscardComponentNow
                                && !preserveTapWindowNow
                                && (survivalStabilizeMode || pressureDangerNow || hpDangerNow || raceDangerNow);

                            if (allowUseDiscardComponentsByEmergency)
                            {
                                allowUseDiscardComponentsNow = true;
                                if (survivalStabilizeMode) discardEmergencyReason = "survival_mode";
                                else if (pressureDangerNow) discardEmergencyReason = "pressure";
                                else if (hpDangerNow) discardEmergencyReason = "low_hp";
                                else discardEmergencyReason = "race";
                            }
                            else if (preserveTapWindowNow)
                            {
                                AddLog("[全局硬规则被弃组件-危机放行抑制] 可分流且敌压=0,敌血=" + enemyHp
                                    + " => 保留分流窗口，暂不前置被弃组件");
                            }
                        }
                        catch
                        {
                            allowUseDiscardComponentsByEmergency = false;
                            discardEmergencyReason = null;
                        }
                        try
                        {
                            bool hasCoinForClaw = false;
                            try { hasCoinForClaw = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinForClaw = false; }
                            int effectiveManaForClaw = board.ManaAvailable + (hasCoinForClaw ? 1 : 0);

                            bool clawEquippedNow = false;
                            try
                            {
                                clawEquippedNow = board.WeaponFriend != null
                                    && board.WeaponFriend.Template != null
                                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                    && board.WeaponFriend.CurrentDurability > 0;
                            }
                            catch { clawEquippedNow = false; }

                            int barrageCountInHand = board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);
                            clawForPriority = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.END_016);
                            bool clawPlayableNow = clawForPriority != null
                                && !clawEquippedNow
                                && clawForPriority.CurrentCost <= effectiveManaForClaw;
                            needCoinForClawPriority = clawForPriority != null
                                && hasCoinForClaw
                                && clawForPriority.CurrentCost > board.ManaAvailable
                                && clawForPriority.CurrentCost <= effectiveManaForClaw;

                            allowClawBarragePairLine = barrageCountInHand >= 2 && clawPlayableNow;
                        }
                        catch
                        {
                            allowClawBarragePairLine = false;
                            clawForPriority = null;
                            coinThenClawForPriority = false;
                            needCoinForClawPriority = false;
                        }
                        if (allowClawBarragePairLine)
                        {
                            allowUseDiscardComponentsNow = true;
                            try { p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2800)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9800)); } catch { }
                            try
                            {
                                if (clawForPriority != null)
                                {
                                    coinThenClawForPriority = TrySetCoinThenCardComboWithoutResim(
                                        p, board, clawForPriority, needCoinForClawPriority, "全局硬规则-双弹幕提刀");
                                    if (!coinThenClawForPriority)
                                    {
                                        p.ComboModifier = new ComboSet(clawForPriority.Id);
                                        p.ForceResimulation = true;
                                    }
                                }
                            }
                            catch { }
                        }
                        try
                        {
                            Card caveOnly;
                            Card pirateOnly;
                            bool onlyCaveAndSpacePirate = IsOnlyCaveAndSpacePirateHand(board, out caveOnly, out pirateOnly);
                            bool cavePlayableNow = caveOnly != null
                                && caveOnly.CurrentCost <= board.ManaAvailable
                                && GetFriendlyBoardSlotsUsed(board) < 7;
                            allowCaveSpacePirateDirectLine = !heroPowerUsedThisTurn
                                && onlyCaveAndSpacePirate
                                && cavePlayableNow;
                        }
                        catch { allowCaveSpacePirateDirectLine = false; }
                        if (allowCaveSpacePirateDirectLine)
                        {
                            allowUseDiscardComponentsNow = true;
                        }
                        bool allowBoilerFuelEmergency = false;
                        string boilerFuelEmergencyReason = null;
                        int boilerFuelEmergencyCastModifier = -1800;
                        int boilerFuelEmergencyPlayOrderModifier = 9200;
                        try
                        {
                            int myHpArmorNow = 0;
                            try { myHpArmorNow = board.HeroFriend != null ? (board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) : 0; } catch { myHpArmorNow = 0; }

                            bool hasPlayableBoilerFuel = board.Hand.Any(c => c != null && c.Template != null
                                && c.Template.Id == Card.Cards.WW_441
                                && c.CurrentCost <= board.ManaAvailable);

                            bool hasPlayableNonDiscardNonCoinAction = board.Hand.Any(c => c != null && c.Template != null
                                && c.Template.Id != Card.Cards.GAME_005
                                && c.Template.Id != Card.Cards.WW_441
                                && c.CurrentCost <= board.ManaAvailable
                                && !IsForeignInjectedCardForDiscardLogic(board, c)
                                && !discardComponents.Contains(c.Template.Id));
                            bool hasPlayableNonDiscardNonCoinActionExcludingCoyote = board.Hand.Any(c => c != null && c.Template != null
                                && c.Template.Id != Card.Cards.GAME_005
                                && c.Template.Id != Card.Cards.WW_441
                                && c.Template.Id != Card.Cards.TIME_047
                                && c.CurrentCost <= board.ManaAvailable
                                && !IsForeignInjectedCardForDiscardLogic(board, c)
                                && !discardComponents.Contains(c.Template.Id));
                            bool onlyCoyoteAsNonDiscardAction = hasPlayableNonDiscardNonCoinAction
                                && !hasPlayableNonDiscardNonCoinActionExcludingCoyote;

                            bool tapUsableNow = false;
                            try { tapUsableNow = CanUseLifeTapNow(board); }
                            catch { tapUsableNow = false; }

                            bool coyoteUnlocksDiscardWindowNow = false;
                            try { coyoteUnlocksDiscardWindowNow = CanCoyoteUnlockClawDiscardWindow(board, discardComponents); } catch { coyoteUnlocksDiscardWindowNow = false; }

                            int highestDiscardCountNow = 0;
                            int highestTotalCountNow = 0;
                            bool highestDiscardRatioHalfOrMoreNow = false;
                            try
                            {
                                highestDiscardRatioHalfOrMoreNow = IsHighestCostDiscardRatioAtLeastHalf(
                                    board, discardComponents, out highestDiscardCountNow, out highestTotalCountNow);
                            }
                            catch
                            {
                                highestDiscardCountNow = 0;
                                highestTotalCountNow = 0;
                                highestDiscardRatioHalfOrMoreNow = false;
                            }

                            bool clawCanAttackNow = false;
                            bool hasClawInHandAndReadyNow = false;
                            bool merchantPlayableNow = false;
                            bool cavePlayableFromHandNow = false;
                            try
                            {
                                clawCanAttackNow = board.WeaponFriend != null && board.WeaponFriend.Template != null
                                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                    && board.HeroFriend != null && board.HeroFriend.CanAttack;

                                hasClawInHandAndReadyNow = board.Hand.Any(c => c != null && c.Template != null
                                    && c.Template.Id == Card.Cards.END_016
                                    && c.CurrentCost <= board.ManaAvailable);

                                merchantPlayableNow = GetFriendlyBoardSlotsUsed(board) < 7
                                    && board.Hand.Any(c => c != null && c.Template != null
                                        && c.Template.Id == Card.Cards.ULD_163
                                        && c.CurrentCost <= board.ManaAvailable);

                                cavePlayableFromHandNow = GetFriendlyBoardSlotsUsed(board) < 7
                                    && board.Hand.Any(c => c != null && c.Template != null
                                        && c.Template.Id == Card.Cards.WON_103
                                        && c.CurrentCost <= board.ManaAvailable);
                            }
                            catch
                            {
                                clawCanAttackNow = false;
                                hasClawInHandAndReadyNow = false;
                                merchantPlayableNow = false;
                                cavePlayableFromHandNow = false;
                            }

                            bool stableStarterNow = highestDiscardRatioHalfOrMoreNow
                                && (clawCanAttackNow || hasClawInHandAndReadyNow || merchantPlayableNow || canClickCave || cavePlayableFromHandNow);

                            // 保命例外ｅ緞锛?
                            // - 非斩杀回合?
                            // - 血鐢?=3（不应再分流）；
                            // - 本回合可直接打锅炉燃料；
                            // - 且无其它“非被弃组件”的稳定可打?綔銆?
                            bool allowBoilerFuelLowHp = !lethalThisTurn
                                && myHpArmorNow <= 3
                                && hasPlayableBoilerFuel
                                && !hasPlayableNonDiscardNonCoinAction
                                && (tapUsableNow || handCount <= 2);
                            bool allowBoilerFuelAvoidPass = !lethalThisTurn
                                && hasPlayableBoilerFuel
                                && !tapUsableNow
                                && (!hasPlayableNonDiscardNonCoinAction
                                    || (onlyCoyoteAsNonDiscardAction && !coyoteUnlocksDiscardWindowNow));

                            // 中后期高密度例外?
                            // 当最高费已命中被弃组件=佷笖当前最高费弃牌占比>=1/2，但当前℃稳定启动?椂锛?
                            // 允许直接打锅炉燃料，避免“有?但全是低?过牌”导致节奏?杞???
                            bool allowBoilerFuelHighDensity = !lethalThisTurn
                                && hasPlayableBoilerFuel
                                && board.MaxMana >= 5
                                && hasDiscardComponentAtMax
                                && highestDiscardRatioHalfOrMoreNow
                                && !tapUsableNow
                                && !stableStarterNow;

                            allowBoilerFuelEmergency = allowBoilerFuelLowHp || allowBoilerFuelAvoidPass || allowBoilerFuelHighDensity;
                            if (allowBoilerFuelLowHp)
                            {
                                boilerFuelEmergencyReason = "low_hp";
                            }
                            else if (allowBoilerFuelAvoidPass)
                            {
                                boilerFuelEmergencyReason = "avoid_pass";
                            }
                            else if (allowBoilerFuelHighDensity)
                            {
                                boilerFuelEmergencyReason = "high_density";
                                boilerFuelEmergencyCastModifier = -2600;
                                boilerFuelEmergencyPlayOrderModifier = 9600;
                            }
                        }
                        catch { allowBoilerFuelEmergency = false; boilerFuelEmergencyReason = null; }

                        if (!allowUseDiscardComponentsNow)
                        {
                            foreach (var id in discardComponents)
                            {
                                // 行尸(RLK_532)例外：若当前有费用且场面需要/无其他动作，允许作为普通随从打出（不再硬禁）。
                                // 动态调整：手牌<=4时优先打出(-150)抢节奏；手牌多时倾向保留(350)等弃牌。
                                if (id == Card.Cards.RLK_532)
                                {
                                    int mod = handCount <= 4 ? -150 : 350;
                                    try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(mod)); } catch { }
                                    continue;
                                }

                                // 同时对法?随从做禁用（?未来组件类型变化?
                                try { p.CastSpellsModifiers.AddOrUpdate(id, new Modifier(9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(9999)); } catch { }
                            }
                            // 同时给手牌实例上锁，避免仅?板修ｅ个别时序被绕过??
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (!discardComponents.Contains(c.Template.Id)) continue;
                                try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                            }

                            // 额外硬护栏：
                            // 当手里有可直?出的窟穴时，禁止“直拍被弃组件=（例如加拉克苏斯之拳），优先先拍窟穴建立弃牌引擎??
                            try
                            {
                                if (!lethalThisTurn && canPlayCaveFromHand)
                                {
                                    var caveInHandNow = board.Hand.FirstOrDefault(c => c != null && c.Template != null
                                        && c.Template.Id == Card.Cards.WON_103);
                                    bool cavePlayableWithoutCoin = caveInHandNow != null
                                        && caveInHandNow.CurrentCost <= board.ManaAvailable
                                        && GetFriendlyBoardSlotsUsed(board) < 7;
                                    bool hasBoardSpaceForMinionNow = GetFriendlyBoardSlotsUsed(board) < 7;
                                    bool hasPlayableDiscardComponentNow = board.Hand.Any(c => c != null
                                        && c.Template != null
                                        && c.Template.Id != Card.Cards.WON_103
                                        && c.Template.Id != Card.Cards.GAME_005
                                        && discardComponents.Contains(c.Template.Id)
                                        && c.CurrentCost <= board.ManaAvailable
                                        && (c.Type != Card.CType.MINION || hasBoardSpaceForMinionNow));

                                    bool stableMerchantBarrageNowBeforeCave = false;
                                    try
                                    {
                                        var merchantNow = board.Hand.FirstOrDefault(h => h != null && h.Template != null && h.Template.Id == Card.Cards.ULD_163);
                                        bool merchantPlayableNow = merchantNow != null
                                            && merchantNow.CurrentCost <= board.ManaAvailable
                                            && GetFriendlyBoardSlotsUsed(board) < 7;
                                        if (merchantPlayableNow)
                                        {
                                            bool stableByDiscardLogic = false;
                                            bool stableByRawHand = false;

                                            var handWithoutMerchantAndCoin = GetHandCardsForDiscardLogic(board).Where(h => h != null
                                                && h.Template != null
                                                && h.Template.Id != Card.Cards.ULD_163
                                                && h.Template.Id != Card.Cards.GAME_005).ToList();
                                            if (handWithoutMerchantAndCoin.Count > 0)
                                            {
                                                int maxCostNow = handWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                                                var highestNow = handWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxCostNow).ToList();
                                                stableByDiscardLogic = highestNow.Count > 0
                                                    && highestNow.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                                            }

                                            var rawHandWithoutMerchantAndCoin = (board.Hand ?? new List<Card>()).Where(h => h != null
                                                && h.Template != null
                                                && h.Template.Id != Card.Cards.ULD_163
                                                && h.Template.Id != Card.Cards.GAME_005).ToList();
                                            if (rawHandWithoutMerchantAndCoin.Count > 0)
                                            {
                                                int maxRawCostNow = rawHandWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                                                var highestRawNow = rawHandWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxRawCostNow).ToList();
                                                stableByRawHand = highestRawNow.Count > 0
                                                    && highestRawNow.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                                            }

                                            stableMerchantBarrageNowBeforeCave = stableByDiscardLogic || stableByRawHand;
                                        }
                                    }
                                    catch { stableMerchantBarrageNowBeforeCave = false; }

                                    bool canStabilizeMerchantByCoyoteBeforeCave = false;
                                    string coyoteMerchantBeforeCaveReason = null;
                                    try
                                    {
                                        bool hasCoinForCaveMerchant = false;
                                        try { hasCoinForCaveMerchant = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinForCaveMerchant = false; }
                                        int effectiveManaForCaveMerchant = board.ManaAvailable + (hasCoinForCaveMerchant ? 1 : 0);
                                        canStabilizeMerchantByCoyoteBeforeCave = CanStabilizeMerchantBarrageAfterCoyoteFaceDiscount(
                                            board,
                                            effectiveManaForCaveMerchant,
                                            out coyoteMerchantBeforeCaveReason);
                                    }
                                    catch
                                    {
                                        canStabilizeMerchantByCoyoteBeforeCave = false;
                                        coyoteMerchantBeforeCaveReason = null;
                                    }

                                    if (cavePlayableWithoutCoin && hasPlayableDiscardComponentNow && !stableMerchantBarrageNowBeforeCave && !canStabilizeMerchantByCoyoteBeforeCave)
                                    {
                                        foreach (var id in discardComponents)
                                        {
                                            try { p.CastSpellsModifiers.AddOrUpdate(id, new Modifier(9999)); } catch { }
                                            try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(9999)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(id, new Modifier(-9999)); } catch { }
                                        }
                                        foreach (var c in board.Hand)
                                        {
                                            if (c == null || c.Template == null) continue;
                                            if (!discardComponents.Contains(c.Template.Id)) continue;
                                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                            try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                                        }

                                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1200)); } catch { }
                                        try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1200)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9800)); } catch { }

                                        if (caveInHandNow != null)
                                        {
                                            try { p.CastMinionsModifiers.AddOrUpdate(caveInHandNow.Id, new Modifier(-1200)); } catch { }
                                            try { p.LocationsModifiers.AddOrUpdate(caveInHandNow.Id, new Modifier(-1200)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(caveInHandNow.Id, new Modifier(9800)); } catch { }
                                        }

                                        AddLog("[全局硬规则窟穴前置] 手牌有可直接拍出的窟穴，且有可直拍被弃组件=> 禁止直拍被弃组件(9999/-9999)，优先窟穴9800/-1200)");
                                    }
                                    else if ((cavePlayableWithoutCoin || canClickCave)
                                        && hasPlayableDiscardComponentNow
                                        && (stableMerchantBarrageNowBeforeCave || canStabilizeMerchantByCoyoteBeforeCave))
                                    {
                                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                        try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                                        try
                                        {
                                            if (caveInHandNow != null)
                                            {
                                                try { p.CastMinionsModifiers.AddOrUpdate(caveInHandNow.Id, new Modifier(9999)); } catch { }
                                                try { p.LocationsModifiers.AddOrUpdate(caveInHandNow.Id, new Modifier(9999)); } catch { }
                                                try { p.PlayOrderModifiers.AddOrUpdate(caveInHandNow.Id, new Modifier(-9999)); } catch { }
                                            }
                                            if (board.MinionFriend != null)
                                            {
                                                foreach (var caveOnBoard in board.MinionFriend.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103))
                                                {
                                                    try { p.LocationsModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(9999)); } catch { }
                                                    try { p.PlayOrderModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(-9999)); } catch { }
                                                }
                                            }
                                        }
                                        catch { }

                                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-2600)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9950)); } catch { }
                                        try
                                        {
                                            if (board.Hand != null)
                                            {
                                                foreach (var m in board.Hand.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163))
                                                {
                                                    try { p.CastMinionsModifiers.AddOrUpdate(m.Id, new Modifier(-2600)); } catch { }
                                                    try { p.PlayOrderModifiers.AddOrUpdate(m.Id, new Modifier(9950)); } catch { }
                                                }
                                            }
                                        }
                                        catch { }

                                        AddLog("[全局硬规则窟穴前置-让位商贩] 商贩+灵魂弹幕连段可达成("
                                            + (stableMerchantBarrageNowBeforeCave ? "已稳定" : (string.IsNullOrEmpty(coyoteMerchantBeforeCaveReason) ? "可压费转稳定" : coyoteMerchantBeforeCaveReason))
                                            + ") => 禁用窟穴前置，优先商贩");
                                    }
                                }
                            }
                            catch { }

                            // 低血线保命例外：允许并强?炉燃?WW_441)过牌，避免?不能分?鍏?弃锁”导致?杩囥??
                            if (allowBoilerFuelEmergency)
                            {
                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_441, new Modifier(boilerFuelEmergencyCastModifier)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_441, new Modifier(boilerFuelEmergencyCastModifier)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_441, new Modifier(boilerFuelEmergencyPlayOrderModifier)); } catch { }
                                foreach (var c in board.Hand)
                                {
                                    if (c == null || c.Template == null) continue;
                                    if (c.Template.Id != Card.Cards.WW_441) continue;
                                    try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(boilerFuelEmergencyCastModifier)); } catch { }
                                    try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(boilerFuelEmergencyCastModifier)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(boilerFuelEmergencyPlayOrderModifier)); } catch { }
                                }
                                AddLog("[全局硬规则被弃组件-例外] reason=" + (boilerFuelEmergencyReason ?? "fallback")
                                    + " => 放行锅炉燃料(WW_441)(" + boilerFuelEmergencyCastModifier + "/" + boilerFuelEmergencyPlayOrderModifier + ")");
                            }

                            AddLog("[全局硬规则被弃组件] 非“全手被弃组件已分流”all=" + (handAllDiscardComponents ? "Y" : "N")
                                + ",heroPowerUsed=" + (heroPowerUsedThisTurn ? "Y" : "N") + ") => 彻底禁用被弃组件使用(9999)");
                        }
                        else
                        {
                            // 仅在允许窗口内?放行?使用（不强制打出，但避免被其它逻辑长期压制?
                            foreach (var id in discardComponents)
                            {
                                try { p.CastSpellsModifiers.AddOrUpdate(id, new Modifier(-50)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(-50)); } catch { }
                            }
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (!discardComponents.Contains(c.Template.Id)) continue;
                                try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-50)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(-50)); } catch { }
                            }
                            bool emergencyYieldToClickableCave = false;
                            int caveYieldDiscardCount = 0;
                            int caveYieldTotalCount = 0;
                            bool emergencyPreferDirectBarrage = false;
                            try
                            {
                                if (allowUseDiscardComponentsByEmergency && !lethalThisTurn && canClickCave)
                                {
                                    var handForCaveYield = GetHandCardsForDiscardLogic(board);
                                    var discardSetForCaveYield = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                                    caveYieldTotalCount = handForCaveYield.Count(c => c != null
                                        && c.Template != null
                                        && c.Template.Id != Card.Cards.WON_103
                                        && c.Template.Id != Card.Cards.GAME_005
                                        && !ShouldIgnoreCardForCaveDiscardRatio(c));
                                    caveYieldDiscardCount = handForCaveYield.Count(c => c != null
                                        && c.Template != null
                                        && c.Template.Id != Card.Cards.WON_103
                                        && c.Template.Id != Card.Cards.GAME_005
                                        && !ShouldIgnoreCardForCaveDiscardRatio(c)
                                        && discardSetForCaveYield.Contains(c.Template.Id));

                                    bool caveHasDiscardValue = caveYieldDiscardCount > 0;
                                    bool caveCanCycleWhenSmallHand = caveYieldTotalCount <= 4;
                                    emergencyYieldToClickableCave = caveHasDiscardValue || caveCanCycleWhenSmallHand;
                                }
                            }
                            catch
                            {
                                emergencyYieldToClickableCave = false;
                                caveYieldDiscardCount = 0;
                                caveYieldTotalCount = 0;
                            }
                            try
                            {
                                if (allowUseDiscardComponentsByEmergency && !lethalThisTurn)
                                {
                                    bool deckEmptyNow = board != null && board.FriendDeckCount <= 0;
                                    bool hasClawEquippedNow = board.WeaponFriend != null
                                        && board.WeaponFriend.Template != null
                                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                        && board.WeaponFriend.CurrentDurability > 0;
                                    bool hasClawInHandNow = board.Hand != null && board.Hand.Any(h => h != null
                                        && h.Template != null
                                        && h.Template.Id == Card.Cards.END_016);
                                    bool hasAnyCaveOnBoardNow = board.MinionFriend != null && board.MinionFriend.Any(m => m != null
                                        && m.Template != null
                                        && m.Template.Id == Card.Cards.WON_103);
                                    bool hasAnyCaveInHandNow = board.Hand != null && board.Hand.Any(h => h != null
                                        && h.Template != null
                                        && h.Template.Id == Card.Cards.WON_103);
                                    bool hasPlayableBarrageNow = board.Hand != null && board.Hand.Any(h => h != null
                                        && h.Template != null
                                        && h.Template.Id == Card.Cards.RLK_534
                                        && h.CurrentCost <= board.ManaAvailable);

                                    // 商贩稳定线存在时，仍让位商贩，不走直拍弹幕兜底。
                                    bool stableMerchantBarrageNow = false;
                                    try
                                    {
                                        bool hasSpace = GetFriendlyBoardSlotsUsed(board) < 7;
                                        var merchantNow = board.Hand.FirstOrDefault(h => h != null && h.Template != null && h.Template.Id == Card.Cards.ULD_163);
                                        bool merchantPlayableNow = merchantNow != null && hasSpace && merchantNow.CurrentCost <= board.ManaAvailable;
                                        if (merchantPlayableNow)
                                        {
                                            var handWithoutMerchantAndCoin = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                                                && h.Template.Id != Card.Cards.ULD_163
                                                && h.Template.Id != Card.Cards.GAME_005).ToList();
                                            if (handWithoutMerchantAndCoin.Count > 0)
                                            {
                                                int maxCostNow = handWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                                                var highestNow = handWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxCostNow).ToList();
                                                stableMerchantBarrageNow = highestNow.Count > 0
                                                    && highestNow.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                                            }
                                        }
                                    }
                                    catch { stableMerchantBarrageNow = false; }

                                    emergencyPreferDirectBarrage = deckEmptyNow
                                        && hasPlayableBarrageNow
                                        && !hasClawEquippedNow
                                        && !hasClawInHandNow
                                        && !hasAnyCaveOnBoardNow
                                        && !hasAnyCaveInHandNow
                                        && !stableMerchantBarrageNow;
                                }
                            }
                            catch { emergencyPreferDirectBarrage = false; }
                            if (allowUseDiscardComponentsByEmergency)
                            {
                                if (emergencyPreferDirectBarrage)
                                {
                                    foreach (var id in discardComponents)
                                    {
                                        if (id == Card.Cards.RLK_534)
                                        {
                                            try { p.CastSpellsModifiers.AddOrUpdate(id, new Modifier(-2600)); } catch { }
                                            try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(-2600)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(id, new Modifier(9920)); } catch { }
                                        }
                                        else
                                        {
                                            try { p.CastSpellsModifiers.AddOrUpdate(id, new Modifier(1600)); } catch { }
                                            try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(1600)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(id, new Modifier(-1200)); } catch { }
                                        }
                                    }
                                    foreach (var c in board.Hand)
                                    {
                                        if (c == null || c.Template == null) continue;
                                        if (!discardComponents.Contains(c.Template.Id)) continue;
                                        if (c.Template.Id == Card.Cards.RLK_534)
                                        {
                                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-2600)); } catch { }
                                            try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(-2600)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9920)); } catch { }
                                        }
                                        else
                                        {
                                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(1600)); } catch { }
                                            try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(1600)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-1200)); } catch { }
                                        }
                                    }
                                    AddLog("[全局硬规则被弃组件-危机放行改写] reason=" + (discardEmergencyReason ?? "unknown")
                                        + ",hp=" + myHpArmorForStabilize
                                        + ",enemyPressure=" + enemyPotentialNextTurnDamage
                                        + ",enemyHp=" + enemyHp
                                        + " => 牌库为空且无刀/无地标，优先直拍灵魂弹幕(-2600/9920)，压制其他被弃组件");
                                }
                                else if (!emergencyYieldToClickableCave)
                                {
                                    foreach (var id in discardComponents)
                                    {
                                        try { p.CastSpellsModifiers.AddOrUpdate(id, new Modifier(-900)); } catch { }
                                        try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(-900)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(id, new Modifier(9200)); } catch { }
                                    }
                                    foreach (var c in board.Hand)
                                    {
                                        if (c == null || c.Template == null) continue;
                                        if (!discardComponents.Contains(c.Template.Id)) continue;
                                        try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-900)); } catch { }
                                        try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(-900)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9200)); } catch { }
                                    }
                                    AddLog("[全局硬规则被弃组件-危机放行] reason=" + (discardEmergencyReason ?? "unknown")
                                        + ",hp=" + myHpArmorForStabilize
                                        + ",enemyPressure=" + enemyPotentialNextTurnDamage
                                        + ",enemyHp=" + enemyHp
                                        + " => 放行并前置可打被弃组件(-900/9200)");
                                }
                                else
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800)); } catch { }
                                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                    try
                                    {
                                        foreach (var caveOnBoard in board.MinionFriend.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103))
                                        {
                                            if (caveOnBoard == null) continue;
                                            try { p.LocationsModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(-1800)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(9999)); } catch { }
                                        }
                                    }
                                    catch { }

                                    AddLog("[全局硬规则被弃组件-危机放行让位地标] reason=" + (discardEmergencyReason ?? "unknown")
                                        + ",hp=" + myHpArmorForStabilize
                                        + ",enemyPressure=" + enemyPotentialNextTurnDamage
                                        + ",enemyHp=" + enemyHp
                                        + ",caveDiscard=" + caveYieldDiscardCount + "/" + caveYieldTotalCount
                                        + " => 场上地标可点，先点地标重算，暂不前置被弃组件");
                                }
                            }
                            else if (allowClawBarragePairLine)
                            {
                                AddLog("[全局硬规则被弃组件-提刀特判] 手牌灵魂弹幕>=2且可提刀 => 放行被弃组件并强推提刀"
                                    + (coinThenClawForPriority ? "(硬币->提刀)" : ""));
                            }
                            else if (allowCaveSpacePirateDirectLine)
                            {
                                AddLog("[全局硬规则被弃组件-特判] 手牌仅地标+太空海盗 => 放行地标使用，不要求先分流");
                            }
                            else
                            {
                                AddLog("[全局硬规则被弃组件] 满足“全手被弃组件已分流” => 放行被弃组件使用(-50)");
                            }
                        }
                    }
                }
                catch { }

                // (2.0) 双郊狼优先：当手里有2张可直接打出的郊狼时，行尸让位，避免抢先落行尸。
                try
                {
                    if (!lethalThisTurn && board.Hand != null && board.Hand.Count > 0)
                    {
                        bool hasBoardSpaceNow = GetFriendlyBoardSlotsUsed(board) < 7;
                        int playableCoyoteCountNow = board.Hand.Count(h => h != null
                            && h.Template != null
                            && IsCoyoteCardVariant(h)
                            && h.CurrentCost <= board.ManaAvailable
                            && hasBoardSpaceNow);
                        int playableWalkingDeadCountNow = board.Hand.Count(h => h != null
                            && h.Template != null
                            && h.Template.Id == Card.Cards.RLK_532
                            && h.CurrentCost <= board.ManaAvailable
                            && hasBoardSpaceNow);

                        if (playableCoyoteCountNow >= 2 && playableWalkingDeadCountNow > 0)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_532, new Modifier(2200)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_532, new Modifier(-2600)); } catch { }
                            foreach (var c in board.Hand.Where(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_532))
                            {
                                try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(2200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-2600)); } catch { }
                            }
                            AddLog("[行尸-让位双郊狼] 可直接下郊狼x" + playableCoyoteCountNow
                                + " => 延后行尸(-2600/2200)，先拍郊狼");
                        }
                    }
                }
                catch { }

                // (2.1) 灵魂弹幕终极?爮锛氬瓨鍦??滅?定弃弹幕”启?时，禁止直拍灵魂弹幕并强制让位给启动???
                try
                {
                    if (board.Hand != null && board.Hand.Count > 0 && board.HasCardInHand(Card.Cards.RLK_534))
                    {
                        var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                        var barrages = handForDiscardLogic.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534).ToList();
                        bool barragePlayableNow = barrages.Any(c => c != null && c.CurrentCost <= board.ManaAvailable);
                        if (barragePlayableNow)
                        {
                            bool stableByMerchant = false;
                            bool stableByClaw = false;
                            bool needCoinForMerchant = false;
                            Card merchant = null;

                            // A) 商贩稳定弃弹幕：战吼目标“最高费组?仅为灵魂弹幕
                            try
                            {
                                merchant = handForDiscardLogic.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163);
                                bool hasSpace = GetFriendlyBoardSlotsUsed(board) < 7;
                                bool canPlayMerchantNow = merchant != null && hasSpace && merchant.CurrentCost <= effectiveMana;
                                needCoinForMerchant = merchant != null && hasCoin
                                    && merchant.CurrentCost > board.ManaAvailable
                                    && merchant.CurrentCost <= effectiveMana;

                                if (canPlayMerchantNow)
                                {
                                    var handWithoutMerchantAndCoin = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                                        && h.Template.Id != Card.Cards.ULD_163
                                        && h.Template.Id != Card.Cards.GAME_005).ToList();
                                    if (handWithoutMerchantAndCoin.Count > 0)
                                    {
                                        int maxCost = handWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                                        var highest = handWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxCost).ToList();
                                        stableByMerchant = highest.Count > 0
                                            && highest.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                                    }
                                }
                            }
                            catch { stableByMerchant = false; needCoinForMerchant = false; merchant = null; }

                            // B) 爪子稳定弃弹幕：已装备且可攻击，且最高费组仅为灵魂弹幕
                            try
                            {
                                bool clawCanAttackNow = board.WeaponFriend != null && board.WeaponFriend.Template != null
                                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                    && board.HeroFriend != null && board.HeroFriend.CanAttack;
                                if (clawCanAttackNow)
                                {
                                    var handWithoutCoin = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                                        && h.Template.Id != Card.Cards.GAME_005).ToList();
                                    if (handWithoutCoin.Count > 0)
                                    {
                                        int maxCost = handWithoutCoin.Max(h => h.CurrentCost);
                                        var highest = handWithoutCoin.Where(h => h.CurrentCost == maxCost).ToList();
                                        stableByClaw = highest.Count > 0
                                            && highest.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                                    }
                                }
                            }
                            catch { stableByClaw = false; }

                            if (stableByMerchant || stableByClaw)
                            {
                                SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);

                                if (stableByMerchant && merchant != null)
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-2500)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9300)); } catch { }
                                    try { p.CastMinionsModifiers.AddOrUpdate(merchant.Id, new Modifier(-2500)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(merchant.Id, new Modifier(9300)); } catch { }

                                    bool coinThenMerchant = TrySetCoinThenCardComboWithoutResim(p, board, merchant, needCoinForMerchant, "全局终极护栏-灵魂弹幕");
                                    if (!coinThenMerchant)
                                    {
                                        try
                                        {
                                            if (p.ComboModifier == null)
                                            {
                                                p.ComboModifier = new ComboSet(merchant.Id);
                                            }
                                        }
                                        catch { }
                                    }

	                                    AddLog("[全局终极护栏-灵魂弹幕] 可稳定商贩弃弹幕 => 禁止直拍弹幕(9999)，强制让位商贩"
	                                        + (needCoinForMerchant ? "（硬币起手）" : ""));
                                }
                                else if (stableByClaw)
                                {
                                    try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2500)); } catch { }
                                    p.ForceResimulation = true;
                                    AddLog("[全局终极护栏-灵魂弹幕] 可稳定挥刀弃弹幕=> 禁止直拍弹幕(9999)，优先挥刀触发弃牌");
                                }
                            }
                        }
                    }
                }
                catch { }

                // (2.25) 硬币节奏：早期当硬币能凑出??费关键牌 + 1费随从?濇垨鈥滅?币点地标”时，优先交?
                // 目的：避免被 0 费咒?发现链占?作导致?握币?杞??，错过关键节奏回合?
                try
                {
                    if (!lethalThisTurn && board.Hand != null && board.Hand.Count > 0)
                    {
                        bool hasCoinLocal = false;
                        try { hasCoinLocal = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinLocal = false; }

                        if (hasCoinLocal && handCount <= 8) // 鎶?：避免接近爆牌时乱交?
                        {
                            // 鎶?：如果上层已经?氳繃 ComboSet 鏄庣‘指定“?甯?>X”，这里不再覆盖
                            bool comboAlreadySet = false;
                            try { comboAlreadySet = (p.ComboModifier != null); } catch { comboAlreadySet = false; }

                            if (!comboAlreadySet)
                            {
                                var coinCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.GAME_005);

                                // E) 手里只剩硬币：直接交币并重算，避免阻塞“空手地标/刀”逻辑。
                                if (coinCard != null && board.Hand.Count == 1)
                                {
                                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(9999)); } catch { }
                                    try { p.CastSpellsModifiers.AddOrUpdate(coinCard.Id, new Modifier(-9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(coinCard.Id, new Modifier(9999)); } catch { }
                                    p.ForceResimulation = true;
                                    AddLog("[全局硬规则硬币] 手牌仅硬币 => 强制先交币并重算，避免阻塞地标/时空之爪逻辑");
                                }
                                else
                                {
                                    // C) 极窄ｅ：手牌仅“?甯?魂火”时，先?再魂火??
                                    // 目的：避免魂火把硬币当作随机弃牌?（交币后手牌只剩魂火，魂火不再弃牌）?
                                    // 注：魂火本身仍被 PlayOrder 永远后置；该 ComboSet 只影响?币必』先交”??
                                    try
                                    {
                                        if (coinCard != null && board.Hand.Count == 2 && board.HasCardInHand(Card.Cards.EX1_308))
                                        {
                                            var soulfireCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.EX1_308);
                                            if (soulfireCard != null)
                                            {
                                                // 关键：这里不?ForceResimulation，否则交币后会重新?濊?导致魂火『序被打断?
                                                p.ComboModifier = new ComboSet(coinCard.Id, soulfireCard.Id);
                                                p.ForceResimulation = false;
                                                AddLog("[全局硬规则硬币] 手牌仅硬币+魂火 => 硬币->魂火(ComboSet)，避免魂火弃到硬币");
                                            }
                                        }
                                    }
                                    catch { }

                                    // D) 极窄ｅ緞锛氭墜鐗?=2 且场上窟穴可点时，优先交币??
                                    // 目的：避免点窟穴时把硬币弃掉/或握币?转（手牌很少时交币几乎无负面）??
                                    try
                                    {
                                        if (coinCard != null && canClickCave && board.Hand.Count <= 2)
                                        {
                                            try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-9999)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(9999)); } catch { }
                                            try { p.CastSpellsModifiers.AddOrUpdate(coinCard.Id, new Modifier(-9999)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(coinCard.Id, new Modifier(9999)); } catch { }
                                            p.ForceResimulation = false;
                                            AddLog("[全局硬规则硬币] 手牌<=2且场上窟穴可点=> 优先交币(9999)避免窟穴弃硬币；交币后不重算");
                                        }
                                    }
                                    catch { }

                                    // A) 2费回合：硬币??超光子(2) + 1费随从?同回合打出（最典型节奏：打?清场后立刻站场）
                                    bool canDoTwoPlusOne = false;
                                    bool hasPlayablePhoton = false;
                                    bool hasPlayableOneCostMinion = false;
                                    try
                                    {
                                        hasPlayablePhoton = board.Hand.Any(h => h != null && h.Template != null
                                            && h.Template.Id == Card.Cards.TIME_027
                                            && h.CurrentCost <= board.ManaAvailable);

                                        // 仅挑“明确的节奏1费随从?，避免硬币只为随便下杂?
                                        var oneCostTempoIds = new HashSet<Card.Cards>
                                        {
                                            Card.Cards.GDB_333, // 太空海盗
                                            Card.Cards.TOY_518, // 宝藏经销?
                                            Card.Cards.LOOT_014 // 狗头人图?理员
                                        };
                                        hasPlayableOneCostMinion = board.Hand.Any(h => h != null && h.Template != null
                                            && h.Type == Card.CType.MINION
                                            && oneCostTempoIds.Contains(h.Template.Id)
                                            && h.CurrentCost == 1
                                            && h.CurrentCost <= board.ManaAvailable + 1);

                                        canDoTwoPlusOne = (board.ManaAvailable == 2) && hasPlayablePhoton && hasPlayableOneCostMinion;
                                    }
                                    catch { canDoTwoPlusOne = false; hasPlayablePhoton = false; hasPlayableOneCostMinion = false; }

                                    if (canDoTwoPlusOne && coinCard != null)
                                    {
                                        try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-9999)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(9999)); } catch { }
                                        try { p.CastSpellsModifiers.AddOrUpdate(coinCard.Id, new Modifier(-9999)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(coinCard.Id, new Modifier(9999)); } catch { }

                                        // 进一?导后续『序：超光子优先，其次1费随?
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9800)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(9700)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(9700)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9700)); } catch { }

                                        p.ForceResimulation = false;
                                        AddLog("[全局硬规则硬币] 2费可凑出超光子(2)+节奏1费 => 优先交币(9999)以完成+1节奏");
                                    }

                                    // B) 1费回合：?上有可用地标但差1费无法点，则优先级凑够点地标费?
                                    // 注：这里不强行绑定?滀氦甯?>点地标?濈殑 ComboSet（点地标不是手牌），只做强优先，不强制重算??
                                    bool coinForCaveClick = false;
                                    try
                                    {
                                        // 经验ｅ：点窟穴通常?瑕?费；当可点且当前?费时，交币可?刻点?
                                        coinForCaveClick = canClickCave && board.ManaAvailable == 1;
                                    }
                                    catch { coinForCaveClick = false; }

                                    if (coinForCaveClick && coinCard != null)
                                    {
                                        try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-9999)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(9999)); } catch { }
                                        try { p.CastSpellsModifiers.AddOrUpdate(coinCard.Id, new Modifier(-9999)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(coinCard.Id, new Modifier(9999)); } catch { }
                                        p.ForceResimulation = false;
                                        AddLog("[全局硬规则硬币] 1费且场上可点窟穴 => 优先级凑够点地标费用；交币后不重算");
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                // (2.5) 提刀优先于拍地标/墓：
                // 命中“提刀弃牌收益窗口”（如最高费被弃组件/古尔丹之手）时，优先装备时空之爪（必要时可跳币），
                // 避免先拍窟穴或咒怨之墓；不再仅限“手里有恐怖海盗”。
                try
                {
                    if (!lethalThisTurn && board.Hand != null)
                    {
                        bool hasPirate = false;
                        int dreadCorsairCount = 0;
                        bool hasClaw = false;
                        bool hasCave = false;
                        bool hasTomb = false;
                        bool hasHandOfGuldan = false;
                        bool guldanAsDiscardComponentNow = false;
                        bool clawDiscardWindow = false;
                        try
                        {
                            dreadCorsairCount = CountDreadCorsairInHand(board);
                            hasPirate = dreadCorsairCount > 0;
                            hasClaw = board.HasCardInHand(Card.Cards.END_016);
                            hasCave = board.HasCardInHand(Card.Cards.WON_103);
                            hasTomb = board.HasCardInHand(Card.Cards.TLC_451);
                            hasHandOfGuldan = board.HasCardInHand(Card.Cards.BT_300);
                            // 对齐“被弃组件”口径：手里有古手 != 古手可作为当前提刀弃牌收益来源。
                            // 例如手牌过多时，古手会被排除，不应触发“提刀窗口”。
                            try
                            {
                                var discardSetForClawWindow = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                                guldanAsDiscardComponentNow = hasHandOfGuldan && discardSetForClawWindow.Contains(Card.Cards.BT_300);
                            }
                            catch { guldanAsDiscardComponentNow = false; }
                            clawDiscardWindow = hasDiscardComponentAtMaxForWeapon || guldanAsDiscardComponentNow;
                        }
                        catch
                        {
                            hasPirate = false;
                            dreadCorsairCount = 0;
                            hasClaw = false;
                            hasCave = false;
                            hasTomb = false;
                            hasHandOfGuldan = false;
                            guldanAsDiscardComponentNow = false;
                            clawDiscardWindow = false;
                        }

                        bool hasEquippedWeapon = board.WeaponFriend != null
                            && board.WeaponFriend.Template != null
                            && board.WeaponFriend.CurrentDurability > 0;

                        if (hasHandOfGuldan && !guldanAsDiscardComponentNow)
                        {
                            AddLog("[全局硬规则-提刀] 手里有古尔丹之手，但当前不计入被弃组件(手牌过多/武器状态限制) => 不以古手触发提刀窗口");
                        }

                        bool shouldYieldClawToSketchGlobal = false;
                        int barrageRemainForGlobalYield = 0;
                        Card sketchForGlobalYield = null;
                        try
                        {
                            if (hasClaw && !hasEquippedWeapon && (hasPirate || clawDiscardWindow))
                            {
                                // 非海盗提刀窗口下，若速写可打且牌库仍有灵魂弹幕，优先速写再考虑提刀。
                                if (!hasPirate)
                                {
                                    sketchForGlobalYield = board.Hand.FirstOrDefault(x => x != null
                                        && x.Template != null
                                        && x.Template.Id == Card.Cards.TOY_916
                                        && x.CurrentCost <= board.ManaAvailable);
                                    if (sketchForGlobalYield != null && (board.Hand != null ? board.Hand.Count : 0) <= 9)
                                    {
                                        barrageRemainForGlobalYield = GetRemainingBarrageCountInDeck(board);
                                        shouldYieldClawToSketchGlobal = barrageRemainForGlobalYield > 0;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            shouldYieldClawToSketchGlobal = false;
                            barrageRemainForGlobalYield = 0;
                            sketchForGlobalYield = null;
                        }

                        if (shouldYieldClawToSketchGlobal)
                        {
                            try { p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-9999)); } catch { }
                            try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-1800)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(9850)); } catch { }
                            try
                            {
                                if (sketchForGlobalYield != null)
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(sketchForGlobalYield.Id, new Modifier(-1800)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(sketchForGlobalYield.Id, new Modifier(9850)); } catch { }
                                }
                            }
                            catch { }
                            p.ForceResimulation = true;
                            AddLog("[全局硬规则-提刀让位速写] 牌库有灵魂弹幕(est=" + barrageRemainForGlobalYield
                                + ")且速写可打 => 暂不提刀，先速写(-1800/9850)");
                        }
                        else if (hasClaw && !hasEquippedWeapon && (hasPirate || clawDiscardWindow))
                        {
                            var claw = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.END_016);
                            var cave = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.WON_103);
                            var tomb = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.TLC_451);
                            bool emergencyAllowTombInEarlyBoardGapGlobal = false;
                            string emergencyTombReasonGlobal = null;
                            try
                            {
                                emergencyAllowTombInEarlyBoardGapGlobal = ShouldEmergencyAllowTombInEarlyBoardGap(
                                    board, tomb, out emergencyTombReasonGlobal);
                            }
                            catch
                            {
                                emergencyAllowTombInEarlyBoardGapGlobal = false;
                                emergencyTombReasonGlobal = null;
                            }
                            bool shouldYieldClawToPlayableTomb = false;
                            bool blockedTombYieldByTurnGate = false;
                            try
                            {
                                bool hasCoinForTombYield = false;
                                try { hasCoinForTombYield = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinForTombYield = false; }
                                bool canUseTombByTurnGateForYield = board.MaxMana >= 3 || (board.MaxMana >= 2 && hasCoinForTombYield);
                                blockedTombYieldByTurnGate = tomb != null
                                    && tomb.CurrentCost <= board.ManaAvailable
                                    && board.ManaAvailable >= 2
                                    && !canUseTombByTurnGateForYield
                                    && !emergencyAllowTombInEarlyBoardGapGlobal;
                                shouldYieldClawToPlayableTomb = tomb != null
                                    && tomb.CurrentCost <= board.ManaAvailable
                                    && ((board.ManaAvailable >= 2 && canUseTombByTurnGateForYield)
                                        || emergencyAllowTombInEarlyBoardGapGlobal);
                            }
                            catch { shouldYieldClawToPlayableTomb = false; blockedTombYieldByTurnGate = false; }

                            if (blockedTombYieldByTurnGate)
                            {
                                AddLog("[全局硬规则-提刀让位咒怨之墓] MaxMana=" + board.MaxMana + " 且无硬币 => 不让位给墓");
                            }
                            else if (emergencyAllowTombInEarlyBoardGapGlobal && tomb != null && tomb.CurrentCost <= board.ManaAvailable)
                            {
                                AddLog("[全局硬规则-提刀让位咒怨之墓危机放行] " + (string.IsNullOrEmpty(emergencyTombReasonGlobal) ? "前期场面劣势" : emergencyTombReasonGlobal) + " => 本回合允许先墓，不被提刀覆盖");
                            }

                            bool shouldDelayTombForCoyoteMerchantLine = false;
                            string coyoteMerchantDelayReason = null;
                            try
                            {
                                bool hasCoinForTombYieldCheck = false;
                                try { hasCoinForTombYieldCheck = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinForTombYieldCheck = false; }
                                int effectiveManaForTombYieldCheck = board.ManaAvailable + (hasCoinForTombYieldCheck ? 1 : 0);
                                shouldDelayTombForCoyoteMerchantLine = CanStabilizeMerchantBarrageAfterCoyoteFaceDiscount(
                                    board,
                                    effectiveManaForTombYieldCheck,
                                    out coyoteMerchantDelayReason);
                            }
                            catch
                            {
                                shouldDelayTombForCoyoteMerchantLine = false;
                                coyoteMerchantDelayReason = null;
                            }

                            if (shouldYieldClawToPlayableTomb && shouldDelayTombForCoyoteMerchantLine && !emergencyAllowTombInEarlyBoardGapGlobal)
                            {
                                shouldYieldClawToPlayableTomb = false;
                                AddLog("[全局硬规则-提刀让位咒怨之墓] " + coyoteMerchantDelayReason
                                    + " => 墓让位，不先墓");
                            }

                            bool canEquipClawNow = false;
                            bool needCoinForClaw = false;
                            try
                            {
                                if (claw != null)
                                {
                                    canEquipClawNow = claw.CurrentCost <= board.ManaAvailable;
                                    if (!canEquipClawNow)
                                    {
                                        bool coin = false;
                                        try { coin = board.HasCardInHand(Card.Cards.GAME_005); } catch { coin = false; }
                                        if (coin && claw.CurrentCost <= board.ManaAvailable + 1)
                                        {
                                            canEquipClawNow = true;
                                            needCoinForClaw = true;
                                        }
                                    }
                                }
                            }
                            catch { canEquipClawNow = false; needCoinForClaw = false; }

                            bool keepTombBeforeClawForSoulfirePrep = false;
                            string tombSoulfirePrepReason = null;
                            bool blockTombPrepByImmediateClawWindow = false;
                            try
                            {
                                Card prepCardForSoulfire = null;
                                int prepDiscardCountForSoulfire = 0;
                                int prepTotalCountForSoulfire = 0;
                                string prepReasonForSoulfire = null;
                                bool hitSoulfirePrepWindow =
                                    shouldYieldClawToPlayableTomb
                                    && TryGetLowCostPrepForSoulfireBarrageWindow(
                                        board,
                                        out prepCardForSoulfire,
                                        out prepDiscardCountForSoulfire,
                                        out prepTotalCountForSoulfire,
                                        out prepReasonForSoulfire);

                                if (hitSoulfirePrepWindow && prepCardForSoulfire != null && prepCardForSoulfire.Template != null)
                                {
                                    try
                                    {
                                        blockTombPrepByImmediateClawWindow = ShouldBlockSoulfirePrepForImmediateClawWindow(
                                            board,
                                            prepCardForSoulfire,
                                            lethalThisTurn,
                                            out tombSoulfirePrepReason);
                                    }
                                    catch
                                    {
                                        blockTombPrepByImmediateClawWindow = false;
                                    }

                                    keepTombBeforeClawForSoulfirePrep =
                                        prepCardForSoulfire.Template.Id == Card.Cards.TLC_451
                                        && !blockTombPrepByImmediateClawWindow;

                                    if (!blockTombPrepByImmediateClawWindow)
                                    {
                                        tombSoulfirePrepReason = prepReasonForSoulfire;
                                    }
                                }
                            }
                            catch
                            {
                                keepTombBeforeClawForSoulfirePrep = false;
                                tombSoulfirePrepReason = null;
                                blockTombPrepByImmediateClawWindow = false;
                            }

                            if (shouldYieldClawToPlayableTomb && clawDiscardWindow && canEquipClawNow
                                && !keepTombBeforeClawForSoulfirePrep
                                && !emergencyAllowTombInEarlyBoardGapGlobal)
                            {
                                shouldYieldClawToPlayableTomb = false;
                                AddLog("[全局硬规则-提刀让位咒怨之墓] "
                                    + (blockTombPrepByImmediateClawWindow
                                        ? "先手提纯=咒怨之墓但本回合命中提刀窗口且可提刀 => 墓不覆盖刀，优先提刀"
                                        : "命中提刀窗口且本回合可提刀 => 墓不覆盖刀，优先提刀"));
                            }
                            else if (shouldYieldClawToPlayableTomb && clawDiscardWindow && canEquipClawNow && keepTombBeforeClawForSoulfirePrep)
                            {
                                AddLog("[全局硬规则-提刀让位咒怨之墓] 命中先墓后魂火窗口("
                                    + (string.IsNullOrEmpty(tombSoulfirePrepReason) ? "提纯弃弹幕" : tombSoulfirePrepReason)
                                    + ") => 放行先墓，不让提刀覆盖");
                            }

                            if (shouldYieldClawToPlayableTomb)
                            {
                                try { p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-999)); } catch { }
                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-2200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(9850)); } catch { }
                                try { if (tomb != null) p.CastSpellsModifiers.AddOrUpdate(tomb.Id, new Modifier(-2200)); } catch { }
                                try { if (tomb != null) p.PlayOrderModifiers.AddOrUpdate(tomb.Id, new Modifier(9850)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(1200)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(1200)); } catch { }
                                p.ForceResimulation = true;
                                AddLog("[全局硬规则-提刀让位咒怨之墓] 墓本回合可用(Mana=" + board.ManaAvailable + ") => 暂不提刀，先墓再连段");
                            }
                            else if (claw != null && canEquipClawNow)
                            {
                                // 提刀窗口下，先锁分流和常见抢先低费动作，避免再次丢失提刀回合。
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }

                                if (hasCave)
                                {
                                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-999)); } catch { }
                                    try { if (cave != null) p.LocationsModifiers.AddOrUpdate(cave.Id, new Modifier(9999)); } catch { }
                                    try { if (cave != null) p.PlayOrderModifiers.AddOrUpdate(cave.Id, new Modifier(-999)); } catch { }
                                }

                                if (hasTomb && !emergencyAllowTombInEarlyBoardGapGlobal)
                                {
                                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-999)); } catch { }
                                    try { if (tomb != null) p.CastSpellsModifiers.AddOrUpdate(tomb.Template.Id, new Modifier(9999)); } catch { }
                                    try { if (tomb != null) p.PlayOrderModifiers.AddOrUpdate(tomb.Template.Id, new Modifier(-999)); } catch { }
                                }
                                else if (hasTomb && emergencyAllowTombInEarlyBoardGapGlobal)
                                {
                                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-2200)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(9850)); } catch { }
                                    try { if (tomb != null) p.CastSpellsModifiers.AddOrUpdate(tomb.Id, new Modifier(-2200)); } catch { }
                                    try { if (tomb != null) p.PlayOrderModifiers.AddOrUpdate(tomb.Id, new Modifier(9850)); } catch { }
                                    AddLog("[全局硬规则-提刀让位咒怨之墓危机放行] 前期场面劣势窗口 => 放行先墓，再重算");
                                }

                                // 提刀窗口下，压后会抢费的非核心动作，避免再次出现“先下随从后过提刀窗口”
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(9999)); } catch { } // 乐器技师
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9999)); } catch { } // 狗头人图书管理员
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(9999)); } catch { } // 栉龙
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(9999)); } catch { } // 狡诈的郊狼
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(9999)); } catch { } // 狂暴邪翼蝠
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOD_032, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(9999)); } catch { } // 空降歹徒
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(9999)); } catch { } // 太空海盗
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(-9999)); } catch { }

                                p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-650));
                                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9300));

                                if (needCoinForClaw)
                                {
                                    var coinCard = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.GAME_005);
                                    if (coinCard != null)
                                    {
                                        p.ComboModifier = new ComboSet(coinCard.Id, claw.Id);
                                        p.ForceResimulation = false;
                                        AddLog("[全局硬规则-提刀] 命中提刀窗口(" + (hasPirate ? ("恐怖海盗x" + dreadCorsairCount) : "非恐怖海盗-弃牌收益") + ")+需跳币提刀 => 硬币->提刀(ComboSet)，并压住窟穴/墓/抢费随从(9999)");
                                    }
                                    else
                                    {
                                        p.ForceResimulation = true;
                                        AddLog("[全局硬规则-提刀] 命中提刀窗口(" + (hasPirate ? ("恐怖海盗x" + dreadCorsairCount) : "非恐怖海盗-弃牌收益") + ")+可跳币提刀 => 强推提刀(-650/9300)，并压住窟穴/墓/抢费随从(9999)");
                                    }
                                }
                                else
                                {
                                    p.ForceResimulation = true;
                                    AddLog("[全局硬规则-提刀] 命中提刀窗口(" + (hasPirate ? ("恐怖海盗x" + dreadCorsairCount) : "非恐怖海盗-弃牌收益") + ") => 提刀优先于窟穴/墓(-650/9300)，并压住窟穴/墓/抢费随从(9999)");
                                }
                            }
                        }
                    }
                }
                catch { }

                // (3) 灵魂之火ｅ收口（必须放??滃叏灞?临时牌提权?之后，防止被临时牌覆盖?
                // 统一规则?
                // - 魂火 PlayOrder 永远?鍚庯紱
                // - 仅在“斩?回合”或“除魂火/硬币外全是被弃组件=时允许使用?
                try
                {
                    if (board.Hand != null && board.Hand.Count > 0 && board.HasCardInHand(Card.Cards.EX1_308))
                    {
                        List<Card> soulfireCards = null;
                        try
                        {
                            soulfireCards = board.Hand
                                .Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.EX1_308)
                                .ToList();
                        }
                        catch { soulfireCards = new List<Card>(); }

                        bool allNonSoulfireAreDiscardComponents = false;
                        int nonSoulfireCountForRule = 0;
                        int discardNonSoulfireCountForRule = 0;
                        bool onlyOneSoulfireInHandGlobal = false;
                        try
                        {
                            var nonSoulfireNonCoin = GetHandCardsForDiscardLogic(board, true)
                                .Where(c => c != null && c.Template != null
                                    && c.Template.Id != Card.Cards.EX1_308)
                                .ToList();

                            nonSoulfireCountForRule = nonSoulfireNonCoin.Count;
                            if (nonSoulfireCountForRule > 0 && discardComponents != null && discardComponents.Count > 0)
                            {
                                discardNonSoulfireCountForRule = nonSoulfireNonCoin.Count(c => discardComponents.Contains(c.Template.Id));
                                allNonSoulfireAreDiscardComponents = discardNonSoulfireCountForRule == nonSoulfireCountForRule;
                            }

                            int soulfireCountForRule = board.Hand
                                .Count(c => c != null && c.Template != null && IsSoulfireCardVariant(c));
                            onlyOneSoulfireInHandGlobal = soulfireCountForRule == 1;
                        }
                        catch { allNonSoulfireAreDiscardComponents = false; onlyOneSoulfireInHandGlobal = false; }

                        // 魂火顺序：永远最后（模板 + 实例）??
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-9999)); } catch { }
                        foreach (var c in soulfireCards)
                        {
                            if (c == null) continue;
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                        }

                        bool allowSoulfireByStarterOverride = false;
                        bool allowSoulfireByNoStarterHighDiscardGlobal = false;
                        bool allowSoulfireByEmergencyDefenseGlobal = false;
                        bool preferSoulfireFaceByEmergencyGlobal = false;
                        bool allowSoulfireByLifestealDefenseGlobal = false;
                        string lifestealDefenseReasonGlobal = null;
                        int lifestealDefenseTargetIdGlobal = 0;
                        bool allowBySingleSoulfireGambleGlobal = false;
                        string singleSoulfireGambleReasonGlobal = null;
                        bool allowByDoubleSoulfireGambleGlobal = false;
                        string doubleSoulfireGambleReasonGlobal = null;
                        string emergencyDefenseReasonGlobal = null;
                        int emergencyDefenseTargetIdGlobal = 0;
                        int emergencyEnemyAttackGlobal = 0;
                        int emergencyMyHpGlobal = 0;
                        int starterOverrideDiscardCountGlobal = 0;
                        int starterOverrideTotalCountGlobal = 0;
                        string starterOverrideReasonGlobal = null;
                        int noStarterHighDiscardCountGlobal = 0;
                        int noStarterHighDiscardTotalGlobal = 0;
                        string noStarterHighDiscardReasonGlobal = null;
                        try
                        {
                            var discardSetForSoulfireGlobal = discardComponents != null && discardComponents.Count > 0
                                ? new HashSet<Card.Cards>(discardComponents)
                                : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                            allowSoulfireByStarterOverride = ShouldAllowSoulfireStarterOverride(board, discardSetForSoulfireGlobal, lethalThisTurn,
                                out starterOverrideReasonGlobal, out starterOverrideDiscardCountGlobal, out starterOverrideTotalCountGlobal);
                            allowSoulfireByNoStarterHighDiscardGlobal = ShouldAllowSoulfireNoReadyStarterHighDiscard(
                                board, discardSetForSoulfireGlobal,
                                out noStarterHighDiscardReasonGlobal, out noStarterHighDiscardCountGlobal, out noStarterHighDiscardTotalGlobal);
                            allowSoulfireByEmergencyDefenseGlobal = ShouldAllowSoulfireEmergencyDefense(board,
                                out emergencyDefenseReasonGlobal, out emergencyDefenseTargetIdGlobal, out emergencyEnemyAttackGlobal, out emergencyMyHpGlobal);
                            if (allowSoulfireByEmergencyDefenseGlobal)
                            {
                                preferSoulfireFaceByEmergencyGlobal = ShouldSoulfireEmergencyPreferFace(
                                    board, emergencyEnemyAttackGlobal, emergencyMyHpGlobal);
                            }
                            allowSoulfireByLifestealDefenseGlobal = ShouldAllowSoulfireVsLifestealThreatCompat(board,
                                out lifestealDefenseReasonGlobal, out lifestealDefenseTargetIdGlobal);
                            allowBySingleSoulfireGambleGlobal = ShouldAllInSingleSoulfireDiscardLethalGamble(board, lethalThisTurn, out singleSoulfireGambleReasonGlobal);
                            allowByDoubleSoulfireGambleGlobal = ShouldAllInDoubleSoulfireLethalGamble(board, lethalThisTurn, out doubleSoulfireGambleReasonGlobal);
                        }
                        catch
                        {
                            allowSoulfireByStarterOverride = false;
                            allowSoulfireByNoStarterHighDiscardGlobal = false;
                            allowSoulfireByEmergencyDefenseGlobal = false;
                            preferSoulfireFaceByEmergencyGlobal = false;
                            allowSoulfireByLifestealDefenseGlobal = false;
                            lifestealDefenseReasonGlobal = null;
                            lifestealDefenseTargetIdGlobal = 0;
                            allowBySingleSoulfireGambleGlobal = false;
                            singleSoulfireGambleReasonGlobal = null;
                            allowByDoubleSoulfireGambleGlobal = false;
                            doubleSoulfireGambleReasonGlobal = null;
                            emergencyDefenseReasonGlobal = null;
                            emergencyDefenseTargetIdGlobal = 0;
                            emergencyEnemyAttackGlobal = 0;
                            emergencyMyHpGlobal = 0;
                            starterOverrideDiscardCountGlobal = 0;
                            starterOverrideTotalCountGlobal = 0;
                            starterOverrideReasonGlobal = null;
                            noStarterHighDiscardCountGlobal = 0;
                            noStarterHighDiscardTotalGlobal = 0;
                            noStarterHighDiscardReasonGlobal = null;
                        }

                        if (allowByDoubleSoulfireGambleGlobal)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-9999)); } catch { }
                            try
                            {
                                var dreadPiratesForBlock = GetDreadCorsairCardsInHand(board);
                                foreach (var pirate in dreadPiratesForBlock)
                                {
                                    if (pirate == null || pirate.Template == null) continue;
                                    try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-9999)); } catch { }
                                }
                            }
                            catch { }

                            AddLog("[全局硬规则魂火-双魂火赌博] " + (doubleSoulfireGambleReasonGlobal ?? "命中双魂火斩杀赌博窗口")
                                + " => 暂禁恐怖海盗，保留双魂火窗口");
                        }
                        else if (allowBySingleSoulfireGambleGlobal)
                        {
                            AddLog("[全局硬规则魂火-单魂火赌博] " + (singleSoulfireGambleReasonGlobal ?? "命中单魂火弃牌斩杀窗口")
                                + " => 放行魂火前置尝试");
                        }

                        bool forceSoulfireLastByOtherAction = false;
                        bool forceSoulfireAfterCheapPrepWindow = false;
                        bool forceSoulfireAfterCaveBarrageWindow = false;
                        bool forceSoulfireFirstByAllDiscardWindow = false;
                        bool forceSoulfireFirstByEmergencyDefenseWindow = false;
                        bool forceSoulfireFirstByLifestealDefenseWindow = false;
                        bool forceSoulfireFirstBySingleGambleWindow = false;
                        bool forceSoulfireFirstByStarterNoOtherWindow = false;
                        bool hasPlayablePirateBeforeSoulfire = false;
                        int playablePirateCountBeforeSoulfire = 0;
                        bool pressureEmergencyOverrideGlobal = allowSoulfireByStarterOverride
                            && !string.IsNullOrEmpty(starterOverrideReasonGlobal)
                            && starterOverrideReasonGlobal.StartsWith("场压保命");
                        // 用户口径：魂火在任何局面都必须“最后出”，不允许任何前置例外。
                        try
                        {
                            int boardSlotsUsedNow = GetFriendlyBoardSlotsUsed(board);
                            bool hasBoardSlotNow = boardSlotsUsedNow < 7;

                            Func<Card, bool> isHardDisabledByCastModifierCard = (card) =>
                            {
                                try
                                {
                                    if (card == null || card.Template == null) return true;

                                    Func<RulesSet, Card, bool> isDisabledInRulesSet = (rules, c) =>
                                    {
                                        try
                                        {
                                            if (rules == null || c == null || c.Template == null) return false;

                                            Rule r = null;
                                            try { r = rules.RulesCardIds != null ? rules.RulesCardIds[c.Template.Id] : null; } catch { r = null; }
                                            // 这里按 >=900 视为“本轮不可执行”（覆盖 999/9999 等硬禁用，避免把伪动作算进“其它可打动作”）。
                                            if (r != null && r.CardModifier != null && r.CardModifier.Value >= 900) return true;

                                            try { r = rules.RulesIntIds != null ? rules.RulesIntIds[c.Id] : null; } catch { r = null; }
                                            if (r != null && r.CardModifier != null && r.CardModifier.Value >= 900) return true;

                                            try { r = rules.RulesIntIds != null ? rules.RulesIntIds[(int)c.Template.Id] : null; } catch { r = null; }
                                            if (r != null && r.CardModifier != null && r.CardModifier.Value >= 900) return true;

                                            try { r = rules.RulesIntIds != null ? rules.RulesIntIds[c.Id] : null; } catch { r = null; }
                                            if (r != null && r.CardModifier != null && r.CardModifier.Value >= 900) return true;
                                        }
                                        catch { }
                                        return false;
                                    };

                                    if (isDisabledInRulesSet(p.CastMinionsModifiers, card)) return true;
                                    if (isDisabledInRulesSet(p.CastSpellsModifiers, card)) return true;
                                    if (isDisabledInRulesSet(p.CastWeaponsModifiers, card)) return true;
                                    if (isDisabledInRulesSet(p.LocationsModifiers, card)) return true;
                                }
                                catch { }
                                return false;
                            };

                            var playablePirates = board.Hand
                                .Where(c => c != null
                                    && c.Template != null
                                    && c.Template.Id != Card.Cards.EX1_308
                                    && c.Template.Id != Card.Cards.GAME_005
                                    && c.Type == Card.CType.MINION
                                    && c.IsRace(Card.CRace.PIRATE)
                                    && c.CurrentCost <= board.ManaAvailable
                                    && hasBoardSlotNow)
                                .ToList();

                            if (allowByDoubleSoulfireGambleGlobal && playablePirates.Count > 0)
                            {
                                var dreadPlayablePirates = playablePirates.Where(IsDreadCorsairCard).ToList();
                                foreach (var pirate in dreadPlayablePirates)
                                {
                                    if (pirate == null || pirate.Template == null) continue;
                                    try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-9999)); } catch { }
                                }

                                playablePirates = playablePirates.Where(c => !IsDreadCorsairCard(c)).ToList();
                            }

                            hasPlayablePirateBeforeSoulfire = playablePirates.Count > 0;
                            playablePirateCountBeforeSoulfire = playablePirates.Count;

                            if (hasPlayablePirateBeforeSoulfire)
                            {
                                foreach (var pirate in playablePirates)
                                {
                                    if (pirate == null || pirate.Template == null) continue;
                                    try { p.CastMinionsModifiers.AddOrUpdate(pirate.Template.Id, new Modifier(-2600)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(pirate.Template.Id, new Modifier(9300)); } catch { }
                                    try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(-2600)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(9300)); } catch { }
                                }
                            }

                            bool hasOtherPlayableNonSoulfireAction = board.Hand.Any(c => c != null
                                && c.Template != null
                                && c.Template.Id != Card.Cards.EX1_308
                                && c.Template.Id != Card.Cards.GAME_005
                                && c.CurrentCost <= board.ManaAvailable
                                && (hasPlayablePirateBeforeSoulfire && c.Type == Card.CType.MINION && c.IsRace(Card.CRace.PIRATE)
                                    || (!isHardDisabledByCastModifierCard(c)
                                        && (c.Type != Card.CType.MINION || hasBoardSlotNow))));

                            forceSoulfireLastByOtherAction = hasOtherPlayableNonSoulfireAction;

                            bool canPlayPhotonBeforeSoulfire = false;
                            try
                            {
                                var photonNow = board.Hand
                                    .FirstOrDefault(c => c != null
                                        && c.Template != null
                                        && c.Template.Id == Card.Cards.TIME_027
                                        && c.CurrentCost <= board.ManaAvailable);
                                canPlayPhotonBeforeSoulfire = photonNow != null && !isHardDisabledByCastModifierCard(photonNow);
                            }
                            catch { canPlayPhotonBeforeSoulfire = false; }

                            if (!lethalThisTurn && canPlayPhotonBeforeSoulfire)
                            {
                                forceSoulfireLastByOtherAction = true;
                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-2200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9600)); } catch { }
                                AddLog("[全局硬规则魂火-让位超光子] 非斩杀且超光子可打 => 魂火后置(Cast=8000,Order=-9999)，先打超光子");
                            }

                            // 终局护栏：若商贩可稳定弃到灵魂弹幕，魂火必须后置，避免打断商贩连段。
                            bool stableMerchantBarrageNow = false;
                            Card merchantNow = null;
                            try
                            {
                                bool hasBoardSlotNowForMerchant = hasBoardSlotNow;
                                merchantNow = board.Hand
                                    .FirstOrDefault(c => c != null && c.Template != null
                                        && c.Template.Id == Card.Cards.ULD_163
                                        && c.CurrentCost <= board.ManaAvailable
                                        && hasBoardSlotNowForMerchant);

                                bool hasBarrageNowForMerchant = board.Hand != null
                                    && board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);
                                if (merchantNow != null && hasBarrageNowForMerchant)
                                {
                                    var handWithoutMerchant = GetHandCardsForDiscardLogic(board, true)
                                        .Where(c => c != null && c.Template != null && c.Template.Id != Card.Cards.ULD_163)
                                        .ToList();
                                    if (handWithoutMerchant.Count > 0)
                                    {
                                        int maxCostAfterMerchant = handWithoutMerchant.Max(c => c.CurrentCost);
                                        var highestAfterMerchant = handWithoutMerchant.Where(c => c.CurrentCost == maxCostAfterMerchant).ToList();
                                        stableMerchantBarrageNow = highestAfterMerchant.Count > 0
                                            && highestAfterMerchant.All(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);
                                    }
                                }
                            }
                            catch { stableMerchantBarrageNow = false; merchantNow = null; }

                            if (!lethalThisTurn && stableMerchantBarrageNow)
                            {
                                forceSoulfireLastByOtherAction = true;
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-2200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9600)); } catch { }
                                if (merchantNow != null)
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(merchantNow.Id, new Modifier(-2200)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(merchantNow.Id, new Modifier(9600)); } catch { }
                                }
                                AddLog("[全局硬规则魂火-让位商贩弹幕] 可稳定商贩弃弹幕 => 魂火后置(Cast=8000,Order=-9999)，先打商贩");
                            }

                            // 终局护栏：手上地标可直接拍时，魂火必须让位地标（非斩杀）。
                            bool cavePlayableNowForSoulfire = false;
                            Card caveNowForSoulfire = null;
                            try
                            {
                                caveNowForSoulfire = board.Hand
                                    .FirstOrDefault(c => c != null
                                        && c.Template != null
                                        && c.Template.Id == Card.Cards.WON_103
                                        && c.CurrentCost <= board.ManaAvailable
                                        && hasBoardSlotNow);
                                cavePlayableNowForSoulfire = !lethalThisTurn && caveNowForSoulfire != null;
                            }
                            catch
                            {
                                cavePlayableNowForSoulfire = false;
                                caveNowForSoulfire = null;
                            }

                            bool canClickBoardCaveNowForSoulfire = false;
                            try
                            {
                                canClickBoardCaveNowForSoulfire = board.MinionFriend != null
                                    && board.MinionFriend.Any(c => c != null
                                        && c.Template != null
                                        && c.Template.Id == Card.Cards.WON_103
                                        && GetTag(c, Card.GAME_TAG.EXHAUSTED) == 0);
                            }
                            catch { canClickBoardCaveNowForSoulfire = false; }

                            bool canCastSoulfireNow = soulfireCards != null
                                && soulfireCards.Any(c => c != null && c.CurrentCost <= board.ManaAvailable);
                            int barrageCountInHandForCaveSoulfireGlobal = 0;
                            try
                            {
                                barrageCountInHandForCaveSoulfireGlobal = board.Hand != null
                                    ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534)
                                    : 0;
                            }
                            catch { barrageCountInHandForCaveSoulfireGlobal = 0; }

                            // 新口径：非斩杀且“地标可拍 + 魂火可打 + 手牌含灵魂弹幕”时，锁定先地标后魂火。
                            // 目的：主动尝试用魂火丢弹幕，避免被超光子/分流/低价值动作抢节奏。
                            bool forceCaveThenSoulfireBarrageWindowGlobal = !lethalThisTurn
                                && cavePlayableNowForSoulfire
                                && canCastSoulfireNow
                                && barrageCountInHandForCaveSoulfireGlobal > 0;

                            // 低费提纯后魂火窗口：先打低费非被弃组件，再魂火尝试弃弹幕。
                            Card prepCardForSoulfireGlobal = null;
                            int prepDiscardCountGlobal = 0;
                            int prepTotalCountGlobal = 0;
                            string prepReasonGlobal = null;
                            bool preferSoulfireAfterCheapPrepGlobal = false;
                            try
                            {
                                if (!lethalThisTurn && canCastSoulfireNow)
                                {
                                    preferSoulfireAfterCheapPrepGlobal = TryGetLowCostPrepForSoulfireBarrageWindow(
                                        board,
                                        out prepCardForSoulfireGlobal,
                                        out prepDiscardCountGlobal,
                                        out prepTotalCountGlobal,
                                        out prepReasonGlobal);
                                }
                            }
                            catch
                            {
                                preferSoulfireAfterCheapPrepGlobal = false;
                                prepCardForSoulfireGlobal = null;
                                prepDiscardCountGlobal = 0;
                                prepTotalCountGlobal = 0;
                                prepReasonGlobal = null;
                            }

                            bool blockCheapPrepSoulfireByClawWindowGlobal = false;
                            string blockCheapPrepSoulfireReasonGlobal = null;
                            try
                            {
                                blockCheapPrepSoulfireByClawWindowGlobal =
                                    !lethalThisTurn
                                    && preferSoulfireAfterCheapPrepGlobal
                                    && ShouldBlockSoulfirePrepForImmediateClawWindow(
                                        board,
                                        prepCardForSoulfireGlobal,
                                        lethalThisTurn,
                                        out blockCheapPrepSoulfireReasonGlobal);
                            }
                            catch
                            {
                                blockCheapPrepSoulfireByClawWindowGlobal = false;
                                blockCheapPrepSoulfireReasonGlobal = null;
                            }
                            if (blockCheapPrepSoulfireByClawWindowGlobal)
                            {
                                preferSoulfireAfterCheapPrepGlobal = false;
                                prepCardForSoulfireGlobal = null;
                                prepDiscardCountGlobal = 0;
                                prepTotalCountGlobal = 0;
                                prepReasonGlobal = null;
                                AddLog("[全局硬规则魂火-提纯弃弹幕让位提刀] "
                                    + (string.IsNullOrEmpty(blockCheapPrepSoulfireReasonGlobal)
                                        ? "命中提刀窗口且本回合可提刀"
                                        : blockCheapPrepSoulfireReasonGlobal)
                                    + " => 不启用“先提纯后魂火”，优先提刀");
                            }

                            if (forceCaveThenSoulfireBarrageWindowGlobal)
                            {
                                forceSoulfireAfterCaveBarrageWindow = true;
                                forceSoulfireLastByOtherAction = false;

                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2600)); } catch { }
                                try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-2600)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9990)); } catch { }
                                if (caveNowForSoulfire != null)
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(-2600)); } catch { }
                                    try { p.LocationsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(-2600)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(9990)); } catch { }
                                }

                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-1900)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9750)); } catch { }
                                foreach (var c in soulfireCards)
                                {
                                    if (c == null) continue;
                                    try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-1900)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9750)); } catch { }
                                }

                                // 明确压后超光子与分流，避免连段被抢跑。
                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(2600)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-2600)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(2200)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(2200)); } catch { }

                                AddLog("[全局硬规则-地标魂火弃弹幕] 手牌可拍地标+可用魂火+灵魂弹幕x"
                                    + barrageCountInHandForCaveSoulfireGlobal
                                    + " => 锁定先地标后魂火(地标9990/-2600,魂火9750/-1900)，并压后超光子/分流");
                            }
                            else if (preferSoulfireAfterCheapPrepGlobal && prepCardForSoulfireGlobal != null && prepCardForSoulfireGlobal.Template != null)
                            {
                                forceSoulfireAfterCheapPrepWindow = true;
                                forceSoulfireLastByOtherAction = false;

                                // 先低费提纯（如狗头人/栉龙），再魂火。
                                try
                                {
                                    if (prepCardForSoulfireGlobal.Type == Card.CType.MINION)
                                    {
                                        p.CastMinionsModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Template.Id, new Modifier(-2600));
                                        p.CastMinionsModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Id, new Modifier(-2600));
                                    }
                                    else if (prepCardForSoulfireGlobal.Type == Card.CType.SPELL)
                                    {
                                        p.CastSpellsModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Template.Id, new Modifier(-2600));
                                        p.CastSpellsModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Id, new Modifier(-2600));
                                    }
                                    else if (prepCardForSoulfireGlobal.Type == Card.CType.WEAPON)
                                    {
                                        p.CastWeaponsModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Template.Id, new Modifier(-2600));
                                        p.CastWeaponsModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Id, new Modifier(-2600));
                                    }
                                    else
                                    {
                                        p.LocationsModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Template.Id, new Modifier(-2600));
                                        p.LocationsModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Id, new Modifier(-2600));
                                    }
                                    p.PlayOrderModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Template.Id, new Modifier(9800));
                                    p.PlayOrderModifiers.AddOrUpdate(prepCardForSoulfireGlobal.Id, new Modifier(9800));
                                }
                                catch { }

                                // 郊狼后置，避免抢先打断魂火线。
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(2600)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-3600)); } catch { }
                                try
                                {
                                    if (board.Hand != null)
                                    {
                                        foreach (var coy in board.Hand.Where(c => c != null && c.Template != null && IsCoyoteCardVariant(c)))
                                        {
                                            try { p.CastMinionsModifiers.AddOrUpdate(coy.Id, new Modifier(2600)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(coy.Id, new Modifier(-3600)); } catch { }
                                        }
                                    }
                                }
                                catch { }

                                // 低费提纯线下，墓后置避免覆盖顺序。
                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(1600)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-1200)); } catch { }

                                AddLog("[全局硬规则魂火-提纯弃弹幕] " + (prepReasonGlobal ?? ("弃牌密度=" + prepDiscardCountGlobal + "/" + prepTotalCountGlobal))
                                    + " => 先提纯(含灰烬)后魂火(Prep=9800/-2600,Soulfire=9600/-1800,郊狼后置)");
                            }
                            // 用户口径：当“除魂火外全是被弃组件”时，魂火应直接前置，并暂时禁用抽牌手段。
                            else if (allowSoulfireByEmergencyDefenseGlobal && canCastSoulfireNow)
                            {
                                bool emergencyMustFrontSoulfireGlobal = false;
                                try
                                {
                                    emergencyMustFrontSoulfireGlobal = emergencyMyHpGlobal > 0
                                        && (emergencyEnemyAttackGlobal >= emergencyMyHpGlobal || emergencyMyHpGlobal <= 8);
                                }
                                catch { emergencyMustFrontSoulfireGlobal = false; }

                                if (emergencyMustFrontSoulfireGlobal)
                                {
                                    forceSoulfireFirstByEmergencyDefenseWindow = true;
                                    forceSoulfireLastByOtherAction = false;

                                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-4200)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9900)); } catch { }
                                    foreach (var c in soulfireCards)
                                    {
                                        if (c == null) continue;
                                        try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-4200)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9900)); } catch { }
                                    }

                                    try
                                    {
                                        var target = board.MinionEnemy != null
                                            ? board.MinionEnemy.FirstOrDefault(m => m != null && m.Id == emergencyDefenseTargetIdGlobal)
                                            : null;
                                        if (target != null && target.Template != null)
                                        {
                                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(target.Template.Id, new Modifier(9999));
                                        }
                                    }
                                    catch { }

                                    // 危急血线下，避免0费墓/地标等动作覆盖魂火解场时机。
                                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-9999)); } catch { }
                                    if (cavePlayableNowForSoulfire)
                                    {
                                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                        try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                                        if (caveNowForSoulfire != null)
                                        {
                                            try { p.CastMinionsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(9999)); } catch { }
                                            try { p.LocationsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(9999)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(-9999)); } catch { }
                                        }
                                    }
                                    if (canClickBoardCaveNowForSoulfire)
                                    {
                                        try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                                    }

                                    AddLog("[全局硬规则魂火-保命前置] " + (emergencyDefenseReasonGlobal ?? "血线危险")
                                        + " => 先魂火解场(Cast=-4200,Order=9900,target=" + emergencyDefenseTargetIdGlobal
                                        + ",敌攻=" + emergencyEnemyAttackGlobal + ",我血=" + emergencyMyHpGlobal + ")");
                                }
                            }
                            else if (allowSoulfireByLifestealDefenseGlobal && canCastSoulfireNow)
                            {
                                forceSoulfireFirstByLifestealDefenseWindow = true;
                                forceSoulfireLastByOtherAction = false;

                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-4200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9900)); } catch { }
                                foreach (var c in soulfireCards)
                                {
                                    if (c == null) continue;
                                    try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-4200)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9900)); } catch { }
                                }

                                try
                                {
                                    var target = board.MinionEnemy != null
                                        ? board.MinionEnemy.FirstOrDefault(m => m != null && m.Id == lifestealDefenseTargetIdGlobal)
                                        : null;
                                    if (target != null && target.Template != null)
                                    {
                                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(target.Template.Id, new Modifier(9999));
                                    }
                                }
                                catch { }

                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }

                                AddLog("[全局硬规则魂火-吸血威胁前置] " + (lifestealDefenseReasonGlobal ?? "可击杀吸血威胁")
                                    + " => 先魂火解场(Cast=-4200,Order=9900,target=" + lifestealDefenseTargetIdGlobal + ")");
                            }
                            else if (allowBySingleSoulfireGambleGlobal && canCastSoulfireNow)
                            {
                                forceSoulfireFirstBySingleGambleWindow = true;
                                forceSoulfireLastByOtherAction = false;

                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-3600)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9950)); } catch { }
                                foreach (var c in soulfireCards)
                                {
                                    if (c == null) continue;
                                    try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-3600)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9950)); } catch { }
                                }

                                // 赌博斩杀窗口：避免被超光子/过牌动作抢走先手。
                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-9999)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-9999)); } catch { }

                                if (cavePlayableNowForSoulfire)
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                                    if (caveNowForSoulfire != null)
                                    {
                                        try { p.CastMinionsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(9999)); } catch { }
                                        try { p.LocationsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(9999)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(-9999)); } catch { }
                                    }
                                }

                                if (canClickBoardCaveNowForSoulfire)
                                {
                                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                                    try
                                    {
                                        foreach (var caveOnBoard in board.MinionFriend.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103))
                                        {
                                            try { p.LocationsModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(9999)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(-9999)); } catch { }
                                        }
                                    }
                                    catch { }
                                }

                                AddLog("[全局硬规则魂火-单魂火赌博前置] " + (singleSoulfireGambleReasonGlobal ?? "命中单魂火弃牌斩杀窗口")
                                    + " => 先魂火(-3600/9950)，并暂禁超光子/抽牌/地标抢先");
                            }
                            else if (allNonSoulfireAreDiscardComponents && canCastSoulfireNow)
                            {
                                forceSoulfireFirstByAllDiscardWindow = true;
                                forceSoulfireLastByOtherAction = false;

                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-3200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9900)); } catch { }
                                foreach (var c in soulfireCards)
                                {
                                    if (c == null) continue;
                                    try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-3200)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9900)); } catch { }
                                }

                                bool hasCaveActionForSoulfire = cavePlayableNowForSoulfire || canClickBoardCaveNowForSoulfire;
                                if (cavePlayableNowForSoulfire)
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                                    if (caveNowForSoulfire != null)
                                    {
                                        try { p.CastMinionsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(9999)); } catch { }
                                        try { p.LocationsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(9999)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(-9999)); } catch { }
                                    }
                                }

                                if (canClickBoardCaveNowForSoulfire)
                                {
                                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-9999)); } catch { }
                                    try
                                    {
                                        foreach (var caveOnBoard in board.MinionFriend.Where(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103))
                                        {
                                            try { p.LocationsModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(9999)); } catch { }
                                            try { p.PlayOrderModifiers.AddOrUpdate(caveOnBoard.Id, new Modifier(-9999)); } catch { }
                                        }
                                    }
                                    catch { }
                                }

                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-9999)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-9999)); } catch { }

                                AddLog("[全局硬规则魂火-全手被弃前置] 除魂火外全是被弃组件("
                                    + discardNonSoulfireCountForRule + "/" + nonSoulfireCountForRule
                                    + ") => 先魂火，暂禁抽牌手段"
                                    + (hasCaveActionForSoulfire ? "并暂缓地标" : "")
                                    + "(魂火-3200/9900)");
                            }
                            else if (!lethalThisTurn
                                && allowSoulfireByStarterOverride
                                && canCastSoulfireNow
                                && !hasOtherPlayableNonSoulfireAction)
                            {
                                forceSoulfireFirstByStarterNoOtherWindow = true;
                                forceSoulfireLastByOtherAction = false;

                                try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-2600)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9800)); } catch { }
                                foreach (var c in soulfireCards)
                                {
                                    if (c == null) continue;
                                    try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-2600)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9800)); } catch { }
                                }

                                AddLog("[全局硬规则魂火-启动前置] 启动放行且当前无其它可打动作 => 先魂火避免空过(Cast=-2600,Order=9800)");
                            }
                            else if (cavePlayableNowForSoulfire)
                            {
                                forceSoulfireLastByOtherAction = true;
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800)); } catch { }
                                try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1800)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9800)); } catch { }
                                if (caveNowForSoulfire != null)
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(-1800)); } catch { }
                                    try { p.LocationsModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(-1800)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(caveNowForSoulfire.Id, new Modifier(9800)); } catch { }
                                }
                                AddLog("[全局硬规则魂火-让位地标] 手牌地标可拍 => 魂火后置(Cast=8000,Order=-9999)，先拍地标");
                            }
                        }
                        catch
                        {
                            forceSoulfireLastByOtherAction = false;
                            hasPlayablePirateBeforeSoulfire = false;
                            playablePirateCountBeforeSoulfire = 0;
                        }

                        bool allowSoulfireByRule = lethalThisTurn
                            || allNonSoulfireAreDiscardComponents
                            || allowSoulfireByStarterOverride
                            || allowSoulfireByNoStarterHighDiscardGlobal
                            || allowSoulfireByEmergencyDefenseGlobal
                            || allowSoulfireByLifestealDefenseGlobal
                            || allowBySingleSoulfireGambleGlobal
                            || allowByDoubleSoulfireGambleGlobal
                            || forceSoulfireAfterCaveBarrageWindow
                            || forceSoulfireAfterCheapPrepWindow;
                        if (forceSoulfireAfterCaveBarrageWindow
                            || forceSoulfireAfterCheapPrepWindow
                            || forceSoulfireFirstByAllDiscardWindow
                            || forceSoulfireFirstByEmergencyDefenseWindow
                            || forceSoulfireFirstByLifestealDefenseWindow
                            || forceSoulfireFirstBySingleGambleWindow
                            || forceSoulfireFirstByStarterNoOtherWindow)
                        {
                            int soulfireFirstCastVal = forceSoulfireAfterCaveBarrageWindow
                                ? -1900
                                : (forceSoulfireAfterCheapPrepWindow
                                ? -1800
                                : (forceSoulfireFirstByEmergencyDefenseWindow
                                ? -4200
                                : (forceSoulfireFirstByLifestealDefenseWindow
                                ? -4200
                                : (forceSoulfireFirstBySingleGambleWindow
                                    ? -3600
                                    : (forceSoulfireFirstByStarterNoOtherWindow ? -2600 : -3200)))));
                            int soulfireFirstOrderVal = forceSoulfireAfterCaveBarrageWindow
                                ? 9750
                                : (forceSoulfireAfterCheapPrepWindow
                                ? 9600
                                : (forceSoulfireFirstByEmergencyDefenseWindow
                                ? 9900
                                : (forceSoulfireFirstBySingleGambleWindow
                                ? 9950
                                : (forceSoulfireFirstByStarterNoOtherWindow ? 9800 : 9900))));
                            try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(soulfireFirstCastVal)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(soulfireFirstOrderVal)); } catch { }
                            foreach (var c in soulfireCards)
                            {
                                if (c == null) continue;
                                try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(soulfireFirstCastVal)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(soulfireFirstOrderVal)); } catch { }
                            }
                        }
                        else if (forceSoulfireLastByOtherAction)
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-9999)); } catch { }
                            foreach (var c in soulfireCards)
                            {
                                if (c == null) continue;
                                try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(8000)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                            }

                            AddLog("[全局硬规则魂火-最后] 存在其他可打动作"
                                + (hasPlayablePirateBeforeSoulfire ? "(含海盗x" + playablePirateCountBeforeSoulfire + ")" : "")
                                + " => 暂缓魂火(Cast=8000,Order=-9999)，先打其它牌");
                        }
                        else if (!allowSoulfireByRule)
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-9999)); } catch { }
                            foreach (var c in soulfireCards)
                            {
                                if (c == null) continue;
                                try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                            }

                            AddLog("[全局硬规则魂火] 非斩杀且不满足“除魂火/硬币外全是被弃组件”("
                                + discardNonSoulfireCountForRule + "/" + nonSoulfireCountForRule
                                + ",放宽窗口=" + noStarterHighDiscardCountGlobal + "/" + noStarterHighDiscardTotalGlobal
                                + (onlyOneSoulfireInHandGlobal ? ",单魂火=Y(不单独放行)" : "")
                                + ") => 禁用Cast=9999，Order=-9999");
                        }
                        else
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(8000)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-9999)); } catch { }
                            foreach (var c in soulfireCards)
                            {
                                if (c == null) continue;
                                try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(8000)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                            }

                            AddLog("[全局硬规则魂火] 允许窗口(lethal=" + (lethalThisTurn ? "Y" : "N")
                                + ",全手" + (allNonSoulfireAreDiscardComponents ? "Y" : "N")
                                + ",启动放行=" + (allowSoulfireByStarterOverride ? "Y" : "N")
                                + ",无现成组件放行=" + (allowSoulfireByNoStarterHighDiscardGlobal ? "Y" : "N")
                                + ",保命放行=" + (allowSoulfireByEmergencyDefenseGlobal ? "Y" : "N")
                                + ",吸血放行=" + (allowSoulfireByLifestealDefenseGlobal ? "Y" : "N")
                                + ",单魂火赌博=" + (allowBySingleSoulfireGambleGlobal ? "Y" : "N")
                                + ",双魂火赌博=" + (allowByDoubleSoulfireGambleGlobal ? "Y" : "N")
                                + ",单魂火=" + (onlyOneSoulfireInHandGlobal ? "Y" : "N") + "(不单独放行)"
                                + "," + discardNonSoulfireCountForRule + "/" + nonSoulfireCountForRule
                                + (allowSoulfireByStarterOverride
                                    ? ",启动密度=" + starterOverrideDiscardCountGlobal + "/" + starterOverrideTotalCountGlobal
	                                        + ",原因=" + (starterOverrideReasonGlobal ?? "无")
                                    : "")
                                + (allowSoulfireByNoStarterHighDiscardGlobal
                                    ? ",无现成组件原因=" + (noStarterHighDiscardReasonGlobal ?? "无")
                                        + ",密度=" + noStarterHighDiscardCountGlobal + "/" + noStarterHighDiscardTotalGlobal
                                    : "")
                                + (allowSoulfireByEmergencyDefenseGlobal
                                    ? ",保命原因=" + (emergencyDefenseReasonGlobal ?? "血线危险")
                                        + ",敌攻=" + emergencyEnemyAttackGlobal + ",我血=" + emergencyMyHpGlobal
                                    : "")
                                + (allowSoulfireByLifestealDefenseGlobal
                                    ? ",吸血原因=" + (lifestealDefenseReasonGlobal ?? "可击杀吸血威胁")
                                        + ",target=" + lifestealDefenseTargetIdGlobal
                                    : "")
                                + (allowByDoubleSoulfireGambleGlobal
                                    ? ",赌博原因=" + (doubleSoulfireGambleReasonGlobal ?? "双魂火斩杀窗口")
                                    : "")
                                + (allowBySingleSoulfireGambleGlobal
                                    ? ",单魂火赌博原因=" + (singleSoulfireGambleReasonGlobal ?? "单魂火弃牌斩杀窗口")
                                    : "")
                                + ") => 仅放行使用，仍保持最后出牌(Cast=8000,Order=-9999)");
                        }
                    }
                }
                catch { }
            }
            catch { }
            
            // AI复盘用一行快照：手牌 + 己方随从/武器/敌方随从
            try { LogHandSnapshotOneLine(board); } catch { }
            try { LogBoardSnapshotOneLine(board); } catch { }
            AddLog("================ 决策完成 ================");
            
            // ==============================================
            // 威胁值设置：提高敌方关键随从的?鑳佸??
            // ==============================================
            try
            {
                // ==============================================
                // 投降兜底收口：高置信必死且本回合可分流时，先抽一ｆ翻盘?
                // 说明：放?熬閮?盖，避免被中间规则把分流再次后置?
                // ==============================================
                try
                {
                    if (avoidLifeTapForDiscardCounterplay)
                    {
                        // 反打?紡锛氱?鐢?流，避免吃掉“武?敾鍑?+ 商贩弃牌”的法力窗口?
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }

                        // 鑻?前可下专卖商且最高费包含灵魂弹幕，强?晢费试反打??
                        try
                        {
                            bool highestHasBarrage = false;
                            int maxCostNow = 0;
                            var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                            if (handForDiscardLogic.Count > 0)
                            {
                                maxCostNow = handForDiscardLogic.Max(c => c != null ? c.CurrentCost : 0);
                                highestHasBarrage = handForDiscardLogic.Any(c => c != null && c.Template != null
                                    && c.CurrentCost == maxCostNow
                                    && c.Template.Id == Card.Cards.RLK_534);
                            }

                            if (highestHasBarrage
                                && board.MinionFriend != null && board.MinionFriend.Count < 7
                                && handForDiscardLogic.Count > 0)
                            {
                                var merchants = handForDiscardLogic
                                    .Where(c => c != null && c.Template != null
                                        && c.Template.Id == Card.Cards.ULD_163
                                        && c.CurrentCost <= board.ManaAvailable)
                                    .ToList();

                                if (merchants.Count > 0)
                                {
                                    var preferredMerchant = merchants
                                        .OrderByDescending(c =>
                                        {
                                            int pos = 0;
                                            try { pos = board.Hand.IndexOf(c); } catch { pos = 0; }
                                            bool isTemp = false;
                                            try { isTemp = IsTemporaryCard(board, c); } catch { isTemp = false; }
                                            return (isTemp ? 1000 : 0) + pos;
                                        })
                                        .FirstOrDefault();

                                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-2500)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9800)); } catch { }
                                    if (preferredMerchant != null)
                                    {
                                        try { p.CastMinionsModifiers.AddOrUpdate(preferredMerchant.Id, new Modifier(-2500)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(preferredMerchant.Id, new Modifier(9800)); } catch { }
                                        try { if (p.ComboModifier == null) p.ComboModifier = new ComboSet(preferredMerchant.Id); } catch { }
                                    }

                                    AddLog("[投降-必死] 反打模式：先分流，强推专卖商弃最高费灵魂弹幕(9800/-2500)");
                                }
                            }
                        }
                        catch { }
                    }

                    if (forceLifeTapLastChance)
                    {
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(-9999)); } catch { }
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(-9999)); } catch { }
                        AddLog("[投降-必死] 最后机会模式：锁定本回合先分流(-9999)；若分流后仍高概率致死，则下回合自动投降");
                    }
                }
                catch { }

                // (3.4) 鎸?兜底：已装备时空之爪且最高费有被弃组件时，若本回合无可执行的非武?作，强制先挥?
                // 目的：避免出现?应当挥?瑙?弃牌，却因为其它规则互相抵消导致空过”的情况?
                try
                {
                    bool hasEquippedClawNow = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0;
                    bool heroLikelyCanAttackNow = false;
                    try
                    {
                        heroLikelyCanAttackNow = hasEquippedClawNow
                            && board.HeroFriend != null
                            && !board.HeroFriend.IsFrozen
                            && board.HeroFriend.CountAttack == 0;
                    }
                    catch
                    {
                        heroLikelyCanAttackNow = hasEquippedClawNow
                            && board.HeroFriend != null
                            && !board.HeroFriend.IsFrozen;
                    }

                    if (!lethalThisTurn
                        && hasEquippedClawNow
                        && hasDiscardComponentAtMaxForWeapon
                        && heroLikelyCanAttackNow
                        && !merchantCanSacrifice
                        && !canClickCave
                        && !holdClawForMerchantBarrage)
                    {
                        Func<Card, bool> isHardDisabledByCastModifier = (card) =>
                        {
                            try
                            {
                                if (card == null || card.Template == null) return false;

                                Func<RulesSet, Card.Cards, int, bool> isDisabledInRulesSet = (rules, templateId, instanceId) =>
                                {
                                    try
                                    {
                                        if (rules == null) return false;

                                        Rule r = null;
                                        try { r = rules.RulesCardIds != null ? rules.RulesCardIds[templateId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                        try { r = rules.RulesIntIds != null ? rules.RulesIntIds[(int)templateId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                        try { r = rules.RulesIntIds != null ? rules.RulesIntIds[instanceId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;
                                    }
                                    catch { }
                                    return false;
                                };

                                if (isDisabledInRulesSet(p.CastMinionsModifiers, card.Template.Id, card.Id)) return true;
                                if (isDisabledInRulesSet(p.CastSpellsModifiers, card.Template.Id, card.Id)) return true;
                                if (isDisabledInRulesSet(p.CastWeaponsModifiers, card.Template.Id, card.Id)) return true;
                                if (isDisabledInRulesSet(p.LocationsModifiers, card.Template.Id, card.Id)) return true;
                            }
                            catch { }
                            return false;
                        };

                        bool hasPlayableNonWeaponAction = board.Hand != null && board.Hand.Any(c => c != null
                            && c.Template != null
                            && c.Template.Id != Card.Cards.GAME_005
                            && c.Template.Id != Card.Cards.END_016
                            && c.CurrentCost <= effectiveMana
                            && !isHardDisabledByCastModifier(c));

                        if (!hasPlayableNonWeaponAction)
                        {
                            try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-3200)); } catch { }
                            p.ForceResimulation = true;
                            AddLog("[全局硬规则挥刀兜底] 已装备时空之爪且最高费有被弃组件，且无可执行非武器动作 => 强制优先武器攻击(-3200)");
                        }
                    }
                }
                catch { }

                // ==============================================
                // 分流兜底：可抽一ｄ絾浼氱?杩?费时，强?娊涓?鍙?
                // 说明：SmartBot 默认并不总会把英雄技能当作?必须消耗的?綔鈥濄??
                // 这里做一个保守兜底：当没有明显更好的可用牌时，避免直?EndTurn銆?
                // ==============================================
                try
                {
                    bool skipLifeTapByClawDiscardPlan = false;
                    bool skipLifeTapByClawEquipPlan = false;
                    bool skipLifeTapByClawFaceDiscountPlan = false;
                    bool hasClawDiscardPlanButCannotAttackNow = false;
                    try
                    {
                        bool hasClawEquippedNow = board.WeaponFriend != null
                            && board.WeaponFriend.Template != null
                            && board.WeaponFriend.Template.Id == Card.Cards.END_016
                            && board.WeaponFriend.CurrentDurability > 0;
                        bool heroLikelyCanAttackNow = false;
                        try
                        {
                            heroLikelyCanAttackNow = hasClawEquippedNow
                                && board.HeroFriend != null
                                && !board.HeroFriend.IsFrozen
                                && board.HeroFriend.CountAttack == 0;
                        }
                        catch
                        {
                            heroLikelyCanAttackNow = hasClawEquippedNow
                                && board.HeroFriend != null
                                && !board.HeroFriend.IsFrozen
                                && board.HeroFriend.CanAttack;
                        }

                        bool canEquipClawNowForPlan = false;
                        bool hasPirateForClawPlan = false;
                        try
                        {
                            canEquipClawNowForPlan = !hasClawEquippedNow
                                && board.Hand != null
                                && board.Hand.Any(c => c != null
                                    && c.Template != null
                                    && c.Template.Id == Card.Cards.END_016
                                    && c.CurrentCost <= effectiveMana);
                            hasPirateForClawPlan = CountDreadCorsairInHand(board) > 0
                                || (board.Hand != null && board.Hand.Any(c => c != null
                                    && c.Template != null
                                    && c.Template.Id == Card.Cards.CORE_NEW1_022));
                        }
                        catch
                        {
                            canEquipClawNowForPlan = false;
                            hasPirateForClawPlan = false;
                        }

                        bool hasBarrageForClawPlan = false;
                        try
                        {
                            hasBarrageForClawPlan = board.Hand != null
                                && board.Hand.Any(c => c != null
                                    && c.Template != null
                                    && c.Template.Id == Card.Cards.RLK_534);
                        }
                        catch { hasBarrageForClawPlan = false; }

                        skipLifeTapByClawEquipPlan = canEquipClawNowForPlan
                            && (hasDiscardComponentAtMaxForWeapon || hasPirateForClawPlan || hasBarrageForClawPlan);

                        bool coyoteUnlocksNow = false;
                        bool felwingUnlocksNow = false;
                        try { coyoteUnlocksNow = CanCoyoteUnlockClawDiscardWindow(board, discardComponents); } catch { coyoteUnlocksNow = false; }
                        try { felwingUnlocksNow = CanFelwingUnlockClawDiscardWindow(board, discardComponents); } catch { felwingUnlocksNow = false; }
                        skipLifeTapByClawFaceDiscountPlan = hasClawEquippedNow && (coyoteUnlocksNow || felwingUnlocksNow);

                        skipLifeTapByClawDiscardPlan = (hasClawEquippedNow
                            && hasDiscardComponentAtMaxForWeapon
                            && heroLikelyCanAttackNow)
                            || skipLifeTapByClawFaceDiscountPlan
                            || skipLifeTapByClawEquipPlan;
                        hasClawDiscardPlanButCannotAttackNow = hasClawEquippedNow
                            && hasDiscardComponentAtMaxForWeapon
                            && !heroLikelyCanAttackNow;
                    }
                    catch
                    {
                        skipLifeTapByClawDiscardPlan = false;
                        skipLifeTapByClawEquipPlan = false;
                        skipLifeTapByClawFaceDiscountPlan = false;
                        hasClawDiscardPlanButCannotAttackNow = false;
                    }

                    bool skipLifeTapByHandCaveDiscardPlan = false;
                    try
                    {
                        var discardSetForCaveTap = discardComponents != null && discardComponents.Count > 0
                            ? new HashSet<Card.Cards>(discardComponents)
                            : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                        bool canPlayCaveNow = canPlayCaveFromHand && GetFriendlyBoardSlotsUsed(board) < 7;
                        if (canPlayCaveNow)
                        {
                            int caveDiscardCountForTap = GetHandCardsForDiscardLogic(board).Count(c => c != null && c.Template != null
                                && c.Template.Id != Card.Cards.WON_103
                                && c.Template.Id != Card.Cards.GAME_005
                                && !ShouldIgnoreCardForCaveDiscardRatio(c)
                                && discardSetForCaveTap.Contains(c.Template.Id));

                            Card caveOnly;
                            Card pirateOnly;
                            bool onlyCaveAndSpacePirate = IsOnlyCaveAndSpacePirateHand(board, out caveOnly, out pirateOnly);

                            bool hasSoulfireInHandForCaveOrder = false;
                            try
                            {
                                hasSoulfireInHandForCaveOrder = board.Hand != null
                                    && board.Hand.Any(c => c != null
                                        && c.Template != null
                                        && c.Template.Id == Card.Cards.EX1_308);
                            }
                            catch { hasSoulfireInHandForCaveOrder = false; }

                             skipLifeTapByHandCaveDiscardPlan = caveDiscardCountForTap > 0
                                  || onlyCaveAndSpacePirate
                                  || hasSoulfireInHandForCaveOrder;
                         }
 
                         // 场上可点地标且被弃组件占比>=20%时，也应让位地标（不抽一口）
                         if (!skipLifeTapByHandCaveDiscardPlan && canClickCave)
                         {
                             var handForDiscardTap = GetHandCardsForDiscardLogic(board)
                                 .Where(h => h != null && h.Template != null
                                     && h.Template.Id != Card.Cards.WON_103
                                     && h.Template.Id != Card.Cards.GAME_005
                                     && !ShouldIgnoreCardForCaveDiscardRatio(h))
                                 .ToList();
                             int nonCaveCountTap = handForDiscardTap.Count;
                             int discardCountTap = handForDiscardTap.Count(h => h != null && h.Template != null
                                 && discardSetForCaveTap != null && discardSetForCaveTap.Contains(h.Template.Id));
                             if (nonCaveCountTap > 0 && ((double)discardCountTap / nonCaveCountTap) >= 0.2)
                             {
                                 skipLifeTapByHandCaveDiscardPlan = true;
                             }
                         }
                     }
                     catch { skipLifeTapByHandCaveDiscardPlan = false; }

                    // 娉?：这里不要依?canTapNow（某些情况下 board.Ability 鐨?EXHAUSTED 标签会误?級锛?
                    // 只要本回合还?2 费且不爆牌，就允许?尝试?分流；?际不可用，引擎会自行跳过?
                    // 额外ｅ修正：如果手里?看起来能打的牌”在本策?已被禁用(9999)，则依然视为“无更优?”，应抽?鍙ｃ??
                    // 同时：若本回合可点击窟穴等关?Location，则不要?底覆盖（先执?Location锛夈??
                    bool soulfireAllDiscardMustGoNow = false;
                    try
                    {
                        if (board.Hand != null && board.Hand.Count > 0 && board.HasCardInHand(Card.Cards.EX1_308))
                        {
                            var nonSoulfireNonCoinForTap = GetHandCardsForDiscardLogic(board, true)
                                .Where(c => c != null && c.Template != null && c.Template.Id != Card.Cards.EX1_308)
                                .ToList();

                            int nonSoulfireCountForTap = nonSoulfireNonCoinForTap.Count;
                            int discardNonSoulfireCountForTap = 0;
                            bool allNonSoulfireAreDiscardForTap = false;
                            if (nonSoulfireCountForTap > 0 && discardComponents != null && discardComponents.Count > 0)
                            {
                                discardNonSoulfireCountForTap = nonSoulfireNonCoinForTap.Count(c => discardComponents.Contains(c.Template.Id));
                                allNonSoulfireAreDiscardForTap = discardNonSoulfireCountForTap == nonSoulfireCountForTap;
                            }

                            bool canCastSoulfireNowForTap = board.Hand.Any(c => c != null
                                && c.Template != null
                                && c.Template.Id == Card.Cards.EX1_308
                                && c.CurrentCost <= board.ManaAvailable);

                            soulfireAllDiscardMustGoNow = allNonSoulfireAreDiscardForTap && canCastSoulfireNowForTap;
                        }
                    }
                    catch { soulfireAllDiscardMustGoNow = false; }

                    if (soulfireAllDiscardMustGoNow)
                    {
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(9999)); } catch { }
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(9999)); } catch { }
                        AddLog("[分流兜底-让位魂火] 除魂火外全是被弃组件且魂火可用 => 暂禁抽牌手段，先魂火");
                    }
                    else if (!lethalThisTurn
                        && !avoidLifeTapForDiscardCounterplay
                        && !heroPowerUsedThisTurn
                        && !skipLifeTapByClawDiscardPlan
                        && board.ManaAvailable >= 2
                        && board.Hand != null
                        && board.Hand.Count <= 9
                        && !canClickCave
                        && !skipLifeTapByHandCaveDiscardPlan)
                    {
                        int myHpArmor = 0;
                        try { myHpArmor = (board.HeroFriend != null ? (board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) : 0); } catch { myHpArmor = 0; }

                        // 分流会扣2血锛氳?鐢?=3时都不做分流兜底（保守避免被反杀）??
                        if (myHpArmor <= 3)
                        {
                            // do nothing
                        }
                        else
                        {
                        var weakPlaySet = new HashSet<Card.Cards>
                        {
                            Card.Cards.GAME_005, // 幸运?
                            Card.Cards.EX1_308,  // 灵魂之火
                            Card.Cards.DRG_056   // 空降歹徒
                        };

                        // 细化：已装备时空之爪但本回合不可挥刀，且超光子当前并非高收益时，
                        // 允许“抽一口”优先于超光子，避免 3 费回合打超光子后空过。
                        try
                        {
                            bool hasPhotonPlayableNow = board.Hand != null && board.Hand.Any(h => h != null
                                && h.Template != null
                                && h.Template.Id == Card.Cards.TIME_027
                                && h.CurrentCost <= effectiveMana);

                            bool hasClawEquippedNowForTap = board.WeaponFriend != null
                                && board.WeaponFriend.Template != null
                                && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                && board.WeaponFriend.CurrentDurability > 0;

                            bool heroLikelyCanAttackNowForTap = false;
                            try
                            {
                                heroLikelyCanAttackNowForTap = hasClawEquippedNowForTap
                                    && board.HeroFriend != null
                                    && !board.HeroFriend.IsFrozen
                                    && board.HeroFriend.CountAttack == 0;
                            }
                            catch
                            {
                                heroLikelyCanAttackNowForTap = hasClawEquippedNowForTap
                                    && board.HeroFriend != null
                                    && !board.HeroFriend.IsFrozen;
                            }

                            bool hasEnemyTauntNowForTap = false;
                            try { hasEnemyTauntNowForTap = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt); }
                            catch { hasEnemyTauntNowForTap = false; }

                            int handCountForDiscardLogicTap = 0;
                            try { handCountForDiscardLogicTap = GetHandCardsForDiscardLogic(board).Count; }
                            catch { handCountForDiscardLogicTap = board.Hand != null ? board.Hand.Count : 0; }

                            bool yieldPhotonToTap = !lethalThisTurn
                                && board.ManaAvailable >= 2
                                && board.ManaAvailable <= 3
                                && hasPhotonPlayableNow
                                && hasClawEquippedNowForTap
                                && hasDiscardComponentAtMaxForWeapon
                                && !heroLikelyCanAttackNowForTap
                                && !hasEnemyTauntNowForTap
                                && lowHealthEnemyCount <= 1
                                && discardPayoffCount >= 2
                                && handCountForDiscardLogicTap <= 3
                                && !canClickCave;

                            if (yieldPhotonToTap)
                            {
                                weakPlaySet.Add(Card.Cards.TIME_027);
                                AddLog("[分流兜底-超光子让位] 已装备爪子但本回合不可挥刀，且超光子当前收益低(低血随从<=1,弃牌密度高) => 优先抽一口后重算");
                            }
                        }
                        catch { }

                        Func<Card, bool> isDisabledByCastModifier = (card) =>
                        {
                            try
                            {
                                if (card == null || card.Template == null) return false;
                                var id = card.Template.Id;
                                int instanceId = card.Id;

                                Func<RulesSet, Card.Cards, bool> isDisabledInRulesSet = (rules, cardId) =>
                                {
                                    try
                                    {
                                        if (rules == null) return false;

                                        Rule r = null;
                                        try { r = rules.RulesCardIds != null ? rules.RulesCardIds[cardId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                        try { r = rules.RulesIntIds != null ? rules.RulesIntIds[(int)cardId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                        try { r = rules.RulesIntIds != null ? rules.RulesIntIds[instanceId] : null; } catch { r = null; }
                                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;
                                    }
                                    catch { }
                                    return false;
                                };

                                // 按卡牌类型判定“硬禁用”，避免跨类型修正（如 CastMinions 对武器ID 的防御性写入）误伤可打动作。
                                if (card.Type == Card.CType.MINION) return isDisabledInRulesSet(p.CastMinionsModifiers, id);
                                if (card.Type == Card.CType.SPELL) return isDisabledInRulesSet(p.CastSpellsModifiers, id);
                                if (card.Type == Card.CType.WEAPON) return isDisabledInRulesSet(p.CastWeaponsModifiers, id);
                                return isDisabledInRulesSet(p.LocationsModifiers, id);
                            }
                            catch { }
                            return false;
                        };

                        bool hasPlayableStrongCard = board.Hand.Any(h => h != null && h.Template != null
                            && h.CurrentCost <= effectiveMana
                            && !weakPlaySet.Contains(h.Template.Id)
                            && !isDisabledByCastModifier(h));

                        // 用户口径：狗头人/栉龙优先于分流；可打时禁止分流兜底抢跑。
                        bool hasPlayablePriorityDrawMinion = false;
                        try
                        {
                            hasPlayablePriorityDrawMinion = board.Hand.Any(h => h != null
                                && h.Template != null
                                && h.CurrentCost <= effectiveMana
                                && (h.Template.Id == Card.Cards.LOOT_014 || h.Template.Id == Card.Cards.TLC_603)
                                && !isDisabledByCastModifier(h));
                        }
                        catch { hasPlayablePriorityDrawMinion = false; }

                        if (hasPlayablePriorityDrawMinion)
                        {
                            hasPlayableStrongCard = true;
                            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(2200)); } catch { }
                            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(2200)); } catch { }
                            AddLog("[分流兜底-让位过牌随从] 存在可打狗头人/栉龙 => 不强推抽一口，优先过牌随从");
                        }

                        // 无修正?费回合打到剩2费时，若当前“可打强动作”只剩过牌随从，
                        // 且手里有灵魂弹幕并命中?最高费有被弃组件=，优先先抽?鍙ｅ重算?
                        // 目的：避免连续拍过牌随从导致错过更高收益的启?矾寰勩??
                        bool preferLifeTapBeforeOnlyDrawMinions = false;
                        try
                        {
                            var drawMinionIds = new HashSet<Card.Cards>
                            {
                                Card.Cards.TLC_603, // 栉龙
                                Card.Cards.LOOT_014 // 狗头人图?理员
                            };

                            bool hasPlayableDrawMinion = board.Hand.Any(h => h != null && h.Template != null
                                && h.CurrentCost <= effectiveMana
                                && drawMinionIds.Contains(h.Template.Id)
                                && !isDisabledByCastModifier(h));

                            bool hasPlayableStrongNonDrawCard = board.Hand.Any(h => h != null && h.Template != null
                                && h.CurrentCost <= effectiveMana
                                && !weakPlaySet.Contains(h.Template.Id)
                                && !drawMinionIds.Contains(h.Template.Id)
                                && !isDisabledByCastModifier(h));

                            preferLifeTapBeforeOnlyDrawMinions = !lethalThisTurn
                                && !heroPowerUsedThisTurn
                                && board.MaxMana >= 3
                                && board.ManaAvailable == 2
                                && hasDiscardComponentAtMax
                                && board.HasCardInHand(Card.Cards.RLK_534)
                                && hasPlayableDrawMinion
                                && !hasPlayableStrongNonDrawCard
                                && !hasPlayablePriorityDrawMinion;
                            // 用户反馈：优先下狗头人/栉龙，不再强推分流
                            preferLifeTapBeforeOnlyDrawMinions = false;

                            if (preferLifeTapBeforeOnlyDrawMinions)
                            {
                                foreach (var id in drawMinionIds)
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(1200)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(id, new Modifier(-900)); } catch { }
                                }
                                if (board.Hand != null)
                                {
                                    foreach (var c in board.Hand)
                                    {
                                        if (c == null || c.Template == null) continue;
                                        if (!drawMinionIds.Contains(c.Template.Id)) continue;
                                        try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(1200)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-900)); } catch { }
                                    }
                                }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(-2600)); } catch { }
                                try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(-2600)); } catch { }
                                p.ForceResimulation = true;
                                AddLog("[分流兜底-无修正] 仅剩过牌随从可打(3费回合剩2费且有灵魂弹幕计划 => 先分流(-2600)后再重算");
                                hasPlayableStrongCard = false;
                            }
                        }
                        catch { preferLifeTapBeforeOnlyDrawMinions = false; }

                        // 用户口径：魂火始终最后出，不再让位分流。
                        // 因此这里不再用魂火“放行窗口”去压掉分流兜底。

                        if (!hasPlayableStrongCard)
                        {
                            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(-2000)); } catch { }
                            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(-2000)); } catch { }
                            p.ForceResimulation = true;
                            AddLog("[分流兜底] 法力>=2且无更优动作(已忽略禁用手牌) => 强推抽一口(-2000)并强制重算");
                        }
                        }
                    }
                     else if (!lethalThisTurn && board.ManaAvailable >= 2 && skipLifeTapByHandCaveDiscardPlan)
                     {
                         try { LogHandCards(board); } catch { }
                         AddLog("[分流兜底-让位地标] 手牌可从手使用地标且有弃牌收益(已排除0费随从) => 本回合不强推分流");
                     }
                    else if (!lethalThisTurn && skipLifeTapByClawDiscardPlan)
                    {
                        if (skipLifeTapByClawEquipPlan)
                        {
                            AddLog("[分流兜底-让位提刀] 本回合可直接提刀且命中提刀窗口=> 跳过分流兜底，优先提刀");
                        }
                        else
                        {
                            AddLog("[分流兜底] 已装备时空之爪且最高费有被弃组件=> 跳过分流兜底，保留武器攻击弃牌窗口");
                        }
                    }
                    else if (!lethalThisTurn && hasClawDiscardPlanButCannotAttackNow && board.ManaAvailable >= 2 && !canClickCave)
                    {
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(-2000)); } catch { }
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(-2000)); } catch { }
                        p.ForceResimulation = true;
                        AddLog("[分流兜底-修正] 已装备时空之爪且最高费有被弃组件，但本回合不可挥刀 => 放行并强推分流(-2000)");
                    }
                }
                catch { }

                // 说明：OnBoardBoardEnemyMinionsModifiers 鏁板?越高越优先作为攻击/瑙ｅ目标

                // 玩具船：优先解（否则持续滚雪球）
                if (board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.TOY_505))
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.TOY_505, new Modifier(9999));
                    AddLog("[威胁值] 敌方有玩具船(TOY_505) => 强制优先级9999)");
                }

                // 纳兹曼尼织血者：优先解（避免其持续滚雪球压制）
                if (!lethalThisTurn
                    && board.MinionEnemy != null
                    && board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.DMF_120))
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DMF_120, new Modifier(9500));
                    AddLog("[威胁值] 敌方有纳兹曼尼织血者(DMF_120) => 强制优先解场(9500)");
                }
                
                // 甯?件的威胁值设?
                // 纳迦侍从：优先解
                if (board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.TID_098))
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.TID_098, new Modifier(9999));
                }
                
                // 空灵：无嘲讽时降低优先度
                if (board.MinionEnemy.Any(minion => minion.Template.Id == Card.Cards.FP1_022 && minion.IsTaunt == false))
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.FP1_022, new Modifier(50));
                }
                
                // 沉睡者伊瑟拉：有吸血属??提高优先级
                if ((board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor) >= 20 
                    && board.MinionEnemy.Count(x => x.IsLifeSteal == true && x.Template.Id == Card.Cards.CS3_033) >= 1)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CS3_033, new Modifier(200));
                }
                else
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CS3_033, new Modifier(0));
                }
                
                // 织法者玛里苟斯：有吸?灞炴??提高优先级
                if ((board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor) >= 20 
                    && board.MinionEnemy.Count(x => x.IsLifeSteal == true && x.Template.Id == Card.Cards.CS3_034) >= 1)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CS3_034, new Modifier(200));
                }
                else
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CS3_034, new Modifier(0));
                }
                
                // 楂樺?鑳佸?随从（600锛氭嘲鍧?传奇随从?
                var threat600 = new[] {
                    Card.Cards.TTN_415,    // 鍗?格罗?TITAN
                    Card.Cards.TTN_960,    // 萨格拉斯 TITAN
                    Card.Cards.TTN_800,    // 高戈奈斯 TITAN
                    Card.Cards.TTN_721,    // V-07-TR-0N Prime TITAN
                    Card.Cards.TTN_429,    // 阿曼苏尔 TITAN
                    Card.Cards.TTN_858,    // 阿米特斯 TITAN
                    Card.Cards.TTN_075,    // 诺甘?TITAN
                    Card.Cards.TTN_092,    // 阿格拉玛 TITAN
                    Card.Cards.TTN_903,    // 伊欧纳尔 TITAN
                    Card.Cards.TTN_862,    // 阿古?TITAN
                    Card.Cards.TTN_737     // 普锐姆斯 TITAN
                };
                foreach (var id in threat600)
                {
                    if (board.MinionEnemy.Any(m => m.Template.Id == id))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(id, new Modifier(600));
                }
                
                // 楂樺?鑳佸?随从（500：需要优先解的核心随从）
                var threat500 = new[] {
                    Card.Cards.SW_115,     // 伯尔纳·锤?
                    Card.Cards.RLK_572,    // 药剂?普崔塞德
                    Card.Cards.BRM_002     // 火妖
                };
                foreach (var id in threat500)
                {
                    if (board.MinionEnemy.Any(m => m.Template.Id == id))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(id, new Modifier(500));
                }
                
                // 楂樺?鑳佸?随从（350：战斗邪犬等?
                var threat350 = new[] {
                    Card.Cards.TTN_801     // Champion of Storms
                };
                foreach (var id in threat350)
                {
                    if (board.MinionEnemy.Any(m => m.Template.Id == id))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(id, new Modifier(350));
                }
                
                // 涓??鑳佸?随从（300：法强?过牌?关键效果随从）
                var threat300 = new[] {
                    Card.Cards.TSC_032,    // 剑圣?崱灏?
                    Card.Cards.CS2_237,    // 楗??的秃?
                    Card.Cards.VAN_CS2_237,// 楗??的秃鹫（?増锛?
                    Card.Cards.TTN_730     // Lab Constructor
                };
                foreach (var id in threat300)
                {
                    if (board.MinionEnemy.Any(m => m.Template.Id == id))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(id, new Modifier(300));
                }
                
                // 涓??鑳佸?随从（250：需要优先处理的随从?
                var threat250 = new[] {
                    Card.Cards.YOP_031,    // 螃蟹骑士
                    Card.Cards.UNG_900,    // 灵魂歌?安布拉
                    Card.Cards.ULD_240,    // 对空奥术法师
                    Card.Cards.EX1_608,    // 巫师?緬
                    Card.Cards.VAN_EX1_608,// 巫师?（旧版）
                    Card.Cards.SCH_600t3,  // 加攻击的恶魔伙伴
                    Card.Cards.BAR_871,    // 士兵?槦
                    Card.Cards.BAR_043,    // 鱼人宝宝车队
                    Card.Cards.BAR_860,    // 火焰术＋弗洛格尔
                    Card.Cards.BAR_063,    // 甜水鱼人???
                    Card.Cards.BAR_918,    // 塔姆辛·罗?
                    Card.Cards.TTN_732,    // Invent-o-matic
                    Card.Cards.TTN_466     // Minotauren
                };
                foreach (var id in threat250)
                {
                    if (board.MinionEnemy.Any(m => m.Template.Id == id))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(id, new Modifier(250));
                }
                
                // 常规高?鑳佸?随从（200锛氬?閮?垎闇?要优先处理的随从?
                var threat200 = new[] {
                    // 新版本随?
                    Card.Cards.VAC_927,    // 狂飙邪魔
                    Card.Cards.VAC_938,    // 粗暴的猢?
                    Card.Cards.ETC_355,    // 剃刀沼泽摇滚明星
                    Card.Cards.WW_091,     // 腐臭?偿娉?櫘鍔?
                    Card.Cards.MIS_026,    // 鍌?鍎??师多里安
                    Card.Cards.CORE_WON_065,// 随船外科医师
                    Card.Cards.WW_357,     // 老腐和?佸?
                    Card.Cards.DEEP_999t2, // 深岩之洲晶簇
                    Card.Cards.CFM_039,    // 鏉傝?嶅皬楝?
                    Card.Cards.WON_365,    // 鏉傝?小鬼（奇观?
                    Card.Cards.WW_364t,    // 狡诈巨龙威拉罗克
                    Card.Cards.TSC_026t,   // 可拉克的?
                    Card.Cards.JAM_028,    // 鲜血树人
                    Card.Cards.WW_415,     // 许愿?
                    Card.Cards.CS3_014,    // 赤红教士
                    Card.Cards.YOG_516,    // 脱困??灏?牸-萨隆
                    Card.Cards.NX2_033,    // 宸??塔迪乌?
                    Card.Cards.JAM_004,    // 镂骨恶犬
                    Card.Cards.TTN_330,    // Kologarn
                    Card.Cards.TTN_729,    // Melted Maker
                    Card.Cards.TTN_479,    // Flame Revenant
                    Card.Cards.TTN_833,    // Disciple of Golganneth
                    Card.Cards.TTN_856,    // Disciple of Amitus
                    Card.Cards.TTN_907,    // Astral Serpent
                    Card.Cards.TTN_071,    // Sif
                    Card.Cards.TTN_078,    // Observer of Myths
                    Card.Cards.TTN_843,    // Eredar Deceptor
                    Card.Cards.ETC_541,    // 盗版之王托尼
                    Card.Cards.CORE_LOOT_231,// 奥术工匠
                    Card.Cards.ETC_339,    // 心动歌手
                    Card.Cards.ETC_833,    // 箭矢?尃
                    Card.Cards.NX2_006,    // 旗标骷髅
                    Card.Cards.ETC_105,    // 立体声图?
                    Card.Cards.ETC_522,    // 尖叫女妖
                    Card.Cards.RLK_121,    // 死亡侍僧
                    Card.Cards.RLK_539,    // 达尔坎·德拉希?
                    Card.Cards.RLK_061,    // 战场通灵?
                    Card.Cards.RLK_824,    // 肢体商贩
                    Card.Cards.CORE_EX1_012,// 血法师?诺斯
                    Card.Cards.TSC_074,    // 克托里·光?
                    Card.Cards.RLK_607,    // 搅局破法?
                    Card.Cards.RLK_924,    // 血楠戝＋领袖莉亚德琳
                    Card.Cards.CORE_NEW1_020,// 狂野炎术?
                    Card.Cards.RLK_083,    // 死亡寒冰
                    Card.Cards.RLK_218,    // 银月城?术师
                    Card.Cards.REV_935,    // 派对图腾
                    Card.Cards.REV_935t,   // 派对图腾（变形）
                    Card.Cards.RLK_912,    // 天灾巨魔
                    Card.Cards.DMF_709,    // 宸?图腾埃索?
                    Card.Cards.RLK_970,    // 陆行鸟牧?
                    Card.Cards.MAW_009t,   // ??
                    Card.Cards.TSC_922,    // 驻锚图腾
                    Card.Cards.AV_137,     // 深铁穴居?
                    Card.Cards.REV_515,    // 豪宅?俄里?
                    Card.Cards.TSC_959,    // 扎库?
                    Card.Cards.TSC_218,    // 赛丝诺?澹?
                    Card.Cards.TSC_962,    // 老巨?
                    Card.Cards.REV_016,    // 邪恶的厨?
                    Card.Cards.REV_828t,   // 绑架犯的袋子
                    Card.Cards.KAR_006,    // 神秘女猎?
                    Card.Cards.REV_332,    // 心能提取?
                    Card.Cards.REV_011,    // 嫉妒收割?
                    Card.Cards.LOOT_412,   // 狗头人幻术师
                    Card.Cards.TSC_950,    // 海卓拉顿
                    Card.Cards.SW_062,     // 闪金镇豺狼人
                    Card.Cards.REV_513,    // 健谈的调酒师
                    Card.Cards.BAR_033,    // 勘探者车?
                    Card.Cards.ONY_007,    // 监护者哈尔琳
                    Card.Cards.CS3_032,    // 龙巢之母??克希?
                    Card.Cards.SW_431,     // 花园猎豹
                    Card.Cards.AV_340,     // 亮铜之翼
                    Card.Cards.SW_458t,    // 塔维?的山?
                    Card.Cards.WC_006,     // 安娜科德?
                    Card.Cards.ONY_004,    // 鍥?首领??克希?
                    Card.Cards.SW_319,     // 农夫
                    Card.Cards.TSC_002,    // 刺豚拳手
                    Card.Cards.CORE_LOE_077,// 布莱?烽摐椤?
                    Card.Cards.TSC_620,    // 恶鞭海妖
                    Card.Cards.TSC_073,    // 拉伊·纳兹?
                    Card.Cards.DED_006,    // 重拳先生
                    Card.Cards.CORE_AT_029,// 锈水海盗
                    Card.Cards.BAR_074,    // 前沿哨所
                    Card.Cards.AV_118,     // 历战先锋
                    Card.Cards.GVG_040,    // 沙鳞灵魂行??
                    Card.Cards.BT_304,     // 改进型恐?瓟鐜?
                    Card.Cards.SW_068,     // 莫尔葛熔?
                    Card.Cards.DED_519,    // 迪菲亚炮?
                    Card.Cards.CFM_807,    // 大富翁比尔杜
                    Card.Cards.TSC_054,    // 机械?奔
                    Card.Cards.GIL_646,    // 发条机器?
                    Card.Cards.DMF_237,    // 狂欢?箷鍛?
                    Card.Cards.DMF_217,    // 越线的游?
                    Card.Cards.DMF_707,    // 鱼人魔术?
                    Card.Cards.DMF_082,    // 暗月雕像
                    Card.Cards.DMF_082t,   // 暗月雕像（变形）
                    Card.Cards.DMF_708,    // 伊纳拉·碎?
                    Card.Cards.DMF_102,    // 游戏＄悊鍛?
                    Card.Cards.DMF_222,    // 获救的流?
                    Card.Cards.ULD_003,    // 了不起的杰弗里斯
                    Card.Cards.GVG_104,    // 大胖
                    Card.Cards.BAR_537,    // 閽?卫兵
                    Card.Cards.BAR_035,    // 科卡尔?鐘???
                    Card.Cards.BAR_312,    // 占卜者车?
                    Card.Cards.BAR_720,    // 古夫·符文图腾
                    Card.Cards.BAR_038,    // 塔维?·雷矛
                    Card.Cards.BAR_545,    // 濂?发光?
                    Card.Cards.BAR_888,    // 霜舌半人?
                    Card.Cards.BAR_317,    // 原野联络?
                    Card.Cards.BAR_076,    // 莫尔杉哨?
                    Card.Cards.BAR_890,    // 十字路口?槾宸?
                    Card.Cards.BAR_082,    // 贫瘠之地诱捕?
                    Card.Cards.BAR_540,    // 腐烂的普雷莫?
                    Card.Cards.BAR_878,    // 战地医师老兵
                    Card.Cards.BAR_048,    // 布鲁?
                    Card.Cards.SCH_351     // 詹迪斯·巴罗?
                };
                foreach (var id in threat200)
                {
                    if (board.MinionEnemy.Any(m => m.Template.Id == id))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(id, new Modifier(200));
                }
                
                // 中等威胁值（150锛?
                if (board.MinionEnemy.Any(m => m.Template.Id == Card.Cards.TTN_812))
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.TTN_812, new Modifier(150));
                
                // 浣庡?鑳佸?随从（避免优先攻击?
                var threat0 = new[] {
                    Card.Cards.DRG_320,    // 新伊瑟拉
                    Card.Cards.CFM_020,    // 缚链者拉?
                    Card.Cards.CORE_EX1_110,// 凯恩·?韫?
                    Card.Cards.BAR_072     // 火刃侍僧
                };
                foreach (var id in threat0)
                {
                    if (board.MinionEnemy.Any(m => m.Template.Id == id))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(id, new Modifier(0));
                }
                
                // 降低威胁值（避免攻击?
                if (board.MinionEnemy.Any(m => m.Template.Id == Card.Cards.BOT_447))
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.BOT_447, new Modifier(-10));

                // 保命?爮血厖锛氶?璁?回合可斩?但血线危?，提高全体敌方随从?鑳佸?，优先ｅ防反?銆?
                if (survivalStabilizeMode && board.MinionEnemy != null)
                {
                    foreach (var m in board.MinionEnemy)
                    {
                        if (m == null || m.Template == null) continue;
                        int threat = 300 + Math.Max(0, m.CurrentAtk) * 120;
                        if (m.IsTaunt) threat += 200;
                        if (m.IsLifeSteal) threat += 180;
                        if (myHpArmorForStabilize > 0 && m.CurrentAtk >= myHpArmorForStabilize) threat += 300;
                        threat = Math.Min(9800, threat);
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(m.Template.Id, new Modifier(threat));
                    }
                    AddLog("[保命护栏] 下回合有血线且血线危险 => 提高解敌随从的控场权重");
                }

                // 非斩杀回合：优先清?0攻低?随从（如 0/2 图腾），避免其持续提供功能价值??
                if (!lethalThisTurn && board.MinionEnemy != null)
                {
                    var zeroAtkLowHpMinions = board.MinionEnemy
                        .Where(m => m != null
                            && m.Template != null
                            && m.CurrentHealth > 0
                            && m.CurrentHealth <= 2
                            && m.CurrentAtk <= 0)
                        .ToList();

                    bool hasTemporarySoulBarrageInHand = false;
                    try
                    {
                        hasTemporarySoulBarrageInHand = board.Hand != null
                            && board.Hand.Any(c => c != null
                                && c.Template != null
                                && c.Template.Id == Card.Cards.RLK_534
                                && IsTemporaryCard(board, c));
                    }
                    catch { hasTemporarySoulBarrageInHand = false; }

                    if (hasTemporarySoulBarrageInHand && zeroAtkLowHpMinions.Count > 0)
                    {
                        int skippedOneHp = zeroAtkLowHpMinions.Count(m => m != null && m.CurrentHealth <= 1);
                        if (skippedOneHp > 0)
                        {
                            zeroAtkLowHpMinions = zeroAtkLowHpMinions
                                .Where(m => m != null && m.CurrentHealth > 1)
                                .ToList();
                            AddLog("[威胁-0攻低血] 手上有临时灵魂弹幕 => 跳过1血随从加权(数量="
                                + skippedOneHp + ")");
                        }
                    }

                    bool hasFriendlyAttacker = board.MinionFriend != null
                        && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);

                    if (zeroAtkLowHpMinions.Count > 0 && hasFriendlyAttacker)
                    {
                        foreach (var m in zeroAtkLowHpMinions)
                        {
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(m.Template.Id, new Modifier(420));
                        }
                        AddLog("[威胁-0攻低血] 非斩杀回合且存在0攻低血随从("
                            + zeroAtkLowHpMinions.Count + ") => 提高解场优先级(420)");
                    }
                }

                // 灰烬元素专用：可攻击时优先用于解掉“能打死的关键随从”（尤其嘲讽/高攻/吸血）
                if (!lethalThisTurn && board.MinionFriend != null && board.MinionEnemy != null)
                {
                    var ashAttackers = board.MinionFriend
                        .Where(m => m != null
                            && m.Template != null
                            && m.Template.Id == Card.Cards.REV_960
                            && m.CanAttack
                            && m.CurrentAtk > 0)
                        .ToList();

                    if (ashAttackers.Count > 0 && board.MinionEnemy.Any(m => m != null && m.CurrentHealth > 0))
                    {
                        int ashAtkMax = 0;
                        try { ashAtkMax = ashAttackers.Max(m => Math.Max(0, m.CurrentAtk)); } catch { ashAtkMax = 0; }

                        if (ashAtkMax > 0)
                        {
                            bool hasAshPreferredTarget = false;
                            try { p.AttackOrderModifiers.AddOrUpdate(Card.Cards.REV_960, new Modifier(980)); } catch { }

                            foreach (var e in board.MinionEnemy.Where(x => x != null && x.Template != null && x.CurrentHealth > 0))
                            {
                                int bonus = 0;
                                if (e.IsTaunt && e.CurrentHealth <= ashAtkMax) bonus += 1800;
                                if (e.CurrentHealth <= ashAtkMax && e.CurrentAtk >= 3) bonus += 700;
                                if (e.IsLifeSteal && e.CurrentHealth <= ashAtkMax) bonus += 600;

                                if (bonus > 0)
                                {
                                    hasAshPreferredTarget = true;
                                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(e.Template.Id, new Modifier(bonus));
                                }
                            }

                            if (hasAshPreferredTarget)
                            {
                                AddLog("[灰烬元素-攻击优先] 场上灰烬元素可攻击且存在可解关键目标 => 提前灰烬元素攻击序(980)并提高目标权重");
                            }
                        }
                    }
                }
                 
                AddLog("[威胁值] 已设置敌方关键随从技能权重");
            }
            catch (Exception ex)
            {
                AddLog("[威胁值] 异常: " + ex.Message);
            }

            // ==================================================
            // 【全局硬规则】邪翼蝠/郊狼压费走脸：
            // 当手里有邪翼蝠/郊狼，且满足「MaxMana>=3 或手里没硬币」时，
            // 若本回合通过随从走脸可将其压到可打费用，则强制优先走脸并保留重算。
            // 放在威胁值设置之后，避免被常规解场权重覆盖。
            // ==================================================
            try
            {
                keepForceResimulationForDiscountPlan = TryApplyFelwingCoyoteFaceDiscountPlan(
                    p,
                    board,
                    survivalStabilizeMode,
                    "全局硬规则-邪翼蝠郊狼压费");
            }
            catch { }

            // ==================================================
            // 【全局硬规则】咒怨之墓前置：当墓本回合可打且武器可攻击时，不先A武器
            // 说明：避免出现“先攻击再墓”的错序；仅在墓未被确定连段互斥时启用。
            // ==================================================
            try
            {
                if (!lethalThisTurn && board.Hand != null && board.Hand.Count > 0)
                {
                    bool hasCoinLocal = false;
                    try { hasCoinLocal = board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.GAME_005); } catch { hasCoinLocal = false; }
                    int effectiveManaLocal = board.ManaAvailable + (hasCoinLocal ? 1 : 0);

                    var playableTomb = board.Hand.FirstOrDefault(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.TLC_451
                        && h.CurrentCost <= board.ManaAvailable);

                    bool canUseTombByTurnGate = board.MaxMana >= 3 || (board.MaxMana >= 2 && hasCoinLocal);
                    bool hasAtLeastTwoManaForTomb = board.ManaAvailable >= 2;

                    bool hasEquippedWeapon = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.CurrentDurability > 0;

                    bool heroLikelyCanAttackNow = false;
                    try
                    {
                        heroLikelyCanAttackNow = hasEquippedWeapon
                            && board.HeroFriend != null
                            && !board.HeroFriend.IsFrozen
                            && board.HeroFriend.CountAttack == 0;
                    }
                    catch
                    {
                        heroLikelyCanAttackNow = hasEquippedWeapon
                            && board.HeroFriend != null
                            && !board.HeroFriend.IsFrozen
                            && board.HeroFriend.CanAttack;
                    }

                    bool delayTombByCombo = false;
                    string delayReason = null;
                    try { delayTombByCombo = ShouldDelayCurseTombBecauseOtherComboAvailable(board, effectiveManaLocal, out delayReason); } catch { delayTombByCombo = false; delayReason = null; }

                    bool shouldTapBeforeTombGlobal = false;
                    try
                    {
                        bool clawEquipWindowForGlobalTombYield = false;
                        try
                        {
                            bool hasClawEquippedForGlobalTombYield = board.WeaponFriend != null
                                && board.WeaponFriend.Template != null
                                && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                && board.WeaponFriend.CurrentDurability > 0;
                            var clawForGlobalTombYield = board.Hand != null
                                ? board.Hand.FirstOrDefault(h => h != null && h.Template != null && h.Template.Id == Card.Cards.END_016)
                                : null;
                            bool canEquipClawForGlobalTombYield = !hasClawEquippedForGlobalTombYield
                                && clawForGlobalTombYield != null
                                && clawForGlobalTombYield.CurrentCost <= effectiveManaLocal;
                            bool hasPirateForGlobalTombYield = CountDreadCorsairInHand(board) > 0
                                || (board.Hand != null && board.Hand.Any(c => c != null
                                    && c.Template != null
                                    && c.Template.Id == Card.Cards.CORE_NEW1_022));
                            bool hasBarrageForGlobalTombYield = board.Hand != null
                                && board.Hand.Any(c => c != null
                                    && c.Template != null
                                    && c.Template.Id == Card.Cards.RLK_534);
                            clawEquipWindowForGlobalTombYield = canEquipClawForGlobalTombYield
                                && (hasDiscardComponentAtMaxForWeapon || hasPirateForGlobalTombYield || hasBarrageForGlobalTombYield);
                        }
                        catch { clawEquipWindowForGlobalTombYield = false; }

                        bool canTapNowForGlobalTombYield = CanUseLifeTapNow(board);
                        int enemyMinionCountForGlobalTombYield = board.MinionEnemy != null
                            ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0)
                            : 0;
                        int enemyAttackForGlobalTombYield = board.MinionEnemy != null
                            ? board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk))
                            : 0;
                        bool highEnemyPressureForGlobalTombYield = enemyAttackForGlobalTombYield >= 8 || enemyMinionCountForGlobalTombYield >= 2;
                        bool crowdedHandForGlobalTombYield = board.Hand != null && board.Hand.Count >= 7;
                        bool hasPlayableMerchantForGlobalTombYield = board.Hand != null
                            && board.Hand.Any(h => h != null
                                && h.Template != null
                                && h.Template.Id == Card.Cards.ULD_163
                                && h.CurrentCost <= board.ManaAvailable)
                            && GetFriendlyBoardSlotsUsed(board) < 7;

                        bool enoughManaForGlobalTapYield = board.ManaAvailable >= 4;
                        bool baseTapBeforeTombGlobal = playableTomb != null
                            && playableTomb.CurrentCost == 0
                            && canTapNowForGlobalTombYield
                            && enoughManaForGlobalTapYield
                            && crowdedHandForGlobalTombYield
                            && highEnemyPressureForGlobalTombYield
                            && !hasPlayableMerchantForGlobalTombYield;

                        shouldTapBeforeTombGlobal = baseTapBeforeTombGlobal
                            && !clawEquipWindowForGlobalTombYield;

                        if (clawEquipWindowForGlobalTombYield && baseTapBeforeTombGlobal)
                        {
                            shouldTapBeforeTombGlobal = false;
                            AddLog("[全局硬规则-咒怨之墓让位分流] 命中提刀窗口且本回合可提刀 => 不先分流，保留提刀");
                        }
                    }
                    catch { shouldTapBeforeTombGlobal = false; }

                    if (playableTomb != null && canUseTombByTurnGate && !hasAtLeastTwoManaForTomb)
                    {
                        AddLog("[全局硬规则-咒怨之墓前置] ManaAvailable=" + board.ManaAvailable + " < 2 => 不前置咒怨之墓");
                    }

                    if (playableTomb != null && canUseTombByTurnGate && hasAtLeastTwoManaForTomb && !delayTombByCombo && !shouldTapBeforeTombGlobal)
                    {
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(1200)); } catch { }
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(1200)); } catch { }
                        try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-2200)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(9850)); } catch { }
                        AddLog("[全局硬规则-咒怨之墓前置] 墓可打且未命中互斥连段 => 先墓后分流(分流后置1200)");
                    }

                    if (playableTomb != null && canUseTombByTurnGate && hasAtLeastTwoManaForTomb && hasEquippedWeapon && heroLikelyCanAttackNow && !delayTombByCombo && !shouldTapBeforeTombGlobal)
                    {
                        // 用户口径：墓不覆盖刀。这里不再锁武器攻击，交给综合评分链路决定先后。
                        AddLog("[全局硬规则-咒怨之墓前置] 墓可打且武器可攻击 => 不覆盖武器攻击，按综合优先级决策");
                    }

                    if (shouldTapBeforeTombGlobal)
                    {
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(-2400)); } catch { }
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(-2400)); } catch { }
                        try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(1400)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-1300)); } catch { }
                        AddLog("[全局硬规则-咒怨之墓让位分流] 0费墓+场压高+手牌拥挤 => 先分流，墓后置(1400/-1300)");
                    }
                }
            }
            catch { }

            // ==================================================
            // 【全局硬规则】恐怖海盗后置：
            // 仅在有刀联动（手里有刀或已装备刀）时，不直接打恐怖海盗。
            // 放在尾部兜底，避免被前面分支覆盖。
            // ==================================================
            try
            {
                if (board.Hand != null && board.Hand.Count > 0)
                {
                    var dreadPirates = GetDreadCorsairCardsInHand(board);
                    if (dreadPirates.Count > 0)
                    {
                        try
                        {
                            bool hasFourCostDread = dreadPirates.Any(dp => dp != null && dp.CurrentCost >= 4);
                            bool hasZeroCostDread = dreadPirates.Any(dp => dp != null && dp.CurrentCost == 0);
                            bool hasClawInHandNowForTap = board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.END_016);
                            bool hasClawEquippedNowForTap = board.WeaponFriend != null
                                && board.WeaponFriend.Template != null
                                && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                && board.WeaponFriend.CurrentDurability > 0;
                            int soulBarrageCountForTap = board.Hand.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);

                            bool canTapNowGlobalRobust = canTapNow;
                            try { canTapNowGlobalRobust = canTapNow || CanUseLifeTapNow(board); }
                            catch { canTapNowGlobalRobust = canTapNow; }

                            bool shouldYieldToLifeTapGlobal = !lethalThisTurn
                                && canTapNowGlobalRobust
                                && hasFourCostDread
                                && !hasZeroCostDread
                                && !hasClawInHandNowForTap
                                && !hasClawEquippedNowForTap
                                && (hasDiscardComponentAtMax || soulBarrageCountForTap >= 2);

                            if (shouldYieldToLifeTapGlobal)
                            {
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-9999)); } catch { }
                                foreach (var pirate in dreadPirates)
                                {
                                    if (pirate == null || pirate.Template == null) continue;
                                    try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-9999)); } catch { }
                                }

                                AddLog("[全局硬规则-恐怖海盗后置] 无刀联动且可分流(弃牌收益密度="
                                    + (hasDiscardComponentAtMax ? "Y" : "N") + ",弹幕x" + soulBarrageCountForTap
                                    + ") => 暂禁4费恐怖海盗(9999/-9999)，优先抽一口");
                            }
                        }
                        catch { }

                        bool allInDoubleSoulfire = false;
                        string gambleReason = null;
                        bool allInSingleSoulfire = false;
                        string singleGambleReason = null;
                        try
                        {
                            allInDoubleSoulfire = ShouldAllInDoubleSoulfireLethalGamble(board, lethalThisTurn, out gambleReason);
                            allInSingleSoulfire = ShouldAllInSingleSoulfireDiscardLethalGamble(board, lethalThisTurn, out singleGambleReason);
                        }
                        catch
                        {
                            allInDoubleSoulfire = false;
                            gambleReason = null;
                            allInSingleSoulfire = false;
                            singleGambleReason = null;
                        }

                        if (allInDoubleSoulfire || allInSingleSoulfire)
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-9999)); } catch { }
                            foreach (var pirate in dreadPirates)
                            {
                                if (pirate == null || pirate.Template == null) continue;
                                try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-9999)); } catch { }
                            }

                            AddLog("[全局硬规则-恐怖海盗后置] " + (allInDoubleSoulfire
                                    ? (gambleReason ?? "命中双魂火斩杀赌博窗口")
                                    : (singleGambleReason ?? "命中单魂火弃牌斩杀窗口"))
                                + " => 暂禁直接打恐怖海盗(9999/-9999)");
                        }

                        bool hasClawInHandNow = false;
                        bool hasClawEquippedNow = false;
                        try
                        {
                            hasClawInHandNow = board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.END_016);
                            hasClawEquippedNow = board.WeaponFriend != null
                                && board.WeaponFriend.Template != null
                                && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                && board.WeaponFriend.CurrentDurability > 0;
                        }
                        catch
                        {
                            hasClawInHandNow = false;
                            hasClawEquippedNow = false;
                        }

                        if (!allInDoubleSoulfire && !allInSingleSoulfire && (hasClawInHandNow || hasClawEquippedNow))
                        {
                            try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-9999)); } catch { }
                            foreach (var pirate in dreadPirates)
                            {
                                if (pirate == null || pirate.Template == null) continue;
                                try { p.CastMinionsModifiers.AddOrUpdate(pirate.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(pirate.Id, new Modifier(-9999)); } catch { }
                            }

                            string reason = hasClawInHandNow ? "手里有时空之爪" : "已装备时空之爪";
                            AddLog("[全局硬规则-恐怖海盗后置] " + reason + " => 暂禁直接打恐怖海盗(9999/-9999)");
                        }
                    }
                }
            }
            catch { }

            // ==================================================
            // 【全局硬规则】最高费组没有被弃组件 => 禁止任何武器攻击
            // 说明：之前仅对 END_016(时空之爪) 做了禁攻；若实际装备的是其它武器，会出现绕过。
            // 放在 return 之前，确保不被后续逻辑覆盖。
            // ==================================================
            try
            {
                bool hasEquippedWeapon = board.WeaponFriend != null
                    && board.WeaponFriend.Template != null
                    && board.WeaponFriend.CurrentDurability > 0;
                bool heroCanAttackWithWeapon = hasEquippedWeapon
                    && board.HeroFriend != null
                    && board.HeroFriend.CanAttack;

                // 注意：这里不依赖 heroCanAttackWithWeapon锛岄伩鍏?CanAttack 鍦?些时序下误判导致绕过禁攻?
                bool coyoteUnlocksDiscardWindowNow = false;
                bool felwingUnlocksDiscardWindowNow = false;
                bool faceDiscountUnlocksDiscardWindowNow = false;
                try { coyoteUnlocksDiscardWindowNow = CanCoyoteUnlockClawDiscardWindow(board, discardComponents); } catch { coyoteUnlocksDiscardWindowNow = false; }
                try { felwingUnlocksDiscardWindowNow = CanFelwingUnlockClawDiscardWindow(board, discardComponents); } catch { felwingUnlocksDiscardWindowNow = false; }
                faceDiscountUnlocksDiscardWindowNow = coyoteUnlocksDiscardWindowNow || felwingUnlocksDiscardWindowNow;

                if (hasEquippedWeapon && !hasDiscardComponentAtMaxForWeapon && !faceDiscountUnlocksDiscardWindowNow)
                {
                    bool emptyHandClawOverride = false;
                    try
                    {
                        int handCountNow = board.Hand != null ? board.Hand.Count : 0;
                        bool isClawEquipped = board.WeaponFriend != null
                            && board.WeaponFriend.Template != null
                            && board.WeaponFriend.Template.Id == Card.Cards.END_016;
                        emptyHandClawOverride = handCountNow <= 0 && isClawEquipped;
                    }
                    catch { emptyHandClawOverride = false; }

                    if (emptyHandClawOverride)
                    {
                        AddLog("[全局硬规则武器禁攻-空手放行] 手牌为空且时空之爪可攻击 => 跳过禁攻，允许挥刀");
                    }
                    else
                    {
                    var weaponId = board.WeaponFriend.Template.Id;
                    BlockWeaponAttackForAllTargets(p, board, weaponId, 9999);
                    string weaponName = "?";
                    int weaponAtk = 0;
                    int weaponDur = 0;
                    try
                    {
                        weaponName = board.WeaponFriend.Template != null ? board.WeaponFriend.Template.NameCN : "?";
                        weaponAtk = board.WeaponFriend.CurrentAtk;
                        weaponDur = board.WeaponFriend.CurrentDurability;
                    }
                    catch { }

                    AddLog("[全局硬规则武器禁攻] 当前最高费组无被弃组件(挥刀判定最高费有被弃组件=" + hasDiscardComponentAtMaxForWeapon
                        + ",全局最高费有被弃组件=" + hasDiscardComponentAtMax
                        + ") => 禁止武器攻击(9999) | weapon=" + weaponId + "(" + weaponName + ")"
                        + " atk=" + weaponAtk + " dur=" + weaponDur
                        + " heroCanAttack=" + (board.HeroFriend != null && board.HeroFriend.CanAttack ? "Y" : "N"));
                    }
                }
                else if (hasEquippedWeapon && !hasDiscardComponentAtMaxForWeapon && faceDiscountUnlocksDiscardWindowNow)
                {
                    AddLog("[全局硬规则武器禁攻-放行] 当前最高费组暂不含被弃组件，但可通过郊狼/邪翼蝠压费转化 => 暂不禁攻，保留先A后挥刀线");
                }
            }
            catch { }

            // ===== 全局硬规则：不?对面的?凯洛斯的蛋”系列（DINO_410*锛?=====
            // 鍙ｅ緞锛氬?闈?现蛋阶段时，尽量不要?殢浠?武器/瑙ｇ去处理它（优先处理其他滚雪球?直伤点）?
            // 娉?剰锛氭斁鍦?GetParameters 尾部，避免被前面?閮??辑覆盖?
            try
            {
                if (board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.Template != null &&
                    (m.Template.Id == Card.Cards.DINO_410
                     || m.Template.Id == Card.Cards.DINO_410t2
                     || m.Template.Id == Card.Cards.DINO_410t3
                     || m.Template.Id == Card.Cards.DINO_410t4
                     || m.Template.Id == Card.Cards.DINO_410t5)))
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t2, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t3, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t4, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t5, new Modifier(-9999));
                }
            }
            catch { }

            // 全局最终护栏：若存在“稳定触发灵魂弹幕弃牌”线路，终局禁止灵魂弹幕直拍。
            // 目的：避免前序分支设置被后续逻辑覆盖后，仍把灵魂弹幕直接打出。
            try
            {
                if (board.Hand != null && board.Hand.Count > 0 && board.HasCardInHand(Card.Cards.RLK_534))
                {
                    var handForFinalBarrageGuard = GetHandCardsForDiscardLogic(board);
                    bool barragePlayableNow = handForFinalBarrageGuard.Any(c => c != null && c.Template != null
                        && c.Template.Id == Card.Cards.RLK_534
                        && c.CurrentCost <= board.ManaAvailable);

                    if (barragePlayableNow)
                    {
                        bool stableByMerchantFinal = false;
                        bool stableByClawFinal = false;
                        bool stableByCaveFinal = false;
                        bool needCoinForMerchantFinal = false;
                        Card merchantFinal = null;
                        Card clawFinal = null;
                        bool needCoinForClawFinal = false;
                        bool clawEquippedCanSwingFinal = false;

                        // A) 商贩稳定弃弹幕：商贩可用，且最高费组仅为灵魂弹幕
                        try
                        {
                            bool hasSpaceFinal = GetFriendlyBoardSlotsUsed(board) < 7;
                            merchantFinal = handForFinalBarrageGuard.FirstOrDefault(c => c != null && c.Template != null
                                && c.Template.Id == Card.Cards.ULD_163);
                            bool canPlayMerchantNowFinal = merchantFinal != null
                                && hasSpaceFinal
                                && merchantFinal.CurrentCost <= effectiveMana;
                            needCoinForMerchantFinal = merchantFinal != null
                                && hasCoin
                                && merchantFinal.CurrentCost > board.ManaAvailable
                                && merchantFinal.CurrentCost <= effectiveMana;

                            if (canPlayMerchantNowFinal)
                            {
                                var handWithoutMerchantAndCoinFinal = handForFinalBarrageGuard.Where(h => h != null && h.Template != null
                                    && h.Template.Id != Card.Cards.ULD_163
                                    && h.Template.Id != Card.Cards.GAME_005).ToList();
                                if (handWithoutMerchantAndCoinFinal.Count > 0)
                                {
                                    int maxCostFinal = handWithoutMerchantAndCoinFinal.Max(h => h.CurrentCost);
                                    var highestFinal = handWithoutMerchantAndCoinFinal.Where(h => h.CurrentCost == maxCostFinal).ToList();
                                    stableByMerchantFinal = highestFinal.Count > 0
                                        && highestFinal.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                                }
                            }
                        }
                        catch
                        {
                            stableByMerchantFinal = false;
                            needCoinForMerchantFinal = false;
                            merchantFinal = null;
                        }

                        // B) 挥刀稳定弃弹幕：已装备且可挥，或可提刀后立即挥刀，且最高费组仅为灵魂弹幕
                        try
                        {
                            bool heroCanAttackFinal = board.HeroFriend != null
                                && board.HeroFriend.CanAttack
                                && board.HeroFriend.CountAttack == 0
                                && !board.HeroFriend.IsFrozen;
                            clawEquippedCanSwingFinal = board.WeaponFriend != null
                                && board.WeaponFriend.Template != null
                                && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                && board.WeaponFriend.CurrentDurability > 0
                                && heroCanAttackFinal;
                            clawFinal = handForFinalBarrageGuard.FirstOrDefault(h => h != null
                                && h.Template != null
                                && h.Template.Id == Card.Cards.END_016);
                            bool clawPlayableFromHandFinal = clawFinal != null
                                && clawFinal.CurrentCost <= effectiveMana;
                            needCoinForClawFinal = clawFinal != null
                                && hasCoin
                                && clawFinal.CurrentCost > board.ManaAvailable
                                && clawFinal.CurrentCost <= effectiveMana;
                            bool canEquipThenSwingFinal = (board.WeaponFriend == null || board.WeaponFriend.Template == null)
                                && heroCanAttackFinal
                                && clawPlayableFromHandFinal;

                            bool canEquipClawFinal = (board.WeaponFriend == null || board.WeaponFriend.Template == null)
                                && clawPlayableFromHandFinal;

                            if (clawEquippedCanSwingFinal || canEquipThenSwingFinal || canEquipClawFinal)
                            {
                                var handWithoutCoinFinal = handForFinalBarrageGuard.Where(h => h != null && h.Template != null
                                    && h.Template.Id != Card.Cards.GAME_005).ToList();
                                if (handWithoutCoinFinal.Count > 0)
                                {
                                    int maxCostFinal = handWithoutCoinFinal.Max(h => h.CurrentCost);
                                    var highestFinal = handWithoutCoinFinal.Where(h => h.CurrentCost == maxCostFinal).ToList();
                                    stableByClawFinal = highestFinal.Count > 0
                                        && highestFinal.All(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                                }
                            }
                        }
                        catch { stableByClawFinal = false; }

                        // C) 地标稳定弃弹幕：可点场上窟穴，或可从手直接下窟穴（且有位置）
                        try
                        {
                            bool canUseBoardCaveFinal = canClickCave;
                            bool canPlayHandCaveFinal = canPlayCaveFromHand && GetFriendlyBoardSlotsUsed(board) < 7;
                            stableByCaveFinal = canUseBoardCaveFinal || canPlayHandCaveFinal;
                        }
                        catch { stableByCaveFinal = false; }

                        if (stableByMerchantFinal || stableByClawFinal || stableByCaveFinal)
                        {
                            SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);

                            if (stableByMerchantFinal && merchantFinal != null)
                            {
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-2500)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(9300)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(merchantFinal.Id, new Modifier(-2500)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(merchantFinal.Id, new Modifier(9300)); } catch { }
                                TrySetCoinThenCardComboWithoutResim(p, board, merchantFinal, needCoinForMerchantFinal, "全局最终护栏-灵魂弹幕");
                                AddLog("[全局最终护栏-灵魂弹幕] 可稳定商贩弃弹幕 => 终局禁止直拍弹幕(9999/-9999)");
                            }
                            else if (stableByClawFinal)
                            {
                                if (clawEquippedCanSwingFinal)
                                {
                                    try { p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2500)); } catch { }
                                    p.ForceResimulation = true;
                                    AddLog("[全局最终护栏-灵魂弹幕] 可稳定挥刀弃弹幕 => 终局禁止直拍弹幕(9999/-9999)");
                                }
                                else if (clawFinal != null)
                                {
                                    bool coinThenClawFinal = TrySetCoinThenCardComboWithoutResim(p, board, clawFinal, needCoinForClawFinal, "全局最终护栏-灵魂弹幕");
                                    if (!coinThenClawFinal)
                                    {
                                        try { p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-2500)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(9300)); } catch { }
                                        try { p.CastWeaponsModifiers.AddOrUpdate(clawFinal.Id, new Modifier(-2500)); } catch { }
                                        try { p.PlayOrderModifiers.AddOrUpdate(clawFinal.Id, new Modifier(9300)); } catch { }
                                    }
                                    p.ForceResimulation = true;
                                    AddLog("[全局最终护栏-灵魂弹幕] 手上有刀且最高费仅灵魂弹幕 => 终局禁止直拍弹幕(9999/-9999)，优先提刀");
                                }
                            }
                            else
                            {
                                try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1200)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-1200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(9800)); } catch { }
                                AddLog("[全局最终护栏-灵魂弹幕] 可稳定窟穴弃弹幕 => 终局禁止直拍弹幕(9999/-9999)");
                            }
                        }
                    }
                }
            }
            catch { }

            // 全局终局护栏：斩杀窗口若灵魂弹幕与加拉克苏斯之拳都可打，固定优先灵魂弹幕。
            try
            {
                int enemyHpArmorFinal = 0;
                try
                {
                    enemyHpArmorFinal = board.HeroEnemy != null
                        ? (board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor)
                        : 0;
                }
                catch { enemyHpArmorFinal = 0; }

                bool canPlayBarrageFinal = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == Card.Cards.RLK_534
                    && c.CurrentCost <= board.ManaAvailable);
                bool canPlayFistFinal = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == Card.Cards.AT_022
                    && c.CurrentCost <= board.ManaAvailable);

                if (lethalThisTurn && enemyHpArmorFinal > 0 && enemyHpArmorFinal <= 6 && canPlayBarrageFinal && canPlayFistFinal)
                {
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-3600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(9970)); } catch { }
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(1600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(-1600)); } catch { }

                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;
                        if (c.Template.Id == Card.Cards.RLK_534)
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-3600)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9970)); } catch { }
                        }
                        else if (c.Template.Id == Card.Cards.AT_022)
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(1600)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-1600)); } catch { }
                        }
                    }

                    AddLog("[全局硬规则-斩杀压拳] 敌方血池=" + enemyHpArmorFinal
                        + "，灵魂弹幕与加拉克苏斯之拳同回合可打 => 强制灵魂弹幕优先");
                }
            }
            catch { }

            // 全局终局护栏：超光子与灵魂弹幕都可打且同伤害窗口时，固定优先超光子。
            try
            {
                int enemyHpArmorFinalForPhoton = 0;
                int spellPowerFinalForPhoton = 0;
                int enemyTargetsFinalForPhoton = 1;
                int photonEstimatedDamageFinal = 0;
                int barrageEstimatedDamageFinal = 0;

                try
                {
                    enemyHpArmorFinalForPhoton = board.HeroEnemy != null
                        ? (board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor)
                        : 0;
                }
                catch { enemyHpArmorFinalForPhoton = 0; }
                try
                {
                    spellPowerFinalForPhoton = board.MinionFriend != null
                        ? board.MinionFriend.Where(m => m != null).Sum(m => m.SpellPower)
                        : 0;
                }
                catch { spellPowerFinalForPhoton = 0; }
                try
                {
                    enemyTargetsFinalForPhoton = Math.Max(1, 1 + (board.MinionEnemy != null
                        ? board.MinionEnemy.Count(m => m != null)
                        : 0));
                }
                catch { enemyTargetsFinalForPhoton = 1; }
                try
                {
                    photonEstimatedDamageFinal = GetEstimatedFaceDamageForMode(Card.Cards.TIME_027, spellPowerFinalForPhoton, enemyTargetsFinalForPhoton);
                    barrageEstimatedDamageFinal = GetEstimatedFaceDamageForMode(Card.Cards.RLK_534, spellPowerFinalForPhoton, enemyTargetsFinalForPhoton);
                }
                catch
                {
                    photonEstimatedDamageFinal = 0;
                    barrageEstimatedDamageFinal = 0;
                }

                bool canPlayPhotonFinal = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == Card.Cards.TIME_027
                    && c.CurrentCost <= board.ManaAvailable);
                bool canPlayBarrageFinal = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == Card.Cards.RLK_534
                    && c.CurrentCost <= board.ManaAvailable);

                if (lethalThisTurn
                    && enemyHpArmorFinalForPhoton > 0
                    && canPlayPhotonFinal
                    && canPlayBarrageFinal
                    && photonEstimatedDamageFinal >= barrageEstimatedDamageFinal)
                {
                    SetSoulBarrageDirectPlayModifier(p, board, 9999, -9999);
                    try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(-3600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_027, new Modifier(9980)); } catch { }

                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;
                        if (c.Template.Id == Card.Cards.RLK_534)
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(1600)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-1600)); } catch { }
                        }
                        else if (c.Template.Id == Card.Cards.TIME_027)
                        {
                            try { p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(-3600)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(9980)); } catch { }
                        }
                    }

                    AddLog("[全局硬规则-等伤取舍] 敌方血池=" + enemyHpArmorFinalForPhoton
                        + "，超光子预估=" + photonEstimatedDamageFinal
                        + " >= 灵魂弹幕预估=" + barrageEstimatedDamageFinal
                        + " => 强制超光子优先");
                }
            }
            catch { }

            // ==================================================
            // 【全局硬规则】场上地标先手：
            // 当场上窟穴可点且未被禁用时，先暂缓所有手牌动作，避免“点地标+继续出牌”导致卡选择/卡死。
            // 口径：先点场上地标 -> 等选择结算 -> 重算后再出牌。
            // ==================================================
            try
            {
                bool hasExecutableBoardCave = false;
                var executableBoardCaves = new List<Card>();
                try
                {
                    if (board.MinionFriend != null)
                    {
                        executableBoardCaves = board.MinionFriend
                            .Where(c => c != null
                                && c.Template != null
                                && c.Template.Id == Card.Cards.WON_103
                                && GetTag(c, Card.GAME_TAG.EXHAUSTED) != 1
                                && !isDisabledInRulesSet(p.LocationsModifiers, Card.Cards.WON_103, c.Id))
                            .ToList();
                        hasExecutableBoardCave = executableBoardCaves.Count > 0
                            && !isDisabledInRulesSet(p.LocationsModifiers, Card.Cards.WON_103);
                    }
                }
                catch
                {
                    hasExecutableBoardCave = false;
                    executableBoardCaves = new List<Card>();
                }

                if (hasExecutableBoardCave)
                {
                    const int handCastDelay = 2600;
                    const int handOrderDelay = -2600;

                    if (board.Hand != null && board.Hand.Count > 0)
                    {
                        foreach (var h in board.Hand)
                        {
                            if (h == null || h.Template == null) continue;

                            try
                            {
                                if (h.Type == Card.CType.MINION)
                                {
                                    p.CastMinionsModifiers.AddOrUpdate(h.Template.Id, new Modifier(handCastDelay));
                                    p.CastMinionsModifiers.AddOrUpdate(h.Id, new Modifier(handCastDelay));
                                }
                                else if (h.Type == Card.CType.SPELL)
                                {
                                    p.CastSpellsModifiers.AddOrUpdate(h.Template.Id, new Modifier(handCastDelay));
                                    p.CastSpellsModifiers.AddOrUpdate(h.Id, new Modifier(handCastDelay));
                                }
                                else if (h.Type == Card.CType.WEAPON)
                                {
                                    p.CastWeaponsModifiers.AddOrUpdate(h.Template.Id, new Modifier(handCastDelay));
                                    p.CastWeaponsModifiers.AddOrUpdate(h.Id, new Modifier(handCastDelay));
                                }
                                else
                                {
                                    p.LocationsModifiers.AddOrUpdate(h.Template.Id, new Modifier(handCastDelay));
                                    p.LocationsModifiers.AddOrUpdate(h.Id, new Modifier(handCastDelay));
                                }
                            }
                            catch { }

                            try { p.PlayOrderModifiers.AddOrUpdate(h.Template.Id, new Modifier(handOrderDelay)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(h.Id, new Modifier(handOrderDelay)); } catch { }
                        }
                    }

                    try { p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-3600)); } catch { }
                    foreach (var cave in executableBoardCaves)
                    {
                        if (cave == null) continue;
                        try { p.LocationsModifiers.AddOrUpdate(cave.Id, new Modifier(-3600)); } catch { }
                    }

                    // 点场上地标这一拍：同时禁用场上随从攻击与武器攻击，避免“点地标+攻击”导致选择流程卡住。
                    try
                    {
                        if (board.MinionFriend != null)
                        {
                            foreach (var fm in board.MinionFriend.Where(x => x != null && x.Template != null && x.CanAttack))
                            {
                                try { p.AttackOrderModifiers.AddOrUpdate(fm.Template.Id, new Modifier(-9999)); } catch { }
                                try { p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(fm.Template.Id, new Modifier(9999)); } catch { }
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        if (board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.CurrentDurability > 0)
                        {
                            var weaponId = board.WeaponFriend.Template.Id;
                            try { p.WeaponsAttackModifiers.AddOrUpdate(weaponId, new Modifier(9999)); } catch { }
                            BlockWeaponAttackForAllTargets(p, board, weaponId, 9999);
                        }
                    }
                    catch { }

                    p.ForceResimulation = true;
                    AddLog("[全局硬规则-场上地标先手] 场上地标可点且未禁用 => 暂缓所有手牌并禁用随从/武器攻击，先点地标后重算");
                }
            }
            catch { }

            // ==================================================
            // 重算ｅ收口?
            // 只保?ForcedResimulationCardList（手牌打出后重算，且已排??甯侊級锛?
            // 关闭?有与具体分支/场景?悎鐨?ForceResimulation，避免?滀氦甯?无关?后重复?濊?冣?濄??
            // ==================================================
            try
            {
                bool keepForceResimulationAtEnd = keepForceResimulationForDiscountPlan
                    || keepForceResimulationForAttackSequencing;
                if (disableResimForEarlyCoin)
                {
                    if (keepForceResimulationAtEnd)
                    {
                        AddLog("[全局重算收口] 3费前硬币模式但命中压费/攻击排序连段 => 保留 ForceResimulation");
                    }
                    else
                    {
                        p.ForceResimulation = false;
                        AddLog("[全局重算收口] 3费前硬币模式 => 关闭 ForceResimulation(交币不思考)");
                    }
                }
                else
                {
                    if (keepForceResimulationAtEnd)
                    {
                        AddLog("[全局重算收口] 命中压费/攻击排序连段 => 保留 ForceResimulation");
                    }
                    else
                    {
                        if (p.ForceResimulation)
                        {
                            AddLog("[全局重算收口] 关闭 ForceResimulation；仅保留手牌重算(硬币除外)");
                        }
                        p.ForceResimulation = false;
                    }
                }
            }
            catch { }
             
            return p;
        }

        #endregion

        #region 辅助工具方法实现

        // 无織寮?鍏筹細
        // - VerboseLog: 总开?
        // - CompactLog: 绱?噾妯?紡锛堥粯璁?启，压缩高频?并按“手牌存??濊繃婊?牌日志）
        // - MinimalLog: 仅保留关键日志（机器可用，极少量）
        private static readonly bool VerboseLog = true;
        private static readonly bool CompactLog = true;
        private static readonly bool MinimalLog = true;
        private const int CompactLogMaxLinesPerDecision = 20;

        private string _log = "";
        private int _compactLogLineCount = 0;
        private readonly HashSet<Card.Cards> _compactHandCardIds = new HashSet<Card.Cards>();

        private void ResetCompactLogContext(Board board)
        {
            _compactLogLineCount = 0;
            _compactHandCardIds.Clear();

            try
            {
                if (board == null || board.Hand == null) return;
                foreach (var c in board.Hand)
                {
                    if (c == null || c.Template == null) continue;
                    _compactHandCardIds.Add(c.Template.Id);
                }
            }
            catch { }
        }

        private bool HasAnyCardInCompactHand(params Card.Cards[] ids)
        {
            try
            {
                if (ids == null || ids.Length == 0) return false;
                foreach (var id in ids)
                {
                    if (_compactHandCardIds.Contains(id)) return true;
                }
            }
            catch { }
            return false;
        }

        private bool ShouldSuppressByCompactPrefix(string message)
        {
            if (string.IsNullOrEmpty(message)) return true;

            // 高频且复盘价值低的噪声日?
            var noisyPrefixes = new[]
            {
                "[手牌]",
                "[时空之爪-判定]",
                "[武器-判定]",
                "[全局强制重算]",
                "[全局实例优先]",
                "[全局临时牌]",
                "[全局重算收口]",
	                "[威胁值] 已设置敌方关键随从技能权重",
            };

            return noisyPrefixes.Any(prefix => message.StartsWith(prefix, StringComparison.Ordinal));
        }

        private bool IsCardLogAllowedByHandInCompactMode(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;

            // 仅在该卡在手时输出其对应卡牌日志（紧凑模式）
            if (message.StartsWith("[过牌优先级]", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.TLC_603, Card.Cards.LOOT_014);

            if (message.StartsWith("[时空之爪", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.END_016);
            if (message.StartsWith("[灵魂之火", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.EX1_308);
            if (message.StartsWith("[超光子弹幕", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.TIME_027);
            if (message.StartsWith("[灵魂弹幕", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.RLK_534);
            if (message.StartsWith("[狂暴邪翼蝠", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.YOD_032);
            if (message.StartsWith("[维希度斯的窟穴", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.WON_103);
            if (message.StartsWith("[过期货物专卖商", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.ULD_163);
            if (message.StartsWith("[恐怖海盗", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.CORE_NEW1_022);
            if (message.StartsWith("[咒怨之墓", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.TLC_451);
            if (message.StartsWith("[船载火炮", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.CORE_NEW1_023, Card.Cards.GVG_075);
            if (message.StartsWith("[速写-估算]", StringComparison.Ordinal) || message.StartsWith("[速写美术家", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.TOY_916);
            if (message.StartsWith("[宝藏经销商", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.TOY_518);
            if (message.StartsWith("[太空海盗", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.GDB_333);
            if (message.StartsWith("[乐器技师", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.ETC_418);
            if (message.StartsWith("[郊狼", StringComparison.Ordinal) || message.StartsWith("[攻击顺序-郊狼压费]", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.TIME_047);
            if (message.StartsWith("[空降歹徒", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.DRG_056);

            // 与卡绑定的全局护栏日志
            if (message.StartsWith("[全局终极护栏-灵魂弹幕]", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.RLK_534);
            if (message.StartsWith("[全局硬规则提刀]", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.END_016, Card.Cards.CORE_NEW1_022);
            if (message.StartsWith("[全局硬规则武器]", StringComparison.Ordinal)
                || message.StartsWith("[全局硬规则武器禁攻]", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.END_016);
            if (message.StartsWith("[全局硬规则窟穴前置]", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.WON_103);
            if (message.StartsWith("[全局硬规则魂火]", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.EX1_308);
            if (message.StartsWith("[全局硬规则先挥刀]", StringComparison.Ordinal))
                return HasAnyCardInCompactHand(Card.Cards.END_016);

            return true;
        }

        private bool ShouldAllowMinimalLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            if (message.Contains("异常")) return true;
            if (message.StartsWith("[对局模式-动态]", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[手牌快照]", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[场面快照]", StringComparison.Ordinal)) return true;
            if (message.StartsWith("全局策略：", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[保命护栏]", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[全局硬规则", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[灵魂之火", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[灵魂弹幕", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[超光子弹幕", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[维希度斯的窟穴", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[咒怨之墓", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[分流兜底", StringComparison.Ordinal)) return true;
            if (message.StartsWith("[投降", StringComparison.Ordinal)) return true;
            if (message.Contains("决策完成")) return true;
            return false;
        }

        private void AddLog(string message)
        {
            if (!VerboseLog) return;
            if (string.IsNullOrEmpty(message)) return;

            bool isReplaySnapshot =
                message.StartsWith("[手牌快照]", StringComparison.Ordinal) ||
                message.StartsWith("[场面快照]", StringComparison.Ordinal);

            if (MinimalLog && !ShouldAllowMinimalLog(message)) return;
            if (CompactLog)
            {
                if (!isReplaySnapshot)
                {
                    if (ShouldSuppressByCompactPrefix(message)) return;
                    if (!IsCardLogAllowedByHandInCompactMode(message)) return;
                    if (_compactLogLineCount >= CompactLogMaxLinesPerDecision) return;
                }
            }

            _log += message + "\n";
            try { Bot.Log(message); } catch { }
            if (CompactLog) _compactLogLineCount++;
        }

        private void LogHandCards(Board board)
        {
            try
            {
                if (!VerboseLog) return;
                if (board == null || board.Hand == null) return;

                // 单行压缩输出，便于复盘粘?
                var parts = new List<string>();
                for (int i = 0; i < board.Hand.Count; i++)
                {
                    var c = board.Hand[i];
                    if (c == null || c.Template == null) continue;

                    bool isTemp = false;
                    bool byShiftingEnchant = false;
                    try { isTemp = IsTemporaryCard(board, c, out byShiftingEnchant); } catch { isTemp = false; }

                    string name = "";
                    try { name = !string.IsNullOrEmpty(c.Template.NameCN) ? c.Template.NameCN : c.Template.Id.ToString(); }
                    catch { name = c.Template.Id.ToString(); }

                    string tempTag = "Temp=" + (isTemp ? "Y" : "N") + (isTemp && byShiftingEnchant ? "(shift)" : "");
                    parts.Add("#" + i
                        + "|" + name
                        + "|费" + c.CurrentCost
                        + "|Id=" + c.Template.Id
                        + "|Inst=" + c.Id
                        + "|" + tempTag);
                }

                AddLog("[手牌] " + (parts.Count > 0 ? string.Join(" ; ", parts) : "(空)"));
            }
            catch (Exception ex)
            {
                AddLog("[手牌] 打印异常: " + ex.Message);
            }
        }

        private void LogDiscardComponentsInHand(Board board, HashSet<Card.Cards> discardComponents, bool merchantCanSacrifice)
        {
            try
            {
                if (!VerboseLog) return;
                if (board == null || board.Hand == null) return;
                if (discardComponents == null || discardComponents.Count == 0)
                {
                    AddLog("[被弃组件] 组件集合为空（未计算/无配置）");
                    return;
                }

                var nonCoinHand = GetHandCardsForDiscardLogic(board, true);
                int nonCoinCount = nonCoinHand.Count;

                var componentCards = nonCoinHand
                    .Where(h => discardComponents.Contains(h.Template.Id))
                    .ToList();
                int componentCount = componentCards.Count;

                double ratio = nonCoinCount > 0 ? (double)componentCount / nonCoinCount : 0.0;

                // 统计每个组件 ID 的数量（例如 RLK_534x2锛?
                var idCounts = new Dictionary<Card.Cards, int>();
                foreach (var c in componentCards)
                {
                    var id = c.Template.Id;
                    if (!idCounts.ContainsKey(id)) idCounts[id] = 0;
                    idCounts[id]++;
                }

                // 场上可?死专卖商：?里的被弃组件也视为?手上有被弃组件”（?复盘占比?
                try
                {
                    if (merchantCanSacrifice && board.MinionFriend != null)
                    {
                        foreach (var m in board.MinionFriend.ToArray())
                        {
                            if (m == null || m.Template == null) continue;
                            if (m.Template.Id != Card.Cards.ULD_163) continue;

                            var ench = m.Enchantments != null
                                ? m.Enchantments.FirstOrDefault(x => x != null
                                    && x.EnchantCard != null && x.EnchantCard.Template != null
                                    && x.EnchantCard.Template.Id == Card.Cards.ETC_424)
                                : null;
                            if (ench == null || ench.DeathrattleCard == null || ench.DeathrattleCard.Template == null) continue;

                            var drId = ench.DeathrattleCard.Template.Id;
                            if (!discardComponents.Contains(drId)) continue;

                            bool drAlreadyInHand = nonCoinHand.Any(h => h != null && h.Template != null && h.Template.Id == drId);
                            if (drAlreadyInHand) continue;

                            componentCount += 1;
                            if (!idCounts.ContainsKey(drId)) idCounts[drId] = 0;
                            idCounts[drId] += 1;
                        }

                        ratio = nonCoinCount > 0 ? (double)componentCount / nonCoinCount : 0.0;
                    }
                }
                catch { }

                string idList = idCounts.Count > 0
                    ? string.Join(", ", idCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.ToString())
                        .Select(kv => kv.Key + "x" + kv.Value))
                    : "(无";

                AddLog("[被弃组件] 占比=" + componentCount + "/" + nonCoinCount + "=" + (ratio * 100).ToString("F0") + "% | Id=" + idList);
            }
            catch (Exception ex)
            {
                AddLog("[被弃组件] 打印异常: " + ex.Message);
            }
        }

        private bool HasShiftingEnchantment(Card card)
        {
            try
            {
                if (card == null) return false;
                if (card.Enchantments == null) return false;

                var ench = card.Enchantments.FirstOrDefault(x => x != null
                    && x.EnchantCard != null
                    && x.EnchantCard.Template != null);
                return ench != null;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 同名手牌实例优先级：
        /// 1) 默认右侧优先（更靠右的实例先用）?
        /// 2) 鑻?组存?时牌，则临时牌优先；
        ///    排除组：硬币/被弃组件/郊狼/狂暴邪翼蝠；
        /// 3) 仅调实例 PlayOrder，不改?板级优先级避免干扰各卡专属逻辑?
        /// </summary>
        private void ApplyDuplicateHandCardInstancePreference(ProfileParameters p, Board board, HashSet<Card.Cards> discardComponents)
        {
            try
            {
                if (p == null || board == null || board.Hand == null || board.Hand.Count <= 1) return;

                // 记录手牌位置：索引越大越靠右
                var handPos = new Dictionary<int, int>();
                for (int i = 0; i < board.Hand.Count; i++)
                {
                    var c = board.Hand[i];
                    if (c == null) continue;
                    handPos[c.Id] = i;
                }

                var validHand = board.Hand
                    .Where(c => c != null && c.Template != null)
                    .ToList();

                var duplicateGroups = validHand
                    .GroupBy(c => c.Template.Id)
                    .Where(g => g.Count() >= 2)
                    .ToList();

                foreach (var group in duplicateGroups)
                {
                    var cards = group.ToList();
                    if (cards.Count < 2) continue;

                    bool isDiscardComponent = false;
                    try { isDiscardComponent = discardComponents != null && discardComponents.Contains(group.Key); } catch { isDiscardComponent = false; }
                    bool blockTemporaryPreference = isDiscardComponent
                        || group.Key == Card.Cards.GAME_005
                        || group.Key == Card.Cards.TIME_047
                        || group.Key == Card.Cards.YOD_032;

                    Card keep = null;
                    bool usedTemporaryPreference = false;

                    // 非排?：若同组有临时牌，优先临时牌（同为临时则右侧优先级
                    if (!blockTemporaryPreference)
                    {
                        try
                        {
                            var temporaryInGroup = cards
                                .Where(c =>
                                {
                                    try { return IsTemporaryCard(board, c); }
                                    catch { return false; }
                                })
                                .OrderByDescending(c => handPos.ContainsKey(c.Id) ? handPos[c.Id] : -1)
                                .ToList();

                            if (temporaryInGroup.Count > 0)
                            {
                                keep = temporaryInGroup[0];
                                usedTemporaryPreference = true;
                            }
                        }
                        catch { }
                    }

                    // 默认：右?紭鍏?
                    if (keep == null)
                    {
                        keep = cards
                            .OrderByDescending(c => handPos.ContainsKey(c.Id) ? handPos[c.Id] : -1)
                            .FirstOrDefault();
                    }

                    if (keep == null) continue;

                    // 仅实例级处理：保留实例给轻微ｆ重，其余同名实例强后?
                    foreach (var c in cards)
                    {
                        if (c == null) continue;
                        if (c.Id == keep.Id)
                        {
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(20)); } catch { }
                        }
                        else
                        {
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                        }
                    }

	                    string mode = usedTemporaryPreference ? "临时牌优?" : "右侧优先";
                    AddLog("[全局实例优先] " + group.Key + " x" + cards.Count
                        + " => " + mode + " | keep=" + keep.Id + " | others=-9999");
                }
            }
            catch { }
        }

        // 甯?因输出的临时牌判定（?簬无織锛?
        private bool IsTemporaryCard(Board board, Card card, out bool byShiftingEnchant)
        {
            byShiftingEnchant = false;
            bool byTrackedEntity = false;

            if (card == null || board == null) return false;

            try { byShiftingEnchant = HasShiftingEnchantment(card); }
            catch { byShiftingEnchant = false; }

            try { byTrackedEntity = _temporaryHandEntityIdsThisTurn.Contains(card.Id); }
            catch { byTrackedEntity = false; }

            return byShiftingEnchant || byTrackedEntity;
        }

        // 卡牌名称
        private string CardName(Card.Cards cardId)
        {
            try
            {
                var card = CardTemplate.LoadFromId(cardId);
                return card?.NameCN ?? cardId.ToString();
            }
            catch
            {
                return cardId.ToString();
            }
        }

        private int GetFriendlyHpArmor(Board board)
        {
            try
            {
                if (board == null || board.HeroFriend == null) return 0;
                return board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
            }
            catch
            {
                return 0;
            }
        }

        private bool CanUseLifeTapNow(Board board)
        {
            try
            {
                if (board == null) return false;
                if (board.ManaAvailable < 2) return false;
                if (GetFriendlyHpArmor(board) <= 2) return false;

                // 部分时序下 Ability 可能为空；保持原口径，按“可尝试分流”处理。
                if (board.Ability == null) return true;

                return GetTag(board.Ability, Card.GAME_TAG.EXHAUSTED) == 0;
            }
            catch
            {
                return false;
            }
        }

        private void AddOrUpdateHeroPowerModifier(ProfileParameters p, Card.Cards heroPowerId, Modifier modifier)
        {
            try
            {
                if (p == null || modifier == null) return;
                
                // 尝试多种可能的英雄技能ID
                var lifeTapIds = new List<Card.Cards>
                {
                    Card.Cards.CS2_056_H1,
                    Card.Cards.HERO_07bp
                };

                // 尝试添加其他变体
                try
                {
                    lifeTapIds.Add((Card.Cards)Enum.Parse(typeof(Card.Cards), "CS2_056_H2", false));
                    lifeTapIds.Add((Card.Cards)Enum.Parse(typeof(Card.Cards), "CS2_056_H3", false));
                }
                catch { }

                foreach (var id in lifeTapIds)
                {
                    try
                    {
                        // 兼容当前编译接口：CastHeroPowerModifier.AddOrUpdate 的第二参数是 int。
                        p.CastHeroPowerModifier.AddOrUpdate(id, modifier.Value);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private int GetMaxCostInHand(Board board)
        {
            try
            {
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                if (handForDiscardLogic == null || handForDiscardLogic.Count == 0) return -1;
                return handForDiscardLogic.Max(h => h != null ? h.CurrentCost : -1);
            }
            catch
            {
                return -1;
            }
        }

        private List<Card> GetHighestCostCardsInHand(Board board, int maxCostInHand)
        {
            try
            {
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                if (handForDiscardLogic.Count <= 0) return new List<Card>();

                int targetMaxCost = handForDiscardLogic.Max(h => h.CurrentCost);
                if (maxCostInHand >= 0 && handForDiscardLogic.Any(h => h.CurrentCost == maxCostInHand))
                    targetMaxCost = maxCostInHand;

                return handForDiscardLogic
                    .Where(h => h != null && h.Template != null && h.CurrentCost == targetMaxCost)
                    .ToList();
            }
            catch
            {
                return new List<Card>();
            }
        }

        private bool IsDeckEmptyNow(Board board)
        {
            try
            {
                if (board == null) return false;

                if (board.FriendDeckCount <= 0) return true;
                if (board.Deck != null && board.Deck.Count <= 0) return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// SW_091t4（终章衍生）生效判定。
        /// 生效后我方回合内疲劳伤害会转移给对手，因此 deck=0 仍允许继续过牌。
        /// </summary>
        private bool HasPlayedBlightbornTamsin(Board board)
        {
            if (board == null) return false;
            const string rewardId = "SW_091t4";

            try
            {
                if (board.MinionFriend != null && board.MinionFriend.Any(m =>
                    m != null && m.Template != null
                    && m.Template.Id.ToString().Equals(rewardId, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch { }

            try
            {
                if (board.FriendGraveyard != null && board.FriendGraveyard.Any(id =>
                    id.ToString().Equals(rewardId, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch { }

            try
            {
                if (board.PlayedCards != null && board.PlayedCards.Any(id =>
                    id.ToString().Equals(rewardId, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool AreAllHighestCards(List<Card> highestCards, Card.Cards cardId)
        {
            try
            {
                return highestCards != null
                    && highestCards.Count > 0
                    && highestCards.All(h => h != null && h.Template != null && h.Template.Id == cardId);
            }
            catch
            {
                return false;
            }
        }

        private int EstimateFriendlyImmediateRemovalCapacity(Board board)
        {
            if (board == null) return 0;

            int capacity = 0;

            // 场上可立即攻击的己方随从：可?当回合?鍦恒??
            try
            {
                if (board.MinionFriend != null)
                {
                    capacity += board.MinionFriend
                        .Where(m => m != null && m.CanAttack)
                        .Sum(m => Math.Max(0, m.CurrentAtk));
                }
            }
            catch { }

            // 英雄可攻击时，也可用于当回合ｅ満銆?
            try
            {
                if (board.HeroFriend != null && board.HeroFriend.CanAttack)
                    capacity += Math.Max(0, board.HeroFriend.CurrentAtk);
            }
            catch { }

            // 手牌可立即打出的直伤法术，按“最乐观”视角计??场能力??
            try
            {
                if (board.Hand != null)
                {
                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;
                        if (c.Type != Card.CType.SPELL) continue;
                        if (c.CurrentCost > board.ManaAvailable) continue;

                        if (c.Template.Id == Card.Cards.EX1_308) capacity += 4;       // 灵魂之火
                        else if (c.Template.Id == Card.Cards.TIME_027) capacity += 6; // 超光子弹幕
                        else if (c.Template.Id == Card.Cards.RLK_534) capacity += 5;  // 灵魂弹幕
                    }
                }
            }
            catch { }

            return Math.Max(0, capacity);
        }

        private int EstimateHandFaceDamagePotentialThisTurn(Board board)
        {
            if (board == null || board.Hand == null || board.Hand.Count == 0) return 0;

            try
            {
                int manaBudget = Math.Max(0, board.ManaAvailable);
                if (manaBudget <= 0) return 0;

                int enemyMinionCount = 0;
                try { enemyMinionCount = board.MinionEnemy != null ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0) : 0; } catch { enemyMinionCount = 0; }
                int enemyTargets = Math.Max(1, enemyMinionCount + 1);

                int spellPower = 0;
                try { spellPower = board.MinionFriend != null ? board.MinionFriend.Where(m => m != null).Sum(m => m.SpellPower) : 0; } catch { spellPower = 0; }
                if (spellPower < 0) spellPower = 0;

                var candidates = new List<Tuple<int, int>>();
                foreach (var c in board.Hand)
                {
                    if (c == null || c.Template == null) continue;
                    if (c.Type != Card.CType.SPELL) continue;
                    if (c.CurrentCost > manaBudget) continue;

                    int dmg = GetEstimatedFaceDamageForMode(c.Template.Id, spellPower, enemyTargets);
                    if (dmg <= 0) continue;

                    candidates.Add(Tuple.Create(Math.Max(0, c.CurrentCost), dmg));
                }

                if (candidates.Count == 0) return 0;

                // 0/1 背包：在当前法力内估算“本回合手牌直伤上限”
                int[] dp = new int[manaBudget + 1];
                foreach (var x in candidates)
                {
                    int cost = x.Item1;
                    int dmg = x.Item2;
                    for (int m = manaBudget; m >= cost; m--)
                    {
                        dp[m] = Math.Max(dp[m], dp[m - cost] + dmg);
                    }
                }

                return Math.Max(0, dp[manaBudget]);
            }
            catch
            {
                return 0;
            }
        }

        private int EstimateHandFaceDamageWithinManaExcluding(Board board, int manaBudget, Card.Cards excludedCardId)
        {
            if (board == null || board.Hand == null || board.Hand.Count == 0) return 0;
            if (manaBudget <= 0) return 0;

            try
            {
                int enemyMinionCount = 0;
                try { enemyMinionCount = board.MinionEnemy != null ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0) : 0; } catch { enemyMinionCount = 0; }
                int enemyTargets = Math.Max(1, enemyMinionCount + 1);

                int spellPower = 0;
                try { spellPower = board.MinionFriend != null ? board.MinionFriend.Where(m => m != null).Sum(m => m.SpellPower) : 0; } catch { spellPower = 0; }
                if (spellPower < 0) spellPower = 0;

                var candidates = new List<Tuple<int, int>>();
                foreach (var c in board.Hand)
                {
                    if (c == null || c.Template == null) continue;
                    if (c.Template.Id == excludedCardId) continue;
                    if (c.Type != Card.CType.SPELL) continue;
                    if (c.CurrentCost > manaBudget) continue;

                    int dmg = GetEstimatedFaceDamageForMode(c.Template.Id, spellPower, enemyTargets);
                    if (dmg <= 0) continue;

                    candidates.Add(Tuple.Create(Math.Max(0, c.CurrentCost), dmg));
                }

                if (candidates.Count == 0) return 0;

                int[] dp = new int[manaBudget + 1];
                foreach (var x in candidates)
                {
                    int cost = x.Item1;
                    int dmg = x.Item2;
                    for (int m = manaBudget; m >= cost; m--)
                    {
                        dp[m] = Math.Max(dp[m], dp[m - cost] + dmg);
                    }
                }

                return Math.Max(0, dp[manaBudget]);
            }
            catch
            {
                return 0;
            }
        }

        private int GetEstimatedFaceDamageForMode(Card.Cards id, int spellPower, int enemyTargets)
        {
            int raw = 0;
            bool randomDamage = false;

            switch (id)
            {
                case Card.Cards.EX1_308:   // 灵魂之火
                    raw = 4;
                    break;
                case Card.Cards.CORE_CS2_062: // 地狱烈焰
                    raw = 3;
                    break;
                case Card.Cards.TIME_027:  // 超光子弹幕（随机）
                    raw = 6;
                    randomDamage = true;
                    break;
                case Card.Cards.RLK_534:   // 灵魂弹幕（随机）
                    raw = 5;
                    randomDamage = true;
                    break;
                case Card.Cards.AT_022:    // 加拉克苏斯之拳（随机）
                    raw = 4;
                    randomDamage = true;
                    break;
                case Card.Cards.WW_044t:   // 淤泥桶（随机）
                    raw = 4;
                    randomDamage = true;
                    break;
                default:
                    raw = 0;
                    break;
            }

            if (raw <= 0) return 0;
            raw += Math.Max(0, spellPower);

            if (!randomDamage) return Math.Max(0, raw);
            if (enemyTargets <= 1) return Math.Max(0, raw);

            // 随机伤害按期望值近似，避免把随机直伤当作稳定脸伤高估
            return Math.Max(1, (int)Math.Round(raw / (double)enemyTargets));
        }

        private void ApplyDynamicCombatMode(
            ProfileParameters p,
            Board board,
            bool lethalThisTurn,
            int enemyHpArmor,
            int myHpArmor,
            int enemyMinionCount,
            int friendlyMinionCount,
            int enemyBoardAttack,
            int friendlyBoardAttack,
            int handDamagePotential,
            int enemyPotentialNextTurnDamage,
            bool secretEmergencyMode)
        {
            if (p == null || board == null) return;

            int baseAggro = p.GlobalAggroModifier != null ? p.GlobalAggroModifier.Value : 100;
            int aggro = baseAggro;
            int defense = p.GlobalDefenseModifier != null ? p.GlobalDefenseModifier.Value : 0;
            var reasons = new List<string>();

            if (lethalThisTurn)
            {
                aggro = Math.Max(aggro, 180);
                defense = Math.Min(defense, 300);
                reasons.Add("斩杀回合强进攻");
            }
            else
            {
                int enemyHp = Math.Max(0, enemyHpArmor);
                int myHp = Math.Max(0, myHpArmor);
                int boardDeltaCount = friendlyMinionCount - enemyMinionCount;
                int boardDeltaAttack = friendlyBoardAttack - enemyBoardAttack;
                int immediateReach = friendlyBoardAttack + handDamagePotential;
                int killGap = immediateReach - enemyHp;
                int survivalGap = myHp - enemyPotentialNextTurnDamage;

                // 动态连续评分：避免固定“档位”导致模式切换生硬
                double modeScore = 0.0;
                modeScore += boardDeltaCount * 5.5;
                modeScore += boardDeltaAttack * 2.2;
                modeScore += killGap * 1.8;
                modeScore += (myHp - enemyHp) * 0.6;

                if (enemyHp <= 18)
                    modeScore += Math.Min(10, handDamagePotential) * 1.2;

                if (enemyMinionCount >= 4)
                    modeScore -= (8 + Math.Max(0, enemyMinionCount - 3) * 3);

                if (friendlyMinionCount == 0 && enemyMinionCount >= 2)
                    modeScore -= 10;

                if (myHp <= 12)
                    modeScore -= (13 - myHp) * 4;

                if (enemyPotentialNextTurnDamage >= myHp && myHp > 0)
                    modeScore -= 35;
                else if (enemyPotentialNextTurnDamage >= Math.Max(6, myHp - 2) && myHp > 0)
                    modeScore -= 18;

                if (enemyHp <= 10 && handDamagePotential >= 4)
                    modeScore += 18;
                if (enemyHp <= 6 && handDamagePotential >= 3)
                    modeScore += 16;

                int aggroShift = (int)Math.Round(modeScore / 2.0);
                aggroShift = Math.Max(-80, Math.Min(80, aggroShift));
                aggro += aggroShift;

                if (survivalGap <= 0)
                {
                    defense = Math.Max(defense, 1100);
                    reasons.Add("下回合高危");
                }
                else if (survivalGap <= 3)
                {
                    defense = Math.Max(defense, 850);
                    reasons.Add("血线告急");
                }
                else if (myHp <= 12)
                {
                    defense = Math.Max(defense, 650);
                    reasons.Add("低血转稳");
                }
                else if (enemyMinionCount >= 4 && boardDeltaCount < 0)
                {
                    defense = Math.Max(defense, 500);
                    reasons.Add("敌方铺场压制");
                }

                reasons.Add("score=" + modeScore.ToString("0.0"));
                reasons.Add("shift=" + aggroShift);
            }

            // 奥秘硬护栏在动态评分后仍保持最高优先级
            if (secretEmergencyMode)
            {
                aggro = Math.Min(aggro, 50);
                defense = Math.Max(defense, 900);
                reasons.Add("奥秘护栏锁定");
            }

            aggro = Math.Max(20, Math.Min(190, aggro));
            defense = Math.Max(0, Math.Min(1400, defense));

            if (p.GlobalAggroModifier != null) p.GlobalAggroModifier.Value = aggro;
            else p.GlobalAggroModifier = new Modifier(aggro);

            if (defense > 0)
            {
                if (p.GlobalDefenseModifier != null)
                    p.GlobalDefenseModifier.Value = Math.Max(p.GlobalDefenseModifier.Value, defense);
                else
                    p.GlobalDefenseModifier = new Modifier(defense);
            }

            AddLog("[对局模式-动态] 敌从=" + enemyMinionCount
                + ",我从=" + friendlyMinionCount
                + ",敌血=" + enemyHpArmor
                + ",我血=" + myHpArmor
                + ",手伤=" + handDamagePotential
                + ",敌压=" + enemyPotentialNextTurnDamage
                + " => Aggro=" + aggro
                + (defense > 0 ? ",Defense=" + defense : "")
                + " | " + (reasons.Count > 0 ? string.Join(";", reasons) : "基线"));
        }

        private int EstimateEnemyPotentialDamageNextTurn(Board board)
        {
            if (board == null) return 0;

            int damage = 0;
            try
            {
                if (board.MinionEnemy != null)
                {
                    damage += board.MinionEnemy
                        .Where(m => m != null)
                        .Sum(m => Math.Max(0, m.CurrentAtk));
                }
            }
            catch { }

            try
            {
                if (board.WeaponEnemy != null && board.WeaponEnemy.CurrentDurability > 0)
                {
                    damage += Math.Max(0, board.WeaponEnemy.CurrentAtk);
                }
            }
            catch { }

            return Math.Max(0, damage);
        }

        private bool IsLikelyLethalNextTurn(Board board)
        {
            if (board == null || board.HeroEnemy == null) return false;

            try
            {
                int enemyHpArmor = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                if (enemyHpArmor <= 0) return true;

                int nextTurnMana = 0;
                try
                {
                    int baseNext = Math.Max(board.ManaAvailable, board.MaxMana) + 1;
                    nextTurnMana = Math.Min(10, Math.Max(0, baseNext));
                }
                catch { nextTurnMana = Math.Min(10, Math.Max(0, board.ManaAvailable + 1)); }

                bool enemyHasTaunt = false;
                try { enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { enemyHasTaunt = false; }

                int boardDamage = 0;
                if (!enemyHasTaunt)
                {
                    try
                    {
                        if (board.MinionFriend != null)
                        {
                            boardDamage = board.MinionFriend
                                .Where(m => m != null)
                                .Sum(m => Math.Max(0, m.CurrentAtk));
                        }
                    }
                    catch { boardDamage = 0; }
                }

                int weaponDamage = 0;
                if (!enemyHasTaunt)
                {
                    try
                    {
                        if (board.WeaponFriend != null && board.WeaponFriend.CurrentDurability > 0)
                        {
                            weaponDamage = Math.Max(0, board.WeaponFriend.CurrentAtk);
                        }
                    }
                    catch { weaponDamage = 0; }
                }

                var bursts = new List<Tuple<int, int>>();
                try
                {
                    if (board.Hand != null)
                    {
                        foreach (var c in board.Hand)
                        {
                            if (c == null || c.Template == null) continue;
                            int dmg = 0;
                            if (c.Template.Id == Card.Cards.EX1_308) dmg = 4;       // 灵魂之火
                            else if (c.Template.Id == Card.Cards.TIME_027) dmg = 6;  // 超光子弹幕
                            else if (c.Template.Id == Card.Cards.RLK_534) dmg = 5;   // 灵魂弹幕
                            if (dmg <= 0) continue;

                            int cost = Math.Max(0, c.CurrentCost);
                            if (cost > nextTurnMana) continue;
                            bursts.Add(Tuple.Create(cost, dmg));
                        }
                    }
                }
                catch { }

                int burstDamage = 0;
                int remainingMana = nextTurnMana;
                try
                {
                    foreach (var b in bursts
                        .OrderByDescending(x => (double)x.Item2 / Math.Max(1, x.Item1))
                        .ThenBy(x => x.Item1)
                        .ThenByDescending(x => x.Item2))
                    {
                        if (b.Item1 <= remainingMana)
                        {
                            remainingMana -= b.Item1;
                            burstDamage += b.Item2;
                        }
                    }
                }
                catch { }

                int totalDamage = boardDamage + weaponDamage + burstDamage;
                return totalDamage >= enemyHpArmor;
            }
            catch
            {
                return false;
            }
        }

        private string GetCardIdsForLog(List<Card> cards)
        {
            try
            {
                if (cards == null || cards.Count == 0) return "(空)";
                return string.Join(",", cards
                    .Where(c => c != null && c.Template != null)
                    .Select(c => c.Template.Id.ToString()));
            }
            catch
            {
                return "(解析失败)";
            }
        }

        private void BlockWeaponAttackForAllTargets(ProfileParameters p, Board board, Card.Cards weaponId, int modifierValue)
        {
            if (p == null || board == null) return;

            try
            {
                if (board.HeroEnemy != null)
                {
                    p.WeaponsAttackModifiers.AddOrUpdate(weaponId, modifierValue, board.HeroEnemy.Id);
                }
            }
            catch { }

            try
            {
                if (board.MinionEnemy != null)
                {
                    foreach (var m in board.MinionEnemy)
                    {
                        if (m == null || m.Template == null) continue;
                        p.WeaponsAttackModifiers.AddOrUpdate(weaponId, modifierValue, m.Id);
                    }
                }
            }
            catch { }

            try
            {
                p.WeaponsAttackModifiers.AddOrUpdate(weaponId, new Modifier(modifierValue));
            }
            catch { }
        }

        private bool IsOnlyCaveAndSpacePirateHand(Board board, out Card caveCard, out Card spacePirateCard)
        {
            caveCard = null;
            spacePirateCard = null;
            if (board == null || board.Hand == null || board.Hand.Count == 0) return false;

            try
            {
                var nonCoinCards = GetHandCardsForDiscardLogic(board, true);

                if (nonCoinCards.Count != 2) return false;

                caveCard = nonCoinCards.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103);
                spacePirateCard = nonCoinCards.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.GDB_333);
                return caveCard != null && spacePirateCard != null;
            }
            catch
            {
                caveCard = null;
                spacePirateCard = null;
                return false;
            }
        }

        // 地标弃牌收益评估口径：0费随从不计入收益池，避免其稀释“地标弃到目标组件”的判断。
        private bool ShouldIgnoreCardForCaveDiscardRatio(Card card)
        {
            try
            {
                return card != null
                    && card.Template != null
                    && card.Type == Card.CType.MINION
                    && card.CurrentCost == 0;
            }
            catch { return false; }
        }

        private bool TryPrioritizeLifeTapForClawBlock(ProfileParameters p, Board board, bool canTapNow, string logPrefix)
        {
            if (!canTapNow || p == null || board == null) return false;

            int handCountLocal = 0;
            try { handCountLocal = board.Hand != null ? board.Hand.Count : 0; } catch { handCountLocal = 0; }
            if (handCountLocal > 9) return false;

            try
            {
                Card caveOnly;
                Card pirateOnly;
                if (IsOnlyCaveAndSpacePirateHand(board, out caveOnly, out pirateOnly))
                {
                    bool hasCoin = false;
                    try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }
                    int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);
                    bool cavePlayableNow = caveOnly != null
                        && caveOnly.CurrentCost <= effectiveMana
                        && GetFriendlyBoardSlotsUsed(board) < 7;
                    if (cavePlayableNow)
                    {
                        AddLog(logPrefix + "；手里仅地标+太空海盗 => 不先分流，优先地标连段");
                        return false;
                    }
                }
            }
            catch { }

            // 提刀窗口优先：若本回合可直接提刀，且手里已有海盗承接或弃牌收益组件，不应先分流。
            // 目的：修正“可提刀却先抽一口”导致节奏丢失的问题。
            try
            {
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                bool hasClawEquipped = board.WeaponFriend != null
                    && board.WeaponFriend.Template != null
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                    && board.WeaponFriend.CurrentDurability > 0;

                bool canEquipClawNow = !hasClawEquipped
                    && handForDiscardLogic.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.END_016
                        && h.CurrentCost <= board.ManaAvailable);

                bool hasPirateInHand = handForDiscardLogic.Any(h => h != null
                    && h.Template != null
                    && h.IsRace(Card.CRace.PIRATE));

                bool hasKnownDiscardPayoff = handForDiscardLogic.Any(h => h != null
                    && h.Template != null
                    && (h.Template.Id == Card.Cards.RLK_534
                        || h.Template.Id == Card.Cards.BT_300
                        || h.Template.Id == Card.Cards.WW_441));

                if (canEquipClawNow && (hasPirateInHand || hasKnownDiscardPayoff))
                {
                    AddLog(logPrefix + "；本回合可提刀且手里有海盗/弃牌收益 => 不先分流，优先提刀");
                    return false;
                }
            }
            catch { }

            try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(-2000)); } catch { }
            p.ForceResimulation = true;
            AddLog(logPrefix + "；且可分流(手牌" + handCountLocal + ") => 先分流(-2000)并强制重算");
            return true;
        }

        /// <summary>
        /// 海星解嘲讽推进弃牌线：
        /// 敌方有嘲讽时，若本回合可先海星沉默，再走脸压费转化为“稳定挥刀弃弹幕”，
        /// 则海星应前置，灵魂弹幕应后置禁直拍。
        /// </summary>
        private bool ShouldPrioritizeStarfishForTauntBreakDiscardSetup(
            Board board, bool lethalThisTurn, int enemyTauntCount, int friendlyReadyAttack, out string reason)
        {
            reason = null;
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;
                if (lethalThisTurn) return false;
                if (enemyTauntCount <= 0) return false;
                if (friendlyReadyAttack <= 0) return false;

                var starfishes = board.Hand.Where(c => c != null
                    && c.Template != null
                    && c.Template.Id == Card.Cards.TSC_926).ToList();
                if (starfishes.Count == 0) return false;

                bool hasBoardSlot = false;
                try { hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7; } catch { hasBoardSlot = false; }
                if (!hasBoardSlot) return false;

                int minStarfishCost = 99;
                try { minStarfishCost = starfishes.Min(c => Math.Max(0, c.CurrentCost)); } catch { minStarfishCost = 99; }
                if (minStarfishCost > board.ManaAvailable) return false;

                bool hasSoulBarrageInHand = false;
                try
                {
                    hasSoulBarrageInHand = board.Hand.Any(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.RLK_534);
                }
                catch { hasSoulBarrageInHand = false; }
                if (!hasSoulBarrageInHand) return false;

                bool hasClawEquipped = false;
                try
                {
                    hasClawEquipped = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0;
                }
                catch { hasClawEquipped = false; }
                if (!hasClawEquipped) return false;

                bool heroLikelyCanAttack = false;
                try
                {
                    heroLikelyCanAttack = board.HeroFriend != null
                        && !board.HeroFriend.IsFrozen
                        && board.HeroFriend.CountAttack == 0;
                }
                catch
                {
                    heroLikelyCanAttack = board.HeroFriend != null && !board.HeroFriend.IsFrozen;
                }
                if (!heroLikelyCanAttack) return false;

                var discardSet = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                if (discardSet.Count == 0) return false;

                bool coyoteUnlocksClawDiscard = false;
                try { coyoteUnlocksClawDiscard = CanCoyoteUnlockClawDiscardWindow(board, discardSet); } catch { coyoteUnlocksClawDiscard = false; }
                if (!coyoteUnlocksClawDiscard) return false;

                reason = "敌方嘲讽阻断走脸，海星可解嘲讽后触发郊狼压费并稳定挥刀弃弹幕";
                return true;
            }
            catch
            {
                reason = null;
                return false;
            }
        }

        private bool CanCoyoteUnlockClawDiscardWindow(Board board, HashSet<Card.Cards> discardComponents)
        {
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;
                if (discardComponents == null || discardComponents.Count == 0) return false;
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                if (handForDiscardLogic.Count == 0) return false;

                bool hasClawEquipped = board.WeaponFriend != null
                    && board.WeaponFriend.Template != null
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                    && board.WeaponFriend.CurrentDurability > 0;
                if (!hasClawEquipped) return false;

                bool heroLikelyCanAttack = false;
                try
                {
                    heroLikelyCanAttack = board.HeroFriend != null
                        && !board.HeroFriend.IsFrozen
                        && board.HeroFriend.CountAttack == 0;
                }
                catch
                {
                    heroLikelyCanAttack = board.HeroFriend != null && !board.HeroFriend.IsFrozen;
                }
                if (!heroLikelyCanAttack) return false;

                var playableCoyotes = handForDiscardLogic
                    .Where(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.TIME_047
                        && h.CurrentCost > 0
                        && h.CurrentCost <= board.ManaAvailable)
                    .ToList();
                if (playableCoyotes.Count == 0) return false;

                int maxNow = handForDiscardLogic.Max(h => h != null ? h.CurrentCost : -1);
                if (maxNow < 0) return false;

                var highestNow = handForDiscardLogic
                    .Where(h => h != null && h.Template != null && h.CurrentCost == maxNow)
                    .ToList();
                if (highestNow.Count == 0 || !highestNow.Any(h => h.Template.Id == Card.Cards.TIME_047)) return false;

                foreach (var coyote in playableCoyotes.OrderByDescending(h => h.CurrentCost))
                {
                    var handAfter = handForDiscardLogic
                        .Where(h => h != null && h.Template != null && h.Id != coyote.Id)
                        .ToList();
                    if (handAfter.Count == 0) continue;

                    int maxAfter = handAfter.Max(h => h.CurrentCost);
                    var highestAfter = handAfter.Where(h => h.CurrentCost == maxAfter).ToList();
                    if (highestAfter.Count == 0) continue;

                    if (highestAfter.All(h => discardComponents.Contains(h.Template.Id)))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private bool CanFelwingUnlockClawDiscardWindow(Board board, HashSet<Card.Cards> discardComponents)
        {
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;
                if (discardComponents == null || discardComponents.Count == 0) return false;

                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                if (handForDiscardLogic.Count == 0) return false;

                bool hasClawEquipped = board.WeaponFriend != null
                    && board.WeaponFriend.Template != null
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016
                    && board.WeaponFriend.CurrentDurability > 0;
                if (!hasClawEquipped) return false;

                bool heroLikelyCanAttack = false;
                try
                {
                    heroLikelyCanAttack = board.HeroFriend != null
                        && !board.HeroFriend.IsFrozen
                        && board.HeroFriend.CountAttack == 0;
                }
                catch
                {
                    heroLikelyCanAttack = board.HeroFriend != null && !board.HeroFriend.IsFrozen;
                }
                if (!heroLikelyCanAttack) return false;

                bool enemyHasTaunt = false;
                try { enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { enemyHasTaunt = false; }
                if (enemyHasTaunt) return false;

                int potentialFaceDiscount = 0;
                try
                {
                    if (board.MinionFriend != null)
                    {
                        potentialFaceDiscount = board.MinionFriend
                            .Where(m => m != null && m.CanAttack && m.CurrentAtk > 0)
                            .Sum(m => Math.Max(0, m.CurrentAtk));
                    }
                }
                catch { potentialFaceDiscount = 0; }
                if (potentialFaceDiscount <= 0) return false;

                bool hasDiscountableFelwing = false;
                try
                {
                    hasDiscountableFelwing = handForDiscardLogic.Any(h => h != null
                        && h.Template != null
                        && IsFelwingCardVariant(h)
                        && h.CurrentCost > 0);
                }
                catch { hasDiscountableFelwing = false; }
                if (!hasDiscountableFelwing) return false;

                var discardComponentsForWeapon = new HashSet<Card.Cards>(discardComponents.Where(id => id != Card.Cards.WON_103));
                if (discardComponentsForWeapon.Count == 0) return false;

                int maxNow = handForDiscardLogic.Max(h => h != null ? h.CurrentCost : -1);
                if (maxNow < 0) return false;
                bool hasDiscardAtMaxNow = handForDiscardLogic.Any(h => h != null
                    && h.Template != null
                    && h.CurrentCost == maxNow
                    && discardComponentsForWeapon.Contains(h.Template.Id));

                int maxAfter = -1;
                bool hasDiscardAtMaxAfter = false;
                bool anyFelwingActuallyDiscounted = false;

                foreach (var c in handForDiscardLogic)
                {
                    if (c == null || c.Template == null) continue;

                    int adjustedCost = c.CurrentCost;
                    if (IsFelwingCardVariant(c) && c.CurrentCost > 0)
                    {
                        int projected = Math.Max(0, c.CurrentCost - potentialFaceDiscount);
                        if (projected < c.CurrentCost) anyFelwingActuallyDiscounted = true;
                        adjustedCost = projected;
                    }

                    if (adjustedCost > maxAfter)
                    {
                        maxAfter = adjustedCost;
                        hasDiscardAtMaxAfter = discardComponentsForWeapon.Contains(c.Template.Id);
                    }
                    else if (adjustedCost == maxAfter && discardComponentsForWeapon.Contains(c.Template.Id))
                    {
                        hasDiscardAtMaxAfter = true;
                    }
                }

                if (!anyFelwingActuallyDiscounted) return false;
                if (maxAfter < 0 || !hasDiscardAtMaxAfter) return false;
                if (hasDiscardAtMaxNow) return false;
                return true;
            }
            catch { }

            return false;
        }

        private bool ShouldAllowSoulfireStarterOverride(Board board, HashSet<Card.Cards> discardComponents, bool lethalThisTurn,
            out string reason, out int discardCount, out int totalCount)
        {
            reason = null;
            discardCount = 0;
            totalCount = 0;

            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;
                if (lethalThisTurn) return false;

                var playableSoulfire = board.Hand.FirstOrDefault(c => c != null && c.Template != null
                    && c.Template.Id == Card.Cards.EX1_308 && c.CurrentCost <= board.ManaAvailable);
                if (playableSoulfire == null) return false;

                var discardSet = discardComponents != null && discardComponents.Count > 0
                    ? new HashSet<Card.Cards>(discardComponents)
                    : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                if (discardSet.Count == 0) return false;

                var nonSoulfireNonCoinCards = GetHandCardsForDiscardLogic(board, true)
                    .Where(c => c != null && c.Template != null
                        && c.Template.Id != Card.Cards.EX1_308)
                    .ToList();
                totalCount = nonSoulfireNonCoinCards.Count;
                if (totalCount <= 0) return false;

                discardCount = nonSoulfireNonCoinCards.Count(c => discardSet.Contains(c.Template.Id));

                // 场压保命ｅ緞锛氬満闈?力显著且手牌被弃密度较高时，允许魂火应???鍦恒??
                // 目的：避免?规则过??鑷寸?杩団?濓紝鍦?压回合给出应?嚭鍙ｃ??
                bool highDiscardDensity = false;
                int enemyMinionCount = 0;
                int enemyAttackTotal = 0;
                int myHpArmor = 0;
                bool enemyHasBigThreat = false;
                bool highBoardPressure = false;
                try
                {
                    highDiscardDensity = discardCount >= 3 && (discardCount * 2 >= totalCount);

                    if (board.MinionEnemy != null)
                    {
                        enemyMinionCount = board.MinionEnemy.Count(m => m != null && m.Template != null);
                        enemyAttackTotal = board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk));
                        enemyHasBigThreat = board.MinionEnemy.Any(m => m != null && m.CurrentAtk >= 5);
                    }

                    myHpArmor = board.HeroFriend != null ? (board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) : 0;

                    highBoardPressure = enemyMinionCount >= 3
                        || enemyAttackTotal >= 8
                        || (enemyAttackTotal >= 5 && myHpArmor <= 20)
                        || (enemyHasBigThreat && enemyMinionCount >= 2)
                        || (enemyHasBigThreat && myHpArmor <= 18)
                        || (myHpArmor <= 14 && enemyMinionCount >= 2);
                }
                catch
                {
                    highDiscardDensity = false;
                    highBoardPressure = false;
                }

                if (highBoardPressure && highDiscardDensity)
                {
                    reason = "场压保命(敌攻=" + enemyAttackTotal + ",敌从=" + enemyMinionCount + ",我血=" + myHpArmor + ")";
                    return true;
                }

                bool hasBarrageInHand = board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);
                if (!hasBarrageInHand) return false;

                // 鍔??佽?鍒?1)：只有?除魂火外全是被弃组件=时，魂火才算启?粍浠躲??
                if (discardCount < totalCount) return false;

                bool hasCoin = false;
                try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }
                int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);

                bool highestDiscardRatioHalfOrMore = false;
                try
                {
                    int tmpHighestDiscard = 0, tmpHighestTotal = 0;
                    highestDiscardRatioHalfOrMore = IsHighestCostDiscardRatioAtLeastHalf(
                        board, discardSet, out tmpHighestDiscard, out tmpHighestTotal);
                }
                catch
                {
                    highestDiscardRatioHalfOrMore = false;
                }

                bool clawCanAttackNow = false;
                try
                {
                    clawCanAttackNow = board.WeaponFriend != null && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;
                }
                catch { clawCanAttackNow = false; }

                bool clawStarterByHighestHalf = false;
                try
                {
                    bool hasClawInHandAndReady = board.Hand.Any(c => c != null && c.Template != null
                        && c.Template.Id == Card.Cards.END_016
                        && c.CurrentCost <= effectiveMana)
                        && effectiveMana >= 4;
                    clawStarterByHighestHalf = highestDiscardRatioHalfOrMore && (hasClawInHandAndReady || clawCanAttackNow);
                }
                catch { clawStarterByHighestHalf = false; }

                bool canClickCaveNow = false;
                try
                {
                    var caveOnBoard = board.MinionFriend?.FirstOrDefault(x => x != null && x.Template != null
                        && x.Template.Id == Card.Cards.WON_103
                        && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
                    canClickCaveNow = caveOnBoard != null;
                }
                catch { canClickCaveNow = false; }

                bool canPlayCaveFromHandNow = false;
                try
                {
                    var caveInHand = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103);
                    canPlayCaveFromHandNow = caveInHand != null
                        && caveInHand.CurrentCost <= effectiveMana
                        && GetFriendlyBoardSlotsUsed(board) < 7;
                }
                catch { canPlayCaveFromHandNow = false; }

                bool stableMerchantNow = false;
                try
                {
                    bool hasSpace = GetFriendlyBoardSlotsUsed(board) < 7;
                    var merchant = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.ULD_163);
                    bool merchantPlayableNow = merchant != null && hasSpace && merchant.CurrentCost <= effectiveMana;
                    // 鍔??佽?鍒?2)：最高费组被弃组件占?=1/2 时，商贩?启动组件?
                    stableMerchantNow = merchantPlayableNow && highestDiscardRatioHalfOrMore;
                }
                catch { stableMerchantNow = false; }

                if (clawStarterByHighestHalf || canClickCaveNow || canPlayCaveFromHandNow || stableMerchantNow)
                {
                    return false;
                }

                bool hasPlayableNonDiscardActionNow = board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id != Card.Cards.EX1_308
                    && c.Template.Id != Card.Cards.GAME_005
                    && c.CurrentCost <= board.ManaAvailable
                    && !IsForeignInjectedCardForDiscardLogic(board, c)
                    && !discardSet.Contains(c.Template.Id));
                if (hasPlayableNonDiscardActionNow) return false;

                reason = "弹幕在手且无更优启动(武器A脸/窟穴/商贩)，避免空过";
                return true;
            }
            catch
            {
                reason = null;
                discardCount = 0;
                totalCount = 0;
                return false;
            }
        }

        private bool ShouldAllowSoulfireNoReadyStarterHighDiscard(Board board, HashSet<Card.Cards> discardComponents,
            out string reason, out int discardCount, out int totalCount)
        {
            reason = null;
            discardCount = 0;
            totalCount = 0;

            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;

                bool canCastSoulfireNow = board.Hand.Any(c => c != null && c.Template != null
                    && IsSoulfireCardVariant(c)
                    && c.CurrentCost <= board.ManaAvailable);
                if (!canCastSoulfireNow) return false;

                var discardSet = discardComponents != null && discardComponents.Count > 0
                    ? new HashSet<Card.Cards>(discardComponents)
                    : new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                if (discardSet.Count == 0) return false;

                var nonSoulfireNonCoinCards = GetHandCardsForDiscardLogic(board, true)
                    .Where(c => c != null && c.Template != null && !IsSoulfireCardVariant(c))
                    .ToList();
                totalCount = nonSoulfireNonCoinCards.Count;
                if (totalCount <= 0) return false;

                discardCount = nonSoulfireNonCoinCards.Count(c => discardSet.Contains(c.Template.Id));
                if (discardCount <= 2) return false;

                bool hasCoin = false;
                try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }
                int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);
                bool hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7;

                bool canClickCaveNow = false;
                bool canPlayCaveFromHandNow = false;
                try
                {
                    canClickCaveNow = board.MinionFriend != null
                        && board.MinionFriend.Any(x => x != null
                            && x.Template != null
                            && x.Template.Id == Card.Cards.WON_103
                            && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);

                    canPlayCaveFromHandNow = hasBoardSlot && board.Hand.Any(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.WON_103
                        && c.CurrentCost <= effectiveMana);
                }
                catch
                {
                    canClickCaveNow = false;
                    canPlayCaveFromHandNow = false;
                }

                bool clawCanAttackNow = false;
                bool clawPlayableNow = false;
                try
                {
                    clawCanAttackNow = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;

                    clawPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                        && c.Template.Id == Card.Cards.END_016
                        && c.CurrentCost <= effectiveMana)
                        && effectiveMana >= 4;
                }
                catch
                {
                    clawCanAttackNow = false;
                    clawPlayableNow = false;
                }

                bool highestDiscardRatioHalfOrMore = false;
                try
                {
                    int tmpHighestDiscard = 0, tmpHighestTotal = 0;
                    highestDiscardRatioHalfOrMore = IsHighestCostDiscardRatioAtLeastHalf(
                        board, discardSet, out tmpHighestDiscard, out tmpHighestTotal);
                }
                catch { highestDiscardRatioHalfOrMore = false; }

                bool stableMerchantNow = false;
                try
                {
                    var merchant = hasBoardSlot
                        ? board.Hand.FirstOrDefault(c => c != null && c.Template != null
                            && c.Template.Id == Card.Cards.ULD_163
                            && c.CurrentCost <= effectiveMana)
                        : null;
                    stableMerchantNow = merchant != null && highestDiscardRatioHalfOrMore;
                }
                catch { stableMerchantNow = false; }

                if (canClickCaveNow || canPlayCaveFromHandNow || clawCanAttackNow || clawPlayableNow || stableMerchantNow)
                {
                    return false;
                }

                reason = "无现成启动组件(cave/claw/merchant)";
                return true;
            }
            catch
            {
                reason = null;
                discardCount = 0;
                totalCount = 0;
                return false;
            }
        }

        /// <summary>
        /// 魂火“先提纯后魂火”窗口与提刀窗口冲突时，优先提刀。
        /// 仅在 prep=咒怨之墓，且本回合可直接提刀、最高费组命中被弃组件时生效。
        /// </summary>
        private bool ShouldBlockSoulfirePrepForImmediateClawWindow(
            Board board,
            Card prepCard,
            bool lethalThisTurn,
            out string reason)
        {
            reason = null;
            try
            {
                if (board == null || lethalThisTurn) return false;
                if (prepCard == null || prepCard.Template == null) return false;
                if (prepCard.Template.Id != Card.Cards.TLC_451) return false;
                if (board.Hand == null || board.Hand.Count == 0) return false;

                bool hasEquippedWeapon = false;
                try
                {
                    hasEquippedWeapon = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.CurrentDurability > 0;
                }
                catch { hasEquippedWeapon = false; }
                if (hasEquippedWeapon) return false;

                Card clawInHand = null;
                try
                {
                    clawInHand = board.Hand.FirstOrDefault(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.END_016);
                }
                catch { clawInHand = null; }
                if (clawInHand == null) return false;

                int effectiveMana = board.ManaAvailable;
                try
                {
                    if (board.HasCardInHand(Card.Cards.GAME_005))
                    {
                        effectiveMana += 1;
                    }
                }
                catch { }

                bool canEquipClawNow = clawInHand.CurrentCost <= effectiveMana;
                if (!canEquipClawNow) return false;

                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                if (handForDiscardLogic == null || handForDiscardLogic.Count == 0) return false;

                int maxCostInHand = handForDiscardLogic.Max(h => h.CurrentCost);
                var discardSet = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                discardSet.Remove(Card.Cards.WON_103); // 对齐提刀窗口：窟穴不计入提刀被弃组件

                bool hasDiscardAtMaxForWeapon = handForDiscardLogic.Any(h => h != null
                    && h.Template != null
                    && h.CurrentCost == maxCostInHand
                    && discardSet.Contains(h.Template.Id));
                if (!hasDiscardAtMaxForWeapon) return false;

                reason = "先手提纯=咒怨之墓，但命中提刀窗口且本回合可提刀";
                return true;
            }
            catch
            {
                reason = null;
                return false;
            }
        }

        /// <summary>
        /// 低费提纯后魂火窗口：
        /// 非斩杀时，若手里有灵魂弹幕且弃牌密度较高，且存在可先打的非被弃组件（低费随从/灰烬元素等），
        /// 则允许走“先提纯 -> 再魂火尝试弃弹幕”的线路。
        /// </summary>
        private bool TryGetLowCostPrepForSoulfireBarrageWindow(
            Board board,
            out Card prepCard,
            out int discardCount,
            out int totalCount,
            out string reason)
        {
            prepCard = null;
            discardCount = 0;
            totalCount = 0;
            reason = null;

            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;

                bool canCastSoulfireNow = board.Hand.Any(c => c != null && c.Template != null
                    && IsSoulfireCardVariant(c)
                    && c.CurrentCost <= board.ManaAvailable);
                if (!canCastSoulfireNow) return false;

                int minSoulfireCost = 99;
                try
                {
                    var soulfireCards = board.Hand
                        .Where(c => c != null && c.Template != null && IsSoulfireCardVariant(c))
                        .ToList();
                    if (soulfireCards.Count == 0) return false;
                    minSoulfireCost = soulfireCards.Min(c => c.CurrentCost);
                }
                catch
                {
                    minSoulfireCost = 1;
                }
                if (minSoulfireCost < 0) minSoulfireCost = 0;

                // 提纯牌必须保证“打完还能同回合接魂火”。
                int prepManaBudget = board.ManaAvailable - minSoulfireCost;
                if (prepManaBudget <= 0) return false;

                bool hasBarrageInHand = board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);
                if (!hasBarrageInHand) return false;

                var discardSet = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                if (discardSet.Count == 0) return false;

                var nonSoulfireNonCoin = GetHandCardsForDiscardLogic(board, true)
                    .Where(c => c != null && c.Template != null && c.Template.Id != Card.Cards.EX1_308)
                    .ToList();

                totalCount = nonSoulfireNonCoin.Count;
                if (totalCount <= 0) return false;

                discardCount = nonSoulfireNonCoin.Count(c => discardSet.Contains(c.Template.Id));
                bool highDiscardDensity = discardCount >= 2 && discardCount * 2 >= totalCount;
                if (!highDiscardDensity) return false;

                bool hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7;

                bool canClickCave = false;
                bool canPlayCaveFromHand = false;
                try
                {
                    canClickCave = board.MinionFriend != null
                        && board.MinionFriend.Any(c => c != null
                            && c.Template != null
                            && c.Template.Id == Card.Cards.WON_103
                            && GetTag(c, Card.GAME_TAG.EXHAUSTED) != 1);

                    canPlayCaveFromHand = hasBoardSlot && board.Hand.Any(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.WON_103
                        && c.CurrentCost <= board.ManaAvailable);
                }
                catch
                {
                    canClickCave = false;
                    canPlayCaveFromHand = false;
                }
                if (canClickCave || canPlayCaveFromHand) return false;

                bool stableMerchantBarrageNow = false;
                try
                {
                    var merchant = hasBoardSlot
                        ? board.Hand.FirstOrDefault(c => c != null
                            && c.Template != null
                            && c.Template.Id == Card.Cards.ULD_163
                            && c.CurrentCost <= board.ManaAvailable)
                        : null;
                    if (merchant != null)
                    {
                        var handWithoutMerchant = GetHandCardsForDiscardLogic(board, true)
                            .Where(c => c != null && c.Template != null && c.Template.Id != Card.Cards.ULD_163)
                            .ToList();
                        if (handWithoutMerchant.Count > 0)
                        {
                            int maxCostAfterMerchant = handWithoutMerchant.Max(c => c.CurrentCost);
                            var highestAfterMerchant = handWithoutMerchant
                                .Where(c => c != null && c.Template != null && c.CurrentCost == maxCostAfterMerchant)
                                .ToList();
                            stableMerchantBarrageNow = highestAfterMerchant.Count > 0
                                && highestAfterMerchant.All(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);
                        }
                    }
                }
                catch { stableMerchantBarrageNow = false; }
                if (stableMerchantBarrageNow) return false;

                var prepCandidates = board.Hand
                    .Where(c => c != null
                        && c.Template != null
                        && c.CurrentCost <= prepManaBudget
                        && (c.CurrentCost <= 2 || c.Template.Id == Card.Cards.REV_960)
                        && c.Template.Id != Card.Cards.EX1_308
                        && c.Template.Id != Card.Cards.GAME_005
                        && !discardSet.Contains(c.Template.Id)
                        && !IsCoyoteCardVariant(c)
                        && !IsFelwingCardVariant(c)
                        && (c.Type != Card.CType.MINION || hasBoardSlot))
                    .ToList();
                if (prepCandidates.Count == 0) return false;

                prepCard = prepCandidates
                    .OrderBy(c => c.Template.Id == Card.Cards.LOOT_014 ? 0
                        : (c.Template.Id == Card.Cards.TLC_603 ? 1
                            : (c.Template.Id == Card.Cards.CORE_SCH_713 ? 2
                                : (c.Template.Id == Card.Cards.REV_960 ? 3 : 10))))
                    .ThenBy(c => c.CurrentCost)
                    .ThenBy(c => c.Id)
                    .FirstOrDefault();

                if (prepCard == null || prepCard.Template == null) return false;

                reason = "弃牌密度=" + discardCount + "/" + totalCount
                    + ",先手提纯=" + prepCard.Template.NameCN
                    + ",保留魂火费=" + minSoulfireCost;
                return true;
            }
            catch
            {
                prepCard = null;
                discardCount = 0;
                totalCount = 0;
                reason = null;
                return false;
            }
        }

        private void ApplyClawFaceBiasWhenHighestOnlySoulBarrage(ProfileParameters p, Board board, bool highestOnlySoulBarrage, string logPrefix)
        {
            if (p == null || board == null || !highestOnlySoulBarrage) return;

            try
            {
                bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                if (enemyHasTaunt) return;

                try
                {
                    if (board.HeroEnemy != null)
                    {
                        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, -800, board.HeroEnemy.Id);
                    }
                }
                catch { }

                try
                {
                    if (board.MinionEnemy != null)
                    {
                        foreach (var m in board.MinionEnemy)
                        {
                            if (m == null || m.Template == null) continue;
                            if (!m.IsTaunt)
                            {
                                p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, 900, m.Id);
                            }
                        }
                    }
                }
                catch { }

                AddLog(logPrefix + " 当前最高费仅灵魂弹幕且无嘲讽=> 优先打脸触发弃牌(-800)，避免解随从(+900)");
            }
            catch { }
        }

        private bool TryApplyFelwingCoyoteFaceDiscountPlan(ProfileParameters p, Board board, bool survivalStabilizeMode, string logTag)
        {
            if (p == null || board == null || board.Hand == null || board.Hand.Count == 0) return false;
            if (survivalStabilizeMode) return false;

            try
            {
                bool comboAlreadySet = false;
                try { comboAlreadySet = (p.ComboModifier != null); } catch { comboAlreadySet = false; }
                if (comboAlreadySet) return false;

                bool hasCoin = false;
                try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }

                bool canUsePlan = board.MaxMana >= 3 || !hasCoin;
                if (!canUsePlan) return false;

                bool enemyHasTaunt = false;
                try { enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(x => x != null && x.IsTaunt); } catch { enemyHasTaunt = false; }
                if (enemyHasTaunt) return false;

                int attackersCount = 0;
                int potentialFaceDamage = 0;
                if (board.MinionFriend != null)
                {
                    foreach (var x in board.MinionFriend)
                    {
                        if (x == null || !x.CanAttack || x.CurrentAtk <= 0) continue;
                        attackersCount++;
                        potentialFaceDamage += Math.Max(0, x.CurrentAtk);
                    }
                }
                if (attackersCount == 0 || potentialFaceDamage <= 0) return false;

                int manaNow = board.ManaAvailable;
                bool hasFelwing = false;
                bool hasCoyote = false;
                bool felwingCanBeDiscountedPlayable = false;
                bool coyoteCanBeDiscountedPlayable = false;

                foreach (var h in board.Hand)
                {
                    if (h == null || h.Template == null) continue;

                    if (h.Template.Id == Card.Cards.YOD_032)
                    {
                        hasFelwing = true;
                        if (h.CurrentCost > manaNow && h.CurrentCost <= manaNow + potentialFaceDamage)
                        {
                            felwingCanBeDiscountedPlayable = true;
                        }
                        continue;
                    }

                    if (h.Template.Id == Card.Cards.TIME_047)
                    {
                        hasCoyote = true;
                        if (h.CurrentCost > manaNow && h.CurrentCost <= manaNow + potentialFaceDamage)
                        {
                            coyoteCanBeDiscountedPlayable = true;
                        }
                    }
                }

                if (!hasFelwing && !hasCoyote) return false;
                if (!felwingCanBeDiscountedPlayable && !coyoteCanBeDiscountedPlayable) return false;

                p.ForceResimulation = true;

                if (board.MinionFriend != null)
                {
                    foreach (var a in board.MinionFriend)
                    {
                        if (a == null || a.Template == null || !a.CanAttack || a.CurrentAtk <= 0) continue;
                        p.AttackOrderModifiers.AddOrUpdate(a.Template.Id, new Modifier(920));
                    }
                }

                if (board.MinionEnemy != null)
                {
                    foreach (var e in board.MinionEnemy)
                    {
                        if (e == null || e.Template == null || e.IsTaunt) continue;
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(e.Template.Id, new Modifier(-900));
                    }
                }

                AddLog("[" + logTag + "] 手牌有邪翼蝠/郊狼且满足条件(MaxMana=" + board.MaxMana
                    + ",有币=" + (hasCoin ? "Y" : "N") + ",规则=MaxMana>=3或无币)"
                    + "，本回合可通过走脸压费变可打(邪翼蝠="
                    + (felwingCanBeDiscountedPlayable ? "Y" : "N")
                    + ",郊狼=" + (coyoteCanBeDiscountedPlayable ? "Y" : "N")
                    + ",可走脸伤害=" + potentialFaceDamage + ") => 强制先A脸压费并重算");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void LogBoardSnapshotOneLine(Board board)
        {
            try
            {
                if (!VerboseLog) return;
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
                            int baseCost = 0;
                            try { baseCost = m.Template.Cost; } catch { baseCost = 0; }
                            list.Add((!string.IsNullOrEmpty(m.Template.NameCN) ? m.Template.NameCN : m.Template.Id.ToString())
                                + "|Id=" + m.Template.Id
                                + "|Inst=" + m.Id
                                + "|Cost=" + baseCost
                                + "|Stat=" + m.CurrentAtk + "/" + m.CurrentHealth
                                + "|CanAtk=" + (m.CanAttack ? "Y" : "N"));
                        }
                    }
                    friendMinions = list.Count > 0 ? string.Join(";", list) : "(空)";
                }
                catch { friendMinions = "(解析失败)"; }

                string weaponInfo = "(无";
                try
                {
                    if (board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.CurrentDurability > 0)
                    {
                        int weaponBaseCost = 0;
                        try { weaponBaseCost = board.WeaponFriend.Template.Cost; } catch { weaponBaseCost = 0; }
                        weaponInfo = (!string.IsNullOrEmpty(board.WeaponFriend.Template.NameCN) ? board.WeaponFriend.Template.NameCN : board.WeaponFriend.Template.Id.ToString())
                            + "|Id=" + board.WeaponFriend.Template.Id
                            + "|Cost=" + weaponBaseCost
                            + "|Atk=" + board.WeaponFriend.CurrentAtk
                            + "|Dur=" + board.WeaponFriend.CurrentDurability;
                    }
                }
                catch { weaponInfo = "(解析失败)"; }

                string enemyMinions = "";
                try
                {
                    var list = new List<string>();
                    if (board.MinionEnemy != null)
                    {
                        foreach (var m in board.MinionEnemy)
                        {
                            if (m == null || m.Template == null) continue;
                            int baseCost = 0;
                            try { baseCost = m.Template.Cost; } catch { baseCost = 0; }
                            list.Add((!string.IsNullOrEmpty(m.Template.NameCN) ? m.Template.NameCN : m.Template.Id.ToString())
                                + "|Id=" + m.Template.Id
                                + "|Inst=" + m.Id
                                + "|Cost=" + baseCost
                                + "|Stat=" + m.CurrentAtk + "/" + m.CurrentHealth
                                + "|Taunt=" + (m.IsTaunt ? "Y" : "N"));
                        }
                    }
                    enemyMinions = list.Count > 0 ? string.Join(";", list) : "(空)";
                }
                catch { enemyMinions = "(解析失败)"; }

                AddLog("[场面快照] F=" + friendMinions + " | W=" + weaponInfo + " | E=" + enemyMinions);
            }
            catch { }
        }

        private void LogHandSnapshotOneLine(Board board)
        {
            try
            {
                if (!VerboseLog) return;
                if (board == null) return;

                var parts = new List<string>();
                if (board.Hand != null)
                {
                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null) continue;
                        bool isTemp = false;
                        try { isTemp = IsTemporaryCard(board, c); } catch { isTemp = false; }

                        int baseCost = 0;
                        try { baseCost = c.Template.Cost; } catch { baseCost = 0; }
                        parts.Add((!string.IsNullOrEmpty(c.Template.NameCN) ? c.Template.NameCN : c.Template.Id.ToString())
                            + "|Id=" + c.Template.Id
                            + "|Inst=" + c.Id
                            + "|CostNow=" + c.CurrentCost
                            + "|CostBase=" + baseCost
                            + "|Temp=" + (isTemp ? "Y" : "N"));
                    }
                }

                string hand = parts.Count > 0 ? string.Join(";", parts) : "(空)";
                AddLog("[手牌快照] H=" + hand);
            }
            catch { }
        }

        private int GetConfiguredBarrageBaseCount(Board board)
        {
            try
            {
                if (board != null && board.Deck != null)
                {
                    // deck 可见时，0 也是有效信息（表示当前牌库确实没有灵魂弹幕）。
                    int deckListedCopies = board.Deck.Count(id => id == Card.Cards.RLK_534);
                    if (deckListedCopies < 0) deckListedCopies = 0;
                    return deckListedCopies;
                }
            }
            catch { }

            // deck 不可见时：若开局牌库快照确认未出现灵魂弹幕，按 0 处理，避免误判。
            try
            {
                if (_openingDeckSnapshotReady && _openingDeckCardIds.Count > 0
                    && !_openingDeckCardIds.Contains(Card.Cards.RLK_534))
                    return 0;
            }
            catch { }

            // Fallback when deck list is unavailable.
            return 2;
        }

        // 牌库列表不可见时，估算要更保守，避免“牌库已空仍估x1灵魂弹幕”。
        // 口径：只要已经在手牌/坟场看到过非临时灵魂弹幕，就把未知牌库基线从2降到1。
        private int NormalizeBarrageBaseCountForUnknownDeck(Board board, int baseCount, int observedNonTempCopies)
        {
            try
            {
                bool deckVisible = board != null && board.Deck != null;
                if (!deckVisible && baseCount > 1 && observedNonTempCopies > 0)
                    return 1;
            }
            catch { }

            if (baseCount < 0) return 0;
            return baseCount;
        }

        private int GetRemainingBarrageCountInDeck(Board board)
        {
            try
            {
                if (board == null || board.Hand == null) return 0;
                int baseCount = GetConfiguredBarrageBaseCount(board);

                int barrageInHandNonTemp = 0;
                try
                {
                    barrageInHandNonTemp = board.Hand.Count(c => c != null && c.Template != null
                        && c.Template.Id == Card.Cards.RLK_534 && !IsTemporaryCard(board, c));
                }
                catch { barrageInHandNonTemp = 0; }

                int barrageInHandTemp = 0;
                try
                {
                    barrageInHandTemp = board.Hand.Count(c => c != null && c.Template != null
                        && c.Template.Id == Card.Cards.RLK_534 && IsTemporaryCard(board, c));
                }
                catch { barrageInHandTemp = 0; }

                int barrageInGrave = 0;
                try
                {
                    barrageInGrave = board.FriendGraveyard != null
                        ? board.FriendGraveyard.Count(id => id == Card.Cards.RLK_534)
                        : 0;
                }
                catch { barrageInGrave = 0; }

                int observedTemporaryBarrageTotal = 0;
                try { observedTemporaryBarrageTotal = _seenTemporaryBarrageEntityIds.Count; } catch { observedTemporaryBarrageTotal = 0; }

                // 临时弹幕（例如?写复制）不会消耗牌库里的真实弹幕??
                // 估算时应从坟场弹幕中ｉ“很可能是临时弹幕?濈殑閮?，避免把 est 错压?0銆?
                int temporaryBarrageLikelyInGrave = observedTemporaryBarrageTotal - barrageInHandTemp;
                if (temporaryBarrageLikelyInGrave < 0) temporaryBarrageLikelyInGrave = 0;
                int barrageInGraveNonTemp = barrageInGrave - temporaryBarrageLikelyInGrave;
                if (barrageInGraveNonTemp < 0) barrageInGraveNonTemp = 0;

                int observedNonTempCopies = barrageInHandNonTemp + barrageInGraveNonTemp;
                baseCount = NormalizeBarrageBaseCountForUnknownDeck(board, baseCount, observedNonTempCopies);

                // Conservative estimate: only trust base deck copies - currently seen non-temp copies.
                // Do not optimistic-add Sketch/Merchant generated value, to avoid false positives.
                int estimate = baseCount - barrageInHandNonTemp - barrageInGraveNonTemp;

                // Runtime API fallback: only when baseCount itself suggests RLK_534 can exist.
                // If baseCount==0 (deck/快照明确无弹幕), never force-add by API.
                bool hasBarrageInDeckByApi = false;
                try { hasBarrageInDeckByApi = board.HasCardInDeck(Card.Cards.RLK_534); } catch { hasBarrageInDeckByApi = false; }
                if (baseCount > 0 && estimate <= 0 && hasBarrageInDeckByApi)
                    estimate = 1;

                if (estimate < 0) estimate = 0;
                return estimate;
            }
            catch
            {
                return 0;
            }
        }

        private string GetBarrageEstimateDebugString(Board board)
        {
            try
            {
                if (board == null) return "board=null";
                int baseCount = GetConfiguredBarrageBaseCount(board);

                int sketchOnBoard = 0;
                try
                {
                    sketchOnBoard = board.MinionFriend != null
                        ? board.MinionFriend.Count(m => m != null && m.Template != null && m.Template.Id == Card.Cards.TOY_916)
                        : 0;
                }
                catch { sketchOnBoard = 0; }

                int sketchInGrave = 0;
                int merchantInGrave = 0;
                int barrageInGrave = 0;
                try
                {
                    if (board.FriendGraveyard != null)
                    {
                        sketchInGrave = board.FriendGraveyard.Count(id => id == Card.Cards.TOY_916);
                        merchantInGrave = board.FriendGraveyard.Count(id => id == Card.Cards.ULD_163);
                        barrageInGrave = board.FriendGraveyard.Count(id => id == Card.Cards.RLK_534);
                    }
                }
                catch { sketchInGrave = 0; merchantInGrave = 0; barrageInGrave = 0; }

                int barrageInHandNonTemp = 0;
                try
                {
                    barrageInHandNonTemp = board.Hand != null
                        ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534 && !IsTemporaryCard(board, c))
                        : 0;
                }
                catch { barrageInHandNonTemp = 0; }

                int barrageInHandTemp = 0;
                try
                {
                    barrageInHandTemp = board.Hand != null
                        ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534 && IsTemporaryCard(board, c))
                        : 0;
                }
                catch { barrageInHandTemp = 0; }

                int observedTemporaryBarrageTotal = 0;
                try { observedTemporaryBarrageTotal = _seenTemporaryBarrageEntityIds.Count; } catch { observedTemporaryBarrageTotal = 0; }

                int temporaryBarrageLikelyInGrave = observedTemporaryBarrageTotal - barrageInHandTemp;
                if (temporaryBarrageLikelyInGrave < 0) temporaryBarrageLikelyInGrave = 0;

                int barrageInGraveNonTemp = barrageInGrave;
                if (temporaryBarrageLikelyInGrave > 0)
                    barrageInGraveNonTemp = Math.Max(0, barrageInGrave - temporaryBarrageLikelyInGrave);

                int barrageInGraveUsed = barrageInGrave;
                int observedNonTempCopies = barrageInHandNonTemp + barrageInGraveNonTemp;
                int baseCountAdjusted = NormalizeBarrageBaseCountForUnknownDeck(board, baseCount, observedNonTempCopies);

                int estimate = 0;
                try { estimate = GetRemainingBarrageCountInDeck(board); } catch { estimate = 0; }

                bool hasBarrageInDeckByApi = false;
                try { hasBarrageInDeckByApi = board.HasCardInDeck(Card.Cards.RLK_534); } catch { hasBarrageInDeckByApi = false; }

                return "base=" + baseCount
                    + ";baseAdj=" + baseCountAdjusted
                    + ";skB=" + sketchOnBoard
                    + ";skG=" + sketchInGrave
                    + ";merG=" + merchantInGrave
                    + ";bH=" + barrageInHandNonTemp
                    + ";bHT=" + barrageInHandTemp
                    + ";bG=" + barrageInGrave
                    + ";bGReal=" + barrageInGraveNonTemp
                    + ";bGUsed=" + barrageInGraveUsed
                    + ";tSeen=" + observedTemporaryBarrageTotal
                    + ";deckApi=" + (hasBarrageInDeckByApi ? "Y" : "N")
                    + ";est=" + estimate;
            }
            catch
            {
                return "debug-failed";
            }
        }

        // 友方场面“占位格”数量（7格位）：随从 + 地标?
        // 说明：SmartBot 鍦?分场景下场上地标不会体现??友方随从数量?口径里?
        // 会导致出现??随从+1地标”仍被误?负鏈夌?位，从?尝试再拍手牌地?下随从??
        private int GetFriendlyBoardSlotsUsed(Board board)
        {
            int used = 0;
            try
            {
                used = board != null && board.MinionFriend != null
                    ? board.MinionFriend.Count(m => m != null && m.Template != null)
                    : 0;
            }
            catch { used = 0; }

            // 兜底：如果?窟穴地标?在某些 API 口径下不计入 MinionFriend，这里用 HasCardOnBoard 血1 鏍笺??
            try
            {
                bool caveOnBoard = board != null && board.HasCardOnBoard(Card.Cards.WON_103, true);
                if (caveOnBoard)
                {
                    bool caveAlreadyCounted = false;
                    try
                    {
                        caveAlreadyCounted = board.MinionFriend != null
                            && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.WON_103);
                    }
                    catch { caveAlreadyCounted = false; }

                    if (!caveAlreadyCounted) used += 1;
                }
            }
            catch { }

            return used;
        }

        // 鈥滆瘹淇?家格里伏塔??VAC_959) 给双方的?牌：忽略它们?干手牌口径计算中的干扰??
        private static bool IsHonestMerchantCharm(Card.Cards id)
        {
            try
            {
                // 鎶??token褰?? VAC_959t05 / VAC_959t06 ...
                return id.ToString().StartsWith("VAC_959t");
            }
            catch
            {
                return false;
            }
        }

        private bool IsCardVariantByIdOrName(Card card, Card.Cards canonicalId, string nameCn)
        {
            try
            {
                if (card == null || card.Template == null) return false;
                if (card.Template.Id == canonicalId) return true;

                var canonicalText = canonicalId.ToString();
                var idText = card.Template.Id.ToString();
                if (!string.IsNullOrEmpty(canonicalText)
                    && !string.IsNullOrEmpty(idText)
                    && idText.EndsWith(canonicalText, StringComparison.OrdinalIgnoreCase))
                    return true;

                return !string.IsNullOrEmpty(nameCn)
                    && string.Equals(card.Template.NameCN, nameCn, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private bool IsCoyoteCardVariant(Card card)
        {
            return IsCardVariantByIdOrName(card, Card.Cards.TIME_047, "郊狼");
        }

        private bool IsFelwingCardVariant(Card card)
        {
            return IsCardVariantByIdOrName(card, Card.Cards.YOD_032, "狂暴邪翼蝠");
        }

        // 兼容识别灵魂之火的不同变体（?EX1_308 / VAN_EX1_308 绛夛級銆?
        private bool IsSoulfireCardVariant(Card card)
        {
            try
            {
                if (card == null || card.Template == null) return false;
                if (card.Template.Id == Card.Cards.EX1_308) return true;

                var idText = card.Template.Id.ToString();
                if (!string.IsNullOrEmpty(idText)
                    && idText.EndsWith("EX1_308", StringComparison.OrdinalIgnoreCase))
                    return true;

                return string.Equals(card.Template.NameCN, "灵魂之火", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private int GetTag(Card card, Card.GAME_TAG tag)
        {
            if (card == null) return -1;
            try
            {
                // 统一使用小写 tags（符合API规范?
                if (card.tags != null && card.tags.ContainsKey(tag))
                    return card.tags[tag];
            }
            catch { }
            return -1;
        }

        private static int _tempTrackTurn = int.MinValue;
        private static readonly HashSet<int> _prevHandEntityIds = new HashSet<int>();
        private static readonly HashSet<int> _temporaryHandEntityIdsThisTurn = new HashSet<int>();
        // Cross-turn tracking of temporary Soul Barrage entity ids.
        private static int _lastSeenTurnForTempBarrageTracking = int.MinValue;
        private static readonly HashSet<int> _seenTemporaryBarrageEntityIds = new HashSet<int>();
        // Opening deck snapshot: cards outside this set are treated as foreign for discard logic.
        private static readonly HashSet<Card.Cards> _openingDeckCardIds = new HashSet<Card.Cards>();
        private static string _openingDeckSnapshotGameKey = string.Empty;
        private static int _openingDeckSnapshotLastSeenTurn = int.MinValue;
        private static bool _openingDeckSnapshotReady = false;
        private static bool _openingDeckSnapshotLogged = false;

        private string BuildOpeningDeckSnapshotGameKey(Board board)
        {
            try
            {
                int friendHeroEntityId = -1;
                int enemyHeroEntityId = -1;
                try { friendHeroEntityId = board != null && board.HeroFriend != null ? board.HeroFriend.Id : -1; } catch { friendHeroEntityId = -1; }
                try { enemyHeroEntityId = board != null && board.HeroEnemy != null ? board.HeroEnemy.Id : -1; } catch { enemyHeroEntityId = -1; }
                return friendHeroEntityId + "|" + enemyHeroEntityId;
            }
            catch
            {
                return "unknown";
            }
        }

        private void UpdateOpeningDeckSnapshot(Board board)
        {
            try
            {
                if (board == null) return;

                int turn = GetBoardTurn(board);
                string gameKey = BuildOpeningDeckSnapshotGameKey(board);
                bool turnResetDetected = turn >= 0
                    && _openingDeckSnapshotLastSeenTurn >= 0
                    && turn < _openingDeckSnapshotLastSeenTurn;
                bool likelyNewGameByMana = board.MaxMana <= 1
                    && _openingDeckSnapshotReady
                    && _openingDeckSnapshotLastSeenTurn > 1;
                bool gameChanged = !string.Equals(_openingDeckSnapshotGameKey, gameKey, StringComparison.Ordinal);
                if (gameChanged || turnResetDetected || likelyNewGameByMana)
                {
                    _openingDeckSnapshotGameKey = gameKey;
                    _openingDeckCardIds.Clear();
                    _openingDeckSnapshotReady = false;
                    _openingDeckSnapshotLogged = false;
                    _openingDeckSnapshotLastSeenTurn = int.MinValue;
                }

                if (turn >= 0)
                    _openingDeckSnapshotLastSeenTurn = turn;

                bool openingWindow = (turn >= 0 && turn <= 1)
                    || (turn < 0 && board.MaxMana <= 1);

                if (openingWindow)
                {
                    if (board.Deck != null)
                    {
                        foreach (var c in board.Deck)
                        {
                            _openingDeckCardIds.Add(c);
                        }
                    }

                    if (board.Hand != null)
                    {
                        foreach (var c in board.Hand)
                        {
                            if (c == null || c.Template == null) continue;
                            _openingDeckCardIds.Add(c.Template.Id);
                        }
                    }
                }
                else if (!_openingDeckSnapshotReady)
                {
                    if (board.Deck != null)
                    {
                        foreach (var c in board.Deck)
                        {
                            _openingDeckCardIds.Add(c);
                        }
                    }
                }

                _openingDeckSnapshotReady = _openingDeckCardIds.Count > 0;
                if (_openingDeckSnapshotReady && !_openingDeckSnapshotLogged)
                {
                    AddLog("[开局牌库快照] 已记录牌库来源ID=" + _openingDeckCardIds.Count + "；弃牌逻辑忽略非牌库来源手牌");
                    _openingDeckSnapshotLogged = true;
                }
            }
            catch { }
        }

        private static bool IsForeignInjectedCardForDiscardLogic(Board board, Card card)
        {
            try
            {
                if (board == null || card == null || card.Template == null) return false;
                if (!_openingDeckSnapshotReady || _openingDeckCardIds.Count == 0) return false;
                return !_openingDeckCardIds.Contains(card.Template.Id);
            }
            catch
            {
                return false;
            }
        }

        private static List<Card> GetHandCardsForDiscardLogic(Board board, bool excludeCoin = false)
        {
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0)
                    return new List<Card>();

                return board.Hand
                    .Where(c => c != null
                        && c.Template != null
                        && !IsForeignInjectedCardForDiscardLogic(board, c)
                        && (!excludeCoin || c.Template.Id != Card.Cards.GAME_005))
                    .ToList();
            }
            catch
            {
                return new List<Card>();
            }
        }

        private static int? TryGetIntProperty(object obj, params string[] propertyNames)
        {
            try
            {
                if (obj == null || propertyNames == null || propertyNames.Length == 0) return null;
                var t = obj.GetType();
                foreach (var name in propertyNames)
                {
                    try
                    {
                        var p = t.GetProperty(name);
                        if (p == null) continue;
                        var v = p.GetValue(obj, null);
                        if (v == null) continue;
                        if (v is int) return (int)v;
                        if (v is short) return (short)v;
                        if (v is byte) return (byte)v;
                        int parsed;
                        if (int.TryParse(v.ToString(), out parsed)) return parsed;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static int GetBoardTurn(Board board)
        {
            try
            {
                var t = TryGetIntProperty(board, "Turn", "TurnNumber", "CurrentTurn", "TurnCount", "GameTurn");
                return t.HasValue ? t.Value : -1;
            }
            catch
            {
                return -1;
            }
        }

        private void UpdateTemporaryHandTracking(Board board)
        {
            if (board == null) return;

            int turn = GetBoardTurn(board);
            if (turn != _tempTrackTurn)
            {
                _tempTrackTurn = turn;
                _prevHandEntityIds.Clear();
                _temporaryHandEntityIdsThisTurn.Clear();
            }

            try
            {
                bool shouldResetTempBarrageTracker =
                    (turn >= 0 && turn <= 1 && _lastSeenTurnForTempBarrageTracking > 1)
                    || (turn >= 0 && _lastSeenTurnForTempBarrageTracking >= 0 && turn < _lastSeenTurnForTempBarrageTracking);
                if (shouldResetTempBarrageTracker)
                    _seenTemporaryBarrageEntityIds.Clear();
                if (turn >= 0)
                    _lastSeenTurnForTempBarrageTracking = turn;
            }
            catch { }

            if (board.Hand == null) return;

            var currentHandIds = new HashSet<int>();
            for (int i = 0; i < board.Hand.Count; i++)
            {
                var c = board.Hand[i];
                if (c == null || c.Template == null) continue;
                currentHandIds.Add(c.Id);
            }

            Card.Cards lastPlayed = (Card.Cards)0;
            bool hasLastPlayed = false;
            try
            {
                if (board.PlayedCards != null && board.PlayedCards.Count > 0)
                {
                    lastPlayed = board.PlayedCards.Last();
                    hasLastPlayed = true;
                }
            }
            catch { hasLastPlayed = false; }

            int requiredNewCardsForTempMark = int.MaxValue;
            if (hasLastPlayed)
            {
                if (lastPlayed == Card.Cards.TLC_451) requiredNewCardsForTempMark = 1;
                else if (lastPlayed == Card.Cards.TOY_916) requiredNewCardsForTempMark = 2;
                else if (lastPlayed == Card.Cards.TLC_603) requiredNewCardsForTempMark = 1;
            }

            if (requiredNewCardsForTempMark != int.MaxValue && _prevHandEntityIds.Count > 0)
            {
                var newIds = currentHandIds.Where(id => !_prevHandEntityIds.Contains(id)).ToList();
                if (newIds.Count >= requiredNewCardsForTempMark)
                {
                    // 发现/生成牌?常出现?牌最右侧：只标记“最右侧ｅ新牌”，避免误把同时抽到的牌也当临时?
                    int pickedId = -1;
                    for (int i = board.Hand.Count - 1; i >= 0; i--)
                    {
                        var c = board.Hand[i];
                        if (c == null || c.Template == null) continue;
                        if (newIds.Contains(c.Id)) { pickedId = c.Id; break; }
                    }

                    if (pickedId != -1)
                        _temporaryHandEntityIdsThisTurn.Add(pickedId);
                }
            }

            try
            {
                foreach (var c in board.Hand)
                {
                    if (c == null || c.Template == null) continue;
                    if (c.Template.Id != Card.Cards.RLK_534) continue;

                    bool isTemp = false;
                    try { isTemp = IsTemporaryCard(board, c); } catch { isTemp = false; }
                    if (isTemp)
                        _seenTemporaryBarrageEntityIds.Add(c.Id);
                }
            }
            catch { }

            _prevHandEntityIds.Clear();
            foreach (var id in currentHandIds)
                _prevHandEntityIds.Add(id);
        }

        private bool IsTemporaryCard(Board board, Card card)
        {
            if (card == null || board == null) return false;

            try
            {
                bool byShiftingEnchant;
                return IsTemporaryCard(board, card, out byShiftingEnchant);
            }
            catch
            {
                return false;
            }
        }

        private bool IsTemporaryPayoffMinion(Card card)
        {
            if (card == null || card.Template == null) return false;

            try
            {
            // 临时牌判定口?
            bool isTemporary = HasShiftingEnchantment(card);

                // 妫?鏌?槸鍚?弃牌收益随从
                bool isPayoffMinion = card.Template.Id == Card.Cards.WON_098  // 闀?银魔?
                    || card.Template.Id == Card.Cards.KAR_205  // 闀?银魔像（?級
                    || card.Template.Id == Card.Cards.RLK_532; // 行尸

                return isTemporary && isPayoffMinion;
            }
            catch
            {
                return false;
            }
        }

        private Card.Cards[] GetDiscardComponentsConsideringHand(Board board, int furnaceFuelMaxHand)
        {
            var list = new List<Card.Cards>
            {
                Card.Cards.RLK_534, // 灵魂弹幕
                Card.Cards.AT_022,  // 加拉克苏斯之?
                Card.Cards.WW_044t, // 娣?偿妗?
            };

            try
            {
                var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                int handCount = handForDiscardLogic.Count;
                int friendMinions = board != null && board.MinionFriend != null ? board.MinionFriend.Count : 0;
                bool hasCoin = false;
                try { hasCoin = board != null && board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }
                int effectiveMana = (board != null ? board.ManaAvailable : 0) + (hasCoin ? 1 : 0);

                // 行尸/闀?银魔像：只有随从?=6时才算?被弃组件=濓紙鍚?弃掉也召不出?等价于浪费）
                if (friendMinions <= 6)
                {
                    list.Add(Card.Cards.RLK_532); // 行尸
                    list.Add(Card.Cards.WON_098); // 闀?银魔?
                    list.Add(Card.Cards.KAR_205); // 闀?银魔像（?増锛?
                }

                // 鍙?丹之手：只有手牌<=7时才算?被弃组件=（避免弃到后抽3爆牌/节奏?級
                // 鏈?新口径：只要已装备武?英雄可攻击，就禁?尔丹之手(BT_300)作为被弃组件/决策?彂鐐广??
                bool weaponEquippedAndHeroCanAttack = false;
                try
                {
                    weaponEquippedAndHeroCanAttack = board != null
                        && board.WeaponFriend != null
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;
                }
                catch { weaponEquippedAndHeroCanAttack = false; }

                if (!weaponEquippedAndHeroCanAttack && handCount > 0 && handCount <= 8)
                {
                    list.Add(Card.Cards.BT_300);
                }

                // 锅炉燃料：只有手牌数?=设定值时才算被弃组件
                if (handCount > 0 && handCount <= furnaceFuelMaxHand)
                    list.Add(Card.Cards.WW_441);

                // 鍔??佽?鍒?3)锛氭墜鐗?=8，且“手里有地标并且可用?=3”或“场上有地标”时，地标也?被弃组件=
                bool caveInHandAndManaReady = false;
                bool caveOnBoard = false;
                try
                {
                    caveInHandAndManaReady = board != null
                        && handForDiscardLogic.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103)
                        && effectiveMana >= 3;
                }
                catch { caveInHandAndManaReady = false; }
                try
                {
                    caveOnBoard = board != null && board.MinionFriend != null
                        && board.MinionFriend.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.WON_103);
                }
                catch { caveOnBoard = false; }
                if (handCount > 0 && handCount <= 8 && (caveInHandAndManaReady || caveOnBoard))
                {
                    list.Add(Card.Cards.WON_103);
                }

                // 鍔??佽?鍒?4)：最高费组?被弃组件占?=1/2鈥濇椂锛?
                // - 手上有时空之爪且可用?=4(且本回合可提?) -> 爪子?被弃组件=
                // - 或已装备时空之爪且英雄可攻击 -> 爪子?被弃组件=
                bool highestDiscardRatioHalfOrMore = false;
                try
                {
                    int tmpHighestDiscard = 0, tmpHighestTotal = 0;
                    highestDiscardRatioHalfOrMore = IsHighestCostDiscardRatioAtLeastHalf(
                        board, new HashSet<Card.Cards>(list), out tmpHighestDiscard, out tmpHighestTotal);
                }
                catch { highestDiscardRatioHalfOrMore = false; }

                if (highestDiscardRatioHalfOrMore)
                {
                    bool clawInHandAndPlayable = false;
                    bool clawEquippedCanAttack = false;
                    try
                    {
                        clawInHandAndPlayable = board != null
                            && effectiveMana >= 4
                            && handForDiscardLogic.Any(c => c != null && c.Template != null
                                && c.Template.Id == Card.Cards.END_016
                                && c.CurrentCost <= effectiveMana);
                    }
                    catch { clawInHandAndPlayable = false; }
                    try
                    {
                        clawEquippedCanAttack = board != null
                            && board.WeaponFriend != null
                            && board.WeaponFriend.Template != null
                            && board.WeaponFriend.Template.Id == Card.Cards.END_016
                            && board.WeaponFriend.CurrentDurability > 0
                            && board.HeroFriend != null
                            && board.HeroFriend.CanAttack;
                    }
                    catch { clawEquippedCanAttack = false; }

                    if (clawInHandAndPlayable || clawEquippedCanAttack)
                    {
                        list.Add(Card.Cards.END_016);
                    }
                }
            }
            catch { }

            return list.Distinct().ToArray();
        }

        private bool IsNonSoulfireAllDiscardComponents(Board board, HashSet<Card.Cards> discardComponents,
            out int discardCount, out int totalCount)
        {
            discardCount = 0;
            totalCount = 0;
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;
                if (discardComponents == null || discardComponents.Count == 0) return false;

                var nonSoulfireNonCoinCards = GetHandCardsForDiscardLogic(board, true)
                    .Where(c => c != null && c.Template != null
                        && c.Template.Id != Card.Cards.EX1_308)
                    .ToList();

                totalCount = nonSoulfireNonCoinCards.Count;
                if (totalCount <= 0) return false;

                discardCount = nonSoulfireNonCoinCards.Count(c => discardComponents.Contains(c.Template.Id));
                return discardCount == totalCount;
            }
            catch
            {
                discardCount = 0;
                totalCount = 0;
                return false;
            }
        }

        private bool IsHighestCostDiscardRatioAtLeastHalf(Board board, HashSet<Card.Cards> discardComponents,
            out int discardCount, out int totalCount)
        {
            discardCount = 0;
            totalCount = 0;
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;
                if (discardComponents == null || discardComponents.Count == 0) return false;

                var relevant = GetHandCardsForDiscardLogic(board, true)
                    .Where(c => c != null && c.Template != null && c.Template.Id != Card.Cards.WON_103)
                    .ToList();
                if (relevant.Count == 0) return false;

                int maxCost = relevant.Max(c => c.CurrentCost);
                var highest = relevant.Where(c => c.CurrentCost == maxCost).ToList();
                totalCount = highest.Count;
                if (totalCount <= 0) return false;

                discardCount = highest.Count(c => discardComponents.Contains(c.Template.Id));
                return discardCount * 2 >= totalCount;
            }
            catch
            {
                discardCount = 0;
                totalCount = 0;
                return false;
            }
        }

        #endregion

        #region 必需的接ｆ柟娉?

        private static readonly Card.Cards LifeTap = Card.Cards.CS2_056_H1;

        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            if (choices == null || choices.Count == 0)
                return LifeTap;

            // 优先选择生命分流
            Card.Cards best = choices[0];
            int bestScore = int.MinValue;
            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                int score = 0;

                // 鍒?是否为分流技?
                if (c == Card.Cards.CS2_056_H1 || c == Card.Cards.HERO_07bp)
                {
                    score = 8;
                }

                if (score >bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            return best;
        }

        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : LifeTap;
        }

        #endregion

        #region 新增＄处理方法

        /// <summary>
        /// 前期场面劣势时的墓放行窗口：
        /// 即便手里有0费郊狼，也允许优先打咒怨之墓找被弃组件反打。
        /// </summary>
        private bool ShouldEmergencyAllowTombInEarlyBoardGap(Board board, Card tombCard, out string reason)
        {
            reason = null;
            try
            {
                if (board == null || board.Hand == null) return false;
                var tomb = tombCard ?? board.Hand.FirstOrDefault(h => h != null
                    && h.Template != null
                    && h.Template.Id == Card.Cards.TLC_451);
                if (tomb == null) return false;
                if (tomb.CurrentCost != 0) return false;
                if (BoardHelper.IsLethalPossibleThisTurn(board)) return false;

                int enemyMinions = board.MinionEnemy != null
                    ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0)
                    : 0;
                int friendMinions = board.MinionFriend != null
                    ? board.MinionFriend.Count(m => m != null && m.CurrentHealth > 0)
                    : 0;
                int enemyAttack = board.MinionEnemy != null
                    ? board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk))
                    : 0;
                int myHpArmor = board.HeroFriend != null
                    ? Math.Max(0, board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor)
                    : 0;
                bool hasZeroCostCoyote = board.Hand.Any(h => h != null
                    && h.Template != null
                    && IsCoyoteCardVariant(h)
                    && h.CurrentCost == 0);

                bool earlyGame = board.MaxMana <= 4;
                bool hugeBoardGap = enemyMinions >= friendMinions + 2
                    || (enemyMinions >= 2 && enemyAttack >= 6);
                bool underPressure = myHpArmor <= 24;
                bool allow = earlyGame && hugeBoardGap && underPressure;
                if (!allow) return false;

                reason = "前期场面劣势(敌从=" + enemyMinions
                    + ",我从=" + friendMinions
                    + ",敌攻=" + enemyAttack
                    + ",我血甲=" + myHpArmor
                    + ",0费郊狼=" + (hasZeroCostCoyote ? "Y" : "N") + ")";
                return true;
            }
            catch
            {
                reason = null;
                return false;
            }
        }

        /// <summary>
        /// 处理咒??墓的?鏈夐?昏緫
        /// </summary>
        private void ProcessCard_TLC_451_CurseTomb(ProfileParameters p, Board board)
        {
            // ==============================================
            // Card.Cards.TLC_451 - 咒怨之墓
            // 说明：限制使?潯浠?MaxMana>=3 鎴?MaxMana>=2有币)锛?费特许?发现优先级
            // ==============================================
            
            if (!board.HasCardInHand(Card.Cards.TLC_451)) return;
            
            try
            {
                var curse = board.Hand.FirstOrDefault(x => x != null && x.Template != null 
                    && x.Template.Id == Card.Cards.TLC_451);
                if (curse == null) return;
                
                // 妫?鏌?槸鍚?硬币
                bool hasCoin = board.Hand.Any(x => x != null && x.Template != null 
                    && x.Template.Id == Card.Cards.GAME_005);
                int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);

                bool emergencyAllowTombInEarlyBoardGap = false;
                int emergencyEnemyMinions = 0;
                int emergencyFriendMinions = 0;
                int emergencyEnemyAttack = 0;
                int emergencyMyHpArmor = 0;
                bool emergencyHasZeroCostCoyote = false;
                bool allowTombBeforeHandCaveByLowDiscard = false;
                try
                {
                    emergencyEnemyMinions = board.MinionEnemy != null
                        ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0)
                        : 0;
                    emergencyFriendMinions = board.MinionFriend != null
                        ? board.MinionFriend.Count(m => m != null && m.CurrentHealth > 0)
                        : 0;
                    emergencyEnemyAttack = board.MinionEnemy != null
                        ? board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk))
                        : 0;
                    emergencyMyHpArmor = board.HeroFriend != null
                        ? Math.Max(0, board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor)
                        : 0;
                    emergencyHasZeroCostCoyote = board.Hand.Any(h => h != null
                        && h.Template != null
                        && IsCoyoteCardVariant(h)
                        && h.CurrentCost == 0);

                    bool earlyGame = board.MaxMana <= 4;
                    bool hugeBoardGap = emergencyEnemyMinions >= emergencyFriendMinions + 2
                        || (emergencyEnemyMinions >= 2 && emergencyEnemyAttack >= 6);
                    bool underPressure = emergencyMyHpArmor <= 24;

                    emergencyAllowTombInEarlyBoardGap = curse.CurrentCost == 0
                        && earlyGame
                        && hugeBoardGap
                        && underPressure
                        && !BoardHelper.IsLethalPossibleThisTurn(board);

                    // 低被弃占比：优先用墓找被弃组件，暂缓手上地标
                    try
                    {
                        var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                        var discardSetForTomb = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                        int nonTombNonCoinCount = handForDiscardLogic.Count(h => h != null && h.Template != null
                            && h.Template.Id != Card.Cards.TLC_451
                            && h.Template.Id != Card.Cards.GAME_005
                            && !ShouldIgnoreCardForCaveDiscardRatio(h));
                        int discardNonTombNonCoinCount = handForDiscardLogic.Count(h => h != null && h.Template != null
                            && h.Template.Id != Card.Cards.TLC_451
                            && h.Template.Id != Card.Cards.GAME_005
                            && !ShouldIgnoreCardForCaveDiscardRatio(h)
                            && discardSetForTomb.Contains(h.Template.Id));
                        double ratio = nonTombNonCoinCount > 0 ? (double)discardNonTombNonCoinCount / Math.Max(1, nonTombNonCoinCount) : 0.0;
                        allowTombBeforeHandCaveByLowDiscard = nonTombNonCoinCount > 0
                            && ratio < 0.2
                            && !BoardHelper.IsLethalPossibleThisTurn(board);
                    }
                    catch { allowTombBeforeHandCaveByLowDiscard = false; }
                }
                catch
                {
                    emergencyAllowTombInEarlyBoardGap = false;
                    allowTombBeforeHandCaveByLowDiscard = false;
                }

                // 新护栏：若本回合计划通过“硬币 -> 手牌地标(窟穴)”完成关键动作，
                // 且预计拍完地标后剩余可用法力为0，则本回合不再补打咒怨之墓。
                // 目的：避免出现“先币+地标，后手顺带拍墓”打乱节奏。
                try
                {
                    var caveInHandNow = board.Hand.FirstOrDefault(c => c != null
                        && c.Template != null
                        && c.Template.Id == Card.Cards.WON_103);
                    bool hasBoardSlotNowForCave = false;
                    try { hasBoardSlotNowForCave = GetFriendlyBoardSlotsUsed(board) < 7; } catch { hasBoardSlotNowForCave = false; }

                    bool canPlayCaveThisTurn = caveInHandNow != null
                        && hasBoardSlotNowForCave
                        && caveInHandNow.CurrentCost <= effectiveMana;
                    bool coinNeededForCave = canPlayCaveThisTurn
                        && hasCoin
                        && caveInHandNow.CurrentCost > board.ManaAvailable
                        && caveInHandNow.CurrentCost <= effectiveMana;

                    int manaAfterPlannedCave = board.ManaAvailable;
                    if (canPlayCaveThisTurn)
                    {
                        int manaBeforeCave = board.ManaAvailable + (coinNeededForCave ? 1 : 0);
                        manaAfterPlannedCave = Math.Max(0, manaBeforeCave - caveInHandNow.CurrentCost);
                    }

                    if (curse.CurrentCost == 0 && canPlayCaveThisTurn && manaAfterPlannedCave <= 0
                        && !allowTombBeforeHandCaveByLowDiscard)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(9999));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-9999));
                        AddLog("[咒怨之墓硬门槛] 本回合需先拍手牌地标且拍后剩余法力=0 => 禁止使用咒怨之墓(9999/-9999)");
                        return;
                    }
                    if (curse.CurrentCost == 0 && canPlayCaveThisTurn && manaAfterPlannedCave <= 0
                        && allowTombBeforeHandCaveByLowDiscard)
                    {
                        AddLog("[咒怨之墓优先级-低弃牌] 被弃占比<20% => 暂缓手上地标，优先墓");
                    }
                }
                catch { }
                
                // 【优先级0 - 使用条件检查】
                // 仅允许：MaxMana>=3，或 MaxMana>=2 且有币。
                // 二费无币一律不打墓，避免过早交墓打乱节奏。
                bool canUseCurse = board.MaxMana >= 3 || (board.MaxMana >= 2 && hasCoin);
                if (!canUseCurse && !emergencyAllowTombInEarlyBoardGap)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(999));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-999));
                    AddLog("[咒怨之墓条件限制] MaxMana=" + board.MaxMana + ", 有币=" + hasCoin + " => 禁止使用(999/-999)");
                    return;
                }
                if (!canUseCurse && emergencyAllowTombInEarlyBoardGap)
                {
                    AddLog("[咒怨之墓危机放行] 前期场面劣势(敌从=" + emergencyEnemyMinions
                        + ",我从=" + emergencyFriendMinions
                        + ",敌攻=" + emergencyEnemyAttack
                        + ",我血甲=" + emergencyMyHpArmor
                        + ",0费郊狼=" + (emergencyHasZeroCostCoyote ? "Y" : "N")
                        + ") => 放行使用咒怨之墓(忽略MaxMana门槛)");
                }

                // 【新增?限制（在现有?基础上）】可?垂鐢?繀椤?>= 2 才允许使?拻鎬?箣澧?
                if (board.ManaAvailable < 2 && !emergencyAllowTombInEarlyBoardGap)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(999));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-999));
                    AddLog("[咒怨之墓硬门槛] ManaAvailable=" + board.ManaAvailable + " < 2 => 禁止使用(999/-999)");
                    return;
                }
                if (board.ManaAvailable < 2 && emergencyAllowTombInEarlyBoardGap)
                {
                    AddLog("[咒怨之墓危机放行] 前期场面劣势且墓为0费 => 忽略ManaAvailable<2门槛");
                }
                
                // 【优先级1 - 费用耗尽保护?费但费用已?尽：延后使?
                if (curse.CurrentCost == 0 && board.ManaAvailable == 0)
                {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(500));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-999));
                        AddLog("[咒怨之墓保护] 费用耗尽(0/" + board.MaxMana + ") => 抑制使用(500/-999)");
                        return;
                }

                // 【优先级1.4】0费墓让位分流（防守抽解）：
                // 当本回合可分流，且手牌拥挤+敌方场压较高时，先分流找解，再考虑墓。
                // 避免出现“0费墓抢节奏，分流被挤掉”导致中期断解。
                if (curse.CurrentCost == 0)
                {
                    bool canTapNowForTombYield = false;
                    int enemyMinionCountForTombYield = 0;
                    int enemyAttackForTombYield = 0;
                    bool hasPlayableMerchantNow = false;
                    try
                    {
                        canTapNowForTombYield = CanUseLifeTapNow(board);
                        enemyMinionCountForTombYield = board.MinionEnemy != null
                            ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0)
                            : 0;
                        enemyAttackForTombYield = board.MinionEnemy != null
                            ? board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk))
                            : 0;

                        hasPlayableMerchantNow = board.Hand.Any(h => h != null
                            && h.Template != null
                            && h.Template.Id == Card.Cards.ULD_163
                            && h.CurrentCost <= board.ManaAvailable)
                            && GetFriendlyBoardSlotsUsed(board) < 7;
                    }
                    catch
                    {
                        canTapNowForTombYield = false;
                        enemyMinionCountForTombYield = 0;
                        enemyAttackForTombYield = 0;
                        hasPlayableMerchantNow = false;
                    }

                    bool highEnemyPressureForTombYield = enemyAttackForTombYield >= 8 || enemyMinionCountForTombYield >= 2;
                    bool crowdedHandForTombYield = board.Hand != null && board.Hand.Count >= 7;
                    bool enoughManaForTapYield = board.ManaAvailable >= 4;
                    bool shouldTapBeforeTomb = canTapNowForTombYield
                        && enoughManaForTapYield
                        && crowdedHandForTombYield
                        && highEnemyPressureForTombYield
                        && !hasPlayableMerchantNow;

                    if (shouldTapBeforeTomb && !emergencyAllowTombInEarlyBoardGap)
                    {
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(-2400)); } catch { }
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(-2400)); } catch { }
                        try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(1400)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-1300)); } catch { }
                        AddLog("[咒怨之墓让位分流] 0费墓可打但场压较高(敌攻=" + enemyAttackForTombYield
                            + ",敌从=" + enemyMinionCountForTombYield + ",手牌=" + (board.Hand != null ? board.Hand.Count : 0)
                            + ") => 先分流找解，墓后置(1400/-1300)");
                        return;
                    }
                    if (shouldTapBeforeTomb && emergencyAllowTombInEarlyBoardGap)
                    {
                        AddLog("[咒怨之墓危机放行] 命中场压高让位分流，但当前前期场面劣势 => 不让位分流，优先墓找被弃组件");
                    }
                }

                // 新口径：如果本回合存?叾瀹冣?滅?定可打?濈殑 combo，则暂缓使用咒??墓（包含0费墓与正常费??锛夈??
                // 说明：墓的价值偏“找机会/闃茬?杩団?，应让位给明‘可执行的连段?
                if (!emergencyAllowTombInEarlyBoardGap)
                {
                    try
                    {
                        string delayReason = null;
                        if (ShouldDelayCurseTombBecauseOtherComboAvailable(board, effectiveMana, out delayReason))
                        {
                            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(350));
                            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-900));
                            AddLog("[咒怨之墓互斥] " + delayReason + " => 暂缓使用咒怨之墓(350/-900)");
                            return;
                        }
                    }
                    catch { }
                }
                else
                {
                    AddLog("[咒怨之墓危机放行] 前期场面劣势 => 忽略墓互斥连段，优先用墓找行尸/弹幕等被弃组件");
                }
                
                // 【优先级1.5】0费墓不再让位分流：先墓后分流
                // 你要求“这里应该先用咒怨之墓”，因此当0费墓可打时，显式压后分流。
                if (curse.CurrentCost == 0)
                {
                    bool canTapNow = false;
                    try { canTapNow = CanUseLifeTapNow(board); }
                    catch { canTapNow = false; }

                    if (canTapNow)
                    {
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(1200)); } catch { }
                        try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(1200)); } catch { }
                        AddLog("[咒怨之墓前置] 0费咒怨之墓可用 => 先用墓，再考虑分流(分流后置1200)");
                    }
                }

                // 【优先级2 - 鏈?楂樸??费咒?墓：极高出牌优先级
                if (curse.CurrentCost == 0)
                {
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(9999));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-3000));
                    
                    // 添加到强制重算列?
                    if (!p.ForcedResimulationCardList.Contains(Card.Cards.TLC_451))
                        p.ForcedResimulationCardList.Add(Card.Cards.TLC_451);
                    
                    AddLog("[咒怨之墓优先级] 0费=> 极高优先(9999/-3000)，添加到重算列表（不覆盖武器攻击）");
                    return;
                }
                
                // 【优先级3】正常费用：优先发现
                else if (curse.CurrentCost <= board.ManaAvailable)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-1200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(8600));
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(1200)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(1200)); } catch { }
                    
                    // 添加到强制重算列?
                    if (!p.ForcedResimulationCardList.Contains(Card.Cards.TLC_451))
                        p.ForcedResimulationCardList.Add(Card.Cards.TLC_451);
                    
                    AddLog("[咒怨之墓优先级] 正常费用(" + curse.CurrentCost + "费 => 高优先使用(-1200/8600)，并延后分流(1200)");
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// 咒怨之墓TLC_451)互斥?：当本回合存?叾瀹冣?滅?定可打?濈殑 combo 时，暂缓使用墓??
        /// 娉?：这里不包含“墓本身”的?，避免自我互???
        /// </summary>
        private bool ShouldDelayCurseTombBecauseOtherComboAvailable(Board board, int effectiveMana, out string reason)
        {
            reason = null;

            try
            {
                if (board == null || board.Hand == null) return false;

                int handCount = 0;
                try { handCount = board.Hand.Count; } catch { handCount = 0; }

                int friendlyMinionsCount = 0;
                try { friendlyMinionsCount = board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { friendlyMinionsCount = 0; }
                bool hasSpaceForMinion = friendlyMinionsCount < 7;
                var handForDiscardLogicLocal = GetHandCardsForDiscardLogic(board);
                bool tombPlayableNow = false;
                bool preferPlayTombNow = false;
                try
                {
                    tombPlayableNow = handForDiscardLogicLocal.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.TLC_451
                        && h.CurrentCost <= board.ManaAvailable);
                    // 规则约束：墓本回合至少保留2点可用法力才允许使用。
                    preferPlayTombNow = tombPlayableNow && board.ManaAvailable >= 2;
                }
                catch
                {
                    tombPlayableNow = false;
                    preferPlayTombNow = false;
                }

                // 新规则：若本回合会从手使用地标（窟穴），则咒怨之墓必须让位。
                try
                {
                    var caveInHand = handForDiscardLogicLocal.FirstOrDefault(h => h != null && h.Template != null && h.Template.Id == Card.Cards.WON_103);
                    bool canPlayCaveFromHandNow = caveInHand != null
                        && caveInHand.CurrentCost <= effectiveMana
                        && GetFriendlyBoardSlotsUsed(board) < 7;

                    if (canPlayCaveFromHandNow)
                    {
                        var discardSetForCave = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                        int nonCaveNonCoinCount = handForDiscardLogicLocal.Count(c => c != null && c.Template != null
                            && c.Template.Id != Card.Cards.WON_103
                            && c.Template.Id != Card.Cards.GAME_005
                            && !ShouldIgnoreCardForCaveDiscardRatio(c));
                        int discardComponentNonCaveNonCoinCount = handForDiscardLogicLocal.Count(c => c != null && c.Template != null
                            && c.Template.Id != Card.Cards.WON_103
                            && c.Template.Id != Card.Cards.GAME_005
                            && !ShouldIgnoreCardForCaveDiscardRatio(c)
                            && discardSetForCave.Contains(c.Template.Id));
                        bool hasDiscardComponentForCave = discardComponentNonCaveNonCoinCount > 0;

                        Card caveOnly;
                        Card pirateOnly;
                        bool onlyCaveAndSpacePirate = IsOnlyCaveAndSpacePirateHand(board, out caveOnly, out pirateOnly);

                        if (hasDiscardComponentForCave || onlyCaveAndSpacePirate)
                        {
                            string caveDelayReason = null;
                            bool caveShouldDelayByOtherCombo = ShouldDelayCaveFromHandBecauseComboAvailable(
                                board, effectiveMana, handCount, discardSetForCave, out caveDelayReason, true);

                            if (!caveShouldDelayByOtherCombo)
                            {
                                reason = onlyCaveAndSpacePirate
                                    ? "本回合可执行地标+太空海盗连段 => 先地标后墓"
                                    : "本回合可从手使用地标且有弃牌收益(" + discardComponentNonCaveNonCoinCount + "/" + nonCaveNonCoinCount + ") => 先地标后墓";
                                return true;
                            }
                        }
                    }
                }
                catch { }

                Func<Card.Cards, bool> hasPlayableMinion = (id) =>
                {
                    try
                    {
                        if (!hasSpaceForMinion) return false;
                        return handForDiscardLogicLocal.Any(h => h != null && h.Template != null && h.Template.Id == id && h.CurrentCost <= effectiveMana);
                    }
                    catch { return false; }
                };

                Func<Card.Cards, bool> hasPlayableSpell = (id) =>
                {
                    try { return handForDiscardLogicLocal.Any(h => h != null && h.Template != null && h.Template.Id == id && h.CurrentCost <= effectiveMana); }
                    catch { return false; }
                };

                Func<Card.Cards, bool> hasCardInHand = (id) =>
                {
                    try { return handForDiscardLogicLocal.Any(h => h != null && h.Template != null && h.Template.Id == id); }
                    catch { return false; }
                };

                // 鍙栤?最高费组?（排除硬币/窟穴/墓），用于判断商?爪子能否稳定弃到目标?
                List<Card> relevant = null;
                try
                {
                    relevant = GetHandCardsForDiscardLogic(board).Where(h => h != null && h.Template != null
                        && h.Template.Id != Card.Cards.GAME_005
                        && h.Template.Id != Card.Cards.WON_103
                        && h.Template.Id != Card.Cards.TLC_451).ToList();
                }
                catch { relevant = new List<Card>(); }

                int maxCost = 0;
                try { maxCost = relevant.Count > 0 ? relevant.Max(h => h.CurrentCost) : 0; } catch { maxCost = 0; }
                List<Card> highest = null;
                try { highest = relevant.Where(h => h.CurrentCost == maxCost).ToList(); } catch { highest = new List<Card>(); }

                Func<Card.Cards, bool> highestAllAre = (id) =>
                {
                    try
                    {
                        if (highest == null || highest.Count == 0) return false;
                        return highest.All(h => h != null && h.Template != null && h.Template.Id == id);
                    }
                    catch { return false; }
                };

                // 璁＄“牌库仍可能有灵魂弹幕??
                int remainingBarrageInDeck = 0;
                try { remainingBarrageInDeck = GetRemainingBarrageCountInDeck(board); } catch { remainingBarrageInDeck = 0; }
                bool deckHasBarrage = remainingBarrageInDeck > 0;

                // 1) 牌库有灵魂弹幕：速写?优先
                if (deckHasBarrage && hasPlayableMinion(Card.Cards.TOY_916))
                {
                    bool shouldYieldSketchToDoubleTomb = false;
                    try
                    {
                        int tombCountInHand = handForDiscardLogicLocal.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.TLC_451);
                        bool hasPlayableTombNow = handForDiscardLogicLocal.Any(h => h != null && h.Template != null
                            && h.Template.Id == Card.Cards.TLC_451
                            && h.CurrentCost <= board.ManaAvailable);
                        bool hasCoinNow = false;
                        try { hasCoinNow = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoinNow = false; }

                        // 与速写“跳币让位双墓”一致：2费有币且双墓在手时，墓不再让位速写。
                        shouldYieldSketchToDoubleTomb = board.ManaAvailable == 2
                            && hasCoinNow
                            && tombCountInHand >= 2
                            && hasPlayableTombNow;
                    }
                    catch { shouldYieldSketchToDoubleTomb = false; }

                    if (!shouldYieldSketchToDoubleTomb)
                    {
                        reason = "牌库仍可能有灵魂弹幕(est=" + remainingBarrageInDeck + ")且本回合可用速写美术家";
                        return true;
                    }
                }

                // 2) 专卖?+ 灵魂弹幕（?瀹氾細当前最高费?弹幕?
                if (hasPlayableMinion(Card.Cards.ULD_163) && hasCardInHand(Card.Cards.RLK_534) && highestAllAre(Card.Cards.RLK_534))
                {
                    reason = "本回合可打专卖商且最高费稳定=灵魂弹幕 => 优先专卖商弃弹幕";
                    return true;
                }

                // 2.1) 郊狼压费后可转化为“商贩稳定弃弹幕”：先走脸压费，再走商贩连段，不应先墓。
                try
                {
                    string coyoteMerchantReason = null;
                    if (CanStabilizeMerchantBarrageAfterCoyoteFaceDiscount(board, effectiveMana, out coyoteMerchantReason))
                    {
                        reason = coyoteMerchantReason + " => 先A脸压费后再商贩弃弹幕";
                        return true;
                    }
                }
                catch { }

                // 3) 可打时空之爪 + 当前最高费组命中被弃组件：优先提刀连段，不先墓。
                // 用户口径：墓不要覆盖刀，墓可打也应让位提刀窗口。
                bool clawPlayableNow = false;
                bool highestHasDiscardComponent = false;
                try
                {
                    clawPlayableNow = board.Hand.Any(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.END_016
                        && h.CurrentCost <= effectiveMana);

                    if (clawPlayableNow && highest != null && highest.Count > 0)
                    {
                        var discardSet = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                        highestHasDiscardComponent = highest.Any(h => h != null && h.Template != null
                            && discardSet.Contains(h.Template.Id));
                    }
                }
                catch
                {
                    clawPlayableNow = false;
                    highestHasDiscardComponent = false;
                }

                if (clawPlayableNow && highestHasDiscardComponent)
                {
                    bool keepTombBeforeClawForSoulfirePrep = false;
                    try
                    {
                        Card prepCardForSoulfire = null;
                        int prepDiscardCountForSoulfire = 0;
                        int prepTotalCountForSoulfire = 0;
                        string prepReasonForSoulfire = null;
                        keepTombBeforeClawForSoulfirePrep =
                            preferPlayTombNow
                            && TryGetLowCostPrepForSoulfireBarrageWindow(
                                board,
                                out prepCardForSoulfire,
                                out prepDiscardCountForSoulfire,
                                out prepTotalCountForSoulfire,
                                out prepReasonForSoulfire)
                            && prepCardForSoulfire != null
                            && prepCardForSoulfire.Template != null
                            && prepCardForSoulfire.Template.Id == Card.Cards.TLC_451;
                    }
                    catch
                    {
                        keepTombBeforeClawForSoulfirePrep = false;
                    }

                    if (!keepTombBeforeClawForSoulfirePrep)
                    {
                        reason = "本回合可打时空之爪且最高费组命中被弃组件=> 优先提刀连段";
                        return true;
                    }
                }

                // 4) 爪子 + 灵魂弹幕（?定：已装备可?且最高费?弹幕?
                bool clawEquipped = false;
                bool heroCanAttackNow = false;
                try
                {
                    clawEquipped = board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.Template.Id == Card.Cards.END_016;
                    heroCanAttackNow = board.HeroFriend != null && board.HeroFriend.CanAttack;
                }
                catch { clawEquipped = false; heroCanAttackNow = false; }

                if (clawEquipped && heroCanAttackNow && hasCardInHand(Card.Cards.RLK_534) && highestAllAre(Card.Cards.RLK_534))
                {
                    reason = "已装备时空之爪且可攻击，最高费稳定=灵魂弹幕 => 优先弃弹幕";
                    return true;
                }

                // 5) 爪子 + 古尔丹之手（手牌<=8锛岀?瀹氾細当前最高费?槸鍙?墜锛?
                if (handCount <= 8 && clawEquipped && heroCanAttackNow && hasCardInHand(Card.Cards.BT_300) && highestAllAre(Card.Cards.BT_300))
                {
                    reason = "已装备时空之爪且可攻击，手牌<=8且最高费稳定=古尔丹之手=> 优先弃古手";
                    return true;
                }

                // 6) 船载火炮 + 海盗
                bool hasPirateInHandOrBoard = false;
                try
                {
                    bool piratesInHand = board.Hand.Any(h => h != null && h.Template != null && h.IsRace(Card.CRace.PIRATE));
                    bool piratesOnBoard = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.Template != null && m.IsRace(Card.CRace.PIRATE));
                    hasPirateInHandOrBoard = piratesInHand || piratesOnBoard;
                }
                catch { hasPirateInHandOrBoard = false; }

                if (hasPirateInHandOrBoard && (hasPlayableMinion(Card.Cards.CORE_NEW1_023) || hasPlayableMinion(Card.Cards.GVG_075)))
                {
                    reason = "本回合可打船载火炮且有海盗联动=> 优先火炮连段";
                    return true;
                }

                // 7) 超光子弹幕+ 邪翼蝠/郊狼（能打出弹幕且手里有连段随从?
                // 修正：若墓本回合可用，则不因超光子连段而延后墓。
                if (!preferPlayTombNow
                    && hasPlayableSpell(Card.Cards.TIME_027)
                    && (hasCardInHand(Card.Cards.YOD_032) || hasCardInHand(Card.Cards.TIME_047)))
                {
                    reason = "本回合可打超光子弹幕且手里有邪翼蝠/郊狼 => 优先弹幕连段";
                    return true;
                }

                bool hasPreferredNonMerchantStarterForGuldan = false;
                try
                {
                    string starterReason = null;
                    hasPreferredNonMerchantStarterForGuldan = HasPreferredNonMerchantStarterForGuldan(board, handCount, out starterReason);
                }
                catch { hasPreferredNonMerchantStarterForGuldan = false; }

                // 8) 专卖?+ 古尔丹之手（手牌<=8锛岀?瀹氾細当前最高费?槸鍙?墜锛?
                if (handCount <= 8
                    && hasPlayableMinion(Card.Cards.ULD_163)
                    && hasCardInHand(Card.Cards.BT_300)
                    && highestAllAre(Card.Cards.BT_300)
                    && !hasPreferredNonMerchantStarterForGuldan)
                {
                    reason = "本回合可打专卖商且手牌<=8，最高费稳定=古尔丹之手=> 优先专卖商弃古手";
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        /// <summary>
        /// 魂火保命放行：血线危险时允许优先用魂火解场。
        /// 该判定独立于“被弃组件密度”规则，专门用于避免被硬规则锁死而空过。
        /// </summary>
        private bool ShouldAllowSoulfireEmergencyDefense(Board board,
            out string reason, out int preferredEnemyTargetId, out int enemyAttackTotal, out int myHpArmor)
        {
            reason = null;
            preferredEnemyTargetId = 0;
            enemyAttackTotal = 0;
            myHpArmor = 0;

            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;

                var playableSoulfire = board.Hand.FirstOrDefault(c => c != null
                    && c.Template != null
                    && IsSoulfireCardVariant(c)
                    && c.CurrentCost <= board.ManaAvailable);
                if (playableSoulfire == null) return false;

                var enemyMinions = board.MinionEnemy != null
                    ? board.MinionEnemy.Where(m => m != null).ToList()
                    : null;
                if (enemyMinions == null || enemyMinions.Count == 0) return false;

                try
                {
                    myHpArmor = board.HeroFriend != null
                        ? Math.Max(0, board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor)
                        : 0;
                }
                catch { myHpArmor = 0; }

                try
                {
                    enemyAttackTotal = enemyMinions.Sum(m => Math.Max(0, m.CurrentAtk));
                }
                catch { enemyAttackTotal = 0; }

                try
                {
                    if (board.WeaponEnemy != null && board.WeaponEnemy.CurrentDurability > 0)
                        enemyAttackTotal += Math.Max(0, board.WeaponEnemy.CurrentAtk);
                }
                catch { }

                bool enemyHasBigThreat = false;
                try { enemyHasBigThreat = enemyMinions.Any(m => m != null && m.CurrentAtk >= 5); } catch { enemyHasBigThreat = false; }

                bool imminentLethal = myHpArmor > 0 && enemyAttackTotal >= myHpArmor;
                bool highRisk = myHpArmor <= 12
                    || (myHpArmor <= 18 && enemyAttackTotal >= Math.Max(1, myHpArmor - 2))
                    || (enemyHasBigThreat && myHpArmor <= 16)
                    // 场压扩窗：敌方多随从且总攻较高时，允许魂火作为末段解场动作。
                    || (enemyMinions.Count >= 3 && enemyAttackTotal >= 8 && myHpArmor <= 24);

                if (!imminentLethal && !highRisk) return false;

                dynamic killableBest = null;
                try
                {
                    killableBest = enemyMinions
                        .Where(m => m != null && m.CurrentHealth <= 4)
                        .OrderByDescending(m => Math.Max(0, m.CurrentAtk))
                        .ThenByDescending(m => m.IsTaunt ? 1 : 0)
                        .ThenByDescending(m => Math.Max(0, m.CurrentHealth))
                        .FirstOrDefault();
                }
                catch { killableBest = null; }

                if (killableBest != null)
                {
                    try { preferredEnemyTargetId = killableBest.Id; } catch { preferredEnemyTargetId = 0; }
                    reason = "血线危险(敌攻=" + enemyAttackTotal + ",我血=" + myHpArmor + ")，优先魂火解高威胁随从";
                    return true;
                }

                if (imminentLethal || myHpArmor <= 8)
                {
                    dynamic topThreat = null;
                    try
                    {
                        topThreat = enemyMinions
                            .OrderByDescending(m => Math.Max(0, m.CurrentAtk))
                            .ThenByDescending(m => m.IsTaunt ? 1 : 0)
                            .ThenByDescending(m => Math.Max(0, m.CurrentHealth))
                            .FirstOrDefault();
                    }
                    catch { topThreat = null; }

                    if (topThreat != null)
                    {
                        try { preferredEnemyTargetId = topThreat.Id; } catch { preferredEnemyTargetId = 0; }
                        reason = "血线极危(敌攻=" + enemyAttackTotal + ",我血=" + myHpArmor + ")，优先魂火压制最高威胁";
                        return true;
                    }
                }
            }
            catch
            {
                reason = null;
                preferredEnemyTargetId = 0;
                enemyAttackTotal = 0;
                myHpArmor = 0;
            }

            return false;
        }

        /// <summary>
        /// 魂火吸血威胁放行：
        /// 当敌方存在“可被魂火击杀(血<=4)的吸血随从”时，允许魂火前置解场，
        /// 以避免对手通过吸血随从回补血量并反打。
        /// </summary>
        private bool ShouldAllowSoulfireVsLifestealThreat(Board board,
            out string reason, out int preferredEnemyTargetId)
        {
            reason = null;
            preferredEnemyTargetId = 0;

            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;

                bool hasPlayableSoulfire = board.Hand.Any(c => c != null
                    && c.Template != null
                    && IsSoulfireCardVariant(c)
                    && c.CurrentCost <= board.ManaAvailable);
                if (!hasPlayableSoulfire) return false;

                var enemyMinions = board.MinionEnemy != null
                    ? board.MinionEnemy.Where(m => m != null).ToList()
                    : new List<Card>();
                if (enemyMinions.Count == 0) return false;

                var killableLifesteal = enemyMinions
                    .Where(m => m != null && m.IsLifeSteal && m.CurrentHealth <= 4)
                    .OrderByDescending(m => m.IsTaunt ? 1 : 0)
                    .ThenByDescending(m => Math.Max(0, m.CurrentAtk))
                    .ThenByDescending(m => Math.Max(0, m.CurrentHealth))
                    .FirstOrDefault();
                if (killableLifesteal == null) return false;

                int myHpArmor = 0;
                try
                {
                    myHpArmor = board.HeroFriend != null
                        ? Math.Max(0, board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor)
                        : 0;
                }
                catch { myHpArmor = 0; }

                // 触发条件尽量保守：血线不高、目标具备嘲讽或高攻击，或场上存在多个吸血点。
                bool hasMultiLifestealThreat = false;
                try { hasMultiLifestealThreat = enemyMinions.Count(m => m != null && m.IsLifeSteal) >= 2; } catch { hasMultiLifestealThreat = false; }

                bool shouldForceClear = myHpArmor <= 24
                    || killableLifesteal.IsTaunt
                    || killableLifesteal.CurrentAtk >= 4
                    || hasMultiLifestealThreat;
                if (!shouldForceClear) return false;

                preferredEnemyTargetId = killableLifesteal.Id;
                reason = "敌方吸血威胁可被魂火击杀(id=" + killableLifesteal.Id
                    + ",攻/血=" + killableLifesteal.CurrentAtk + "/" + killableLifesteal.CurrentHealth
                    + ",Taunt=" + (killableLifesteal.IsTaunt ? "Y" : "N") + ")";
                return true;
            }
            catch
            {
                reason = null;
                preferredEnemyTargetId = 0;
                return false;
            }
        }

        /// <summary>
        /// 保命放行中的进攻分支：
        /// 未达立即致死时，若已装备时空之爪且手中有直伤压血组件，则允许魂火优先打脸。
        /// </summary>
        private bool ShouldSoulfireEmergencyPreferFace(Board board, int enemyAttackTotal, int myHpArmor)
        {
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;
                if (myHpArmor <= 0) return false;
                if (enemyAttackTotal >= myHpArmor) return false; // 立即致死风险时仍优先解场

                bool enemyHasTaunt = false;
                try { enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { enemyHasTaunt = false; }
                if (enemyHasTaunt) return false;

                bool clawEquipped = false;
                try
                {
                    clawEquipped = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0;
                }
                catch { clawEquipped = false; }
                if (!clawEquipped) return false;

                int barrageCount = 0;
                bool hasPhoton = false;
                int soulfireCount = 0;
                try
                {
                    barrageCount = board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534);
                    hasPhoton = board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.TIME_027);
                    soulfireCount = board.Hand.Count(c => c != null && c.Template != null && IsSoulfireCardVariant(c));
                }
                catch
                {
                    barrageCount = 0;
                    hasPhoton = false;
                    soulfireCount = 0;
                }

                bool hasBurstPressure = barrageCount > 0 || hasPhoton;
                if (!hasBurstPressure || soulfireCount <= 0) return false;

                return myHpArmor >= 8 && enemyAttackTotal <= myHpArmor - 3;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 单魂火弃牌斩杀赌博窗口：
        /// 当本回合非稳定斩杀，但“魂火打脸 + 弃到伤害型被弃组件”可直接终结时，允许魂火前置赌博。
        /// 当前仅在敌方场面为空时放行，避免随机伤害分流导致误判。
        /// </summary>
        private bool ShouldAllInSingleSoulfireDiscardLethalGamble(Board board, bool lethalThisTurn, out string reason)
        {
            reason = null;
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;
                if (lethalThisTurn) return false;

                var castableSoulfire = board.Hand.FirstOrDefault(c => c != null
                    && c.Template != null
                    && IsSoulfireCardVariant(c)
                    && c.CurrentCost <= board.ManaAvailable);
                if (castableSoulfire == null) return false;

                int enemyHp = 0;
                try
                {
                    enemyHp = board.HeroEnemy != null
                        ? Math.Max(0, board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor)
                        : 0;
                }
                catch { enemyHp = 0; }
                if (enemyHp <= 0) return false;

                int enemyMinionCount = 0;
                try
                {
                    enemyMinionCount = board.MinionEnemy != null
                        ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0)
                        : 0;
                }
                catch { enemyMinionCount = 0; }
                if (enemyMinionCount > 0) return false;

                int faceAttackPotential = 0;
                try
                {
                    bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                    if (!enemyHasTaunt)
                    {
                        if (board.MinionFriend != null)
                        {
                            faceAttackPotential += board.MinionFriend
                                .Where(m => m != null && m.CanAttack && !m.IsFrozen)
                                .Sum(m => Math.Max(0, m.CurrentAtk));
                        }

                        bool heroCanAttack = board.HeroFriend != null && board.HeroFriend.CanAttack && !board.HeroFriend.IsFrozen;
                        if (heroCanAttack)
                        {
                            int heroAtk = Math.Max(0, board.HeroFriend.CurrentAtk);
                            try
                            {
                                if (board.WeaponFriend != null && board.WeaponFriend.CurrentDurability > 0)
                                    heroAtk = Math.Max(heroAtk, Math.Max(0, board.WeaponFriend.CurrentAtk));
                            }
                            catch { }
                            faceAttackPotential += heroAtk;
                        }
                    }
                }
                catch { faceAttackPotential = 0; }

                int guaranteedDamage = faceAttackPotential + 4; // 魂火打脸
                if (enemyHp <= guaranteedDamage) return false;

                var discardPool = board.Hand
                    .Where(c => c != null && c.Template != null && c.Id != castableSoulfire.Id)
                    .ToList();
                int poolCount = discardPool.Count;
                if (poolCount <= 0) return false;

                int successCount = 0;
                int maxBurst = 0;
                foreach (var c in discardPool)
                {
                    if (c == null || c.Template == null) continue;

                    int burst = 0;
                    if (c.Template.Id == Card.Cards.RLK_534) burst = 5;
                    else if (c.Template.Id == Card.Cards.AT_022) burst = 4;
                    else if (c.Template.Id == Card.Cards.WW_044t) burst = 4;

                    if (burst <= 0) continue;
                    if (burst > maxBurst) maxBurst = burst;
                    if (enemyHp <= guaranteedDamage + burst) successCount++;
                }

                if (successCount <= 0 || maxBurst <= 0) return false;

                double successRate = (double)successCount / Math.Max(1, poolCount);
                if (successRate < 0.20) return false;
                int successRatePct = (int)Math.Round(successRate * 100.0);

                int strictReach = guaranteedDamage + maxBurst;
                if (enemyHp <= strictReach)
                {
                    reason = "敌方血量" + enemyHp
                        + "处于单魂火弃牌斩杀区间(基础=" + guaranteedDamage
                        + ",补伤上限=" + maxBurst
                        + ",命中率≈" + successRatePct + "%)";
                    return true;
                }

                // 放宽窗口：非必杀但接近斩杀时也允许魂火赌博，避免错过高赔率终结线。
                const int nearLethalRelaxHp = 2;
                if (enemyHp <= strictReach + nearLethalRelaxHp && successRate >= 0.30)
                {
                    reason = "敌方血量" + enemyHp
                        + "处于单魂火弃牌近斩杀区间(基础=" + guaranteedDamage
                        + ",补伤上限=" + maxBurst
                        + ",放宽=+" + nearLethalRelaxHp
                        + ",命中率≈" + successRatePct + "%)";
                    return true;
                }

                return false;
            }
            catch
            {
                reason = null;
                return false;
            }
        }

        /// <summary>
        /// 双魂火斩杀赌博窗口：
        /// 非确定斩杀时，若敌方血量落在“需要双魂火且本回合可尝试”的区间，
        /// 则优先保留魂火窗口，避免先手打出会降低成功率的动作（如恐怖海盗）。
        /// </summary>
        private bool ShouldAllInDoubleSoulfireLethalGamble(Board board, bool lethalThisTurn, out string reason)
        {
            reason = null;
            try
            {
                if (board == null || board.Hand == null || board.Hand.Count == 0) return false;
                if (lethalThisTurn) return false;

                int enemyHp = 0;
                try
                {
                    enemyHp = board.HeroEnemy != null
                        ? Math.Max(0, board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor)
                        : 0;
                }
                catch { enemyHp = 0; }
                if (enemyHp <= 0) return false;

                int soulfireCount = 0;
                try
                {
                    soulfireCount = board.Hand.Count(c => c != null && c.Template != null && IsSoulfireCardVariant(c));
                }
                catch { soulfireCount = 0; }
                if (soulfireCount < 2) return false;

                bool hasCoin = false;
                try { hasCoin = board.HasCardInHand(Card.Cards.GAME_005); } catch { hasCoin = false; }
                int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);
                if (effectiveMana < 2) return false;

                // 仅当存在“非0概率连打第二张魂火”时才赌：
                // 两张魂火+至少另一张手牌（排除硬币）时，第一张魂火不必然弃掉第二张。
                var nonCoinHand = GetHandCardsForDiscardLogic(board, true)
                    .Where(c => c != null && c.Template != null)
                    .ToList();
                if (nonCoinHand.Count < 3) return false;

                bool enemyHasTaunt = false;
                try { enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { enemyHasTaunt = false; }

                int faceAttackPotential = 0;
                try
                {
                    if (!enemyHasTaunt)
                    {
                        if (board.MinionFriend != null)
                        {
                            faceAttackPotential += board.MinionFriend
                                .Where(m => m != null && m.CanAttack && !m.IsFrozen)
                                .Sum(m => Math.Max(0, m.CurrentAtk));
                        }

                        bool heroCanAttack = board.HeroFriend != null && board.HeroFriend.CanAttack && !board.HeroFriend.IsFrozen;
                        if (heroCanAttack)
                        {
                            int heroAtk = Math.Max(0, board.HeroFriend.CurrentAtk);
                            try
                            {
                                if (board.WeaponFriend != null && board.WeaponFriend.CurrentDurability > 0)
                                    heroAtk = Math.Max(heroAtk, Math.Max(0, board.WeaponFriend.CurrentAtk));
                            }
                            catch { }
                            faceAttackPotential += heroAtk;
                        }
                    }
                }
                catch { faceAttackPotential = 0; }

                // 触发区间：
                // - 单魂火(+4)不够；
                // - 双魂火(+8)有机会斩杀。
                int minNeed = faceAttackPotential + 5;
                int maxReach = faceAttackPotential + 8;
                if (enemyHp < minNeed || enemyHp > maxReach) return false;

                reason = "敌方血量" + enemyHp + "落在双魂火斩杀赌博区间(" + minNeed + "-" + maxReach + ")";
                return true;
            }
            catch
            {
                reason = null;
                return false;
            }
        }

        /// <summary>
        /// 处理船载火炮的所有?昏緫
        /// </summary>
        private void ProcessCard_CORE_NEW1_023_ShipCannon(ProfileParameters p, Board board)
        {
            // ==============================================
            // Card.Cards.CORE_NEW1_023 - 船载火炮
            // 说明：必须在?有海盗之前出场，?彂浼??
            // ==============================================
            
            bool hasCannon = board.HasCardInHand(Card.Cards.CORE_NEW1_023) 
                || board.HasCardInHand(Card.Cards.GVG_075);
            if (!hasCannon) return;
            
            try
            {
                bool hasPiratesInHand = board.Hand != null && board.Hand.Any(h => h != null 
                    && h.Template != null && h.IsRace(Card.CRace.PIRATE));
                bool hasPiratesOnBoard = board.MinionFriend != null && board.MinionFriend.Any(m => 
                    m != null && m.Template != null && m.IsRace(Card.CRace.PIRATE));
                
                // 【优先级1 - 鏈?楂樸?有海盗时：火炮必』鏈?先出
                if (hasPiratesInHand || hasPiratesOnBoard)
                {
                    // 船载火炮?高优先级
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_023, new Modifier(-150));
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(-150));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_023, new Modifier(8000));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(8000));
                    
                    // 降低?有海盗的出牌顺序，‘保在火炮之后
                    if (board.Hand != null)
                    {
                        foreach (var h in board.Hand)
                        {
                            if (h != null && h.Template != null && h.IsRace(Card.CRace.PIRATE))
                            {
                                p.PlayOrderModifiers.AddOrUpdate(h.Template.Id, new Modifier(7500));
                            }
                        }
                    }
                    
                    AddLog("[船载火炮-优先级] 有海盗在场 => 火炮最优先(8000)，海盗后置(7500)");
                    
                    // 场面保护：火炮是输出核心
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_023, new Modifier(150));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(150));
                }
                // 【优先级2】无海盗时：?般优先级
                else
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_023, new Modifier(-40));
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GVG_075, new Modifier(-40));
                    AddLog("[船载火炮-优先级] 无海盗 => 一般优先(-40)");
                }
            }
            catch { }
        }

        /// <summary>
        /// 处理速写美术家的?鏈夐?昏緫
        /// </summary>
        private void ProcessCard_TOY_916_SketchArtist(ProfileParameters p, Board board)
        {
            // ==============================================
            // Card.Cards.TOY_916 - 速写美术?
            // 说明：检测牌库剩余RLK_534，决定优先级（当前构筑不?BT_300 时不应把它算作?价值法术?濓級
            // ==============================================
            
            if (!board.HasCardInHand(Card.Cards.TOY_916)) return;
            
            try
            {
                bool lethalThisTurn = false;
                try { lethalThisTurn = BoardHelper.IsLethalPossibleThisTurn(board); } catch { lethalThisTurn = false; }

                var sketch = board.Hand.FirstOrDefault(x => x != null && x.Template != null 
                    && x.Template.Id == Card.Cards.TOY_916);

                if (sketch == null) return;

                // 允许“跳币?熷啓鈥濓細2费有币时也视为可用（速写?费）
                var coin = board.Hand.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.GAME_005);
                bool hasCoin = coin != null;
                int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);

                if (sketch.CurrentCost > effectiveMana) return;
                
                // 计算牌库剩余关键法术（灵魂弹幕RLK_534；BT_300 按构筑开关）
                int handInHand = 0;
                int handInGrave = 0;

                if (board.Hand != null)
                {
                    handInHand = board.Hand.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.BT_300);
                }
                if (board.FriendGraveyard != null)
                {
                    handInGrave = board.FriendGraveyard.Count(id => id == Card.Cards.BT_300);
                }

                // 关键修复：采?守估算（?始卡?数减去已见弹幕），避免?牌库已空仍强推速写”??
                int barrageRemaining = GetRemainingBarrageCountInDeck(board);
                int bt300TotalConfigured = 1;
                try
                {
                    if (board.Deck != null)
                    {
                        int listedBt300 = board.Deck.Count(id => id == Card.Cards.BT_300);
                        if (listedBt300 > 0) bt300TotalConfigured = listedBt300;
                    }
                }
                catch { }
                int handRemaining = Math.Max(0, bt300TotalConfigured - handInHand - handInGrave);
                bool hasBt300InDeckByApi = false;
                try { hasBt300InDeckByApi = board.HasCardInDeck(Card.Cards.BT_300); } catch { hasBt300InDeckByApi = false; }
                if (handRemaining <= 0 && hasBt300InDeckByApi) handRemaining = 1;

                // 复盘?：打印?滀负浠?么判定有/无弹幕?的关键℃暟锛堝帇缂?崟琛岋級
                try
                {
                    if (VerboseLog)
                    {
                        AddLog("[速写-估算] m=" + board.ManaAvailable + "/" + board.MaxMana
                            + ";eff=" + effectiveMana
                            + ";hc=" + (board.Hand != null ? board.Hand.Count : 0)
                            + ";" + GetBarrageEstimateDebugString(board)
                            + ";btH=" + handInHand
                            + ";btG=" + handInGrave
                            + ";btRem=" + handRemaining);
                    }
                }
                catch { }
                
                bool hasPrioritySpell = barrageRemaining > 0 || handRemaining > 0;
                int handCount = board.Hand != null ? board.Hand.Count : 0;
                // 允许手牌=9时仍优先速写（本套里速写价值高于0费海盗抢节奏）。
                int sketchHandCap = 9;

                bool shouldYieldCoinSketchToDoubleTomb = false;
                try
                {
                    int tombCountInHand = board.Hand != null
                        ? board.Hand.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.TLC_451)
                        : 0;
                    bool hasPlayableTombNow = board.Hand != null
                        && board.Hand.Any(h => h != null && h.Template != null
                            && h.Template.Id == Card.Cards.TLC_451
                            && h.CurrentCost <= board.ManaAvailable);

                    // 仅放在“2费有币跳速写”窗口生效：双墓在手时让位墓连段，避免速写抢掉前期关键墓节奏。
                    shouldYieldCoinSketchToDoubleTomb = board.ManaAvailable == 2
                        && hasCoin
                        && tombCountInHand >= 2
                        && hasPlayableTombNow
                        && !lethalThisTurn;
                }
                catch { shouldYieldCoinSketchToDoubleTomb = false; }

                if (shouldYieldCoinSketchToDoubleTomb)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(1800));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-2200));
                    AddLog("[速写美术家-跳币让位双墓] 2费有币且手握双墓 => 不强制跳币速写，先走墓连段");
                    return;
                }

                // 【跳币优先级?费有?+ 速写(3费 + 牌库仍有价?兼硶鏈?=> 强制走?滃竵->速写?
                // 目的：避免用太?海盗等低收益?綔鎶?合，导致速写延后?
                if (board.ManaAvailable == 2 && hasCoin && sketch.CurrentCost == 3 && hasPrioritySpell && handCount <= sketchHandCap)
                {
                    p.ComboModifier = new ComboSet(coin.Id, sketch.Id);

                    // 保护：严禁用灵魂之火弃弹?
                    if (board.HasCardInHand(Card.Cards.RLK_534))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(999));
                        AddLog("[速写美术家保护] 禁止灵魂之火弃弹幕(999)");
                    }

                    // 跳币速写窗口下也压后恐怖海盗，避免0费海盗抢先拍。
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(1600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-1600)); } catch { }

                    AddLog("[速写美术家-跳币] 2费有币且牌库有价值法术(弹幕=" + barrageRemaining + ",古手=" + handRemaining + ") => 强制跳币速写(ComboSet)");
                    return;
                }
                
                // 【优先级0 - 超高】牌库有灵魂弹幕且手牌≤9：极高优先，?术家先过牌再?脊骞?
                if (barrageRemaining > 0 && handCount <= sketchHandCap)
                {
					  if (sketch.CurrentCost > board.ManaAvailable && hasCoin)
                    {
                        p.ComboModifier = new ComboSet(coin.Id, sketch.Id);
                        AddLog("[速写美术家-优先级] 需要跳币=> 先币后速写(ComboSet)");
                    }
                    else
                    {
                        p.ComboModifier = new ComboSet(sketch.Id);
                    }
                    
                    // 保护：严禁用灵魂之火弃弹?
                    if (board.HasCardInHand(Card.Cards.RLK_534))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(999));
                        AddLog("[速写美术家保护] 禁止灵魂之火弃弹幕(999)");
                    }

                    // 速写高优先时，压后恐怖海盗，避免“0费海盗抢先拍”打断速写节奏。
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(1600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-1600)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var h in board.Hand)
                            {
                                if (h == null || h.Template == null) continue;
                                if (h.Template.Id != Card.Cards.CORE_NEW1_022) continue;
                                try { p.CastMinionsModifiers.AddOrUpdate(h.Id, new Modifier(1600)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(h.Id, new Modifier(-1600)); } catch { }
                            }
                        }
                    }
                    catch { }
                    
                    AddLog("[速写美术家-优先级] 牌库有灵魂弹幕" + barrageRemaining + ")且手牌≤" + sketchHandCap + "(当前" + handCount + ") => 必出，并压后恐怖海盗");
                    return;
                }
                
                // 【优先级1】牌库有价?法术（非优先级0情况）：高优先发?
                // 鍙ｅ修正：只有?仍可能有灵魂弹幕?时才把速写当作必』拍下?
                // 鑻?魂弹?0（即使还?尔丹之手），默认不必出，避免 3 费白板浪费节奏??
                if (hasPrioritySpell && handCount <= sketchHandCap)
                {
                    if (barrageRemaining <= 0 && handRemaining <= 0)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(250));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-200));
                        AddLog("[速写美术家-优先级b] 牌库无灵魂弹幕(弹幕=0,古手=" + handRemaining + ") => 不必出，默认后置(250/-200)");
                        return;
                    }

                    if (sketch.CurrentCost > board.ManaAvailable && hasCoin)
                    {
                        p.ComboModifier = new ComboSet(coin.Id, sketch.Id);
                        AddLog("[速写美术家-优先级] 需要跳币=> 先币后速写(ComboSet)");
                    }
                    else
                    {
                        p.ComboModifier = new ComboSet(sketch.Id);
                    }
                    
                    // 保护：有速写时严禁用灵魂之火弃弹?
                    if (barrageRemaining > 0 && board.HasCardInHand(Card.Cards.RLK_534))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(999));
                        AddLog("[速写美术家保护] 禁止灵魂之火弃弹幕(999)");
                    }

                    // 同步压后恐怖海盗，避免与速写抢先手。
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(1400)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(-1400)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var h in board.Hand)
                            {
                                if (h == null || h.Template == null) continue;
                                if (h.Template.Id != Card.Cards.CORE_NEW1_022) continue;
                                try { p.CastMinionsModifiers.AddOrUpdate(h.Id, new Modifier(1400)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(h.Id, new Modifier(-1400)); } catch { }
                            }
                        }
                    }
                    catch { }
                    
                    AddLog("[速写美术家-优先级] 牌库有价值法术(弹幕=" + barrageRemaining + ",古手=" + handRemaining + ") => 必出");
                    return;
                }
                
                // 【优先级2】牌库无价?法术：轻度后置
                else
                {
                    // 妫?鏌?槸鍚?其他可用?綔
                    bool hasOtherAction = false;
                    if (board.Hand != null)
                    {
                        hasOtherAction = board.Hand.Any(h => h != null && h.Template != null 
                            && h.Template.Id != Card.Cards.TOY_916 
                            && h.CurrentCost <= board.ManaAvailable
                            && (h.Type == Card.CType.MINION || h.Type == Card.CType.SPELL));
                    }
                    
                    if (hasOtherAction)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(200));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-200));
                        AddLog("[速写美术家-优先级a] 牌库无价值法术且有其他动作=> 后置(200/-200)");
                    }
                    else
                    {
                        // 无其他动作时允许当白板站?
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-200));
                        AddLog("[速写美术家优先级b] 牌库无价值法术但无其他动作=> 允许站场(-200)");
                    }
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// 处理宝藏经销商的?鏈夐?昏緫
        /// </summary>
        private void ProcessCard_TOY_518_ToyDealer(ProfileParameters p, Board board)
        {
            // ==============================================
            // Card.Cards.TOY_518 - 宝藏经销?
            // 说明：T1优先级：宝藏>澶??>鐙楀?浜?
            // ==============================================
            
            if (!board.HasCardInHand(Card.Cards.TOY_518)) return;
            
            try
            {
                var dealer = board.Hand.FirstOrDefault(x => x != null && x.Template != null 
                    && x.Template.Id == Card.Cards.TOY_518);
                if (dealer == null || dealer.CurrentCost > board.ManaAvailable) return;
                
                bool isTurnOne = board.MaxMana == 1;
                
                // 【优先级1 - 鏈?楂樸?慣1：宝藏经销商最优先
                if (isTurnOne)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-350));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(3900));
                    
                    // T1有宝藏时压制狗?浜?
                    if (board.HasCardInHand(Card.Cards.LOOT_014) || board.HasCardInHand(Card.Cards.TRL_555))
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(999));
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TRL_555, new Modifier(999));
                    }
                    
                    AddLog("[宝藏经销商-优先级] T1最优先(3900/-350)，压制狗头人");
                    return;
                }
                
                // 【优先级2】非T1鏃讹細涓?般优先级
                else if (board.MaxMana >= 2)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-900));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(7600));
                    AddLog("[宝藏经销商-优先级] 非T1 => 提高优先(-900/7600)");
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// 处理太?海盗的所有?昏緫
        /// </summary>
        private void ProcessCard_GDB_333_SpacePirate(ProfileParameters p, Board board)
        {
            // ==============================================
            // Card.Cards.GDB_333 - 太空海盗
            // 说明：T1序列，配合火?
            // ==============================================
            
            if (!board.HasCardInHand(Card.Cards.GDB_333)) return;
            
            try
            {
                var pirate = board.Hand.FirstOrDefault(x => x != null && x.Template != null 
                    && x.Template.Id == Card.Cards.GDB_333);
                if (pirate == null || pirate.CurrentCost > board.ManaAvailable) return;

                Func<RulesSet, Card.Cards, int, bool> isDisabledInRulesSet = (rules, cardId, instanceId) =>
                {
                    try
                    {
                        if (rules == null) return false;

                        Rule r = null;
                        try { r = rules.RulesCardIds != null ? rules.RulesCardIds[cardId] : null; } catch { r = null; }
                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                        try { r = rules.RulesIntIds != null ? rules.RulesIntIds[(int)cardId] : null; } catch { r = null; }
                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                        try { r = rules.RulesCardIds != null ? rules.RulesCardIds[(Card.Cards)instanceId] : null; } catch { r = null; }
                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                        try { r = rules.RulesIntIds != null ? rules.RulesIntIds[instanceId] : null; } catch { r = null; }
                        if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;
                    }
                    catch { }
                    return false;
                };
                 
                bool isTurnOne = board.MaxMana == 1;

                // 口径修正：T1且可用硬币时，若可先下狗头人，且手里无速写/无墓，则“先狗头人后海盗”。
                // 目的：避免太空海盗抢跑，导致错过硬币展开下的稳定过牌节奏。
                bool coinOpenPreferKoboldFirst = false;
                try
                {
                    bool hasCoin = board.HasCardInHand(Card.Cards.GAME_005);
                    bool hasSketchInHand = board.HasCardInHand(Card.Cards.TOY_916);
                    bool hasTlc451InHand = board.HasCardInHand(Card.Cards.TLC_451);
                    bool coinUsableAndNotDisabled = false;
                    if (hasCoin)
                    {
                        bool coinPlayableByCost = false;
                        bool coinHardDisabled = false;
                        Card coinCard = null;
                        try
                        {
                            coinCard = board.Hand.FirstOrDefault(h => h != null
                                && h.Template != null
                                && h.Template.Id == Card.Cards.GAME_005);
                            coinPlayableByCost = coinCard != null && coinCard.CurrentCost <= board.ManaAvailable;
                        }
                        catch { coinPlayableByCost = false; }

                        try
                        {
                            if (p != null && coinCard != null)
                                coinHardDisabled = isDisabledInRulesSet(p.CastSpellsModifiers, Card.Cards.GAME_005, coinCard.Id);
                        }
                        catch { coinHardDisabled = false; }

                        coinUsableAndNotDisabled = coinPlayableByCost && !coinHardDisabled;
                    }

                    int effectiveMana = board.ManaAvailable + (coinUsableAndNotDisabled ? 1 : 0);
                    bool koboldPlayable = board.Hand.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.LOOT_014
                        && h.CurrentCost <= effectiveMana);

                    coinOpenPreferKoboldFirst = isTurnOne
                        && coinUsableAndNotDisabled
                        && koboldPlayable
                        && !hasSketchInHand
                        && !hasTlc451InHand;
                }
                catch { coinOpenPreferKoboldFirst = false; }

                  if (coinOpenPreferKoboldFirst)
                  {
                      try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(9999)); } catch { }
                      try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(-9999)); } catch { }
                      try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-2200)); } catch { }
                      try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9900)); } catch { }
                      AddLog("[太空海盗-让位狗头人] T1有硬币且无速写/无墓，且狗头人可下 => 暂缓太空海盗(9999/-9999)，先狗头人");
                      return;
                  }
 
                  // 新口径：手里有空降歹徒且海盗可用 => 优先海盗拉空降歹徒，狗头人让位。
                  try
                  {
                      bool hasParachuteInHand = board.HasCardInHand(Card.Cards.DRG_056);
                      bool hasKoboldPlayableNow = board.Hand.Any(h => h != null && h.Template != null
                          && h.Template.Id == Card.Cards.LOOT_014
                          && h.CurrentCost <= board.ManaAvailable);
 
                      if (hasParachuteInHand)
                      {
                          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(-2000));
                          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(9800));
                          if (hasKoboldPlayableNow)
                          {
                              p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9999));
                              p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-9999));
                          }
                          AddLog("[太空海盗-优先级] 手牌有空降歹徒 => 优先1费海盗拉空降歹徒(9800/-2000)，狗头人让位");
                          return;
                      }
                  }
                  catch { }
  
                  // 新口径：非T1且宝藏经销商可下时，太空海盗必须让位，避免抢经销商节奏。
                  bool toyDealerPlayableNow = false;
                try
                {
                    bool hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7;
                    toyDealerPlayableNow = hasBoardSlot && board.Hand.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.TOY_518
                        && h.CurrentCost <= board.ManaAvailable);
                }
                catch { toyDealerPlayableNow = false; }

                if (!isTurnOne && toyDealerPlayableNow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(2200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(-1800));
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(-1600));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_518, new Modifier(8400));
                    AddLog("[太空海盗-让位经销商] 非T1且宝藏经销商可下 => 延后海盗(2200/-1800)，先下经销商(-1600/8400)");
                    return;
                }
                
                // 【优先级1】T1：次于宝藏经?鍟?
                if (isTurnOne)
                {
                    bool hasToyDealer = board.HasCardInHand(Card.Cards.TOY_518);
                    
                    if (hasToyDealer)
                    {
                        // T1有宝藏时太?海盗降低优先级
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(-30));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(2900));
                        AddLog("[太空海盗-优先级a] T1有宝藏经销商=> 次优先2900/-30)");
                    }
                    else
                    {
                        // T1无宝藏时太?海盗可优?
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(-200));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(2900));
                        AddLog("[太空海盗-优先级b] T1无宝藏经销商=> 优先(2900/-200)");
                    }
                    return;
                }
                
                // 【优先级2】非T1时：降低优先级
                else if (board.MaxMana >= 2)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_333, new Modifier(-40));
                    AddLog("[太空海盗-优先级] 非T1 => 降低优先级-40)");
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// 处理乐器?师的?鏈夐?昏緫
        /// </summary>
        private void ProcessCard_ETC_418_InstrumentTech(ProfileParameters p, Board board)
        {
            // ==============================================
            // Card.Cards.ETC_418 - 乐器?甯?
            // 说明：抽?（时空之爪），检测牌库剩?
            // ==============================================
            
            if (!board.HasCardInHand(Card.Cards.ETC_418)) return;
            
            try
            {
                var tech = board.Hand.FirstOrDefault(x => x != null && x.Template != null 
                    && x.Template.Id == Card.Cards.ETC_418);
                if (tech == null || tech.CurrentCost > board.ManaAvailable) return;
                
                // 璁＄牌库?綑鏃剁?之爪（?诲叡2把）
                int clawInHand = 0;
                int clawEquipped = 0;
                int clawInGrave = 0;
                
                if (board.Hand != null)
                {
                    clawInHand = board.Hand.Count(h => h != null && h.Template != null 
                        && h.Template.Id == Card.Cards.END_016);
                }
                if (board.WeaponFriend != null && board.WeaponFriend.Template != null 
                    && board.WeaponFriend.Template.Id == Card.Cards.END_016)
                {
                    clawEquipped = 1;
                }
                // 坟场计算（使用FriendGraveyard锛?
                if (board.FriendGraveyard != null)
                {
                    clawInGrave = board.FriendGraveyard.Count(id => id == Card.Cards.END_016);
                }
                
                int clawRemaining = Math.Max(0, 2 - clawInHand - clawEquipped - clawInGrave);
                bool hasClawAlready = clawInHand > 0 || clawEquipped > 0;
                
                // 【优先级1 - 鏈?楂樸?牌库有?且手?场上无刀：极高优?
                if (clawRemaining > 0 && !hasClawAlready)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-9999));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(9999));
                    AddLog("[乐器技师优先级] 牌库有刀(" + clawRemaining + ")且无刀 => 极高优先(-9999/9999)");
                    return;
                }
                
                // 【优先级2】牌库有?但已有刀：暂?
                else if (clawRemaining > 0 && hasClawAlready)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(200));
                    AddLog("[乐器技师优先级] 牌库有刀但已有刀 => 暂缓(200)");
                    return;
                }
                
                // 【优先级3】牌库无?锛氬悗缃?
                else
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-200));
                    AddLog("[乐器技师优先级] 牌库无刀 => 后置(200/-200)");
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// 处理郊狼的所有?昏緫
        /// </summary>
        private void ProcessCard_TIME_047_Coyote(ProfileParameters p, Board board, bool canAttackFace, bool lethalThisTurn)
        {
            // ==============================================
            // Card.Cards.TIME_047 - 郊狼
            // 说明?璐硅?鍙戙?减费窗?
            // ==============================================
            
            try
            {
                if (board == null || board.Hand == null) return;

                var coyotes = GetHandCardsForDiscardLogic(board).Where(h => IsCoyoteCardVariant(h)).ToList();
                if (coyotes.Count == 0) return;
                
                var zeroCostCoyote = coyotes.FirstOrDefault(c => c.CurrentCost == 0);
                
                // 【优先级1 - 0费郊狼】默认最高优先；但非斩杀且可先过牌时，让位狗头人/栉龙（除提刀转化窗口外）
                if (zeroCostCoyote != null)
                {
                    bool canPlayDrawMinionNowForZero = false;
                    bool coyoteUnlocksClawDiscardWindowForZero = false;
                    try
                    {
                        bool hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7;
                        canPlayDrawMinionNowForZero = !lethalThisTurn
                            && hasBoardSlot
                            && board.Hand.Any(h => h != null
                                && h.Template != null
                                && h.CurrentCost <= board.ManaAvailable
                                && (h.Template.Id == Card.Cards.LOOT_014 || h.Template.Id == Card.Cards.TLC_603)
                                && !IsForeignInjectedCardForDiscardLogic(board, h));

                        if (canPlayDrawMinionNowForZero)
                        {
                            var discardSetForZero = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                            coyoteUnlocksClawDiscardWindowForZero = CanCoyoteUnlockClawDiscardWindow(board, discardSetForZero);
                        }
                    }
                    catch
                    {
                        canPlayDrawMinionNowForZero = false;
                        coyoteUnlocksClawDiscardWindowForZero = false;
                    }

                    if (canPlayDrawMinionNowForZero && !coyoteUnlocksClawDiscardWindowForZero)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(3200));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-3800));
                        try { p.CastMinionsModifiers.AddOrUpdate(zeroCostCoyote.Id, new Modifier(3200)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(zeroCostCoyote.Id, new Modifier(-3800)); } catch { }
                        AddLog("[郊狼-0费让位过牌] 可先打狗头人/栉龙且未命中提刀转化窗口 => 暂缓郊狼(-3800/3200)");
                        return;
                    }

                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-999));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(9999));
                    AddLog("[郊狼-优先级] 0费 => 最高优先(9999/-999)");
                    return;
                }
                
                var nonZeroCoyote = coyotes
                    .Where(c => c.CurrentCost > 0 && c.CurrentCost <= board.ManaAvailable)
                    .OrderBy(c => c.CurrentCost)
                    .FirstOrDefault();
                if (nonZeroCoyote == null) return;

                // 非斩杀时，命中“低费提纯后魂火弃弹幕”窗口：郊狼强制后置。
                // 目的：避免郊狼先手打断狗头人/栉龙 -> 魂火的弃牌提纯线。
                try
                {
                    if (!lethalThisTurn)
                    {
                        Card prepCard = null;
                        int prepDiscardCount = 0;
                        int prepTotalCount = 0;
                        string prepReason = null;
                        bool shouldYieldToSoulfirePrep = TryGetLowCostPrepForSoulfireBarrageWindow(
                            board,
                            out prepCard,
                            out prepDiscardCount,
                            out prepTotalCount,
                            out prepReason);

                        if (shouldYieldToSoulfirePrep)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(2800));
                            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-3800));
                            try { p.CastMinionsModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(2800)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(-3800)); } catch { }
                            AddLog("[郊狼-让位魂火提纯] " + (prepReason ?? ("弃牌密度=" + prepDiscardCount + "/" + prepTotalCount))
                                + " => 暂缓郊狼(-3800/2800)，先低费提纯后魂火");
                            return;
                        }
                    }
                }
                catch { }

                bool canTapNow = false;
                bool canClickCave = false;
                bool hasOtherPlayableAction = false;
                try { canTapNow = CanUseLifeTapNow(board); }
                catch { canTapNow = false; }
                try
                {
                    var clickableCaveOnBoard = board.MinionFriend?.FirstOrDefault(x => x != null && x.Template != null
                        && x.Template.Id == Card.Cards.WON_103 && GetTag(x, Card.GAME_TAG.EXHAUSTED) != 1);
                    canClickCave = clickableCaveOnBoard != null;
                }
                catch { canClickCave = false; }
                try
                {
                    var discardSet = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                    hasOtherPlayableAction = board.Hand.Any(h => h != null && h.Template != null
                        && h.Id != nonZeroCoyote.Id
                        && !IsCoyoteCardVariant(h)
                        && !IsFelwingCardVariant(h)
                        && h.Template.Id != Card.Cards.GAME_005
                        && h.Template.Id != Card.Cards.EX1_308
                        && h.CurrentCost <= board.ManaAvailable
                        && !IsForeignInjectedCardForDiscardLogic(board, h)
                        && !discardSet.Contains(h.Template.Id));
                }
                catch { hasOtherPlayableAction = false; }

                // 鐢?埛鍙ｅ緞锛?费郊狼属于?兜底动作?濄??
                // 只要当前还有其他可执行动作（可打其他?可分?可点地标），就先做其他动作；无动作时再下5费郊狼??
                bool isFiveCostCoyote = false;
                bool hasAnyOtherPlayableCard = false;
                bool canPlayDrawMinionNow = false;
                bool coyoteUnlocksClawDiscardWindowForDrawYield = false;
                try
                {
                    int originalCost = 5;
                    try { originalCost = nonZeroCoyote.Template != null ? nonZeroCoyote.Template.Cost : 5; } catch { originalCost = 5; }
                    isFiveCostCoyote = nonZeroCoyote.CurrentCost >= 5 && originalCost >= 5;

                    bool hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7;
                    canPlayDrawMinionNow = hasBoardSlot && board.Hand.Any(h => h != null && h.Template != null
                        && h.CurrentCost <= board.ManaAvailable
                        && (h.Template.Id == Card.Cards.LOOT_014 || h.Template.Id == Card.Cards.TLC_603));

                    var discardSetForFallback = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                    // 过滤掉常见?虽可打但当前策?通常会被硬禁”的牌，尽量贴近“真ｅ彲鐢?姩浣溾?濄??
                    hasAnyOtherPlayableCard = board.Hand.Any(h => h != null && h.Template != null
                        && h.Id != nonZeroCoyote.Id
                        && !IsCoyoteCardVariant(h)
                        && h.Template.Id != Card.Cards.GAME_005
                        && h.CurrentCost <= board.ManaAvailable
                        && !IsForeignInjectedCardForDiscardLogic(board, h)
                        && (
                            IsFelwingCardVariant(h)
                            || (!discardSetForFallback.Contains(h.Template.Id) && h.Template.Id != Card.Cards.EX1_308)
                        ));

                    try
                    {
                        coyoteUnlocksClawDiscardWindowForDrawYield = CanCoyoteUnlockClawDiscardWindow(board, discardSetForFallback);
                    }
                    catch { coyoteUnlocksClawDiscardWindowForDrawYield = false; }
                }
                catch
                {
                    isFiveCostCoyote = nonZeroCoyote.CurrentCost >= 5;
                    hasAnyOtherPlayableCard = false;
                    canPlayDrawMinionNow = false;
                    coyoteUnlocksClawDiscardWindowForDrawYield = false;
                }

                if (!lethalThisTurn && canPlayDrawMinionNow && !coyoteUnlocksClawDiscardWindowForDrawYield)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(3200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-3800));
                    try { p.CastMinionsModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(-3800)); } catch { }
                    AddLog("[郊狼-让位过牌] 可先打狗头人/栉龙且未命中提刀转化窗口 => 暂缓郊狼(-3800/3200)");
                    return;
                }

                if (isFiveCostCoyote && canPlayDrawMinionNow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(3200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-3800));
                    try { p.CastMinionsModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(-3800)); } catch { }
                    AddLog("[郊狼-五费兜底] 可先打狗头人/栉龙 => 强制延后5费郊狼(-3800/3200)");
                    return;
                }

                if (isFiveCostCoyote && (hasAnyOtherPlayableCard || canTapNow || canClickCave))
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(2600));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-3500));
                    try { p.CastMinionsModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(2600)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(-3500)); } catch { }
                    AddLog("[郊狼-五费兜底] 5费郊狼且仍有其它动作"
                        + "(otherCard=" + (hasAnyOtherPlayableCard ? "Y" : "N")
                        + ",tap=" + (canTapNow ? "Y" : "N")
                        + ",cave=" + (canClickCave ? "Y" : "N")
                        + ") => 暂缓使用(-3500/2600)，无动作时再出");
                    return;
                }

                bool notDiscounted = false;
                try { notDiscounted = nonZeroCoyote.CurrentCost >= nonZeroCoyote.Template.Cost; } catch { notDiscounted = false; }

                bool coyoteUnlocksClawDiscardWindow = coyoteUnlocksClawDiscardWindowForDrawYield;
                if (!coyoteUnlocksClawDiscardWindow)
                {
                    try
                    {
                        var discardSet = new HashSet<Card.Cards>(GetDiscardComponentsConsideringHand(board, 8));
                        coyoteUnlocksClawDiscardWindow = CanCoyoteUnlockClawDiscardWindow(board, discardSet);
                    }
                    catch { coyoteUnlocksClawDiscardWindow = false; }
                }

                bool nonZeroCoyoteIsTemporary = false;
                try { nonZeroCoyoteIsTemporary = IsTemporaryCard(board, nonZeroCoyote); } catch { nonZeroCoyoteIsTemporary = false; }
                if (nonZeroCoyoteIsTemporary)
                {
                    // 鐢?埛鍙ｅ：即使是临时牌，?费郊狼也不要直接拍，优先等待进一?帇费联动?
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(3200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-3600));
                    try { p.CastMinionsModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(3200)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(-3600)); } catch { }
                    AddLog("[郊狼-临时后置] 临时牌且非0费" + nonZeroCoyote.CurrentCost + "费 => 强制后置(-3600/3200)，等待压费窗口");
                    return;
                }

                if (coyoteUnlocksClawDiscardWindow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-1800));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(9300));
                    try { p.CastMinionsModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(-1800)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(9300)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.CS2_056_H1, new Modifier(1200)); } catch { }
                    try { AddOrUpdateHeroPowerModifier(p, Card.Cards.HERO_07bp, new Modifier(1200)); } catch { }
                    p.ForceResimulation = true;
                    AddLog("[郊狼-优先级] 可打郊狼将最高费转为被弃组件并解锁时空之爪挥刀 => 强推先出郊狼(-1800/9300)，暂缓分流(1200)");
                    return;
                }

                bool deadTurnRisk = !canAttackFace && !canTapNow && !canClickCave && !hasOtherPlayableAction;
                bool shouldStrongDelayNow = (canAttackFace || notDiscounted || canTapNow || canClickCave || hasOtherPlayableAction) && !deadTurnRisk;
                if (shouldStrongDelayNow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(1200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-2200));
	                    AddLog("[郊狼-后置] 非0费" + nonZeroCoyote.CurrentCost + "费且仍可压费/有其它动作"
	                        + "(face=" + (canAttackFace ? "Y" : "N")
                        + ",tap=" + (canTapNow ? "Y" : "N")
                        + ",cave=" + (canClickCave ? "Y" : "N")
                        + ",other=" + (hasOtherPlayableAction ? "Y" : "N")
                        + ",fullCost=" + (notDiscounted ? "Y" : "N")
                        + ") => 强后置(-2200/1200)");
                    return;
                }

                if (deadTurnRisk && nonZeroCoyote.CurrentCost <= 1)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-350));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(8200));
                    try { p.CastMinionsModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(-350)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(nonZeroCoyote.Id, new Modifier(8200)); } catch { }
                    AddLog("[郊狼-防空过] 非0费郊狼(1费)且当前无其它稳定动作 => 放宽后置，避免空过");
                    return;
                }
                if (deadTurnRisk && notDiscounted)
                {
                    AddLog("[郊狼-防空过] 非0费郊狼且当前无其它稳定动作 => 放宽后置，避免空过");
                }

                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(450));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_047, new Modifier(-900));
                AddLog("[郊狼-默认后置] 非0费" + nonZeroCoyote.CurrentCost + "费 => 稍后使用(-900/450)");
                return;
            }
            catch { }
        }

        /// <summary>
        /// 处理空降歹徒的所有?昏緫
        /// </summary>
        private void ProcessCard_DRG_056_Parachute(ProfileParameters p, Board board, bool canTapNow)
        {
            // ==============================================
            // Card.Cards.DRG_056 - 空降歹徒
            // 说明：有其他海盗时禁用，等被拉下
            // ==============================================
            
            if (!board.HasCardInHand(Card.Cards.DRG_056)) return;

            bool hasNoOtherPlayableNonCoinCard = false;
            bool canTapNowReal = canTapNow;
            try
            {
                try
                {
                    canTapNowReal = canTapNow || CanUseLifeTapNow(board);
                }
                catch { canTapNowReal = canTapNow; }

                bool hasBoardSlotNow = GetFriendlyBoardSlotsUsed(board) < 7;
                hasNoOtherPlayableNonCoinCard = board.Hand != null
                    && board.Hand.Any(h => h != null && h.Template != null
                        && h.Template.Id == Card.Cards.DRG_056
                        && h.CurrentCost <= board.ManaAvailable
                        && (h.Type != Card.CType.MINION || hasBoardSlotNow))
                    && !board.Hand.Any(h => h != null && h.Template != null
                        && h.Template.Id != Card.Cards.GAME_005
                        && h.Template.Id != Card.Cards.DRG_056
                        && h.CurrentCost <= board.ManaAvailable
                        && (h.Type != Card.CType.MINION || hasBoardSlotNow));
            }
            catch { hasNoOtherPlayableNonCoinCard = false; }

            // 硬让位：只要本回合可直接打狗头人/咒怨之墓，空降歹徒一律不先手。
            // 目的：避免“先手拍空降歹徒”打乱过牌与墓节奏。
            try
            {
                bool hasBoardSlotForMinion = GetFriendlyBoardSlotsUsed(board) < 7;
                bool hasKoboldByMana = hasBoardSlotForMinion
                    && board.Hand != null
                    && board.Hand.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.LOOT_014
                        && h.CurrentCost <= board.ManaAvailable);

                bool hasCoinForTomb = false;
                try { hasCoinForTomb = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.GAME_005); }
                catch { hasCoinForTomb = false; }

                bool canUseTombByTurnGate = board.MaxMana >= 3 || (board.MaxMana >= 2 && hasCoinForTomb);
                bool hasTombByMana = canUseTombByTurnGate
                    && board.Hand != null
                    && board.Hand.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.TLC_451
                        && h.CurrentCost <= board.ManaAvailable);

                if (hasKoboldByMana || hasTombByMana)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-9999)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (c.Template.Id != Card.Cards.DRG_056) continue;
                                try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                            }
                        }
                    }
                    catch { }

                    if (hasKoboldByMana)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-2200)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9700)); } catch { }
                    }
                    if (hasTombByMana)
                    {
                        try { p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-1800)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(9600)); } catch { }
                    }

                    AddLog("[空降歹徒-硬让位狗头/咒怨之墓] 本回合可直接打狗头人或咒怨之墓 => 禁止先手打空降歹徒(9999/-9999)");
                    return;
                }
            }
            catch { }

            // 新口径：有稳定过牌动作（狗头人/栉龙）时，空降歹徒必须让位。
            // 目的：避免“先拍空降歹徒导致过牌后置/空过”。
            try
            {
                Func<Card, bool> isHardDisabledByCastModifierCard = (card) =>
                {
                    try
                    {
                        if (card == null || card.Template == null) return true;

                        Func<RulesSet, Card, bool> isDisabledInRulesSet = (rules, c) =>
                        {
                            try
                            {
                                if (rules == null || c == null || c.Template == null) return false;

                                Rule r = null;
                                try { r = rules.RulesCardIds != null ? rules.RulesCardIds[c.Template.Id] : null; } catch { r = null; }
                                if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                try { r = rules.RulesIntIds != null ? rules.RulesIntIds[(int)c.Template.Id] : null; } catch { r = null; }
                                if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                try { r = rules.RulesIntIds != null ? rules.RulesIntIds[c.Id] : null; } catch { r = null; }
                                if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;
                            }
                            catch { }
                            return false;
                        };

                        if (isDisabledInRulesSet(p.CastMinionsModifiers, card)) return true;
                        if (isDisabledInRulesSet(p.CastSpellsModifiers, card)) return true;
                        if (isDisabledInRulesSet(p.CastWeaponsModifiers, card)) return true;
                        if (isDisabledInRulesSet(p.LocationsModifiers, card)) return true;
                    }
                    catch { }
                    return false;
                };

                bool hasBoardSlot = GetFriendlyBoardSlotsUsed(board) < 7;
                var playableKoboldsNow = hasBoardSlot && board.Hand != null
                    ? board.Hand.Where(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.LOOT_014
                        && h.CurrentCost <= board.ManaAvailable
                        && !isHardDisabledByCastModifierCard(h))
                        .ToList()
                    : new List<Card>();

                var playableDragonsNow = hasBoardSlot && board.Hand != null
                    ? board.Hand.Where(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.TLC_603
                        && h.CurrentCost <= board.ManaAvailable
                        && !isHardDisabledByCastModifierCard(h))
                        .ToList()
                    : new List<Card>();

                bool hasPlayableDrawMinionNow = playableKoboldsNow.Count > 0 || playableDragonsNow.Count > 0;

                if (hasPlayableDrawMinionNow)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-9999)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (c.Template.Id != Card.Cards.DRG_056) continue;
                                try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                            }
                        }
                    }
                    catch { }

                    if (playableKoboldsNow.Count > 0)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-2200)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(9700)); } catch { }
                        foreach (var k in playableKoboldsNow)
                        {
                            if (k == null) continue;
                            try { p.CastMinionsModifiers.AddOrUpdate(k.Id, new Modifier(-2200)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(k.Id, new Modifier(9700)); } catch { }
                        }
                    }

                    if (playableDragonsNow.Count > 0)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-1800)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(9600)); } catch { }
                        foreach (var d in playableDragonsNow)
                        {
                            if (d == null) continue;
                            try { p.CastMinionsModifiers.AddOrUpdate(d.Id, new Modifier(-1800)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(d.Id, new Modifier(9600)); } catch { }
                        }
                    }

                    AddLog("[空降歹徒-让位过牌] 本回合可打狗头人/栉龙(且未被硬禁用) => 禁用空降歹徒(9999/-9999)，优先过牌随从");
                    return;
                }
            }
            catch { }

            // 墓前置硬互斥：墓本回合可打且未命中互斥连段时，空降歹徒必须让位。
            // 解决“日志判断墓优先，但实际先拍空降歹徒”的错序。
            try
            {
                bool hasCoin = board.Hand != null && board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.GAME_005);
                int effectiveMana = board.ManaAvailable + (hasCoin ? 1 : 0);
                bool canUseTombByTurnGate = board.MaxMana >= 3 || (board.MaxMana >= 2 && hasCoin);

                var playableTomb = board.Hand != null
                    ? board.Hand.FirstOrDefault(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.TLC_451
                        && h.CurrentCost <= board.ManaAvailable)
                    : null;

                bool delayTombByCombo = false;
                string delayReason = null;
                try { delayTombByCombo = ShouldDelayCurseTombBecauseOtherComboAvailable(board, effectiveMana, out delayReason); }
                catch { delayTombByCombo = false; delayReason = null; }

                bool shouldYieldToTomb = playableTomb != null
                    && canUseTombByTurnGate
                    && board.ManaAvailable >= 2
                    && !delayTombByCombo;

                if (shouldYieldToTomb)
                {
                    try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(9999)); } catch { }
                    try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-9999)); } catch { }
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (c.Template.Id != Card.Cards.DRG_056) continue;
                                try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                            }
                        }
                    }
                    catch { }

                    AddLog("[空降歹徒-让位咒怨之墓] 墓可打且未命中互斥连段 => 禁止先手打空降歹徒(9999/-9999)");
                    return;
                }
            }
            catch { }

            // 新口径：手里有其他海盗或其他可用牌时，降低空降歹徒优先级（避免直接拍出）。
            try
            {
                Func<Card.Cards, bool> isDisabledByCastModifier = (id) =>
                {
                    try
                    {
                        Func<RulesSet, Card.Cards, bool> isDisabledInRulesSet = (rules, cardId) =>
                        {
                            try
                            {
                                if (rules == null) return false;

                                Rule r = null;
                                try { r = rules.RulesCardIds != null ? rules.RulesCardIds[cardId] : null; } catch { r = null; }
                                if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;

                                try { r = rules.RulesIntIds != null ? rules.RulesIntIds[(int)cardId] : null; } catch { r = null; }
                                if (r != null && r.CardModifier != null && r.CardModifier.Value >= 9000) return true;
                            }
                            catch { }
                            return false;
                        };

                        if (isDisabledInRulesSet(p.CastMinionsModifiers, id)) return true;
                        if (isDisabledInRulesSet(p.CastSpellsModifiers, id)) return true;
                        if (isDisabledInRulesSet(p.CastWeaponsModifiers, id)) return true;
                        if (isDisabledInRulesSet(p.LocationsModifiers, id)) return true;
                    }
                    catch { }
                    return false;
                };

                bool hasBoardSlotNow = GetFriendlyBoardSlotsUsed(board) < 7;
                int playableOtherPirates = board.Hand != null
                    ? board.Hand.Count(h => h != null
                        && h.Template != null
                        && h.Template.Id != Card.Cards.DRG_056
                        && h.IsRace(Card.CRace.PIRATE)
                        && h.CurrentCost <= board.ManaAvailable
                        && (h.Type != Card.CType.MINION || hasBoardSlotNow)
                        && !isDisabledByCastModifier(h.Template.Id))
                    : 0;

                bool hasOtherPlayableCard = board.Hand != null
                    && board.Hand.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id != Card.Cards.GAME_005
                        && h.Template.Id != Card.Cards.DRG_056
                        && h.CurrentCost <= board.ManaAvailable
                        && (h.Type != Card.CType.MINION || hasBoardSlotNow)
                        && !isDisabledByCastModifier(h.Template.Id));

                if (playableOtherPirates > 0)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-9999));
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (c.Template.Id == Card.Cards.DRG_056)
                                {
                                    try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                                    continue;
                                }

                                if (!c.IsRace(Card.CRace.PIRATE)) continue;
                                if (c.CurrentCost > board.ManaAvailable) continue;
                                if (c.Type == Card.CType.MINION && !hasBoardSlotNow) continue;
                                if (isDisabledByCastModifier(c.Template.Id)) continue;

                                try { p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, new Modifier(-1200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Template.Id, new Modifier(8600)); } catch { }
                                try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(-1200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(8600)); } catch { }
                            }
                        }
                    }
                    catch { }

                    AddLog("[空降歹徒-让位其他海盗] 存在其他可用海盗("
                        + playableOtherPirates + ") => 禁用空降歹徒(9999/-9999)，优先其他海盗");
                    return;
                }

                if (hasOtherPlayableCard)
                {
                    if (canTapNowReal)
                    {
                        try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(9999)); } catch { }
                        try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-9999)); } catch { }
                        try
                        {
                            if (board.Hand != null)
                            {
                                foreach (var c in board.Hand)
                                {
                                    if (c == null || c.Template == null) continue;
                                    if (c.Template.Id != Card.Cards.DRG_056) continue;
                                    try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                                    try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                                }
                            }
                        }
                        catch { }

                        AddLog("[空降歹徒-让位分流] 可抽一口且存在其他可用动作 => 禁止手动打出空降歹徒(9999/-9999)");
                        return;
                    }

                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(1200));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-1800));
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (c.Template.Id != Card.Cards.DRG_056) continue;
                                try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(1200)); } catch { }
                                try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-1800)); } catch { }
                            }
                        }
                    }
                    catch { }

                    AddLog("[空降歹徒-后置] 存在其他可用动作(其他海盗=0,其他可打牌=Y) => 降低优先级(1200/-1800)");
                    return;
                }
            }
            catch { }

            // 新口径：如果本回合可?娊涓?鍙?Life Tap 可用且有2费，就别手?媿涓嬬?降歹徒??
            // 目的：保留其“被海盗拉下/白嫖”价值，优先级垎娴佽?资源?
            bool shouldYieldToLifeTap = canTapNowReal;
            try
            {
                if (!shouldYieldToLifeTap)
                {
                    shouldYieldToLifeTap = CanUseLifeTapNow(board);
                }
            }
            catch { }

            if (shouldYieldToLifeTap)
            {
                try { p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(9999)); } catch { }
                try { p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-9999)); } catch { }
                try
                {
                    if (board.Hand != null)
                    {
                        foreach (var c in board.Hand)
                        {
                            if (c == null || c.Template == null) continue;
                            if (c.Template.Id != Card.Cards.DRG_056) continue;
                            try { p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(9999)); } catch { }
                            try { p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9999)); } catch { }
                        }
                    }
                }
                catch { }

                AddLog("[空降歹徒-硬规则] 可抽一口(含兜底可用判定) => 禁止手动打出(9999/-9999)");
                return;
            }

            if (hasNoOtherPlayableNonCoinCard)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-1200));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(8600));
                AddLog("[空降歹徒-兜底放行] 当前无其他可打非硬币手牌 => 允许直接打出(-1200/8600)");
                return;
            }

            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(250));
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DRG_056, new Modifier(-500));
            AddLog("[空降歹徒-优先级] 无其他可用动作 => 允许但低优先(250/-500)");
        }

        #endregion

        #region BoardHelper 辅助?

        public static class BoardHelper
        {
            public static bool IsLethalPossibleThisTurn(Board board)
            {
                if (board == null) return false;

                try
                {
                    // 仅计算?走脸斩?銆戞墍闇?的敌方英雄血量（不把敌方随从?量算进来?
                    int enemyHeroHp = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;

                    bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                    
                    // 计算手牌直伤
                    int handDamage = 0;
                    if (board.Hand != null)
                    {
                        foreach (var c in board.Hand)
                        {
                            if (c == null || c.Template == null) continue;
                            if (c.Type != Card.CType.SPELL) continue;
                            if (c.CurrentCost > board.ManaAvailable) continue;

                            int dmg = 0;
                            var id = c.Template.Id;
                            
                            // 灵魂之火
                            if (id == Card.Cards.EX1_308) dmg = 4;
                            // 超光子弹幕
                            else if (id == Card.Cards.TIME_027) dmg = 6;
                            // 灵魂弹幕
                            else if (id == Card.Cards.RLK_534) dmg = 5;
                            
                            handDamage += dmg;
                        }
                    }

                    // 璁＄畻鏃剁?之爪的?弃牌?鍙戙?额外伤害（不占法力，发生在攻击时）
                    // 绾?畾锛氬綋当前最高费为灵魂弹幕RLK_534)且已装备并可攻击时，?会弃掉它并?成额外?（按经验值估?6锛?
                    int clawDiscardDamage = 0;
                    try
                    {
                        bool hasClawEquipped = board.WeaponFriend != null
                            && board.WeaponFriend.Template != null
                            && board.WeaponFriend.Template.Id == Card.Cards.END_016
                            && board.WeaponFriend.CurrentDurability > 0;
                        bool heroCanAttack = board.HeroFriend != null && board.HeroFriend.CanAttack;

                        var handForDiscardLogic = GetHandCardsForDiscardLogic(board);
                        if (hasClawEquipped && heroCanAttack && handForDiscardLogic.Any())
                        {
                            int maxCostInHand = handForDiscardLogic.Max(h => h != null ? h.CurrentCost : -1);
                            bool highestIsSoulBarrage = handForDiscardLogic.Any(h => h != null && h.Template != null
                                && h.Template.Id == Card.Cards.RLK_534
                                && h.CurrentCost == maxCostInHand);

                            if (highestIsSoulBarrage)
                            {
                                clawDiscardDamage = 6;
                            }
                        }
                    }
                    catch
                    {
                        clawDiscardDamage = 0;
                    }
                    
                    // 计算英雄攻击
                    int heroAttack = 0;
                    // 有嘲讽时不计?脸攻击伤害（但弃牌?发仍可能造成走脸??锛屽凡鍦?clawDiscardDamage 里单算）
                    if (!enemyHasTaunt && board.HeroFriend != null && board.HeroFriend.CanAttack)
                    {
                        heroAttack = board.HeroFriend.CurrentAtk;
                        if (board.WeaponFriend != null && board.WeaponFriend.CurrentAtk > heroAttack)
                        {
                            heroAttack = board.WeaponFriend.CurrentAtk;
                        }
                    }
                    
                    // 计算场攻
                    int boardAttack = 0;
                    if (!enemyHasTaunt && board.MinionFriend != null)
                    {
                        foreach (var m in board.MinionFriend)
                        {
                            if (m != null && m.CanAttack && !m.IsFrozen)
                            {
                                boardAttack += m.CurrentAtk;
                            }
                        }
                    }
                    
                    // 灰烬元素（REV_960）按稳定直伤口径计入斩杀估算：每个按2点
                    int ashenStableDamage = 0;
                    try
                    {
                        if (board.MinionFriend != null)
                        {
                            int ashenCount = board.MinionFriend.Count(m => m != null
                                && m.Template != null
                                && m.Template.Id == Card.Cards.REV_960
                                && m.CurrentHealth > 0);
                            ashenStableDamage = Math.Max(0, ashenCount) * 2;
                        }
                    }
                    catch
                    {
                        ashenStableDamage = 0;
                    }

                    int totalAvailable = handDamage + clawDiscardDamage + heroAttack + boardAttack + ashenStableDamage;

                    return totalAvailable >= enemyHeroHp;
                }
                catch
                {
                    return false;
                }
            }
        }

        #endregion
    }
}
