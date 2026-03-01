using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API;

namespace SmartBotProfiles
{
	[Serializable]
	public class STDAuraPaladin : Profile
	{
		private const string ProfileVersion = "2026-01-17.025";
		private const bool DebugDumpHeroEnchantments = true;
		private string _log = "";

		private static Dictionary<Card.GAME_TAG, int> _lastHeroTags;
		private static List<string> _lastHeroEnchantSig;
		private static int? _lastAuraEstimate;

		private class AuraInstance
		{
			public Card.Cards AuraId;
			public int RemainingOwnTurns;
		}

		// 由于当前 SmartBot Board API 并未直接暴露“英雄当前挂了几个光环(含剩余回合)”的稳定字段，
		// 这里用“实时跟踪器”推断：
		// - 通过 FriendGraveyardTurn 增量识别本回合新打出的光环（法术施放会进坟场）
		// - 每到我方新回合，将所有已记录光环的剩余回合 -1，<=0 即过期移除
		private static readonly List<AuraInstance> _activeAuraInstances = new List<AuraInstance>();
		private static Dictionary<Card.Cards, int> _lastFriendGraveyardTurnAuraCounts = new Dictionary<Card.Cards, int>();
		private static int? _lastAuraTickOwnTurn;
		private static int _cardboardGolemPlayedCount;
		private static int? _cardsPlayedThisTurnTurn;
		private static int _cardsPlayedThisTurnProcessedCount;

		// ===== 牌表（来自 卡组攻略和修改记录/标准/光环骑/光环骑.md） =====
		private static readonly Card.Cards Wisp = Card.Cards.CORE_CS2_231;                 // 小精灵
		private static readonly Card.Cards RighteousProtector = Card.Cards.CORE_ICC_038;    // 正义保护者
		private static readonly Card.Cards AccelerationAura = Card.Cards.END_011;           // 加速光环
		private static readonly Card.Cards PurpleGillsMurloc = Card.Cards.TLC_438;          // 紫色珍鳃鱼人
		private static readonly Card.Cards CardboardGolem = Card.Cards.TOY_809;             // 纸板魔像
		private static readonly Card.Cards GnomeAuraHeal = Card.Cards.TIME_009t1;           // 侏儒光环（可交易/回合结束全体回血）
		private static readonly Card.Cards CrusaderAura = Card.Cards.TTN_908;               // 十字军光环
		private static readonly Card.Cards Elise = Card.Cards.TLC_100;                      // 导航员伊莉斯
		private static readonly Card.Cards GrizzlyHammer = Card.Cards.EDR_253;              // 巨熊之槌
		private static readonly Card.Cards TimeFlowElemental = Card.Cards.TIME_019;         // 时间流具象
		private static readonly Card.Cards Stheno = Card.Cards.EDR_856;                     // 梦魇之王萨维斯（文件里叫萨维斯）
		private static readonly Card.Cards PlushTiger = Card.Cards.TOY_811;                 // 绒绒虎
		private static readonly Card.Cards PlushTigerToken = Card.Cards.TOY_811t;           // 绒绒虎(衍生物/1费)
		private static readonly Card.Cards Dorian = Card.Cards.MIS_026;                     // 傀儡大师多里安
		private static readonly Card.Cards Torres = Card.Cards.EDR_258;                     // 坚韧的托雷斯
		private static readonly Card.Cards TimeAura = Card.Cards.TIME_700;                  // 时序光环
		private static readonly Card.Cards MechatokAura = Card.Cards.TIME_009t2;            // 梅卡托克的光环（可交易/回合结束+4/+4圣盾）
		private static readonly Card.Cards Nolia = Card.Cards.CORE_TOY_100;                 // 侏儒飞行员诺莉亚
		private static readonly Card.Cards ArtisanAura = Card.Cards.TOY_808;                // 工匠光环
		private static readonly Card.Cards Anachronos = Card.Cards.CORE_RLK_919;            // 阿纳克洛斯
		private static readonly Card.Cards Gelbin = Card.Cards.TIME_009;                    // 明日巨匠格尔宾
		private static readonly Card.Cards Zilliax = Card.Cards.TOY_330;                    // 奇利亚斯豪华版3000型
		private static readonly Card.Cards TheCoin = Card.Cards.GAME_005;                   // 幸运币

		private static readonly HashSet<Card.Cards> AuraCards = new HashSet<Card.Cards>
		{
			AccelerationAura,
			CrusaderAura,
			GnomeAuraHeal,
			TimeAura,
			MechatokAura,
			ArtisanAura,
		};

		private static readonly HashSet<Card.Cards> KnownTradableCards = new HashSet<Card.Cards>
		{
			GnomeAuraHeal,
			MechatokAura,
		};

