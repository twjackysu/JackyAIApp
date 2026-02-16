using Newtonsoft.Json;

namespace JackyAIApp.Server.DTO
{
    /// <summary>
    /// Represents the definition of an English word, including its meanings, usage examples, synonyms, antonyms, and additional linguistic information.
    /// </summary>
    public class Word : WordBase
    {
        /// <summary>
        /// Unique identifier for a Word.
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public required string Id { get; set; }

        /// <summary>
        /// Partition key for data distribution.
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

        public List<ClozeTest>? ClozeTests { get; set; }
        public List<TranslationTest>? TranslationTests { get; set; }

        /// <summary>
        /// Data is invalid after verification.
        /// </summary>
        public bool? DataInvalid { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    /// <summary>
    /// Base class for word definition (used for OpenAI response parsing)
    /// </summary>
    public class WordBase
    {
        /// <summary>
        /// The English word being defined.
        /// </summary>
        public required string Word { get; set; }

        /// <summary>
        /// Gets or sets the KK phonics of the word.
        /// </summary>
        public required string KKPhonics { get; set; }

        /// <summary>
        /// A collection of meanings for the word.
        /// </summary>
        public required List<WordMeaning> Meanings { get; set; }
    }

    /// <summary>
    /// Represents a meaning of a word with its definitions and examples
    /// </summary>
    public class WordMeaning
    {
        /// <summary>
        /// The part of speech for this meaning of the word.
        /// </summary>
        public required string PartOfSpeech { get; set; }

        /// <summary>
        /// Definitions for this meaning of the word.
        /// </summary>
        public required List<Definition> Definitions { get; set; }

        /// <summary>
        /// Example sentences demonstrating the use of this meaning.
        /// </summary>
        public required List<ExampleSentence> ExampleSentences { get; set; }

        /// <summary>
        /// List of synonyms related to this specific meaning.
        /// </summary>
        public required List<string> Synonyms { get; set; }

        /// <summary>
        /// List of antonyms related to this specific meaning.
        /// </summary>
        public required List<string> Antonyms { get; set; }

        /// <summary>
        /// List of related words or phrases.
        /// </summary>
        public required List<string> RelatedWords { get; set; }
    }

    /// <summary>
    /// Bilingual definition with English and Chinese explanations
    /// </summary>
    public class Definition
    {
        /// <summary>
        /// The definition in English.
        /// </summary>
        public required string English { get; set; }

        /// <summary>
        /// The definition in Chinese.
        /// </summary>
        public required string Chinese { get; set; }
    }

    /// <summary>
    /// Bilingual example sentence
    /// </summary>
    public class ExampleSentence
    {
        /// <summary>
        /// The example sentence in English.
        /// </summary>
        public required string English { get; set; }

        /// <summary>
        /// The translation in Chinese.
        /// </summary>
        public required string Chinese { get; set; }
    }
}
