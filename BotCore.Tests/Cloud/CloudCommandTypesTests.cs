using HearthBot.Cloud.Models;
using Xunit;

namespace BotCore.Tests.Cloud;

public class CloudCommandTypesTests
{
    [Theory]
    [InlineData(CloudCommandTypes.Start)]
    [InlineData(CloudCommandTypes.Stop)]
    [InlineData(CloudCommandTypes.ChangeDeck)]
    [InlineData(CloudCommandTypes.ChangeProfile)]
    [InlineData(CloudCommandTypes.ChangeTarget)]
    public void ValidCommands_ContainsDashboardDetailActions(string commandType)
    {
        Assert.Contains(commandType, CloudCommandTypes.Valid);
    }
}
