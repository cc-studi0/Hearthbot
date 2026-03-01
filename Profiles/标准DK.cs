using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API;
using SmartBotAPI.Battlegrounds;
using SmartBot.Plugins.API.Actions;


/* Explanation on profiles :
 * 
 * 配置文件中定义的所有值都是百分比修饰符，这意味着它将影响基本配置文件的默认值。
 * 
 * 修饰符值可以在[-10000;范围内设置。 10000]（负修饰符有相反的效果）
 * 您可以为非全局修改器指定目标，这些目标特定修改器将添加到卡的全局修改器+修改器之上（无目标）
 * 
 * 应用的总修改器=全局修改器+无目标修改器+目标特定修改器
 * 
 * GlobalDrawModifier --->修改器应用于卡片绘制值
 * GlobalWeaponsAttackModifier --->修改器适用于武器攻击的价值，它越高，人工智能攻击武器的可能性就越小
 * 
 * GlobalCastSpellsModifier --->修改器适用于所有法术，无论它们是什么。修饰符越高，AI玩法术的可能性就越小
 * GlobalCastMinionsModifier --->修改器适用于所有仆从，无论它们是什么。修饰符越高，AI玩仆从的可能性就越小
 * 
 * GlobalAggroModifier --->修改器适用于敌人的健康值，越高越好，人工智能就越激进
 * GlobalDefenseModifier --->修饰符应用于友方的健康值，越高，hp保守的将是AI
 * 
 * CastSpellsModifiers --->你可以为每个法术设置个别修饰符，修饰符越高，AI玩法术的可能性越小
 * CastMinionsModifiers --->你可以为每个小兵设置单独的修饰符，修饰符越高，AI玩仆从的可能性越小
 * CastHeroPowerModifier --->修饰符应用于heropower，修饰符越高，AI玩它的可能性就越小
 * 
 * WeaponsAttackModifiers --->适用于武器攻击的修饰符，修饰符越高，AI攻击它的可能性越小
 * 
 * OnBoardFriendlyMinionsValuesModifiers --->修改器适用于船上友好的奴才。修饰语越高，AI就越保守。
 * OnBoardBoardEnemyMinionsModifiers --->修改器适用于船上的敌人。修饰符越高，AI就越会将其视为优先目标。
 *
 */

namespace SmartBotProfiles
{
    [Serializable]
    public class STDDK  : Profile
    {
        #region 英雄技能
        //幸运币
        private const Card.Cards TheCoin = Card.Cards.GAME_005;
        //战士
        private const Card.Cards ArmorUp = Card.Cards.HERO_01bp;
        //萨满
        private const Card.Cards TotemicCall = Card.Cards.HERO_02bp;
        //盗贼
        private const Card.Cards DaggerMastery = Card.Cards.HERO_03bp;
        //圣骑士
        private const Card.Cards Reinforce = Card.Cards.HERO_04bp;
        //猎人
        private const Card.Cards SteadyShot = Card.Cards.HERO_05bp;
        //德鲁伊
        private const Card.Cards Shapeshift = Card.Cards.HERO_06bp;
        //术士
        private const Card.Cards LifeTap = Card.Cards.HERO_07bp;
        //法师
        private const Card.Cards Fireblast = Card.Cards.HERO_08bp;
        //牧师
        private const Card.Cards LesserHeal = Card.Cards.HERO_09bp;
        #endregion

#region 英雄能力优先级
        private readonly Dictionary<Card.Cards, int> _heroPowersPriorityTable = new Dictionary<Card.Cards, int>
        {
            {SteadyShot, 9},//猎人
            {LifeTap, 8},//术士
            {DaggerMastery, 7},//盗贼
            {Reinforce, 5},//骑士
            {Fireblast, 4},//法师
            {Shapeshift, 3},//德鲁伊
            {LesserHeal, 2},//牧师
            {ArmorUp, 1},//战士
        };
        private int GetTag(Card c, Card.GAME_TAG tag)
        {
            if (c.tags != null && c.tags.ContainsKey(tag))
                return c.tags[tag];
            return -1;
        }
            private int GetSireDenathriusDamageCount(Card c)
        {
            return GetTag(c, Card.GAME_TAG.TAG_SCRIPT_DATA_NUM_2);
        }
					private int quantityOverflowLava(Card c)
				{
						return GetTag(c, Card.GAME_TAG.TAG_SCRIPT_DATA_NUM_1);
				}
#endregion

#region 直伤卡牌 标准模式
        //直伤法术卡牌（必须是可打脸的伤害） 需要计算法强
        private static readonly Dictionary<Card.Cards, int> _spellDamagesTable = new Dictionary<Card.Cards, int>
        {
            //萨满
            {Card.Cards.CORE_EX1_238, 3},//闪电箭 Lightning Bolt     CORE_EX1_238
            {Card.Cards.DMF_701, 4},//深水炸弹 Dunk Tank     DMF_701
            {Card.Cards.DMF_701t, 4},//深水炸弹 Dunk Tank     DMF_701t
            {Card.Cards.BT_100, 3},//毒蛇神殿传送门 Serpentshrine Portal     BT_100 
            //德鲁伊

            //猎人
            {Card.Cards.BAR_801, 1},//击伤猎物 Wound Prey     BAR_801
            {Card.Cards.CORE_DS1_185, 2},//奥术射击 Arcane Shot     CORE_DS1_185
            {Card.Cards.CORE_BRM_013, 3},//快速射击 Quick Shot     CORE_BRM_013
            {Card.Cards.BT_205, 3},//废铁射击 Scrap Shot     BT_205 
            //法师
            {Card.Cards.BAR_541, 2},//符文宝珠 Runed Orb     BAR_541 
            {Card.Cards.CORE_CS2_029, 6},//火球术 Fireball     CORE_CS2_029
            {Card.Cards.BT_291, 5},//埃匹希斯冲击 Apexis Blast     BT_291 
            //骑士
            {Card.Cards.CORE_CS2_093, 2},//奉献 Consecration     CORE_CS2_093 
            //牧师
            //盗贼
            {Card.Cards.BAR_319, 2},//邪恶挥刺（等级1） Wicked Stab (Rank 1)     BAR_319
            {Card.Cards.BAR_319t, 4},//邪恶挥刺（等级2） Wicked Stab (Rank 2)     BAR_319t
            {Card.Cards.BAR_319t2, 6},//邪恶挥刺（等级3） Wicked Stab (Rank 3)     BAR_319t2 
            {Card.Cards.CORE_CS2_075, 3},//影袭 Sinister Strike     CORE_CS2_075
            {Card.Cards.TSC_086, 4},//剑鱼 TSC_086
            //术士
            {Card.Cards.CORE_CS2_062, 3},//地狱烈焰 Hellfire     CORE_CS2_062
            //战士
            {Card.Cards.DED_006, 6},//重拳先生 DED_006
            //中立
            {Card.Cards.DREAM_02, 5},//伊瑟拉苏醒 Ysera Awakens     DREAM_02
            {Card.Cards.TOY_508, 2},//立体书  TOY_508
        };
        //直伤随从卡牌（必须可以打脸）
        private static readonly Dictionary<Card.Cards, int> _MinionDamagesTable = new Dictionary<Card.Cards, int>
        {
            //盗贼
            {Card.Cards.BAR_316, 2},//油田伏击者 Oil Rig Ambusher     BAR_316 
            //萨满
            {Card.Cards.CORE_CS2_042, 4},//火元素 Fire Elemental     CORE_CS2_042 
            //德鲁伊
            //术士
            {Card.Cards.CORE_CS2_064, 1},//恐惧地狱火 Dread Infernal     CORE_CS2_064 
            //中立
            {Card.Cards.CORE_CS2_189, 1},//精灵弓箭手 Elven Archer     CORE_CS2_189
            {Card.Cards.CS3_031, 8},//生命的缚誓者阿莱克丝塔萨 Alexstrasza the Life-Binder     CS3_031 
            {Card.Cards.DMF_174t, 4},//马戏团医师 Circus Medic     DMF_174t
            {Card.Cards.DMF_066, 2},//小刀商贩 Knife Vendor     DMF_066 
            {Card.Cards.SCH_199t2, 2},//转校生 Transfer Student     SCH_199t2 
            {Card.Cards.SCH_273, 1},//莱斯·霜语 Ras Frostwhisper     SCH_273
            {Card.Cards.BT_187, 3},//凯恩·日怒 Kayn Sunfury     BT_187
            {Card.Cards.BT_717, 2},//潜地蝎 Burrowing Scorpid     BT_717 
            {Card.Cards.CORE_EX1_249, 2},//迦顿男爵 Baron Geddon     CORE_EX1_249 
            {Card.Cards.DMF_254, 30},//迦顿男爵 Baron Geddon     CORE_EX1_249 
            {Card.Cards.RLK_222t2, 14},//火焰使者阿斯塔洛 Astalor, the Flamebringer ID：RLK_222t2
            {Card.Cards.RLK_224, 2},//监督者弗里吉达拉 Overseer Frigidara ID：RLK_224
             {Card.Cards.RLK_063, 5},//冰霜巨龙之怒 Frostwyrm's Fury ID：RLK_063 
            {Card.Cards.RLK_015, 3},//凛风冲击 Howling Blast ID：RLK_015 
            {Card.Cards.RLK_516, 2},//碎骨手斧 Bone Breaker ID：RLK_516
            {Card.Cards.TTN_454, 3},//殉船 TTN_454
            {Card.Cards.VAC_427, 3},//甜筒殡淇淋 VAC_427
            {Card.Cards.VAC_323t, 1},//麦芽岩浆 VAC_323t/VAC_323t2/VAC_323
            {Card.Cards.VAC_323t2, 1},//麦芽岩浆 VAC_323t/VAC_323t2/VAC_323
            {Card.Cards.VAC_323, 1},//麦芽岩浆 VAC_323t/VAC_323t2/VAC_323
            {Card.Cards.CORE_RLK_505, 10},//髓骨使御者 CORE_RLK_505
        };
        #endregion

#region 攻击模式和自定义 
      private string _log = "";   // 日志字符串
      private const string ProfileVersion = "2026-01-03.1";
      public ProfileParameters GetParameters(Board board)
      {
            _log = "";
            var p = new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = -10 }; 

            try
            {
                int enemyHealth = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                int friendHealth = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                AddLog($"================ 标准DK 决策日志 v{ProfileVersion} ================");
                AddLog($"敌方血甲: {enemyHealth} | 我方血甲: {friendHealth} | 法力:{board.ManaAvailable} | 手牌:{board.Hand.Count} | 牌库:{board.FriendDeckCount}");
                AddLog("手牌: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
                    .Select(x => $"{(string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN)}({x.Template.Id}){x.CurrentCost}")));
            }
            catch
            {
                // ignore
            }
            //  增加思考时间
             
            int a =(board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) - BoardHelper.GetEnemyHealthAndArmor(board);
            //攻击模式切换
            // 德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER 死：DEATHKNIGHT
  

