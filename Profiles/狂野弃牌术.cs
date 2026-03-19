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

            if (ProfileCommon.TryRunPureLearningPlayExecutor(board, p))
                return p;

            try
            {
                int enemyHealth = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                int friendHealth = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                AddLog("================ 狂野弃牌术 决策日志 v" + ProfileVersion + " ================");
                AddLog("敌方血甲: " + enemyHealth + " | 我方血甲: " + friendHealth + " | 法力:" + board.ManaAvailable + " | 手牌:" + board.Hand.Count + " | 牌库:" + board.FriendDeckCount);
                AddLog("手牌: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
                    .Select(x => (string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN) + "(" + x.Template.Id + ")" + x.CurrentCost)));
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

                           // 友方随从数量
            int friendCount = board.MinionFriend.Count;
            // 手牌数量
            int HandCount = board.Hand.Count;
            // 可以扔的牌
           var canThrowIds = new[]
					{
							Card.Cards.KAR_205,    // 镀银魔像
							Card.Cards.WON_098,    // 镀银魔像 WON_098
							Card.Cards.SCH_147,    // 骨网之卵
							Card.Cards.RLK_532,    // 行尸
							Card.Cards.BT_300,     // 古尔丹之手
							Card.Cards.RLK_534,    // 灵魂弹幕
							Card.Cards.UNG_836,    // 萨瓦丝女王
							Card.Cards.CORE_TRL_252, // 高阶祭司耶克里克
							Card.Cards.REV_239,    // 窒息暗影
							Card.Cards.AT_022      // 加拉克苏斯之拳
					};
					int canThrow = board.Hand.Count(x => canThrowIds.Contains(x.Template.Id));
				AddLog("可以扔的牌: " + canThrow);
					var discardAssemblyIds = new[]
					{
							Card.Cards.REV_240,    // 篡改卷宗
							Card.Cards.EX1_308,    // 灵魂之火
							Card.Cards.CORE_OG_109,// 夜色镇图书管理员
							Card.Cards.EX1_306,    // 魔犬
							Card.Cards.BT_301,     // 夜影主母
							Card.Cards.EX1_310,    // 末日守卫
							Card.Cards.RLK_533,    // 天灾补给
							Card.Cards.WON_103,    // 维希度斯的窟穴
							Card.Cards.DEEP_027,   // 暗石守卫
							Card.Cards.DMF_119,    // 邪恶低语
							Card.Cards.RLK_535,  // 野蛮的伊米亚人
							Card.Cards.ULD_163     // 过期货物专卖商 ULD_163
					};
					var discardAssemblyInHand = board.Hand.Count(x => discardAssemblyIds.Contains(x.Template.Id));
				AddLog("弃牌启动: " + discardAssemblyInHand);

                    // 规则：当本回合已经用过英雄技能、且手里没有任何弃牌启动组件时，允许把“被弃组件”当普通牌打出去（不再强行捂手等弃牌）。
                    bool heroPowerAlreadyUsedThisTurn = false;
                    try
                    {
                        // 英雄技能用过后通常会带 EXHAUSTED 标签
                        heroPowerAlreadyUsedThisTurn = board.Ability != null && GetTag(board.Ability, Card.GAME_TAG.EXHAUSTED) == 1;
                    }
                    catch
                    {
                        heroPowerAlreadyUsedThisTurn = false;
                    }
                    bool noDiscardStarterInHand = discardAssemblyInHand == 0;
                    bool allowUseDiscardPayoffsAsNormal = heroPowerAlreadyUsedThisTurn && noDiscardStarterInHand;
				AddLog("英雄技能已用: " + heroPowerAlreadyUsedThisTurn + " | 无弃牌启动: " + noDiscardStarterInHand + " | 放手被弃组件: " + allowUseDiscardPayoffsAsNormal);
            // 是否有可以扔牌的组件
            int spiritualRealmMorigan=board.Hand.Count(x => x.Template.Id == Card.Cards.BOT_568)//莫瑞甘的灵界 BOT_568 
                +board.Hand.Count(x => x.Template.Id == Card.Cards.LOOT_014)//狗头人图书管理员 LOOT_014
                +board.Hand.Count(x => x.Template.Id == Card.Cards.REV_240);//篡改卷宗 REV_240
            // 过期货物专卖商 Expired Merchant ULD_163 
            var ExpiredMerchant = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.ULD_163);
            // 玛克扎尔的小鬼 Malchezaar's Imp ID：KAR_089 
            var Malchezaar = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.KAR_089);
            // 小鬼骑士 Tiny Knight of Evil ID：CORE_AT_021 
            var TinyKnight = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.CORE_AT_021);
            // 幸运币
            var luckyCoin=board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.REV_COIN2);
 #endregion

