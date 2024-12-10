namespace JackyAIApp.Server.Services
{
    public interface ICacheKeyTracker
    {
        void AddKey(string key, TimeSpan expirationTime);
        void RemoveKey(string key);
        IEnumerable<string> GetKeysByPattern(string pattern);
        void CleanUp();
    }
}
