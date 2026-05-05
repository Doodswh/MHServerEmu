using System.Collections.Specialized;
using System.Net;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.WebFrontend.AccountDashboard;

namespace MHServerEmu.WebFrontend.Handlers.AccountDashboard
{
    public sealed class AccountDashboardAdminPageWebHandler : WebHandler
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly string _filePath;
        private byte[] _data = Array.Empty<byte>();

        public AccountDashboardAdminPageWebHandler(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

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
            AccountDashboardSession session = await RequireAdminSessionAsync(context, null);
            if (session == null)
                return;

            await context.SendAsync(_data, "text/html");
        }

        protected override async Task Post(WebRequestContext context)
        {
            NameValueCollection form = await context.ReadQueryStringAsync();
            string token = form?["token"];

            AccountDashboardSession session = await RequireAdminSessionAsync(context, token);
            if (session == null)
                return;

            await context.SendAsync(_data, "text/html");
        }

        private static async Task<AccountDashboardSession> RequireAdminSessionAsync(WebRequestContext context, string tokenOverride)
        {
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
                return null;
            }

            if (session.Account.UserLevel != AccountUserLevel.Admin)
            {
                context.StatusCode = (int)HttpStatusCode.Forbidden;
                await context.SendJsonAsync(new
                {
                    authenticated = true,
                    message = "Admin access is required.",
                });
                return null;
            }

            return session;
        }
    }
}