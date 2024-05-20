using Newtonsoft.Json;

namespace JackyAIApp.Server.Data.Models
{
    public class PersonalWord
    {
        /// <summary>
        /// Unique identifier for a PersonalWord document in Cosmos DB.
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public required string Id { get; set; }

        /// <summary>
        /// Partition key used by Cosmos DB to distribute data across multiple partitions. It's calculated based on specific business logic to ensure efficient data distribution and query performance.
        /// </summary>
        [JsonProperty(PropertyName = "partitionKey")]
        public required string PartitionKey { get; set; }

        /// <summary>
        /// The id of the word that is being favorited.
        /// </summary>
        public required string WordId { get; set; }

        /// <summary>
        /// The user id who favorited the word.
        /// </summary>
        public required string UserId { get; set; }

        /// <summary>
        /// The date when the word was favorited.
        /// </summary>
        public required DateTime CreationDate { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

}
