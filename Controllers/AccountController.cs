using CampusCoin.Models;
using CampusCoin.Services;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CampusCoin.Controllers;

public class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AdminWorkspaceService workspace) : Controller
{
    [HttpGet] public IActionResult Login(string? returnUrl) => View(new LoginForm { ReturnUrl = returnUrl });
    [HttpPost]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);
        var user = await users.FindByEmailAsync(form.Email);
        if (user != null && await users.IsLockedOutAsync(user))
        {
            ModelState.AddModelError("", "Your account is disabled or locked out. Please contact system support.");
            return View(form);
        }
        var result = user == null ? Microsoft.AspNetCore.Identity.SignInResult.Failed : await signIn.PasswordSignInAsync(user, form.Password, form.RememberMe, true);
        if (result.Succeeded)
        {
            var isAdmin = await users.IsInRoleAsync(user!, "Administrator");
            if (isAdmin)
            {
                // Land in admin tools. Force=true because claims may not be on HttpContext.User yet.
                workspace.SetMode(AdminWorkspaceService.AdminMode, force: true);
                if (Url.IsLocalUrl(form.ReturnUrl)) return LocalRedirect(form.ReturnUrl!);
                return RedirectToAction("Index", "Admin");
            }
            workspace.SetMode(AdminWorkspaceService.StudentMode, force: true);
            if (Url.IsLocalUrl(form.ReturnUrl)) return LocalRedirect(form.ReturnUrl!);
            return RedirectToAction("Index", "Dashboard");
        }
        ModelState.AddModelError("", result.IsLockedOut ? "Too many attempts. Please try again in a few minutes." : "Email or password is incorrect.");
        return View(form);
    }
    [HttpGet] public IActionResult Register() => View(new RegisterForm());
    [HttpPost]
    public async Task<IActionResult> Register(RegisterForm form)
    {
        if (!ModelState.IsValid) return View(form);
        var user = new ApplicationUser { FullName = form.FullName.Trim(), UserName = form.Email.Trim(), Email = form.Email.Trim() };
        var result = await users.CreateAsync(user, form.Password);
        if (result.Succeeded)
        {
            await users.AddToRoleAsync(user, "Student");
            await signIn.SignInAsync(user, false);
            return RedirectToAction("Index", "Dashboard");
        }
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return View(form);
    }
    // GET fallbacks avoid Chrome's HTTP 405 when users refresh/back onto a POST-only URL.
    [Authorize, HttpGet]
    public IActionResult Logout() => RedirectToAction("Index", "Home");

    [Authorize, HttpPost, ActionName("Logout")]
    public async Task<IActionResult> LogoutPost()
    {
        Response.Cookies.Delete(AdminWorkspaceService.CookieName);
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [Authorize, HttpGet]
    public IActionResult SetWorkspace() =>
        RedirectToAction("Index", workspace.ShowAdminUi ? "Admin" : "Dashboard");

    [Authorize, HttpPost]
    public IActionResult SetWorkspace(string mode)
    {
        if (!User.IsInRole("Administrator"))
            return RedirectToAction("Index", "Dashboard");

        var next = string.Equals(mode, AdminWorkspaceService.AdminMode, StringComparison.OrdinalIgnoreCase)
            ? AdminWorkspaceService.AdminMode
            : AdminWorkspaceService.StudentMode;

        workspace.SetMode(next);
        return next == AdminWorkspaceService.AdminMode
            ? RedirectToAction("Index", "Admin")
            : RedirectToAction("Index", "Dashboard");
    }

    [Authorize, HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordForm());

    [Authorize, HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordForm form)
    {
        var returnToProfile = string.Equals(Request.Form["returnTo"], "Profile", StringComparison.OrdinalIgnoreCase);
        IActionResult Fail()
        {
            if (!returnToProfile) return View(form);
            var message = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)));
            TempData["Error"] = string.IsNullOrWhiteSpace(message) ? "Could not update your password." : message;
            return RedirectToAction("Index", "Profile");
        }

        if (!ModelState.IsValid) return Fail();
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var result = await users.ChangePasswordAsync(user, form.CurrentPassword, form.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return Fail();
        }
        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Your password was updated.";
        return returnToProfile
            ? RedirectToAction("Index", "Profile")
            : RedirectToAction("Index", "Settings");
    }
}