       {
 
        
            int myAttack = 0;
            int enemyAttack = 0;

            if (board.MinionFriend != null)
            {
                for (int x = 0; x < board.MinionFriend.Count; x++)
                {
                    myAttack += board.MinionFriend[x].CurrentAtk;
                }
            }

            if (board.MinionEnemy != null)
            {
                for (int x = 0; x < board.MinionEnemy.Count; x++)
                {
                    enemyAttack += board.MinionEnemy[x].CurrentAtk;
                }
            }

            if (board.WeaponEnemy != null)
            {
                enemyAttack += board.WeaponEnemy.CurrentAtk;
            }

            if ((int)board.EnemyClass == 2 || (int)board.EnemyClass == 7 || (int)board.EnemyClass == 8)
            {
                enemyAttack += 1;
            }
            else if ((int)board.EnemyClass == 6)
            {
                enemyAttack += 2;
            }   
         //定义场攻  用法 myAttack <= 5 自己场攻大于小于5  enemyAttack  <= 5 对面场攻大于小于5  已计算武器伤害

            int myMinionHealth = 0;
            int enemyMinionHealth = 0;

            if (board.MinionFriend != null)
            {
                for (int x = 0; x < board.MinionFriend.Count; x++)
                {
                    myMinionHealth += board.MinionFriend[x].CurrentHealth;
                }
            }

            if (board.MinionEnemy != null)
            {
                for (int x = 0; x < board.MinionEnemy.Count; x++)
                {
                    enemyMinionHealth += board.MinionEnemy[x].CurrentHealth;
                }
            }
int myHealth = board.HeroFriend.CurrentHealth;
int enemyHealth = BoardHelper.GetEnemyHealthAndArmor(board);
// 主邏輯：根據職業計算動態攻擊值
// 更新職業攻擊值的調整邏輯
switch (board.EnemyClass)
{
    case Card.CClass.PALADIN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 115, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.DEMONHUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 107, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.PRIEST:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 122, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.MAGE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 118, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.DEATHKNIGHT:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 110, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.HUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 103, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.ROGUE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 100, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.WARLOCK:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 128, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.SHAMAN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 97, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.DRUID:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 125, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    case Card.CClass.WARRIOR:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 95, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack);
        break;

    default:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 100, board.EnemyClass, myHealth, enemyHealth, myAttack, enemyAttack); // 預設職業
        break;
}

// 修正勝率及使用率數據
double GetWinRateFromData(Card.CClass enemyClass)
{
    switch (enemyClass)
    {
        case Card.CClass.DEATHKNIGHT: return 50.8;
        case Card.CClass.DEMONHUNTER: return 55.2;
        case Card.CClass.DRUID: return 56.5;
        case Card.CClass.HUNTER: return 53.7;
        case Card.CClass.MAGE: return 51.9;
        case Card.CClass.PALADIN: return 52.3;
        case Card.CClass.PRIEST: return 48.2;
        case Card.CClass.ROGUE: return 47.9;
        case Card.CClass.SHAMAN: return 50.4;
        case Card.CClass.WARLOCK: return 54.1;
        case Card.CClass.WARRIOR: return 49.5;
        default: return 50.0; // 默認勝率
    }
}

double GetUsageRateFromData(Card.CClass enemyClass)
{
    switch (enemyClass)
    {
        case Card.CClass.PALADIN: return 9.1;
        case Card.CClass.DEMONHUNTER: return 12.5;
        case Card.CClass.PRIEST: return 7.8;
        case Card.CClass.MAGE: return 10.3;
        case Card.CClass.DEATHKNIGHT: return 8.7;
        case Card.CClass.HUNTER: return 13.4;
        case Card.CClass.ROGUE: return 14.2;
        case Card.CClass.WARLOCK: return 6.9;
        case Card.CClass.SHAMAN: return 9.2;
        case Card.CClass.DRUID: return 8.5;
        case Card.CClass.WARRIOR: return 7.4;
        default: return 10.0;
    }
}

int CalculateAggroModifier(double baseAggro, int baseValue, Card.CClass enemyClass, int myHealth, int enemyHealth, int myAttack, int enemyAttack)
{
    // 1. 勝率修正
    double winRateModifier = (GetWinRateFromData(enemyClass) - 50) * 2.5; 

    // 2. 使用率修正
    double usageRateModifier = GetUsageRateFromData(enemyClass) * 0.9; 

    // 3. 血量差修正
    double healthDifferenceModifier = (myHealth - enemyHealth) * 1.2; 

    // 4. 場面控制修正
    double boardControlModifier = (myAttack - enemyAttack) * 1.3; 

    // 總修正值計算
    double totalModifier = winRateModifier + usageRateModifier + healthDifferenceModifier + boardControlModifier;

    // 最終攻擊值
    int finalAggro = (int)(baseAggro * 0.6 + baseValue + totalModifier-50);

    AddLog($"職業: {enemyClass}, 攻擊值: {finalAggro}, 勝率修正: {winRateModifier}, 使用率修正: {usageRateModifier}, 血量修正: {healthDifferenceModifier}, 場面修正: {boardControlModifier}");
    
    return finalAggro;
}

            // 友方随从数量
            int friendCount = board.MinionFriend.Count;
            // 手牌数量
            int HandCount = board.Hand.Count;
            // 手上随从数量
            int minionNumber=board.Hand.Count(card => card.Type == Card.CType.MINION);
            // 可攻击随从
            int canAttackMINIONs=board.MinionFriend.Count(card => card.CanAttack);
            // 敌方小于3血随从数量
            int enemyOneBlood=board.MinionEnemy.Count;
            // 手上亡灵属性的牌且不为脆骨海盗 不为 孵化池 SC_000  爆虫 SC_019t
            int graveUndead=board.Hand.Count(card =>card.Template.HasDeathrattle&&card.Template.Id!=Card.Cards.SC_000&&card.Template.Id!=Card.Cards.SC_019t);
						// 场上亡灵数量
						int boardUndead=board.MinionFriend.Count(card =>card.Template.HasDeathrattle&&card.Template.Id!=Card.Cards.SC_000);
            // AddLog("手上亡语"+graveUndead);
						// 坟场里麦芽岩浆数量
						int graveMaltLava=board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.VAC_323);
						 // 定义死亡嘶吼禁用列表
                var deathRattleBlacklist = new List<Card.Cards>
                {
                    Card.Cards.WW_331,     // 奇迹推销员
                    Card.Cards.VAC_514     // 恐惧猎犬训练师 VAC_514
                };

                // 定义死亡嘶吼优先级字典 <卡牌ID, 优先值>
                var deathRattlePriorities = new Dictionary<Card.Cards, int>
                {
                    { Card.Cards.SC_002, -9999 },    // 感染者最高优先级
                    { Card.Cards.VAC_426, -9999 },    // 伊丽扎·刺刃 VAC_426
                    { Card.Cards.WW_373, -100 },    // 矿坑老板雷斯卡次高优先级
                    // { Card.Cards.RLK_708, -50 },    // 堕寒男爵 RLK_708
                };
								// 定义一个卡牌使用优先级字典
var cardPriority = new Dictionary<Card.Cards, int>
{
    { Card.Cards.SC_000, 999 },    // 孵化池 SC_000
    { Card.Cards.SC_019t, 1999 },    // SC_019t", // 爆虫
    { Card.Cards.DEEP_017, 999 },    // DEEP_017	采矿事故
    { Card.Cards.VAC_436, 999 },    // 脆骨海盗 VAC_436
    { Card.Cards.SC_015, 999 },    // 坑道虫 SC_015
    { Card.Cards.VAC_437, 999 },    // 扣子 VAC_437
    { Card.Cards.SC_010,  1899},    // SC_010 跳虫
    { Card.Cards.CORE_RLK_042, 899 },    // 寒冬号角 CORE_RLK_042
    { Card.Cards.VAC_425, 899 },    // 大地之末号 VAC_425
    { Card.Cards.SC_009, 899 },    // "SC_009"  // 潜伏者
    { Card.Cards.SC_003, 899 },    //  虫巢女王 SC_003
    { Card.Cards.WW_357, 899 },    // 老腐和老墓 WW_357  
    { Card.Cards.WW_373, 899 },    // 矿坑老板雷斯卡次高优先级
    { Card.Cards.SC_012, 899 },    // 蟑螂 SC_012
    { Card.Cards.SC_023, 899 },    // 针脊爬虫 SC_023
    { Card.Cards.VAC_933, 899 },    // 飞行员帕奇斯 VAC_933
    { Card.Cards.VAC_450, 899 },    // 悠闲的曲奇 VAC_450
    { Card.Cards.VAC_329, 899 },    // 自然天性 VAC_329
    { Card.Cards.VAC_301, 899 },    // 炫目演出者 VAC_301
    { Card.Cards.GDB_310, 899 },    // 虚灵神谕者 GDB_310
    { Card.Cards.CORE_EX1_012, 899 },    // CORE_EX1_012 血法师萨尔诺斯
    { Card.Cards.VAC_959t10, 899 },    // VAC_959t10 挺进护符
    { Card.Cards.GDB_113, 899 },    // GDB_113 气闸破损
    { Card.Cards.VAC_955t, 899 },    // 美味奶酪 VAC_955t 
    { Card.Cards.VAC_323t, 899 },    // 麦芽岩浆 VAC_323t
    { Card.Cards.VAC_323t2, 899 },    // 麦芽岩浆 VAC_323t2 
    { Card.Cards.VAC_323, 899 },    // 麦芽岩浆 VAC_323
    { Card.Cards.RLK_708, 899 },    // 堕寒男爵 RLK_708 
    { Card.Cards.WW_331, 899 },    // 奇迹推销员 WW_331
    { Card.Cards.SC_002, 899 },    // 感染者 SC_002
    { Card.Cards.RLK_025, 899 },    // 冰霜打击 RLK_025
    { Card.Cards.ETC_424, 150 },    // ETC_424 死亡嘶吼
    { Card.Cards.HERO_11lbp, -500 },    // 英雄技能HERO_11bp 英雄技能
    { Card.Cards.SC_018, -50 },    // 飞蛇 SC_018
};
						// 尸体数量
            int numberBodies=board.CorpsesCount;
							// 定义异虫数量
						var alienCount = GetBeastsCount(board);
						// AddLog("异虫数量"+alienCount);
						// 场上异虫的攻击力总和为
						int alienAttack = GetTotalZergAttack(board);
						// AddLog("异虫攻击力"+alienAttack);
						// 亡语随从排除列表
						var excludedListOfDeadLanguageFollowers = new List<Card.Cards>
						{
								Card.Cards.SC_000,     // 孵化池 SC_000
								Card.Cards.VAC_514,     // 恐惧猎犬训练师 VAC_514
								Card.Cards.RLK_511,     // 寒冬先锋	RLK_511
								Card.Cards.WW_331     // 奇迹推销员
								
						};
						// 排除地标列表
						var excludeLandmarkList = new List<Card.Cards>
						{
								Card.Cards.SC_000,     // 孵化池 SC_000
								
						};
						// 判断手上是否有下回合可用的亡语随从
						bool hasPlayableDeathrattleNextTurn = board.Hand.Any(card =>
								card.Template.HasDeathrattle&&
								!excludeLandmarkList.Contains(card.Template.Id) &&
								card.CurrentCost <= board.MaxMana+1
						);
						int myHeroHealth =(30-board.HeroFriend.CurrentHealth)*-99>0?-99:(30-board.HeroFriend.CurrentHealth)*-99;
						// 己方随从减去孵化池数量
				int removeQuantity = board.MinionFriend.Count - board.MinionFriend.Count(x => x.Template.Id == Card.Cards.SC_000);
  				// 定义场上所有随从数量
						int allMinionCount = CountSpecificRacesInHand(board);
 #endregion

//  蛛魔护群守卫 RLK_062 随从 友方随从数小于等于4
#region 蛛魔护群守卫 RLK_062
					if(board.HasCardInHand(Card.Cards.RLK_062)
					// 友方随从数小于等于4
					&& board.MinionFriend.Count<=4
					)
					{
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_062, new Modifier(-150)); // 降低蛛魔护群守卫的使用优先级
							AddLog("蛛魔护群守卫 -150");
					}else{
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_062, new Modifier(350)); // 提高蛛魔护群守卫的使用优先级
					}
#endregion

//  血虫感染 EDR_817 法术 友方随从书小于等于5 手牌小于等于8
#region 血虫感染 EDR_817
					if(board.HasCardInHand(Card.Cards.EDR_817)
					// 友方随从书小于等于5
					&& board.MinionFriend.Count<=5
					// 手牌小于等于8
					&& HandCount<=8
					)
					{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_817, new Modifier(-150)); // 降低血虫感染的使用优先级
							AddLog("血虫感染 -150");
					}else{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_817, new Modifier(350)); // 提高血虫感染的使用优先级
					}
