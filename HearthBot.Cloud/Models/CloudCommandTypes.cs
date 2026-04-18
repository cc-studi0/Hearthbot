using System;
using System.Collections.Generic;

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

    // 服务端主动推送：通知客户端有新版本。不允许从管理面板 Send 接口下发。
    public const string UpdateAvailable = "UpdateAvailable";

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
