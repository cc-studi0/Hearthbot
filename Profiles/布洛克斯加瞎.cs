using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    [Serializable]
    public class 布洛克斯加瞎 : Profile
    {
        private const string ProfileVersion = "2026-01-27.10";

        // 简易日志，用于复盘决策（累计到 `_log`，并尝试通过 Bot.Log 输出）
        private string _log = string.Empty;

        private void AddLog(string msg)
        {
            try
            {
                // 不输出时间戳，仅保留配置版本和消息，便于复盘但避免时间冗余
                var entry = $"{msg}";
                _log += (string.IsNullOrEmpty(_log) ? "" : "\r\n") + entry;
                try { Bot.Log(entry); } catch { /* best-effort */ }
            }
            catch
            {
                // ignore logging failures
            }
        }

        // 常用卡片常量（参考套牌）
        private const Card.Cards PilotPatches = Card.Cards.VAC_933; // 飞行员帕奇斯
        private const Card.Cards FangDagger = Card.Cards.CORE_BAR_330; // 獠牙锥刃
        private const Card.Cards RedCard = Card.Cards.TOY_644; // 红牌
        private const Card.Cards TempoPush = Card.Cards.END_007; // 发挥优势
        private const Card.Cards Brox = Card.Cards.TIME_020; // 布洛克斯加
        private const Card.Cards CenariusAxe = Card.Cards.TIME_020t1; // 塞纳留斯之斧
        private const Card.Cards Demolisher = Card.Cards.CORE_REV_023; // 拆迁修理工
        private const Card.Cards GreedyHound = Card.Cards.EDR_891; // 贪婪的地狱猎犬
        private const Card.Cards BrutalBat = Card.Cards.EDR_892; // 残暴的魔蝠
        private const Card.Cards FuriousRemnant = Card.Cards.END_004; // 愤怒残魂
        private const Card.Cards FirstPortal = Card.Cards.TIME_020t2; // 第一道阿古斯传送门
        private const Card.Cards ReturnPolicy = Card.Cards.MIS_102; // 退货政策
        private const Card.Cards TheCoin = Card.Cards.GAME_005; // 幸运币

        public ProfileParameters GetParameters(Board board)
        {
            var p = new ProfileParameters(BaseProfile.Default) { DiscoverSimulationValueThresholdPercent = -10 };

            // 快照输出（直接打印，不做累积）
            try { DumpSnapshot(board); } catch { }

            // ===== 重新思考（ForcedResimulation） =====
            // 将当前手牌（除幸运币）全部加入重新思考队列，便于每打出一张牌后立即重算（减少顺序错误）。
            try
            {
                if (board != null && board.Hand != null && board.Hand.Count > 0)
                {
                    var excluded = new HashSet<Card.Cards> { TheCoin };
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
                        AddLog("重新思考：ForcedResimulation 覆盖 +" + unique.Count + " 张手牌（除幸运币）");
                }
            }
            catch { }

            if (board == null) return p;

            try
            {
                // 优先打出 1 费节奏（帕奇斯/獠牙锥刃）
                if (board.HasCardInHand(PilotPatches))
                {
                    p.CastMinionsModifiers.AddOrUpdate(PilotPatches, new Modifier(-450));
                    p.PlayOrderModifiers.AddOrUpdate(PilotPatches, new Modifier(9000));
                }

                if (board.HasCardInHand(FangDagger))
                {
                    p.CastMinionsModifiers.AddOrUpdate(FangDagger, new Modifier(-400));
                    p.PlayOrderModifiers.AddOrUpdate(FangDagger, new Modifier(8800));
                }

                // 鼓励早期法术与抽牌（发挥优势 / 恐怖收割 / 虫害）
                // 你最新诉求：发挥优势提高使用优先级
                p.CastSpellsModifiers.AddOrUpdate(TempoPush, new Modifier(-260));
                p.PlayOrderModifiers.AddOrUpdate(TempoPush, new Modifier(2500));
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_840, new Modifier(-150)); // 恐怖收割
                // 提高虫害侵扰 (TLC_902) 使用优先级：仅在手牌数 <= 9 时优先使用
                try
                {
                    int currentHandCount = board.Hand != null ? board.Hand.Count : 0;
                    if (currentHandCount <= 9)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_902, new Modifier(-350)); // 虫害侵扰 更偏好
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_902, new Modifier(3000));
                        AddLog($"虫害侵扰 TLC_902：手牌={currentHandCount} <=9 -> 提高使用优先级");
                    }
                    else
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_902, new Modifier(300));
                        AddLog($"虫害侵扰 TLC_902：手牌={currentHandCount} >9 -> 降低使用优先级");
                    }
                }
                catch { }

                // ===== 防守偏向调整：检测到敌方场上存在随从时，尽量避免打脸，优先解场并保护关键友方随从 =====
                try
                {
                    int enemyCountNow = board.MinionEnemy != null ? board.MinionEnemy.Count : 0;
                    int friendCountNow = board.MinionFriend != null ? board.MinionFriend.Count : 0;
                    if (enemyCountNow > 0)
                    {
                        // 避免打脸优先清场
                        try { p.WeaponsAttackModifiers.AddOrUpdate(board.HeroEnemy.Id, new Modifier(2000)); } catch { }
                        AddLog("策略调整：检测到敌方随从 -> 避免打脸，优先清场（防守）");

                        // 对当前敌方随从施加清场优先（降低其评分）
                        try
                        {
                            foreach (var em in board.MinionEnemy)
                            {
                                if (em == null || em.Template == null) continue;
                                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(em.Template.Id, new Modifier(-300));
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        // 敌方场面为空时，允许适度走脸
                        try { p.WeaponsAttackModifiers.AddOrUpdate(board.HeroEnemy.Id, new Modifier(-200)); } catch { }

                        // 你最新诉求：敌方没有随从且己方随从<=6 时，恐怖收割(EDR_840) 出牌次序应在 虫害侵扰/发挥优势 之前
                        // 做法：仅在该窗口提高恐怖收割的 PlayOrder，使其优先被考虑。
                        if (friendCountNow <= 6)
                        {
                            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_840, new Modifier(-450));
                            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_840, new Modifier(7200));
                            // 保证相对顺序：EDR_840 > TLC_902 > END_007
                            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_902, new Modifier(7000));
                            p.PlayOrderModifiers.AddOrUpdate(TempoPush, new Modifier(6800));
                            AddLog("出牌次序：敌方无随从 且 己方随从<=6 -> 恐怖收割优先于虫害侵扰/发挥优势");
                        }
                    }

                    // 保护友方嘲讽/高血随从，避免无谓牺牲
                    try
                    {
                        if (board.MinionFriend != null)
                        {
                            foreach (var fm in board.MinionFriend)
                            {
                                if (fm == null || fm.Template == null) continue;
                                if (fm.IsTaunt || fm.CurrentHealth >= 4)
                                {
                                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(fm.Template.Id, new Modifier(200));
                                }
                            }
                        }
                    }
                    catch { }

                    // 降低鼓励直接给英雄攻击的法术使用优先（例如 `发挥优势`），以防止盲目攻脸
                    try { p.CastSpellsModifiers.AddOrUpdate(TempoPush, new Modifier(300)); } catch { }
                }
                catch { }

                // 面对控制对手，保留更多中期价值卡
                if (IsControlOpponent(board))
                {
                    p.CastSpellsModifiers.AddOrUpdate(ReturnPolicy, new Modifier(-100));
                }

                // 大费卡降低优先级（避免 mulligan 后抓到无法早期使用的大费卡）
                p.CastMinionsModifiers.AddOrUpdate(Brox, new Modifier(600));
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330, new Modifier(900)); // 奇利亚斯
                // 根据场上随从数量调整奇利亚斯特定变体 TOY_330t5 的使用优先级
                try
                {
                    int friendCountForToy = board.MinionFriend != null ? board.MinionFriend.Count : 0;
                    if (friendCountForToy <= 5)
                    {
                        // 场上随从较少时，提高 TOY_330t5 的优先级以便触发奇利亚斯组合
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(-500));
                        AddLog("奇利亚斯 TOY_330t5：场上随从<=5 -> 提高使用优先级");
                    }
                    else
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(200));
                    }
                }
                catch { }
                // 默认降低 无界空宇 使用优先级（大费/清场危险）
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_142, new Modifier(900)); // 无界空宇

                // 塞纳留斯之斧：提高使用（装备）优先级
                if (board.HasCardInHand(CenariusAxe))
                {
                    p.CastWeaponsModifiers.AddOrUpdate(CenariusAxe, new Modifier(-450));
                    p.PlayOrderModifiers.AddOrUpdate(CenariusAxe, new Modifier(9800));
                    // 如果手里有黏团焦油，则优先使用黏团焦油，降低斧头使用优先级
                    try
                    {
                        if (board.HasCardInHand(Card.Cards.TLC_468))
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_468, new Modifier(-600));
                            p.CastWeaponsModifiers.AddOrUpdate(CenariusAxe, new Modifier(1200));
                            p.PlayOrderModifiers.AddOrUpdate(CenariusAxe, new Modifier(-12000));
                            AddLog("检测到手牌黏团焦油：优先使用黏团焦油，降低斧头使用优先级");
                        }
                    }
                    catch { }
                }

                // 只有当用斧头攻击可以解掉敌方随从（优先能刚好或足以杀死）时，才优先装备/攻击；否则尽量不装备也不去打脸
                try
                {
                    var axeTpl = CardTemplate.LoadFromId(CenariusAxe);
                    int axeAtk = axeTpl != null ? axeTpl.Atk : 3;
                    int weaponFriendAtk = board.WeaponFriend != null ? board.WeaponFriend.CurrentAtk : 0;
                    int heroBaseAtk = board.HeroFriend != null ? Math.Max(0, board.HeroFriend.CurrentAtk - weaponFriendAtk) : 0;
                    int expectedAtkAfterEquip = heroBaseAtk + axeAtk;

                    bool canKillAny = false;
                    if (board.MinionEnemy != null)
                    {
                        foreach (var m in board.MinionEnemy)
                        {
                            if (m == null || m.Template == null) continue;
                            // 忽略有圣盾的情况（不一定能一击杀）
                            if (m.IsDivineShield) continue;
                            if (m.CurrentHealth <= expectedAtkAfterEquip)
                            {
                                canKillAny = true; break;
                            }
                        }
                    }

                    if (canKillAny)
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(CenariusAxe, new Modifier(-600));
                        p.PlayOrderModifiers.AddOrUpdate(CenariusAxe, new Modifier(9900));
                        // 不鼓励用斧头去打脸（除非没有随从可杀）
                        try { p.WeaponsAttackModifiers.AddOrUpdate(board.HeroEnemy.Id, new Modifier(900)); } catch { }
                    }
                    else
                    {
                        // 无法斩杀时尽量不装备或不使用斧头攻击随从以外目标
                        p.CastWeaponsModifiers.AddOrUpdate(CenariusAxe, new Modifier(900));
                        p.PlayOrderModifiers.AddOrUpdate(CenariusAxe, new Modifier(-9000));
                        try { p.WeaponsAttackModifiers.AddOrUpdate(board.HeroEnemy.Id, new Modifier(1200)); } catch { }
                    }
                }
                catch { }

                // 如果斧头已经装备：仅在能斩杀随从时使用，尽量避免打脸；优先刚好斩杀的目标
                try
                {
                    if (board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.Template.Id == CenariusAxe)
                    {
                        int totalAtk = board.HeroFriend != null ? board.HeroFriend.CurrentAtk : 0;
                        bool anyMinion = board.MinionEnemy != null && board.MinionEnemy.Count > 0;

                        bool hasKillableMinion = false;
                        if (board.MinionEnemy != null)
                        {
                            foreach (var m in board.MinionEnemy)
                            {
                                if (m == null || m.Template == null) continue;
                                if (m.IsDivineShield) continue;
                                if (m.CurrentHealth == totalAtk)
                                {
                                    // 刚好斩杀：强烈优先
                                    p.WeaponsAttackModifiers.AddOrUpdate(m.Template.Id, new Modifier(-1000));
                                    hasKillableMinion = true;
                                }
                                else if (m.CurrentHealth < totalAtk)
                                {
                                    // 可斩杀：优先
                                    p.WeaponsAttackModifiers.AddOrUpdate(m.Template.Id, new Modifier(-300));
                                    hasKillableMinion = true;
                                }
                                else
                                {
                                    // 无法斩杀：避免用斧头去攻击该随从
                                    p.WeaponsAttackModifiers.AddOrUpdate(m.Template.Id, new Modifier(800));
                                }
                            }
                        }

                        // 是否可以用斧头直接斩杀敌方英雄（考虑护甲）
                        bool canKillHero = false;
                        try
                        {
                            var eh = board.HeroEnemy;
                            if (eh != null)
                            {
                                int heroEffectiveHp = eh.CurrentHealth + eh.CurrentArmor;
                                if (totalAtk >= heroEffectiveHp) canKillHero = true;
                            }
                        }
                        catch { }

                        // 规则：敌方有随从时，不要攻击敌方英雄，除非可以直接斩杀敌方英雄
                        if (anyMinion)
                        {
                            if (canKillHero)
                                p.WeaponsAttackModifiers.AddOrUpdate(board.HeroEnemy.Id, new Modifier(-500)); // 允许打脸以斩杀英雄
                            else
                                p.WeaponsAttackModifiers.AddOrUpdate(board.HeroEnemy.Id, new Modifier(2000)); // 强烈避免打脸（尽量只攻击随从）
                        }
                        else
                        {
                            // 无随从时，若存在可斩杀随从则优先斩杀，否则可适度打脸
                            if (hasKillableMinion)
                                p.WeaponsAttackModifiers.AddOrUpdate(board.HeroEnemy.Id, new Modifier(1200));
                            else
                                p.WeaponsAttackModifiers.AddOrUpdate(board.HeroEnemy.Id, new Modifier(-200));
                        }

                        AddLog("斧头已装备：仅用于斩杀随从（优先刚好斩杀）；有随从时避免打脸，除非可斩杀英雄");
                    }
                }
                catch { }

                // 第一道阿古斯传送门：仅在己方有“可攻击随从”或“装备武器且本回合可攻击”时才使用（避免空过后给对手白嫖）
                try
                {
                    bool hasAnyFriendlyAttackerNow = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.CurrentAtk > 0 && m.CanAttack);
                    bool heroCanAttackWithWeaponNow = board.WeaponFriend != null
                        && board.WeaponFriend.CurrentAtk > 0
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;
                    bool canUsePortalNow = hasAnyFriendlyAttackerNow || heroCanAttackWithWeaponNow;
                    if (board.HasCardInHand(FirstPortal))
                    {
                        if (canUsePortalNow)
                        {
                            p.CastSpellsModifiers.AddOrUpdate(FirstPortal, new Modifier(-1000));
                            p.PlayOrderModifiers.AddOrUpdate(FirstPortal, new Modifier(20000));
                            AddLog($"第一道阿古斯传送门 {FirstPortal}：己方有可攻击随从或武器可攻击 -> 强烈优先使用");
                        }
                        else
                        {
                            p.CastSpellsModifiers.AddOrUpdate(FirstPortal, new Modifier(900));
                            AddLog($"第一道阿古斯传送门 {FirstPortal}：当前无可攻击随从且武器不可攻击 -> 降低使用优先级");
                        }
                    }
                }
                catch { }

                // 其他传送门变体（TIME_020t2..t5）：仅在己方有“可攻击随从”或“装备武器且本回合可攻击”时才使用，且使用次序最高
                try
                {
                    var portalVariants = new Card.Cards[] { Card.Cards.TIME_020t2, Card.Cards.TIME_020t3, Card.Cards.TIME_020t4, Card.Cards.TIME_020t5 };
                    bool hasAnyFriendlyAttackerNow = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.CurrentAtk > 0 && m.CanAttack);
                    bool heroCanAttackWithWeaponNow = board.WeaponFriend != null
                        && board.WeaponFriend.CurrentAtk > 0
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;
                    bool canUsePortalNow = hasAnyFriendlyAttackerNow || heroCanAttackWithWeaponNow;
                    foreach (var pid in portalVariants)
                    {
                        if (board.HasCardInHand(pid))
                        {
                            if (canUsePortalNow)
                            {
                                p.CastSpellsModifiers.AddOrUpdate(pid, new Modifier(-1000));
                                p.PlayOrderModifiers.AddOrUpdate(pid, new Modifier(20000));
                                AddLog($"传送门变体 {pid}：己方有可攻击随从或武器可攻击 -> 强烈优先使用（次序最高）");
                            }
                            else
                            {
                                p.CastSpellsModifiers.AddOrUpdate(pid, new Modifier(900));
                                AddLog($"传送门变体 {pid}：当前无可攻击随从且武器不可攻击 -> 降低使用优先级");
                            }
                        }
                    }
                }
                catch { }

                // ===== 无界空宇 GDB_142：仅在己方场面劣势时使用（避免在己方优势时清场） =====
                try
                {
                    var boundlessSpace = board.Hand != null ? board.Hand.FirstOrDefault(x => x.Template != null && x.Template.Id == Card.Cards.GDB_142) : null;
                    // 给出一个较高的 PlayOrder 基线以便在满足条件时触发
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_142, new Modifier(999));

                    int enemyAttack = 0;
                    int myAttack = 0;
                    if (board.MinionEnemy != null) enemyAttack = board.MinionEnemy.Sum(x => x.CurrentAtk);
                    if (board.WeaponEnemy != null) enemyAttack += board.WeaponEnemy.CurrentAtk;
                    if (board.MinionFriend != null) myAttack = board.MinionFriend.Sum(x => x.CurrentAtk);
                    if (board.WeaponFriend != null) myAttack += board.WeaponFriend.CurrentAtk;

                    // 更严格的判断：仅在场面无法挽救时才使用无界空宇（避免误清我方优势）
                    try
                    {
                        int friendCount = board.MinionFriend != null ? board.MinionFriend.Count : 0;
                        int enemyCount = board.MinionEnemy != null ? board.MinionEnemy.Count : 0;
                        int myHeroEffectiveHp = 0;
                        int enemyPotentialDamageNext = enemyAttack; // 近似：敌方当前攻击力
                        if (board.HeroFriend != null) myHeroEffectiveHp = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;

                        bool boardHopeless = false;

                        // 情况A：敌我攻击力差距极大（>=8），几乎无法回复
                        if ((enemyAttack - myAttack) >= 8) boardHopeless = true;

                        // 情况B：敌我差距中等（>=5），但我方几乎无随从且手牌很少，无法即刻反打
                        if (!boardHopeless && (enemyAttack - myAttack) >= 5 && friendCount <= 1 && (board.Hand == null || board.Hand.Count <= 1)) boardHopeless = true;

                        // 情况C：敌方下一轮伤害能直接威胁到英雄生命（我方可能被秒杀）
                        if (!boardHopeless && myHeroEffectiveHp > 0 && enemyPotentialDamageNext >= myHeroEffectiveHp) boardHopeless = true;

                        // 补充判断：敌方随从较多且我方随从很少，也视为难以挽回
                        if (!boardHopeless && enemyCount >= 3 && friendCount <= 1) boardHopeless = true;

                        if (boundlessSpace != null && boardHopeless)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_142, new Modifier(-1200));
                            p.ComboModifier = new ComboSet(boundlessSpace.Id);
                            AddLog("无界空宇：判定场面无法挽救 -> 强烈优先使用(清场)");
                        }
                        else
                        {
                            // 否则保持禁用/低优先级，避免误清场
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_142, new Modifier(1200));
                            AddLog("无界空宇：未判定为无法挽救 -> 禁用/保持低优先级");
                        }
                    }
                    catch { }
                }
                catch { }

                // 武器/攻击：一般不优先用刀去触发额外弃牌类（此套牌无特殊刀逻辑）
                p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.CORE_BAR_330, new Modifier(0));
                p.WeaponsAttackModifiers.AddOrUpdate(CenariusAxe, new Modifier(-50));

                // PlayOrder 微调：鼓励早期动作，避免浪费过牌窗口
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_928, new Modifier(1200)); // 飞翼滑翔 可用于对慢速换资源

                // 拆迁修理工（CORE_REV_023）：仅在对面有地标时优先使用，否则避免浪费
                try
                {
                    var locationCandidates = new List<Card.Cards>() { Card.Cards.VAC_929, Card.Cards.TLC_100t1, Card.Cards.VAC_409, Card.Cards.WON_103, Card.Cards.TLC_451 };
                    bool enemyHasLocation = false;
                    if (board.MinionEnemy != null)
                    {
                        foreach (var m in board.MinionEnemy)
                        {
                            if (m == null || m.Template == null) continue;
                            if (locationCandidates.Contains(m.Template.Id)) { enemyHasLocation = true; break; }
                        }
                    }

                    if (!enemyHasLocation)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Demolisher, new Modifier(900)); // 不要在无地标时下
                    }
                    else
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Demolisher, new Modifier(-300));
                        p.PlayOrderModifiers.AddOrUpdate(Demolisher, new Modifier(7200));
                    }
                }
                catch { }

                // ========= 惊险悬崖 VAC_929 逻辑补全 =========
                try
                {
                    int friendCount = board.MinionFriend != null ? board.MinionFriend.Count : 0;
                    int enemyAttack = 0;
                    int myAttack = 0;
                    if (board.MinionEnemy != null) enemyAttack = board.MinionEnemy.Sum(x => x.CurrentAtk);
                    if (board.WeaponEnemy != null) enemyAttack += board.WeaponEnemy.CurrentAtk;
                    if (board.MinionFriend != null) myAttack = board.MinionFriend.Sum(x => x.CurrentAtk);
                    if (board.WeaponFriend != null) myAttack += board.WeaponFriend.CurrentAtk;

                    var sheerCliffs = board.Hand != null ? board.Hand.FirstOrDefault(x => x.Template != null && x.Template.Id == Card.Cards.VAC_929) : null;

                    // 重要：不要让惊险悬崖的优先级覆盖“坟场连锁”核心逻辑（黏团->贪婪->残暴，以及对应退货政策）。
                    bool hasStickyInGrave = false;
                    bool hasHoundInGrave = false;
                    bool hasBatInGrave = false;
                    try
                    {
                        if (board.FriendGraveyard != null)
                        {
                            hasStickyInGrave = board.FriendGraveyard.Any(id => id == Card.Cards.TLC_468);
                            hasHoundInGrave = board.FriendGraveyard.Any(id => id == Card.Cards.EDR_891);
                            hasBatInGrave = board.FriendGraveyard.Any(id => id == Card.Cards.EDR_892);
                        }
                    }
                    catch { }

                    bool preferStickyNow = board.HasCardInHand(Card.Cards.TLC_468) && !hasStickyInGrave;
                    bool preferHoundNow = board.HasCardInHand(Card.Cards.EDR_891) && hasStickyInGrave;
                    bool preferBatNow = board.HasCardInHand(Card.Cards.EDR_892) && hasStickyInGrave && hasHoundInGrave;
                    bool returnPolicyBoostWindow = friendCount <= 5 && board.HasCardInHand(Card.Cards.MIS_102) && hasStickyInGrave && (hasHoundInGrave || hasBatInGrave);
                    bool protectGraveChainPriority = preferStickyNow || preferHoundNow || preferBatNow || returnPolicyBoostWindow;

                    // 基线顺序：中等（不抢核心连锁节奏）
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(1800));

                    if (board.HasCardInHand(Card.Cards.VAC_929) && sheerCliffs != null)
                    {
                        if (protectGraveChainPriority)
                        {
                            // 坟场连锁窗口：降低惊险悬崖优先级，避免覆盖括号内逻辑
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(400));
                            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(-2500));
                            AddLog("惊险悬崖：检测到坟场连锁窗口 -> 降低优先级（不覆盖黏团/贪婪/残暴/退货政策）");
                        }
                        else if (friendCount < 7 && (enemyAttack <= myAttack || enemyAttack <= 8))
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(-150));
                            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(3999));
                            AddLog("惊险悬崖：满足出场条件，优先下场");
                        }
                    }

                    // 如果场上已有惊险悬崖且己方随从数较少，则优先触发地点效果
                    if (board.HasCardOnBoard(Card.Cards.VAC_929) && friendCount <= 5)
                    {
                        p.LocationsModifiers.AddOrUpdate(Card.Cards.VAC_929, new Modifier(-99));
                        AddLog("惊险悬崖：场上存在，优先触发地点");
                    }
                }
                catch { }

                // ========= 补全剩余卡牌逻辑 =========
                try
                {
                    int handCount = board.Hand != null ? board.Hand.Count : 0;
                    int friendCount = board.MinionFriend != null ? board.MinionFriend.Count : 0;

                    // 飞翼滑翔 VAC_928：作为过牌/换资源工具，在手牌较多或法力允许时优先使用
                    if (board.HasCardInHand(Card.Cards.VAC_928))
                    {
                        if (handCount >= 4 || board.ManaAvailable >= 4)
                        {
                            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_928, new Modifier(-200));
                            AddLog("飞翼滑翔 VAC_928：条件满足，优先出牌");
                        }
                        else
                        {
                            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_928, new Modifier(150));
                        }
                    }

                    // 导航员伊莉斯 TLC_100：当手牌不太多时优先做地标/出牌
                    if (board.HasCardInHand(Card.Cards.TLC_100))
                    {
                        if (handCount <= 7)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_100, new Modifier(-150));
                            p.LocationsModifiers.AddOrUpdate(Card.Cards.TLC_100t1, new Modifier(-99));
                            AddLog("导航员伊莉斯 TLC_100：手牌较少，优先出场并提高地标优先级");
                        }
                        else
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_100, new Modifier(200));
                        }
                    }

                    // 黏团焦油 TLC_468：优先下场（可与 贪婪的地狱猎犬 配合）
                    if (board.HasCardInHand(Card.Cards.TLC_468))
                    {
                        // 基本偏好
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_468, new Modifier(-350));
                        AddLog("黏团焦油 TLC_468：优先下场");
                        try
                        {
                            // 若坟场中没有黏团焦油（尚未拥有亡语目标），则进一步提高下场优先级
                            bool hasStickyInGrave = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == Card.Cards.TLC_468);
                            if (!hasStickyInGrave)
                            {
                                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_468, new Modifier(-450));
                                AddLog("黏团焦油：坟场无黏团焦油 -> 提高使用优先级");
                            }
                        }
                        catch { }
                    }

                    // 调酒师鲍勃 BG31_BOB：中期价值牌，手牌较多或场上随从较少时优先
                    if (board.HasCardInHand(Card.Cards.BG31_BOB))
                    {
                        if (handCount >= 3 || friendCount < 3)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.BG31_BOB, new Modifier(-120));
                            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.BG31_BOB, new Modifier(3000));
                            AddLog("调酒师鲍勃 BG31_BOB：条件满足，优先出场");
                        }
                    }

                    // 红牌 TOY_644：禁止对己方随从使用；无敌方随从时尽量禁用；敌方有高攻击力随从时优先对其使用
                    if (board.HasCardInHand(RedCard))
                    {
                        // 敌方无随从时，避免浪费红牌/误点己方随从
                        if (board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                        {
                            p.CastSpellsModifiers.AddOrUpdate(RedCard, new Modifier(9999));
                            AddLog("红牌 TOY_644：敌方无随从，禁用 +9999");
                        }

                        // 禁止对己方场上随从使用红牌（绝对禁止：对目标添加极大惩罚）
                        try
                        {
                            if (board.MinionFriend != null)
                            {
                                foreach (var minion in board.MinionFriend)
                                {
                                    if (minion == null || minion.Template == null) continue;
                                    // 规则集语义：AddOrUpdate(法术, 惩罚值, 目标)
                                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_644, 999999, minion.Template.Id);
                                    AddLog($"红牌 TOY_644：绝对禁止对己方 {minion.Template.Name} 使用 +999999(目标惩罚)");
                                }
                            }
                        }
                        catch { }

                        // 敌方高攻击力随从：优先红牌（但不对传送门召唤体使用）
                        try
                        {
                            var portalEnemyNames = new HashSet<string> { "TIME_020t2t", "TIME_020t3t", "TIME_020t4t", "TIME_020t5t" };
                            int highAtkCount = 0;
                            if (board.MinionEnemy != null)
                            {
                                foreach (var em in board.MinionEnemy)
                                {
                                    if (em == null || em.Template == null) continue;
                                    if (portalEnemyNames.Contains(em.Template.Id.ToString())) continue;

                                    // “高攻击力”口径：>=6 视为强威胁；>=8 更强
                                    if (em.CurrentAtk >= 8)
                                    {
                                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_644, -1200, em.Template.Id);
                                        highAtkCount++;
                                    }
                                    else if (em.CurrentAtk >= 6)
                                    {
                                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_644, -800, em.Template.Id);
                                        highAtkCount++;
                                    }
                                }
                            }

                            if (highAtkCount > 0)
                            {
                                // 有高攻目标时，鼓励尽快用红牌
                                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_644, new Modifier(-200));
                                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_644, new Modifier(5000));
                                AddLog($"红牌 TOY_644：检测到高攻随从(>=6) 数量={highAtkCount} -> 优先对其使用");
                            }
                        }
                        catch { }

                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_644, new Modifier(999));
                    }

                    // 传送门变体（如 TIME_020t2t/TIME_020t3t/TIME_020t4t/TIME_020t5t）
                    // 若敌方场上存在这些传送门召唤出的随从：优先解掉，并禁止用红牌针对它们
                    try
                    {
                        var portalEnemyNames = new HashSet<string> { "TIME_020t2t", "TIME_020t3t", "TIME_020t4t", "TIME_020t5t" };
                        if (board.MinionEnemy != null)
                        {
                            foreach (var em in board.MinionEnemy)
                            {
                                if (em == null || em.Template == null) continue;
                                var idName = em.Template.Id.ToString();
                                if (portalEnemyNames.Contains(idName))
                                {
                                    // 优先解场：降低该敌方随从在评分中的值（更倾向于被清除）
                                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(em.Template.Id, new Modifier(-1000));
                                    // 优先作为武器/英雄攻击目标（加大权重，强制优先解场）
                                    p.WeaponsAttackModifiers.AddOrUpdate(em.Template.Id, new Modifier(-2000));
                                    // 优先被作为需要清除的敌方随从
                                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(em.Template.Id, new Modifier(-2000));
                                    // 禁止用红牌 TOY_644 针对这些随从
                                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_644, 9999, em.Template.Id);
                                    // 提高 PlayOrder 以便优先考虑清除行为
                                    p.PlayOrderModifiers.AddOrUpdate(em.Template.Id, new Modifier(16000));
                                    AddLog($"传送门变体敌方随从 {em.Template.Name}[{em.Template.Id}]：强烈优先解，禁止红牌针对");
                                }
                            }
                        }
                    }
                    catch { }

                    // 布洛克斯加：保持较低优先级，避免过早上场
                    p.CastMinionsModifiers.AddOrUpdate(Brox, new Modifier(600));
                    p.PlayOrderModifiers.AddOrUpdate(Brox, new Modifier(-5000));
                }
                catch { }

                // 退货政策（MIS_102）优先级：基于坟场与场上随从数量的分层判断
                // 规则：
                // 1) 若场上友方随从 <=5 且坟场同时含 EDR_891 + EDR_892 + TLC_468 -> 强烈优先发现（最高优先级）
                // 2) 若场上友方随从 <=5 且坟场含 EDR_892 + TLC_468 -> 提高优先级
                // 3) 否则若坟场含 EDR_892 -> 优先发现；否则若含 EDR_891 -> 次优先；否则维持默认较低优先级
                try
                {
                    int friendCount = board.MinionFriend != null ? board.MinionFriend.Count : 0;
                    bool hasBrutalBatInGrave = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == Card.Cards.EDR_892);
                    bool hasGreedyHoundInGrave = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == GreedyHound);
                    bool hasStickyTarInGrave = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == Card.Cards.TLC_468);

                    if (friendCount <= 5 && hasGreedyHoundInGrave && hasBrutalBatInGrave && hasStickyTarInGrave)
                    {
                        // 三件套齐全且场上随从较少：强烈优先发现，便于连贯复活/触发
                        p.CastSpellsModifiers.AddOrUpdate(ReturnPolicy, new Modifier(-1000));
                        p.PlayOrderModifiers.AddOrUpdate(ReturnPolicy, new Modifier(16000));
                        AddLog("退货政策：场上随从<=5 且坟场含 贪婪+残暴+黏团焦油 -> 强烈优先发现");
                    }
                    else if (friendCount <= 5 && hasStickyTarInGrave && hasGreedyHoundInGrave)
                    {
                        // 坟场有黏团焦油且有贪婪猎犬：优先用 贪婪猎犬（触发复活链），并提高退货政策优先级
                        p.CastMinionsModifiers.AddOrUpdate(GreedyHound, new Modifier(-400));
                        p.PlayOrderModifiers.AddOrUpdate(GreedyHound, new Modifier(8000));
                        p.CastSpellsModifiers.AddOrUpdate(ReturnPolicy, new Modifier(-900));
                        p.PlayOrderModifiers.AddOrUpdate(ReturnPolicy, new Modifier(14000));
                        AddLog("退货政策：场上随从<=5 且坟场含 黏团焦油+贪婪 -> 优先贪婪猎犬并提高退货政策优先级");
                    }
                    else if (friendCount <= 5 && hasBrutalBatInGrave && hasStickyTarInGrave)
                    {
                        // 有残暴+黏团焦油且场面较少：提高优先级
                        p.CastSpellsModifiers.AddOrUpdate(ReturnPolicy, new Modifier(-800));
                        p.PlayOrderModifiers.AddOrUpdate(ReturnPolicy, new Modifier(13000));
                        AddLog("退货政策：场上随从<=5 且坟场含 残暴+黏团焦油 -> 提高优先发现优先级");
                    }
                    else if (hasBrutalBatInGrave)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(ReturnPolicy, new Modifier(-700));
                        p.PlayOrderModifiers.AddOrUpdate(ReturnPolicy, new Modifier(12000));
                        AddLog("退货政策：坟场有残暴的魔蝠 -> 强烈优先发现");
                    }
                    else if (hasGreedyHoundInGrave)
                    {
                        p.CastSpellsModifiers.AddOrUpdate(ReturnPolicy, new Modifier(-300));
                        p.PlayOrderModifiers.AddOrUpdate(ReturnPolicy, new Modifier(6000));
                        AddLog("退货政策：坟场有贪婪的地狱猎犬 -> 提高发现优先级");
                    }
                    else
                    {
                        p.CastSpellsModifiers.AddOrUpdate(ReturnPolicy, new Modifier(400));
                    }
                }
                catch { }

                // ===== 应用通用敌方随从威胁表（来源：狂野颜射术 / ProfileThreatTables.DefaultEnemyMinionsThreatTable） =====
                try
                {
                    var enemyMinionIds = ProfileCommon.GetEnemyMinionCardIds(board);
                    var threatTable = ProfileThreatTables.DefaultEnemyMinionsThreatTable;
                    var threatMaxById = ProfileCommon.BuildMaxValueById(threatTable);

                    // 只对场上存在的随从应用表驱动的威胁值
                    ProfileCommon.ApplyThreatTableIfPresent(p, enemyMinionIds, threatTable);

                    // 若处于预计两回合斩杀线，允许在非嘲讽情况下走脸，同时对关键威胁随从做回拉
                    try
                    {
                        if (BoardHelper.HasPotentialLethalNextTurn(board) && (board.MinionEnemy == null || !board.MinionEnemy.Any(x => x.IsTaunt)))
                        {
                            const int CriticalThreatOverrideThreshold = 200;
                            ProfileCommon.ApplyGoFaceOverrideWithThreatPullback(p, board, threatMaxById, -500, CriticalThreatOverrideThreshold);
                            AddLog("应用威胁表：两回合斩杀线 -> 启用走脸回拉策略");
                        }
                        else
                        {
                            AddLog("应用威胁表：已应用 DefaultEnemyMinionsThreatTable（提高关键随从解场优先）");
                        }
                    }
                    catch { }
                }
                catch { }

                // 贪婪的地狱猎犬（EDR_891）：仅当坟场有 黏团焦油(TLC_468) 等目标时优先
                try
                {
                    bool hasStickyTar = false;
                    if (board.FriendGraveyard != null)
                    {
                        foreach (var id in board.FriendGraveyard)
                        {
                            var t = CardTemplate.LoadFromId(id);
                            if (t == null) continue;
                            if (t.Id == Card.Cards.TLC_468) { hasStickyTar = true; break; }
                        }
                    }
                    if (!hasStickyTar)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(GreedyHound, new Modifier(700));
                    }
                    else
                    {
                        p.CastMinionsModifiers.AddOrUpdate(GreedyHound, new Modifier(-120));
                    }
                }
                catch { }

                // 愤怒残魂（END_004）：费用越低越优先，但当牌库为空时降低优先级
                try
                {
                    // 默认降低愤怒残魂的优先级（PlayOrder 降低），优先使用其他牌
                    p.CastMinionsModifiers.AddOrUpdate(FuriousRemnant, new Modifier(400));
                    p.PlayOrderModifiers.AddOrUpdate(FuriousRemnant, new Modifier(2000));
                    AddLog("愤怒残魂 END_004：默认降低使用优先级（PlayOrder +2000），优先使用其他牌");

                    // 若牌库已空，降低愤怒残魂使用优先级（避免自残过度消耗资源）
                    if (board.FriendDeckCount == 0)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(FuriousRemnant, new Modifier(800));
                        AddLog("愤怒残魂 END_004：牌库为空 -> 降低使用优先级");
                    }

                    // 只有在手牌较少（<8）时才考虑下 愤怒残魂
                    if (board.Hand != null && board.Hand.Count < 8 && board.Hand.Exists(c => c.Template != null && c.Template.Id == FuriousRemnant))
                    {
                        int minCost = int.MaxValue;
                        foreach (var c in board.Hand)
                        {
                            if (c == null || c.Template == null) continue;
                            if (c.Template.Id == FuriousRemnant)
                            {
                                minCost = Math.Min(minCost, c.CurrentCost);
                            }
                        }
                        if (minCost != int.MaxValue)
                        {
                            // cost lower => bigger negative modifier (更愿意下)
                            int mod = -100 * Math.Max(0, 8 - minCost);
                            // 只有在费用明显降低时才覆盖默认的降低优先级
                            if (mod < 0)
                            {
                                p.CastMinionsModifiers.AddOrUpdate(FuriousRemnant, new Modifier(mod));
                                // 同步降低 PlayOrder，使其可以更早被考虑出场
                                p.PlayOrderModifiers.AddOrUpdate(FuriousRemnant, new Modifier(-800));
                                AddLog($"愤怒残魂 END_004：检测到较低费用 {minCost} -> 设置 Cast modifier {mod}，PlayOrder -800");
                            }
                        }
                    }
                }
                catch { }

                // 残暴的魔蝠（EDR_892）：坟场没有 贪婪的地狱猎犬（EDR_891）则不用
                try
                {
                    if (board.HasCardInHand(BrutalBat))
                    {
                        bool hasGreedyHoundInGraveyard = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == GreedyHound);
                        bool hasStickyInGrave = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == Card.Cards.TLC_468);

                        // 若坟场同时有黏团焦油 + 贪婪猎犬，则优先使用残暴的魔蝠以完成复活链
                        if (hasStickyInGrave && hasGreedyHoundInGraveyard)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(BrutalBat, new Modifier(-450));
                            p.PlayOrderModifiers.AddOrUpdate(BrutalBat, new Modifier(6000));
                            AddLog("残暴的魔蝠：坟场含 黏团焦油+贪婪猎犬 -> 提高使用优先级");
                        }
                        else if (!hasGreedyHoundInGraveyard)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(BrutalBat, new Modifier(1500));
                            AddLog("残暴的魔蝠：坟场无贪婪猎犬 -> 禁止使用");
                        }
                    }
                }
                catch { }
            }
            catch (Exception)
            {
                // ignore heuristics exceptions
            }

            // ===== 全局硬规则：不解对面的“凯洛斯的蛋”系列（DINO_410*） =====
            // 口径：对面出现蛋阶段时，尽量不要用随从/武器/解牌去处理它（优先处理其他滚雪球点/直伤点）。
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

            return p;
        }

        // 直接输出决策快照（不做累积），便于复盘：手牌数、手牌id、己方/敌方随从状态、英雄状态
        private void DumpSnapshot(Board board)
        {
            try
            {
                if (board == null)
                {
                    try { Bot.Log($"[{ProfileVersion}] DumpSnapshot: board==null"); } catch { }
                    return;
                }

                int handCount = board.Hand != null ? board.Hand.Count : 0;
                string handIds = "";
                try
                {
                    var parts = new List<string>();
                    if (board.Hand != null)
                    {
                        foreach (var c in board.Hand)
                        {
                            if (c == null || c.Template == null) continue;
                            var name = string.IsNullOrWhiteSpace(c.Template.NameCN) ? c.Template.Name : c.Template.NameCN;
                            parts.Add($"{name}[{c.Template.Id}]({c.CurrentCost})");
                        }
                    }
                    handIds = string.Join(", ", parts);
                }
                catch { handIds = "(error reading hand)"; }

                // 己方随从
                var friendly = new List<string>();
                try
                {
                    if (board.MinionFriend != null)
                    {
                        foreach (var m in board.MinionFriend)
                        {
                            if (m == null || m.Template == null) continue;
                            var fname = string.IsNullOrWhiteSpace(m.Template.NameCN) ? m.Template.Name : m.Template.NameCN;
                            friendly.Add($"{fname}[{m.Template.Id}]:{m.CurrentAtk}/{m.CurrentHealth}#{(m.IsDivineShield?"DS":"")}{(m.CanAttack?"A":"")}");
                        }
                    }
                }
                catch { friendly.Add("(error reading friendly minions)"); }

                // 敌方随从
                var enemy = new List<string>();
                try
                {
                    if (board.MinionEnemy != null)
                    {
                        foreach (var m in board.MinionEnemy)
                        {
                            if (m == null || m.Template == null) continue;
                            var ename = string.IsNullOrWhiteSpace(m.Template.NameCN) ? m.Template.Name : m.Template.NameCN;
                            enemy.Add($"{ename}[{m.Template.Id}]:{m.CurrentAtk}/{m.CurrentHealth}#{(m.IsTaunt?"T":"")}{(m.IsDivineShield?"DS":"")}");
                        }
                    }
                }
                catch { enemy.Add("(error reading enemy minions)"); }

                // 英雄状态
                string enemyHero = "";
                string friendlyHero = "";
                try
                {
                    if (board.HeroEnemy != null)
                    {
                        enemyHero = $"EnemyHero HP={board.HeroEnemy.CurrentHealth} Armor={board.HeroEnemy.CurrentArmor}";
                    }
                }
                catch { enemyHero = "EnemyHero:(unreadable)"; }
                try
                {
                    if (board.HeroFriend != null)
                    {
                        friendlyHero = $"FriendlyHero HP={board.HeroFriend.CurrentHealth} Armor={board.HeroFriend.CurrentArmor}";
                    }
                }
                catch { friendlyHero = "FriendlyHero:(unreadable)"; }

                // 输出几行便于复盘
                try { Bot.Log($"[{ProfileVersion}] Snapshot MaxMana={board.MaxMana} Mana={board.ManaAvailable}/{board.MaxMana} HandCount={handCount}"); } catch { }
                try { Bot.Log($"[{ProfileVersion}] Hand: {handIds}"); } catch { }
                try { Bot.Log($"[{ProfileVersion}] FriendlyMinions: {string.Join(" | ", friendly)}"); } catch { }
                try { Bot.Log($"[{ProfileVersion}] EnemyMinions: {string.Join(" | ", enemy)}"); } catch { }
                try { Bot.Log($"[{ProfileVersion}] {enemyHero} | {friendlyHero}"); } catch { }
            }
            catch
            {
                try { Bot.Log($"[{ProfileVersion}] DumpSnapshot: exception"); } catch { }
            }
        }

        private static bool IsControlOpponent(Board board)
        {
            try
            {
                switch (board.EnemyClass)
                {
                    case Card.CClass.PRIEST:
                    case Card.CClass.WARRIOR:
                    case Card.CClass.WARLOCK:
                    case Card.CClass.DEATHKNIGHT:
                    case Card.CClass.MAGE:
                    case Card.CClass.DRUID:
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            return choices[0];
        }

        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices[0];
        }

        #region SmartBot 单文件编译兼容（内置公共工具/威胁表）
        // 说明：部分 SmartBot 环境会“按单文件编译每个 Profile”，导致同目录下的工具类文件不可见。
        // 因此将公共工具/数据表内置到 Profile 内部，避免出现“does not exist in current context”。

        internal static class ProfileCommon
        {
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

            public static HashSet<Card.Cards> GetEnemyMinionCardIds(Board board)
            {
                var ids = new HashSet<Card.Cards>();
                if (board == null || board.MinionEnemy == null) return ids;

                foreach (var m in board.MinionEnemy)
                {
                    if (m == null || m.Template == null) continue;
                    ids.Add(m.Template.Id);
                }
                return ids;
            }

            public static Dictionary<Card.Cards, int> BuildMaxValueById(IEnumerable<KeyValuePair<Card.Cards, int>> table)
            {
                var maxById = new Dictionary<Card.Cards, int>();
                if (table == null) return maxById;

                foreach (var kv in table)
                {
                    int cur;
                    if (maxById.TryGetValue(kv.Key, out cur))
                        maxById[kv.Key] = Math.Max(cur, kv.Value);
                    else
                        maxById[kv.Key] = kv.Value;
                }
                return maxById;
            }

            public static void ApplyThreatTableIfPresent(ProfileParameters p, HashSet<Card.Cards> presentEnemyIds, IEnumerable<KeyValuePair<Card.Cards, int>> table)
            {
                if (p == null || presentEnemyIds == null || presentEnemyIds.Count == 0 || table == null) return;

                foreach (var kv in table)
                {
                    if (!presentEnemyIds.Contains(kv.Key))
                        continue;
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(kv.Key, new Modifier(kv.Value));
                }
            }

            public static void ApplyGoFaceOverrideWithThreatPullback(ProfileParameters p, Board board, Dictionary<Card.Cards, int> threatMaxById, int goFaceOverrideValue, int criticalThreatOverrideThreshold)
            {
                if (p == null || board == null || board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                    return;

                foreach (var em in board.MinionEnemy)
                {
                    if (em == null || em.Template == null) continue;
                    if (em.IsTaunt) continue;
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(em.Template.Id, new Modifier(goFaceOverrideValue));
                }

                if (threatMaxById == null || threatMaxById.Count == 0)
                    return;

                foreach (var em in board.MinionEnemy)
                {
                    if (em == null || em.Template == null) continue;
                    if (em.IsTaunt) continue;

                    int t;
                    if (threatMaxById.TryGetValue(em.Template.Id, out t) && t >= criticalThreatOverrideThreshold)
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(em.Template.Id, new Modifier(t));
                }
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
        }

        internal static class ProfileThreatTables
        {
            // 说明：复制自「狂野颜射术」的威胁表基线（只保留关键条目即可满足“优先解关键随从”的诉求）。
            public static readonly List<KeyValuePair<Card.Cards, int>> DefaultEnemyMinionsThreatTable = new List<KeyValuePair<Card.Cards, int>>
            {
                new KeyValuePair<Card.Cards, int>(Card.Cards.TOY_381, 200), // 纸艺天使：必须优先解
                new KeyValuePair<Card.Cards, int>(Card.Cards.TOY_505, 200), // 海盗船
                new KeyValuePair<Card.Cards, int>(Card.Cards.DEEP_008, 200), // 针岩图腾：必须优先解
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
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_924, 200), // 锋鳞：优先解
                new KeyValuePair<Card.Cards, int>(Card.Cards.DMF_120, 200), // 纳兹曼尼织血者
                new KeyValuePair<Card.Cards, int>(Card.Cards.TTN_903, 200), // 生命的缚誓者艾欧娜尔
            };
        }

        internal static class BoardHelper
        {
            // 单文件编译兜底：只实现本 Profile 需要的最小方法。
            // 口径：保守估算“下回合可能斩杀”，用于触发走脸回拉策略。
            public static bool HasPotentialLethalNextTurn(Board board)
            {
                try
                {
                    if (board == null || board.HeroEnemy == null) return false;

                    int enemyEffectiveHp = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                    if (enemyEffectiveHp <= 0) return true;

                    int potentialDamage = 0;

                    // 场面伤害（粗略：按当前攻击力合计，作为“下回合可能打脸”的上限）
                    if (board.MinionFriend != null)
                    {
                        foreach (var m in board.MinionFriend)
                        {
                            if (m == null || m.Template == null) continue;
                            if (m.IsFrozen) continue;
                            if (m.CurrentAtk > 0)
                                potentialDamage += m.CurrentAtk;
                        }
                    }

                    // 武器/英雄攻击
                    try
                    {
                        if (board.WeaponFriend != null)
                            potentialDamage += board.WeaponFriend.CurrentAtk;
                    }
                    catch { }

                    // 手牌直伤（本套牌非常有限，保守只计入 END_007 的 1 伤）
                    try
                    {
                        if (board.Hand != null)
                        {
                            foreach (var c in board.Hand)
                            {
                                if (c == null || c.Template == null) continue;
                                if (c.Template.Id == Card.Cards.END_007)
                                    potentialDamage += 1;
                            }
                        }
                    }
                    catch { }

                    return potentialDamage >= enemyEffectiveHp;
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