#endregion

 #region 城市首脑埃舒 TLC_110
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_110, new Modifier(3999));
				if(board.HasCardInHand(Card.Cards.TLC_110)
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_110, new Modifier(-350));
						AddLog($"城市首脑埃舒 TLC_110 {-350}");
				}
#endregion

//  业余傀儡师 TOY_828 TOY_828t 随从
#region 业余傀儡师 TOY_828
					if(board.HasCardInHand(Card.Cards.TOY_828)
					)
					{
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_828, new Modifier(-150)); // 降低业余傀儡师的使用优先级
							AddLog("业余傀儡师 -150");
					}
#endregion

#region 业余傀儡师 TOY_828t
					if(board.HasCardInHand(Card.Cards.TOY_828t)
					)
					{
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_828t, new Modifier(-150)); // 降低业余傀儡师的使用优先级
							AddLog("业余傀儡师 -150");
					}
#endregion

//  玩具盗窃恶鬼 MIS_006 随从
#region 玩具盗窃恶鬼 MIS_006
					if(board.HasCardInHand(Card.Cards.MIS_006)
					)
					{
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_006, new Modifier(-150)); // 降低玩具盗窃恶鬼的使用优先级
							AddLog("玩具盗窃恶鬼 -150");
					}else{
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_006, new Modifier(350)); // 提高玩具盗窃恶鬼的使用优先级
					}
#endregion

// 活力分流 RLK_712 标记且手上友方随从大于等于2 才使用
#region 活力分流 RLK_712
					if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.RLK_712)
					// 手上随从大于等于2
					&& minionNumber>=2
					)
					{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_712, new Modifier(-150)); // 降低活力分流的使用优先级
							p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_712, new Modifier(999)); // 降低活力分流的使用优先级
							AddLog("活力分流 -150");
					}else{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_712, new Modifier(350)); // 提高活力分流的使用优先级
					}
#endregion 

//  小型法术尖晶石 TOY_825 法术 不用  TOY_825t2 大型法术尖晶石
#region 小型法术尖晶石 TOY_825
					if(board.HasCardInHand(Card.Cards.TOY_825))
					{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_825, new Modifier(350)); // 提高小型法术尖晶石的使用优先级
							AddLog("小型法术尖晶石 350");
					}
#endregion

// TOY_825t 法术尖晶石 不用
#region 法术尖晶石 TOY_825t
					if(board.HasCardInHand(Card.Cards.TOY_825t))
					{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_825t, new Modifier(350)); // 提高法术尖晶石的使用优先级
							AddLog("法术尖晶石 350");
					}
#endregion

#region 大型法术尖晶石 TOY_825t2
					if(board.HasCardInHand(Card.Cards.TOY_825t2)
					// 手上随从大于等于2
					&& minionNumber>=3
					)
					{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_825t2, new Modifier(-150)); // 降低大型法术尖晶石的使用优先级
							p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_825t2, new Modifier(999)); // 降低大型法术尖晶石的使用优先级
							AddLog("大型法术尖晶石 -150");
					}else{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_825t2, new Modifier(350)); // 提高大型法术尖晶石的使用优先级
					}
#endregion

#region 不送的怪
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); //奇迹推销员 WW_331
#endregion

#region EDR_814 感染吐息
					if (board.HasCardInHand(Card.Cards.EDR_814)
					// 我方随从小于等于6
					&& board.MinionFriend.Count <= 6
					)
					{
									p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_814, new Modifier(-99)); // 优先级提高
									AddLog("感染吐息 -99: 我方随从数量小于等于6");
					}
#endregion

#region 展馆茶壶 CORE_WON_142
if(board.HasCardInHand(Card.Cards.CORE_WON_142))
{
    // 记录当前随从种类数量
    AddLog($"当前随从种类数量: {allMinionCount}");
    
    // 根据随从种类调整优先级
    if (allMinionCount >= 2)
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(-500));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(9999)); 
        AddLog("展馆茶壶 -500: 随从种类大于3，提高优先级");
    }
    else
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(350));
        AddLog("展馆茶壶 350: 随从种类少于等于3，降低优先级");
    }
}
#endregion

#region 动态添加逻辑
          if (!board.HasCardInHand(Card.Cards.SC_002))
					{
							deathRattlePriorities.Add(Card.Cards.RLK_708, -50);
					}
#endregion

#region 送的怪
        
          if(board.HasCardOnBoard(Card.Cards.TSC_962)){//修饰老巨鳍 Gigafin ID：TSC_962 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TSC_962t, new Modifier(-100)); //修饰老巨鳍之口 Gigafin's Maw ID：TSC_962t 
          }
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_373t, new Modifier(-100)); //具象暗影 Shadow Manifestation ID：REV_373t 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_955, new Modifier(-100)); //执事者斯图尔特 Stewart the Steward ID：REV_955 
#endregion

#region 狂欢舞台 ETC_533
        // 尸体数量大于等于3,遍历场上随从,提高对deathRattlePriorities随从使用优先级
				if(numberBodies>=3
				
				&&(
					board.HasCardInHand(Card.Cards.ETC_533)
					||board.HasCardOnBoard(Card.Cards.ETC_533)
					)
				){
						foreach (var minion in board.MinionFriend)
						{
								if (deathRattlePriorities.ContainsKey(minion.Template.Id))
								{
										p.LocationsModifiers.AddOrUpdate(Card.Cards.ETC_533, new Modifier(deathRattlePriorities[minion.Template.Id], minion.Template.Id));
										AddLog("狂欢舞台提高优先级"+minion.Template.NameCN);
								}else{
										p.LocationsModifiers.AddOrUpdate(Card.Cards.ETC_533, new Modifier(150, minion.Template.Id));
										AddLog("狂欢舞台降低优先级"+minion.Template.NameCN);
								}
						}
				}
				#region 狂欢舞台 ETC_533
        if(board.HasCardInHand(Card.Cards.ETC_533)
				&&board.MinionFriend.Count>6
				){
					p.LocationsModifiers.AddOrUpdate(Card.Cards.ETC_533, new Modifier(999));
					AddLog("狂欢舞台 999");
				}
#endregion
#endregion

#region Card.Cards.SC_004hp    毁灭跳击
// 场上alienCount数量越多,使用优先级越高,越优先提高使用
						if(board.Ability.Template.Id==Card.Cards.SC_004hp){
        		int priority = -alienCount*50; // 根据虫族数量调整优先级
						p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.SC_004hp, new Modifier(priority));
						// 根据异虫数量动态变化 
						int playOrderPriority;
						if (alienCount >= 5)
						{
								playOrderPriority = alienCount * 5; 
						}
						else
						{
								playOrderPriority = 25;
						}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.SC_004hp, new Modifier(playOrderPriority));
						AddLog("毁灭跳击优先级"+(playOrderPriority));
						}
#endregion

#region 蟑螂 SC_012
				// 如果蟑螂是标记状态,提高使用顺序优先值
				if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) != 1 && card.Template.Id == Card.Cards.SC_012)){
        		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_012, new Modifier(350)); 
				}
#endregion

#region 彩虹裁缝 TOY_823
			// 敌方有随从,提高彩虹裁缝 TOY_823使用优先级
			if(board.HasCardInHand(Card.Cards.TOY_823)
			&&board.MinionEnemy.Count>0
			){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_823, new Modifier(myHeroHealth)); 
				AddLog($"彩虹裁缝{myHeroHealth}");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_823, new Modifier(350)); 
			}
#endregion

#region 石英碎击槌 DEEP_016
// 定义我方英雄当前血量
			
        // 手上没有武器,提高石英碎击槌 DEEP_016使用优先级
				 	if(board.HasCardInHand(Card.Cards.DEEP_016)
					&&board.WeaponFriend==null
					){
					p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.DEEP_016, new Modifier(-150));
					AddLog($"石英碎击槌{-150}");
					}
#endregion

#region 海绵斧 MIS_101
        // 手上没有武器,提高石英碎击槌 DEEP_016使用优先级
				 	if(board.HasCardInHand(Card.Cards.MIS_101)
					&&board.WeaponFriend==null
					){
					p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.MIS_101, new Modifier(-5*numberBodies));
					AddLog("海绵斧"+(-5*numberBodies));
					}
					// 如果尸体数小于3,则不攻击
					if(numberBodies<3
					&&board.WeaponFriend!=null
					&& board.WeaponFriend.Template.Id == Card.Cards.MIS_101
					){
						p.WeaponsAttackModifiers.AddOrUpdate(SmartBot.Plugins.API.Card.Cards.MIS_101, 999, board.HeroEnemy.Id);
					AddLog("尸体数小于3不攻击");
					}
#endregion

#region 奥金尼亡语者 GDB_469
				// 场上有奥金尼亡语者 GDB_469 不送
				if(board.HasCardOnBoard(Card.Cards.GDB_469)){
					p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GDB_469, new Modifier(350)); 
				}
				// 在手上时 提高使用优先级
				if(board.HasCardInHand(Card.Cards.GDB_469)){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_469, new Modifier(-99)); 
					AddLog("奥金尼亡语者-99");
				}
#endregion

#region WW_331t	蛇油
		// 前期不用蛇油 
		// 如果牌库为空,不交易蛇油
		if(board.HasCardInHand(Card.Cards.WW_331t)
		&&board.FriendDeckCount<=2
		){
		p.TradeModifiers.AddOrUpdate(Card.Cards.WW_331t, new Modifier(999));
		AddLog("牌库为空不交易蛇油");
		}
#endregion


#region WW_331	奇迹推销员
         if(board.HasCardInHand(Card.Cards.WW_331)
					){
								p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(350)); 
								AddLog("奇迹推销员 350");
					}
#endregion

#region VAC_445	食尸鬼之夜
        //  随从数小于等于2,提高优先级
        if(board.HasCardInHand(Card.Cards.VAC_445)
        &&friendCount<=2
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_445, new Modifier(-20));
        AddLog("食尸鬼之夜 -20");
        }
#endregion

#region 戈贡佐姆 VAC_955
        // 提高优先级
        if(board.HasCardInHand(Card.Cards.VAC_955)
        // 我方场面占优
        &&myAttack>enemyAttack
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_955, new Modifier(-350));
          AddLog("戈贡佐姆-350");
        }
#endregion

#region 兵主 TTN_737
        // 提高优先级
        if(board.HasCardInHand(Card.Cards.TTN_737)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_737, new Modifier(-99));
          AddLog("兵主-99");
        }
#endregion

#region 飞行员帕奇斯 VAC_933
        if(board.HasCardInHand(Card.Cards.VAC_933)){
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_933, new Modifier(-350)); 
                AddLog("飞行员帕奇斯 -350");
        }
#endregion


#region 伞降咒符 VAC_925
       if(board.HasCardInHand(Card.Cards.VAC_925)
        &&friendCount<5
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_925, new Modifier(-550));   
        AddLog("伞降咒符 -550");
        }
#endregion

#region 贪婪的伴侣 WW_901
    //  如果没有被active,则不用
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.WW_901)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_901, new Modifier(-99)); 
        AddLog("贪婪的伴侣 -99");
    }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_901, new Modifier(350)); 
    }
#endregion

#region 悠闲的曲奇 VAC_450
    // 当场上可攻击随从大于的等于2时,提高优先级
    if (canAttackMINIONs >= 2 
        && board.HasCardInHand(Card.Cards.VAC_450)
        )
        {
            // 如果场上海盗大于2，提高优先级
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_450, new Modifier(canAttackMINIONs*-99));
        AddLog("悠闲的曲奇" + (canAttackMINIONs*-99));
        }
        else
        {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_450, new Modifier(350));
        }