#region 不送的怪
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); //奇迹推销员 WW_331
#endregion

#region 临时牌逻辑
// 判断是否为临时牌
foreach (var c in board.Hand.ToArray())
{
    var ench = c.Enchantments.FirstOrDefault(x => x.EnchantCard != null);
    if (ench != null)
    {
			// p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, new Modifier(150));
					AddLog("临时牌" + c.Template.NameCN);
    }
}
#endregion

#region 栉龙 TLC_603
						// 场上有栉龙 TLC_603,提高其送死优先级
				if(board.HasCardOnBoard(Card.Cards.TLC_603)
				&&!board.HasCardInHand(Card.Cards.TLC_603)
				){
					p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-150));
					AddLog("栉龙 TLC_603 -150");
				}
					p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(999));
#endregion

#region 莫瑞甘的灵界 CORE_BOT_568
				if(board.HasCardInHand(Card.Cards.CORE_BOT_568)
				// 手牌数小于等于8
				&&board.Hand.Count<=8
				// 可用费用大于等于2
				&&board.ManaAvailable >= 3
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BOT_568, new Modifier(-150));
					AddLog("莫瑞甘的灵界 CORE_BOT_568 -150");
				}
#endregion

#region 暗石守卫 DEEP_027
					p.ChoicesModifiers.AddOrUpdate(Card.Cards.DEEP_027, new Modifier(-250, 1 ));
				if(board.HasCardInHand(Card.Cards.DEEP_027)){
					// p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DEEP_027, new Modifier(-150));
					p.ForgeModifiers.AddOrUpdate(Card.Cards.DEEP_027, new Modifier(350));
				}
				// 如果可丢弃的牌小于2,降低使用优先级
				if(canThrow<2
				&&board.HasCardInHand(Card.Cards.DEEP_027)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_027, new Modifier(350));
					AddLog("暗石守卫 DEEP_027 使用优先级降低");
				}
#endregion

#region 咒怨之墓 TLC_451
				if(board.HasCardInHand(Card.Cards.TLC_451)
				// 费用大于等于2
				&&board.ManaAvailable >= 2
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-150));
					AddLog("咒怨之墓 TLC_451");
				}
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(3999));
#endregion

