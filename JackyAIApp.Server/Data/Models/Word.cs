using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

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

        public List<ClozeTest>? ClozeTests { get; set; }

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
        /// Gets or sets the KK phonics of the word.
        /// This represents the pronunciation of the word in KK (Kenyon and Knott) phonetic notation.
        /// </summary>
        public required string KKPhonics { get; set; }

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
        public required List<Definition> Definitions { get; set; }

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
     /// Represents a bilingual definition of a word, providing explanations in both English and Chinese.
     /// This class is essential for understanding the meanings of words across two major languages, aiding bilingual education and translation tasks.
     /// </summary>
    public class Definition
    {
        /// <summary>
        /// The definition of the word in English.
        /// This field is used to convey the meaning of the word as understood and used in English-speaking contexts.
        /// </summary>
        public required string English { get; set; }

        /// <summary>
        /// The corresponding definition of the word in Chinese.
        /// This field is crucial for Chinese-speaking users to understand the exact nuance of the word's English meaning in their native language.
        /// </summary>
        public required string Chinese { get; set; }
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
    public class ClozeTest
    {
        /// <summary>
        /// Gets or sets the question text for the cloze test.
        /// </summary>
        public required string Question { get; set; }

        /// <summary>
        /// Gets or sets the list of options available for the cloze test.
        /// </summary>
        public required List<string> Options { get; set; }

        /// <summary>
        /// Gets or sets the correct answer for the cloze test.
        /// </summary>
        public required string Answer { get; set; }
    }
}
