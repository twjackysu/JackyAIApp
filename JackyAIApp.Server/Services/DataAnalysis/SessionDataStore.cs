using System.Globalization;
using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Registered as a singleton: its <see cref="SessionUploadBudget"/> must count every session in the process.
    /// </summary>
    public class SessionDataStore(
        IMemoryCache cache,
        IOptions<SessionUploadOptions>? options = null,
        SessionUploadBudget? budget = null) : ISessionDataStore
    {
        private readonly IMemoryCache _cache = cache;
        private readonly SessionUploadOptions _limits = (options ?? Options.Create(new SessionUploadOptions())).Value;
        private readonly SessionUploadBudget _budget =
            budget ?? new SessionUploadBudget(options ?? Options.Create(new SessionUploadOptions()));

        internal const string SessionCachePrefix = "da_session_";
        private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(2);

        /// <summary>
        /// A row's own objects: the Dictionary (≈ 80 bytes), its bucket and entry arrays' headers, and its
        /// slot in the row list.
        /// </summary>
        internal const int PerRowOverheadBytes = 136;

        /// <summary>
        /// A cell's objects besides its characters: the key and value strings' headers (≈ 24 bytes each;
        /// the JSON reader allocates a fresh key string for every row) and the dictionary entry + bucket (≈ 28).
        /// </summary>
        internal const int PerCellOverheadBytes = 76;

        public ServiceResult Upload(SessionUploadDto dto)
        {
            if (dto.Rows is null || dto.Rows.Count == 0)
                return ServiceResult.Fail(ErrorCodes.BadRequest, "沒有可上傳的資料列。");

            if (dto.Rows.Count > _limits.MaxRows)
                return ServiceResult.Fail(ErrorCodes.BadRequest, $"每次最多上傳 {_limits.MaxRows:N0} 列。");

            var (bytes, shapeError) = Measure(dto.Rows, _limits);
            if (shapeError is not null)
                return ServiceResult.Fail(ErrorCodes.BadRequest, shapeError);
            if (bytes > _limits.MaxSessionBytes)
                return ServiceResult.Fail(ErrorCodes.PayloadTooLarge,
                    $"資料量太大（單次上傳上限約 {_limits.MaxSessionBytes >> 20} MB 記憶體），請減少列數或欄數後再試。");

            // Nothing is stored — no session id, cache untouched — unless the whole session fits the budget.
            if (!_budget.TryReserve(bytes))
                return ServiceResult.Fail(ErrorCodes.TooManyRequests, SessionUploadOptions.BusyMessage);

            var schema = InferSchema(dto.Rows);
            var expires = DateTimeOffset.UtcNow.Add(SessionTtl);
            var id = Guid.NewGuid().ToString("N");
            var name = dto.Name?.Trim() is { Length: > 0 } n ? n : "Uploaded Data";

            var entryOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = SessionTtl }
                .RegisterPostEvictionCallback((_, _, _, _) => _budget.Release(bytes));
            try
            {
                _cache.Set(SessionCachePrefix + id, new SessionData
                {
                    Name = name,
                    Rows = dto.Rows,
                    Schema = schema,
                    ExpiresAt = expires,
                }, entryOptions);
            }
            catch
            {
                _budget.Release(bytes);
                throw;
            }

            return ServiceResult.Ok(new SessionUploadResponseDto
            {
                SessionId = id,
                Name = name,
                RowCount = dto.Rows.Count,
                ExpiresAt = expires,
                Schema = schema,
            });
        }

        public ServiceResult GetSchema(string sessionId)
        {
            if (!TryGetSession(sessionId, out var session))
                return ServiceResult.Fail(ErrorCodes.NotFound, "Session not found or expired.");

            return ServiceResult.Ok(new { fields = session!.Schema });
        }

        public ServiceResult GetMaxDate(string sessionId, string dateField)
        {
            if (!TryGetSession(sessionId, out var session))
                return ServiceResult.Fail(ErrorCodes.NotFound, "Session not found or expired.");

            var values = session!.Rows
                .Select(r => r.GetValueOrDefault(dateField))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => TryParseDate(v!) ? DateTimeOffset.Parse(v!, CultureInfo.InvariantCulture) : (DateTimeOffset?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            var maxDate = values.Count > 0 ? values.Max().ToString("o") : null;
            return ServiceResult.Ok(new { maxDate });
        }

        public ServiceResult GetDistinctValues(string sessionId, string field, int limit = 200)
        {
            if (!TryGetSession(sessionId, out var session))
                return ServiceResult.Fail(ErrorCodes.NotFound, "Session not found or expired.");

            var values = session!.Rows
                .Select(r => r.GetValueOrDefault(field))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .Take(limit)
                .ToList();

            return ServiceResult.Ok(new { values });
        }

        public ServiceResult Summarize(string sessionId, SummarizeRequestDto dto)
        {
            if (!TryGetSession(sessionId, out var session))
                return ServiceResult.Fail(ErrorCodes.NotFound, "Session not found or expired.");

            var schemaLookup = session!.Schema.ToDictionary(s => s.Name, s => s.CanonicalType, StringComparer.OrdinalIgnoreCase);

            var filters  = DataAnalysisMapping.ExpandDateOnlyRangeEnds(dto.Filters);
            var filtered = session.Rows.Where(row => filters.All(f => MatchesFilter(row, f))).ToList();

            IEnumerable<IGrouping<string, Dictionary<string, string>>> groups;
            if (dto.ViewBy.Count == 0)
            {
                // No group-by: treat all rows as a single group
                groups = filtered.GroupBy(_ => "");
            }
            else
            {
                groups = filtered.GroupBy(row =>
                    string.Join("\0", dto.ViewBy.Select(vb => GetGroupKey(row, vb, schemaLookup))));
            }

            var rows = groups.Select(g =>
            {
                var dict = new Dictionary<string, object?>();

                // Group key fields
                var keyParts = g.Key.Split('\0');
                for (var i = 0; i < dto.ViewBy.Count; i++)
                {
                    var vb = dto.ViewBy[i];
                    var alias = NullIfEmpty(vb.Alias) ?? vb.FieldName;
                    dict[alias] = i < keyParts.Length ? keyParts[i] : null;
                }

                // Aggregations
                foreach (var measure in dto.Measures)
                {
                    var alias = NullIfEmpty(measure.Alias) ?? measure.FieldName;
                    var canonType = schemaLookup.GetValueOrDefault(measure.FieldName, "String");
                    var cellValues = g.Select(row => row.GetValueOrDefault(measure.FieldName));
                    dict[alias] = Aggregate(cellValues, measure.Aggregation, canonType);
                }

                return dict;
            }).ToList();

            return ServiceResult.Ok(new { rows });
        }

        public ServiceResult Details(string sessionId, DetailsRequestDto dto)
        {
            if (!TryGetSession(sessionId, out var session))
                return ServiceResult.Fail(ErrorCodes.NotFound, "Session not found or expired.");

            var filtered = FilterAndSort(session!, dto);

            var totalCount = filtered.Count;
            var take = Math.Min(dto.Take, 200);
            var page = filtered.Skip(dto.Skip).Take(take).ToList();

            var rows = page.Select(r => r.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)kvp.Value)).ToList();

            return ServiceResult.Ok(new { rows, totalCount });
        }

        public ServiceResult Export(string sessionId, DetailsRequestDto dto, int maxRows)
        {
            if (!TryGetSession(sessionId, out var session))
                return ServiceResult.Fail(ErrorCodes.NotFound, "Session not found or expired.");

            var filtered = FilterAndSort(session!, dto);

            return ServiceResult.Ok(new SessionExport(
                session!.Name,
                session.Schema.Select(f => f.Name).ToList(),
                filtered.Take(maxRows).ToList(),
                filtered.Count));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// The upload's estimated in-memory size — Σ rows (<see cref="PerRowOverheadBytes"/> + Σ cells
        /// (<see cref="PerCellOverheadBytes"/> + 2 bytes per character of name and value)) — or the reason
        /// its shape is refused (a missing row, too many columns, an over-long name or cell). Stops counting
        /// once past <see cref="SessionUploadOptions.MaxSessionBytes"/>.
        /// </summary>
        internal static (long Bytes, string? Error) Measure(List<Dictionary<string, string>> rows, SessionUploadOptions limits)
        {
            var columns = new HashSet<string>(StringComparer.Ordinal);
            long bytes = 0;
            foreach (var row in rows)
            {
                if (row is null) return (0, "資料列格式錯誤。");
                if (row.Count > limits.MaxColumns) return (0, $"每份資料最多 {limits.MaxColumns} 欄。");

                bytes += PerRowOverheadBytes;
                foreach (var (key, value) in row)
                {
                    var valueLength = value?.Length ?? 0;
                    if (key.Length > limits.MaxCellLength || valueLength > limits.MaxCellLength)
                        return (0, $"欄位名稱與每個儲存格最多 {limits.MaxCellLength:N0} 個字元。");
                    if (columns.Add(key) && columns.Count > limits.MaxColumns)
                        return (0, $"每份資料最多 {limits.MaxColumns} 欄。");
                    bytes += PerCellOverheadBytes + 2L * (key.Length + valueLength);
                }
                if (bytes > limits.MaxSessionBytes) return (bytes, null);
            }
            return (bytes, null);
        }

        /// <summary>The session's rows that pass the request's filters, in its sort order (first sort field only).</summary>
        private static List<Dictionary<string, string>> FilterAndSort(SessionData session, DetailsRequestDto dto)
        {
            var filters  = DataAnalysisMapping.ExpandDateOnlyRangeEnds(dto.Filters);
            var filtered = session.Rows
                .Where(row => filters.All(f => MatchesFilter(row, f)))
                .ToList();

            var schemaLookup = session.Schema.ToDictionary(s => s.Name, s => s.CanonicalType, StringComparer.OrdinalIgnoreCase);
            if (dto.OrderBy is { Count: > 0 })
            {
                var sort = dto.OrderBy[0];
                filtered = sort.Descending
                    ? filtered.OrderByDescending(r => SortKey(r, sort.FieldName, schemaLookup)).ToList()
                    : filtered.OrderBy(r => SortKey(r, sort.FieldName, schemaLookup)).ToList();
            }
            return filtered;
        }

        private bool TryGetSession(string sessionId, out SessionData? session)
        {
            session = null;
            if (string.IsNullOrWhiteSpace(sessionId)) return false;
            return _cache.TryGetValue(SessionCachePrefix + sessionId, out session) && session is not null;
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static List<SessionFieldDefDto> InferSchema(List<Dictionary<string, string>> rows)
        {
            if (rows.Count == 0) return [];
            var columns = rows[0].Keys.ToList();
            var sample = rows.Take(200).ToList();

            return columns.Select(col =>
            {
                var values = sample
                    .Select(r => r.GetValueOrDefault(col))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                var canonType = InferColumnType(values!);
                var dataType = canonType switch
                {
                    "DateTimeOffset" or "DateTime" => "datetime",
                    "Integer" => "bigint",
                    "Decimal" => "decimal",
                    "Boolean" => "bit",
                    _ => "nvarchar"
                };
                return new SessionFieldDefDto
                {
                    Name = col,
                    DataType = dataType,
                    IsNullable = true,
                    CanonicalType = canonType,
                };
            }).ToList();
        }

        private static string InferColumnType(List<string> values)
        {
            if (values.Count == 0) return "String";

            if (values.All(v => v.Length >= 6 && TryParseDate(v)))
                return "DateTimeOffset";

            if (values.All(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                return "Integer";

            if (values.All(v => decimal.TryParse(v.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out _)))
                return "Decimal";

            var bools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "true", "false", "yes", "no", "1", "0" };
            if (values.All(v => bools.Contains(v)))
                return "Boolean";

            return "String";
        }

        private static bool TryParseDate(string v) =>
            DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            DateTimeOffset.TryParse(v, CultureInfo.GetCultureInfo("zh-TW"), DateTimeStyles.None, out _);

        private static string GetGroupKey(
            Dictionary<string, string> row,
            GroupFieldDto field,
            Dictionary<string, string> schemaLookup)
        {
            var raw = row.GetValueOrDefault(field.FieldName);
            if (raw is null) return "(null)";

            if (field.Type == "DateTrunc")
            {
                var canonType = schemaLookup.GetValueOrDefault(field.FieldName, "String");
                if (canonType is "DateTimeOffset" or "DateTime" && TryParseDate(raw) &&
                    DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    return TruncateDate(dt, field.Granularity ?? "Day");
                }
            }

            return raw;
        }

        private static string TruncateDate(DateTimeOffset dt, string granularity) => granularity switch
        {
            "Year" => $"{dt.Year}",
            "Quarter" => $"{dt.Year}-Q{((dt.Month - 1) / 3) + 1}",
            "Month" => dt.ToString("yyyy-MM"),
            "Week" => $"{dt.Year}-W{ISOWeek.GetWeekOfYear(dt.DateTime):D2}",
            "Hour" => dt.ToString("yyyy-MM-dd HH:00"),
            _ => dt.ToString("yyyy-MM-dd")  // Day
        };

        private static bool MatchesFilter(Dictionary<string, string> row, FilterDimensionDto filter)
        {
            var raw = row.GetValueOrDefault(filter.FieldName) ?? "";
            return filter.Op switch
            {
                "NotIn" => !filter.Values.Contains(raw, StringComparer.OrdinalIgnoreCase),
                "Equals" => filter.Values.Count > 0 && raw.Equals(filter.Values[0], StringComparison.OrdinalIgnoreCase),
                "Contains" => filter.Values.Any(v => raw.Contains(v, StringComparison.OrdinalIgnoreCase)),
                "GreaterThan" => filter.Values.Count > 0 && Compare(raw, filter.Values[0]) > 0,
                "LessThan" => filter.Values.Count > 0 && Compare(raw, filter.Values[0]) < 0,
                "Between" => filter.Values.Count >= 2 &&
                             Compare(raw, filter.Values[0]) >= 0 &&
                             Compare(raw, filter.Values[1]) <= 0,
                _ => filter.Values.Contains(raw, StringComparer.OrdinalIgnoreCase)  // "In"
            };
        }

        private static int Compare(string a, string b)
        {
            if (decimal.TryParse(a.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var da) &&
                decimal.TryParse(b.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var db))
                return da.CompareTo(db);

            if (DateTimeOffset.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dta) &&
                DateTimeOffset.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtb))
                return dta.CompareTo(dtb);

            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static object? Aggregate(
            IEnumerable<string?> rawValues,
            AggregationType aggregation,
            string canonicalType)
        {
            var values = rawValues.ToList();

            if (aggregation == AggregationType.Count)
                return (long)values.Count;

            if (aggregation == AggregationType.DistinctCount)
                return (long)values
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

            if (aggregation is AggregationType.Sum or AggregationType.Average)
            {
                var nums = values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => decimal.TryParse(v!.Replace(",", ""), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .ToList();

                if (nums.Count == 0) return null;
                return aggregation == AggregationType.Sum
                    ? nums.Sum()
                    : Math.Round(nums.Average(), 6);
            }

            var nonEmpty = values.Where(v => !string.IsNullOrEmpty(v)).ToList();
            if (nonEmpty.Count == 0) return null;

            if (canonicalType is "DateTimeOffset" or "DateTime")
            {
                var dates = nonEmpty
                    .Select(v => DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTimeOffset?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (dates.Count == 0) return null;
                return aggregation == AggregationType.Max
                    ? dates.Max().ToString("yyyy-MM-dd")
                    : dates.Min().ToString("yyyy-MM-dd");
            }

            if (canonicalType is "Integer" or "Decimal")
            {
                var nums2 = nonEmpty
                    .Select(v => decimal.TryParse(v!.Replace(",", ""), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (nums2.Count == 0) return null;
                return aggregation == AggregationType.Max ? nums2.Max() : nums2.Min();
            }

            return aggregation == AggregationType.Max
                ? nonEmpty.Max(StringComparer.OrdinalIgnoreCase)
                : nonEmpty.Min(StringComparer.OrdinalIgnoreCase);
        }

        private static IComparable SortKey(
            Dictionary<string, string> row,
            string fieldName,
            Dictionary<string, string> schemaLookup)
        {
            var raw = row.GetValueOrDefault(fieldName) ?? "";
            var canonType = schemaLookup.GetValueOrDefault(fieldName, "String");

            if (canonType is "DateTimeOffset" or "DateTime" &&
                DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;

            if (canonType is "Integer" or "Decimal" &&
                decimal.TryParse(raw.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;

            return raw;
        }
    }
}
