namespace MHServerEmu.WebFrontend.Models
{
    public readonly struct AccountDashboardLoginRequest
    {
        public string Email { get; init; }
        public string Password { get; init; }
    }
}
