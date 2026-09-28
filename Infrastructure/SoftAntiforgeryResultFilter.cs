using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace CampusCoin.Infrastructure;

/// <summary>
/// Turns antiforgery 400 responses into a safe GET redirect.
/// Stops Chrome's blank "HTTP ERROR 400" after refresh / form resubmission.
/// </summary>
public sealed class SoftAntiforgeryResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not AntiforgeryValidationFailedResult)
            return;

        var http = context.HttpContext;
        var accept = http.Request.Headers.Accept.ToString();
        var wantsJson = accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(http.Request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
            || (http.Request.Path.StartsWithSegments("/Assistant")
                || http.Request.Path.Value?.Contains("/SaveCurrency", StringComparison.OrdinalIgnoreCase) == true);

        if (wantsJson)
        {
            context.Result = new JsonResult(new { error = "Session expired. Refresh the page and try again." })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
            return;
        }

        var temp = http.RequestServices.GetService<ITempDataDictionaryFactory>()?.GetTempData(http);
        if (temp != null)
            temp["Error"] = "That form expired or was already submitted. Please try again.";

        var target = ResolveSafeGetUrl(http);
        context.Result = new SoftSeeOtherResult(target);
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    private sealed class SoftSeeOtherResult(string url) : ActionResult
    {
        public override Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = StatusCodes.Status303SeeOther;
            response.Headers.Location = url;
            return Task.CompletedTask;
        }
    }

    private static string ResolveSafeGetUrl(HttpContext http)
    {
        var referer = http.Request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(referer)
            && Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            return uri.PathAndQuery;
        }

        var path = http.Request.Path.Value ?? "/";
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "/";

        // POST /Controller/Edit or /Controller/Delete/5 → /Controller (Index)
        var controller = parts[0];
        if (parts.Length >= 2 && IsMutatingAction(parts[1]))
            return "/" + controller;

        // Same path works for Login/Register/Settings/Profile GET.
        return path + http.Request.QueryString.Value;
    }

    private static bool IsMutatingAction(string action) =>
        action.Equals("Delete", StringComparison.OrdinalIgnoreCase)
        || action.Equals("Toggle", StringComparison.OrdinalIgnoreCase)
        || action.Equals("ImportCsv", StringComparison.OrdinalIgnoreCase)
        || action.Equals("ApplyForAdmin", StringComparison.OrdinalIgnoreCase)
        || action.Equals("SaveCurrency", StringComparison.OrdinalIgnoreCase)
        || action.Equals("SetWorkspace", StringComparison.OrdinalIgnoreCase)
        || action.Equals("Logout", StringComparison.OrdinalIgnoreCase);
}
