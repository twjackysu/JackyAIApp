using JackyAIApp.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace JackyAIApp.Server.Tests.Controllers
{
    public class ConfigControllerTests
    {
        private static string? ConnectionStringOf(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            var prop = ok.Value!.GetType().GetProperty("connectionString")!;
            return (string?)prop.GetValue(ok.Value);
        }

        [Fact]
        public void GetTelemetry_ReturnsConfiguredConnectionString()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=abc" })
                .Build();

            Assert.Equal("InstrumentationKey=abc", ConnectionStringOf(new ConfigController(config).GetTelemetry()));
        }

        [Fact]
        public void GetTelemetry_ReturnsNullWhenEmpty()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ApplicationInsights:ConnectionString"] = "" })
                .Build();
            var saved = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
            Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", null);
            try
            {
                Assert.Null(ConnectionStringOf(new ConfigController(config).GetTelemetry()));
            }
            finally
            {
                Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", saved);
            }
        }
    }
}
