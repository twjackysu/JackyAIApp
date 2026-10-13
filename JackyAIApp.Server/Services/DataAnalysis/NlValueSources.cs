using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Enums;
using DataAnalysisLibrary.Core.Models.Common;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>A column's distinct values; <see cref="IsTruncated"/> means the column holds more than were returned.</summary>
    public sealed record ColumnValues(List<string> Values, bool IsTruncated);

    /// <summary>
    /// Where the NL summarize agent reads a data source's real String values from. Every method is
    /// best-effort: a failure or timeout yields "unknown" (null / empty), never an exception, so the
    /// agent degrades to "not listed in the prompt / not validated" instead of failing the question.
    /// </summary>
    public interface INlValueSource
    {
        /// <summary>Distinct values of each field, at most <paramref name="limit"/> per field; null when they couldn't be read.</summary>
        Task<Dictionary<string, ColumnValues>?> DistinctAsync(IReadOnlyList<string> fields, int limit, CancellationToken ct);

        /// <summary>The stored spellings of <paramref name="values"/> that exist in <paramref name="field"/> (case-insensitive); null when unknown.</summary>
        Task<List<string>?> LookupAsync(string field, IReadOnlyList<string> values, CancellationToken ct);

        /// <summary>Up to <paramref name="limit"/> values of <paramref name="field"/> containing <paramref name="term"/>; empty when none or unknown.</summary>
        Task<List<string>> SearchAsync(string field, string term, int limit, CancellationToken ct);
    }

    /// <summary>
    /// <see cref="INlValueSource"/> over a DataAnalysisLibrary engine (the default SQL source or a
    /// user's connector), using the library's DistinctValues / LookupValues queries.
    /// <para>
    /// Every call is bounded with <see cref="TimeBox"/> — the ADX connector ignores cancellation, so
    /// the wait has to be cut rather than cancelled. Distinct values are cached for a few minutes per
    /// source and table: each NL question reads every String column, which the 5-DTU default database
    /// shouldn't pay per question. A timeout is cached too (as "unknown"), so a source too slow to
    /// list doesn't cost every later question the same wait.
    /// </para>
    /// </summary>
    public sealed class EngineNlValueSource(
        DataAnalysisEngine engine,
        string sourceKey,
        string tableName,
        string cacheScope,
        IMemoryCache cache,
        ILogger logger,
        TimeSpan? distinctTimeout = null,
        TimeSpan? lookupTimeout = null) : INlValueSource
    {
        /// <summary>Default longest wait for the up-front distinct-values query covering all columns.</summary>
        public static readonly TimeSpan DefaultDistinctTimeout = TimeSpan.FromSeconds(6);

        /// <summary>Default longest wait for one lookup / search query.</summary>
        public static readonly TimeSpan DefaultLookupTimeout = TimeSpan.FromSeconds(3);

        private readonly TimeSpan _distinctTimeout = distinctTimeout ?? DefaultDistinctTimeout;
        private readonly TimeSpan _lookupTimeout   = lookupTimeout ?? DefaultLookupTimeout;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public async Task<Dictionary<string, ColumnValues>?> DistinctAsync(IReadOnlyList<string> fields, int limit, CancellationToken ct)
        {
            var key = $"dataanalysis:distinct:{cacheScope}:{tableName}:{limit}:{string.Join("|", fields)}";
            if (cache.TryGetValue(key, out Dictionary<string, ColumnValues>? cached))
                return cached; // null when a previous attempt timed out

            var result = await RunAsync("DistinctValues", _distinctTimeout, ct, async token =>
            {
                var r = await engine.DistinctValuesAsync(new DistinctValuesRequest
                {
                    SourceKey = sourceKey, TableName = tableName, FieldNames = fields, Limit = limit,
                }, token);
                if (!r.IsSuccess)
                {
                    logger.LogWarning("DistinctValues failed: {err}", r.ErrorMessage);
                    return (Ok: false, Value: (Dictionary<string, ColumnValues>?)null);
                }
                var values = r.Value!.Fields.Values.ToDictionary(
                    f => f.FieldName,
                    f => new ColumnValues(NonEmpty(f.Values), f.IsTruncated),
                    StringComparer.OrdinalIgnoreCase);
                return (Ok: true, Value: values);
            });

            if (result.TimedOut || result.Ok)
                cache.Set(key, result.Value, CacheDuration);
            return result.Value;
        }

        public async Task<List<string>?> LookupAsync(string field, IReadOnlyList<string> values, CancellationToken ct)
        {
            var result = await RunAsync("LookupValues", _lookupTimeout, ct, async token =>
            {
                var r = await engine.LookupValuesAsync(new LookupValuesRequest
                {
                    SourceKey = sourceKey, TableName = tableName, FieldName = field, Values = values,
                }, token);
                if (!r.IsSuccess) logger.LogWarning("LookupValues on {field} failed: {err}", field, r.ErrorMessage);
                return (Ok: r.IsSuccess, Value: r.IsSuccess ? NonEmpty(r.Value!.Matches) : null);
            });
            return result.Value;
        }

        public async Task<List<string>> SearchAsync(string field, string term, int limit, CancellationToken ct)
        {
            // A Contains filter on the same column; connectors without Contains (ADX) just fail → empty.
            var result = await RunAsync("DistinctValues search", _lookupTimeout, ct, async token =>
            {
                var r = await engine.DistinctValuesAsync(new DistinctValuesRequest
                {
                    SourceKey = sourceKey, TableName = tableName, FieldNames = [field], Limit = limit,
                    Filters   = [new FilterDimension(field, [term], FilterOperator.Contains)],
                }, token);
                return (Ok: r.IsSuccess, Value: r.IsSuccess ? NonEmpty(r.Value!.Fields[field].Values) : null);
            });
            return result.Value ?? [];
        }

        private async Task<(bool TimedOut, bool Ok, T? Value)> RunAsync<T>(
            string what, TimeSpan timeout, CancellationToken ct, Func<CancellationToken, Task<(bool Ok, T? Value)>> query)
        {
            try
            {
                var (timedOut, result) = await TimeBox.RunAsync(query, timeout, ct);
                if (timedOut)
                {
                    logger.LogWarning("{what} on {table} exceeded {s}s", what, tableName, timeout.TotalSeconds);
                    return (true, false, default);
                }
                return (false, result.Ok, result.Value);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "{what} on {table} failed", what, tableName);
                return (false, false, default);
            }
        }

        private static List<string> NonEmpty(IEnumerable<string?> values) =>
            values.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList();
    }

    /// <summary><see cref="INlValueSource"/> over an uploaded in-memory session (at most 50k rows, so no limits or timeouts needed).</summary>
    public sealed class SessionNlValueSource(ISessionDataStore store, string sessionId) : INlValueSource
    {
        public Task<Dictionary<string, ColumnValues>?> DistinctAsync(IReadOnlyList<string> fields, int limit, CancellationToken ct)
        {
            var result = new Dictionary<string, ColumnValues>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                var values = All(field, limit + 1);
                result[field] = new ColumnValues(values.Take(limit).ToList(), values.Count > limit);
            }
            return Task.FromResult<Dictionary<string, ColumnValues>?>(result);
        }

        public Task<List<string>?> LookupAsync(string field, IReadOnlyList<string> values, CancellationToken ct)
        {
            var wanted = values.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<List<string>?>(All(field, int.MaxValue).Where(wanted.Contains).ToList());
        }

        public Task<List<string>> SearchAsync(string field, string term, int limit, CancellationToken ct) =>
            Task.FromResult(All(field, int.MaxValue)
                .Where(v => v.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList());

        private List<string> All(string field, int limit)
        {
            var result = store.GetDistinctValues(sessionId, field, limit);
            return result.IsSuccess ? ((IEnumerable<string>)((dynamic)result.Data!).values).ToList() : [];
        }
    }
}
