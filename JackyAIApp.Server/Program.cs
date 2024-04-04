using Azure.Identity;
using JackyAIApp.Server.Configuration;
using NLog;
using NLog.Web;

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
    // Add services to the container.
    if (builder.Environment.IsProduction())
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
            new ManagedIdentityCredential());
    }

    //builder.Services.AddAuthentication(options =>
    //{
    //    options.DefaultScheme = "Cookies";
    //    options.DefaultChallengeScheme = "Google";
    //})
    //.AddCookie()
    //.AddGoogle(googleOptions =>
    //{
    //    googleOptions.ClientId = builder.Configuration["Google:ClientId"]?.ToString() ?? "";
    //    googleOptions.ClientSecret = builder.Configuration["Google:ClientSecret"]?.ToString() ?? "";
    //});

    builder.Services.AddControllers();
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddOptions().Configure<Settings>(builder.Configuration.GetSection("Settings"));
    var app = builder.Build();

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