#region 维希度斯的窟穴 WON_103
                // 场上窟穴是否“本回合可用”（未 EXHAUSTED）。可用时应优先点窟穴，再考虑生命分流，避免抽牌后增加随机弃牌风险。
                Card caveOnBoard = null;
                bool caveReadyNow = false;
                try
                {
                    caveOnBoard = board.MinionFriend.FirstOrDefault(x => x != null && x.Template != null && x.Template.Id == Card.Cards.WON_103);
                    // Location 通常也会使用 EXHAUSTED 标记是否已使用
                    caveReadyNow = caveOnBoard != null && GetTag(caveOnBoard, Card.GAME_TAG.EXHAUSTED) != 1;
                }
                catch
                {
                    caveOnBoard = null;
                    caveReadyNow = false;
                }

                bool shouldUseCaveBeforeTap = caveReadyNow
                    && canThrow > 0
                    && board.Hand.Count <= 9
                    && board.FriendDeckCount > 0;
                if (shouldUseCaveBeforeTap)
                {
                    // 强化“点窟穴”的倾向
                    p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-650));
                    // 抑制“先抽一口”
                    p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_07bp, new Modifier(250));
                    AddLog("窟穴可用：优先点窟穴，禁止先分流");
                }

				if(board.HasCardOnBoard(Card.Cards.WON_103)
				// 手上有可弃的牌
				&&canThrow>0
				// 手牌数小于等于9
				&&board.Hand.Count<=9
				// 牌库不为空
				&&board.FriendDeckCount > 0
				){
                    // 正常情况下也希望多点窟穴；若上面判定为“本回合可用且应优先”，这里会被 -650 覆盖。
                    p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-350));
					AddLog("维希度斯的窟穴使用-350");
				}
				if(board.HasCardInHand(Card.Cards.WON_103)
				// 友方随从小于等于6
				&&friendCount <= 6
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-150));
					p.LocationsModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(-150));
					AddLog("维希度斯的窟穴出-150");
				}
            // 窟穴属于关键引擎牌：通常不需要强后置。
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_103, new Modifier(999));
#endregion

#region 生命分流 Life Tap   HERO_07bp
            // 注意：上面的“窟穴可用：禁止先分流”会把这里的权重覆盖成更保守的 250。
            if((board.FriendDeckCount >0
            // &&board.Hand.Count<10
            &&!board.Hand.Exists(x=>x.CurrentCost==1 && x.Template.Id==Card.Cards.BT_300)
            &&(discardAssemblyInHand+board.Hand.Count(x => x.Template.Id == Card.Cards.ULD_163)==0))||(board.MaxMana==2&&board.Hand.Count(x=>x.CurrentCost==2)==0)
            ){
                p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_07bp, new Modifier(55));
            }else{
                p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_07bp, new Modifier(85));
            }
#endregion

#region 玛克扎尔的小鬼 Malchezaar's Imp ID：KAR_089 
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.KAR_089, new Modifier(4999));
           if(Malchezaar!=null
            &&discardAssemblyInHand>0
            &&board.FriendDeckCount >0
            &&!board.HasCardOnBoard(Card.Cards.KAR_089)    
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.KAR_089, new Modifier(-350));
            AddLog("玛克扎尔的小鬼 -350");
            }
						// 场上有小鬼 不送
						if(board.HasCardOnBoard(Card.Cards.KAR_089)
						// 牌库大于0
						&&board.FriendDeckCount > 0
						){
								p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.KAR_089, new Modifier(350));
								AddLog("玛克扎尔的小鬼 不送");
						}
						if(board.HasCardOnBoard(Card.Cards.KAR_089)
						// 牌库大于0
						&&board.FriendDeckCount <= 0
						){
								p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.KAR_089, new Modifier(-350));
								AddLog("玛克扎尔的小鬼 送");
						}

#endregion

#region 过期货物专卖商 Expired Merchant ULD_163 
             if(board.HasCardOnBoard(Card.Cards.ULD_163) 
             &&board.Hand.Count<=8)
            {
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-550)); 
						p.AttackOrderModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(999));
            AddLog("过期货物专卖商,送掉 -550");
            }
					// 遍历手牌,如果手上费用最高的牌包含	canThrowIds其中,则提高过期商贩使用优先级
						if (board.HasCardInHand(Card.Cards.ULD_163))
						{
								// 找出手牌中的最高费用
								int maxCost = board.Hand.Max(x => x.CurrentCost);
								// 找出所有最高费用的牌
								var highestCostCards = board.Hand.Where(x => x.CurrentCost == maxCost);
								// 如果这些牌中有任意一张在 canThrowIds 里
								if (highestCostCards.Any(x => canThrowIds.Contains(x.Template.Id)))
								{
										p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_163, new Modifier(-350));
										AddLog("过期货物专卖商 ULD_163 -350（最高费用可弃牌)");
								}
						}

