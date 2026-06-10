using System.Collections.Specialized;
using System.Net;
using System.Text;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.Network;

namespace MHServerEmu.WebFrontend.Handlers.MTXStore
{
    public class AddGWebHandler : WebHandler
    {
        private const string HtmlTemplateFileName = "add-g.html";

        private static readonly Logger Logger = LogManager.CreateLogger();
        private static readonly string HtmlTemplateFilePath = Path.Combine(FileHelper.DataDirectory, "Web", "MTXStore", HtmlTemplateFileName);

        private string _htmlTemplate;

        public AddGWebHandler()
        {
            Load();
        }

        public void Load()
        {
            if (File.Exists(HtmlTemplateFilePath) == false)
            {
                Logger.Warn($"Load(): '{HtmlTemplateFileName}' not found, adding Gs via in-game UI will not work");
                _htmlTemplate = string.Empty;
                return;
            }

            _htmlTemplate = File.ReadAllText(HtmlTemplateFilePath);
        }

        protected override Task Get(WebRequestContext context)
        {
            return SendAddGPageAsync(context);
        }

        protected override Task Post(WebRequestContext context)
        {
            return SendAddGPageAsync(context);
        }

        private async Task SendAddGPageAsync(WebRequestContext context)
        {
            if (string.IsNullOrWhiteSpace(_htmlTemplate))
            {
                context.StatusCode = (int)HttpStatusCode.InternalServerError;
                return;
            }

            // Read the body (in case it's a POST with form data)
            NameValueCollection requestBody = await context.ReadQueryStringAsync();

            // Read the URL (in case it's a GET with URL parameters)
            NameValueCollection queryParams = context.QueryString;

            // Use the coalesce operator (??) to check the URL first, then fall back to the body
            string downloader = queryParams["downloader"] ?? requestBody["downloader"];
            string token = queryParams["token"] ?? requestBody["token"];
            string email = queryParams["email"] ?? requestBody["email"];

            // Added safety check so it gracefully fails instead of crashing if the client sends garbage
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(email))
            {
                Logger.Warn("AddGWebHandler: Missing email or token in request.");
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            ServiceMessage.MTXStoreESBalanceResponse balanceResponse = await GameServiceTaskManager.Instance.GetESBalanceAsync(email, token);

            if (balanceResponse.StatusCode != (int)HttpStatusCode.OK)
            {
                context.StatusCode = balanceResponse.StatusCode;
                return;
            }

            StringBuilder sb = new(_htmlTemplate);
            sb.Replace("%DOWNLOADER%", downloader);
            sb.Replace("%TOKEN%", token);
            sb.Replace("%EMAIL%", email);
            sb.Replace("%CURRENT_BALANCE%", $"{balanceResponse.CurrentBalance}");
            sb.Replace("%CONVERSION_RATIO%", $"{balanceResponse.ConversionRatio:0.00}");
            sb.Replace("%CONVERSION_STEP%", $"{balanceResponse.ConversionStep}");
            string html = sb.ToString();

            await context.SendAsync(html, "text/html");
        }
    }
}
