using Newtonsoft.Json;

namespace JackyAIApp.Server.Data.Models
{
    public class User
    {
        /// <summary>
        /// Gets or sets the unique identifier for the entity.
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public required string Id { get; set; }

        /// <summary>
        /// Gets or sets the partition key used to distribute data across partitions in a NoSQL database.
        /// </summary>
        [JsonProperty(PropertyName = "partitionKey")]
        public required string PartitionKey { get; set; }

        /// <summary>
        /// User's name
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// User's email
        /// </summary>
        public string? Email { get; set; }
        /// <summary>
        /// Gets or sets the remaining credit balance for the user.
        /// </summary>
        public required ulong CreditBalance { get; set; }

        /// <summary>
        /// Gets or sets the total number of credits the user has used.
        /// </summary>
        public required ulong TotalCreditsUsed { get; set; }

        /// <summary>
        /// Gets or sets the date and time when the credit data was last updated.
        /// </summary>
        public required DateTime LastUpdated { get; set; }

        /// <summary>
        /// Gets or sets the list of word IDs associated with the user.
        /// </summary>
        public required List<string> WordIds { get; set; }
        public bool? IsAdmin { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
