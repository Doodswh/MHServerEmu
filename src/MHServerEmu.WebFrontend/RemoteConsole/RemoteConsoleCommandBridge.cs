using System.Reflection;
using MHServerEmu.Core.Network;

namespace MHServerEmu.WebFrontend.RemoteConsole
{
    public static class RemoteConsoleCommandBridge
    {
        public static bool TryExecute(string command, out string output, out string errorMessage)
        {
            output = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(command))
            {
                errorMessage = "A command string is required.";
                return false;
            }

            Type commandManagerType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType("MHServerEmu.Commands.CommandManager", false))
                .FirstOrDefault(type => type != null);

            if (commandManagerType == null)
            {
                errorMessage = "Command manager is not available.";
                return false;
            }

            object instance = commandManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo invokeCommandMethod = commandManagerType.GetMethod("InvokeCommand", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string), typeof(NetClient) }, null);

            if (instance == null || invokeCommandMethod == null)
            {
                errorMessage = "Command manager bridge is incomplete.";
                return false;
            }

            if (TryExtractCommandAndParameters(command, out string commandName, out string parameters) == false)
            {
                errorMessage = "Command was not recognized. Remote console commands must start with !.";
                return false;
            }

            output = invokeCommandMethod.Invoke(instance, new object[] { commandName, parameters, null }) as string ?? string.Empty;

            if (string.Equals(output, $"Unknown command: {commandName} {parameters}", StringComparison.Ordinal))
            {
                errorMessage = "Command was not recognized. Remote console commands must start with !.";
                output = string.Empty;
                return false;
            }

            return true;
        }

        private static bool TryExtractCommandAndParameters(string input, out string command, out string parameters)
        {
            command = string.Empty;
            parameters = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            ReadOnlySpan<char> span = input.AsSpan().Trim();
            if (span.Length < 2 || span[0] != '!')
                return false;

            int whiteSpaceIndex = span.IndexOf(' ');
            int commandLength = whiteSpaceIndex >= 0 ? whiteSpaceIndex - 1 : span.Length - 1;
            command = span.Slice(1, commandLength).ToString();

            if (whiteSpaceIndex >= 0)
                parameters = span.Slice(whiteSpaceIndex + 1).ToString();

            return true;
        }
    }
}
