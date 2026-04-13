namespace HearthBot.Cloud.Services;

public static class DeviceStatusPolicy
{
    public static readonly TimeSpan OfflineTimeout = TimeSpan.FromSeconds(150);
    public static readonly TimeSpan SwitchingAbnormalTimeout = TimeSpan.FromSeconds(180);
}