		public ProfileParameters GetParameters(Board board)
		{
			_log = "";
			var p = new ProfileParameters(BaseProfile.Rush)
			{
				DiscoverSimulationValueThresholdPercent = -10
			};
			List<int> forcedComboEntityIds = null;

			// ===== 头部复盘信息 =====
			int? turnNow = null;
			try { turnNow = TryGetIntProperty(board, "Turn", "TurnNumber", "CurrentTurn", "TurnCount", "GameTurn"); } catch { /* ignore */ }

			try
			{
				AddLog($"================ 标准光环骑 决策日志 v{ProfileVersion} ================");
				var enemyHealthArmor = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
				var friendHealthArmor = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
				AddLog($"对手职业: {board.EnemyClass} | 回合: {(turnNow.HasValue ? turnNow.Value.ToString() : "?")} | 法力: {board.ManaAvailable}");
				AddLog($"我方场随从:{board.MinionFriend.Count} | 敌方场随从:{board.MinionEnemy.Count} | 手牌:{board.Hand.Count} | 牌库:{board.FriendDeckCount}");
				AddLog("手牌: " + string.Join(", ", board.Hand.Where(x => x?.Template != null)
					.Select(x => $"{GetName(x.Template.Id)}({x.Template.Id}){x.CurrentCost}")));
				AddLog("我方场: " + string.Join(", ", board.MinionFriend.Where(x => x?.Template != null)
					.Select(x => $"{GetName(x.Template.Id)}({x.Template.Id}) {x.CurrentAtk}/{x.CurrentHealth} 可攻={x.CanAttack}")));
				AddLog("敌方场: " + string.Join(", ", board.MinionEnemy.Where(x => x?.Template != null)
					.Select(x => $"{GetName(x.Template.Id)}({x.Template.Id}) {x.CurrentAtk}/{x.CurrentHealth}")));
			}
			catch
			{
				// ignore
			}

			// ===== 对局节奏：根据职业粗略估计快慢，决定更偏站场 or 偏资源 =====
			bool isFastOpponent = IsFastOpponent(board.EnemyClass);
			bool isSlowOpponent = IsSlowOpponent(board.EnemyClass);

			// GlobalAggroModifier：越高越激进
			if (isFastOpponent)
			{
				p.GlobalAggroModifier = 0;
				AddLog("对局节奏：对手偏快 -> 降低激进度（先稳血/保场）");
			}
			else if (isSlowOpponent)
			{
				p.GlobalAggroModifier = 80;
				AddLog("对局节奏：对手偏慢 -> 提高激进度（抢费+光环滚雪球）");
			}
			else
			{
				p.GlobalAggroModifier = 40;
				AddLog("对局节奏：未知 -> 中等激进度");
			}

			// ===== 常用局面信息 =====
			int friendHealth = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
			int enemyHealth = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
			int friendMinions = board.MinionFriend.Count;
			int enemyMinions = board.MinionEnemy.Count;
			int canAttackMinions = board.MinionFriend.Count(m => m.CanAttack);
			bool hasCoin = board.HasCardInHand(TheCoin);
			var reinforce = Card.Cards.HERO_04bp;
			bool hasHammerEquipped = board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.Template.Id == GrizzlyHammer;
			bool hasHammerInHand = board.HasCardInHand(GrizzlyHammer);
			bool hasTradableInHand = false;
			try
			{
				hasTradableInHand = board.Hand.Any(c =>
					c?.Template != null &&
					(KnownTradableCards.Contains(c.Template.Id) || (TryGetBoolProperty(c.Template, "Tradable", "IsTradable", "Tradeable", "IsTradeable") ?? false)));
			}
			catch
			{
				hasTradableInHand = false;
			}
			bool wantDorianComboLine = board.HasCardInHand(Dorian) && board.ManaAvailable >= 5 && (hasHammerEquipped || hasHammerInHand || hasTradableInHand);
			int auraInHandCount = AuraCards.Count(a => board.HasCardInHand(a));
			bool gelbinPlayableNow = board.HasCardInHand(Gelbin) && board.ManaAvailable >= 8;
			bool shouldPlayGelbinBeforeAttacks = gelbinPlayableNow && canAttackMinions > 0;
			int activeHeroAuraCount = GetActiveHeroAuraCount(board, turnNow);
			bool auraCapReached = activeHeroAuraCount >= 5;
			AddLog($"英雄光环数量(估算)：{activeHeroAuraCount}/5（tracker）");
			
			// 英雄光环明细 - 4种检测方式
			int directEnchantCount = 0;
			string enchantDebug = "";
			int detectedByIdMatch = 0, detectedByEnchantCard = 0, detectedByName = 0;
			try
			{
				if (board?.HeroFriend?.Enchantments != null)
				{
					directEnchantCount = board.HeroFriend.Enchantments.Count;
					var enchantList = new List<string>();
					foreach (var e in board.HeroFriend.Enchantments)
					{
						if (e != null)
						{
							string eName = GetName(e.Id);
							bool idMatch = AuraCards.Contains(e.Id);
							var enchantCardId = e.EnchantCard?.Template?.Id ?? default(Card.Cards);
							bool enchantCardMatch = !enchantCardId.Equals(default(Card.Cards)) && AuraCards.Contains(enchantCardId);
							
							if (idMatch) detectedByIdMatch++;
							if (enchantCardMatch) detectedByEnchantCard++;
							
							enchantList.Add($"[{e.Id}={eName}]");
						}
					}
					enchantDebug = string.Join(", ", enchantList);
				}
			}
			catch { }
			
			if (directEnchantCount > 0)
			{
				AddLog($"英雄装备光环数(直接)：{directEnchantCount}个 | 检测统计：ID匹配={detectedByIdMatch}, EnchantCard={detectedByEnchantCard} | 详情：{enchantDebug}");
			}
			else
			{
				AddLog($"英雄光环明细(直接统计)：(无光环检测到-Enchantments.Count={board?.HeroFriend?.Enchantments?.Count ?? 0})");
			}
			AddLog($"纸板魔像累计打出：{_cardboardGolemPlayedCount}（新挂光环基础持续={GetAuraBaseDurationByGolem()}回合）");
			if (DebugDumpHeroEnchantments)
				AddLog($"光环Tracker明细：{FormatAuraTrackerSummary()}");
			if (DebugDumpHeroEnchantments)
			{
				// 只要“与光环相关”的局面出现就输出：
				// - 手牌有光环
				// - 触发上限
				// - 格尔宾可下（可能拉光环）
				// 重要：不再要求 HeroFriend.Enchantments.Count > 0，因为很多版本里英雄光环未必挂在 hero.Enchantments 上。
				bool shouldDump = auraInHandCount > 0 || auraCapReached || gelbinPlayableNow;
				if (shouldDump)
				{
					var reason = $"auraInHand={auraInHandCount}, auraCount={activeHeroAuraCount}, auraCap={auraCapReached}, gelbinPlayableNow={gelbinPlayableNow}";
					DumpHeroAuraDiff(board, reason);
					DumpHeroEnchantments(board, reason);
					DumpFriendlyMinionEnchantments(board, reason);
				}
			}
			if (auraCapReached)
				AddLog("英雄光环已达上限：本回合禁止施放任何光环牌（仅可交易/弃用）");

			bool hasAuraInGraveyard = false;
			try
			{
				// FriendGraveyard 在该版本 SmartBot 中通常是 List<Card.Cards>
				hasAuraInGraveyard = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => AuraCards.Contains(id));
			}
			catch
			{
				hasAuraInGraveyard = false;
			}

