using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network.Web;

namespace MHServerEmu.WebFrontend.Handlers.RemoteConsole
{
    public class RemoteConsoleDashboardWebHandler : RemoteConsoleHandlerBase
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly string _filePath;
        private byte[] _data = Array.Empty<byte>();

        public RemoteConsoleDashboardWebHandler(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        public void Load()
        {
            if (File.Exists(_filePath) == false)
            {
                Logger.Warn($"Load(): File not found at '{_filePath}'");
                return;
            }

            _data = File.ReadAllBytes(_filePath);
        }

        protected override async Task Get(WebRequestContext context)
        {
            if (await RequireWhitelistedIpAsync(context) == false)
                return;

            await context.SendAsync(_data, "text/html");
        }
    }
}
