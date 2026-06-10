using System.Collections.Specialized;
using System.Net;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.WebFrontend.AccountDashboard;
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
            if (await RequireAdminSessionAsync(context, null) == false)
                return;

            await context.SendAsync(_data, "text/html");
        }

        protected override async Task Post(WebRequestContext context)
        {
            NameValueCollection form = await context.ReadQueryStringAsync();
            string token = form?["token"];

            if (await RequireAdminSessionAsync(context, token) == false)
                return;

            await context.SendAsync(_data, "text/html");
        }

        private async Task<bool> RequireAdminSessionAsync(WebRequestContext context, string tokenOverride)
        {
            if (await RequireWhitelistedIpAsync(context) == false)
                return false;

            bool hasSession = string.IsNullOrWhiteSpace(tokenOverride)
                ? AccountDashboardSessionManager.TryGetSession(context, out AccountDashboardSession session, out string message)
                : AccountDashboardSessionManager.TryGetSession(tokenOverride, out session, out message);

            if (hasSession == false)
            {
                context.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.SendJsonAsync(new
                {
                    authenticated = false,
                    message,
                });
                return false;
            }

            if (session.Account.UserLevel < AccountUserLevel.Admin)
            {
                context.StatusCode = (int)HttpStatusCode.Forbidden;
                await context.SendJsonAsync(new
                {
                    authenticated = true,
                    message = "Admin access is required.",
                });
                return false;
            }

            return true;
        }
    }
}
