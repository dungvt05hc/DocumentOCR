using Hangfire.Dashboard;

namespace DocumentOCR.WebApi.Authorization;

/// <summary>
/// Restricts the Hangfire dashboard to requests originating from the local machine.
/// The app has no user authentication yet, so this is a stop-gap: it must be replaced
/// with a real role-based <see cref="IDashboardAuthorizationFilter"/> once auth lands.
/// </summary>
public class LocalOnlyDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var remoteIp = httpContext.Connection.RemoteIpAddress;

        return remoteIp is not null && System.Net.IPAddress.IsLoopback(remoteIp);
    }
}
