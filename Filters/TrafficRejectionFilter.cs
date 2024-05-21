using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.LeaderboardService.Controllers;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Filters;

public class TrafficRejectionFilter : PlatformFilter, IActionFilter
{
    private string[] CoveredRoutes { get; init; }
    
    private long NextLockout { get; set; }
    private long LastRefresh { get; set; }
    
    public TrafficRejectionFilter()
    {
        string[] baseRoutes = typeof(LadderController)
            .GetCustomAttributes()
            .OfType<RouteAttribute>()
            .Select(route => route.Template)
            .ToArray();

        if (!baseRoutes.Any())
            baseRoutes = new[] { "/" };
        
        
        string[] routes = typeof(LadderController)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes())
            .OfType<RouteAttribute>()
            .Select(route => route.Template)
            .ToArray();

        if (!routes.Any())
            return;

        CoveredRoutes = baseRoutes
            .SelectMany(url => routes.Select(route => Path.Combine(url, route)))
            .Distinct()
            .ToArray();
    }
    
    
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (NextLockout > Timestamp.Now && LastRefresh > Timestamp.Now)
            return;

        LadderSeasonDefinition next = PlatformService
            .Require<SeasonDefinitionService>()
            .GetCurrentSeason();
        // LastRefresh = Timestamp.Now;

        if (next == null)
            return;
        
        NextLockout = next.EndTime - Interval.OneMinute;

        if (NextLockout < Timestamp.Now)
            throw new PlatformException("Ladder is currently locked for rollover; try again soon.");
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}