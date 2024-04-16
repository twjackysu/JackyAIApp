using Newtonsoft.Json;

namespace JackyAIApp.Server.Data.Models
{
    /// <summary>
    /// Represents the definition of an English word, including its meanings, usage examples, synonyms, antonyms, and additional linguistic information.
    /// </summary>
    public class Word: WordBase
    {
        /// <summary>
        /// Unique identifier for a WordDefinition document in Cosmos DB.
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public required string Id { get; set; }

        /// <summary>
        /// Partition key used by Cosmos DB to distribute data across multiple partitions. It's calculated based on specific business logic to ensure efficient data distribution and query performance.
        /// </summary>
        [JsonProperty(PropertyName = "partitionKey")]
        public required string PartitionKey { get; set; }

        /// <summary>
        /// The date when the word was added to the dictionary.
        /// </summary>
        public required DateTime DateAdded { get; set; }

        /// <summary>
        /// The date when the word's information was last updated.
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Data is invalid after verification.
        /// </summary>
        public bool? DataInvalid { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
    public class WordBase
    {

        /// <summary>
        /// The English word being defined.
        /// </summary>
        public required string Word { get; set; }

        /// <summary>
        /// A collection of meanings for the word, where each meaning includes a part of speech, definitions, example sentences, 
        /// and associated synonyms, antonyms, and related words.
        /// </summary>
        public required List<WordMeaning> Meanings { get; set; }
    }
    public class WordMeaning
    {
        /// <summary>
        /// The part of speech for this meaning of the word.
        /// </summary>
        public required string PartOfSpeech { get; set; }

        /// <summary>
        /// Definitions for this meaning of the word. Since a single meaning can have multiple definitions,
        /// this is represented as a list.
        /// </summary>
        public required List<string> Definitions { get; set; }

        /// <summary>
        /// Example sentences demonstrating the use of this meaning of the word.
        /// Each example includes both the English sentence and its Chinese translation.
        /// </summary>
        public required List<ExampleSentence> ExampleSentences { get; set; }

        /// <summary>
        /// List of synonyms related to this specific meaning of the word.
        /// </summary>
        public required List<string> Synonyms { get; set; }

        /// <summary>
        /// List of antonyms related to this specific meaning of the word.
        /// </summary>
        public required List<string> Antonyms { get; set; }

        /// <summary>
        /// List of words or phrases that are related to this specific meaning of the word.
        /// </summary>
        public required List<string> RelatedWords { get; set; }
    }

    /// <summary>
    /// Represents an example sentence using a specific word, including both the English sentence and its Chinese translation.
    /// </summary>
    public class ExampleSentence
    {
        /// <summary>
        /// The example sentence in English.
        /// </summary>
        public required string English { get; set; }

        /// <summary>
        /// The corresponding translation of the example sentence in Chinese.
        /// </summary>
        public required string Chinese { get; set; }
    }

}
