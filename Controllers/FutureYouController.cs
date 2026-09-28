using System.Security.Claims;
using CampusCoin.Services;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusCoin.Controllers;

[Authorize]
[Route("FutureYou")]
public class FutureYouController(
    IFutureYouService futureYou,
    CampusChallengeService challenges) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = await futureYou.BuildPageAsync(UserId, ct);
        return View(model);
    }

    [HttpPost("simulate")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Simulate([FromBody] FutureYouSimulateRequest? request, CancellationToken ct)
    {
        request ??= new FutureYouSimulateRequest();
        var result = await futureYou.SimulateAsync(UserId, request, ct);
        return Json(result);
    }

    [HttpPost("messages")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetMessages(bool enabled, CancellationToken ct)
    {
        await futureYou.SetMessagesEnabledAsync(UserId, enabled, ct);
        if (WantsJson()) return Json(new { ok = true, enabled });
        TempData["Success"] = enabled ? "Future You messages are on." : "Future You messages are off.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("parallel-lives/visibility")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetParallelLivesVisibility(bool hidden, CancellationToken ct)
    {
        await futureYou.SetParallelLivesHiddenAsync(UserId, hidden, ct);
        if (WantsJson()) return Json(new { ok = true, hidden });
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("parallel-lives/regenerate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateParallelLives(CancellationToken ct)
    {
        var stories = await futureYou.BuildParallelLivesAsync(UserId, regenerate: true, ct);
        if (WantsJson()) return Json(new { ok = true, stories });
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("expense-message")]
    public async Task<IActionResult> ExpenseMessage(decimal amount, string? category, CancellationToken ct)
    {
        var msg = await futureYou.BuildExpenseMessageAsync(UserId, amount, category, ct);
        return Json(new { ok = msg != null, message = msg });
    }

    [HttpPost("challenge/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartChallenge(CancellationToken ct)
    {
        var model = await challenges.StartAsync(UserId, ct);
        if (WantsJson()) return Json(new { ok = true, challenge = model });
        TempData["Success"] = "Your 30-day campus challenge is live. No pressure — just daily nudges.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("challenge/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChallengeStatus(int challengeId, string status, CancellationToken ct)
    {
        var model = await challenges.SetStatusAsync(UserId, challengeId, status, ct);
        if (model == null) return NotFound();
        if (WantsJson()) return Json(new { ok = true, challenge = model });
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("challenge/day")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChallengeDay(int challengeId, int dayNumber, CancellationToken ct)
    {
        var model = await challenges.ToggleDayAsync(UserId, challengeId, dayNumber, ct);
        if (model == null) return NotFound();
        if (WantsJson()) return Json(new { ok = true, challenge = model });
        return RedirectToAction(nameof(Index));
    }

    private bool WantsJson() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
        || Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
}