#endregion

#region VAC_955t 美味奶酪
			foreach(var card in board.Hand)
      {
        if(card.Template.Id == Card.Cards.VAC_955t)
        {
						var tag = quantityOverflowLava(card);
						// 如果当前手牌数小于等于3,且最大回合数大于等于8,提高优先级
						if (board.MinionFriend.Count<=4
						//  场上没随从
						&&(tag>=5)
						&& board.HasCardInHand(Card.Cards.VAC_955t))
						{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(-90*(10-HandCount)));
								AddLog("美味奶酪"+(-90*(10-HandCount)));
						}else{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(999));
						}
					}
			}
#endregion

#region 硬币 GAME_005
    if(board.HasCardInHand(Card.Cards.GAME_005)
    && board.MaxMana >= 2
    ){
     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-55));//硬币 GAME_005
    }

#endregion

#region 寒冬号角 CORE_RLK_042
// 一费不用寒冬号角
    if(board.HasCardInHand(Card.Cards.CORE_RLK_042)
    && board.MaxMana ==1
    ){
     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_RLK_042, new Modifier(350));
     AddLog("一费不用寒冬号角");
    }
#endregion

#region 冻感舞步 JAM_006
// 下回合叫杀，提高冻感舞步优先级
    if(board.HasCardInHand(Card.Cards.JAM_006)
    &&BoardHelper.HasPotentialLethalNextTurn(board)
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_006, new Modifier(-350));
        AddLog("下回合叫杀，提高冻感舞步优先级");
    }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_006, new Modifier(-99));
    }
#endregion

#region 甜筒殡淇淋 VAC_427
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.VAC_427)
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_427, new Modifier(-5*numberBodies));
        AddLog($"甜筒殡淇淋{(-5*numberBodies)}");
						// 遍历友方随从降低对其使用优先级
				foreach (var item in board.MinionFriend)
				{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_427, new Modifier(350,item.Template.Id));
						AddLog("降低对其使用甜筒殡淇淋"+(item.Template.Id));
				}
    }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_427, new Modifier(350));
    }

#endregion

#region 冰霜打击 RLK_025
// 遍历敌方随从,如果有血量小于等于3的,提高冰霜打击使用优先级
foreach (var item in board.MinionEnemy)
{
		if(item.CurrentHealth<=3
		&&board.HasCardInHand(Card.Cards.RLK_025)
		){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_025, new Modifier(-99,item.Template.Id));
				AddLog($"血量小于等于3的,提高冰霜打击对{item.Template.NameCN}使用优先级");
		}else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_025, new Modifier(350,item.Template.Id));
		}
}
#endregion

#region 殉船 TTN_454
    if(board.HasCardInHand(Card.Cards.TTN_454)
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_454, new Modifier(-10));
             // 循环遍历我方随从,降低对其使用的优先级
        foreach (var item in board.MinionFriend)
        {
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_454, new Modifier(350,item.Template.Id));
            // AddLog("降低对其使用殉船"+(item.Template.Id));
        }
        AddLog("殉船 -10");
    }
    // 如果敌方英雄血量小于15,提高对脸的优先级
    if(board.HasCardInHand(Card.Cards.TTN_454)
    &&BoardHelper.GetEnemyHealthAndArmor(board)<=12
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_454, new Modifier(-150,board.HeroEnemy.Id));
        AddLog("如果敌方英雄血量小于15,提高殉船对脸的优先级");
    }
#endregion

#region 脆骨海盗 VAC_436
var crispyPirate = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.VAC_436);

// 定义一个排除列表
var excludeList = new List<Card.Cards>
{
    Card.Cards.SC_000,     // 孵化池 SC_000 
};

// 如果场上已有脆骨海盗,则不使用
if(board.HasCardOnBoard(Card.Cards.VAC_436)
&& crispyPirate != null
){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_436, new Modifier(999));
    AddLog("场上已有脆骨海盗,本回合不使用");
}
// 没有脆骨海盗时的正常使用逻辑
else if (crispyPirate != null
    && !board.HasCardOnBoard(Card.Cards.VAC_436)
)
{
    int winterHornCount = board.Hand.Count(x => x.Template.Id == Card.Cards.CORE_RLK_042);
    int coinCount = board.Hand.Count(x => x.Template.Id == Card.Cards.GAME_005);
    bool hasValidCombo = false;

    // 遍历所有手牌中的亡语随从
    foreach(var card in board.Hand.Where(x => x.Template.HasDeathrattle && !excludeList.Contains(x.Template.Id)))
    {
        // 对每个亡语随从判断是否可以完成连击
        bool canPlayCombo = (board.ManaAvailable <= 2 && hasPlayableDeathrattleNextTurn) ||
                           board.ManaAvailable >= (card.CurrentCost + 2 + (winterHornCount * 2) + coinCount);

        if(canPlayCombo)
        {
            hasValidCombo = true;
            AddLog($"找到可用亡语随从:{card.Template.NameCN}");
            break;
        }
    }

    if (hasValidCombo)
    {
        p.ComboModifier = new ComboSet(crispyPirate.Id);
        AddLog("脆骨海盗出");
    }
    else
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_436, new Modifier(200));
        AddLog("当前无法完成有效连击");
    }
}
else
{
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_436, new Modifier(200));
}
// 不送
if (board.HasCardOnBoard(Card.Cards.VAC_436))
{
    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_436, new Modifier(350));
}

// 如果场上有脆骨海盗,提高手上亡语随从使用优先级
if (board.HasCardOnBoard(Card.Cards.VAC_436)
    && board.Hand.Exists(card => card.Template.HasDeathrattle))
{
    foreach (var item in board.Hand)
    {
        if (item.Template.HasDeathrattle && !excludeList.Contains(item.Template.Id))
        {
            p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(-150 * item.CurrentCost));
            AddLog($"提高{item.Template.NameCN}使用优先级+{(-150 * item.CurrentCost)}");
        }
    }
}
#endregion
#region DEEP_017	采矿事故
       if(board.HasCardInHand(Card.Cards.DEEP_017)
        && board.MinionFriend.Count <=5
				&&!board.HasCardInHand(Card.Cards.VAC_436)//脆骨海盗 VAC_436
       ){
       	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(-550)); 
        AddLog("采矿事故 -550");
      }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(350)); 
      }
#endregion

#region 麦芽岩浆 VAC_323t/VAC_323t2/VAC_323
			// 敌方随从越多,提高耀斑的使用优先值
		if(board.HasCardInHand(Card.Cards.VAC_323t)
		&&enemyOneBlood>=3
		 ){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_323t, new Modifier(-50*enemyOneBlood));
				AddLog("麦芽岩浆"+(-50*enemyOneBlood));
			}else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_323t, new Modifier(150));
			}
		if(board.HasCardInHand(Card.Cards.VAC_323t2)
		&&enemyOneBlood>=3
		 ){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_323t2, new Modifier(-50*enemyOneBlood));
				AddLog("麦芽岩浆"+(-50*enemyOneBlood));
			}else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_323t2, new Modifier(150));
			}
		if(board.HasCardInHand(Card.Cards.VAC_323)
		&&enemyOneBlood>=3
		 ){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_323, new Modifier(-50*enemyOneBlood));
				AddLog("麦芽岩浆"+(-50*enemyOneBlood));
			}else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_323, new Modifier(150));
			}
#endregion

#region 堕寒男爵 RLK_708
        if(board.HasCardInHand(Card.Cards.RLK_708)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_708, new Modifier(-350));
          AddLog("堕寒男爵 -350");
        }
				// 如果手里有坑道虫且场上没有脆骨海盗,减少堕寒男爵使用优先级 坑道虫 SC_015
				if(board.HasCardInHand(Card.Cards.RLK_708)
				&&!board.HasCardOnBoard(Card.Cards.VAC_436)//脆骨海盗
				&&board.HasCardInHand(Card.Cards.SC_015)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_708, new Modifier(350));
					AddLog("如果手里有坑道虫且场上没有脆骨海盗,减少堕寒男爵使用优先级");
				}
				// 场上有,送
				if(board.HasCardOnBoard(Card.Cards.RLK_708)){
					p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.RLK_708, new Modifier(-10));
				}
#endregion

#region 自然天性 VAC_329
        if(board.HasCardInHand(Card.Cards.VAC_329)
        )
        {
          p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_329, new Modifier(-99));
          AddLog("自然天性 -99");
        }
        if(board.HasCardInHand(Card.Cards.VAC_329)
        )
        {
          p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_329, new Modifier(-150));
          AddLog("自然天性 -150");
        }
#endregion

#region 炫目演出者 VAC_301
			foreach(var card in board.Hand)
      {
        if(card.Template.Id == Card.Cards.VAC_301)
        {
          var tag = quantityOverflowLava(card);
					 if(board.HasCardInHand(Card.Cards.VAC_301)
						&&tag>=2
						// 场上随从数量小于等于4
						&&board.MinionFriend.Count+tag<=6
						)
						{
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_301, new Modifier(-350));
							AddLog("炫目演出者 -350");
						}else{
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_301, new Modifier(200));
						}
					}
			}
#endregion

#region TTN_850	海拉
      if(board.HasCardInHand(Card.Cards.TTN_850)
      ){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_850, new Modifier(-250));
        AddLog("海拉 -250");
        }
#endregion

#region 寒冬先锋	RLK_511
    if(board.HasCardInHand(Card.Cards.RLK_511)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_511, new Modifier(-150));
        AddLog("寒冬先锋 -150");
    }
#endregion

#region 大地之末号 VAC_425
        var endTheEarth = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.VAC_425);
        if(endTheEarth != null
				&&board.MinionFriend.Count<=6
        ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_425, new Modifier(-150));
            AddLog("大地之末号-150");
        }else{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_425, new Modifier(350));
				}
				// 如果场上有VAC_425,遍历手上的法术牌,降低其对友方随从使用
				if(board.HasCardOnBoard(Card.Cards.VAC_425)){
					foreach (var item in board.Hand)
					{
						if(item.Type==Card.CType.SPELL){
							// 遍历场上的友方随从牌,降低其对友方随从使用的优先级
							foreach (var card in board.MinionFriend)
							{
								p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(9999,card.Template.Id));
								// AddLog("降低对友方随从使用"+(item.Template.Id));
							}
						}
					}
				}
				// 如果场上有大地之末号,提高其使用优先级
				if(board.HasCardOnBoard(Card.Cards.VAC_425)){
					p.LocationsModifiers.AddOrUpdate(Card.Cards.VAC_425, new Modifier(-20));
					AddLog("提高大地之末号使用优先级");
				}
#endregion

#region 扣子 VAC_437
    if(board.HasCardInHand(Card.Cards.VAC_437)
		// 手牌数量
		&&HandCount<=8
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_437, new Modifier(-250));
				AddLog("扣子 -250");
    }
#endregion

#region 伊丽扎·刺刃 VAC_426
 if(board.HasCardInHand(Card.Cards.VAC_426)
 		&&board.ManaAvailable>=4
		&&board.MinionFriend.Count<=6
		// 手里没有感染者 SC_002
		&&!board.HasCardInHand(Card.Cards.SC_002)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_426, new Modifier(-999));
         AddLog("伊丽扎·刺刃-999");
    }else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_426, new Modifier(350));
		}
#endregion

#region 老腐和老墓 WW_357
 if(board.HasCardInHand(Card.Cards.WW_357)
 		&&board.ManaAvailable>=4
		&&board.MinionFriend.Count<=6
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_357, new Modifier(-150));
         AddLog("老腐和老墓-150");
    }else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_357, new Modifier(999));
		}
#endregion

