using AgroClimate.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace AgroClimate.Web.Controllers;

public sealed class AuthController : Controller
{
    [HttpGet("/Auth/Login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        return View(DemoRoles.AllPersonas);
    }

    [HttpPost("/Auth/SwitchRole")]
    public async Task<IActionResult> SwitchRole([FromForm] string roleKey, [FromForm] string? returnUrl = null)
    {
        var persona = DemoRoles.FindOrDefault(roleKey);
        var principal = DemoRoles.BuildPrincipal(persona, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Dashboard");
    }
}
