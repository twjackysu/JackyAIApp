namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// A custom data source's settings were refused — on save, or when a stored config is turned into an
    /// engine. The message is ours, never a driver's, so it is safe to return to the caller as a 400.
    /// </summary>
    public sealed class DataSourceConfigException(string message) : Exception(message);
}
