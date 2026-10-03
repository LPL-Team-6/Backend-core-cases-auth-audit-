using CaseAuth.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CaseAuth.Api.Controllers;

public record UploadLimitsResponse(long MaxUploadBytes, IReadOnlyList<string> AllowedContentTypes);

// Lets a client check a file before uploading it, against the same Storage options that
// DocumentsController enforces, instead of copying the limits into frontend constants.
[ApiController]
[Route("api/upload-limits")]
[Authorize]
public class UploadLimitsController(IOptions<StorageOptions> storageOptions) : ControllerBase
{
    [HttpGet]
    public ActionResult<UploadLimitsResponse> Get() =>
        new UploadLimitsResponse(storageOptions.Value.MaxUploadBytes, storageOptions.Value.AllowedContentTypes);
}
