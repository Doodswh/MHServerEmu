using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network.Web;

namespace MHServerEmu.WebFrontend.Handlers.Portal
{
    /// <summary>
    /// CUSTOM: Serves the portal page (Data/Web/Portal/index.html) to the client's built-in browser.
    /// </summary>
    /// <remarks>
    /// Point RealMoneyUrl (the store's Add G button) or HomePageUrl (the store's home tab) in Config.ini at /Portal to show
    /// it in game. When the client attaches the session's token and email, as it does for the Add G button, the page can
    /// send actions to scripts through /Portal/Action (see PortalActionWebHandler and ScriptHooks.WebAction).
    /// The page is served without a session too, and shows what the client sent, so it doubles as a probe.
    /// </remarks>
    public class PortalWebHandler : WebHandler
    {
        private const string HtmlTemplateFileName = "index.html";

        private static readonly Logger Logger = LogManager.CreateLogger();
        private static readonly string HtmlTemplateFilePath = Path.Combine(FileHelper.DataDirectory, "Web", "Portal", HtmlTemplateFileName);

        private string _htmlTemplate;

        public PortalWebHandler()
        {
            Load();
        }

        public void Load()
        {
            if (File.Exists(HtmlTemplateFilePath) == false)
            {
                Logger.Warn($"Load(): '{HtmlTemplateFilePath}' not found, the portal page will not work");
                _htmlTemplate = string.Empty;
                return;
            }

            _htmlTemplate = File.ReadAllText(HtmlTemplateFilePath);
        }

        protected override Task Get(WebRequestContext context)
        {
            return SendPortalPageAsync(context);
        }

        protected override Task Post(WebRequestContext context)
        {
            return SendPortalPageAsync(context);
        }

        private async Task SendPortalPageAsync(WebRequestContext context)
        {
            if (string.IsNullOrWhiteSpace(_htmlTemplate))
            {
                context.StatusCode = (int)HttpStatusCode.InternalServerError;
                return;
            }

            // The client sends its parameters in the URL or in the body, depending on where it opens the page from
            NameValueCollection body = await context.ReadQueryStringAsync();
            NameValueCollection query = context.QueryString;

            string downloader = query["downloader"] ?? body["downloader"] ?? string.Empty;
            string token = query["token"] ?? body["token"] ?? string.Empty;
            string email = query["email"] ?? body["email"] ?? string.Empty;

            // What the client sent, by name only (never log or echo the token itself outside its own field)
            string requestInfo = $"{context.HttpMethod}; URL parameters: {DescribeKeys(query)}; body parameters: {DescribeKeys(body)}; " +
                $"game client: {(context.IsGameClientRequest ? "yes" : "no")}";

            bool hasSession = token.Length > 0 && email.Length > 0;
            Logger.Info($"Portal page requested ({(hasSession ? "with" : "WITHOUT")} a session token): {requestInfo}, user agent [{context.UserAgent}]");

            // The page keeps these in JavaScript string literals, like the Add G page does, so encode them for that
            // (quotes, backslashes and angle brackets become \uXXXX escapes)
            StringBuilder sb = new(_htmlTemplate);
            sb.Replace("%DOWNLOADER%", JavaScriptEncoder.Default.Encode(downloader));
            sb.Replace("%TOKEN%", JavaScriptEncoder.Default.Encode(token));
            sb.Replace("%EMAIL%", JavaScriptEncoder.Default.Encode(email));
            sb.Replace("%REQUEST_INFO%", JavaScriptEncoder.Default.Encode(requestInfo));

            await context.SendAsync(sb.ToString(), "text/html");
        }

        private static string DescribeKeys(NameValueCollection collection)
        {
            if (collection == null || collection.Count == 0)
                return "none";

            List<string> keys = new();
            foreach (string key in collection.AllKeys)
            {
                if (string.IsNullOrEmpty(key) == false)
                    keys.Add(key.Length > 32 ? key[..32] : key);
            }

            return keys.Count > 0 ? string.Join(", ", keys) : "none";
        }
    }
}
