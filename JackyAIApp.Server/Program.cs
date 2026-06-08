using Azure.Identity;
using Betalgo.Ranul.OpenAI.Extensions;
using DataAnalysisLibrary.Connectors.MsSql;
using DataAnalysisLibrary.Core.Engine;
using DotnetSdkUtilities.Extensions.ServiceCollectionExtensions;
using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Job;
using JackyAIApp.Server.Services.Job.Providers;
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
    // Add services to the container.
    if (builder.Environment.IsProduction())
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
            new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
    }

    var configuration = builder.Configuration;

    // Configure SQL Database Context
    var sqlConnectionString = configuration.GetConnectionString("SQLConnection") ?? "";
    builder.Services.AddDbContext<AzureSQLDBContext>(
        options => options.UseSqlServer(sqlConnectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        }));

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
    builder.Services.AddHttpClient();
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

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IApiResponseFactory, ApiResponseFactory>();
    builder.Services.AddScoped<IMyResponseFactory, ResponseFactory>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<ICreditService, CreditService>();
    // Spell-checker dictionary is expensive to load from disk; share one instance.
    builder.Services.AddSingleton<JackyAIApp.Server.Services.SpellChecker.ISpellChecker,
        JackyAIApp.Server.Services.SpellChecker.HunspellSpellChecker>();
    builder.Services.AddScoped<JackyAIApp.Server.Services.Dictionary.IDictionaryService,
        JackyAIApp.Server.Services.Dictionary.DictionaryService>();
    builder.Services.AddScoped<ITWSEOpenAPIService, TWSEOpenAPIService>();
    builder.Services.AddScoped<ITWSEDataService, TWSEDataService>();
    builder.Services.AddScoped<IFinanceAnalysisService, FinanceAnalysisService>();

    // Register finance indicator engine, TWStockLib, and data providers
    builder.Services.AddFinanceAnalysisServices();

    // Configure Job Aggregator services
    builder.Services.AddHttpClient("104", client =>
    {
        client.DefaultRequestHeaders.Add("Referer", "https://www.104.com.tw/");
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        client.Timeout = TimeSpan.FromSeconds(15);
    });
    builder.Services.AddHttpClient("Cake", client =>
    {
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml");
        client.DefaultRequestHeaders.Add("Accept-Language", "zh-TW,zh;q=0.9,en;q=0.8");
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.Timeout = TimeSpan.FromSeconds(15);
    });
    builder.Services.AddSingleton<IJobProvider, Job104Provider>();
    builder.Services.AddSingleton<IJobProvider, CakeJobsProvider>();
    builder.Services.AddSingleton<IJobAggregatorService, JobAggregatorService>();

    // Configure Prompt and OpenAI services
    builder.Services.AddSingleton<JackyAIApp.Server.Services.Prompt.IPromptLoader, JackyAIApp.Server.Services.Prompt.PromptLoader>();
    builder.Services.AddScoped<JackyAIApp.Server.Services.OpenAI.IOpenAIPromptService, JackyAIApp.Server.Services.OpenAI.OpenAIPromptService>();

    // Configure Connectors services
    builder.Services.Configure<ConnectorOptions>(builder.Configuration.GetSection(ConnectorOptions.SectionName));
    builder.Services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
    builder.Services.AddScoped<IStateService, StateService>();
    builder.Services.AddScoped<IConnectorService, ConnectorService>();
    builder.Services.Configure<JackyAIApp.Server.Configuration.StripeOptions>(
        builder.Configuration.GetSection(JackyAIApp.Server.Configuration.StripeOptions.SectionName));
    builder.Services.AddScoped<IStripeService, StripeService>();
    builder.Services.Configure<JackyAIApp.Server.Configuration.PayPalOptions>(
        builder.Configuration.GetSection(JackyAIApp.Server.Configuration.PayPalOptions.SectionName));
    builder.Services.AddHttpClient<IPayPalService, PayPalService>();
    builder.Services.Configure<JackyAIApp.Server.Configuration.ECPayOptions>(
        builder.Configuration.GetSection(JackyAIApp.Server.Configuration.ECPayOptions.SectionName));
    builder.Services.AddScoped<IECPayService, ECPayService>();

    var openAIKey = configuration.GetValue<string>("Settings:OpenAI:Key") ?? "";
    builder.Services.AddOpenAIService(options =>
    {
        options.ApiKey = openAIKey;
        options.UseBeta = true;
    });

    builder.Services.AddOptions().Configure<Settings>(builder.Configuration.GetSection("Settings"));

    // Register DataAnalysisEngine (singleton — engine itself is stateless; connections are per-query)
    builder.Services.AddSingleton(_ =>
        new DataAnalysisEngineBuilder()
            .AddSource(new MsSqlDataSourceFactory(new MsSqlConfig
            {
                SourceKey = "mssql",
                ConnectionString = sqlConnectionString
            }))
            .Build());

    builder.Services.AddHealthChecks();
    var app = builder.Build();

    app.UseResponseCompression();
    app.UseMiddleware<ExceptionMiddleware>();
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

