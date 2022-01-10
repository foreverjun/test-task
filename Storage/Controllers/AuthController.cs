namespace Storage.Controllers;

using Google.Apis.Auth.AspNetCore3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Google.Apis.Oauth2.v2;
using Google.Apis.Oauth2.v2.Data;

[ApiController]
[Route("api/Drive/User")]
public class AuthController : ControllerBase
{
    [GoogleScopedAuthorize(DriveService.ScopeConstants.DriveFile, Oauth2Service.ScopeConstants.UserinfoProfile)]
    [HttpGet("Auth")]
    public async Task<RedirectResult> Auth()
    {
        //Redirect to the desired address after authentication
        return Redirect("~/");
    }

    [HttpGet("IsAuthorized")]
    public async Task<Boolean> IsAuthenticated()
    {
        return User.Identity.IsAuthenticated;
    }

    [HttpGet("UserInfo")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task<Userinfo> UserInfo([FromServices] IGoogleAuthProvider authProvider)
    {
        GoogleCredential cred = await authProvider.GetCredentialAsync();
        var service = new Oauth2Service(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred
        });
        return await service.Userinfo.Get().ExecuteAsync();
    }
}