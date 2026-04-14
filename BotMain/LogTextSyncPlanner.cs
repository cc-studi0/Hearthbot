namespace BotMain
{
    internal enum LogTextSyncMode
    {
        None,
        Append,
        Replace
    }

    internal readonly struct LogTextSyncPlan
    {
        public LogTextSyncPlan(LogTextSyncMode mode, string text)
        {
            Mode = mode;
            Text = text ?? string.Empty;
        }

        public LogTextSyncMode Mode { get; }

        public string Text { get; }
    }

    internal static class LogTextSyncPlanner
    {
        public static LogTextSyncPlan Build(string currentText, string nextText)
        {
            currentText ??= string.Empty;
            nextText ??= string.Empty;

            if (currentText == nextText)
                return new LogTextSyncPlan(LogTextSyncMode.None, string.Empty);

            if (nextText.StartsWith(currentText, System.StringComparison.Ordinal))
                return new LogTextSyncPlan(LogTextSyncMode.Append, nextText.Substring(currentText.Length));

            return new LogTextSyncPlan(LogTextSyncMode.Replace, nextText);
        }
    }
}
