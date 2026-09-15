using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Betalgo.Ranul.OpenAI.Extensions;
using DotnetSdkUtilities.Extensions.ServiceCollectionExtensions;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using NLog;
using NLog.Web;
using System.Reflection;

// Early init of NLog to allow startup and exception logging, before host is built
var logger = LogManager.Setup()
                       .LoadConfigurationFromAppSettings()
                       .GetCurrentClassLogger();
logger.Debug("program start");

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Host.UseNLog();

    // Cloud log/trace/metric sink for prod debugging — NLog's file targets write to the App
    // Service instance's local disk (c:/temp), which is ephemeral and only reachable via the
    // Kudu console. This mirrors ILogger calls (still hitting NLog too) plus auto-instruments
    // HTTP requests and outbound calls (Anthropic, MCP, TWSE, ...) with latency/failure data.
    // No-ops locally / anywhere the connection string isn't set — set
    // APPLICATIONINSIGHTS_CONNECTION_STRING as an Azure App Service Application Setting to
    // enable it in prod; nothing to configure in this repo.
    var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"]
        ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
    if (!string.IsNullOrEmpty(appInsightsConnectionString))
    {
        builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsightsConnectionString);
    }

    // Add services to the container.
    if (builder.Environment.IsProduction())
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
            new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
    }

    // Blob Storage for listening-exam question images (pre-seeded via the bulk importer tool,
    // not written to at request time). No-ops if unset — same pattern as App Insights above.
    // Secret name in Key Vault: BlobStorage--ConnectionString.
    var blobStorageConnectionString = builder.Configuration["BlobStorage:ConnectionString"];
    if (!string.IsNullOrEmpty(blobStorageConnectionString))
    {
        builder.Services.AddSingleton(new BlobServiceClient(blobStorageConnectionString));
        builder.Services.AddScoped<JackyAIApp.Server.Services.Listening.IListeningImageStorageService, JackyAIApp.Server.Services.Listening.ListeningImageStorageService>();
    }

    var configuration = builder.Configuration;

    // Configure SQL Database Context
    var sqlConnectionString = configuration.GetConnectionString("SQLConnection") ?? "";
    void ConfigureSqlOptions(DbContextOptionsBuilder options) =>
        options.UseSqlServer(sqlConnectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        });
    builder.Services.AddDbContext<AzureSQLDBContext>(ConfigureSqlOptions);
    // Also register the factory form: DbContext isn't thread-safe, so code that fans out
    // concurrent DB work within one request (e.g. PersistentTwHistoricalProvider, called from
    // PortfolioService's Task.WhenAll across many stock symbols) needs a fresh, short-lived
    // context per concurrent operation instead of sharing the one scoped instance.
    builder.Services.AddDbContextFactory<AzureSQLDBContext>(ConfigureSqlOptions);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax; // Required for OAuth redirect
        options.Cookie.MaxAge = TimeSpan.FromDays(1);
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
        options.SlidingExpiration = true; // Auto-renew while active
        // For API requests, return 401 instead of redirecting to login page
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.LoginPath = "/api/account/login/Google";
    })
    .AddGoogle(googleOptions =>
    {
        googleOptions.ClientId = builder.Configuration["Settings:Google:ClientId"]?.ToString() ?? "";
        googleOptions.ClientSecret = builder.Configuration["Settings:Google:ClientSecret"]?.ToString() ?? "";
    });
    builder.Services.AddExtendedMemoryCache();
    builder.Services.AddMemoryCache();
    // Default timeout for every HttpClient the app creates (named, typed, or the
    // unnamed default from CreateClient()) — previously unbounded (~100s default),
    // so one slow external API (TWSE/Yahoo/FundRich/SEC/FRED/...) could tie up a
    // request thread far longer than a user will ever wait for a response.
    // Individual AddHttpClient(...) registrations below can still override this.
    builder.Services.ConfigureHttpClientDefaults(http =>
        http.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(20)));

    // The bare unnamed default client (CreateClient() with no name) is used exclusively by
    // TWStockLib's internal TwseHttpFetcher — all idempotent GETs. Retrying it absorbs the
    // transient TWSE/Azure-outbound flakiness behind the intermittent "查無股票" search failures
    // (TWStockLib had timeout but no retry). Override the 20s global default — TWSE's full-market
    // STOCK_DAY_ALL CSV (Big5) alone measured ~13s round-trip; 20s left too little margin for
    // prod network variance and was observed aborting mid-read (SSL stream disposed) rather than
    // reaching the API's own error handling.
    builder.Services.AddHttpClient(string.Empty, client => client.Timeout = TimeSpan.FromSeconds(30))
        .AddReadOnlyResilience();

    // Named client for read-only external market-data GETs (and FundRich's POST-as-query
    // calls) — TWSE, Yahoo, FundRich, BOT/CBC via er-api, SEC, Finnhub, FRED. Deliberately
    // NOT applied via ConfigureHttpClientDefaults: that would also retry OpenAI completions
    // and PayPal/ECPay payment calls, where a transport-level failure after the request
    // already landed server-side would risk a duplicate charge/generation. These providers
    // are all idempotent reads, so retrying is safe (see AddReadOnlyResilience).
    builder.Services.AddHttpClient("ExternalReadOnly").AddReadOnlyResilience();
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
    
    // 配置 Response Compression 支援 streaming
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
    });
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Version = "v1",
            Title = "JackyAI API",
            Description = "",
        });
        var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
    });

    // Feature-grouped registrations (see Extensions/ServiceCollectionExtensions.cs and each
    // module's own *ServiceExtensions.cs) — one call per domain instead of registrations
    // scattered inline here, so the full DI graph is auditable module-by-module.
    builder.Services.AddCoreServices(builder.Configuration);
    builder.Services.AddVocabularyServices();
    builder.Services.AddFinanceAnalysisServices();
    builder.Services.AddJobAggregatorServices();
    builder.Services.AddPortfolioServices();
    builder.Services.AddConnectorAndPaymentServices(builder.Configuration);
    builder.Services.AddDataAnalysisServices(sqlConnectionString);

    var openAIKey = configuration.GetValue<string>("Settings:OpenAI:Key") ?? "";
    builder.Services.AddOpenAIService(options =>
    {
        options.ApiKey = openAIKey;
        options.UseBeta = true;
    });

    builder.Services.AddOptions().Configure<Settings>(builder.Configuration.GetSection("Settings"));

    builder.Services.AddHealthChecks();
    var app = builder.Build();

    // Apply any pending EF Core migrations on startup so schema changes ship with the
    // deploy instead of requiring a manual `dotnet ef database update` against prod.
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AzureSQLDBContext>();
        dbContext.Database.Migrate();
    }

    app.UseResponseCompression();
    app.UseMiddleware<ExceptionMiddleware>();

    // Link-preview bots (LINE/Facebook/etc.) don't execute JS, so intercept them before the
    // SPA static-file/fallback pipeline with a server-rendered title/OG shell — see
    // BotPreviewMiddleware and .agent/architecture/ARCHITECTURE.md ("Phase 2"). Every other
    // request (real users, Googlebot, any other route) falls through unchanged.
    app.UseMiddleware<BotPreviewMiddleware>();

    app.UseDefaultFiles();
    app.UseStaticFiles();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/api/health");

    app.MapFallbackToFile("/index.html");

    app.Run();
}
catch (Exception ex)
{
    // NLog: catch setup errors
    logger.Error(ex, "Stopped program because of exception");
    throw;
}
finally
{
    // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
    LogManager.Shutdown();
}

