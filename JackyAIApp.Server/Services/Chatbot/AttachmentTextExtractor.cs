using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>What kind of file an attachment was, which decides how its text was read.</summary>
    public static class AttachmentKinds
    {
        public const string Word = "word";
        public const string Excel = "excel";
        public const string Csv = "csv";
        public const string Text = "text";
        public const string Image = "image";
        public const string Pdf = "pdf";

        /// <summary>Kept as a file and sent to the model as it is (<see cref="AttachmentFileProcessor"/>),
        /// rather than as text read out of it.</summary>
        public static bool IsFile(string kind) => kind is Image or Pdf;
    }

    /// <param name="Kind">An <see cref="AttachmentKinds"/> value.</param>
    /// <param name="Text">The file's text, cut to <see cref="AttachmentTextExtractor.MaxChars"/>.</param>
    /// <param name="Truncated">The file had more text than was kept.</param>
    public sealed record ExtractedAttachment(string Kind, string Text, bool Truncated);

    /// <summary>The file cannot be read as the kind its name says it is.</summary>
    public sealed class UnreadableAttachmentException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>
    /// Turns an uploaded file into the plain text the AI assistant is given. The Messages API reads
    /// PDFs, images and plain text as documents, but not Word or Excel files, so those are read
    /// here: a Word file as its paragraphs and tables, an Excel file as one CSV block per sheet.
    /// Layout, charts and formulas are lost — only the text and the cell values remain.
    /// </summary>
    public static class AttachmentTextExtractor
    {
        /// <summary>Largest upload accepted.</summary>
        public const long MaxBytes = 10 * 1024 * 1024;

        /// <summary>Text kept per attachment. The whole conversation, attachments included, is sent
        /// again with every message, so one large file is paid for on every turn after it.</summary>
        public const int MaxChars = 100_000;

        private static readonly Dictionary<string, string> KindByExtension = new(StringComparer.OrdinalIgnoreCase)
        {
            [".docx"] = AttachmentKinds.Word,
            [".xlsx"] = AttachmentKinds.Excel,
            [".csv"] = AttachmentKinds.Csv,
            [".txt"] = AttachmentKinds.Text,
            [".md"] = AttachmentKinds.Text,
            [".json"] = AttachmentKinds.Text,
            [".png"] = AttachmentKinds.Image,
            [".jpg"] = AttachmentKinds.Image,
            [".jpeg"] = AttachmentKinds.Image,
            [".gif"] = AttachmentKinds.Image,
            [".webp"] = AttachmentKinds.Image,
            [".pdf"] = AttachmentKinds.Pdf,
        };

        /// <summary>The file types accepted, for the client's file picker and error messages.</summary>
        public static IReadOnlyCollection<string> AcceptedExtensions => KindByExtension.Keys;

        /// <summary>The kind for a file name, or null when the type is not accepted.</summary>
        public static string? KindOf(string fileName) =>
            KindByExtension.TryGetValue(Path.GetExtension(fileName), out var kind) ? kind : null;

        /// <exception cref="UnreadableAttachmentException">The content is not what the name says.</exception>
        public static ExtractedAttachment Extract(string kind, Stream content)
        {
            string text;
            try
            {
                text = kind switch
                {
                    AttachmentKinds.Word => ReadWord(content),
                    AttachmentKinds.Excel => ReadExcel(content),
                    _ => ReadText(content),
                };
            }
            catch (UnreadableAttachmentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A renamed file, a password-protected one, a corrupt zip: all just "can't read it".
                throw new UnreadableAttachmentException($"Could not read the file as {kind}.", ex);
            }

            text = text.Trim();
            return text.Length > MaxChars
                ? new ExtractedAttachment(kind, text[..MaxChars], true)
                : new ExtractedAttachment(kind, text, false);
        }

        private static string ReadWord(Stream content)
        {
            using var doc = WordprocessingDocument.Open(content, false);
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new UnreadableAttachmentException("The document has no body.");

            var sb = new StringBuilder();
            foreach (var element in body.ChildElements)
            {
                switch (element)
                {
                    case Word.Paragraph p:
                        sb.AppendLine(p.InnerText);
                        break;
                    case Word.Table table:
                        // One line per row, cells separated so a table still reads as a table.
                        foreach (var row in table.Elements<Word.TableRow>())
                            sb.AppendLine(string.Join(" | ", row.Elements<Word.TableCell>().Select(c => c.InnerText.Trim())));
                        sb.AppendLine();
                        break;
                }
            }
            return sb.ToString();
        }

        private static string ReadExcel(Stream content)
        {
            using var doc = SpreadsheetDocument.Open(content, false);
            var workbookPart = doc.WorkbookPart ?? throw new UnreadableAttachmentException("The workbook has no data.");
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?
                .Elements<SharedStringItem>().Select(s => s.InnerText).ToArray() ?? [];

            var sb = new StringBuilder();
            foreach (var sheet in workbookPart.Workbook?.Sheets?.Elements<Sheet>() ?? [])
            {
                if (sheet.Id?.Value is not { } relId || workbookPart.GetPartById(relId) is not WorksheetPart part) continue;

                sb.AppendLine($"## 工作表：{sheet.Name?.Value}");
                foreach (var row in part.Worksheet?.Descendants<Row>() ?? [])
                {
                    var cells = new List<string>();
                    foreach (var cell in row.Elements<Cell>())
                    {
                        // Cells can be skipped in the file when empty; keep the columns lined up.
                        var column = ColumnIndex(cell.CellReference?.Value);
                        while (column > 0 && cells.Count < column) cells.Add("");
                        cells.Add(CsvField(CellText(cell, sharedStrings)));
                    }
                    sb.AppendLine(string.Join(",", cells));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string CellText(Cell cell, string[] sharedStrings)
        {
            var raw = cell.CellValue?.Text ?? cell.InlineString?.InnerText ?? "";
            if (cell.DataType?.Value == CellValues.SharedString &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                index >= 0 && index < sharedStrings.Length)
                return sharedStrings[index];
            if (cell.DataType?.Value == CellValues.Boolean) return raw == "1" ? "TRUE" : "FALSE";
            // Numbers and formula results are stored as their computed value; dates stay serial
            // numbers, which is a known limit of reading the file without its styles.
            return raw;
        }

        /// <summary>Zero-based column of a reference like "C12", or -1 when there is none.</summary>
        private static int ColumnIndex(string? reference)
        {
            if (string.IsNullOrEmpty(reference)) return -1;
            var column = 0;
            foreach (var ch in reference)
            {
                if (!char.IsLetter(ch)) break;
                column = column * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return column - 1;
        }

        private static string CsvField(string value) =>
            value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

        /// <summary>
        /// Text as UTF-8 (with or without a BOM). If it is not valid UTF-8 it is read as Big5, which
        /// is what Excel in Taiwan writes a CSV in by default.
        /// </summary>
        private static string ReadText(Stream content)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            var bytes = buffer.ToArray();
            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(950).GetString(bytes);
            }
        }
    }
}