#region 虚灵神谕者 GDB_310
 		if(board.HasCardInHand(Card.Cards.GDB_310)
		
    ){
		// 遍历手牌随从,如果是法术,且当前法术牌的费用+3小于等于当前回合数,提高虚灵神谕者的优先级
				foreach (var item in board.Hand)
			{
				if(item.Type==Card.CType.SPELL
				&&item.CurrentCost+3<=board.ManaAvailable
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(-550));
					AddLog("虚灵神谕者 -550");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(999));
					// AddLog("虚灵神谕者 999");
				}
			}
    }
		// 场上有虚灵神谕者,不送
		if(board.MinionFriend.Exists(x=>x.Template.Id==Card.Cards.GDB_310&&x.HasSpellburst)){
			p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(150));
			foreach (var item in board.MinionFriend)
			{
				if(item.Template.Id==Card.Cards.GDB_310
				&&item.HasSpellburst
				){
						// 遍历手上随从,提高法术牌优先级
					foreach (var card in board.Hand)
					{
						if(card.Type==Card.CType.SPELL
						// 手牌数小于等于9
						&&HandCount<=8
						){
							p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-350));
							AddLog("提高法术牌优先级"+card.Template.NameCN);
						}
					}
				}
			}
		}
#endregion

#region 髓骨使御者 CORE_RLK_505
 if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.CORE_RLK_505)
    &&board.CorpsesCount>=5
		){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_RLK_505, new Modifier(-99));
				AddLog("髓骨使御者 -99");
    }else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_RLK_505, new Modifier(350));
		}
#endregion

#region 矿坑老板雷斯卡 WW_373
		// 如果敌方随从数量小于2降低矿坑老板雷斯卡 WW_373
		if(board.HasCardInHand(Card.Cards.WW_373)
		){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_373, new Modifier(150));
				AddLog("矿坑老板雷斯卡 150");
		}
#endregion

#region 脆弱的食尸鬼 HERO_11bpt
// 送脆弱的食尸鬼
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.HERO_11bpt, new Modifier(55));
#endregion

#region 重新思考
// 定义需要排除的卡牌集合

var excludedCards = new HashSet<string>
{
    Card.Cards.GAME_005.ToString()
};
// 遍历并处理卡牌的方法
void EvaluateCards(IEnumerable<Card> cards)
{
    foreach (var card in cards)
    {
         // 如果卡牌不在排除列表中，则加入重新思考的队列
        if (!excludedCards.Contains(card.Template.Id.ToString()))
        {
            p.ForcedResimulationCardList.Add(card.Template.Id);
        }
    }
}

// 遍历手牌
EvaluateCards(board.Hand);

// 遍历场上随从
EvaluateCards(board.MinionFriend);

// 逻辑后续 - 重新思考
if (p.ForcedResimulationCardList.Any())
{
    // 如果支持日志系统，使用框架的日志记录方法
    // AddLog("需要重新评估策略的卡牌数: " + p.ForcedResimulationCardList.Count);

    // 如果没有日志系统，可以直接处理策略
    // 替代具体的重新思考逻辑
}
#endregion




#region VAC_959t05 追踪护符 Amulet of Tracking 随机获取3张传说卡牌。（然后将其变形成为普通卡牌！）
// 如果手牌数小于等于3,提高使用优先级
        if(board.HasCardInHand(Card.Cards.VAC_959t05)
        &&HandCount<=6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t05, new Modifier(-99));
        AddLog("追踪护符-99");
        }
#endregion

#region VAC_959t06 生灵护符 Amulet of Critters 随机召唤一个法力值消耗为（4）的随从并使其获得嘲讽。（但它无法攻击！）
        if(board.HasCardInHand(Card.Cards.VAC_959t06)
				// 场上随从小于等于6
				&&board.MinionFriend.Count<=6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t06, new Modifier(-99));
        AddLog("生灵护符-99");
        }
#endregion

#region VAC_959t08 能量护符 Amulet of Energy 为你的英雄恢复12点生命值。（然后受到6点伤害！）
        if(board.HasCardInHand(Card.Cards.VAC_959t08)
                // 可恢复生命值大于6
                &&board.HeroFriend.MaxHealth-board.HeroFriend.CurrentHealth > 6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t08, new Modifier(-99));
        AddLog("能量护符-99");
        }
#endregion

#region VAC_959t10 挺进护符 Amulet of Strides 使你手牌中的所有卡牌的法力值消耗减少（1）点。（法术牌除外！）
        if(board.HasCardInHand(Card.Cards.VAC_959t10)
        ){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t10, new Modifier(-99));
        AddLog("挺进护符-99");
        }
#endregion

#region 孵化池 SC_000
				var SC_000 = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.SC_000);
        if(board.MinionFriend.Count<=6
				&&SC_000!=null
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_000, new Modifier(-350));
					AddLog("孵化池-350");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_000, new Modifier(999));
				}
				// 提高0飞孵化池使用优先级,友方随从书小于等于6
				if(board.MinionFriend.Count<=6
				&&board.Hand.Exists(x=>x.CurrentCost==0 && x.Template.Id==Card.Cards.SC_000)
				&&SC_000!=null
				){
						p.ComboModifier = new ComboSet(SC_000.Id);
					AddLog("孵化池出");
					}
// // 场上有孵化池,敌方没有随从,不用孵化池
// 				if(board.HasCardOnBoard(Card.Cards.SC_000)
// 				&&(board.MinionEnemy.Count==0||(board.ManaAvailable==0&&alienCount==0))
// 				){
// 					p.LocationsModifiers.AddOrUpdate(Card.Cards.SC_000, new Modifier(999));
// 					AddLog("场上有孵化池,敌方没有随从,不用孵化池");
// 				}
// 					if(board.HasCardOnBoard(Card.Cards.SC_000)
// 				){
// 					p.AttackOrderModifiers.AddOrUpdate(Card.Cards.SC_000, new Modifier(999));
// 					AddLog("场上有孵化池,优先用");
// 				}
#endregion



#region 黑暗符文 YOG_511
        if(
				board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.YOG_511)
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.YOG_511, new Modifier(-99));
					AddLog("黑暗符文 -99");
				}else{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.YOG_511, new Modifier(350));
				}
#endregion

#region 虫巢女王 SC_003
// 定义场上虫巢女王的数量
int SC_003Count = board.MinionFriend.Count(x => x.Template.Id == Card.Cards.SC_003);
// 手上有虫巢女王且手牌数<=9时可以使用
if(board.HasCardInHand(Card.Cards.SC_003)
    && board.Hand.Count+SC_003Count <= 9
// 手里没有感染者 SC_002
		&&!board.HasCardInHand(Card.Cards.SC_002)
){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_003, new Modifier(-350));
    AddLog("虫巢女王 -350");
}
else
{
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_003, new Modifier(999));
}

// 场上有虫巢女王时不送
if(board.HasCardOnBoard(Card.Cards.SC_003)){
    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.SC_003, new Modifier(150));
}
#endregion

// #region SC_023 脊背刺虫
//         if(board.HasCardInHand(Card.Cards.SC_023)
//         ){
// 					// 不送 
//           p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_023, new Modifier(999));
//         	AddLog("脊背刺虫不使用");
//         }
// #endregion

#region 感染者 SC_002
        var grz = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.SC_002);

        if(grz!=null
				// 且场上没有污染者
				&&!board.HasCardOnBoard(Card.Cards.SC_002)
        ){
						 p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_002, new Modifier(-550));
        	AddLog("感染者出");
        }
				// 如果场上有感染者 SC_002,提高其攻击优先级
				if(board.HasCardOnBoard(Card.Cards.SC_002)){
					 p.AttackOrderModifiers.AddOrUpdate(Card.Cards.SC_002, new Modifier(350));

				}
#endregion

#region 爆虫冲锋 SC_001
        if(board.HasCardInHand(Card.Cards.SC_001)
				&&board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 
				&& card.Template.Id == Card.Cards.SC_001)
				// 手牌数+场上女王数量小于等于8
				&&board.Hand.Count+SC_003Count<=8
        ){
          p.CastSpellsModifiers.AddOrUpdate(Card.Cards.SC_001, new Modifier(-99));
        	AddLog("爆虫冲锋 -99");
        }else{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.SC_001, new Modifier(350));
				}
#endregion

#region GDB_113	气闸破损
     if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 
				&& card.Template.Id == Card.Cards.GDB_113)
        )
				{
						// 根据血量动态调整优先级，血量越低优先级越高
						int healthPriority = -(30 - board.HeroFriend.CurrentHealth) * 100 -350;
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_113, new Modifier(healthPriority));
						AddLog("气闸破损 " + healthPriority);
				}
				else
				{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_113, new Modifier(130));
				}
#endregion

#region"SC_019t", // 爆虫
      //  降低爆虫基础使用优先级
			if(board.HasCardInHand(Card.Cards.SC_019t)
			){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_019t, new Modifier(150));
				AddLog("爆虫 150");
			}
			// 遍历场上随从,如果有大于5攻的爆虫降低爆虫使用优先级
			foreach (var item in board.MinionFriend)
			{
				if(item.CurrentAtk>=5
				&&item.Template.Id==Card.Cards.SC_019t
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_019t, new Modifier(350));
					AddLog("有大于5攻的爆虫降低爆虫使用优先级");
				}
			}
#endregion

#region "SC_009"  // 潜伏者
    	int myBoardAttack = board.MinionFriend.Count(x => x.CanAttack); // 场上可攻击随从

			if(board.HasCardInHand(Card.Cards.SC_009)
			// 我方可攻击随从大于等于2
			&&myBoardAttack>=2
			){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_009, new Modifier(130));
				AddLog("潜伏者 130");
			}
#endregion

#region 星舰
       	foreach(var c in board.MinionFriend)
			{
				//check for starship
				if(GetTag(c,Card.GAME_TAG.STARSHIP) == 1 && GetTag(c,Card.GAME_TAG.LAUNCHPAD) == 1)
				{
					int StarshipAtk = c.CurrentAtk;
					int StarshipHp = c.CurrentHealth;
					AddLog("星舰攻击力 : " + StarshipAtk.ToString() + " / 星舰血量 : " + StarshipHp.ToString());
					// 如果星舰的攻击力加防御力小于10,则不发射
					if(StarshipAtk + StarshipHp < 10)
					{
						p.LocationsModifiers.AddOrUpdate(c.Id, new Modifier(-100));
						AddLog("星舰不发射"+c.Template.NameCN);
					}
					else
					{
						p.LocationsModifiers.AddOrUpdate(c.Id, new Modifier(-50));
						AddLog("星舰发射"+c.Template.NameCN);
					}
				}
				
			}
#endregion

#region 近轨血月 GDB_475
        if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 
				&& card.Template.Id == Card.Cards.GDB_475)
        ){
					// 遍历场上随从,优先对deathRattlePriorities这个里面的随从用
					foreach (var item in board.MinionFriend)
					{
						if(deathRattlePriorities.ContainsKey(item.Template.Id)){
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_475, new Modifier(deathRattlePriorities[item.Template.Id],item.Template.Id));
							AddLog("近轨血月对"+item.Template.NameCN+"使用，优先值"+deathRattlePriorities[item.Template.Id]);
						}else{
							p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_475, new Modifier(150,item.Template.Id));
							AddLog("近轨血月不对"+item.Template.NameCN+"使用，优先值"+150);
						}
					}
        }else{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_475, new Modifier(999));
					// AddLog("近轨血月 999");
				}
			 
#endregion

#region 坑道虫 SC_015
        if(board.HasCardInHand(Card.Cards.SC_015)
				// 手牌数小于等于8
				&&HandCount<=8
        ){
          p.CastSpellsModifiers.AddOrUpdate(Card.Cards.SC_015, new Modifier(-350));
        	AddLog("坑道虫 -350");
        }
#endregion

