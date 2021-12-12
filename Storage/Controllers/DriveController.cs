using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Auth.AspNetCore3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Storage.Controllers
{
    [Route("[controller]")]
    [GoogleScopedAuthorize(DriveService.ScopeConstants.DriveFile)]
    public class DriveController : Controller
    {
        private readonly IGoogleAuthProvider _authProvider;

        public DriveController([FromServices] IGoogleAuthProvider authProvider)
        {
            _authProvider = authProvider;
        }



        [HttpGet]
        public async Task<List<string>> DriveFileList()
        {
            GoogleCredential cred = await _authProvider.GetCredentialAsync();
            var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred
            });
            var files = await service.Files.List().ExecuteAsync();
            var fileNames = files.Files.Select(x => x.Name).ToList();
            return fileNames;
        }
    }
}