#endregion

#region 灵魂之火 EX1_308
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-500));
            if(board.HasCardInHand(Card.Cards.EX1_308)
            &&canThrow>=2
            ){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-99));
						AddLog("灵魂之火 EX1_308 -99");
            }else if(board.HasCardInHand(Card.Cards.EX1_308)){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(130));
            }else if(board.MaxMana >= 3){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(-5));
            }
#endregion

 #region 莫瑞甘的灵界 BOT_568
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.BOT_568, new Modifier(999));
            if(board.HasCardInHand(Card.Cards.BOT_568) 
            &&board.FriendDeckCount >0
            // &&board.Hand.Count<=9
            // &&(board.ManaAvailable>=4)
            &&((board.MaxMana >= 3)||board.MaxMana >= 4)
            // &&((Malchezaar!=null||board.HasCardOnBoard(Card.Cards.KAR_089)))
            ){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.BOT_568, new Modifier(-150));
            AddLog("莫瑞甘的灵界 -150");
            }
#endregion

#region 镀银魔像 Silverware Golem ID：KAR_205  
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.KAR_205, new Modifier(-500));
             if(board.HasCardInHand(Card.Cards.KAR_205)
             &&board.FriendDeckCount >0
            &&(discardAssemblyInHand>0||board.Hand.Exists(x=>x.CurrentCost<=3 && x.Template.Id==Card.Cards.REV_240)||spiritualRealmMorigan>0)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.KAR_205, new Modifier(999));
            }else{
                // 已用英雄技能且没有弃牌启动：不要再捂镀银魔像，直接当节奏随从打
                if (allowUseDiscardPayoffsAsNormal)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.KAR_205, new Modifier(-60));
                    AddLog("镀银魔像 KAR_205 放手出牌 -60");
                }
                else
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.KAR_205, new Modifier(350));
                }
            }
#endregion

#region 镀银魔像 WON_098
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_098, new Modifier(-500));
             if(board.HasCardInHand(Card.Cards.WON_098)
             &&board.FriendDeckCount >0
            &&(discardAssemblyInHand>0||board.Hand.Exists(x=>x.CurrentCost<=3 && x.Template.Id==Card.Cards.REV_240)||spiritualRealmMorigan>0)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_098, new Modifier(999));
            }else{
				if (allowUseDiscardPayoffsAsNormal)
				{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_098, new Modifier(-60));
					AddLog("镀银魔像 WON_098 放手出牌 -60");
				}
				else
				{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_098, new Modifier(350));
				}
            }
#endregion

#region 骨网之卵 Boneweb Egg ID：SCH_147 
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.SCH_147, new Modifier(-500));
            if(board.HasCardInHand(Card.Cards.SCH_147)
            &&board.FriendDeckCount >0
            &&(discardAssemblyInHand>0||board.Hand.Exists(x=>x.CurrentCost<=3 && x.Template.Id==Card.Cards.REV_240)||spiritualRealmMorigan>0)
            // &&board.Hand.Count<=8
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SCH_147, new Modifier(9999));
            // AddLog("骨网之卵不出");
            }else{
				if (allowUseDiscardPayoffsAsNormal)
				{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SCH_147, new Modifier(-40));
					AddLog("骨网之卵 SCH_147 放手出牌 -40");
				}
				else
				{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SCH_147, new Modifier(350));
				}
            }
#endregion