#region SC_010	跳虫
// 如果场上随从小于等于3
        if(board.HasCardInHand(Card.Cards.SC_010)
				// 场上随从数量小于等于6
				&&board.MinionFriend.Count<=5
        ){
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_010, new Modifier(350));
        	AddLog("跳虫 130");
        }else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_010, new Modifier(350));
				}
#endregion

#region 飞蛇 SC_018
// 定义一些基础条件
bool hasSC_002 = board.HasCardOnBoard(Card.Cards.SC_002); // 场上有感染者
bool hasSC_010 = board.HasCardOnBoard(Card.Cards.SC_010); // 场上有跳虫
bool hasZerglethal = alienAttack >= 5 && alienCount > 0; // 虫族致命条件
bool enemyNotFull = board.MinionEnemy.Count <= 6; // 敌方随从未满
bool canPlayFlyingSnake = board.HasCardInHand(Card.Cards.SC_018); // 手上有飞蛇
// 手里有有ETC_424 死亡嘶吼  约德尔狂吼歌手 Yelling Yodeler JAM_005
bool hasETC_424 = board.HasCardInHand(Card.Cards.ETC_424);
bool hasJAM_005 = board.HasCardInHand(Card.Cards.JAM_005);

if (canPlayFlyingSnake)
{
    // 调整跳虫攻击优先级
    if (hasSC_010)
    {
        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.SC_010, new Modifier(350));
        AddLog("降低跳虫攻击优先级");
    }

    // 主要使用逻辑
    if (enemyNotFull && hasZerglethal)
    {
        // 虫族数量越多,优先级越高
        int priority = -99 * alienAttack - alienCount * 10;
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_018, new Modifier(priority));
        AddLog($"高优先级使用飞蛇: {priority}, 虫族攻击: {alienAttack}, 虫族数量: {alienCount}");
    }
    else if (board.MinionFriend.Exists(x => x.Template.Id == Card.Cards.SC_002)
		// 手里没有约德尔狂吼歌手 Yelling Yodeler JAM_005 
		&&!hasJAM_005
		)
    {
        // 有感染者时的使用优先级
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_018, new Modifier(-350));
        AddLog("场上有感染者,提高飞蛇使用优先级");
    }
    else
    {
        // 其他情况下不主动使用
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_018, new Modifier(9999));
        AddLog("当前情况不适合使用飞蛇");
    }
}
#endregion

#region  感染者不送逻辑
if(canPlayFlyingSnake||hasETC_424||hasJAM_005)
{
		    if (hasSC_002)
    {
        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.SC_002, new Modifier(350));
        AddLog("保护场上感染者");
    }
}
#endregion

#region ETC_424 死亡嘶吼
if(board.HasCardInHand(Card.Cards.ETC_424)
    &&removeQuantity>=2
    &&!(board.HasCardInHand(Card.Cards.JAM_005)&&board.ManaAvailable>=4))
{
    foreach (var minion in board.MinionFriend)
    {
        // 如果随从在排除列表中,给予高优先级来避免使用
        if (excludedListOfDeadLanguageFollowers.Contains(minion.Template.Id))
        {
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_424, new Modifier(999, minion.Template.Id));
            // AddLog($"死亡嘶吼不对{minion.Template.NameCN}使用,在排除列表中");
            continue;  
        }

        if (deathRattleBlacklist.Contains(minion.Template.Id))
        {
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_424, new Modifier(350, minion.Template.Id));
            continue;
        }

        if (deathRattlePriorities.ContainsKey(minion.Template.Id) && GetTag(minion,Card.GAME_TAG.DEATHRATTLE) == 1)
        {
            int minionIndex = board.MinionFriend.IndexOf(minion);
            bool hasAdjacentMinion = (minionIndex > 0) || (minionIndex < board.MinionFriend.Count - 1);

            if(hasAdjacentMinion)
            {
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_424, 
                    new Modifier(deathRattlePriorities[minion.Template.Id], minion.Template.Id));
                AddLog($"死亡嘶吼对{minion.Template.NameCN}使用，优先值{deathRattlePriorities[minion.Template.Id]}");
                continue;
            }
        }
        
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_424, new Modifier(150, minion.Template.Id));
    }
}
#endregion

#region 约德尔狂吼歌手 Yelling Yodeler JAM_005 
if(board.HasCardInHand(Card.Cards.JAM_005)
    &&boardUndead>0
)
{
    foreach (var minion in board.MinionFriend)
    {
        // 如果随从在排除列表中,给予高优先级来避免使用
        if (excludedListOfDeadLanguageFollowers.Contains(minion.Template.Id))
        {
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_005, new Modifier(350, minion.Template.Id));
            // AddLog($"约德尔狂吼歌手不对{minion.Template.NameCN}使用,在排除列表中");
            continue;
        }

        if (deathRattleBlacklist.Contains(minion.Template.Id))
        {
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_005, new Modifier(350, minion.Template.Id));
            continue;
        }

        if (deathRattlePriorities.ContainsKey(minion.Template.Id) && GetTag(minion,Card.GAME_TAG.DEATHRATTLE) == 1)
        {
            int minionIndex = board.MinionFriend.IndexOf(minion);
            bool hasAdjacentMinion = (minionIndex > 0) || (minionIndex < board.MinionFriend.Count - 1);

            if(hasAdjacentMinion)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_005, 
                    new Modifier(deathRattlePriorities[minion.Template.Id], minion.Template.Id));
                AddLog($"约德尔狂吼歌手对{minion.Template.NameCN}使用，优先值{deathRattlePriorities[minion.Template.Id]}");
                continue;
            }
        }

        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_005, new Modifier(150, minion.Template.Id));
        // AddLog($"约德尔狂吼歌手不对{minion.Template.NameCN}使用，优先值150");
    }

    // 如果是死亡嘶吼随从,对其使用
    foreach(var c in board.MinionFriend.ToArray())
    {
        var ench = c.Enchantments.FirstOrDefault(x => x.EnchantCard != null && x.EnchantCard.Template.Id == Card.Cards.ETC_424);
        if(ench != null && ench.DeathrattleCard != null)
        {
            var deathrattleCardId = ench.DeathrattleCard.Template.Id;
            // 如果不在排除列表中才降低优先级
            if(!excludedListOfDeadLanguageFollowers.Contains(c.Template.Id))
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_005, new Modifier(-150, deathrattleCardId));
                AddLog($"约德尔狂吼歌手对{c.Template.NameCN}使用,优先值-150,内含死亡嘶吼");
            }
        }
    }
}else{
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_005, new Modifier(999));
}
#endregion

#region 送死
foreach (var c in board.MinionFriend.ToArray())
{
    var ench = c.Enchantments.FirstOrDefault(x => x.EnchantCard != null && x.EnchantCard.Template.Id == Card.Cards.ETC_424);
    if (ench != null) // found a card with ETC_424 buff
    {
        if (ench.DeathrattleCard != null)
        {
            // there you have the id
            var deathrattleCardId = ench.DeathrattleCard.Template.Id;
            // 提高其送死优先级
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(c.Template.Id, new Modifier(-150));
            AddLog($"提高随从 {c.Template.NameCN} 的送死优先级");
        }
    }
}
#endregion

// #region 打印场上随从id
// foreach (var item in board.MinionFriend)
// {
//     AddLog(item.Template.NameCN + ' ' + item.Template.Id);
// }
// // 打印手上的卡牌id
// foreach (var item in board.Hand)
// {
// 		AddLog(item.Template.NameCN + ' ' + item.Template.Id);
// }

// #endregion

#region 临时牌逻辑
// 判断是否为临时牌
foreach (var c in board.Hand.ToArray())
{
    var ench = c.Enchantments.FirstOrDefault(x => x.EnchantCard != null && x.EnchantCard.Template.Id == Card.Cards.SC_003t);
    if (ench != null&&c.Template.Id !=Card.Cards.SC_002)//"SC_002", // 感染者
    {
			p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, new Modifier(150));
      AddLog($"不为感染者 ,降低使用优先级{c.Template.NameCN}150");
    }
}
#endregion

#region 投降
    // 我方场攻小于5,手牌数小于等于1,牌库为空,敌方血量+护甲>20,当前费用大于10,投降
    if (HandCount<=1
        &&board.FriendDeckCount==0
        &&board.MaxMana>=10
        // 敌方血量大于20 
        &&BoardHelper.GetEnemyHealthAndArmor(board)>20
        )
    {
        Bot.Concede();
        AddLog("投降");
    }
		//如果我方场上有7个青蛙 则投降  青蛙 hexfrog

    // 定义青蛙的ID
    // var frogId = "hexfrog";

    // // 计算场上青蛙的数量
    // int frogCount = board.MinionFriend.Count(x => x.Template.Id.ToString() == frogId);

    // // 如果青蛙数量达到7个，则投降
    // if (frogCount >= 6)
    // {
    //     Bot.Concede();
		// 		AddLog("7蛙投降");
    // }
#endregion

#region 优先使用法术
// 根据cardPriority的顺序设置使用优先级
if (board.Hand.Count > 0)
{
    foreach (var card in board.Hand)
    {
        // 按照cardPriority中的顺序遍历
        foreach (var priorityCard in cardPriority)
        {
            if (card.Template.Id == priorityCard.Key)
            {
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(priorityCard.Value));
                // AddLog($"设置{card.Template.NameCN}使用优先级: {priorityCard.Value}");
            }
        }
    }
}
#endregion

#region 打印场上随从id
foreach (var item in board.MinionFriend)
{
    AddLog(item.Template.NameCN + ' ' + item.Template.Id);
}
// 打印手上的卡牌id
foreach (var item in board.Hand)
{
		AddLog(item.Template.NameCN + ' ' + item.Template.Id);
}

#endregion

#region 版本输出
                AddLog("---\n版本27.7 作者by77 Q群:943879501 DC:https://discord.gg/nn329ZU6Ss\n---");
#endregion

