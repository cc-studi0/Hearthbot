using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
	[Serializable]
	public class DefaultMulliganProfile : MulliganProfile
	{
		// 留牌版本号：每次改动请递增（用于日志定位当前运行的是哪一版）
		// tip: 项目是 c#6，不要用新语法
		private const string MulliganVersion = "2026-01-06.015";

		private readonly List<Card.Cards> CardsToKeep = new List<Card.Cards>();

		public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
		{
			Bot.Log($"[Mulligan] 狂野蛋术 v{MulliganVersion} | 对手职业={opponentClass}");
			CardsToKeep.Clear();

			bool hasCoin = choices.Contains(Card.Cards.GAME_005);

			int tombCount = choices.Count(x => x == Card.Cards.TLC_451);
			int librarianCount = choices.Count(x => x == Card.Cards.LOOT_014);
			int eggbearerCount = choices.Count(x => x == Card.Cards.DINO_411);
			int eggCount = choices.Count(x => x == Card.Cards.DINO_410);

			int defileCount = choices.Count(x => x == Card.Cards.ICC_041);
			int darkPactCount = choices.Count(x => x == Card.Cards.LOOT_017);
			int ritualCount = choices.Count(x => x == Card.Cards.CS3_002);
			int grimoireCount = choices.Count(x => x == Card.Cards.BAR_910);
			int chaosConsumeCount = choices.Count(x => x == Card.Cards.TTN_932);
			int eatImpCount = choices.Count(x => x == Card.Cards.VAC_939);
			int dirtyRatCount = choices.Count(x => x == Card.Cards.CORE_CFM_790);

			bool hasEgg = eggCount > 0;
			bool hasEggbearer = eggbearerCount > 0;
			bool hasLibrarian = librarianCount > 0;

			// “有目标才能留自杀法术”：需要手里至少有一个可牺牲的随从。
			// 蛋术里大部分关键法术都要求先消灭友方随从。
			bool hasSacrificeTarget = hasEgg || hasEggbearer || hasLibrarian
				|| choices.Contains(Card.Cards.TSC_908) // 芬利
				|| choices.Contains(Card.Cards.CORE_CFM_790); // 脏鼠

			// 快攻倾向（用最朴素的职业粗分，后续可按日志再细化）
			bool isAggroLikely = opponentClass == Card.CClass.HUNTER
				|| opponentClass == Card.CClass.DEMONHUNTER
				|| opponentClass == Card.CClass.ROGUE
				|| opponentClass == Card.CClass.PALADIN
				|| opponentClass == Card.CClass.SHAMAN;

			bool isComboLikely = opponentClass == Card.CClass.MAGE
				|| opponentClass == Card.CClass.PRIEST
				|| opponentClass == Card.CClass.DRUID;

			// ===== T0：全力找蛋（核心） =====
			if (hasEgg)
			{
				KeepCopies(Card.Cards.DINO_410, eggCount);
				Bot.Log("[留牌] 必留：凯洛斯的蛋(DINO_410)");
			}
			if (hasEggbearer)
			{
				KeepCopies(Card.Cards.DINO_411, eggbearerCount);
				Bot.Log("[留牌] 必留：神圣布蛋者(DINO_411)（检索蛋）");
			}

			// 咒怨之墓：0费检索/补牌，起手强
			if (tombCount > 0)
			{
				// 避免起手两张过于重复，这里最多留1张
				KeepCopies(Card.Cards.TLC_451, 1);
				Bot.Log("[留牌] 必留：咒怨之墓(TLC_451)");
			}

			// 狗头人图书管理员：低费启动点 + 牺牲目标 + 过牌（最多留1张即可）
			if (librarianCount > 0)
			{
				KeepCopies(Card.Cards.LOOT_014, 1);
				Bot.Log("[留牌] 必留：狗头人图书管理员(LOOT_014)（低费启动/牺牲目标，最多留1）");
			}

			// ===== T1：有蛋/有检索时，留“破蛋组件”（尽量做到下蛋即破蛋） =====
			bool hasEggPlan = hasEgg || hasEggbearer;

			// 末日仪式：0费消灭友方随从（可用来下蛋同回合立刻破）
			if (hasEggPlan && ritualCount > 0)
			{
				KeepCopies(Card.Cards.CS3_002, ritualCount);
				Bot.Log("[留牌] 可留：末日仪式(CS3_002)（有蛋/检索时，0费破蛋）");
			}

			// 黑暗契约：破蛋+大回复；没有牺牲目标时不要留
			if (darkPactCount > 0 && (hasEggPlan || (isAggroLikely && hasSacrificeTarget)))
			{
				KeepCopies(Card.Cards.LOOT_017, darkPactCount);
				Bot.Log("[留牌] 可留：黑暗契约(LOOT_017)（破蛋/抗快攻，需牺牲目标）");
			}

			// 吃掉小鬼：破蛋抽三张；更偏向有蛋的起手（没蛋容易卡手）
			if (eatImpCount > 0 && hasEgg)
			{
				KeepCopies(Card.Cards.VAC_939, eatImpCount);
				Bot.Log("[留牌] 可留：吃掉小鬼！(VAC_939)（仅有蛋时留，破蛋抽三）");
			}

			// ===== T2：对快攻提高解场优先级（但仍遵守“需要牺牲目标”） =====
			if (isAggroLikely)
			{
				if (defileCount > 0)
				{
					KeepCopies(Card.Cards.ICC_041, defileCount);
					Bot.Log("[留牌] 抗快攻：亵渎(ICC_041)");
				}

				// 牺牲魔典：需要牺牲目标；有蛋/检索/图书管理员基本都能开出来
				if (grimoireCount > 0 && hasSacrificeTarget)
				{
					KeepCopies(Card.Cards.BAR_910, grimoireCount);
					Bot.Log("[留牌] 抗快攻：牺牲魔典(BAR_910)（需牺牲目标）");
				}

				// 混乱吞噬：点杀，需牺牲目标
				if (chaosConsumeCount > 0 && hasSacrificeTarget)
				{
					KeepCopies(Card.Cards.TTN_932, chaosConsumeCount);
					Bot.Log("[留牌] 抗快攻：混乱吞噬(TTN_932)（需牺牲目标）");
				}
			}

			// ===== 特定对局：脏鼠（偏 combo/控制） =====
			if (dirtyRatCount > 0 && isComboLikely)
			{
				// 不强制要求后手；只要对面像 combo，就留一个
				KeepCopies(Card.Cards.CORE_CFM_790, 1);
				Bot.Log("[留牌] 特定对局：卑劣的脏鼠(CORE_CFM_790)（偏 combo/控制）");
			}

			try
			{
				var keepNames = CardsToKeep.Select(id => CardTemplate.LoadFromId(id).NameCN + "(" + id + ")");
				Bot.Log("[留牌] 最终保留：" + string.Join(", ", keepNames));
			}
			catch
			{
				// ignore
			}

			return CardsToKeep;
		}

		// 允许保留同名多张
		private void KeepCopies(Card.Cards card, int count)
		{
			if (count <= 0) return;
			for (int i = 0; i < count; i++)
				CardsToKeep.Add(card);
		}
	}
}
