using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using JackyAIApp.Server.Services.Chatbot;
using SQL = JackyAIApp.Server.Data.Models.SQL;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// What the AI assistant is given from an uploaded file: its text, read here because the
    /// Messages API does not read Word or Excel files itself.
    /// </summary>
    public class AttachmentTextExtractorTests
    {
        [Theory]
        [InlineData("報告.docx", AttachmentKinds.Word)]
        [InlineData("持股.XLSX", AttachmentKinds.Excel)]
        [InlineData("交易.csv", AttachmentKinds.Csv)]
        [InlineData("notes.txt", AttachmentKinds.Text)]
        [InlineData("README.md", AttachmentKinds.Text)]
        [InlineData("data.json", AttachmentKinds.Text)]
        [InlineData("截圖.PNG", AttachmentKinds.Image)]
        [InlineData("photo.jpeg", AttachmentKinds.Image)]
        [InlineData("anim.gif", AttachmentKinds.Image)]
        [InlineData("pic.webp", AttachmentKinds.Image)]
        [InlineData("年報.pdf", AttachmentKinds.Pdf)]
        public void AcceptedFiles_AreKnownByTheirExtension(string fileName, string kind) =>
            Assert.Equal(kind, AttachmentTextExtractor.KindOf(fileName));

        [Theory]
        [InlineData("old.doc")]
        [InlineData("old.xls")]
        [InlineData("noextension")]
        public void OtherFiles_AreNotAccepted(string fileName) =>
            Assert.Null(AttachmentTextExtractor.KindOf(fileName));

        [Fact]
        public void AWordFile_IsReadAsItsParagraphsAndTableRows()
        {
            using var file = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(file, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new W.Document(new W.Body(
                    Paragraph("台積電法說會重點"),
                    new W.Table(
                        Row("項目", "數值"),
                        Row("毛利率", "59%")),
                    Paragraph("結論")));
            }
            file.Position = 0;

            var result = AttachmentTextExtractor.Extract(AttachmentKinds.Word, file);

            Assert.Equal("台積電法說會重點\n項目 | 數值\n毛利率 | 59%\n\n結論", Normalize(result.Text));
            Assert.False(result.Truncated);
        }

        [Fact]
        public void AnExcelFile_IsReadAsOneCsvBlockPerSheet_WithSkippedCellsKeptInPlace()
        {
            using var file = new MemoryStream();
            using (var doc = SpreadsheetDocument.Create(file, SpreadsheetDocumentType.Workbook))
            {
                var workbook = doc.AddWorkbookPart();
                workbook.Workbook = new Workbook(new Sheets());
                var strings = workbook.AddNewPart<SharedStringTablePart>();
                strings.SharedStringTable = new SharedStringTable(
                    new SharedStringItem(new Text("代號")),
                    new SharedStringItem(new Text("名稱, 備註")));

                var sheet = workbook.AddNewPart<WorksheetPart>();
                sheet.Worksheet = new Worksheet(new SheetData(
                    new Row(SharedCell("A1", 0), SharedCell("B1", 1)),
                    // B2 is missing from the file: an empty cell.
                    new Row(NumberCell("A2", "2330"), NumberCell("C2", "1050.5"))));
                workbook.Workbook.Sheets!.Append(new Sheet
                {
                    Id = workbook.GetIdOfPart(sheet),
                    SheetId = 1,
                    Name = "持股",
                });
            }
            file.Position = 0;

            var result = AttachmentTextExtractor.Extract(AttachmentKinds.Excel, file);

            Assert.Equal("## 工作表：持股\n代號,\"名稱, 備註\"\n2330,,1050.5", Normalize(result.Text));
        }

        [Fact]
        public void ACellSaidToBeFarPastTheLastColumn_IsLeftOut_RatherThanPaddedTo()
        {
            // "ZZZZZZ1" would be column 321 million: lining it up would take gigabytes of empty cells.
            using var file = Workbook(new Row(NumberCell("A1", "1"), NumberCell("ZZZZZZ1", "2"), NumberCell("XFE1", "3")));

            var result = AttachmentTextExtractor.Extract(AttachmentKinds.Excel, file);

            Assert.Equal("## 工作表：表\n1", Normalize(result.Text));
        }

        [Fact]
        public void TheLastColumn_IsStillKeptInPlace()
        {
            using var file = Workbook(new Row(NumberCell("A1", "1"), NumberCell("XFD1", "2")));

            var result = AttachmentTextExtractor.Extract(AttachmentKinds.Excel, file);

            var row = Normalize(result.Text).Split('\n')[1];
            Assert.Equal(AttachmentTextExtractor.MaxColumns, row.Split(',').Length);
            Assert.EndsWith(",2", row);
        }

        [Fact]
        public void APartLargerThanTheLimitOnceUnzipped_IsUnreadable()
        {
            // A few hundred KB zipped, far more unzipped: the SDK would read the whole part into memory.
            var huge = new string('a', (int)AttachmentTextExtractor.MaxCharactersInPart + 1);
            using var file = Workbook(new Row(new Cell
            {
                CellReference = "A1",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(huge)),
            }));

            Assert.Throws<UnreadableAttachmentException>(() => AttachmentTextExtractor.Extract(AttachmentKinds.Excel, file));
        }

        [Fact]
        public void ALongSheet_StopsBeingReadPastTheLimit_AndIsMarkedAsCut()
        {
            var rows = Enumerable.Range(1, 20_000).Select(i => new Row(NumberCell($"A{i}", "1234567890"))).ToArray();
            using var file = Workbook(rows);

            var result = AttachmentTextExtractor.Extract(AttachmentKinds.Excel, file);

            Assert.Equal(AttachmentTextExtractor.MaxChars, result.Text.Length);
            Assert.True(result.Truncated);
        }

        [Fact]
        public void AUtf8Csv_IsReadWithItsBomDropped()
        {
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("代號,名稱\n2330,台積電")).ToArray();

            var result = AttachmentTextExtractor.Extract(AttachmentKinds.Csv, new MemoryStream(bytes));

            Assert.Equal("代號,名稱\n2330,台積電", result.Text);
        }

        [Fact]
        public void ABig5Csv_AsExcelInTaiwanSavesIt_IsReadCorrectly()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var bytes = Encoding.GetEncoding(950).GetBytes("代號,名稱\n2330,台積電");

            var result = AttachmentTextExtractor.Extract(AttachmentKinds.Csv, new MemoryStream(bytes));

            Assert.Equal("代號,名稱\n2330,台積電", result.Text);
        }

        [Fact]
        public void TextPastTheLimit_IsCutAndMarkedAsCut()
        {
            var bytes = Encoding.UTF8.GetBytes(new string('a', AttachmentTextExtractor.MaxChars + 10));

            var result = AttachmentTextExtractor.Extract(AttachmentKinds.Text, new MemoryStream(bytes));

            Assert.Equal(AttachmentTextExtractor.MaxChars, result.Text.Length);
            Assert.True(result.Truncated);
        }

        [Theory]
        [InlineData(AttachmentKinds.Word)]
        [InlineData(AttachmentKinds.Excel)]
        public void AFileThatIsNotWhatItsNameSays_IsUnreadable(string kind)
        {
            var notAZip = new MemoryStream(Encoding.UTF8.GetBytes("just text renamed to .docx"));

            Assert.Throws<UnreadableAttachmentException>(() => AttachmentTextExtractor.Extract(kind, notAZip));
        }

        [Fact]
        public void AMessageWithoutAttachments_IsSentAsPlainText()
        {
            var json = Wire(new SQL.ChatMessage { ConversationId = "c", Role = "user", Content = "你好" });

            Assert.Equal("你好", json.GetProperty("content").GetString());
        }

        [Fact]
        public void AMessageWithAttachments_SendsEachAsATitledDocument_BeforeTheQuestion()
        {
            var message = new SQL.ChatMessage { ConversationId = "c", Role = "user", Content = "幫我總結" };
            message.Attachments.Add(new SQL.ChatAttachment { Id = 2, UserId = "u", FileName = "b.csv", Kind = "csv", Text = "B", Truncated = true });
            message.Attachments.Add(new SQL.ChatAttachment { Id = 1, UserId = "u", FileName = "a.docx", Kind = "word", Text = "A" });

            var blocks = Wire(message).GetProperty("content").EnumerateArray().ToList();

            Assert.Equal(3, blocks.Count);
            Assert.Equal("document", blocks[0].GetProperty("type").GetString());
            Assert.Equal("a.docx", blocks[0].GetProperty("title").GetString());
            Assert.Equal("text", blocks[0].GetProperty("source").GetProperty("type").GetString());
            Assert.Equal("text/plain", blocks[0].GetProperty("source").GetProperty("media_type").GetString());
            Assert.Equal("A", blocks[0].GetProperty("source").GetProperty("data").GetString());
            // A cut file says so, so the model does not answer as if it saw all of it.
            Assert.Equal("B" + AnthropicChatService.TruncationNote, blocks[1].GetProperty("source").GetProperty("data").GetString());
            Assert.Equal("text", blocks[2].GetProperty("type").GetString());
            Assert.Equal("幫我總結", blocks[2].GetProperty("text").GetString());
        }

        private static JsonElement Wire(SQL.ChatMessage message) =>
            JsonSerializer.SerializeToElement(AnthropicChatService.ToMessageParam(message, new Dictionary<long, byte[]>()));

        private static string Normalize(string text) => text.Replace("\r\n", "\n");

        private static W.Paragraph Paragraph(string text) => new(new W.Run(new W.Text(text)));

        private static W.TableRow Row(params string[] cells) =>
            new(cells.Select(c => new W.TableCell(Paragraph(c))));

        private static MemoryStream Workbook(params Row[] rows)
        {
            var file = new MemoryStream();
            using (var doc = SpreadsheetDocument.Create(file, SpreadsheetDocumentType.Workbook))
            {
                var workbook = doc.AddWorkbookPart();
                workbook.Workbook = new Workbook(new Sheets());
                var sheet = workbook.AddNewPart<WorksheetPart>();
                sheet.Worksheet = new Worksheet(new SheetData(rows));
                workbook.Workbook.Sheets!.Append(new Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "表" });
            }
            file.Position = 0;
            return file;
        }

        private static Cell SharedCell(string reference, int index) => new()
        {
            CellReference = reference,
            DataType = CellValues.SharedString,
            CellValue = new CellValue(index.ToString()),
        };

        private static Cell NumberCell(string reference, string value) => new()
        {
            CellReference = reference,
            CellValue = new CellValue(value),
        };
    }
}