#region 攻击优先 卡牌威胁
var cardModifiers = new Dictionary<Card.Cards, int>
{   
			{ Card.Cards.TLC_468t1,	999 }, // 细长黏团 TLC_468t1
			{ Card.Cards.DINO_410,	999 }, // 凯洛斯的蛋 DINO_410
		{ Card.Cards.DINO_410t2,	999 }, // 凯洛斯的蛋 DINO_410t2
		{ Card.Cards.DINO_410t4,	999 }, // 凯洛斯的蛋 DINO_410t4
		{ Card.Cards.DINO_410t5,	999 }, // 凯洛斯的蛋 DINO_410t5
		{ Card.Cards.DINO_410t3,	999 }, // 凯洛斯的蛋 DINO_410t3
		{ Card.Cards.SC_671t1,	200 }, // 执政官 SC_671t1
		{ Card.Cards.EDR_849,	200 }, // 梦缚迅猛龙 EDR_849  
		{ Card.Cards.EDR_892, -100 }, // 残暴的魔蝠 EDR_892
		{ Card.Cards.EDR_891, -100 }, // 贪婪的地狱猎犬 EDR_891
		{ Card.Cards.GDB_471, 200 }, // GDB_471	沃罗尼招募官
		{ Card.Cards.VAC_501, 200 }, // 极限追逐者阿兰娜 VAC_501 
		{ Card.Cards.GDB_100, 200 }, // 阿肯尼特防护水晶 GDB_100
    { Card.Cards.EDR_815, 200},//EDR_815	尸魔花
    { Card.Cards.TOY_528, 200},//伴唱机 TOY_528
    { Card.Cards.EDR_540, 200},//EDR_540	扭曲的织网蛛
    { Card.Cards.EDR_889, 200},//EDR_889	鲜花商贩 
    { Card.Cards.VAC_503, 200},//VAC_503	召唤师达克玛洛
    { Card.Cards.EDR_816, 200},//EDR_816	怪异魔蚊
    { Card.Cards.EDR_810t, 100},//EDR_810t	饱胀水蛭
    { Card.Cards.SC_765, 200},//SC_765	高阶圣堂武士
    { Card.Cards.SC_756, 200},//SC_756	航母
    { Card.Cards.SC_003, 200},//虫巢女王 SC_003
    { Card.Cards.WW_827, 200},//雏龙牧人 WW_827
    { Card.Cards.TTN_903, 200},//生命的缚誓者艾欧娜尔 TTN_903
    { Card.Cards.TTN_960, 200},//灭世泰坦萨格拉斯 TTN_960
    { Card.Cards.TTN_862, 200},//翠绿之星阿古斯 TTN_862
    { Card.Cards.TTN_429, 200},//阿曼苏尔 TTN_429
     { Card.Cards.TTN_092, 200},//复仇者阿格拉玛 TTN_092
     { Card.Cards.TTN_075, 200},//诺甘农 TTN_075  
    
     { Card.Cards.DEEP_008, 200},//针岩图腾 DEEP_008
     { Card.Cards.CORE_RLK_121, 200},//死亡侍僧 CORE_RLK_121
     { Card.Cards.TTN_737, 200},//兵主 TTN_737
     { Card.Cards.VAC_406, 900},//VAC_406	困倦的岛民
     { Card.Cards.TTN_858, 200},//TTN_858	维和者阿米图斯
     { Card.Cards.GDB_226, 200},//GDB_226	凶恶的入侵者
     { Card.Cards.TOY_330t5, 200},//TOY_330t5 奇利亚斯豪华版3000型
    { Card.Cards.TOY_330t11, 200},//TOY_330t11 奇利亚斯豪华版3000型
     { Card.Cards.CORE_EX1_012, 200},//CORE_EX1_012 血法师萨尔诺斯
     { Card.Cards.CORE_BT_187, 200},//CORE_BT_187	凯恩·日怒
     { Card.Cards.CS2_052, 200},//空气之怒图腾 CS2_052
     { Card.Cards.WORK_040, 200 },//笨拙的杂役 WORK_040
     { Card.Cards.TOY_606, 200 },//测试假人 TOY_606   
     { Card.Cards.WW_382, 200 },//步移山丘 WW_382   
     { Card.Cards.GDB_841, 200 },//GDB_841	游侠斥候  
     { Card.Cards.GDB_110, 200 },//GDB_110	邪能动力源 
     { Card.Cards.CORE_ICC_210, 200 },//CORE_ICC_210	暗影升腾者 
     { Card.Cards.JAM_024, 200 },//JAM_024	布景光耀之子 
     { Card.Cards.CORE_CS3_014, 200 },//CORE_CS3_014	赤红教士
     
     { Card.Cards.GVG_075, 200 },//GVG_075 	船载火炮
     { Card.Cards.GDB_310, 200 },//GDB_310	虚灵神谕者 
     { Card.Cards.JAM_010, 200 },//JAM_010	点唱机图腾
     { Card.Cards.TOY_351t, -200 },//TOY_351t	神秘的蛋
    { Card.Cards.TOY_351, -200 },//TOY_351	神秘的蛋
    { Card.Cards.WW_391, 200 }, // WW_391	淘金客
    { Card.Cards.TOY_515, 200 }, // 水上舞者索尼娅 TOY_515
    { Card.Cards.CORE_TOY_100, 200 }, // 侏儒飞行员诺莉亚 CORE_TOY_100
    { Card.Cards.WW_381, 200 }, // 受伤的搬运工 WW_381
    { Card.Cards.TTN_900, -200 }, // 石心之王 TTN_900
    { Card.Cards.CORE_DAL_609, 200 }, // 卡雷苟斯 CORE_DAL_609
    { Card.Cards.TOY_646, 200 }, // 捣蛋林精 TOY_646
    { Card.Cards.TOY_357, 200 }, // 抱龙王噗鲁什 TOY_357
    { Card.Cards.VAC_507, 200 }, // 阳光汲取者莱妮莎 VAC_507
    { Card.Cards.WORK_042, 500 }, // 食肉格块 WORK_042
    { Card.Cards.WW_344, 200 }, // 威猛银翼巨龙 
    { Card.Cards.TOY_812, -5},//TOY_812 皮普希·彩蹄
    { Card.Cards.VAC_532, 200 },//椰子火炮手 VAC_532
    { Card.Cards.TOY_505, 200 },//TOY_505	玩具船
    { Card.Cards.TOY_381, 200 },//TOY_381	纸艺天使
    { Card.Cards.TOY_824, 350 }, // 黑棘针线师
    { Card.Cards.VAC_927, 200 }, // 狂飙邪魔
    { Card.Cards.VAC_938, 200 }, // 粗暴的猢狲
    { Card.Cards.ETC_355, 200 }, // 剃刀沼泽摇滚明星
    { Card.Cards.WW_091, 200 },  // 腐臭淤泥波普加
    { Card.Cards.VAC_450, 200}, // 悠闲的曲奇
    { Card.Cards.TOY_028, 200 }, // 团队之灵
    { Card.Cards.VAC_436, 350 }, // 脆骨海盗
    { Card.Cards.VAC_321, 200 }, // 伊辛迪奥斯
    { Card.Cards.TTN_800, 200 }, // 雷霆之神高戈奈斯 TTN_800
    { Card.Cards.TTN_415, 200 }, // 卡兹格罗斯
    { Card.Cards.ETC_541, 200 }, // 盗版之王托尼
    { Card.Cards.CORE_LOOT_231, 200 }, // 奥术工匠
    { Card.Cards.ETC_339, 200 }, // 心动歌手
    { Card.Cards.ETC_833, 200 }, // 箭矢工匠
    { Card.Cards.MIS_026, 200 }, // 傀儡大师多里安
    { Card.Cards.CORE_WON_065, 200 }, // 随船外科医师
    { Card.Cards.WW_357, 500 }, // 老腐和老墓
    { Card.Cards.DEEP_999t2, 200 }, // 深岩之洲晶簇
    { Card.Cards.CFM_039, 200 }, // 杂耍小鬼
    { Card.Cards.WW_364t, 200 }, // 狡诈巨龙威拉罗克
    { Card.Cards.TSC_026t, 200 }, // 可拉克的壳
    { Card.Cards.WW_415, 200 }, // 许愿井
    { Card.Cards.CS3_014, 200 }, // 赤红教士
    { Card.Cards.YOG_516, 200 }, // 脱困古神尤格-萨隆
    { Card.Cards.NX2_033, 200 }, // 巨怪塔迪乌斯
    { Card.Cards.JAM_004, 200 }, // 镂骨恶犬
    { Card.Cards.TTN_330, 200 }, // Kologarn
    { Card.Cards.TTN_729, 200 }, // Melted Maker
    { Card.Cards.TTN_812, 150 }, // Victorious Vrykul
    { Card.Cards.TTN_479, 200 }, // Flame Revenant
    { Card.Cards.TTN_732, 250 }, // Invent-o-matic
    { Card.Cards.TTN_466, 250 }, // Minotauren
    { Card.Cards.TTN_801, 350 }, // Champion of Storms
    { Card.Cards.TTN_833, 200 }, // Disciple of Golganneth
    { Card.Cards.TTN_730, 300 }, // Lab Constructor
    { Card.Cards.TTN_920, 300 }, // Mimiron, the Mastermind
    { Card.Cards.TTN_856, 200 }, // Disciple of Amitus
    { Card.Cards.TTN_907, 200 }, // Astral Serpent
    { Card.Cards.TTN_071, 200 }, // Sif
    { Card.Cards.TTN_078, 200 }, // Observer of Myths
    { Card.Cards.TTN_843, 200 }, // Eredar Deceptor
};

foreach (var cardModifier in cardModifiers)
{
    if (board.MinionEnemy.Any(minion => minion.Template.Id == cardModifier.Key))
    {
        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(cardModifier.Key, new Modifier(cardModifier.Value));
    }
}
#endregion
						 
#region Bot.Log
Bot.Log(_log);         
#endregion                       

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


//德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER
            return p;
        }}
				 // 向 _log 字符串添加日志的私有方法，包括回车和新行
        private void AddLog(string log)
        {
            _log += "\r\n" + log;
        }
        //芬利·莫格顿爵士技能选择
        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            var filteredTable = _heroPowersPriorityTable.Where(x => choices.Contains(x.Key)).ToList();
            return filteredTable.First(x => x.Value == filteredTable.Max(y => y.Value)).Key;
        }
    
        //卡扎库斯选择
        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices[0];
        }
