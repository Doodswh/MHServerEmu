using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Gazillion;
using MHServerEmu.Core.Config;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.PlayerManagement;
using MHServerEmu.PlayerManagement.Auth;
using MHServerEmu.PlayerManagement.Players;

namespace MHServerEmu.WebFrontend.RemoteConsole
{
    public class RemoteConsoleAuthManager
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly ConcurrentDictionary<string, RemoteConsoleSession> _sessionDict = new(StringComparer.Ordinal);

        public static RemoteConsoleAuthManager Instance { get; } = new();

        private RemoteConsoleAuthManager()
        {
        }

        private WebFrontendConfig Config => ConfigManager.Instance.GetConfig<WebFrontendConfig>();

        public bool IsRemoteConsoleEnabled()
        {
            return Config.EnableRemoteConsole;
        }

        public bool IsIpAllowed(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            string whitelist = Config.RemoteConsoleAllowedIPs;
            if (string.IsNullOrWhiteSpace(whitelist))
                return false;

            foreach (string token in whitelist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token == "*" || MatchesIpRule(ipAddress, token))
                    return true;
            }

            return false;
        }

        public bool TryLogin(WebRequestContext context, string username, string password, out RemoteConsoleSession session, out string message)
        {
            session = null;
            message = string.Empty;

            if (IsRemoteConsoleEnabled() == false)
            {
                message = "Remote console is disabled in the server configuration.";
                return false;
            }

            string ipAddress = context.GetIPAddress();
            if (IsIpAllowed(ipAddress) == false)
            {
                message = "This IP address is not whitelisted for remote console access.";
                return false;
            }

            string emailAddress = username?.Trim();
            LoginDataPB loginData = LoginDataPB.CreateBuilder()
                .SetEmailAddress(emailAddress ?? string.Empty)
                .SetPassword(password ?? string.Empty)
                .Build();

            bool useAccountWhitelist = ConfigManager.Instance.GetConfig<PlayerManagerConfig>().UseWhitelist;
            AuthStatusCode statusCode = AccountManager.TryGetAccountByLoginDataPB(loginData, useAccountWhitelist, out DBAccount account);
            if (statusCode != AuthStatusCode.Success)
            {
                message = GetLoginFailureMessage(statusCode);
                return false;
            }

            if (account.UserLevel < AccountUserLevel.Admin)
            {
                message = "This account is not permitted to use the remote console.";
                return false;
            }

            session = CreateSession(account, ipAddress);

            Logger.Info($"Remote console session created for [{account.Email}] from {ipAddress}");
            message = "Authenticated.";
            return true;
        }

        public bool TryCreateSessionForAccount(WebRequestContext context, DBAccount account, out RemoteConsoleSession session, out string message)
        {
            session = null;
            message = string.Empty;

            if (IsRemoteConsoleEnabled() == false)
            {
                message = "Remote console is disabled in the server configuration.";
                return false;
            }

            string ipAddress = context.GetIPAddress();
            if (IsIpAllowed(ipAddress) == false)
            {
                message = "This IP address is not whitelisted for remote console access.";
                return false;
            }

            if (account == null)
            {
                message = "No active account session was found.";
                return false;
            }

            if (account.Flags.HasFlag(AccountFlags.IsBanned))
            {
                message = "This account is banned.";
                return false;
            }

            if (account.Flags.HasFlag(AccountFlags.IsArchived))
            {
                message = "This account is archived.";
                return false;
            }

            if (account.Flags.HasFlag(AccountFlags.IsPasswordExpired))
            {
                message = "This account password has expired.";
                return false;
            }

            bool useAccountWhitelist = ConfigManager.Instance.GetConfig<PlayerManagerConfig>().UseWhitelist;
            if (useAccountWhitelist && account.Flags.HasFlag(AccountFlags.IsWhitelisted) == false)
            {
                message = "This account is not whitelisted.";
                return false;
            }

            if (account.UserLevel < AccountUserLevel.Admin)
            {
                message = "This account is not permitted to use the remote console.";
                return false;
            }

            session = CreateSession(account, ipAddress);

            Logger.Info($"Remote console session created from account dashboard session for [{account.Email}] from {ipAddress}");
            message = "Authenticated from account portal session.";
            return true;
        }

        public bool TryGetSession(WebRequestContext context, out RemoteConsoleSession session)
        {
            session = null;

            if (IsRemoteConsoleEnabled() == false)
                return false;

            string token = context.GetBearerToken();
            if (string.IsNullOrWhiteSpace(token))
                return false;

            if (_sessionDict.TryGetValue(token, out session) == false)
                return false;

            if (session.ExpiresAtUtc <= DateTime.UtcNow)
            {
                _sessionDict.TryRemove(token, out _);
                session = null;
                return false;
            }

            string ipAddress = context.GetIPAddress();
            if (string.Equals(session.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase) == false || IsIpAllowed(ipAddress) == false)
            {
                Logger.Warn($"Remote console session rejected for [{session.Username}] from {ipAddress}");
                _sessionDict.TryRemove(token, out _);
                session = null;
                return false;
            }

            return true;
        }

        public void Logout(WebRequestContext context)
        {
            string token = context.GetBearerToken();
            if (string.IsNullOrWhiteSpace(token) == false)
                _sessionDict.TryRemove(token, out _);
        }

        private void CleanupExpiredSessions()
        {
            DateTime utcNow = DateTime.UtcNow;

            foreach (var pair in _sessionDict)
            {
                if (pair.Value.ExpiresAtUtc <= utcNow)
                    _sessionDict.TryRemove(pair.Key, out _);
            }
        }

        private static string GetLoginFailureMessage(AuthStatusCode statusCode)
        {
            return statusCode switch
            {
                AuthStatusCode.EmailNotVerified => "This account is not whitelisted.",
                AuthStatusCode.AccountBanned => "This account is banned.",
                AuthStatusCode.AccountArchived => "This account is archived.",
                AuthStatusCode.PasswordExpired => "This account password has expired.",
                _ => "Invalid account credentials."
            };
        }

        private static string CreateToken()
        {
            Span<byte> buffer = stackalloc byte[32];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToHexString(buffer);
        }

        private RemoteConsoleSession CreateSession(DBAccount account, string ipAddress)
        {
            CleanupExpiredSessions();

            RemoteConsoleSession session = new()
            {
                Token = CreateToken(),
                Username = string.IsNullOrWhiteSpace(account.PlayerName) == false ? account.PlayerName : account.Email,
                IpAddress = ipAddress,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(Math.Max(5, Config.RemoteConsoleSessionDurationMinutes))
            };

            _sessionDict[session.Token] = session;
            return session;
        }

        private static bool MatchesIpRule(string ipAddressString, string rule)
        {
            if (string.Equals(ipAddressString, rule, StringComparison.OrdinalIgnoreCase))
                return true;

            string[] cidrParts = rule.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (cidrParts.Length != 2)
                return false;

            if (IPAddress.TryParse(ipAddressString, out IPAddress ipAddress) == false)
                return false;

            if (IPAddress.TryParse(cidrParts[0], out IPAddress networkAddress) == false)
                return false;

            if (int.TryParse(cidrParts[1], out int prefixLength) == false)
                return false;

            byte[] ipBytes = ipAddress.GetAddressBytes();
            byte[] networkBytes = networkAddress.GetAddressBytes();

            if (ipBytes.Length != networkBytes.Length)
                return false;

            int maxPrefixLength = ipBytes.Length * 8;
            if (prefixLength < 0 || prefixLength > maxPrefixLength)
                return false;

            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            for (int i = 0; i < fullBytes; i++)
            {
                if (ipBytes[i] != networkBytes[i])
                    return false;
            }

            if (remainingBits == 0)
                return true;

            int mask = 0xFF << (8 - remainingBits);
            return (ipBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
        }
    }

    public class RemoteConsoleSession
    {
        public string Token { get; set; }
        public string Username { get; set; }
        public string IpAddress { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
