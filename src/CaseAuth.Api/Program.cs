using System.Text.Json.Serialization;
using CaseAuth.Api.Auth;
using CaseAuth.Api.Data;
using CaseAuth.Api.Errors;
using CaseAuth.Api.Infrastructure;
using CaseAuth.Api.Pipeline;
using CaseAuth.Api.Screening;
using CaseAuth.Api.Services;
using CaseAuth.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// --- Database --------------------------------------------------------------------------
var dbProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<CaseAuthDbContext>(options =>
{
    if (string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
    }
    else
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=caseauth.db");
    }
});

// --- Auth --------------------------------------------------------------------------------
// The X-Dev-User handler trusts a plain header against a hardcoded seed list (see
// Auth/DevUserStore.cs) and is only ever wired up in Development. There is no production
// authentication handler configured yet - wire up real OIDC/JWT before deploying anywhere else.
if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddAuthentication(DevAuthDefaults.Scheme)
        .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthDefaults.Scheme, configureOptions: null);
}
else
{
    throw new InvalidOperationException(
        "No production authentication handler is configured. The development-only X-Dev-User " +
        "handler (Auth/DevAuthenticationHandler.cs) is intentionally refused outside the " +
        "Development environment; configure real authentication before running elsewhere.");
}

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// --- App services --------------------------------------------------------------------------
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ICaseAccessor, CaseAccessor>();

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddScoped<IFileStorageService>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>();
    return string.Equals(options.Value.Mode, "S3", StringComparison.OrdinalIgnoreCase)
        ? new S3FileStorageService()
        : new LocalDiskFileStorageService(options);
});

// --- Background pipeline ---------------------------------------------------------------
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.SectionName));
builder.Services.AddScoped<IDocumentExtractor, PdfTextDocumentExtractor>();

var screeningOptions = new ScreeningOptions();
builder.Configuration.GetSection("Screening").Bind(screeningOptions);
screeningOptions.Validate();
builder.Services.AddSingleton(screeningOptions);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<ScreeningEngine>();
builder.Services.AddSingleton<SanctionsSnapshot>(services =>
    SanctionsSnapshots.FromConfiguration(
        services.GetRequiredService<ScreeningOptions>(),
        services.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<IScreeningService, DeterministicScreeningService>();
var aiReviewOptions = builder.Configuration.GetSection(AiReviewAgentOptions.SectionName).Get<AiReviewAgentOptions>() ?? new();
if (aiReviewOptions.Mode.Equals("Remote", StringComparison.OrdinalIgnoreCase))
{
    if (!Uri.TryCreate(aiReviewOptions.BaseUrl, UriKind.Absolute, out var aiReviewBaseUrl)
        || aiReviewOptions.TimeoutSeconds <= 0)
    {
        throw new InvalidOperationException("AiReviewAgent requires an absolute BaseUrl and a positive TimeoutSeconds.");
    }

    builder.Services.AddHttpClient<IAiReviewer, RemoteAiReviewer>(client =>
    {
        client.BaseAddress = new Uri($"{aiReviewBaseUrl.ToString().TrimEnd('/')}/");
        client.Timeout = TimeSpan.FromSeconds(aiReviewOptions.TimeoutSeconds);
    });
}
else if (aiReviewOptions.Mode.Equals("Deterministic", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IAiReviewer, DeterministicAiReviewer>();
}
else
{
    throw new InvalidOperationException($"Unsupported AiReviewAgent:Mode '{aiReviewOptions.Mode}'. Use Remote or Deterministic.");
}
builder.Services.AddScoped<IPipelineJobProcessor, PipelineJobProcessor>();
builder.Services.AddHostedService<PipelineBackgroundService>();

// --- MVC / OpenAPI ---------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CaseAuth API", Version = "v1" });

    // Without this, Swashbuckle leaves operationId unset and the generated Angular client
    // falls back to ugly path-derived names (e.g. apiCasesIdWithdrawPost). ControllerName is
    // included because action names collide across controllers (several have Create/List).
    c.CustomOperationIds(apiDesc =>
        apiDesc.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor cad
            ? $"{cad.ControllerName}_{cad.ActionName}"
            : null);

    const string devUserScheme = "DevUser";
    c.AddSecurityDefinition(devUserScheme, new OpenApiSecurityScheme
    {
        Name = DevAuthDefaults.HeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Description = "Development-only auth: set to a seeded username (analyst1, analyst2, supervisor).",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = devUserScheme },
            },
            Array.Empty<string>()
        },
    });
});

const string angularDevCorsPolicy = "AngularDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(angularDevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Convenience for local/demo use only: applies pending migrations on startup instead of
    // requiring a separate `dotnet ef database update` step.
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<CaseAuthDbContext>().Database.Migrate();
}

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseCors(angularDevCorsPolicy);

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