public int CountSpecificRacesInHand(Board board)
{
    if (board?.MinionFriend == null) return 0; // 检查手牌是否为 null

    // 定义所有可能的种族
    Card.CRace[] races = new Card.CRace[]
    {
        Card.CRace.BLOODELF,
        Card.CRace.DRAENEI,
        Card.CRace.DWARF,
        Card.CRace.GNOME,
        Card.CRace.GOBLIN,
        Card.CRace.HUMAN,
        Card.CRace.NIGHTELF,
        Card.CRace.ORC,
        Card.CRace.TAUREN,
        Card.CRace.TROLL,
        Card.CRace.UNDEAD,
        Card.CRace.WORGEN,
        Card.CRace.GOBLIN2,
        Card.CRace.MURLOC,
        Card.CRace.DEMON,
        Card.CRace.SCOURGE,
        Card.CRace.MECHANICAL,
        Card.CRace.ELEMENTAL,
        Card.CRace.OGRE,
        Card.CRace.PET,
        Card.CRace.TOTEM,
        Card.CRace.NERUBIAN,
        Card.CRace.PIRATE,
        Card.CRace.DRAGON,
        Card.CRace.BLANK,
        Card.CRace.ALL,
        Card.CRace.EGG,
        Card.CRace.QUILBOAR,
        Card.CRace.CENTAUR,
        Card.CRace.FURBOLG,
        Card.CRace.HIGHELF,
        Card.CRace.TREANT,
        Card.CRace.OWLKIN,
        Card.CRace.HALFORC,
        Card.CRace.LOCK,
        Card.CRace.NAGA,
        Card.CRace.OLDGOD,
        Card.CRace.PANDAREN,
        Card.CRace.GRONN,
        Card.CRace.CELESTIAL,
        Card.CRace.GNOLL,
        Card.CRace.GOLEM,
        Card.CRace.HARPY,
        Card.CRace.VULPERA
    };

    HashSet<Card.CRace> uniqueRaces = new HashSet<Card.CRace>();

    foreach (Card card in board.MinionFriend)
    {
        if (card?.Type != Card.CType.MINION) continue; // 忽略空卡或非随从卡

        foreach (Card.CRace race in races)
        {
            if (card.IsRace(race))
            {
                uniqueRaces.Add(race);
                break; // 确保每张卡只添加一个种族
            }
        }
    }

    return uniqueRaces.Count;
}
				// 定义虫族列表
				public static List<string> ZergList = new List<string>
				{
					// 刺蛇 SC_008
						"SC_008",
						// 针脊爬虫 SC_023
						"SC_023",
						"SC_012", // 蟑螂
						"SC_019t", // 爆虫
						"SC_018", // 飞蛇
						"SC_010", // 跳虫
						"SC_003", // 虫巢女王
						"SC_002", // 感染者
						"SC_003t", // 幼虫 SC_003t
						"SC_022", // 异龙 SC_022
						"SC_009"  // 潜伏者
				};

				// 计算场上虫族的数量
				public static int GetBeastsCount(Board board)
				{
						return board.MinionFriend.Count(x => ZergList.Contains(x.Template.Id.ToString()));
				}
				// 计算场上异虫的攻击力总和
				public static int GetTotalZergAttack(Board board)
				{
						return board.MinionFriend
												.Where(x => ZergList.Contains(x.Template.Id.ToString()))
												.Sum(x => x.CurrentAtk);
				}


        //计算类
        public static class BoardHelper
        {
						
            //得到敌方的血量和护甲之和
            public static int GetEnemyHealthAndArmor(Board board)
            {
                return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
            }

            //得到自己的法强
            public static int GetSpellPower(Board board)
            {
                //计算没有被沉默的随从的法术强度之和
                return board.MinionFriend.FindAll(x => x.IsSilenced == false).Sum(x => x.SpellPower);
            }

            //获得第二轮斩杀血线
            public static int GetSecondTurnLethalRange(Board board)
            {
                //敌方英雄的生命值和护甲之和减去可释放法术的伤害总和
                return GetEnemyHealthAndArmor(board) - GetPlayableSpellSequenceDamages(board);
            }

            //下一轮是否可以斩杀敌方英雄
            public static bool HasPotentialLethalNextTurn(Board board)
            {
                //如果敌方随从没有嘲讽并且造成伤害
                //(敌方生命值和护甲的总和 减去 下回合能生存下来的当前场上随从的总伤害 减去 下回合能攻击的可使用随从伤害总和)
                //后的血量小于总法术伤害
                if (!board.MinionEnemy.Any(x => x.IsTaunt) &&
                    (GetEnemyHealthAndArmor(board) - GetPotentialMinionDamages(board) - GetPlayableMinionSequenceDamages(GetPlayableMinionSequence(board), board))
                        <= GetTotalBlastDamagesInHand(board))
                {
                    return true;
                }
                //法术释放过敌方英雄的血量是否大于等于第二轮斩杀血线
                return GetRemainingBlastDamagesAfterSequence(board) >= GetSecondTurnLethalRange(board);
            }

            //获得下回合能生存下来的当前场上随从的总伤害
            public static int GetPotentialMinionDamages(Board board)
            {
                return GetPotentialMinionAttacker(board).Sum(x => x.CurrentAtk);
            }

            //获得下回合能生存下来的当前场上随从集合
            public static List<Card> GetPotentialMinionAttacker(Board board)
            {
                //下回合能生存下来的当前场上随从集合
                var minionscopy = board.MinionFriend.ToArray().ToList();

                //遍历 以敌方随从攻击力 降序排序 的 场上敌方随从集合
                foreach (var mi in board.MinionEnemy.OrderByDescending(x => x.CurrentAtk))
                {
                    //以友方随从攻击力 降序排序 的 场上的所有友方随从集合，如果该集合存在生命值大于与敌方随从攻击力
                    if (board.MinionFriend.OrderByDescending(x => x.CurrentAtk).Any(x => x.CurrentHealth <= mi.CurrentAtk))
                    {
                        //以友方随从攻击力 降序排序 的 场上的所有友方随从集合,找出该集合中友方随从的生命值小于等于敌方随从的攻击力的随从
                        var tar = board.MinionFriend.OrderByDescending(x => x.CurrentAtk).FirstOrDefault(x => x.CurrentHealth <= mi.CurrentAtk);
                        //将该随从移除掉
                        minionscopy.Remove(tar);
                    }
                }

                return minionscopy;
            }

            //获得下回合能生存下来的对面随从集合
            public static List<Card> GetSurvivalMinionEnemy(Board board)
            {
                //下回合能生存下来的当前对面场上随从集合
                var minionscopy = board.MinionEnemy.ToArray().ToList();

                //遍历 以友方随从攻击力 降序排序 的 场上友方可攻击随从集合
                foreach (var mi in board.MinionFriend.FindAll(x => x.CanAttack).OrderByDescending(x => x.CurrentAtk))
                {
                    //如果存在友方随从攻击力大于等于敌方随从血量
                    if (board.MinionEnemy.OrderByDescending(x => x.CurrentHealth).Any(x => x.CurrentHealth <= mi.CurrentAtk))
                    {
                        //以敌方随从血量降序排序的所有敌方随从集合，找出敌方生命值小于等于友方随从攻击力的随从
                        var tar = board.MinionEnemy.OrderByDescending(x => x.CurrentHealth).FirstOrDefault(x => x.CurrentHealth <= mi.CurrentAtk);
                        //将该随从移除掉
                        minionscopy.Remove(tar);
                    }
                }
                return minionscopy;
            }

            //获取可以使用的随从集合
            public static List<Card.Cards> GetPlayableMinionSequence(Board board)
            {
                //卡片集合
                var ret = new List<Card.Cards>();

                //当前剩余的法力水晶
                var manaAvailable = board.ManaAvailable;

                //遍历以手牌中费用降序排序的集合
                foreach (var card in board.Hand.OrderByDescending(x => x.CurrentCost))
                {
                    //如果当前卡牌不为随从，继续执行
                    if (card.Type != Card.CType.MINION) continue;

                    //当前法力值小于卡牌的费用，继续执行
                    if (manaAvailable < card.CurrentCost) continue;

                    //添加到容器里
                    ret.Add(card.Template.Id);

                    //修改当前使用随从后的法力水晶
                    manaAvailable -= card.CurrentCost;
                }

                return ret;
            }

            //获取可以使用的奥秘集合
            public static List<Card.Cards> GetPlayableSecret(Board board)
            {
                //卡片集合
                var ret = new List<Card.Cards>();

                //遍历手牌中所有奥秘集合
                foreach (var card1 in board.Hand.FindAll(card => card.Template.IsSecret))
                {
                    if (board.Secret.Count > 0)
                    {
                        //遍历头上奥秘集合
                        foreach (var card2 in board.Secret.FindAll(card => CardTemplate.LoadFromId(card).IsSecret))
                        {

                            //如果手里奥秘和头上奥秘不相等
                            if (card1.Template.Id != card2)
                            {
                                //添加到容器里
                                ret.Add(card1.Template.Id);
                            }
                        }
                    }
                    else
                    { ret.Add(card1.Template.Id); }
                }
                return ret;
            }


            //获取下回合能攻击的可使用随从伤害总和
            public static int GetPlayableMinionSequenceDamages(List<Card.Cards> minions, Board board)
            {
                //下回合能攻击的可使用随从集合攻击力相加
                return GetPlayableMinionSequenceAttacker(minions, board).Sum(x => CardTemplate.LoadFromId(x).Atk);
            }

            //获取下回合能攻击的可使用随从集合
            public static List<Card.Cards> GetPlayableMinionSequenceAttacker(List<Card.Cards> minions, Board board)
            {
                //未处理的下回合能攻击的可使用随从集合
                var minionscopy = minions.ToArray().ToList();

                //遍历 以敌方随从攻击力 降序排序 的 场上敌方随从集合
                foreach (var mi in board.MinionEnemy.OrderByDescending(x => x.CurrentAtk))
                {
                    //以友方随从攻击力 降序排序 的 场上的所有友方随���集合，如果该集合存在生命值大于与敌方随从攻击力
                    if (minions.OrderByDescending(x => CardTemplate.LoadFromId(x).Atk).Any(x => CardTemplate.LoadFromId(x).Health <= mi.CurrentAtk))
                    {
                        //以友方随从攻击力 降序排序 的 场上的所有友方随从集合,找出该集合中友方随从的生命值小于等于敌方随从的攻击力的随从
                        var tar = minions.OrderByDescending(x => CardTemplate.LoadFromId(x).Atk).FirstOrDefault(x => CardTemplate.LoadFromId(x).Health <= mi.CurrentAtk);
                        //将该随从移除掉
                        minionscopy.Remove(tar);
                    }
                }

                return minionscopy;
            }

            //获取当前回合手牌中的总法术伤害
            public static int GetTotalBlastDamagesInHand(Board board)
            {
                //从手牌中找出法术伤害表存在的法术的伤害总和(包括法强)
                return
                    board.Hand.FindAll(x => _spellDamagesTable.ContainsKey(x.Template.Id))
                        .Sum(x => _spellDamagesTable[x.Template.Id] + GetSpellPower(board));
            }

            //获取可以使用的法术集合
            public static List<Card.Cards> GetPlayableSpellSequence(Board board)
            {
                //卡片集合
                var ret = new List<Card.Cards>();

                //当前剩余的法力水晶
                var manaAvailable = board.ManaAvailable;

                if (board.Secret.Count > 0)
                {
                    //遍历以手牌中费用降序排序的集合
                    foreach (var card in board.Hand.OrderBy(x => x.CurrentCost))
                    {
                        //如果手牌中又不在法术序列的法术牌，继续执行
                        if (_spellDamagesTable.ContainsKey(card.Template.Id) == false) continue;

                        //当前法力值小于卡牌的费用，继续执行
                        if (manaAvailable < card.CurrentCost) continue;

                        //添加到容器里
                        ret.Add(card.Template.Id);

                        //修改当前使用随从后的法力水晶
                        manaAvailable -= card.CurrentCost;
                    }
                }
                else if (board.Secret.Count == 0)
                {
                    //遍历以手牌中费用降序排序的集合
                    foreach (var card in board.Hand.FindAll(x => x.Type == Card.CType.SPELL).OrderBy(x => x.CurrentCost))
                    {
                        //如果手牌中又不在法术序列的法术牌，继续执行
                        if (_spellDamagesTable.ContainsKey(card.Template.Id) == false) continue;

                        //当前法力值小于卡牌的费用，继续执行
                        if (manaAvailable < card.CurrentCost) continue;

                        //添加到容器里
                        ret.Add(card.Template.Id);

                        //修改当前使用随从后的法力水晶
                        manaAvailable -= card.CurrentCost;
                    }
                }

                return ret;
            }
            
            //获取存在于法术列表中的法术集合的伤害总和(包括法强)
            public static int GetSpellSequenceDamages(List<Card.Cards> sequence, Board board)
            {
                return
                    sequence.FindAll(x => _spellDamagesTable.ContainsKey(x))
                        .Sum(x => _spellDamagesTable[x] + GetSpellPower(board));
            }

            //得到可释放法术的伤害总和
            public static int GetPlayableSpellSequenceDamages(Board board)
            {
                return GetSpellSequenceDamages(GetPlayableSpellSequence(board), board);
            }
            
            //计算在法术释放过敌方英雄的血量
            public static int GetRemainingBlastDamagesAfterSequence(Board board)
            {
                //当前回合总法术伤害减去可释放法术的伤害总和
                return GetTotalBlastDamagesInHand(board) - GetPlayableSpellSequenceDamages(board);
            }

            public static bool IsOutCastCard(Card card, Board board)
            {
                var OutcastLeft = board.Hand.Find(x => x.CurrentCost >= 0);
                var OutcastRight = board.Hand.FindLast(x => x.CurrentCost >= 0);
                if (card.Template.Id == OutcastLeft.Template.Id
                    || card.Template.Id == OutcastRight.Template.Id)
                {
                    return true;
                    
                }
                return false;
            }
            public static bool IsGuldanOutCastCard(Card card, Board board)
            {
                if ((board.FriendGraveyard.Exists(x => CardTemplate.LoadFromId(x).Id == Card.Cards.BT_601)
                    && card.Template.Cost - card.CurrentCost == 3))
                {
                    return true;
                }
                
                return false;
            }
            public static bool  IsOutcast(Card card,Board board)
            {
                if(IsOutCastCard(card,board) || IsGuldanOutCastCard(card,board))
                {
                    return true;
                }
                return false;
            }


            //在没有法术的情况下有潜在致命的下一轮
            public static bool HasPotentialLethalNextTurnWithoutSpells(Board board)
            {
                if (!board.MinionEnemy.Any(x => x.IsTaunt) &&
                    (GetEnemyHealthAndArmor(board) -
                     GetPotentialMinionDamages(board) -
                     GetPlayableMinionSequenceDamages(GetPlayableMinionSequence(board), board) <=
                     0))
                {
                    return true;
                }
                return false;
            }
        }
    }
}