using System.Text.Json;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Assistant.Memory;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests.Services.Assistant.Memory
{
    /// <summary>
    /// Studio AI's <c>remember</c> and <c>forget</c> (docs/ai-native/design/08): each asks first and its card says the
    /// note in full; <c>forget</c> names a note by its words, and can never run on its own.
    /// </summary>
    public class MemoryToolsTests : IDisposable
    {
        private static readonly AiToolContext Reader = new("u1", "c1");

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        public void Dispose() => _db.Dispose();

        private AssistantMemoryService Memories() => new(_db, new TestClock(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)));

        private static JsonElement Input(object value) => JsonSerializer.SerializeToElement(value);

        private static string CardText(object? card) => JsonSerializer.Serialize(card);

        [Fact]
        public void Remember_AsksFirst_AndForget_CanNeverRunOnItsOwn()
        {
            Assert.Equal(ToolEffect.Write, RememberTool.Definition.Effect);
            Assert.Equal(ToolEffect.Destructive, ForgetTool.Definition.Effect);
        }

        [Fact]
        public void TheConfirmationCard_SaysTheNoteInFull_AsItWouldBeKept()
        {
            var remember = new RememberTool(Memories());
            var forget = new ForgetTool(Memories());

            Assert.Equal("記住：「在準備多益， 目標 800 分」", remember.Describe(Input(new { text = " 在準備多益，\n目標 800 分 " }), Reader));
            Assert.Equal("忘掉：「在準備多益」", forget.Describe(Input(new { text = "在準備多益" }), Reader));
            Assert.Null(remember.Describe(Input(new { text = "  " }), Reader));
            Assert.Null(remember.Describe(Input(new { note = "x" }), Reader));
        }

        [Fact]
        public async Task Remember_KeepsTheNote_ForTheConversationsReader_AndShowsWhereToManageIt()
        {
            var result = await new RememberTool(Memories()).RunAsync(Input(new { text = "短文用商務情境" }), Reader, default);

            Assert.False(result.IsError);
            Assert.Equal("已記住：「短文用商務情境」（共 1 則）。", result.Content);
            Assert.Contains("\"href\":\"/connectors\"", CardText(result.Card));
            Assert.Equal("u1", Assert.Single(_db.AssistantMemories).UserId);

            var again = await new RememberTool(Memories()).RunAsync(Input(new { text = "短文用商務情境" }), Reader, default);
            Assert.False(again.IsError);
            Assert.Contains("本來就記得", again.Content);
            Assert.Single(_db.AssistantMemories);
        }

        [Fact]
        public async Task Remember_RefusesWhatCannotBeKept()
        {
            var tool = new RememberTool(Memories());

            Assert.True((await tool.RunAsync(Input(new { text = "" }), Reader, default)).IsError);
            Assert.True((await tool.RunAsync(Input(new { text = new string('長', AssistantMemoryService.MaxLength + 1) }), Reader, default)).IsError);
            for (var i = 0; i < AssistantMemoryService.Limit; i++)
                await Memories().AddAsync("u1", $"note {i}", default);
            var full = await tool.RunAsync(Input(new { text = "one more" }), Reader, default);
            Assert.True(full.IsError);
            Assert.Contains("滿了", full.Content);
            Assert.Equal(AssistantMemoryService.Limit, _db.AssistantMemories.Count());
        }

        [Fact]
        public async Task Forget_RemovesTheNoteWithThoseWords_AndOnlyTheReaders()
        {
            await Memories().AddAsync("u1", "在準備多益，目標 800 分", default);
            await Memories().AddAsync("u2", "在準備多益，目標 800 分", default);
            var tool = new ForgetTool(Memories());

            var missing = await tool.RunAsync(Input(new { text = "在準備多益" }), Reader, default);
            var forgotten = await tool.RunAsync(Input(new { text = "在準備多益，目標 800 分" }), Reader, default);

            Assert.True(missing.IsError);
            Assert.False(forgotten.IsError);
            Assert.Equal("已忘掉：「在準備多益，目標 800 分」。", forgotten.Content);
            Assert.Equal("u2", Assert.Single(_db.AssistantMemories).UserId);
        }
    }
}
