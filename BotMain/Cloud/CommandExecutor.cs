using System;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BotMain.Cloud
{
    public class CommandExecutor
    {
        private readonly BotService _bot;
        private readonly AccountController _accounts;
        private readonly CloudAgent _agent;
        private readonly Action<string> _log;
        private readonly ConcurrentQueue<(int Id, string Type, string Payload)> _pendingCommands = new();

        public CommandExecutor(BotService bot, AccountController accounts, CloudAgent agent, Action<string> log)
        {
            _bot = bot;
            _accounts = accounts;
            _agent = agent;
            _log = log;

            _agent.OnCommandReceived += (id, type, payload) =>
            {
                _pendingCommands.Enqueue((id, type, payload));
                _log($"[云控] 指令已缓存: {type} (id={id})，将在当局结束后执行");
            };
        }

        public void ProcessPendingCommands()
        {
            while (_pendingCommands.TryDequeue(out var cmd))
            {
                try
                {
                    ExecuteCommand(cmd.Id, cmd.Type, cmd.Payload);
                    _ = _agent.AckCommandAsync(cmd.Id, true);
                    _log($"[云控] 指令执行成功: {cmd.Type} (id={cmd.Id})");
                }
                catch (Exception ex)
                {
                    _ = _agent.AckCommandAsync(cmd.Id, false, ex.Message);
                    _log($"[云控] 指令执行失败: {cmd.Type} (id={cmd.Id}): {ex.Message}");
                }
            }
        }

        private void ExecuteCommand(int id, string type, string payload)
        {
            switch (type)
            {
                case "Start":
                    _bot.Start();
                    break;

                case "Stop":
                    _bot.Stop();
                    break;

                case "ChangeDeck":
                {
                    using var doc = JsonDocument.Parse(payload);
                    var deckName = doc.RootElement.GetProperty("DeckName").GetString() ?? "";
                    var profileName = doc.RootElement.TryGetProperty("ProfileName", out var pn)
                        ? pn.GetString() ?? "" : "";
                    var account = _accounts.CurrentAccount;
                    if (account != null)
                    {
                        account.DeckName = deckName;
                        if (!string.IsNullOrEmpty(profileName))
                            account.ProfileName = profileName;
                        _accounts.Save();
                        _log($"[云控] 卡组已切换为: {deckName}");
                    }
                    break;
                }

                case "ChangeTarget":
                {
                    using var doc = JsonDocument.Parse(payload);
                    var targetStarLevel = doc.RootElement.GetProperty("TargetRankStarLevel").GetInt32();
                    var account = _accounts.CurrentAccount;
                    if (account != null)
                    {
                        account.TargetRankStarLevel = targetStarLevel;
                        _accounts.Save();
                        _log($"[云控] 目标段位已更新");
                    }
                    break;
                }

                default:
                    _log($"[云控] 未知指令类型: {type}");
                    break;
            }
        }
    }
}
