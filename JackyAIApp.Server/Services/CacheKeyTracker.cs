namespace JackyAIApp.Server.Services
{
    public class CacheKeyTracker: ICacheKeyTracker
    {
        private readonly Dictionary<string, (DateTime AddedTime, TimeSpan ExpirationTime)> _cacheKeys;

        public CacheKeyTracker()
        {
            _cacheKeys = new Dictionary<string, (DateTime, TimeSpan)>();
        }

        public void AddKey(string key, TimeSpan expirationTime)
        {
            _cacheKeys[key] = (DateTime.UtcNow, expirationTime);
        }

        public void RemoveKey(string key)
        {
            _cacheKeys.Remove(key);
        }

        public IEnumerable<string> GetKeysByPattern(string pattern)
        {
            return _cacheKeys.Keys.Where(key => key.Contains(pattern));
        }

        public void CleanUp()
        {
            var now = DateTime.UtcNow;
            var keysToRemove = _cacheKeys.Where(kvp => now - kvp.Value.AddedTime > kvp.Value.ExpirationTime).Select(kvp => kvp.Key).ToList();
            foreach (var key in keysToRemove)
            {
                _cacheKeys.Remove(key);
            }
        }
    }
}
