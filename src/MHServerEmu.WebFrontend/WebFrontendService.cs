using MHServerEmu.Core.Config;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.Handlers;
using MHServerEmu.WebFrontend.Handlers.AccountDashboard;
using MHServerEmu.WebFrontend.Handlers.MTXStore;
using MHServerEmu.WebFrontend.Handlers.RemoteConsole;
using MHServerEmu.WebFrontend.Handlers.WebApi;
using MHServerEmu.WebFrontend.Network;
using MHServerEmu.WebFrontend.RemoteConsole;

namespace MHServerEmu.WebFrontend
{
    /// <summary>
    /// Handles HTTP requests from clients.
    /// </summary>
    public class WebFrontendService : IGameService
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly WebFrontendServiceMailbox _serviceMailbox = new();

        private readonly WebService _webService;
        private List<string> _dashboardEndpoints;
        private RemoteConsoleLogBuffer _remoteConsoleLogBuffer;

        public GameServiceState State { get; private set; } = GameServiceState.Created;

        /// <summary>
        /// Constructs a new <see cref="WebFrontendService"/> instance.
        /// </summary>
        public WebFrontendService()
        {
            var config = ConfigManager.Instance.GetConfig<WebFrontendConfig>();

            WebServiceSettings webServiceSettings = new()
            {
                Name = "WebFrontend",
                ListenUrl = $"http://{config.Address}:{config.Port}/",
                FallbackHandler = new NotFoundWebHandler(),
            };

            _webService = new(webServiceSettings);

            // Register the protobuf handler to the /Login/IndexPB path for compatibility with legacy reverse proxy setups.
            // We should probably prefer to use /AuthServer/Login/IndexPB because it's more accurate to what Gazillion had.
            ProtobufWebHandler protobufHandler = new(config.EnableLoginRateLimit, TimeSpan.FromMilliseconds(config.LoginRateLimitCostMS), config.LoginRateLimitBurst);
            _webService.RegisterHandler("/Login/IndexPB",            protobufHandler);
            _webService.RegisterHandler("/AuthServer/Login/IndexPB", protobufHandler);

            // MTXStore handlers are used for the Add G panel in the client UI.
            _webService.RegisterHandler("/MTXStore/AddG", new AddGWebHandler());
            _webService.RegisterHandler("/MTXStore/AddG/Submit", new AddGSubmitWebHandler());

            if (config.EnableWebApi)
            {
                InitializeWebBackend();
                WebApiKeyManager.Instance.LoadKeys();

                if (config.EnableRemoteConsole)
                    InitializeRemoteConsole();

                if (config.EnableDashboard)
                {
                    if (config.EnableRemoteConsole)
                        InitializeRemoteConsoleDashboard(config.DashboardFileDirectory, config.DashboardUrlPath);
                    else
                        InitializeWebDashboard(config.DashboardFileDirectory, config.DashboardUrlPath);
                }

                InitializeWebDashboard("AccountDashboard", "/AccountDashboard/");
            }
        }

        #region IGameService Implementation

        /// <summary>
        /// Runs this <see cref="WebFrontendService"/> instance.
        /// </summary>
        public void Run()
        {
            _webService.Start();
            State = GameServiceState.Running;

            while (_webService.IsRunning)
            {
                _serviceMailbox.ProcessMessages();
                Thread.Sleep(1);
            }

            State = GameServiceState.Shutdown;
        }

        /// <summary>
        /// Stops listening and shuts down this <see cref="WebFrontendService"/> instance.
        /// </summary>
        public void Shutdown()
        {
            _webService.Stop();
        }

        public void ReceiveServiceMessage<T>(in T message) where T : struct, IGameServiceMessage
        {
            _serviceMailbox.PostMessage(message);
        }

        public void GetStatus(Dictionary<string, long> statusDict)
        {
            statusDict["WebFrontendHandlers"] = _webService.HandlerCount;
            statusDict["WebFrontendHandledRequests"] = _webService.HandledRequests;
        }

        #endregion

        public void ReloadDashboard()
        {
            if (_dashboardEndpoints == null)
                return;

            foreach (string localPath in _dashboardEndpoints)
            {
                WebHandler handler = _webService.GetHandler(localPath);
                (handler as StaticFileWebHandler)?.Load();
                (handler as RemoteConsoleDashboardWebHandler)?.Load();
            }
        }

        public void ReloadAddGPage()
        {
            AddGWebHandler addGHandler = _webService.GetHandler("/MTXStore/AddG") as AddGWebHandler;
            addGHandler?.Load();
        }

        private void InitializeWebBackend()
        {
            _webService.RegisterHandler("/AccountManagement/Create",        new AccountCreateWebHandler());
            _webService.RegisterHandler("/AccountManagement/SetPlayerName", new AccountSetPlayerNameWebHandler());
            _webService.RegisterHandler("/AccountManagement/SetPassword",   new AccountSetPasswordWebHandler());
            _webService.RegisterHandler("/AccountManagement/SetUserLevel",  new AccountSetUserLevelWebHandler());
            _webService.RegisterHandler("/AccountManagement/SetFlag",       new AccountSetFlagWebHandler());
            _webService.RegisterHandler("/AccountManagement/ClearFlag",     new AccountClearFlagWebHandler());

            _webService.RegisterHandler("/AccountDashboard/Login",   new AccountDashboardLoginWebHandler());
            _webService.RegisterHandler("/AccountDashboard/Session", new AccountDashboardSessionWebHandler());
            _webService.RegisterHandler("/AccountDashboard/Logout",  new AccountDashboardLogoutWebHandler());
            _webService.RegisterHandler("/AccountDashboard/Data",    new AccountDashboardDataWebHandler());

            _webService.RegisterHandler("/ServerStatus", new ServerStatusWebHandler());
            _webService.RegisterHandler("/RegionReport", new RegionReportWebHandler());
            _webService.RegisterHandler("/Metrics/Performance", new MetricsPerformanceWebHandler());
        }

