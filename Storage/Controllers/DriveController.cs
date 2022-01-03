using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http.Results;
using Google.Apis.Auth.AspNetCore3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Storage.Controllers
{
    [ApiController]
    [Route("api/User/Drive")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    public class DriveController : ControllerBase
    {
        private readonly IGoogleAuthProvider _authProvider;
        
        public DriveController([FromServices] IGoogleAuthProvider authProvider)
        {
            _authProvider = authProvider;
        }
        
        [HttpGet("Files")]
        public async Task<List<KeyValuePair<string,string>>> GetFileList()
        {
            GoogleCredential cred = await _authProvider.GetCredentialAsync();
            var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred
            });
            
            var files = await service.Files.List().ExecuteAsync();
            var filesData = files.Files.Select(x => new KeyValuePair<string,string>(x.Id,x.Name)).ToList();
            return filesData;
        }

        [HttpGet("File")]
        public async Task<FileStreamResult> GetFile(string fileId)
        {
            GoogleCredential cred = await _authProvider.GetCredentialAsync();
            var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred
            });

            string filepath = Path.GetTempFileName();
            Console.WriteLine(filepath);
            string fileName = service.Files.Get(fileId).Execute().Name;
            var getRequest = service.Files.Get(fileId);
            Stream fileStream = null;
            fileStream = new FileStream(filepath, FileMode.Create, FileAccess.ReadWrite,FileShare.None,512,FileOptions.DeleteOnClose);
                getRequest.MediaDownloader.ProgressChanged += progress => Console.WriteLine(progress.Status + "  " + progress.BytesDownloaded);
                getRequest.Download(fileStream);
                fileStream.Seek(0, SeekOrigin.Begin);
                return File(fileStream,"text/plan", fileName);
            }
        
        [DisableFormValueModelBinding]
        [RequestSizeLimit(10L * 1024L * 1024L * 1024L)]
        [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024L * 1024L * 1024L)]
        [HttpPost("File")]
        public async Task<ActionResult> SendFile()
        {
            if (!MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
            {
                return BadRequest("Not a multipart request");
            }

            var boundary = MultipartRequestHelper.GetBoundary(MediaTypeHeaderValue.Parse(Request.ContentType));
            var reader = new MultipartReader(boundary, Request.Body);

            // note: this is for a single file, you could also process multiple files
            var section = await reader.ReadNextSectionAsync();

            if (section == null)
                return BadRequest("No sections in multipart defined");

            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
                return BadRequest("No content disposition in multipart defined");

            var fileName = contentDisposition.FileNameStar.ToString();
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = contentDisposition.FileName.ToString();
            }

            if (string.IsNullOrEmpty(fileName))
                return BadRequest("No filename defined.");
            
            GoogleCredential cred = await _authProvider.GetCredentialAsync();
            var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred
            });

            await using var fileStream = section.Body;
            
            Google.Apis.Drive.v3.Data.File driveFile = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName
            };
            
            FilesResource.CreateMediaUpload insertRequest = service.Files.Create(
                driveFile, fileStream, "text/plain");
            
            insertRequest.ProgressChanged += Upload_ProgressChanged;
            insertRequest.ResponseReceived += Upload_ResponseReceived;

            await insertRequest.UploadAsync();

            static void Upload_ProgressChanged(IUploadProgress progress) =>
                Console.WriteLine(progress.Status + " " + progress.BytesSent);

            static void Upload_ResponseReceived(Google.Apis.Drive.v3.Data.File file) =>
                Console.WriteLine(file.Name + " was uploaded successfully");
            return Ok();
        }
    }
    
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var factories = context.ValueProviderFactories;
            factories.RemoveType<FormValueProviderFactory>();
            factories.RemoveType<FormFileValueProviderFactory>();
            factories.RemoveType<JQueryFormValueProviderFactory>();
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
    
    public static class MultipartRequestHelper
    {
        public static string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit = 70)
        {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;

            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new InvalidDataException("Missing content-type boundary.");
            }

            if (boundary.Length > lengthLimit)
            {
                throw new InvalidDataException(
                    $"Multipart boundary length limit {lengthLimit} exceeded.");
            }

            return boundary;
        }

        public static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType)
                   && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}