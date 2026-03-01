using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
	[Serializable]
	public class STDAuraPaladinMulligan : MulliganProfile
	{
		private const string MulliganVersion = "2026-01-17.025";

		private readonly List<Card.Cards> _cardsToKeep = new List<Card.Cards>();
		private readonly Dictionary<Card.Cards, List<string>> _keepReasons = new Dictionary<Card.Cards, List<string>>();

		// ===== 牌表（来自 卡组攻略和修改记录/标准/光环骑/光环骑.md） =====
		private const Card.Cards Wisp = Card.Cards.CORE_CS2_231;                 // 小精灵
		private const Card.Cards RighteousProtector = Card.Cards.CORE_ICC_038;    // 正义保护者
		private const Card.Cards AccelerationAura = Card.Cards.END_011;           // 加速光环
		private const Card.Cards PurpleGillsMurloc = Card.Cards.TLC_438;          // 紫色珍鳃鱼人
		private const Card.Cards CardboardGolem = Card.Cards.TOY_809;             // 纸板魔像
		private const Card.Cards CrusaderAura = Card.Cards.TTN_908;               // 十字军光环
		private const Card.Cards Elise = Card.Cards.TLC_100;                      // 导航员伊莉斯
		private const Card.Cards GrizzlyHammer = Card.Cards.EDR_253;              // 巨熊之槌
		private const Card.Cards TimeFlowElemental = Card.Cards.TIME_019;         // 时间流具象

		public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
		{
			_cardsToKeep.Clear();
			_keepReasons.Clear();

			bool hasCoin = choices.Count >= 4;
			bool isFastOpponent = IsFastOpponent(opponentClass);
			bool isSlowOpponent = IsSlowOpponent(opponentClass);

			Bot.Log($"[Mulligan] 标准光环骑 v{MulliganVersion} | 对手职业={opponentClass} | {(hasCoin ? "后手" : "先手")} | 预估={(isFastOpponent ? "快" : (isSlowOpponent ? "慢" : "未知"))}");

			try
			{
				var choiceNames = choices
					.Select(c => $"{GetName(c)}({c}){CardTemplate.LoadFromId(c).Cost}")
					.ToList();
				Bot.Log("[Mulligan] 起手备选：" + string.Join(" | ", choiceNames));
			}
			catch
			{
				// ignore
			}

			// ===== 1) 通用必留（优先级最高） =====
			KeepIfOffered(choices, PurpleGillsMurloc, "通用必留：2费紫色珍鳃鱼人（拉≤2费法术抢节奏）");
			KeepIfOffered(choices, RighteousProtector, "通用必留：1费正义保护者（抗快攻/保场面）");
			KeepIfOffered(choices, AccelerationAura, "通用必留：2费加速光环（抢费发动机）");
			KeepIfOffered(choices, CardboardGolem, "通用必留：3费纸板魔像（延长光环收益）");

			// ===== 2) 曲线与对局倾向 =====
			bool hasEarlyMinion = HasAny(choices, RighteousProtector) || HasAny(choices, PurpleGillsMurloc) || HasAny(choices, Wisp);
			bool hasKeptEarlyAction = _cardsToKeep.Any(c => SafeCost(c) <= 2) || _cardsToKeep.Contains(CardboardGolem);

			// 对快攻额外倾向：小精灵
			if (isFastOpponent)
			{
				KeepIfOffered(choices, Wisp, "对快攻倾向：小精灵（补前期站场/接光环）");
			}

			// 可留：十字军光环（慢速/能站住随从时才留）
			if (isSlowOpponent && (hasEarlyMinion || _cardsToKeep.Contains(RighteousProtector) || _cardsToKeep.Contains(PurpleGillsMurloc)))
			{
				KeepIfOffered(choices, CrusaderAura, "可留：十字军光环（慢速对局且已有前期动作，争取雪球）");
			}

			// 可留：巨熊之槌/伊莉斯（慢速对局，且起手有前期动作/后手更容易留）
			if (isSlowOpponent && (hasKeptEarlyAction || hasCoin))
			{
				KeepIfOffered(choices, GrizzlyHammer, "可留：巨熊之槌（慢速对局续航；需已有前期动作/后手）");
				KeepIfOffered(choices, Elise, "可留：导航员伊莉斯（慢速对局资源点；需已有前期动作/后手）");
			}

			// 对快攻额外倾向：时间流具象（需能较稳定挂上光环，这里用“起手有加速光环”近似）
			if (isFastOpponent && _cardsToKeep.Contains(AccelerationAura))
			{
				KeepIfOffered(choices, TimeFlowElemental, "对快攻倾向：时间流具象（有加速光环时更易触发返场）");
			}

			// ===== 3) 按职业细化（以攻略为准，做轻量补正） =====
			switch (opponentClass)
			{
				case Card.CClass.DEMONHUNTER:
				case Card.CClass.HUNTER:
				case Card.CClass.ROGUE:
					// 偏快：不建议贪 4 费牌（除非已经很稳）
					if (!_cardsToKeep.Any(c => SafeCost(c) <= 2))
					{
						KeepIfOffered(choices, Wisp, "职业细化：对快攻职业，优先补前期动作（小精灵兜底）");
					}
					break;

				case Card.CClass.WARRIOR:
				case Card.CClass.PRIEST:
				case Card.CClass.DRUID:
					// 偏慢：若已具备前期动作，可更大胆留资源/雪球牌
					if (hasCoin && (hasEarlyMinion || hasKeptEarlyAction))
					{
						KeepIfOffered(choices, GrizzlyHammer, "职业细化：对慢速职业后手可留巨熊之槌（资源）");
						KeepIfOffered(choices, Elise, "职业细化：对慢速职业后手可留伊莉斯（资源）");
					}
					if (hasEarlyMinion)
					{
						KeepIfOffered(choices, CrusaderAura, "职业细化：对慢速职业且已能站住随从，可留十字军光环（雪球）");
					}
					break;

				case Card.CClass.PALADIN:
					// 内战：抢费+延长光环优先，十字军要有随从才留
					if (hasEarlyMinion)
					{
						KeepIfOffered(choices, CrusaderAura, "职业细化：内战且已有前期动作，可留十字军光环抢雪球");
					}
					break;

				default:
					break;
			}

			// ===== 4) 兜底：尽量保证至少一个前期动作（≤2费） =====
			if (!_cardsToKeep.Any(c => SafeCost(c) <= 2))
			{
				var fallback = choices
					.OrderBy(SafeCost)
					.FirstOrDefault(c => SafeCost(c) <= 2);

				if (fallback != default(Card.Cards))
				{
					Keep(fallback, "兜底：起手未留到≤2费牌，优先保留最低费动作");
				}
			}

			// ===== 输出复盘日志 =====
			try
			{
				var keepNames = _cardsToKeep.Select(c => $"{GetName(c)}({c}){SafeCost(c)}").ToList();
				Bot.Log("[留牌] 最终保留：" + (keepNames.Count == 0 ? "（空）" : string.Join(" | ", keepNames)));
				foreach (var kv in _keepReasons)
				{
					Bot.Log($"[留牌] {GetName(kv.Key)}({kv.Key})：" + string.Join("；", kv.Value.Distinct()));
				}
			}
			catch
			{
				// ignore
			}

			return _cardsToKeep;
		}

		private void KeepIfOffered(List<Card.Cards> choices, Card.Cards card, string reason)
		{
			if (!HasAny(choices, card))
				return;

			Keep(card, reason);
		}

		private void Keep(Card.Cards card, string reason)
		{
			if (!_cardsToKeep.Contains(card))
				_cardsToKeep.Add(card);

			if (!_keepReasons.ContainsKey(card))
				_keepReasons[card] = new List<string>();

			_keepReasons[card].Add(reason);
		}

		private static bool HasAny(List<Card.Cards> choices, Card.Cards card)
		{
			return choices != null && choices.Contains(card);
		}

		private static int SafeCost(Card.Cards card)
		{
			try
			{
				return CardTemplate.LoadFromId(card).Cost;
			}
			catch
			{
				return 99;
			}
		}

		private static string GetName(Card.Cards card)
		{
			try
			{
				var t = CardTemplate.LoadFromId(card);
				return string.IsNullOrWhiteSpace(t?.NameCN) ? t?.Name ?? card.ToString() : t.NameCN;
			}
			catch
			{
				return card.ToString();
			}
		}

		// 快攻：更看重“站场+保血+返场”，通常不贪 4 费资源牌
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

		// 慢速：更看重“抢费+资源+雪球光环”，可更大胆留 4 费关键牌
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
	}
}



