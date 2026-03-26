using System.Collections.Concurrent;
using Gazillion;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.PlayerManagement.Auth;
using MHServerEmu.PlayerManagement.Players;

namespace MHServerEmu.WebFrontend.AccountDashboard
{
    public sealed class AccountDashboardSession
    {
        public string Token { get; init; }
        public string Email { get; init; }
        public DateTimeOffset ExpiresAtUtc { get; init; }
        public DBAccount Account { get; init; }
    }

    public static class AccountDashboardSessionManager
    {
        private static readonly ConcurrentDictionary<string, AccountDashboardSession> Sessions = new(StringComparer.Ordinal);
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

        public static bool TryLogin(string email, string password, out AccountDashboardSession session, out string message)
        {
            session = null;
            message = null;

            email = email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                message = "Email and password are required.";
                return false;
            }

            LoginDataPB loginData = LoginDataPB.CreateBuilder()
                .SetEmailAddress(email)
                .SetPassword(password)
                .Build();

            AuthStatusCode statusCode = AccountManager.TryGetAccountByLoginDataPB(loginData, false, out DBAccount account);
            if (statusCode != AuthStatusCode.Success)
            {
                message = GetAuthFailureMessage(statusCode);
                return false;
            }

            DateTimeOffset expiresAtUtc = DateTimeOffset.UtcNow.Add(SessionLifetime);
            string token = Guid.NewGuid().ToString("N");

            session = new AccountDashboardSession
            {
                Token = token,
                Email = email,
                ExpiresAtUtc = expiresAtUtc,
                Account = account,
            };

            Sessions[token] = session;
            message = "Signed in.";
            return true;
        }

        private static string GetAuthFailureMessage(AuthStatusCode statusCode)
        {
            return statusCode switch
            {
                AuthStatusCode.AccountBanned => "This account is banned.",
                AuthStatusCode.AccountArchived => "This account is archived.",
                AuthStatusCode.PasswordExpired => "This account password has expired.",
                AuthStatusCode.EmailNotVerified => "This account is not verified for login.",
                _ => "Incorrect email or password.",
            };
        }

        public static bool TryGetSession(WebRequestContext context, out AccountDashboardSession session, out string message)
        {
            session = null;
            message = null;

            string token = context.GetBearerToken();
            return TryGetSession(token, out session, out message);
        }

        public static bool TryGetSession(string token, out AccountDashboardSession session, out string message)
        {
            session = null;
            message = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                message = "No active account session was found.";
                return false;
            }

            if (Sessions.TryGetValue(token, out session) == false)
            {
                message = "No active account session was found.";
                return false;
            }

            if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                Sessions.TryRemove(token, out _);
                session = null;
                message = "Your account session has expired.";
                return false;
            }

            return true;
        }

        public static void Logout(WebRequestContext context)
        {
            string token = context.GetBearerToken();
            if (string.IsNullOrWhiteSpace(token))
                return;

            Sessions.TryRemove(token, out _);
        }
    }
}
