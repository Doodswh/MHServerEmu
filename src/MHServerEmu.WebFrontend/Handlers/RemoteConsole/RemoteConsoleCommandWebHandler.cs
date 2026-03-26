using System.Net;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.Models;
using MHServerEmu.WebFrontend.RemoteConsole;

namespace MHServerEmu.WebFrontend.Handlers.RemoteConsole
{
    public class RemoteConsoleCommandWebHandler : RemoteConsoleHandlerBase
    {
        protected override async Task Post(WebRequestContext context)
        {
            RemoteConsoleSession session = await RequireSessionAsync(context);
            if (session == null)
                return;

            RemoteConsoleCommandRequest request = await context.ReadJsonAsync<RemoteConsoleCommandRequest>();
            string command = request?.Command?.Trim();

            if (string.IsNullOrWhiteSpace(command))
            {
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.SendJsonAsync(new { message = "A command string is required." });
                return;
            }

            if (RemoteConsoleCommandBridge.TryExecute(command, out string output, out string errorMessage) == false)
            {
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.SendJsonAsync(new { message = errorMessage });
                return;
            }

            await context.SendJsonAsync(new
            {
                message = $"Command submitted by {session.Username}.",
                command,
                output
            });
        }
    }
}
