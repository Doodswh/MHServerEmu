using System.Net;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.AccountDashboard;
using MHServerEmu.WebFrontend.Models;
using MHServerEmu.WebFrontend.RemoteConsole;

namespace MHServerEmu.WebFrontend.Handlers.RemoteConsole
{
    public class RemoteConsoleLoginWebHandler : RemoteConsoleHandlerBase
    {
        protected override async Task Post(WebRequestContext context)
        {
            if (await RequireWhitelistedIpAsync(context) == false)
                return;

            RemoteConsoleLoginRequest request = await context.ReadJsonAsync<RemoteConsoleLoginRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.SendJsonAsync(new { authenticated = false, whitelisted = true, message = "Username and password are required." });
                return;
            }

            if (RemoteConsoleAuthManager.Instance.TryLogin(context, request.Username, request.Password, out RemoteConsoleSession session, out string message) == false)
            {
                context.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.SendJsonAsync(new { authenticated = false, whitelisted = true, message });
                return;
            }

            await context.SendJsonAsync(new
            {
                authenticated = true,
                whitelisted = true,
                message,
                token = session.Token,
                session = new
                {
                    username = session.Username,
                    ipAddress = session.IpAddress,
                    expiresAtUtc = session.ExpiresAtUtc
                }
            });
        }
    }

    public class RemoteConsoleAccountSessionLoginWebHandler : RemoteConsoleHandlerBase
    {
        protected override async Task Post(WebRequestContext context)
        {
            if (await RequireWhitelistedIpAsync(context) == false)
                return;

            string accountToken = context.GetBearerToken();
            if (AccountDashboardSessionManager.TryGetSession(accountToken, out AccountDashboardSession accountSession, out string accountMessage) == false)
            {
                context.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.SendJsonAsync(new { authenticated = false, whitelisted = true, message = accountMessage });
                return;
            }

            if (RemoteConsoleAuthManager.Instance.TryCreateSessionForAccount(context, accountSession.Account, out RemoteConsoleSession session, out string message) == false)
            {
                context.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.SendJsonAsync(new { authenticated = false, whitelisted = true, message });
                return;
            }

            await context.SendJsonAsync(new
            {
                authenticated = true,
                whitelisted = true,
                message,
                token = session.Token,
                session = new
                {
                    username = session.Username,
                    ipAddress = session.IpAddress,
                    expiresAtUtc = session.ExpiresAtUtc
                }
            });
        }
    }
}
