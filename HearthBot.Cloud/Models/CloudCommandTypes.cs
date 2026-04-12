namespace HearthBot.Cloud.Models;

public static class CloudCommandTypes
{
    public const string Start = "Start";
    public const string Stop = "Stop";
    public const string ChangeDeck = "ChangeDeck";
    public const string ChangeProfile = "ChangeProfile";
    public const string ChangeTarget = "ChangeTarget";
    public const string Concede = "Concede";
    public const string Restart = "Restart";

    public static readonly HashSet<string> Valid = new(StringComparer.OrdinalIgnoreCase)
    {
        Start,
        Stop,
        ChangeDeck,
        ChangeProfile,
        ChangeTarget,
        Concede,
        Restart
    };
}
