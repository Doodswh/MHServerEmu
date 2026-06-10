window.dashboardConfig = window.dashboardConfig || {};
window.dashboardConfig.originSuffix = window.location.pathname.toLowerCase().indexOf("/authserver/") === 0 ? "/AuthServer" : "";
