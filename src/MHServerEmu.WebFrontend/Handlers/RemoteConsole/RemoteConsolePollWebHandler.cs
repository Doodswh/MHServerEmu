using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.RemoteConsole;

namespace MHServerEmu.WebFrontend.Handlers.RemoteConsole
{
    public class RemoteConsolePollWebHandler : RemoteConsoleHandlerBase
    {
        private readonly RemoteConsoleLogBuffer _logBuffer;

        public RemoteConsolePollWebHandler(RemoteConsoleLogBuffer logBuffer)
        {
            _logBuffer = logBuffer;
        }

        protected override async Task Get(WebRequestContext context)
        {
            if (await RequireSessionAsync(context) == null)
                return;

            var query = await context.ReadQueryStringAsync();
            long.TryParse(query["since"], out long sinceSequence);
            int.TryParse(query["limit"], out int limit);
            if (limit <= 0)
                limit = 200;

            RemoteConsolePollSnapshot snapshot = _logBuffer.GetSnapshot(sinceSequence, limit);

            using var statusDictHandle = DictionaryPool<string, long>.Instance.Get(out Dictionary<string, long> statusDict);
            ServerManager.Instance.GetServerStatus(statusDict);

            await context.SendJsonAsync(new
            {
                latestSequence = snapshot.LatestSequence,
                logs = snapshot.Entries,
                status = statusDict
            });
        }
    }
}
