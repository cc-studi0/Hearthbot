using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Discover
{
    [Serializable]
    public class HSAngDiscover : DiscoverPickHandler
    {
        /// <summary>
        /// Reads HSAng's choice recommendation from the bridge and picks
        /// the card matching target_cardId or target_position.
        /// Falls back to CRED_01 if no recommendation is available.
        /// </summary>
        public Card.Cards HandlePickDecision(Card.Cards originCard, List<Card.Cards> choices, Board board)
        {
            try
            {
                Bot.Log("[HSAng-Discover] ENTER: origin=" + originCard + " choices=[" + string.Join(",", choices) + "]");

                // Wait up to 3 seconds for HSAng to produce a choice recommendation
                string json = null;
                for (int wait = 0; wait < 6; wait++)
                {
                    json = Bot._hsAngRecommendations;
                    if (!string.IsNullOrEmpty(json) && json.Contains("\"choice\""))
                        break;
                    json = null;
                    System.Threading.Thread.Sleep(500);
                }

                if (string.IsNullOrEmpty(json))
                {
                    Bot.Log("[HSAng-Discover] No choice recommendation after 3s, falling back to default");
                    return Card.Cards.CRED_01;
                }

                Bot.Log("[HSAng-Discover] Bridge JSON: " + json);

                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                var recs = obj["rec"] as Newtonsoft.Json.Linq.JArray;
                if (recs == null || recs.Count == 0)
                {
                    Bot.Log("[HSAng-Discover] No recs in JSON, falling back");
                    return Card.Cards.CRED_01;
                }

                // Find the choice rec
                string targetCardId = null;
                int targetPosition = 0;
                foreach (var rec in recs)
                {
                    if ((string)rec["option"] == "choice")
                    {
                        targetCardId = (string)rec["target_cardId"] ?? (string)rec["cardId"] ?? "";
                        targetPosition = rec["target_position"] != null ? (int)rec["target_position"] :
                                        (rec["position"] != null ? (int)rec["position"] : 0);
                        break;
                    }
                }

                if (string.IsNullOrEmpty(targetCardId) && targetPosition <= 0)
                {
                    Bot.Log("[HSAng-Discover] No target in choice rec, falling back");
                    return Card.Cards.CRED_01;
                }

                Bot.Log("[HSAng-Discover] HSAng recommends: cardId=" + targetCardId + " position=" + targetPosition);

                // Try to match by cardId first
                if (!string.IsNullOrEmpty(targetCardId))
                {
                    Card.Cards targetEnum;
                    if (Enum.TryParse(targetCardId, out targetEnum))
                    {
                        if (choices.Contains(targetEnum))
                        {
                            Bot.Log("[HSAng-Discover] Matched by cardId: " + targetEnum);
                            return targetEnum;
                        }
                        Bot.Log("[HSAng-Discover] cardId " + targetCardId + " parsed but not in choices");
                    }
                    else
                    {
                        Bot.Log("[HSAng-Discover] cardId " + targetCardId + " failed to parse as Card.Cards enum");
                    }
                }

                // Fallback: match by position (1-indexed)
                if (targetPosition >= 1 && targetPosition <= choices.Count)
                {
                    var pick = choices[targetPosition - 1];
                    Bot.Log("[HSAng-Discover] Matched by position " + targetPosition + ": " + pick);
                    return pick;
                }

                Bot.Log("[HSAng-Discover] No match found, falling back to default");
                return Card.Cards.CRED_01;
            }
            catch (Exception e)
            {
                Bot.Log("[HSAng-Discover] Error: " + e.Message + ", falling back");
                return Card.Cards.CRED_01;
            }
        }
    }
}
