using System;

namespace BotMain
{
    public static class PassParser
    {
        private const string Prefix = "PASS_INFO:";

        public static bool TryParsePassInfoResponse(
            string response, out int level, out int xp, out int xpNeeded)
        {
            level = 0;
            xp = 0;
            xpNeeded = 0;

            if (string.IsNullOrWhiteSpace(response)
                || !response.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = response.Substring(Prefix.Length);
            var parts = payload.Split('|');
            if (parts.Length < 3)
                return false;

            if (!int.TryParse(parts[0], out level))
                return false;
            if (!int.TryParse(parts[1], out xp))
            {
                level = 0;
                return false;
            }
            if (!int.TryParse(parts[2], out xpNeeded))
            {
                level = 0;
                xp = 0;
                return false;
            }

            return true;
        }
    }
}
