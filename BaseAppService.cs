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

        public Guid? CurrentUserId
        {
            get => Guid.Parse(_httpContext.HttpContext.User?.FindFirst(i => i.Type == ClaimTypes.NameIdentifier)?.Value);
        }
    }
}
