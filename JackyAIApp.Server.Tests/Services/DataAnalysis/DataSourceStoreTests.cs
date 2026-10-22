using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.DataAnalysis
{
    /// <summary>
    /// A user's saved data sources, now behind <see cref="DataSourceStore"/>: each user sees and
    /// changes only their own, and a config the validator refuses changes nothing.
    /// </summary>
    public class DataSourceStoreTests : IDisposable
    {
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly Mock<IDataSourceConfigService> _config = new();

        public DataSourceStoreTests()
        {
            _config.Setup(c => c.SerializeAndEncrypt(It.IsAny<object>(), It.IsAny<DataSourceConnectorType>())).Returns("encrypted");
            _config.Setup(c => c.MergeAndEncrypt(It.IsAny<object>(), It.IsAny<DataSourceConfig>(), It.IsAny<DataSourceConnectorType>())).Returns("merged");
            _config.Setup(c => c.ToResponseDto(It.IsAny<DataSourceConfig>()))
                .Returns((DataSourceConfig s) => new DataSourceConfigResponseDto
                {
                    Id = s.Id, Name = s.Name, ConnectorType = s.ConnectorType, Config = "***", DefaultTableName = s.DefaultTableName, CreatedAt = s.CreatedAt,
                });
        }

        public void Dispose() => _db.Dispose();

        private DataSourceStore As(string userId)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(userId);
            return new DataSourceStore(NullLogger<DataSourceStore>.Instance, _db, _config.Object, user.Object);
        }

        private static SaveDataSourceConfigDto Save(string name, string? table = null) => new()
        {
            Name = name, ConnectorType = DataSourceConnectorType.MsSql, Config = new { }, DefaultTableName = table,
        };

        [Fact]
        public async Task List_HasOnlyYourOwnSources_OldestFirst()
        {
            var first = (await As("alice").CreateAsync(Save("A1"), default)).Data!;
            await As("bob").CreateAsync(Save("B1"), default);
            var second = (await As("alice").CreateAsync(Save("A2"), default)).Data!;

            var list = (await As("alice").ListAsync(default)).Data!;

            Assert.Equal([first.Id, second.Id], list.Select(s => s.Id));
            Assert.Equal("encrypted", _db.DataSourceConfigs.Single(s => s.Id == first.Id).ConfigJson);
        }

        [Fact]
        public async Task SomeoneElsesSource_IsNotFound_ToUpdateOrDelete()
        {
            var bobs = (await As("bob").CreateAsync(Save("B1"), default)).Data!;

            Assert.Equal(ErrorCodes.NotFound, (await As("alice").UpdateAsync(bobs.Id, Save("mine now"), default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await As("alice").DeleteAsync(bobs.Id, default)).Error);
            Assert.Equal("B1", _db.DataSourceConfigs.Single().Name);
        }

        [Fact]
        public async Task Update_MergesTheConfig_AndTakesTheOtherFields()
        {
            var created = (await As("alice").CreateAsync(Save("Old"), default)).Data!;

            var updated = await As("alice").UpdateAsync(created.Id, Save("New", table: "dbo.Sales"), default);

            Assert.Equal("New", updated.Data!.Name);
            var stored = _db.DataSourceConfigs.Single();
            Assert.Equal("merged", stored.ConfigJson);
            Assert.Equal("dbo.Sales", stored.DefaultTableName);
            Assert.NotNull(stored.UpdatedAt);
        }

        [Fact]
        public async Task ARefusedConfig_IsBadRequest_AndChangesNothing()
        {
            var created = (await As("alice").CreateAsync(Save("Old"), default)).Data!;
            _config.Setup(c => c.SerializeAndEncrypt(It.IsAny<object>(), It.IsAny<DataSourceConnectorType>()))
                .Throws(new DataSourceConfigException("Integrated Security is not allowed."));
            _config.Setup(c => c.MergeAndEncrypt(It.IsAny<object>(), It.IsAny<DataSourceConfig>(), It.IsAny<DataSourceConnectorType>()))
                .Throws(new DataSourceConfigException("Integrated Security is not allowed."));

            var create = await As("alice").CreateAsync(Save("Another"), default);
            var update = await As("alice").UpdateAsync(created.Id, Save("Renamed"), default);

            Assert.Equal(ErrorCodes.BadRequest, create.Error);
            Assert.Equal(ErrorCodes.BadRequest, update.Error);
            Assert.Equal("Integrated Security is not allowed.", update.ErrorMessage);
            var only = Assert.Single(_db.DataSourceConfigs);
            Assert.Equal("Old", only.Name);
        }

        [Fact]
        public async Task Delete_RemovesYourOwnSource()
        {
            var created = (await As("alice").CreateAsync(Save("A1"), default)).Data!;

            Assert.True((await As("alice").DeleteAsync(created.Id, default)).IsSuccess);
            Assert.Empty(_db.DataSourceConfigs);
        }
    }
}
