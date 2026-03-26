using MHServerEmu.Core.Config;

namespace MHServerEmu.WebFrontend
{
    public class WebFrontendConfig : ConfigContainer
    {
        public string Address { get; private set; } = "localhost";
        public string Port { get; private set; } = "8080";
        public bool EnableLoginRateLimit { get; private set; } = false;
        public int LoginRateLimitCostMS { get; private set; } = 30000;
        public int LoginRateLimitBurst { get; private set; } = 10;
        public bool EnableWebApi { get; private set; } = true;
        public bool EnableDashboard { get; private set; } = true;
        public string DashboardFileDirectory { get; private set; } = "Dashboard";
        public string DashboardUrlPath { get; private set; } = "/";
        public bool EnableRemoteConsole { get; private set; } = false;
        public string RemoteConsoleUsername { get; private set; } = "admin";
        public string RemoteConsolePassword { get; private set; } = "change-me";
        public string RemoteConsoleAllowedIPs { get; private set; } = "127.0.0.1,::1";
        public int RemoteConsoleSessionDurationMinutes { get; private set; } = 480;
        public int RemoteConsoleMaxLogEntries { get; private set; } = 1000;
    }
}
