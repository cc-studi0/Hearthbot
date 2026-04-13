using HearthBot.Cloud.Models;
using Xunit;

namespace BotCore.Tests.Cloud;

public class HiddenDeviceEntryTests
{
    [Fact]
    public async Task HideLiveDevice_HidesMatchingIdentityOnly()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-02",
            CurrentAccount = "账号B",
            OrderNumber = "B-1002",
            Status = "Running"
        });
        await env.Db.SaveChangesAsync();

        var hiddenDevices = env.CreateHiddenDevices();
        var hidden = await hiddenDevices.HideAsync("pc-02", "账号B", "B-1002");
        var visible = await hiddenDevices.IsVisibleAsync("pc-02", "账号B", "B-1002");

        Assert.NotNull(hidden);
        Assert.False(visible);
    }

    [Fact]
    public async Task HideEntry_BecomesIneffectiveWhenIdentityChanges()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        var hiddenDevices = env.CreateHiddenDevices();
        await hiddenDevices.HideAsync("pc-03", "账号A", "A-1");

        var sameIdentityVisible = await hiddenDevices.IsVisibleAsync("pc-03", "账号A", "A-1");
        var changedIdentityVisible = await hiddenDevices.IsVisibleAsync("pc-03", "账号C", "C-9");

        Assert.False(sameIdentityVisible);
        Assert.True(changedIdentityVisible);
    }
}
