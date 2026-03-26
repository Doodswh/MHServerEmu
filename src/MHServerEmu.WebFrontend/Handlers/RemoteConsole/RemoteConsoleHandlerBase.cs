using System.Net;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.RemoteConsole;

namespace MHServerEmu.WebFrontend.Handlers.RemoteConsole
{
    public abstract class RemoteConsoleHandlerBase : WebHandler
    {
        protected async Task<bool> RequireWhitelistedIpAsync(WebRequestContext context)
        {
            if (RemoteConsoleAuthManager.Instance.IsRemoteConsoleEnabled() == false)
            {
                context.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                await context.SendJsonAsync(new { authenticated = false, whitelisted = false, message = "Remote console is disabled." });
                return false;
            }

            if (RemoteConsoleAuthManager.Instance.IsIpAllowed(context.GetIPAddress()) == false)
            {
                context.StatusCode = (int)HttpStatusCode.Forbidden;
                await context.SendJsonAsync(new { authenticated = false, whitelisted = false, message = "This IP address is not whitelisted for the remote console." });
                return false;
            }

            return true;
        }

        protected async Task<RemoteConsoleSession> RequireSessionAsync(WebRequestContext context)
        {
            if (await RequireWhitelistedIpAsync(context) == false)
                return null;

            if (RemoteConsoleAuthManager.Instance.TryGetSession(context, out RemoteConsoleSession session) == false)
            {
                context.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.SendJsonAsync(new { authenticated = false, whitelisted = true, message = "Authentication required." });
                return null;
            }

            return session;
        }
    }
}
