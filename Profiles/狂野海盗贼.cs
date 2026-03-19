using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API;
using SmartBotAPI.Battlegrounds;
using SmartBot.Plugins.API.Actions;

namespace SmartBotProfiles
{
    // 狂野：海盗贼（参考卡组：参考卡组(用记事本可以打开)/狂野模式/狂野剑鱼（海盗）贼.txt）
    // 策略说明：
    // 1. 快速铺场海盗（触发帕奇斯自动入场）
    // 2. 英雄技能出武器配合南海船工（+1攻）、恐怖海盗（降费）
    // 3. 空降歹徒配合1费海盗降费
    // 4. 船载火炮持续输出
    // 5. 冷血配合武器或强力随从打脸
    // 6. 后期用剑鱼连击斩杀
    [Serializable]
    public class WildPirateRogue : Profile
    {
        // ==================== 版本和配置 ====================
        private const string ProfileVersion = "2026-01-05.3";
        private static readonly bool VerboseLog = false;
        private string _log = "";

        // 本回合“每张牌的思考”（用于输出到日志/文档）
        private readonly Dictionary<Card.Cards, string> _cardThoughtThisTurn = new Dictionary<Card.Cards, string>();
        private readonly Dictionary<Card.Cards, int> _cardModThisTurn = new Dictionary<Card.Cards, int>();

        // ==================== 卡牌ID ====================
        #region 卡牌ID定义
        // 1费海盗
        private const Card.Cards 冷血 = Card.Cards.CS2_073;                // 法术：给一个随从+4攻击力
        private const Card.Cards 南海船工 = Card.Cards.CORE_CS2_146;      // 1/2 战吼：如果你有武器，+1攻击力
        private const Card.Cards 奖品掠夺者 = Card.Cards.DMF_519;         // 2/1 连击：抽一张牌
        private const Card.Cards 宝藏经销商 = Card.Cards.TOY_518;         // 2/1 亡语：发现一件宝藏
        private const Card.Cards 换挡漂移 = Card.Cards.TTN_922;           // 法术：抽一张海盗牌，使其法力值消耗减少（2）点
        private const Card.Cards 旗标骷髅 = Card.Cards.NX2_006;           // 1/3 你的其他海盗牌获得+1攻击力
        private const Card.Cards 海盗帕奇斯 = Card.Cards.CFM_637;         // 1/1 当你召唤一个海盗时，从你的牌库中召唤此随从
        private const Card.Cards 鱼排斗士 = Card.Cards.TSC_963;           // 2/2 连击：获得+2攻击力

        // 2费
        private const Card.Cards 洞穴探宝者 = Card.Cards.LOOT_033;        // 2/2 连击：发现一张法术牌
        private const Card.Cards 玩具船 = Card.Cards.TOY_505;             // 3/2 战吼：你的海盗牌获得+1/+1
        private const Card.Cards 空降歹徒 = Card.Cards.DRG_056;           // 4/3 在你使用一张海盗牌后，此牌的法力值消耗减少（2）点
        private const Card.Cards 船载火炮 = Card.Cards.GVG_075;           // 0/3 在你召唤一个海盗后，随机对一个敌人造成2点伤害

        // 3费
        private const Card.Cards 剑鱼 = Card.Cards.TSC_086;                // 法术：造成4点伤害，连击：再造成4点伤害
        private const Card.Cards 南海船长 = Card.Cards.NEW1_027;          // 3/3 你的其他海盗牌获得+1/+1
        private const Card.Cards 粗暴的猢狲 = Card.Cards.VAC_938;          // 3/4 突袭，亡语：将一张双倍伤害的"粗暴的猢狲"洗入你的牌库

        // 4费
        private const Card.Cards 恐怖海盗 = Card.Cards.CORE_NEW1_022;     // 5/6 如果你装备武器，此牌的法力值消耗减少（3）点

        // 英雄技能
        private const Card.Cards 匕首精通 = Card.Cards.HERO_03bp;         // 装备一把1/2的匕首
        private const Card.Cards 幸运币 = Card.Cards.GAME_005;
        #endregion

        #region 英雄能力优先级（芬利爵士用）
        private readonly Dictionary<Card.Cards, int> _heroPowersPriorityTable = new Dictionary<Card.Cards, int>
        {
            {匕首精通, 7},
        };
        #endregion

