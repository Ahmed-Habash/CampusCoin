using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CampusCoin.Infrastructure;

/// <summary>
/// After POST, respond with 303 See Other so refresh uses GET instead of resubmitting the form.
/// </summary>
public sealed class SeeOtherRedirectFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method)
            && !HttpMethods.IsPut(context.HttpContext.Request.Method))
            return;

        string? url = context.Result switch
        {
            RedirectToActionResult action when action.Permanent != true
                => BuildActionUrl(context, action),
            RedirectToRouteResult route when route.Permanent != true
                => BuildRouteUrl(context, route),
            RedirectResult redirect when !redirect.Permanent
                => redirect.Url,
            LocalRedirectResult local when !local.Permanent
                => local.Url,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(url))
            return;

        context.Result = new SeeOtherRedirectResult(url);
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    private static string? BuildActionUrl(ResultExecutingContext context, RedirectToActionResult action)
    {
        var factory = context.HttpContext.RequestServices.GetService<IUrlHelperFactory>();
        if (factory == null) return null;
        var url = factory.GetUrlHelper(context);
        return url.Action(action.ActionName, action.ControllerName, action.RouteValues);
    }

    private static string? BuildRouteUrl(ResultExecutingContext context, RedirectToRouteResult route)
    {
        var factory = context.HttpContext.RequestServices.GetService<IUrlHelperFactory>();
        if (factory == null) return null;
        var url = factory.GetUrlHelper(context);
        return route.RouteName != null
            ? url.RouteUrl(route.RouteName, route.RouteValues)
            : url.RouteUrl(route.RouteValues);
    }

    private sealed class SeeOtherRedirectResult(string url) : ActionResult
    {
        public override Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = StatusCodes.Status303SeeOther;
            response.Headers.Location = url;
            return Task.CompletedTask;
        }
    }
}
