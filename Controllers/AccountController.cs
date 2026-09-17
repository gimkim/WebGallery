using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WebGallery.Models;
using WebGallery.Services;
using WebGallery.ViewModels;

namespace WebGallery.Controllers;

public sealed class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    LoginAttemptLimiter attemptLimiter,
    LoginSecuritySettings loginSecuritySettings,
    TimeProvider timeProvider) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var clientAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cooldown = attemptLimiter.GetCooldown(clientAddress, model.UserName);
        if (cooldown.IsActive) return Cooldown(model, cooldown);

        var result = await signInManager.PasswordSignInAsync(model.UserName, model.Password, model.RememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            attemptLimiter.Reset(clientAddress, model.UserName);
            var signedIn = await userManager.FindByNameAsync(model.UserName);
            if (signedIn?.RequirePasswordChange == true) return RedirectToAction(nameof(ChangePassword));
            return LocalRedirect(string.IsNullOrWhiteSpace(model.ReturnUrl) ? Url.Action("Index", "Gallery")! : model.ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            var user = await userManager.FindByNameAsync(model.UserName);
            var lockoutEnd = user is null ? null : await userManager.GetLockoutEndDateAsync(user);
            if (lockoutEnd is DateTimeOffset end)
            {
                var identityRetryAfter = end - timeProvider.GetUtcNow();
                if (identityRetryAfter > cooldown.RetryAfter) cooldown = new LoginCooldown(identityRetryAfter);
            }
            return Cooldown(model, cooldown.IsActive ? cooldown : new LoginCooldown(TimeSpan.FromSeconds(1)));
        }

        cooldown = attemptLimiter.RecordFailure(clientAddress, model.UserName);
        var failedUser = await userManager.FindByNameAsync(model.UserName);
        if (failedUser is not null && await userManager.GetLockoutEnabledAsync(failedUser))
        {
            var loginOptions = loginSecuritySettings.Current;
            var accessResult = await userManager.AccessFailedAsync(failedUser);
            if (accessResult.Succeeded && await userManager.GetAccessFailedCountAsync(failedUser) >= loginOptions.UserFailureLimit)
            {
                var lockoutEnd = timeProvider.GetUtcNow() + loginOptions.UserCooldown;
                var lockoutResult = await userManager.SetLockoutEndDateAsync(failedUser, lockoutEnd);
                if (lockoutResult.Succeeded)
                {
                    await userManager.ResetAccessFailedCountAsync(failedUser);
                    var identityRetryAfter = lockoutEnd - timeProvider.GetUtcNow();
                    if (identityRetryAfter > cooldown.RetryAfter) cooldown = new LoginCooldown(identityRetryAfter);
                }
            }
        }
        if (cooldown.IsActive) return Cooldown(model, cooldown);

        ModelState.AddModelError("", "Incorrect username or password.");
        return View(model);
    }

    private IActionResult Cooldown(LoginViewModel model, LoginCooldown cooldown)
    {
        model.Password = "";
        model.RetryAfterSeconds = cooldown.RetryAfterSeconds;
        Response.StatusCode = StatusCodes.Status429TooManyRequests;
        Response.Headers.RetryAfter = cooldown.RetryAfterSeconds.ToString();
        return View(model);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }

    [AllowAnonymous]
    public IActionResult Denied() => View();

    [Authorize, HttpGet]
    public IActionResult ChangePassword() => View(new PasswordChangeViewModel());

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(PasswordChangeViewModel model)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid) return View(model);
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (model.CurrentPassword == model.NewPassword) ModelState.AddModelError("", "Choose a different password.");
        else {
            var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword ?? "", model.NewPassword);
            if (result.Succeeded) {
                user.RequirePasswordChange = false;
                var saved = await userManager.UpdateAsync(user);
                if (saved.Succeeded) { await signInManager.RefreshSignInAsync(user);return RedirectToAction("Index", "Gallery"); }
                foreach (var error in saved.Errors) ModelState.AddModelError("", error.Description);
            } else foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        }
        return View(model);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult ResetPassword(string userId, string token)
    {
        Response.Headers.CacheControl="no-store";Response.Headers["Referrer-Policy"]="no-referrer";
        return View(new PasswordChangeViewModel { UserId=userId,Token=token });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(PasswordChangeViewModel model)
    {
        Response.Headers.CacheControl="no-store";Response.Headers["Referrer-Policy"]="no-referrer";
        var ip=HttpContext.Connection.RemoteIpAddress?.ToString()??"unknown";
        if (attemptLimiter.GetCooldown(ip,"password-reset").IsActive) {
            Response.StatusCode=429;ModelState.AddModelError("", "Too many attempts. Please try again later.");return View(model);
        }
        if (!ModelState.IsValid) return View(model);
        var user=await userManager.FindByIdAsync(model.UserId??"");
        var result=user is null ? IdentityResult.Failed() : await userManager.ResetPasswordAsync(user,model.Token??"",model.NewPassword);
        if(result.Succeeded && user is not null) {
            user.RequirePasswordChange=false;await userManager.UpdateAsync(user);
            await signInManager.SignOutAsync();TempData["Success"]="Password changed. Sign in with your new password.";
            return RedirectToAction(nameof(Login));
        }
        attemptLimiter.RecordFailure(ip,"password-reset");
        ModelState.AddModelError("", "The link is invalid, expired or already used, or the password does not meet the requirements.");return View(model);
    }
}