			// ===== 前期站场组件：保证“有随从能吃光环” =====
			// 关键开局：后手1费“跳币紫色珍鳃鱼人”，用战吼高概率拉到加速光环，直接进入滚雪球节奏。
			bool wantCoinPurpleGillsTurn1 = board.MaxMana == 1 && hasCoin && board.HasCardInHand(PurpleGillsMurloc);
			if (wantCoinPurpleGillsTurn1)
			{
				p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(-350));
				p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9000));
				p.CastMinionsModifiers.AddOrUpdate(PurpleGillsMurloc, new Modifier(-500));
				p.PlayOrderModifiers.AddOrUpdate(PurpleGillsMurloc, new Modifier(-8000));
				AddLog("开局线路：1费跳币 -> 紫色珍鳃鱼人（高概率触发加速光环）");
			}

			// 关键开局（你指定的硬规则）：后手1费如果手里有 END_011，则允许跳币直接打加速光环提速。
			// 注意：若已经走“跳币紫色珍鳃”线路，则不再重复触发。
			bool wantCoinAccelerationAuraTurn1 = !wantCoinPurpleGillsTurn1 && board.MaxMana == 1 && hasCoin && board.HasCardInHand(AccelerationAura) && !auraCapReached;
			if (wantCoinAccelerationAuraTurn1)
			{
				var coinPlayable = board.Hand.FirstOrDefault(c => c?.Template != null && c.Template.Id == TheCoin && c.CurrentCost <= board.ManaAvailable);
				var auraPlayable = board.Hand.FirstOrDefault(c => c?.Template != null && c.Template.Id == AccelerationAura);
				p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(-450));
				p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9500));
				p.CastSpellsModifiers.AddOrUpdate(AccelerationAura, new Modifier(-650));
				p.PlayOrderModifiers.AddOrUpdate(AccelerationAura, new Modifier(-9000));
				if (coinPlayable != null && auraPlayable != null)
				{
					p.ComboModifier = new ComboSet(coinPlayable.Id, auraPlayable.Id);
					AddLog($"COMBO锁定：硬币(实体{coinPlayable.Id})->加速光环(实体{auraPlayable.Id})（1费跳币提速）");
				}
				AddLog("开局线路：1费跳币 -> 加速光环(END_011)（硬规则提速）");
			}

			if (board.HasCardInHand(Wisp))
			{
				int mod = wantCoinPurpleGillsTurn1 ? 200 : -40;
				p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(mod));
				AddLog($"小精灵 {mod}（补前期站场）");
			}
			if (board.HasCardInHand(RighteousProtector))
			{
				// 2费回合且纸板魔像还在手：降权以让纸板魔像优先
				if (board.MaxMana == 2 && board.HasCardInHand(CardboardGolem))
				{
					int mod = -50;  // 大幅降权
					p.CastMinionsModifiers.AddOrUpdate(RighteousProtector, new Modifier(mod));
					p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(RighteousProtector, new Modifier(150));
					AddLog($"正义保护者 {mod}（2费回合纸板魔像优先：后置）");
				}
				else
				{
					int mod = wantCoinPurpleGillsTurn1 ? 200 : (isFastOpponent ? -180 : -90);
					p.CastMinionsModifiers.AddOrUpdate(RighteousProtector, new Modifier(mod));
					p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(RighteousProtector, new Modifier(150));
					AddLog($"正义保护者 {mod}（抗快攻/保场面）");
				}
			}
			if (board.HasCardInHand(PurpleGillsMurloc))
			{
				if (!wantCoinPurpleGillsTurn1)
				{
					int mod = board.ManaAvailable <= 3 ? -220 : -120;
					p.CastMinionsModifiers.AddOrUpdate(PurpleGillsMurloc, new Modifier(mod));
					AddLog($"紫色珍鳃鱼人 {mod}（拉≤2费法术抢节奏）");
				}
			}

			// ===== 核心策略映射（对应攻略：抢费 -> 延长光环 -> 十字军滚雪球/返场） =====

			// 0) 明日巨匠格尔宾：越早下越好。
			// 关键节奏：7费回合（或可用法力=7）且手里有硬币 -> 跳币8费直接下。
			// 注意：这里用 board.MaxMana/board.ManaAvailable 做兼容口径（加速光环可能让 ManaAvailable > MaxMana）。
			if (board.HasCardInHand(Gelbin))
			{
				bool canPlayGelbinNow = board.ManaAvailable >= 8;
				bool canCoinGelbin = hasCoin && board.ManaAvailable == 7;
				var gelbinPlayable = board.Hand.FirstOrDefault(c => c?.Template != null && c.Template.Id == Gelbin && c.CurrentCost <= board.ManaAvailable);
				var coinPlayable = board.Hand.FirstOrDefault(c => c?.Template != null && c.Template.Id == TheCoin && c.CurrentCost <= board.ManaAvailable);
				if (!canPlayGelbinNow && !canCoinGelbin)
					AddLog($"格尔宾在手但不可打：费用8 | 当前法力={board.ManaAvailable} | {(hasCoin ? "有币" : "无币")}（需8费，或7费+硬币）");
				if (canCoinGelbin)
				{
					p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(-450));
					p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9000));
					p.CastMinionsModifiers.AddOrUpdate(Gelbin, new Modifier(-1200));
					p.PlayOrderModifiers.AddOrUpdate(Gelbin, new Modifier(-9999));
					// 硬锁顺序：先硬币再格尔宾（避免先攻击/先拍别的导致错过 8 费窗口）
					if (coinPlayable != null && gelbinPlayable != null)
					{
						p.ComboModifier = new ComboSet(coinPlayable.Id, gelbinPlayable.Id);
						forcedComboEntityIds = new List<int> { coinPlayable.Id, gelbinPlayable.Id };
						AddLog($"COMBO锁定：硬币(实体{coinPlayable.Id})->格尔宾(实体{gelbinPlayable.Id})（强制先出牌再行动）");
					}
					AddLog("节奏线：7费跳币 -> 明日巨匠格尔宾（越早铺光环越强）");
				}
				else if (canPlayGelbinNow)
				{
					// 这里必须“硬优先”，避免被 5 费托雷斯等中费防守牌抢先
					p.CastMinionsModifiers.AddOrUpdate(Gelbin, new Modifier(-1200));
					p.PlayOrderModifiers.AddOrUpdate(Gelbin, new Modifier(-9999));
					// 更硬的兜底：当格尔宾“确实可打出”时，本回合强制后置所有其他出牌/英雄技能，避免 SmartBot 内置估值抢先把费用花掉。
					if (gelbinPlayable != null)
					{
						foreach (var c in board.Hand)
						{
							if (c?.Template == null)
								continue;
							var id = c.Template.Id;
							if (id == Gelbin || id == TheCoin)
								continue;
							if (c.Type == Card.CType.MINION)
								p.CastMinionsModifiers.AddOrUpdate(id, new Modifier(900));
							else if (c.Type == Card.CType.SPELL)
								p.CastSpellsModifiers.AddOrUpdate(id, new Modifier(900));
							p.PlayOrderModifiers.AddOrUpdate(id, new Modifier(8000));
						}
						p.CastHeroPowerModifier.AddOrUpdate(reinforce, new Modifier(999));
						p.PlayOrderModifiers.AddOrUpdate(reinforce, new Modifier(9000));
						AddLog("格尔宾可打出：强制后置本回合其他出牌/英雄技能，确保先下格尔宾");
					}
					// 硬锁：本回合优先打出格尔宾（避免先攻击导致顺序反了）
					if (gelbinPlayable != null)
					{
						p.ComboModifier = new ComboSet(gelbinPlayable.Id);
						forcedComboEntityIds = new List<int> { gelbinPlayable.Id };
						AddLog($"COMBO锁定：格尔宾(实体{gelbinPlayable.Id})（强制先出牌再行动）");
					}
					AddLog("格尔宾 -900（8费可用：硬优先下，直接把光环铺出来）");
				}
			}

			// 0.1) 格尔宾应在攻击前：因为可能拉出/生成十字军光环等导致攻击收益变化。
			// 做法：在“格尔宾可下且有可攻击随从”时，仅把可攻击随从的 AttackOrder 后置，促使 AI 先出牌再攻击（更干净可控）。
			if (shouldPlayGelbinBeforeAttacks)
			{
				foreach (var m in board.MinionFriend)
				{
					if (m?.Template == null)
						continue;
					if (m.CanAttack)
						p.AttackOrderModifiers.AddOrUpdate(m.Template.Id, new Modifier(9999));
				}
				AddLog("格尔宾可下且我方有可攻击随从：攻击顺序后置，先下格尔宾再攻击（避免错过十字军光环buff）");
			}

			// 1) 纸板魔像：越早越好。
			// 特别是“2费回合”：若因加速光环导致可用法力>=3，或手里有硬币能跳费，都应尽快下。
			if (board.HasCardInHand(CardboardGolem) && !board.HasCardOnBoard(CardboardGolem))
			{
				int mod;
				if (board.MaxMana == 2)
				{
					mod = -400;
					p.PlayOrderModifiers.AddOrUpdate(CardboardGolem, new Modifier(-200));
					AddLog("纸板魔像 -400（2费回合可跳到3费：尽快延长光环收益）");

					// 若需要硬币才能下，则硬币应紧挨着在前面
					if (hasCoin && board.ManaAvailable == 2)
					{
						p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(-300));
						p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9000));
						p.PlayOrderModifiers.AddOrUpdate(CardboardGolem, new Modifier(-8000));
						AddLog("2费线路：跳币 -> 纸板魔像");
					}
				}
				else
				{
					mod = board.ManaAvailable <= 4 ? -250 : -150;
					p.PlayOrderModifiers.AddOrUpdate(CardboardGolem, new Modifier(-50));
					AddLog($"纸板魔像 优先级 {mod}（延长光环收益）");
				}
				p.CastMinionsModifiers.AddOrUpdate(CardboardGolem, new Modifier(mod));
			}

			// 2) 光环类法术：本套牌核心就是“光环叠加滚雪球”。
			// 但当英雄头上光环已满(>=5)时，游戏规则不允许继续挂光环，需要强制禁止施放。
			if (auraInHandCount > 0)
				AddLog($"光环在手：{auraInHandCount} 张（允许叠加）");
			if (auraCapReached)
			{
				foreach (var aura in AuraCards)
				{
					if (!board.HasCardInHand(aura))
						continue;
					p.CastSpellsModifiers.AddOrUpdate(aura, new Modifier(9999));
					p.PlayOrderModifiers.AddOrUpdate(aura, new Modifier(9000));
					AddLog($"光环 {aura} +9999（已达5层上限：禁止施放）");
				}
			}
			else
			{
				foreach (var aura in AuraCards)
				{
					if (!board.HasCardInHand(aura))
						continue;

					// 让光环整体更早结算（尤其是十字军光环：应在攻击前施放）
					p.PlayOrderModifiers.AddOrUpdate(aura, new Modifier(-2000));

					// 基础倾向：有随从可吃收益时更想尽快挂；光环越多，越鼓励同回合连挂
					int baseAuraMod = -120 - Math.Min(120, (auraInHandCount - 1) * 40);
					if (friendMinions > 0)
						baseAuraMod -= 80;
					if (isSlowOpponent)
						baseAuraMod -= 30;
					if (isFastOpponent && friendMinions == 0)
						baseAuraMod += 120; // 快攻且空场时，避免空过

					p.CastSpellsModifiers.AddOrUpdate(aura, new Modifier(baseAuraMod));
					AddLog($"光环 {aura} 施放倾向 {baseAuraMod}");
				}
			}

			if (!auraCapReached)
			{
				// 2.1 加速光环：抢费发动机，前期更想打出
				if (board.HasCardInHand(AccelerationAura))
				{
					int mod = board.ManaAvailable <= 3 ? -250 : -150;
					if (friendMinions == 0 && isFastOpponent)
						mod += 100; // 快攻对局没站场时，避免“空过防守”去挂加速

					p.CastSpellsModifiers.AddOrUpdate(AccelerationAura, new Modifier(mod));
					AddLog($"加速光环 优先级 {mod}");
				}

				// 2.2 十字军光环：当“可攻击随从>=2 且 场面能延续”时极强
				// 策略：优先在攻击前施放；满足条件则大幅提高施放优先级。
				p.PlayOrderModifiers.AddOrUpdate(CrusaderAura, new Modifier(-3000));
				if (board.HasCardInHand(CrusaderAura))
				{
					if (canAttackMinions >= 2 && friendMinions >= 2)
					{
						p.CastSpellsModifiers.AddOrUpdate(CrusaderAura, new Modifier(-350));
						p.PlayOrderModifiers.AddOrUpdate(CrusaderAura, new Modifier(-6000));
						AddLog("十字军光环 -350（可攻击随从>=2，适合滚雪球）");
					}
					else if (isFastOpponent && friendMinions == 0)
					{
						p.CastSpellsModifiers.AddOrUpdate(CrusaderAura, new Modifier(150));
						AddLog("十字军光环 +150（对快攻且无站场，避免卡手）");
					}
					else
					{
						p.CastSpellsModifiers.AddOrUpdate(CrusaderAura, new Modifier(-120));
						AddLog("十字军光环 -120（常规偏好：有场再挂）");
					}
				}

				// 2.3 侏儒光环/梅卡托克光环：可交易；快攻对局更偏“保血/稳场”，慢速对局更偏“换牌找关键”
				if (board.HasCardInHand(GnomeAuraHeal))
				{
					// 若本回合希望挂光环（有随从/手里光环多），则优先施放而不是交易
					bool preferCastAuraNow = friendMinions > 0 || auraInHandCount >= 2;
					bool veryHealthy = friendHealth >= 24;
					if (isFastOpponent && friendHealth <= 20)
					{
						p.CastSpellsModifiers.AddOrUpdate(GnomeAuraHeal, new Modifier(-180));
						p.TradeModifiers.AddOrUpdate(GnomeAuraHeal, new Modifier(preferCastAuraNow ? 999 : -50));
						AddLog("侏儒光环 -180（快攻+血线压力，优先当防守工具）");
					}
					else if (veryHealthy)
					{
						// 血量很健康时：尽量不要把侏儒光环当作“挂光环”，优先用 1 费交易去找关键牌。
						p.CastSpellsModifiers.AddOrUpdate(GnomeAuraHeal, new Modifier(350));
						p.TradeModifiers.AddOrUpdate(GnomeAuraHeal, new Modifier(-250));
						AddLog("侏儒光环 血量健康：施放 +350（避免浪费），交易 -250（优先循环换牌）");
					}
					else
					{
						p.TradeModifiers.AddOrUpdate(GnomeAuraHeal, new Modifier(preferCastAuraNow ? 999 : 150));
						AddLog("侏儒光环 倾向交易 +150（慢速/不缺血时找关键牌）");
					}
				}

				if (board.HasCardInHand(MechatokAura))
				{
					// 规则：
					// - 费用够打出(>=5)且有随从：更偏施放（滚雪球/保场）
					// - 费用不够打出(<5)：用“剩余1费”交易进牌库找解/找节奏（配合 ForcedResimulation 可在打完主动作后补交易）
					bool canCastMechatok = board.ManaAvailable >= 5;
					if (!canCastMechatok)
					{
						if (board.ManaAvailable >= 1)
						{
							p.TradeModifiers.AddOrUpdate(MechatokAura, new Modifier(-250));
							AddLog("梅卡托克的光环 交易 -250（当前费用不够施放，用剩余1费循环换牌）");
						}
						else
						{
							p.TradeModifiers.AddOrUpdate(MechatokAura, new Modifier(120));
							AddLog("梅卡托克的光环 交易 +120（无可用法力，等待下回合）");
						}
					}
					else if (friendMinions > 0)
					{
						int castMod = isFastOpponent ? -160 : -120;
						p.CastSpellsModifiers.AddOrUpdate(MechatokAura, new Modifier(castMod));
						p.TradeModifiers.AddOrUpdate(MechatokAura, new Modifier(80));
						AddLog($"梅卡托克的光环 {castMod}（费用够且有随从：优先挂光环滚雪球/保场）");
					}
					else
					{
						// 空场时先找站场/返场，避免裸挂
						p.TradeModifiers.AddOrUpdate(MechatokAura, new Modifier(120));
						AddLog("梅卡托克的光环 倾向交易 +120（空场时先找站场牌）");
					}
				}

				// 2.4 时序光环/工匠光环：工匠光环优先级高于时序光环。
				// 额外规则：当我方场上随从<=6时，工匠光环更适合尽快挂（避免因格子满导致收益打折）。
				bool preferArtisanAura = friendMinions <= 6;
				bool hasArtisanAura = board.HasCardInHand(ArtisanAura);
				bool hasTimeAura = board.HasCardInHand(TimeAura);

				if (hasArtisanAura)
				{
					int mod;
					if (friendMinions >= 7)
						mod = 180; // 接近满场时先别挂，容易浪费召唤位
					else if (preferArtisanAura)
						mod = isSlowOpponent ? -220 : -180;
					else
						mod = isSlowOpponent ? -150 : -120;

					p.CastSpellsModifiers.AddOrUpdate(ArtisanAura, new Modifier(mod));
					p.PlayOrderModifiers.AddOrUpdate(ArtisanAura, new Modifier(-2600));
					AddLog($"工匠光环 优先级 {mod}（随从<=6 更优先={preferArtisanAura}）");
				}

				if (hasTimeAura)
				{
					// 若同时有工匠光环，时序光环降一档，避免抢先级
					int mod = isSlowOpponent ? -140 : -90;
					if (hasArtisanAura && preferArtisanAura)
						mod += 80;
					p.CastSpellsModifiers.AddOrUpdate(TimeAura, new Modifier(mod));
					p.PlayOrderModifiers.AddOrUpdate(TimeAura, new Modifier(-2400));
					AddLog($"时序光环 优先级 {mod}（工匠光环更优先）");
				}
			}
			else
			{
				// 光环满层时：绝不施放光环，优先把可交易光环换掉
				if (board.HasCardInHand(GnomeAuraHeal))
					p.TradeModifiers.AddOrUpdate(GnomeAuraHeal, new Modifier(-150));
				if (board.HasCardInHand(MechatokAura))
					p.TradeModifiers.AddOrUpdate(MechatokAura, new Modifier(-150));
			}

			// 3) 时间流具象：返场点（需要“你控制着光环”才触发）。
			// 规则（按你要求）：只有在激活态(POWERED_UP/IsPowered)才允许打出；非激活态一律不打。
			if (board.HasCardInHand(TimeFlowElemental))
			{
				var tfe = board.Hand.FirstOrDefault(c => c?.Template != null && c.Template.Id == TimeFlowElemental);
				bool tfePowered = false;
				try
				{
					// 有些版本里 IsPowered 会同步 POWERED_UP tag
					if (tfe != null && tfe.IsPowered)
						tfePowered = true;
					else if (tfe != null && GetTag(tfe, Card.GAME_TAG.POWERED_UP) == 1)
						tfePowered = true;
				}
				catch
				{
					tfePowered = false;
				}

				bool enemyBoardWide = enemyMinions >= 2;
				bool enemyBoardHasManyLowHealth = board.MinionEnemy.Count(m => m.CurrentHealth <= 3) >= 2;
				AddLog($"时间流具象激活态: {(tfePowered ? "Y" : "N")}（POWERED_UP/IsPowered）");
				if (!tfePowered)
				{
					p.CastMinionsModifiers.AddOrUpdate(TimeFlowElemental, new Modifier(350));
					AddLog("时间流具象 +350（未激活态：禁止打出）");
				}
				else if (enemyBoardWide || enemyBoardHasManyLowHealth)
				{
					p.CastMinionsModifiers.AddOrUpdate(TimeFlowElemental, new Modifier(-250));
					AddLog("时间流具象 -250（激活态：作为返场点）");
				}
				else
				{
					p.CastMinionsModifiers.AddOrUpdate(TimeFlowElemental, new Modifier(120));
					AddLog("时间流具象 +120（激活态但不需要返场：通常不打）");
				}
			}

			// 4) 绒绒虎：对快攻更重要（突袭/吸血/圣盾），优先解场保血
			if (board.HasCardInHand(PlushTiger))
			{
				int mod = isFastOpponent ? -180 : -80;
				p.CastMinionsModifiers.AddOrUpdate(PlushTiger, new Modifier(mod));
				AddLog($"绒绒虎 优先级 {mod}");
			}

			// 4.1 伊莉斯：偏慢速资源点，快攻对局降低
			if (board.HasCardInHand(Elise))
			{
				int mod = isSlowOpponent ? -90 : 80;
				p.CastMinionsModifiers.AddOrUpdate(Elise, new Modifier(mod));
				AddLog($"伊莉斯 优先级 {mod}");
			}

			// 4.2 萨维斯：偏中后期价值，慢速略提
			if (board.HasCardInHand(Stheno))
			{
				int mod = isSlowOpponent ? -60 : 40;
				p.CastMinionsModifiers.AddOrUpdate(Stheno, new Modifier(mod));
				AddLog($"萨维斯 优先级 {mod}");
			}

			// 4.3 托雷斯：防守点（圣盾嘲讽），快攻/血线危险更想打
			if (board.HasCardInHand(Torres))
			{
				int mod = (isFastOpponent || friendHealth <= 15) ? -150 : -60;
				// 有刀且本回合可下多里安：优先多里安 -> 再攻击抽牌，不要被托雷斯抢走5费窗口
				bool dorianLine = wantDorianComboLine;
				if (dorianLine)
				{
					p.CastMinionsModifiers.AddOrUpdate(Torres, new Modifier(350));
					p.PlayOrderModifiers.AddOrUpdate(Torres, new Modifier(9000));
					AddLog("托雷斯 +350（多里安combo线路：本回合优先多里安->再抽牌/交易，托雷斯后置）");
				}
				// 若格尔宾本回合可直接下，则托雷斯必须降级，避免抢先打乱节奏
				else if (gelbinPlayableNow)
				{
					p.CastMinionsModifiers.AddOrUpdate(Torres, new Modifier(250));
					p.PlayOrderModifiers.AddOrUpdate(Torres, new Modifier(8000));
					AddLog("托雷斯 +250（格尔宾可下：托雷斯降级，避免抢先）");
				}
				else
				{
					p.CastMinionsModifiers.AddOrUpdate(Torres, new Modifier(mod));
					AddLog($"托雷斯 优先级 {mod}");
				}
			}

			// 4.4 诺莉亚：突袭/亡语AOE，更多用于返场
			if (board.HasCardInHand(Nolia))
			{
				int mod = enemyMinions >= 2 ? -120 : -20;
				p.CastMinionsModifiers.AddOrUpdate(Nolia, new Modifier(mod));
				AddLog($"诺莉亚 优先级 {mod}");
			}

			// 5) 巨熊之槌：中期资源引擎；手牌快满时降低攻击避免爆牌
			if (board.HasCardInHand(GrizzlyHammer))
			{
				int mod = isSlowOpponent ? -200 : -120;
				// 关键：当“实际可用法力=4”且我方未装备武器时，强制优先提刀（否则很容易先花费导致本回合提不了刀）。
				bool forceEquipHammer4 = board.WeaponFriend == null && board.ManaAvailable == 4 && !gelbinPlayableNow;
				int hammerPlayOrder = forceEquipHammer4 ? -9500 : 3000;
				int hammerCastMod = forceEquipHammer4 ? -900 : mod;
				p.CastWeaponsModifiers.AddOrUpdate(GrizzlyHammer, new Modifier(hammerCastMod));
				p.PlayOrderModifiers.AddOrUpdate(GrizzlyHammer, new Modifier(hammerPlayOrder));
				AddLog($"巨熊之槌 出牌优先级 {hammerCastMod} | 顺序 {hammerPlayOrder}（forceEquip4={forceEquipHammer4}）");

				if (forceEquipHammer4)
				{
					// 后置会占费用的动作，确保先提刀
					foreach (var aura in AuraCards)
					{
						if (board.HasCardInHand(aura))
						{
							p.CastSpellsModifiers.AddOrUpdate(aura, new Modifier(500));
							p.PlayOrderModifiers.AddOrUpdate(aura, new Modifier(5000));
						}
					}
					if (board.HasCardInHand(CardboardGolem))
					{
						p.CastMinionsModifiers.AddOrUpdate(CardboardGolem, new Modifier(500));
						p.PlayOrderModifiers.AddOrUpdate(CardboardGolem, new Modifier(7000));
					}
					if (board.HasCardInHand(RighteousProtector))
					{
						p.CastMinionsModifiers.AddOrUpdate(RighteousProtector, new Modifier(300));
						p.PlayOrderModifiers.AddOrUpdate(RighteousProtector, new Modifier(7000));
					}
					// 英雄技能也必须后置
					p.CastHeroPowerModifier.AddOrUpdate(reinforce, new Modifier(999));
					AddLog("4费提刀硬规则：已后置光环/魔像/1费/英雄技能，确保本回合先提巨熊之槌（PlayOrder 越小越优先）");
				}
			}

			if (board.WeaponFriend != null && board.WeaponFriend.Template.Id == GrizzlyHammer)
			{
				if (board.Hand.Count >= 9)
				{
					p.WeaponsAttackModifiers.AddOrUpdate(GrizzlyHammer, new Modifier(999));
					AddLog("巨熊之槌 不攻击（手牌>=9，防止爆牌）");
				}
				else if (isFastOpponent && friendHealth <= 12 && enemyMinions >= 2)
				{
					p.WeaponsAttackModifiers.AddOrUpdate(GrizzlyHammer, new Modifier(150));
					AddLog("巨熊之槌 降低攻击倾向（快攻且血线危险，优先处理场面）");
				}
				else
				{
					p.WeaponsAttackModifiers.AddOrUpdate(GrizzlyHammer, new Modifier(-50));
					AddLog("巨熊之槌 提高攻击倾向（补抽牌节奏）");
				}
			}

			// 6) 多里安：慢速对局更强；快攻对局降低优先级
			if (board.HasCardInHand(Dorian))
			{
				// 关键节奏：手里有刀/已装备刀/手里有可交易牌时，优先下多里安，便于尽快启动“抽牌/交易/铺光环”的combo线路。
				bool dorianLine = wantDorianComboLine;
				int mod = dorianLine ? -650 : (isSlowOpponent ? -120 : 120);
				p.CastMinionsModifiers.AddOrUpdate(Dorian, new Modifier(mod));
				if (dorianLine)
				{
					p.PlayOrderModifiers.AddOrUpdate(Dorian, new Modifier(-9000));
					var dorianPlayable = board.Hand.FirstOrDefault(c => c?.Template != null && c.Template.Id == Dorian && c.CurrentCost <= board.ManaAvailable);
					// 硬规则：当多里安“可打出”时，禁止武器先挥刀抽牌，避免出现“先攻击->再多里安”的反直觉顺序。
					// 出牌后会触发 Resimulate；多里安不在手时此规则自然失效，武器可正常攻击补牌。
					if (dorianPlayable != null && board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.Template.Id == GrizzlyHammer)
					{
						p.WeaponsAttackModifiers.AddOrUpdate(GrizzlyHammer, new Modifier(9999));
						AddLog("有巨熊之槌且多里安可下：武器攻击 +9999（强制先下多里安，再挥刀抽牌）");
					}
					if (dorianPlayable != null)
					{
						try
						{
							if (p.ComboModifier == null)
							{
								p.ComboModifier = new ComboSet(dorianPlayable.Id);
								AddLog($"COMBO锁定：多里安(实体{dorianPlayable.Id})（有刀：强制先下多里安再进行攻击/其他动作）");
							}
						}
						catch
						{
							// ignore
						}
					}
					AddLog($"傀儡大师多里安 优先级 {mod}（combo线路：先下多里安->再抽牌/交易/行动）");
				}
				else
				{
					AddLog($"傀儡大师多里安 优先级 {mod}（慢速更强/快攻避免卡手）");
				}
			}

			// 7) 阿纳克洛斯：后期强控场，默认略提；对快攻过早打出意义不大
			if (board.HasCardInHand(Anachronos))
			{
				int mod = isSlowOpponent ? -80 : 50;
				p.CastMinionsModifiers.AddOrUpdate(Anachronos, new Modifier(mod));
				AddLog($"阿纳克洛斯 优先级 {mod}");
			}

			// 8) 格尔宾：核心后期终结/资源点。
			// 若上面已经进入“7费跳币/8费可下”的强制节奏，则无需再覆盖；否则按对局快慢给中等优先级。
			if (board.HasCardInHand(Gelbin) && board.ManaAvailable < 7)
			{
				int mod = isSlowOpponent ? -120 : -40;
				p.CastMinionsModifiers.AddOrUpdate(Gelbin, new Modifier(mod));
				AddLog($"格尔宾 优先级 {mod}");
			}

			// 9) 奇利亚斯：默认中后期质量牌
			if (board.HasCardInHand(Zilliax))
			{
				int mod = isFastOpponent ? -60 : -120;
				p.CastMinionsModifiers.AddOrUpdate(Zilliax, new Modifier(mod));
				AddLog($"奇利亚斯 优先级 {mod}");
			}

			// 10) 英雄技能：该版本 API 为 CastHeroPowerModifier.AddOrUpdate(英雄技能ID, Modifier)
			// 援军(英雄技能)改为“补费动作”：有更强动作时必须后置
			bool hasOtherPlayableCard = board.Hand.Any(c => c?.Template != null
				&& c.Template.Id != TheCoin
				&& c.Template.Id != PlushTigerToken
				&& c.Template.Id != Card.Cards.TLC_100t1
				&& c.CurrentCost <= board.ManaAvailable);

			// 10.1) 地标：安戈洛丛林（TLC_100t1）
			// 需求：提高使用优先级，但不要因为它是1费可打而导致 2 费回合不按英雄技能。
			if (board.HasCardInHand(Card.Cards.TLC_100t1))
			{
				// 2费且“没有其他可打牌”（hasOtherPlayableCard 为 false）时，优先英雄技能，不先下地标。
				if (board.ManaAvailable == 2 && !hasOtherPlayableCard)
				{
					p.LocationsModifiers.AddOrUpdate(Card.Cards.TLC_100t1, new Modifier(350));
					AddLog("地标 安戈洛丛林 +350（2费且无其他好动作：优先英雄技能，避免地标吃掉2费）");
				}
				else
				{
					p.LocationsModifiers.AddOrUpdate(Card.Cards.TLC_100t1, new Modifier(-150));
					AddLog("地标 安戈洛丛林 -150（提高使用优先级）");
				}
			}
			p.PlayOrderModifiers.AddOrUpdate(reinforce, new Modifier(9000));
			if (friendMinions >= 6)
			{
				p.CastHeroPowerModifier.AddOrUpdate(reinforce, new Modifier(999));
				AddLog("英雄技能 +999（场面接近满，防止卡格子）");
			}
			else if (gelbinPlayableNow)
			{
				p.CastHeroPowerModifier.AddOrUpdate(reinforce, new Modifier(400));
				AddLog("英雄技能 +400（格尔宾可下：援军强制后置）");
			}
			else if (hasOtherPlayableCard && board.ManaAvailable >= 2)
			{
				p.CastHeroPowerModifier.AddOrUpdate(reinforce, new Modifier(250));
				AddLog("英雄技能 +250（有其他可打牌：援军后置，仅作补费）");
			}
			else if (board.ManaAvailable >= 2)
			{
				p.CastHeroPowerModifier.AddOrUpdate(reinforce, new Modifier(-50));
				AddLog("英雄技能 -50（无更好动作：补费援军）");
			}

			// ===== 输出日志 =====
			// ===== 重新思考：出牌后强制重算（排除硬币，避免无意义重算） =====
			try
			{
				foreach (var card in board.Hand)
				{
					if (card?.Template == null)
						continue;

					var id = card.Template.Id;
					if (id == TheCoin)
						continue;

					if (!p.ForcedResimulationCardList.Contains(id))
						p.ForcedResimulationCardList.Add(id);
				}
				AddLog($"重新思考：ForcedResimulationCardList={p.ForcedResimulationCardList.Count}（已排除硬币）");
			}
			catch
			{
				// ignore
			}

			// ===== 最终强制连段（避免后续逻辑/内置估值覆盖导致“明明可下却先花费”） =====
			if (forcedComboEntityIds != null && forcedComboEntityIds.Count > 0)
			{
				if (forcedComboEntityIds.Count == 1)
				{
					p.ComboModifier = new ComboSet(forcedComboEntityIds[0]);
					AddLog($"COMBO最终覆盖：格尔宾(实体{forcedComboEntityIds[0]})（必出）");
				}
				else if (forcedComboEntityIds.Count >= 2)
				{
					p.ComboModifier = new ComboSet(forcedComboEntityIds[0], forcedComboEntityIds[1]);
					AddLog($"COMBO最终覆盖：实体{forcedComboEntityIds[0]} -> 实体{forcedComboEntityIds[1]}（必出）");
				}
			}

			Bot.Log(_log);

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



		// 英雄光环计数 - 统计英雄身上装备的某张光环数量
		private int CountHeroAura(Board board, Card.Cards auraId)
		{
			try
			{
				if (board?.HeroFriend?.Enchantments == null)
					return 0;
				
				int count = 0;
				foreach (var e in board.HeroFriend.Enchantments)
				{
					if (e == null) continue;
					// 多种方式检查以兼容不同API版本
					if (e.Id == auraId) { count++; continue; }
					if (e.EnchantCard?.Template?.Id == auraId) { count++; continue; }
				}
				return count;
			}
			catch 
			{ 
				return 0; 
			}
		}

		// 统计英雄所有6张主要光环 - 返回Dictionary
		private Dictionary<Card.Cards, int> CountAllHeroAuras(Board board)
		{
			var result = new Dictionary<Card.Cards, int>();
			var auraIds = new Card.Cards[] { AccelerationAura, CrusaderAura, GnomeAuraHeal, TimeAura, MechatokAura, ArtisanAura };
			
			foreach (var id in auraIds)
				result[id] = CountHeroAura(board, id);
			
			return result;
		}

				private int GetActiveHeroAuraCount(Board board, int? turnNow)
		{
			try
			{
				UpdateAuraTracker(board, turnNow);
				if (_activeAuraInstances.Count > 0)
					return _activeAuraInstances.Count;

				var hero = board?.HeroFriend;
				var enchants = hero?.Enchantments;
				if (enchants == null)
					return 0;

				// 注意：这里要“保守计数”。
				// 之前用 enchants.Count 兜底会把任意BUFF当作“光环层数”，导致误触发“5层上限禁用光环”。
				int candidates = 0;
				foreach (var e in enchants)
				{
					if (e == null)
						continue;

					bool idMatch = AuraCards.Contains(e.Id);
					var enchantCardId = e.EnchantCard?.Template != null ? e.EnchantCard.Template.Id : default(Card.Cards);
					bool enchantCardMatch = !enchantCardId.Equals(default(Card.Cards)) && AuraCards.Contains(enchantCardId);

					bool nameMatch = false;
					try
					{
						var t = CardTemplate.LoadFromId(e.Id);
						var name = string.IsNullOrWhiteSpace(t?.NameCN) ? t?.Name : t.NameCN;
						if (!string.IsNullOrWhiteSpace(name) && (name.Contains("光环") || name.IndexOf("Aura", StringComparison.OrdinalIgnoreCase) >= 0))
							nameMatch = true;
					}
					catch
					{
						// ignore
					}

					if (idMatch || enchantCardMatch || nameMatch)
						candidates++;
				}

				return candidates;
			}
			catch
			{
				return 0;
			}
		}

		private void UpdateAuraTracker(Board board, int? turnNow)
		{
			try
			{
				if (board == null)
					return;

				// 0) 本回合已打出的牌：用增量抓“纸板魔像”打出事件。
				// 规则：每打一张纸板魔像，本局光环持续回合 +1（可叠加，两张=+2）。
				if (turnNow.HasValue)
				{
					if (!_cardsPlayedThisTurnTurn.HasValue || _cardsPlayedThisTurnTurn.Value != turnNow.Value)
					{
						_cardsPlayedThisTurnTurn = turnNow.Value;
						_cardsPlayedThisTurnProcessedCount = 0;
					}

					var cardsPlayedThisTurn = TryGetUnknownListProperty(board, "CardsPlayedThisTurn");
					if (cardsPlayedThisTurn != null && cardsPlayedThisTurn.Count > _cardsPlayedThisTurnProcessedCount)
					{
						for (int i = _cardsPlayedThisTurnProcessedCount; i < cardsPlayedThisTurn.Count; i++)
						{
							var id = TryGetCardIdFromUnknown(cardsPlayedThisTurn[i]);
							if (id.HasValue && id.Value == CardboardGolem)
							{
								_cardboardGolemPlayedCount++;
								for (int j = 0; j < _activeAuraInstances.Count; j++)
									_activeAuraInstances[j].RemainingOwnTurns += 1;
								AddLog($"Tracker：检测到打出纸板魔像(+1回合)，累计={_cardboardGolemPlayedCount}；已存在光环全部+1回合");
							}
						}
						_cardsPlayedThisTurnProcessedCount = cardsPlayedThisTurn.Count;
					}
				}

				// 1) 新回合：将所有已跟踪光环剩余回合 -1（表示上一回合末已触发/消耗一次）
				if (board.IsOwnTurn && turnNow.HasValue)
				{
					if (!_lastAuraTickOwnTurn.HasValue)
					{
						_lastAuraTickOwnTurn = turnNow.Value;
					}
					else if (_lastAuraTickOwnTurn.Value != turnNow.Value)
					{
						for (int i = _activeAuraInstances.Count - 1; i >= 0; i--)
						{
							_activeAuraInstances[i].RemainingOwnTurns -= 1;
							if (_activeAuraInstances[i].RemainingOwnTurns <= 0)
								_activeAuraInstances.RemoveAt(i);
						}
						_lastAuraTickOwnTurn = turnNow.Value;
					}
				}

				// 2) 识别本回合新打出的光环：通过 FriendGraveyardTurn 增量
				var graveTurn = TryGetCardListProperty(board, "FriendGraveyardTurn") ?? new List<Card.Cards>();
				var curCounts = new Dictionary<Card.Cards, int>();
				foreach (var id in graveTurn)
				{
					if (!AuraCards.Contains(id))
						continue;
					curCounts[id] = curCounts.TryGetValue(id, out var v) ? (v + 1) : 1;
				}

				int baseDuration = GetAuraBaseDurationByGolem();

				foreach (var kv in curCounts)
				{
					int old = _lastFriendGraveyardTurnAuraCounts.TryGetValue(kv.Key, out var ov) ? ov : 0;
					int delta = kv.Value - old;
					if (delta <= 0)
						continue;
					for (int i = 0; i < delta; i++)
						_activeAuraInstances.Add(new AuraInstance { AuraId = kv.Key, RemainingOwnTurns = baseDuration });
				}

				_lastFriendGraveyardTurnAuraCounts = curCounts;
			}
			catch
			{
				// ignore
			}
		}

		private int GetAuraBaseDurationByGolem()
		{
			// 基础3回合；每打出一张纸板魔像 +1（可叠加）
			int d = 3 + _cardboardGolemPlayedCount;
			if (d < 1) d = 1;
			if (d > 10) d = 10; // 防止异常情况下无限增长
			return d;
		}

		private string FormatAuraTrackerSummary()
		{
			try
			{
				if (_activeAuraInstances.Count == 0)
					return "(空)";
				// 例：AURA_A:[3,2] AURA_B:[4]
				var groups = _activeAuraInstances
					.GroupBy(a => a.AuraId)
					.Select(g => $"{g.Key}:[{string.Join(",", g.Select(x => x.RemainingOwnTurns).OrderByDescending(x => x))}]");
				return string.Join(" ", groups);
			}
			catch
			{
				return "(格式化失败)";
			}
		}

		private List<Card.Cards> TryGetCardListProperty(object obj, params string[] propertyNames)
		{
			try
			{
				if (obj == null || propertyNames == null)
					return null;
				var t = obj.GetType();
				foreach (var name in propertyNames)
				{
					var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (p == null)
						continue;
					var v = p.GetValue(obj, null);
					if (v is List<Card.Cards> list)
						return list;
					if (v is IEnumerable<Card.Cards> enumerable)
						return enumerable.ToList();
				}
			}
			catch
			{
				// ignore
			}
			return null;
		}

		private List<object> TryGetUnknownListProperty(object obj, params string[] propertyNames)
		{
			try
			{
				if (obj == null || propertyNames == null)
					return null;
				var t = obj.GetType();
				foreach (var name in propertyNames)
				{
					var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (p == null)
						continue;
					var v = p.GetValue(obj, null);
					if (v is System.Collections.IEnumerable enumerable)
					{
						var list = new List<object>();
						foreach (var item in enumerable)
							list.Add(item);
						return list;
					}
				}
			}
			catch
			{
				// ignore
			}
			return null;
		}

		private Card.Cards? TryGetCardIdFromUnknown(object unknown)
		{
			try
			{
				if (unknown == null)
					return null;
				if (unknown is Card.Cards cc)
					return cc;
				// Card / PlayedCard 等对象：优先 Template.Id
				var t = unknown.GetType();
				var templateProp = t.GetProperty("Template", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (templateProp != null)
				{
					var templateObj = templateProp.GetValue(unknown, null);
					if (templateObj != null)
					{
						var idProp = templateObj.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (idProp != null)
						{
							var idVal = idProp.GetValue(templateObj, null);
							if (idVal is Card.Cards tid)
								return tid;
						}
					}
				}
				// 兜底：直接 Id
				var idProp2 = t.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (idProp2 != null)
				{
					var idVal2 = idProp2.GetValue(unknown, null);
					if (idVal2 is Card.Cards tid2)
						return tid2;
				}
			}
			catch
			{
				// ignore
			}
			return null;
		}

		private void DumpHeroEnchantments(Board board, string reason)
		{
			try
			{
				var hero = board?.HeroFriend;
				var enchants = hero?.Enchantments;
				var heroName = hero?.Template != null ? GetName(hero.Template.Id) : "?";
				var heroId = hero?.Template != null ? hero.Template.Id.ToString() : "?";
				int count = enchants != null ? enchants.Count : 0;
				AddLog($"[AuraDebug] Hero={heroName}({heroId}) Enchantments={count} | {reason}");

				// 尝试直接用 API 检测“英雄是否拥有某个光环附魔/层数”。
				// 说明：有些版本里 hero.Enchantments 为空，但 HasEnchantement/GetEnchantementCount 仍可能通过 tags 正确返回。
				try
				{
					var auraChecks = new List<string>();
					foreach (var aura in AuraCards)
					{
						bool has = false;
						int cnt = 0;
						try
						{
							has = hero != null && hero.HasEnchantement(hero, aura);
							cnt = hero != null ? hero.GetEnchantementCount(hero, aura) : 0;
						}
						catch
						{
							has = false;
							cnt = 0;
						}
						auraChecks.Add($"{aura}:{(has ? "Y" : "N")}/{cnt}");
					}
					AddLog($"[AuraDebug] Hero.HasEnchantement/Count: {string.Join(" | ", auraChecks)}");
				}
				catch
				{
					AddLog("[AuraDebug] Hero.HasEnchantement/Count: <failed>");
				}

				// 同时把 hero.tags 打出来：很多“规则性状态/计数”可能不在 Enchantments，而是在 tags。
				try
				{
					if (hero?.tags != null && hero.tags.Count > 0)
					{
						var tagPairs = hero.tags
							.Where(kv => kv.Value != 0)
							.Select(kv => $"{kv.Key}={kv.Value}")
							.Take(60)
							.ToList();
						AddLog($"[AuraDebug] Hero.tags(nonzero)={tagPairs.Count}/{hero.tags.Count}: {string.Join(", ", tagPairs)}");
					}
					else
					{
						AddLog("[AuraDebug] Hero.tags: <empty>");
					}
				}
				catch
				{
					AddLog("[AuraDebug] Hero.tags dump failed");
				}

				if (enchants == null || enchants.Count == 0)
					return;

				int idx = 0;
				foreach (var e in enchants)
				{
					idx++;
					if (e == null)
					{
						AddLog($"[AuraDebug] #{idx}: <null>");
						continue;
					}

					string idName = GetName(e.Id);
					bool idMatch = AuraCards.Contains(e.Id);

					var enchantTemplate = e.EnchantCard?.Template;
					string enchantInfo = enchantTemplate != null
						? $"{GetName(enchantTemplate.Id)}({enchantTemplate.Id})"
						: "null";
					bool enchantMatch = enchantTemplate != null && AuraCards.Contains(enchantTemplate.Id);

					var deathTemplate = e.DeathrattleCard?.Template;
					string deathInfo = deathTemplate != null
						? $"{GetName(deathTemplate.Id)}({deathTemplate.Id})"
						: "null";

					bool nameMatch = (!string.IsNullOrWhiteSpace(idName) && (idName.Contains("光环") || idName.IndexOf("Aura", StringComparison.OrdinalIgnoreCase) >= 0))
						|| (enchantTemplate != null && !string.IsNullOrWhiteSpace(enchantTemplate.Name) && enchantTemplate.Name.IndexOf("Aura", StringComparison.OrdinalIgnoreCase) >= 0)
						|| (enchantTemplate != null && !string.IsNullOrWhiteSpace(enchantTemplate.NameCN) && enchantTemplate.NameCN.Contains("光环"));

					string flag = (idMatch || enchantMatch || nameMatch) ? "CANDIDATE" : "-";
					AddLog(
						$"[AuraDebug] #{idx} {flag}: Id={e.Id}({idName}) idMatch={idMatch} | EnchantCard={enchantInfo} enchantMatch={enchantMatch} | Deathrattle={deathInfo} | ctrl={e.controllerOrMaxLimitSpell} creator={e.creatorOrCurLimitSpell}");
				}
			}
			catch (Exception ex)
			{
				AddLog($"[AuraDebug] DumpHeroEnchantments failed: {ex.GetType().Name}");
			}
		}

		private void DumpFriendlyMinionEnchantments(Board board, string reason)
		{
			try
			{
				if (board?.MinionFriend == null || board.MinionFriend.Count == 0)
					return;

				AddLog($"[AuraDebug] FriendlyMinions={board.MinionFriend.Count} | {reason}");
				foreach (var m in board.MinionFriend)
				{
					if (m?.Template == null)
						continue;

					var enchants = m.Enchantments;
					int count = enchants != null ? enchants.Count : 0;
					AddLog($"[AuraDebug] Minion={GetName(m.Template.Id)}({m.Template.Id}) enchants={count} atk/hp={m.CurrentAtk}/{m.CurrentHealth} canAttack={m.CanAttack}");
					if (enchants == null || enchants.Count == 0)
						continue;

					int idx = 0;
					foreach (var e in enchants)
					{
						idx++;
						if (e == null)
						{
							AddLog($"[AuraDebug]   #{idx}: <null>");
							continue;
						}

						string idName = GetName(e.Id);
						var enchantTemplate = e.EnchantCard?.Template;
						string enchantInfo = enchantTemplate != null
							? $"{GetName(enchantTemplate.Id)}({enchantTemplate.Id})"
							: "null";
						AddLog($"[AuraDebug]   #{idx}: Id={e.Id}({idName}) | EnchantCard={enchantInfo}");
					}
				}
			}
			catch (Exception ex)
			{
				AddLog($"[AuraDebug] DumpFriendlyMinionEnchantments failed: {ex.GetType().Name}");
			}
		}

		private void DumpHeroAuraDiff(Board board, string reason)
		{
			try
			{
				int? turnNow = null;
				try { turnNow = TryGetIntProperty(board, "Turn", "TurnNumber", "CurrentTurn", "TurnCount", "GameTurn"); } catch { /* ignore */ }

				var hero = board?.HeroFriend;
				if (hero == null)
					return;

				var curTags = new Dictionary<Card.GAME_TAG, int>();
				try
				{
					if (hero.tags != null)
					{
						foreach (var kv in hero.tags)
							curTags[kv.Key] = kv.Value;
					}
				}
				catch
				{
					// ignore
				}

				var curEnchantSig = new List<string>();
				try
				{
					var enchants = hero.Enchantments;
					if (enchants != null)
					{
						foreach (var e in enchants)
						{
							if (e == null)
								continue;
							var enchantTemplate = e.EnchantCard?.Template;
							var enchantCardId = enchantTemplate != null ? enchantTemplate.Id.ToString() : "null";
							curEnchantSig.Add($"{e.Id}|{enchantCardId}");
						}
					}
				}
				catch
				{
					// ignore
				}

				int curEstimate = GetActiveHeroAuraCount(board, turnNow);
				if (_lastHeroTags == null || _lastHeroEnchantSig == null)
				{
					_lastHeroTags = curTags;
					_lastHeroEnchantSig = curEnchantSig;
					_lastAuraEstimate = curEstimate;
					AddLog($"[AuraDiff] init | {reason}");
					return;
				}

				// 1) tags diff（只输出变化项）
				var changed = new List<string>();
				foreach (var kv in curTags)
				{
					int oldVal = _lastHeroTags.TryGetValue(kv.Key, out var v) ? v : 0;
					if (oldVal != kv.Value)
						changed.Add($"{kv.Key}:{oldVal}->{kv.Value}");
				}
				foreach (var kv in _lastHeroTags)
				{
					if (!curTags.ContainsKey(kv.Key) && kv.Value != 0)
						changed.Add($"{kv.Key}:{kv.Value}->0");
				}

				// 2) enchant diff（新增/移除签名）
				var curSet = new HashSet<string>(curEnchantSig);
				var oldSet = new HashSet<string>(_lastHeroEnchantSig);
				var added = curSet.Except(oldSet).Take(30).ToList();
				var removed = oldSet.Except(curSet).Take(30).ToList();

				int oldEstimate = _lastAuraEstimate ?? 0;
				if (changed.Count == 0 && added.Count == 0 && removed.Count == 0 && oldEstimate == curEstimate)
				{
					// 没变化就不刷屏
					return;
				}

				AddLog($"[AuraDiff] {reason} | auraEstimate {oldEstimate}->{curEstimate} | tagChanged={changed.Count} enchAdded={added.Count} enchRemoved={removed.Count}");
				if (changed.Count > 0)
					AddLog($"[AuraDiff] tags: {string.Join(", ", changed.Take(40))}");
				if (added.Count > 0)
					AddLog($"[AuraDiff] enchAdded: {string.Join(", ", added)}");
				if (removed.Count > 0)
					AddLog($"[AuraDiff] enchRemoved: {string.Join(", ", removed)}");

				_lastHeroTags = curTags;
				_lastHeroEnchantSig = curEnchantSig;
				_lastAuraEstimate = curEstimate;
			}
			catch (Exception ex)
			{
				AddLog($"[AuraDiff] failed: {ex.GetType().Name}");
			}
		}

		private void AddLog(string log)
		{
			_log += "\r\n" + log;
		}

		private static string GetName(Card.Cards id)
		{
			try
			{
				var t = CardTemplate.LoadFromId(id);
				return string.IsNullOrWhiteSpace(t?.NameCN) ? t?.Name ?? id.ToString() : t.NameCN;
			}
			catch
			{
				return id.ToString();
			}
		}

		private static bool IsFastOpponent(Card.CClass opponent)
		{
			switch (opponent)
			{
				case Card.CClass.DEMONHUNTER:
				case Card.CClass.HUNTER:
				case Card.CClass.ROGUE:
					return true;
				default:
					return false;
			}
		}

		private static bool IsSlowOpponent(Card.CClass opponent)
		{
			switch (opponent)
			{
				case Card.CClass.WARRIOR:
				case Card.CClass.PRIEST:
				case Card.CClass.DRUID:
					return true;
				default:
					return false;
			}
		}

		// 芬利/卡扎库斯接口：本套牌通常不会触发，但 SmartBot 需要实现
		public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
		{
			return choices != null && choices.Count > 0 ? choices[0] : default(Card.Cards);
		}

		public Card.Cards KazakusChoice(List<Card.Cards> choices)
		{
			return choices != null && choices.Count > 0 ? choices[0] : default(Card.Cards);
		}

		private static int? TryGetIntProperty(object instance, params string[] propertyNames)
		{
			if (instance == null || propertyNames == null)
				return null;

			var type = instance.GetType();
			foreach (var name in propertyNames)
			{
				try
				{
					var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
					if (prop == null)
						continue;

					var value = prop.GetValue(instance, null);
					if (value is int i)
						return i;
					if (value is long l)
						return (int)l;
					if (value is short s)
						return s;
				}
				catch
				{
					// ignore
				}
			}
			return null;
		}

		private static bool? TryGetBoolProperty(object instance, params string[] propertyNames)
		{
			if (instance == null || propertyNames == null)
				return null;

			var type = instance.GetType();
			foreach (var name in propertyNames)
			{
				try
				{
					var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
					if (prop == null)
						continue;

					var value = prop.GetValue(instance, null);
					if (value is bool b)
						return b;
				}
				catch
				{
					// ignore
				}
			}
			return null;
		}

		private static int GetTag(Card card, Card.GAME_TAG tag)
		{
			try
			{
				if (card?.tags == null)
					return 0;
				return card.tags.TryGetValue(tag, out var v) ? v : 0;
			}
			catch
			{
				return 0;
			}
		}
	}
}

