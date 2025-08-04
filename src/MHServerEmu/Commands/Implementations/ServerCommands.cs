using System.Text;
using Gazillion;
using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games.GameData.LiveTuning;

namespace MHServerEmu.Commands.Implementations
{
    [CommandGroup("server")]
    [CommandGroupDescription("Server management commands.")]
    public class ServerCommands : CommandGroup
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        [Command("status")]
        [CommandDescription("Prints server status.")]
        [CommandUsage("server status")]
        public string Status(string[] @params, NetClient client)
        {
            StringBuilder sb = new();
            sb.AppendLine("Server Status");
            sb.AppendLine(ServerApp.VersionInfo);
            sb.Append(ServerManager.Instance.GetServerStatus(client == null));
            string status = sb.ToString();

            // Display in the console as is
            if (client == null)
                return status;

            // Split for the client chat window
            CommandHelper.SendMessageSplit(client, status, false);
            return string.Empty;
        }

        [Command("broadcast")]
        [CommandDescription("Broadcasts a notification to all players.")]
        [CommandUsage("server broadcast")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandParamCount(1)]
        public string Broadcast(string[] @params, NetClient client)
        {
            var groupingManager = ServerManager.Instance.GetGameService(GameServiceType.GroupingManager) as IMessageBroadcaster;
            if (groupingManager == null) return "Failed to connect to the grouping manager.";

            string message = string.Join(' ', @params);

            groupingManager.BroadcastMessage(ChatServerNotification.CreateBuilder().SetTheMessage(message).Build());
            Logger.Trace($"Broadcasting server notification: \"{message}\"");

            return string.Empty;
        }

        [Command("reloadlivetuning")]
        [CommandDescription("Reloads live tuning settings.")]
        [CommandUsage("server reloadlivetuning")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.ServerConsole)]
        public string ReloadLiveTuning(string[] @params, NetClient client)
        {
            LiveTuningManager.Instance.LoadLiveTuningDataFromDisk();
            return string.Empty;
        }

        [Command("shutdown")]
        [CommandDescription("Shuts the server down.")]
        [CommandUsage("server shutdown")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        public string Shutdown(string[] @params, NetClient client)
        {
            string shutdownRequester = client == null ? "the server console" : client.ToString();
            Logger.Info($"Server shutdown request received from {shutdownRequester}");

            // Schedule shutdown with proper state handling
            _ = Task.Run(async () =>
            {
                try
                {
                    // Small delay to ensure command response is sent
                    await Task.Delay(500);
                    Logger.Info("Executing scheduled server shutdown...");

                    // Wait for ServerManager to reach Running state before attempting shutdown
                    var serverManager = ServerManager.Instance;
                    int maxWaitTime = 30000; // 30 seconds max wait
                    int checkInterval = 100;  // Check every 100ms
                    int totalWaitTime = 0;

                    // Get the current state using reflection since it's private
                    var stateField = typeof(ServerManager).GetField("_state",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    while (totalWaitTime < maxWaitTime)
                    {
                        var currentState = stateField?.GetValue(serverManager);

                        // Check if we're in a state that allows shutdown
                        if (currentState?.ToString() == "Running")
                        {
                            Logger.Info("ServerManager is in Running state, proceeding with shutdown...");
                            ServerApp.Instance.Shutdown();
                            return;
                        }
                        else if (currentState?.ToString() == "ShuttingDown" || currentState?.ToString() == "Shutdown")
                        {
                            Logger.Info($"ServerManager is already {currentState}, shutdown already in progress.");
                            return;
                        }

                        // Log progress every 5 seconds
                        if (totalWaitTime > 0 && totalWaitTime % 5000 == 0)
                        {
                            Logger.Info($"Waiting for ServerManager to reach Running state (currently {currentState})... {totalWaitTime / 1000}s elapsed");
                        }

                        await Task.Delay(checkInterval);
                        totalWaitTime += checkInterval;
                    }

                    // If we get here, we timed out waiting for Running state
                    Logger.Warn($"Timeout waiting for ServerManager to reach Running state. Current state: {stateField?.GetValue(serverManager)}");
                    Logger.Warn("Attempting graceful shutdown anyway...");

                    try
                    {
                        ServerApp.Instance.Shutdown();
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid state"))
                    {
                        Logger.Error($"Graceful shutdown failed due to state issue: {ex.Message}");
                        Logger.Warn("Forcing application exit...");
                        Environment.Exit(0); // Use exit code 0 for intentional shutdown
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error during shutdown process: {ex.Message}");
                    Logger.Error($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(1);
                }
            });

            return "Server shutdown initiated, waiting for services to be ready...";
        }
    }
}
