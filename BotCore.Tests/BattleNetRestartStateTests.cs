using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BattleNetRestartStateTests
    {
        [Fact]
        public void BindingValidator_ReturnsMissingBinding_WhenProcessIdIsNull()
        {
            var binding = new BattleNetRestartBinding(null, string.Empty);

            var result = BattleNetRestartBindingValidator.Validate(binding, _ => true);

            Assert.False(result.Success);
            Assert.Equal(BattleNetRestartFailureKind.MissingBinding, result.FailureKind);
            Assert.Contains("未绑定战网实例", result.Message);
        }

        [Fact]
        public void BindingValidator_ReturnsProcessExited_WhenBattleNetProcessIsGone()
        {
            var binding = new BattleNetRestartBinding(1234, "Battle.net");

            var result = BattleNetRestartBindingValidator.Validate(binding, _ => false);

            Assert.False(result.Success);
            Assert.Equal(BattleNetRestartFailureKind.ProcessExited, result.FailureKind);
            Assert.Contains("PID=1234", result.Message);
        }

        [Fact]
        public void BindingValidator_ReturnsSuccess_WhenBoundBattleNetProcessIsAlive()
        {
            var binding = new BattleNetRestartBinding(5678, "账号A");

            var result = BattleNetRestartBindingValidator.Validate(binding, _ => true);

            Assert.True(result.Success);
            Assert.Equal(5678, result.BattleNetProcessId);
            Assert.Equal(BattleNetRestartFailureKind.None, result.FailureKind);
        }
    }
}