        // ==================== 辅助函数 ====================
        #region 辅助函数
        private static string CardName(Card.Cards id)
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

        private void RememberThought(Card.Cards id, int mod, string reason)
        {
            if (id == 幸运币)
                return;

            _cardModThisTurn[id] = mod;
            _cardThoughtThisTurn[id] = reason ?? "";
        }

        private void SetMinionMod(ProfileParameters p, Card.Cards id, int mod, string reason)
        {
            p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(mod));
            RememberThought(id, mod, reason);
        }

        private void SetSpellMod(ProfileParameters p, Card.Cards id, int mod, string reason)
        {
            p.CastSpellsModifiers.AddOrUpdate(id, new Modifier(mod));
            RememberThought(id, mod, reason);
        }

        private void FlushThoughtPerCard(Board board)
        {
            try
            {
                if (board == null || board.Hand == null) return;

                var ids = board.Hand
                    .Where(c => c != null && c.Template != null)
                    .Select(c => c.Template.Id)
                    .Where(id => id != 幸运币)
                    .Distinct()
                    .ToList();

                foreach (var id in ids)
                {
                    int mod;
                    string reason;
                    if (_cardModThisTurn.TryGetValue(id, out mod) && _cardThoughtThisTurn.TryGetValue(id, out reason))
                    {
                        AddLog(CardName(id) + " " + mod + "（" + reason + "）");
                        continue;
                    }

                    // 要求：每张牌都要有“思考”（硬币除外）
                    AddLog(CardName(id) + " 0（默认：未设置特殊权重）");
                }
            }
            catch
            {
                // ignore
            }
        }

