using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.RemoteConsole;

namespace MHServerEmu.WebFrontend.Handlers.RemoteConsole
{
    public class RemoteConsoleLogoutWebHandler : WebHandler
    {
        protected override async Task Post(WebRequestContext context)
        {
            RemoteConsoleAuthManager.Instance.Logout(context);
            await context.SendJsonAsync(new { authenticated = false, message = "Signed out." });
        }
    }
}
