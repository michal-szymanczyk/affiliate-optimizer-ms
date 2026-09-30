using AffiliateOptimizer.Repository.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AffiliateOptimizer.Web.Features.Authentication;

public sealed class LoginModel
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}

/// <summary>Cookie sign-in with ASP.NET Core Identity (ArchitectureSimple.md §8.2). The only controller with more than one statement per action.</summary>
[Route("authentication/[action]")]
public sealed class AuthenticationController(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginModel { ReturnUrl = returnUrl });

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginModel model)
    {
        var user = string.IsNullOrWhiteSpace(model.Email) ? null : await userManager.FindByEmailAsync(model.Email.Trim());
        var result = user is null
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signInManager.PasswordSignInAsync(user, model.Password ?? string.Empty, model.RememberMe, lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            model.Password = null;
            model.Error = "Unknown operator or wrong password.";
            return View(model);
        }

        var target = !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : "/";
        return LocalRedirect(target);
    }

    /// <summary>Called by the SPA with a plain POST; signing out is not a CSRF-worthy action.</summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }
}