        #region 敌方随从威胁值（优先解）
        private static readonly Dictionary<Card.Cards, int> _EnemyMinionsThreatModifiers = new Dictionary<Card.Cards, int>
        {
            { Card.Cards.WW_827, 200},//雏龙牧人 WW_827
            { Card.Cards.TTN_903, 200},//生命的缚誓者艾欧娜尔 TTN_903
            { Card.Cards.TTN_960, 200},//灭世泰坦萨格拉斯 TTN_960
            { Card.Cards.TTN_862, 200},//翠绿之星阿古斯 TTN_862
            { Card.Cards.TTN_429, 200},//阿曼苏尔 TTN_429
            { Card.Cards.TTN_092, 200},//复仇者阿格拉玛 TTN_092
            { Card.Cards.TTN_075, 200},//诺甘农 TTN_075
            { Card.Cards.SW_115, 200},//Bolner Hammerbeak
            { Card.Cards.DEEP_008, 200},//针岩图腾 DEEP_008
            { Card.Cards.CORE_RLK_121, 200},//死亡侍僧 CORE_RLK_121
            { Card.Cards.TTN_737, 200},//兵主 TTN_737
            { Card.Cards.VAC_406, 900},//困倦的岛民 VAC_406
            { Card.Cards.TTN_858, 200},//维和者阿米图斯 TTN_858
            { Card.Cards.GDB_226, 200},//凶恶的入侵者 GDB_226
            { Card.Cards.TOY_330t5, 200},//奇利亚斯豪华版3000型 TOY_330t5
            { Card.Cards.TOY_330t11, 200},//奇利亚斯豪华版3000型 TOY_330t11
            { Card.Cards.CORE_EX1_012, 200},//血法师萨尔诺斯 CORE_EX1_012
            { Card.Cards.CORE_BT_187, 200},//凯恩·日怒 CORE_BT_187
            { Card.Cards.CS2_052, 200},//空气之怒图腾 CS2_052
            { Card.Cards.WORK_040, 200 },//笨拙的杂役 WORK_040
            { Card.Cards.TOY_606, 200 },//测试假人 TOY_606
            { Card.Cards.WW_382, 200 },//步移山丘 WW_382
            { Card.Cards.GDB_841, 200 },//游侠斥候 GDB_841
            { Card.Cards.GDB_110, 200 },//邪能动力源 GDB_110
            { Card.Cards.CORE_ICC_210, 200 },//暗影升腾者 CORE_ICC_210
            { Card.Cards.JAM_024, 200 },//布景光耀之子 JAM_024
            { Card.Cards.CORE_CS3_014, 200 },//赤红教士 CORE_CS3_014

            { Card.Cards.GVG_075, 200 },//船载火炮 GVG_075
            { Card.Cards.GDB_310, 200 },//虚灵神谕者 GDB_310
            { Card.Cards.JAM_010, 200 },//点唱机图腾 JAM_010
            { Card.Cards.TOY_351t, -200 },//神秘的蛋 TOY_351t
            { Card.Cards.TOY_351, -200 },//神秘的蛋 TOY_351
            { Card.Cards.WW_391, 200 }, // 淘金客 WW_391
            { Card.Cards.TOY_515, 200 }, // 水上舞者索尼娅 TOY_515
            { Card.Cards.CORE_TOY_100, 200 }, // 侏儒飞行员诺莉亚 CORE_TOY_100
            { Card.Cards.WW_381, 200 }, // 受伤的搬运工 WW_381
            { Card.Cards.TTN_900, -200 }, // 石心之王 TTN_900
            { Card.Cards.CORE_DAL_609, 200 }, // 卡雷苟斯 CORE_DAL_609
            { Card.Cards.TOY_646, 200 }, // 捣蛋林精 TOY_646
            { Card.Cards.TOY_357, 200 }, // 抱龙王噗鲁什 TOY_357
            { Card.Cards.VAC_507, 200 }, // 阳光汲取者莱妮莎 VAC_507
            { Card.Cards.WORK_042, 500 }, // 食肉格块 WORK_042
            { Card.Cards.WW_344, 200 }, // 威猛银翼巨龙 WW_344
            { Card.Cards.TOY_812, -5},//皮普希·彩蹄 TOY_812
            { Card.Cards.VAC_532, 200 },//椰子火炮手 VAC_532
            { Card.Cards.TOY_505, 200 },//玩具船 TOY_505
            { Card.Cards.TOY_381, 300 },//纸艺天使 TOY_381
            { Card.Cards.TOY_824, 350 }, // 黑棘针线师 TOY_824
            { Card.Cards.VAC_927, 200 }, // 狂飙邪魔 VAC_927
            { Card.Cards.VAC_938, 200 }, // 粗暴的猢狲 VAC_938
            { Card.Cards.ETC_355, 200 }, // 剃刀沼泽摇滚明星 ETC_355
            { Card.Cards.WW_091, 200 },  // 腐臭淤泥波普加 WW_091
            { Card.Cards.VAC_450, 200}, // 悠闲的曲奇 VAC_450
            { Card.Cards.TOY_028, 200 }, // 团队之灵 TOY_028
            { Card.Cards.VAC_436, 200 }, // 脆骨海盗 VAC_436
            { Card.Cards.VAC_321, 200 }, // 伊辛迪奥斯 VAC_321
            { Card.Cards.TTN_800, 200 }, // 雷霆之神高戈奈斯 TTN_800
            { Card.Cards.TTN_415, 200 }, // 卡兹格罗斯 TTN_415
            { Card.Cards.ETC_541, 200 }, // 盗版之王托尼 ETC_541
            { Card.Cards.CORE_LOOT_231, 200 }, // 奥术工匠 CORE_LOOT_231
            { Card.Cards.ETC_339, 200 }, // 心动歌手 ETC_339
            { Card.Cards.ETC_833, 200 }, // 箭矢工匠 ETC_833
            { Card.Cards.MIS_026, 200 }, // 傀儡大师多里安 MIS_026
            { Card.Cards.CORE_WON_065, 200 }, // 随船外科医师 CORE_WON_065
            { Card.Cards.WW_357, 500 }, // 老腐和老墓 WW_357
            { Card.Cards.DEEP_999t2, 200 }, // 深岩之洲晶簇 DEEP_999t2
            { Card.Cards.CFM_039, 200 }, // 杂耍小鬼 CFM_039
            { Card.Cards.WW_364t, 200 }, // 狡诈巨龙威拉罗克 WW_364t
            { Card.Cards.TSC_026t, 200 }, // 可拉克的壳 TSC_026t
            { Card.Cards.WW_415, 200 }, // 许愿井 WW_415
            { Card.Cards.CS3_014, 200 }, // 赤红教士 CS3_014
            { Card.Cards.YOG_516, 200 }, // 脱困古神尤格-萨隆 YOG_516
            { Card.Cards.NX2_033, 200 }, // 巨怪塔迪乌斯 NX2_033
            { Card.Cards.JAM_004, 200 }, // 镂骨恶犬 JAM_004
            { Card.Cards.TTN_330, 200 }, // Kologarn TTN_330
            { Card.Cards.TTN_729, 200 }, // Melted Maker TTN_729
            { Card.Cards.TTN_812, 150 }, // Victorious Vrykul TTN_812
            { Card.Cards.TTN_479, 200 }, // Flame Revenant TTN_479
            { Card.Cards.TTN_732, 250 }, // Invent-o-matic TTN_732
            { Card.Cards.TTN_466, 250 }, // Minotauren TTN_466
            { Card.Cards.TTN_801, 350 }, // Champion of Storms TTN_801
            { Card.Cards.TTN_833, 200 }, // Disciple of Golganneth TTN_833
            { Card.Cards.TTN_730, 300 }, // Lab Constructor TTN_730
            { Card.Cards.TTN_920, 300 }, // Mimiron, the Mastermind TTN_920
            { Card.Cards.TTN_856, 200 }, // Disciple of Amitus TTN_856
            { Card.Cards.TTN_907, 200 }, // Astral Serpent TTN_907
            { Card.Cards.TTN_071, 200 }, // Sif TTN_071
            { Card.Cards.TTN_078, 200 }, // Observer of Myths TTN_078
            { Card.Cards.TTN_843, 200 }, // Eredar Deceptor TTN_843
        };
        #endregion

