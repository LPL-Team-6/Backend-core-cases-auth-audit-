using CaseAuth.Api.Auth;
using CaseAuth.Api.Demo;
using CaseAuth.Api.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaseAuth.Api.Controllers;

public record DemoResetResponse(int CasesCreated);

// "Reset demo" for the review UI: wipes the caller's firm's cases and reloads the synthetic
// personas, each waiting for a decision. Development only (404 elsewhere, same as an unknown
// route) and supervisor only, scoped to the caller's own firm.
[ApiController]
[Route("api/demo")]
[Authorize]
public class DemoController(
    ICurrentUser currentUser,
    IDemoSeeder seeder,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("reset")]
    public async Task<ActionResult<DemoResetResponse>> Reset(CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        if (currentUser.Role != Roles.Supervisor)
        {
            throw new ForbiddenApiException("Only a supervisor can reset the demo data.");
        }

        return new DemoResetResponse(await seeder.ResetAsync(currentUser.FirmId, ct));
    }
}
