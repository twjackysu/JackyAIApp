﻿using Azure.Identity;
using Betalgo.Ranul.OpenAI.Extensions;
using DotnetSdkUtilities.Extensions.ServiceCollectionExtensions;
using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Jira;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
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
            new ManagedIdentityCredential());
    }

    var configuration = builder.Configuration;
    
    // Add Cosmos DB context
    var cosmosConnectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    var cosmosDatabaseName = configuration.GetValue<string>("Settings:AzureCosmosDatabaseName") ?? "";
    builder.Services.AddDbContext<AzureCosmosDBContext>(
        options => options.UseCosmos(cosmosConnectionString, cosmosDatabaseName));
        
    // Register the migration service
    builder.Services.AddScoped<DataMigrationService>();

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
        options.Cookie.MaxAge = TimeSpan.FromDays(1);
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
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

    builder.Services.AddScoped<IApiResponseFactory, ApiResponseFactory>();
    builder.Services.AddScoped<IMyResponseFactory, ResponseFactory>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IJiraRestApiService, JiraRestApiService>();
    builder.Services.AddScoped<ITWSEOpenAPIService, TWSEOpenAPIService>();
    builder.Services.AddScoped<ITWSEDataService, TWSEDataService>();
    builder.Services.AddScoped<IFinanceAnalysisService, FinanceAnalysisService>();
    var openAIKey = configuration.GetValue<string>("Settings:OpenAI:Key") ?? "";
    builder.Services.AddOpenAIService(options =>
    {
        options.ApiKey = openAIKey;
        options.UseBeta = true;
    });

    builder.Services.AddOptions().Configure<Settings>(builder.Configuration.GetSection("Settings"));
    var app = builder.Build();

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

