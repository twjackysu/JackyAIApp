using Newtonsoft.Json;

namespace JackyAIApp.Server.DTO
{
    /// <summary>
    /// Represents the definition of an English word, including its meanings, usage examples, synonyms, antonyms, and additional linguistic information.
    /// </summary>
    public class Word : WordBase
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
    public class TranslationTestUserResponse
    {
        /// <summary>
        /// Unfamiliar word
        /// </summary>
        public required string UnfamiliarWords { get; set; }
        /// <summary>
        /// Sentence to be translated (Traditional Chinese)
        /// </summary>
        public required string ExaminationQuestion { get; set; }
        /// <summary>
        /// User's English translation
        /// </summary>
        public required string Translation { get; set; }
    }
    public class TranslationQualityGradingAssistantResponse
    {
        /// <summary>
        /// Translation quality grading based on the user's UnfamiliarWords, ExaminationQuestion, and Translation, along with the reason
        /// </summary>
        public required string TranslationQualityGrading { get; set; }
    }

    public class TranslationTest
    {
        public required string Chinese { get; set; }
        public required string English { get; set; }
    }
    public class TranslationTestResponse : TranslationTest
    {
        public required string Word { get; set; }
    }

    public class ConversationStartRequest
    {
        public required string Scenario { get; set; }
        public required string UserRole { get; set; }
        public required string AiRole { get; set; }
        public int DifficultyLevel { get; set; } = 3;
    }

    public class ConversationStartResponse
    {
        public required string Scenario { get; set; }
        public required string UserRole { get; set; }
        public required string AiRole { get; set; }
        public required string Context { get; set; }
        public required string FirstMessage { get; set; }
    }

    public class ConversationContext
    {
        public required string Scenario { get; set; }
        public required string UserRole { get; set; }
        public required string AiRole { get; set; }
        public required int TurnNumber { get; set; }
    }

    public class ConversationTurn
    {
        public required string Speaker { get; set; }
        public required string Message { get; set; }
    }

    public class ConversationResponseRequest
    {
        public required ConversationContext ConversationContext { get; set; }
        public required List<ConversationTurn> ConversationHistory { get; set; }
        public required string UserMessage { get; set; }
    }

    public class ConversationCorrection
    {
        public required bool HasCorrection { get; set; }
        public string? OriginalText { get; set; }
        public string? SuggestedText { get; set; }
        public string? Explanation { get; set; }
    }

    public class ConversationResponseResponse
    {
        public required string AiResponse { get; set; }
        public required ConversationCorrection Correction { get; set; }
    }

    public class WhisperTranscriptionResponse
    {
        public required string Text { get; set; }
    }

    public class SentenceTest
    {
        public required string Prompt { get; set; }
        public required string SampleAnswer { get; set; }
        public required string Context { get; set; }
        public required int DifficultyLevel { get; set; }
        public string? GrammarPattern { get; set; }
    }

    public class SentenceTestResponse : SentenceTest
    {
        public required string Word { get; set; }
    }

    public class SentenceTestUserResponse
    {
        public required string Word { get; set; }
        public required string Prompt { get; set; }
        public required string Context { get; set; }
        public required string UserSentence { get; set; }
        public required int DifficultyLevel { get; set; }
        public string? GrammarPattern { get; set; }
    }

    public class SentenceTestGradingResponse
    {
        public required int Score { get; set; }
        public required string GrammarFeedback { get; set; }
        public required string UsageFeedback { get; set; }
        public required string CreativityFeedback { get; set; }
        public required string OverallFeedback { get; set; }
        public required List<string> Suggestions { get; set; }
    }
}
