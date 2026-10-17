namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Limits on the anonymous session upload (<see cref="SessionDataStore"/>), which keeps every uploaded
    /// row in this process's memory for two hours. Bound from <see cref="SectionName"/>, so an App Service
    /// app setting such as <c>DataAnalysis__SessionUpload__MaxMemoryLoad</c> can tune one without a deploy.
    /// Sizes are the <em>estimated</em> in-memory size (see <see cref="SessionDataStore.Measure"/>), not
    /// the JSON size.
    /// </summary>
    public sealed class SessionUploadOptions
    {
        public const string SectionName = "DataAnalysis:SessionUpload";

        /// <summary>
        /// Largest request body accepted, checked against Content-Length before the body is read and
        /// enforced by Kestrel while it is read (a chunked body has no Content-Length). Half of
        /// <see cref="MaxSessionBytes"/>: plain-ASCII JSON is at most half its estimated in-memory size
        /// (each character is one byte in JSON but two in a .NET string, and every cell adds objects the
        /// JSON doesn't have), so this never refuses such an upload that the memory cap would still take.
        /// Just above Kestrel's 30 MB default that applied until now.
        /// </summary>
        public const long MaxRequestBodyBytes = 32L * 1024 * 1024;

        /// <summary>What a caller is told when the upload is refused for load, not for its content.</summary>
        public const string BusyMessage = "目前上傳的資料量已達上限，請稍後再試。";

        public int MaxRows { get; set; } = 50_000;

        public int MaxColumns { get; set; } = 200;

        /// <summary>Longest column name or cell value, in characters.</summary>
        public int MaxCellLength { get; set; } = 1_000;

        /// <summary>
        /// Largest estimated in-memory size of one upload: 50,000 rows of about ten short columns
        /// (≈ 20 characters of name + value per cell) come to ≈ 63 MB.
        /// </summary>
        public long MaxSessionBytes { get; set; } = 64L * 1024 * 1024;

        /// <summary>
        /// Estimated size of every live session together (<see cref="SessionUploadBudget"/>). Measured on
        /// prod (B1, 1.75 GB) over 2026-10-10..17: the plan sat at 71–78% memory (hourly average, peak
        /// 93%) with this app's working set around 200 MB (max 340 MB) — roughly 400 MB free, so sessions
        /// get a bit over half of it.
        /// </summary>
        public long MaxTotalBytes { get; set; } = 256L * 1024 * 1024;

        /// <summary>
        /// New uploads are refused before their body is read while the machine's memory load (as the GC
        /// last measured it) is at or above this fraction — a last-ditch brake; the session budget above is
        /// the main bound. Set high on purpose: the plan's normal load is already 71–78%, so anything near
        /// 70% would refuse every upload. At 98% only ≈ 35 MB of the 1.75 GB is left, so lower it (app
        /// setting) if uploads ever coincide with out-of-memory restarts.
        /// </summary>
        public double MaxMemoryLoad { get; set; } = 0.98;
    }
}
