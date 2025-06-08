using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MigrationController : ControllerBase
    {
        private readonly DataMigrationService _migrationService;
        private readonly ILogger<MigrationController> _logger;
        private IMyResponseFactory _responseFactory;

        public MigrationController(
            DataMigrationService migrationService,
            ILogger<MigrationController> logger,
            IMyResponseFactory responseFactory)
        {
            _migrationService = migrationService;
            _logger = logger;
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        /// <summary>
        /// Initiates the migration of data from Cosmos DB to SQL DB
        /// </summary>
        /// <returns>Status of the migration operation</returns>
        [HttpGet("migrate-to-sql")]
        public async Task<IActionResult> MigrateToSql()
        {
            try
            {
                _logger.LogInformation("Starting migration from Cosmos DB to SQL DB");
                await _migrationService.MigrateAllDataAsync();
                _logger.LogInformation("Migration completed successfully");
                
                return _responseFactory.CreateOKResponse("Migration completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during migration");
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Migration failed: " + ex.Message);
            }
        }
    }
}