#region 行尸 Walking Dead ID：RLK_532   
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_532, new Modifier(-500));
            if(board.HasCardInHand(Card.Cards.RLK_532)
            &&board.FriendDeckCount >0
            &&(discardAssemblyInHand>0||board.Hand.Exists(x=>x.CurrentCost<=3 && x.Template.Id==Card.Cards.REV_240)||spiritualRealmMorigan>0)
            // &&board.Hand.Count<=8
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_532, new Modifier(9999));
            // AddLog("行尸不出");
            }else{
				if (allowUseDiscardPayoffsAsNormal)
				{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_532, new Modifier(-40));
					AddLog("行尸 RLK_532 放手出牌 -40");
				}
				else
				{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.RLK_532, new Modifier(350));
				}
            }
#endregion

#region 灵魂弹幕 RLK_534 
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-999));
            if(board.HasCardInHand(Card.Cards.RLK_534)
            &&board.FriendDeckCount >0
            &&(discardAssemblyInHand+board.Hand.Count(x => x.Template.Id == Card.Cards.ULD_163)+board.Hand.Count(x => x.Template.Id == Card.Cards.LOOT_417)>0||board.Hand.Exists(x=>x.CurrentCost<=3 && x.Template.Id==Card.Cards.REV_240)||spiritualRealmMorigan>0)
            ){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(9999));
            // AddLog("灵魂弹幕9999");
            }else{
				if (allowUseDiscardPayoffsAsNormal)
				{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(-35));
					AddLog("灵魂弹幕 RLK_534 放手可用 -35");
				}
				else
				{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.RLK_534, new Modifier(350));
				}
            }
#endregion

 #region 加拉克苏斯之拳 AT_022
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(-999));
            if(board.HasCardInHand(Card.Cards.AT_022)
            &&board.FriendDeckCount >0
           &&(discardAssemblyInHand+board.Hand.Count(x => x.Template.Id == Card.Cards.ULD_163)+board.Hand.Count(x => x.Template.Id == Card.Cards.LOOT_417)>0||board.Hand.Exists(x=>x.CurrentCost<=3 && x.Template.Id==Card.Cards.REV_240)||spiritualRealmMorigan>0)
            ){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(9999));
            }else{
				if (allowUseDiscardPayoffsAsNormal)
				{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(-35));
					AddLog("加拉克苏斯之拳 AT_022 放手可用 -35");
				}
				else
				{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.AT_022, new Modifier(350));
				}
            }
#endregion

#region 小鬼骑士 Tiny Knight of Evil ID：CORE_AT_021 小鬼骑士 AT_021 小鬼骑士 WON_099
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_AT_021, new Modifier(4999));
             if(board.HasCardInHand(Card.Cards.CORE_AT_021)
             &&((board.MaxMana >= 2&&board.MinionEnemy.Count==0)||board.MaxMana >= 4)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_AT_021, new Modifier(5));
            AddLog("小鬼骑士5");
            }else{
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_AT_021, new Modifier(150));
            }
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.AT_021, new Modifier(4999));
             if(board.HasCardInHand(Card.Cards.AT_021)
             &&((board.MaxMana >= 2&&board.MinionEnemy.Count==0)||board.MaxMana >= 4)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.AT_021, new Modifier(5));
            AddLog("小鬼骑士5");
            }else{
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.AT_021, new Modifier(150));
            }
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_099, new Modifier(4999));
             if(board.HasCardInHand(Card.Cards.WON_099)
             &&((board.MaxMana >= 2&&board.MinionEnemy.Count==0)||board.MaxMana >= 4)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_099, new Modifier(5));
            AddLog("小鬼骑士5");
            }else{
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WON_099, new Modifier(150));
            }
#endregion

#region 狗头人图书管理员 Kobold Librarian ID：LOOT_014 
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(999));
            if(board.HasCardInHand(Card.Cards.LOOT_014)
            &&board.FriendDeckCount >0
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.LOOT_014, new Modifier(-150));
            AddLog("狗头人图书管理员 -150");
            }
#endregion

