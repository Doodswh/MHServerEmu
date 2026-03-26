using System.Net;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.AccountDashboard;
using MHServerEmu.WebFrontend.Models;

namespace MHServerEmu.WebFrontend.Handlers.AccountDashboard
{
    public abstract class AccountDashboardHandlerBase : WebHandler
    {
        protected async Task<AccountDashboardSession> RequireSessionAsync(WebRequestContext context)
        {
            if (AccountDashboardSessionManager.TryGetSession(context, out AccountDashboardSession session, out string message))
                return session;

            context.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.SendJsonAsync(new
            {
                authenticated = false,
                message,
            });

            return null;
        }

        protected static object BuildSessionResponse(AccountDashboardSession session, string message, bool includeToken)
        {
            return new
            {
                authenticated = true,
                token = includeToken ? session.Token : null,
                expiresAtUtc = session.ExpiresAtUtc.UtcDateTime.ToString("O"),
                message,
                account = new
                {
                    email = session.Account.Email,
                    playerName = session.Account.PlayerName,
                    accountId = session.Account.Id.ToString(),
                    userLevel = session.Account.UserLevel.ToString(),
                    flags = session.Account.Flags.ToString(),
                },
            };
        }
    }

    public class AccountDashboardLoginWebHandler : AccountDashboardHandlerBase
    {
        protected override async Task Post(WebRequestContext context)
        {
            AccountDashboardLoginRequest request = await context.ReadJsonAsync<AccountDashboardLoginRequest>();
            if (AccountDashboardSessionManager.TryLogin(request.Email, request.Password, out AccountDashboardSession session, out string message) == false)
            {
                context.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.SendJsonAsync(new
                {
                    authenticated = false,
                    message,
                });
                return;
            }

            await context.SendJsonAsync(BuildSessionResponse(session, message, true));
        }
    }

    public class AccountDashboardSessionWebHandler : AccountDashboardHandlerBase
    {
        protected override async Task Get(WebRequestContext context)
        {
            AccountDashboardSession session = await RequireSessionAsync(context);
            if (session == null)
                return;

            await context.SendJsonAsync(BuildSessionResponse(session, "Session restored.", false));
        }
    }

    public class AccountDashboardLogoutWebHandler : AccountDashboardHandlerBase
    {
        protected override async Task Post(WebRequestContext context)
        {
            AccountDashboardSessionManager.Logout(context);
            await context.SendJsonAsync(new
            {
                authenticated = false,
                message = "Signed out.",
            });
        }
    }

    public class AccountDashboardDataWebHandler : AccountDashboardHandlerBase
    {
        protected override async Task Get(WebRequestContext context)
        {
            AccountDashboardSession session = await RequireSessionAsync(context);
            if (session == null)
                return;

            if (AccountDashboardDataService.TryBuildResponse(session.Email, out object response, out string message) == false)
            {
                context.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.SendJsonAsync(new
                {
                    authenticated = true,
                    message,
                });
                return;
            }

            await context.SendJsonAsync(response);
        }
    }
}
