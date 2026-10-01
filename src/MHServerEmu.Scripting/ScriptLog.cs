using MHServerEmu.Core.Logging;

namespace MHServerEmu.Scripting
{
    /// <summary>
    /// Logger handed to scripts. Prefixes every message with the script name.
    /// </summary>
    public sealed class ScriptLog
    {
        private static readonly Logger Logger = LogManager.CreateLogger("Script");

        private readonly string _scriptName;

        public ScriptLog(string scriptName)
        {
            _scriptName = scriptName;
        }

        public void Info(string message) => Logger.Info($"[{_scriptName}] {message}");
        public void Warn(string message) => Logger.Warn($"[{_scriptName}] {message}");
        public void Error(string message) => Logger.Error($"[{_scriptName}] {message}");
    }
}
