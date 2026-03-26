using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.RemoteConsole;

namespace MHServerEmu.WebFrontend.Handlers.RemoteConsole
{
    public class RemoteConsoleSessionWebHandler : WebHandler
    {
        protected override async Task Get(WebRequestContext context)
        {
            bool enabled = RemoteConsoleAuthManager.Instance.IsRemoteConsoleEnabled();
            string ipAddress = context.GetIPAddress();
            bool whitelisted = enabled && RemoteConsoleAuthManager.Instance.IsIpAllowed(ipAddress);

            if (RemoteConsoleAuthManager.Instance.TryGetSession(context, out RemoteConsoleSession session))
            {
                await context.SendJsonAsync(new
                {
                    authenticated = true,
                    whitelisted = true,
                    session = new
                    {
                        username = session.Username,
                        ipAddress = session.IpAddress,
                        expiresAtUtc = session.ExpiresAtUtc
                    }
                });
                return;
            }

            await context.SendJsonAsync(new
            {
                authenticated = false,
                whitelisted,
                message = enabled
                    ? (whitelisted ? "Authentication required." : "This IP address is not whitelisted for the remote console.")
                    : "Remote console is disabled."
            });
        }
    }
}
