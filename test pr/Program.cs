using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace DriveQuickstart
{
    class Program
    {
        static string[] Scopes = {DriveService.Scope.DriveReadonly};
        static string ApplicationName = "Download files";

        static void Main(string[] args)
        {
            UserCredential credential;
            var path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            using (var stream =
                new FileStream(path + @"\credentials.json",
                    FileMode.Open, FileAccess.Read))
            {
                // The file token.json stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                string credPath = path + @"\token.json";
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
            }

            // Create Drive API service.
            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            switch (args.Length)
            {
                case 1 when args[0] == "--files":
                    getFilesList(service);
                    break;
                case 2 when args[0] == "--download":
                    DownloadFile(service, args[1]);
                    break;
                default:
                    Console.WriteLine("App receive arguments: --files or --download [file id]");
                    break;
            }
        }


        static void getFilesList(DriveService service)
        {
            var request = service.About.Get();
            request.Fields = "user";
            var emailAddress = request.Execute().User.EmailAddress;

            // Define parameters of request.
            FilesResource.ListRequest listRequest = service.Files.List();
            listRequest.Q = ("'" + emailAddress + "' in owners and mimeType != 'application/vnd.google-apps.folder'");
            listRequest.Fields = "nextPageToken, files(id, name)";

            // List files.
            IList<Google.Apis.Drive.v3.Data.File> files = listRequest.Execute().Files;
            Console.WriteLine("Files:");
            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    Console.WriteLine("{0} ({1})", file.Name, file.Id);
                }
            }
            else
            {
                Console.WriteLine("No files found.");
            }
        }

        static void DownloadFile(DriveService service, string fileId)
        {
            var request = service.Files.Get(fileId);
            var stream = new MemoryStream();

            request.MediaDownloader.ProgressChanged += (Google.Apis.Download.IDownloadProgress progress) =>
            {
                switch (progress.Status)
                {
                    case Google.Apis.Download.DownloadStatus.Downloading:
                    {
                        Console.WriteLine("Bytes Downloaded: "+progress.BytesDownloaded);
                        break;
                    }
                    case Google.Apis.Download.DownloadStatus.Completed:
                    {
                        Console.WriteLine("Download complete.");
                        using (FileStream file = new FileStream(
                            Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + @"\" +
                            service.Files.Get(fileId).Execute().Name, FileMode.Create,
                            FileAccess.Write))
                        {
                            stream.WriteTo(file);
                        }

                        break;
                    }
                    case Google.Apis.Download.DownloadStatus.Failed:
                    {
                        Console.WriteLine("Download failed.");
                        break;
                    }
                }
            };
            request.Download(stream);
        }
    }
}