using System.Net;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Metrics;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.Games.Network.InstanceManagement;

namespace MHServerEmu.WebFrontend.Handlers.WebApi
{
    public class MetricsPerformanceWebHandler : WebHandler
    {
        protected override async Task Get(WebRequestContext context)
        {
            if (ServerManager.Instance.GetGameService(GameServiceType.GameInstance) is not GameInstanceService gameInstanceService)
            {
                context.StatusCode = (int)HttpStatusCode.InternalServerError;
                return;
            }

            using PerformanceReport report = ObjectPoolManager.Instance.Get<PerformanceReport>();
            MetricsManager.Instance.GetPerformanceReportData(report);

            

            await context.SendJsonAsync(report);
        }
    }
}


