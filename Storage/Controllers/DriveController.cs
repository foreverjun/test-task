using Storage.Utilities;

namespace Storage.Controllers;

using System.Net;
using Google;
using Google.Apis.Download;
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

[ApiController]
[Route("api/Drive/User")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class DriveController : ControllerBase
{
    private readonly IGoogleAuthProvider _authProvider;

    public DriveController([FromServices] IGoogleAuthProvider authProvider)
    {
        _authProvider = authProvider;
    }

    //Returns a list of file names and ID on the disk.
    [HttpGet("Files")]
    public async Task<List<KeyValuePair<string, string>>> GetFileList()
    {
        GoogleCredential cred = await _authProvider.GetCredentialAsync();
        var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred
        });

        var requestList = service.Files.List();
        requestList.Q = "mimeType != 'application/vnd.google-apps.folder'";
        var files = await requestList.ExecuteAsync();

        var filesData = files.Files.Select(x => new KeyValuePair<string, string>(x.Id, x.Name)).ToList();
        return filesData;
    }

    //Returns the file by its unique id.
    [HttpGet("File")]
    public async Task<ActionResult> GetFile(string fileId)
    {
        GoogleCredential cred = await _authProvider.GetCredentialAsync();
        var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred
        });
        
        string filepath = Path.GetTempFileName();
        try
        {
            DownloadStatus downloadResult = DownloadStatus.NotStarted;
            string fileName = service.Files.Get(fileId).Execute().Name;
            var getRequest = service.Files.Get(fileId);
            var fileStream = new FileStream(filepath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 512,
                FileOptions.DeleteOnClose);
            getRequest.MediaDownloader.ProgressChanged += progress => downloadResult = progress.Status;
            getRequest.Download(fileStream);
            fileStream.Seek(0, SeekOrigin.Begin);
            if (downloadResult == DownloadStatus.Failed)
                return BadRequest("Download failed");
            return File(fileStream, "text/plan", fileName);
        }
        catch (GoogleApiException e)
        {
            if (e.Error.Code == (int) HttpStatusCode.NotFound) 
                return NotFound($"File with id {fileId} not found");
            throw;
        }
    }
    
    //Gets the file and updates the file in the cloud storage.
    [DisableFormValueModelBinding]
    [RequestSizeLimit(3L * 1024L * 1024L * 1024L)]
    [RequestFormLimits(MultipartBodyLengthLimit = 3L * 1024L * 1024L * 1024L)]
    [HttpPut("File")]
    public async Task<ActionResult> UpdateFile(string fileId)
    {
        if (!MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
        {
            return BadRequest("Not a multipart request");
        }

        var boundary = MultipartRequestHelper.GetBoundary(MediaTypeHeaderValue.Parse(Request.ContentType));
        var reader = new MultipartReader(boundary, Request.Body);

        var section = await reader.ReadNextSectionAsync();

        if (section == null)
            return BadRequest("No sections in multipart defined");

        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
            return BadRequest("No content disposition in multipart defined");
        
        GoogleCredential cred = await _authProvider.GetCredentialAsync();
        var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred
        });
        
        UploadStatus uploadResult = UploadStatus.NotStarted;
        string id = null;
        try
        {
            await using var fileStream = section.Body;
            string fileName = service.Files.Get(fileId).Execute().Name;
            var driveFile = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName
            };

            FilesResource.UpdateMediaUpload insertRequest = service.Files.Update(
                driveFile, fileId, fileStream, "text/plain");

            insertRequest.ProgressChanged += progress => uploadResult = progress.Status;
            insertRequest.ResponseReceived += file => id = file.Id;
            await insertRequest.UploadAsync();
            if (uploadResult == UploadStatus.Failed)
                return BadRequest("Upload failed");
            return Ok(id);
        }
        catch (GoogleApiException e)
        {
            if (e.Error.Code == (int) HttpStatusCode.NotFound) 
                return NotFound($"File with id {fileId} not found");
            throw;
        }
    }

    //Gets the file, uploads the file to google disk, creating a new.
    [DisableFormValueModelBinding]
    [RequestSizeLimit(3L * 1024L * 1024L * 1024L)]
    [RequestFormLimits(MultipartBodyLengthLimit = 3L * 1024L * 1024L * 1024L)]
    [HttpPost("File")]
    public async Task<ActionResult> SendFile()
    {
        if (!MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
        {
            return BadRequest("Not a multipart request");
        }

        var boundary = MultipartRequestHelper.GetBoundary(MediaTypeHeaderValue.Parse(Request.ContentType));
        var reader = new MultipartReader(boundary, Request.Body);

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

        UploadStatus uploadResult = UploadStatus.NotStarted;
        string id = null;
        insertRequest.ProgressChanged += progress => uploadResult = progress.Status;
        insertRequest.ResponseReceived += file => id = file.Id;
        await insertRequest.UploadAsync();
        if (uploadResult == UploadStatus.Failed)
            return BadRequest("Upload failed");
        return Ok(id);
    }
}