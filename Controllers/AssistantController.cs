using System.Security.Claims;
using CampusCoin.Services;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusCoin.Controllers;

[Authorize]
public class AssistantController(AssistantService assistant) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Ask(AssistantQuestion form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Ask a question up to 400 characters." });
        var result = await assistant.AskAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, form.Message, cancellationToken);
        return Json(result);
    }
}