#region 邪恶低语 Wicked Whispers DMF_119 
p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DMF_119, new Modifier(-50));
if(board.HasCardInHand(Card.Cards.DMF_119)
// 友方随从大于等于1
&& board.MinionFriend.Count >= 2
){
    // 排除掉邪恶低语和幸运币
    var filteredHand = board.Hand.Where(x => x.Template.Id != Card.Cards.DMF_119 && x.Template.Id != Card.Cards.GAME_005).ToList();
    if (filteredHand.Any())
    {
        int minCost = filteredHand.Min(x => x.CurrentCost);
        var lowestCostCards = filteredHand.Where(x => x.CurrentCost == minCost).ToList();
						AddLog("最低费用的牌: " + string.Join(", ", lowestCostCards.Select(x => x.Template.NameCN)));
        // 检查最低费用牌是否在弃牌堆里
        if (lowestCostCards.Any(x => canThrowIds.Contains(x.Template.Id)))
        {
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DMF_119, new Modifier(-350));
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-350));
            AddLog("邪恶低语 DMF_119 -350（最低费用可弃牌）");
        }
				// 如果场上随从大于等于3,提高使用优先级
				else if (board.MinionFriend.Count >= 3)
				{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DMF_119, new Modifier(-150));
						AddLog("邪恶低语 DMF_119 -150（场上随从大于等于3）");
				}
		}
}
#endregion

#region GAME_005
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-55));
#endregion

#region 投降
    // 我方场攻小于5,手牌数小于等于1,牌库为空,敌方血量+护甲>20,当前费用大于10,投降
    if (HandCount<=1
        &&board.FriendDeckCount==0
        &&board.MaxMana>=10
        // 敌方血量大于20 
        &&BoardHelper.GetEnemyHealthAndArmor(board)>10
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

#region 重新思考
// 定义需要排除的卡牌集合

var excludedCards = new HashSet<string>
{
    Card.Cards.GAME_005.ToString(),
		// 蛮鱼挑战者 TLC_251
		Card.Cards.TLC_251.ToString()
};
foreach (var card in board.Hand)
{
    if (!excludedCards.Contains(card.Template.Id.ToString()))
    {
        p.ForcedResimulationCardList.Add(card.Template.Id);
    }
}


// 逻辑后续 - 重新思考
// if (p.ForcedResimulationCardList.Any())
// {
    // 如果支持日志系统，使用框架的日志记录方法
    // AddLog("需要重新评估策略的卡牌数: " + p.ForcedResimulationCardList.Count);

    // 如果没有日志系统，可以直接处理策略
    // 替代具体的重新思考逻辑
// }
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
            ApplyLiveMemoryBiasCompat(board, p);
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
        private static bool ApplyLiveMemoryBiasCompat(Board board, ProfileParameters p)
        {
            return false;
        }

        private double GetWinRateFromData(Card.CClass enemyClass)
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
                default: return 50.0;
            }
        }

        private double GetUsageRateFromData(Card.CClass enemyClass)
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

        private int CalculateAggroModifier(double baseAggro, int baseValue, Card.CClass enemyClass, int myHealth, int enemyHealth, int myAttack, int enemyAttack)
        {
            double winRateModifier = (GetWinRateFromData(enemyClass) - 50) * 2.5;
            double usageRateModifier = GetUsageRateFromData(enemyClass) * 0.9;
            double healthDifferenceModifier = (myHealth - enemyHealth) * 1.2;
            double boardControlModifier = (myAttack - enemyAttack) * 1.3;
            double totalModifier = winRateModifier + usageRateModifier + healthDifferenceModifier + boardControlModifier;
            int finalAggro = (int)(baseAggro * 0.6 + baseValue + totalModifier - 50);
            AddLog("職業: " + enemyClass + ", 攻擊值: " + finalAggro + ", 勝率修正: " + winRateModifier + ", 使用率修正: " + usageRateModifier + ", 血量修正: " + healthDifferenceModifier + ", 場面修正: " + boardControlModifier);
            return finalAggro;
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
