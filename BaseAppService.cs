using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Service.Utils;
using Spire.Pdf;
using System;
using System.Buffers.Text;
using System.IO;
using System.Security.Claims;

namespace Service.Base
{
    public abstract class BaseAppService
    {
        private readonly IHttpContextAccessor _httpContext;

        public BaseAppService(IHttpContextAccessor httpContext)
        {
            this._httpContext = httpContext;
        }
        public string PrepareFileForUpload(UploadFileDto input)
        {
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"TempFiles");

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var id = Guid.NewGuid().ToString().Replace("-", "").ToUpper();
            var fullPath = $"{folder}\\{(string.IsNullOrWhiteSpace(input.Name) ? "" : Path.GetFileNameWithoutExtension(input.Name) + "_")}{id}{input.FileExtension}";

            byte[] bytes = Convert.FromBase64String(input.Content);

            File.WriteAllBytes(fullPath, bytes);

            return fullPath;
        }

        public static string PrepareFileForAttachmentsUpload(UploadFileDto input, IConfiguration configuration, bool isLocalFolder = true, string folder = "")
        {
            if (isLocalFolder)
            {
                folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "@Templates");
            }

            if (!Directory.Exists(folder.ToString()))
            {
                Directory.CreateDirectory(folder.ToString());
            }
            var id = Guid.NewGuid().ToString().Replace("-", "").ToUpper();

            var fullPath = $"{folder}\\{(string.IsNullOrWhiteSpace(input.Name) ? "" : Path.GetFileNameWithoutExtension(input.Name) + "_")}{id}{input.FileExtension}";

            byte[] bytes = Convert.FromBase64String(input.Content);


            File.WriteAllBytes(fullPath, bytes);

            var actualType = MimeTypes.GetContentType(fullPath);

            if (actualType.EndsWith("jpg") || actualType.EndsWith("jpeg") || actualType.EndsWith("png") || actualType.EndsWith("pdf"))
            {
                return $"Attachments/{Path.GetFileNameWithoutExtension(input.Name)}_{id}{input.FileExtension}";
            }
            else
            {
                throw new Exception("File is not Valid");
            }
        }

        public Guid? CurrentUserId
        {
            get => Guid.Parse(_httpContext.HttpContext.User?.FindFirst(i => i.Type == ClaimTypes.NameIdentifier)?.Value);
        }
    }
}
