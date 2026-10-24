using System.Net;
using System.Text.RegularExpressions;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.Network;

namespace MHServerEmu.WebFrontend.Handlers.Portal
{
    /// <summary>
    /// CUSTOM: Receives an action from the portal page and routes it to the game instance of the player the token belongs
    /// to, where it is raised as the WebAction script hook. The player must be logged in and in game (same check as Add G).
    /// </summary>
    /// <remarks>
    /// Answers with JSON { Ok, Status, Result }: 200 = a script handled it, 404 = no script handles this action,
    /// 403 = token not valid or player not in game, 408 = the game did not answer in time, 400 = malformed request.
    /// </remarks>
    public partial class PortalActionWebHandler : WebHandler
    {
        private const int MaxDataLength = 4096;

        protected override async Task Post(WebRequestContext context)
        {
            ActionRequest request;
            try
            {
                request = await context.ReadJsonAsync<ActionRequest>();
            }
            catch (Exception)
            {
                await SendResultAsync(context, (int)HttpStatusCode.BadRequest, null);
                return;
            }

            string data = request.Data ?? string.Empty;

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token) ||
                request.Action == null || ActionNameRegex().IsMatch(request.Action) == false || data.Length > MaxDataLength)
            {
                await SendResultAsync(context, (int)HttpStatusCode.BadRequest, null);
                return;
            }

            ServiceMessage.WebActionResponse response = await GameServiceTaskManager.Instance.DoWebActionAsync(
                request.Email, request.Token, request.Action, data);

            await SendResultAsync(context, response.StatusCode, response.Result);
        }

        private static Task SendResultAsync(WebRequestContext context, int statusCode, string result)
        {
            context.StatusCode = statusCode;
            return context.SendJsonAsync(new ActionResult
            {
                Ok = statusCode == (int)HttpStatusCode.OK,
                Status = statusCode,
                Result = result ?? string.Empty,
            });
        }

        [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,64}$")]
        private static partial Regex ActionNameRegex();

        private readonly struct ActionRequest
        {
            public string Email { get; init; }
            public string Token { get; init; }
            public string Action { get; init; }
            public string Data { get; init; }
        }

        private readonly struct ActionResult
        {
            public bool Ok { get; init; }
            public int Status { get; init; }
            public string Result { get; init; }
        }
    }
}