        private void AddLog(string msg)
        {
            if (_log.Length > 0)
                _log += "\r\n";
            _log += msg;
        }

        private static string GetDeckDocPath()
        {
            try
            {
                // SmartBot 通常以 smartbot(压缩包解压到此) 为 BaseDirectory
                // 这里回到上一级 /Users/.../Desktop/smartbot
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var workspaceRoot = Path.GetFullPath(Path.Combine(baseDir, ".."));
                return Path.Combine(workspaceRoot, "参考卡组(用记事本可以打开)", "狂野模式", "狂野海盗贼.md");
            }
            catch
            {
                return null;
            }
        }

        private void AppendLogToDeckDoc(string log)
        {
            try
            {
                var path = GetDeckDocPath();
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var payload = "\n\n---\n\n### 对局日志 " + stamp + " | v" + ProfileVersion + "\n\n```\n" + log + "\n```\n";
                File.AppendAllText(path, payload, Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        private void PrintLog()
        {
            if (!string.IsNullOrEmpty(_log))
            {
                if (VerboseLog)
                    Bot.Log(_log);

                // 你要求：把每一次日志写进参考卡组文档
                AppendLogToDeckDoc(_log);
                _log = "";
            }
        }
        #endregion

        // ==================== 主要策略 ====================
        public ProfileParameters GetParameters(Board board)
        {
            _log = "";
            _cardThoughtThisTurn.Clear();
            _cardModThisTurn.Clear();
            AddLog("=".PadRight(80, '='));
            AddLog("狂野海盗贼 v" + ProfileVersion);
            AddLog("法力 " + board.ManaAvailable + "/" + board.MaxMana + " | 手牌 " + board.Hand.Count + " | 牌库 " + board.FriendDeckCount);
            AddLog("我方：" + board.HeroFriend.CurrentHealth + "血+" + board.HeroFriend.CurrentArmor + "甲 = " + (board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor));
            AddLog("敌方：" + board.HeroEnemy.CurrentHealth + "血+" + board.HeroEnemy.CurrentArmor + "甲 = " + (board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor));
            AddLog("=".PadRight(80, '='));

            // 基础参数：使用快攻模式
            var p = new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = 10 };

            if (ProfileCommon.TryRunPureLearningPlayExecutor(board, p))
                return p;

            // 获取本局实际英雄技能ID（关键：CastHeroPowerModifier 必须对这个ID才生效）
            Card.Cards friendAbility = 匕首精通;
            try
            {
                if (board.Ability != null && board.Ability.Template != null)
                    friendAbility = board.Ability.Template.Id;
            }
            catch
            {
                friendAbility = 匕首精通;
            }

            // 激进设置：海盗贼全力打脸
            p.GlobalAggroModifier = 150;
            p.GlobalDefenseModifier = -100;
            AddLog("策略：激进打脸（攻击+150%，防御-100%）");

            // ==================== 武器策略 ====================
            // 优先出匕首（配合南海船工、恐怖海盗）
            if (board.WeaponFriend == null && board.ManaAvailable >= 2)
            {
                p.CastHeroPowerModifier.AddOrUpdate(friendAbility, new Modifier(-80));
                AddLog("[武器] 无武器时优先使用英雄技能");
            }

            // ==================== 随从优先级 ====================
            // 1. 光环随从优先下（南海船长、旗标骷髅）
            if (board.Hand.Any(x => x.Template.Id == 南海船长))
            {
                SetMinionMod(p, 南海船长, -100, "光环：海盗+1/+1，优先铺场");
            }
            if (board.Hand.Any(x => x.Template.Id == 旗标骷髅))
            {
                SetMinionMod(p, 旗标骷髅, -90, "光环：海盗+1攻，提升打脸与交换效率");
            }
            if (board.Hand.Any(x => x.Template.Id == 玩具船))
            {
                SetMinionMod(p, 玩具船, -85, "持续价值：补牌/站场，优先展开" );
            }

            // 2. 空降歹徒：降费后优先打出
            var parachuteCard = board.Hand.FirstOrDefault(x => x.Template.Id == 空降歹徒);
            if (parachuteCard != null && parachuteCard.CurrentCost <= 2)
            {
                SetMinionMod(p, 空降歹徒, -95, "降费至" + parachuteCard.CurrentCost + "费：优先打出抢节奏");
            }

            // 3. 恐怖海盗：有武器时降费
            var dreadCard = board.Hand.FirstOrDefault(x => x.Template.Id == 恐怖海盗);
            if (dreadCard != null && board.WeaponFriend != null && dreadCard.CurrentCost <= 1)
            {
                SetMinionMod(p, 恐怖海盗, -100, "有武器降费至" + dreadCard.CurrentCost + "费：立即展开" );
            }

            // 4. 船载火炮：核心输出，优先站场
            if (board.Hand.Any(x => x.Template.Id == 船载火炮))
            {
                SetMinionMod(p, 船载火炮, -80, "核心输出：每海盗触发2点伤害" );
            }

            // 5. 南海船工：有武器时价值高
            if (board.Hand.Any(x => x.Template.Id == 南海船工) && board.WeaponFriend != null)
            {
                SetMinionMod(p, 南海船工, -70, "有武器时+1攻：优先抢脸" );
            }

            // ==================== 法术优先级 ====================
            // 1. 剑鱼：斩杀利器
            if (board.Hand.Any(x => x.Template.Id == 剑鱼))
            {
                int baseDamage = 4;
                int comboDamage = 8;
                bool hasCombo = board.PlayedCards != null && board.PlayedCards.Count > 0;
                int realDamage = hasCombo ? comboDamage : baseDamage;
                int enemyHP = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;

                if (realDamage >= enemyHP)
                {
                    SetSpellMod(p, 剑鱼, -200, "斩杀：伤害" + realDamage + ">=敌方" + enemyHP);
                }
                else if (hasCombo)
                {
                    SetSpellMod(p, 剑鱼, -60, "连击输出：" + comboDamage + "点");
                }
                else
                {
                    SetSpellMod(p, 剑鱼, -40, "基础输出：" + baseDamage + "点");
                }
            }

            // 2. 冷血：配合武器或强力随从
            if (board.Hand.Any(x => x.Template.Id == 冷血))
            {
                if (board.WeaponFriend != null)
                {
                    SetSpellMod(p, 冷血, -90, "配合武器：爆发打脸" );
                }
                else if (board.MinionFriend.Any(m => m.CurrentAtk >= 3))
                {
                    SetSpellMod(p, 冷血, -70, "配合场上强随从：提高斩杀压力" );
                }
                else if (board.MinionFriend.Any())
                {
                    SetSpellMod(p, 冷血, -50, "有随从即可用：提速打脸" );
                }
            }

            // 3. 换挡漂移：早期抽海盗，后期减费
            if (board.Hand.Any(x => x.Template.Id == 换挡漂移))
            {
                if (board.ManaAvailable >= 4)
                {
                    SetSpellMod(p, 换挡漂移, -65, "过牌+减费：中期补资源" );
                }
            }

            // 没单独设置时，也要给“思考”（硬币除外）
            if (board.Hand.Any(c => c.Template != null && c.Template.Id == 南海船工) && board.WeaponFriend == null)
                RememberThought(南海船工, 0, "无武器：等待匕首或其他节奏点" );
            if (board.Hand.Any(c => c.Template != null && c.Template.Id == 恐怖海盗) && board.WeaponFriend == null)
                RememberThought(恐怖海盗, 0, "无武器：暂不降费，等待匕首后再展开" );

            // ==================== 敌方随从威胁值（补齐） ====================
            try
            {
                foreach (var kv in _EnemyMinionsThreatModifiers)
                {
                    if (board.MinionEnemy.Any(m => m.Template != null && m.Template.Id == kv.Key))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(kv.Key, new Modifier(kv.Value));
                }
            }
            catch
            {
                // ignore
            }

            // ==================== 随从保护策略 ====================
            // 重要随从不轻易送掉
            var importantMinions = new Dictionary<Card.Cards, int>
            {
                {旗标骷髅, 100},      // 光环不送
                {南海船长, 100},      // 光环不送
                {玩具船, 80},         // Buff随从保护
                {船载火炮, 150},      // 输出核心绝不送
                {宝藏经销商, 80},     // 亡语价值
                {粗暴的猢狲, 70}      // 亡语价值
            };

            foreach (var m in board.MinionFriend.Where(x => x.Template != null))
            {
                if (importantMinions.ContainsKey(m.Template.Id))
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(m.Template.Id, new Modifier(importantMinions[m.Template.Id]));
                    if (VerboseLog)
                    {
                        AddLog("[保护] " + CardName(m.Template.Id) + " 价值+" + importantMinions[m.Template.Id] + "%");
                    }
                }
            }

            // ==================== 对手场面威胁评估 ====================
            // 优先解场面：嘲讽、高攻、威胁随从
            foreach (var m in board.MinionEnemy.Where(x => x.Template != null))
            {
                bool hasTaunt = m.IsTaunt;
                bool highAttack = m.CurrentAtk >= 4;

                if (hasTaunt)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(m.Template.Id, new Modifier(150));
                    if (VerboseLog)
                    {
                        AddLog("[威胁] " + CardName(m.Template.Id) + " 嘲讽必解");
                    }
                }
                else if (highAttack)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(m.Template.Id, new Modifier(80));
                    if (VerboseLog)
                    {
                        AddLog("[威胁] " + CardName(m.Template.Id) + " 高攻" + m.CurrentAtk + "点");
                    }
                }
            }

