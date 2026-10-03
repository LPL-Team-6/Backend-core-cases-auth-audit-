using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using CaseAuth.Api.Pipeline;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CaseAuth.Api.Tests;

// Each instance gets its own SQLite file and upload directory under the temp dir, so parallel
// test classes never collide and nothing is left behind in the repo.
public class ApiFactory : WebApplicationFactory<Program>
{
    public string AiReviewMode { get; set; } = "Deterministic";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"caseauth-test-{Guid.NewGuid():N}.db");
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"caseauth-test-uploads-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs refuses to start the dev auth handler outside Development.
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sqlite"] = $"Data Source={_dbPath}",
                ["Storage:LocalDiskRoot"] = _storageRoot,
                // Fast polling so pipeline-job tests don't need long sleeps/timeouts.
                ["Pipeline:PollIntervalSeconds"] = "1",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            if (AiReviewMode.Equals("Deterministic", StringComparison.OrdinalIgnoreCase))
            {
                services.RemoveAll<IAiReviewer>();
                services.AddScoped<IAiReviewer, DeterministicAiReviewer>();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        // Pooled SQLite connections keep the file open, and Windows won't delete an open file.
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }
}
