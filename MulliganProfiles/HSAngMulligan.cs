using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class HSAngMulligan : MulliganProfile
    {
        /// <summary>
        /// Reads HSAng's mulligan recommendation from the bridge and returns
        /// the cards to KEEP (everything not in HSAng's replace list).
        /// </summary>
        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            var cardsToKeep = new List<Card.Cards>();

            try
            {
                Bot.Log("[HSAng-Mulligan] ENTER: choices=[" + string.Join(",", choices) + "] opponent=" + opponentClass + " own=" + ownClass);

                // Wait up to 15 seconds for HSAng to produce a mulligan recommendation
                string json = null;
                for (int wait = 0; wait < 30; wait++)
                {
                    json = Bot._hsAngRecommendations;
                    if (!string.IsNullOrEmpty(json) && json.Contains("\"replace\""))
                        break;
                    json = null;
                    System.Threading.Thread.Sleep(500);
                }

                if (string.IsNullOrEmpty(json))
                {
                    Bot.Log("[HSAng-Mulligan] No HSAng recommendation after 15s, keeping all cards");
                    return new List<Card.Cards>(choices);
                }

                Bot.Log("[HSAng-Mulligan] Bridge JSON: " + json);

                // Parse the rec to find the replace recommendation
                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                var recs = obj["rec"] as Newtonsoft.Json.Linq.JArray;
                if (recs == null || recs.Count == 0)
                {
                    Bot.Log("[HSAng-Mulligan] No recs in JSON, keeping all cards");
                    return new List<Card.Cards>(choices);
                }

                // Find the replace rec
                string replaceCardIds = null;
                foreach (var rec in recs)
                {
                    if ((string)rec["option"] == "replace")
                    {
                        replaceCardIds = (string)rec["cardIds"] ?? "";
                        break;
                    }
                }

                if (string.IsNullOrEmpty(replaceCardIds))
                {
                    Bot.Log("[HSAng-Mulligan] No replace rec found, keeping all cards");
                    return new List<Card.Cards>(choices);
                }

                // Parse the cardIds to throw away
                var throwAwayIds = replaceCardIds.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                Bot.Log("[HSAng-Mulligan] HSAng says throw away: [" + string.Join(",", throwAwayIds) + "]");

                // Build the keep list: everything in choices NOT in the throw-away list
                // Use a copy of throwAwayIds so we can remove matches (handles duplicates correctly)
                var throwAwayCopy = new List<string>(throwAwayIds);
                foreach (var card in choices)
                {
                    string cardStr = card.ToString();
                    if (throwAwayCopy.Contains(cardStr))
                    {
                        // This card should be thrown away — remove from copy to handle duplicates
                        throwAwayCopy.Remove(cardStr);
                        Bot.Log("[HSAng-Mulligan] TOSS: " + cardStr);
                    }
                    else
                    {
                        cardsToKeep.Add(card);
                        Bot.Log("[HSAng-Mulligan] KEEP: " + cardStr);
                    }
                }

                Bot.Log("[HSAng-Mulligan] Result: keeping " + cardsToKeep.Count + "/" + choices.Count + " cards: [" + string.Join(",", cardsToKeep) + "]");
            }
            catch (Exception e)
            {
                Bot.Log("[HSAng-Mulligan] Error: " + e.Message + ", keeping all cards");
                return new List<Card.Cards>(choices);
            }

            return cardsToKeep;
        }
    }
}