            // ==================== 武器使用策略 ====================
            // 武器主要用于打脸，除非必须解嘲讽
            if (board.WeaponFriend != null)
            {
                bool hasEnemyTaunt = board.MinionEnemy.Any(m => m.IsTaunt);
                if (!hasEnemyTaunt)
                {
                    p.GlobalWeaponsAttackModifier = -100;
                    AddLog("[武器] 无嘲讽，武器全力打脸");
                }
                else
                {
                    AddLog("[武器] 有嘲讽，武器可解场");
                }
            }

            // ==================== 日志输出 ====================
            AddLog("=".PadRight(80, '='));

            // 你要求：每张牌都有“用了思考”（硬币除外）
            FlushThoughtPerCard(board);

            PrintLog();

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

            ApplyLiveMemoryBiasCompat(board, p);
            return p;
        }

        // ==================== 其他接口 ====================
        public Card.Cards SireDenathriusChoice(Board board)
        {
            return 匕首精通;
        }

        public Card.Cards ChooseOneCard(Board board, Card.Cards ChooseOneCard)
        {
            return 匕首精通;
        }

        // 芬利爵士选择
        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            if (choices == null || choices.Count == 0)
                return 匕首精通;

            var filteredTable = _heroPowersPriorityTable.Where(x => choices.Contains(x.Key)).ToList();
            if (filteredTable.Count == 0)
                return choices[0];

            return filteredTable.First(x => x.Value == filteredTable.Max(y => y.Value)).Key;
        }

        // 卡扎库斯选择
        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : 匕首精通;
        }
        private static bool ApplyLiveMemoryBiasCompat(Board board, ProfileParameters p)
        {
            return false;
        }

        internal static class ProfileCommon
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
        }
    }
}