        private void InitializeRemoteConsole()
        {
            var config = ConfigManager.Instance.GetConfig<WebFrontendConfig>();

            _remoteConsoleLogBuffer = new(config.RemoteConsoleMaxLogEntries);
            LogManager.AttachTarget(_remoteConsoleLogBuffer);

            _webService.RegisterHandler("/RemoteConsole/Session", new RemoteConsoleSessionWebHandler());
            _webService.RegisterHandler("/RemoteConsole/Login", new RemoteConsoleLoginWebHandler());
            _webService.RegisterHandler("/RemoteConsole/AccountSessionLogin", new RemoteConsoleAccountSessionLoginWebHandler());
            _webService.RegisterHandler("/RemoteConsole/Logout", new RemoteConsoleLogoutWebHandler());
            _webService.RegisterHandler("/RemoteConsole/Poll", new RemoteConsolePollWebHandler(_remoteConsoleLogBuffer));
            _webService.RegisterHandler("/RemoteConsole/Command", new RemoteConsoleCommandWebHandler());
        }

        private void InitializeWebDashboard(string dashboardDirectoryName, string localPath)
        {
            string dashboardDirectory = Path.Combine(FileHelper.DataDirectory, "Web", dashboardDirectoryName);
            if (Directory.Exists(dashboardDirectory) == false)
            {
                Logger.Warn($"InitializeWebDashboard(): Dashboard directory '{dashboardDirectoryName}' does not exist");
                return;
            }

            string indexFilePath = Path.Combine(dashboardDirectory, "index.html");
            if (File.Exists(indexFilePath) == false)
            {
                Logger.Warn($"InitializeWebDashboard(): Index file not found at '{indexFilePath}'");
                return;
            }

            _dashboardEndpoints ??= new();

            // Make sure local path starts and ends with slashes.
            if (localPath.StartsWith('/') == false)
                localPath = $"/{localPath}";

            if (localPath.EndsWith('/') == false)
                localPath = $"{localPath}/";

            _webService.RegisterHandler(localPath, new StaticFileWebHandler(indexFilePath));
            _dashboardEndpoints.Add(localPath);

            // Add redirect for requests to our dashboard "directory" that don't have trailing slashes.
            if (localPath.Length > 1)
            {
                string localPathRedirect = localPath[..^1];
                _webService.RegisterHandler(localPathRedirect, new TrailingSlashRedirectWebHandler());
                _dashboardEndpoints.Add(localPathRedirect);
            }

            // Register other files.
            foreach (string filePath in Directory.GetFiles(dashboardDirectory, "*", SearchOption.AllDirectories))
            {
                string relativeFilePath = Path.GetRelativePath(dashboardDirectory, filePath);

                if (string.Equals(relativeFilePath, "index.html", StringComparison.InvariantCultureIgnoreCase))
                    continue;

                string subFilePath = $"{localPath}{relativeFilePath.Replace('\\', '/')}";

                _webService.RegisterHandler(subFilePath, new StaticFileWebHandler(filePath));
                _dashboardEndpoints.Add(subFilePath);
            }

            Logger.Info($"Initialized web dashboard at {localPath}");
        }

        private void InitializeRemoteConsoleDashboard(string dashboardDirectoryName, string localPath)
        {
            string dashboardDirectory = Path.Combine(FileHelper.DataDirectory, "Web", dashboardDirectoryName);
            if (Directory.Exists(dashboardDirectory) == false)
            {
                Logger.Warn($"InitializeRemoteConsoleDashboard(): Dashboard directory '{dashboardDirectoryName}' does not exist");
                return;
            }

            string indexFilePath = Path.Combine(dashboardDirectory, "index.html");
            if (File.Exists(indexFilePath) == false)
            {
                Logger.Warn($"InitializeRemoteConsoleDashboard(): Index file not found at '{indexFilePath}'");
                return;
            }

            _dashboardEndpoints ??= new();

            if (localPath.StartsWith('/') == false)
                localPath = $"/{localPath}";

            if (localPath.EndsWith('/') == false)
                localPath = $"{localPath}/";

            _webService.RegisterHandler(localPath, new RemoteConsoleDashboardWebHandler(indexFilePath));
            _dashboardEndpoints.Add(localPath);

            if (localPath.Length > 1)
            {
                string localPathRedirect = localPath[..^1];
                _webService.RegisterHandler(localPathRedirect, new TrailingSlashRedirectWebHandler());
                _dashboardEndpoints.Add(localPathRedirect);
            }

            foreach (string filePath in Directory.GetFiles(dashboardDirectory, "*", SearchOption.AllDirectories))
            {
                string relativeFilePath = Path.GetRelativePath(dashboardDirectory, filePath);

                if (string.Equals(relativeFilePath, "index.html", StringComparison.InvariantCultureIgnoreCase))
                    continue;

                string subFilePath = $"{localPath}{relativeFilePath.Replace('\\', '/')}";

                _webService.RegisterHandler(subFilePath, new StaticFileWebHandler(filePath));
                _dashboardEndpoints.Add(subFilePath);
            }

            Logger.Info($"Initialized remote console dashboard at {localPath}");
        }
    }
}
