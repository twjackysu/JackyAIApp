using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// The signed-in user's saved data-source connectors. Each user sees and changes only their
    /// own; someone else's answers NotFound. Configs are stored encrypted, and handed back with
    /// their secrets masked (<see cref="IDataSourceConfigService"/>).
    /// </summary>
    public interface IDataSourceStore
    {
        Task<ServiceResult<List<DataSourceConfigResponseDto>>> ListAsync(CancellationToken ct);
        Task<ServiceResult<DataSourceConfigResponseDto>> CreateAsync(SaveDataSourceConfigDto request, CancellationToken ct);
        Task<ServiceResult<DataSourceConfigResponseDto>> UpdateAsync(Guid id, SaveDataSourceConfigDto request, CancellationToken ct);
        Task<ServiceResult<NoData>> DeleteAsync(Guid id, CancellationToken ct);
    }

    public class DataSourceStore(
        ILogger<DataSourceStore> logger,
        AzureSQLDBContext dbContext,
        IDataSourceConfigService configService,
        ICurrentUser currentUser) : IDataSourceStore
    {
        private static ServiceFailure SourceNotFound => ServiceResult.Fail(ErrorCodes.NotFound, "Source not found.");

        private Task<DataSourceConfig?> FindOwnAsync(Guid id, string userId, CancellationToken ct) =>
            dbContext.DataSourceConfigs.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);

        public async Task<ServiceResult<List<DataSourceConfigResponseDto>>> ListAsync(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var sources = await dbContext.DataSourceConfigs
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(ct);
            return ServiceResult.Ok(sources.Select(configService.ToResponseDto).ToList());
        }

        public async Task<ServiceResult<DataSourceConfigResponseDto>> CreateAsync(SaveDataSourceConfigDto request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            string configJson;
            try
            {
                configJson = configService.SerializeAndEncrypt(request.Config, request.ConnectorType);
            }
            catch (DataSourceConfigException ex)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, ex.Message);
            }

            var entity = new DataSourceConfig
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = request.Name,
                ConnectorType = request.ConnectorType,
                ConfigJson = configJson,
                DefaultTableName = request.DefaultTableName,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            dbContext.DataSourceConfigs.Add(entity);
            await dbContext.SaveChangesAsync(ct);
            logger.LogInformation("Created DataSourceConfig {id} ({name}) for user {uid}", entity.Id, entity.Name, userId);
            return ServiceResult.Ok(configService.ToResponseDto(entity));
        }

        public async Task<ServiceResult<DataSourceConfigResponseDto>> UpdateAsync(Guid id, SaveDataSourceConfigDto request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var entity = await FindOwnAsync(id, userId, ct);
            if (entity is null)
                return SourceNotFound;

            // Merge: non-secret fields always update; secret fields only update when the client
            // sent a real value (not the masked placeholder "***"). The merged result is validated
            // before anything on the entity changes.
            string configJson;
            try
            {
                configJson = configService.MergeAndEncrypt(request.Config, entity, request.ConnectorType);
            }
            catch (DataSourceConfigException ex)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, ex.Message);
            }

            entity.Name = request.Name;
            entity.ConnectorType = request.ConnectorType;
            entity.DefaultTableName = request.DefaultTableName;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.ConfigJson = configJson;

            await dbContext.SaveChangesAsync(ct);
            return ServiceResult.Ok(configService.ToResponseDto(entity));
        }

        public async Task<ServiceResult<NoData>> DeleteAsync(Guid id, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var entity = await FindOwnAsync(id, userId, ct);
            if (entity is null)
                return SourceNotFound;

            dbContext.DataSourceConfigs.Remove(entity);
            await dbContext.SaveChangesAsync(ct);
            return ServiceResult.Ok();
        }
    }
}
