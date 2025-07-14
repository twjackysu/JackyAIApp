using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Represents the definition of an English word, including its meanings, usage examples, synonyms, antonyms, and additional linguistic information.
    /// </summary>
    public class Word
    {
        public required string Id { get; set; }

        /// <summary>
        /// The English word being defined.
        /// </summary>
        public required string WordText { get; set; }

        /// <summary>
        /// Gets or sets the KK phonics of the word.
        /// This represents the pronunciation of the word in KK (Kenyon and Knott) phonetic notation.
        /// </summary>
        public required string KKPhonics { get; set; }

        /// <summary>
        /// The date when the word was added to the dictionary.
        /// </summary>
        public required DateTime DateAdded { get; set; }

        /// <summary>
        /// The date when the word's information was last updated.
        /// </summary>
        public required DateTime LastUpdated { get; set; }

        /// <summary>
        /// Data is invalid after verification.
        /// </summary>
        public bool? DataInvalid { get; set; }

        public ICollection<WordMeaning> Meanings { get; set; } = [];
        public ICollection<ClozeTest> ClozeTests { get; set; } = [];
        public ICollection<TranslationTest> TranslationTests { get; set; } = [];
        public ICollection<SentenceTest> SentenceTests { get; set; } = [];
    }

    public class WordMeaning
    {
        public required string Id { get; set; }

        /// <summary>
        /// The part of speech for this meaning of the word.
        /// </summary>
        public required string PartOfSpeech { get; set; }

        public required string WordId { get; set; }
        [ForeignKey(nameof(WordId))]
        public Word Word { get; set; } = null!;
        public ICollection<Definition> Definitions { get; set; } = [];
        public ICollection<ExampleSentence> ExampleSentences { get; set; } = [];
        public ICollection<WordMeaningTag> Tags { get; set; } = [];

        /// <summary>
        /// List of synonyms related to this specific meaning of the word.
        /// </summary>
        [NotMapped]
        public List<string> Synonyms => Tags.Where(t => t.TagType == "Synonym").Select(t => t.Word).ToList();

        /// <summary>
        /// List of antonyms related to this specific meaning of the word.
        /// </summary>
        [NotMapped]
        public List<string> Antonyms => Tags.Where(t => t.TagType == "Antonym").Select(t => t.Word).ToList();

        /// <summary>
        /// List of words or phrases that are related to this specific meaning of the word.
        /// </summary>
        [NotMapped]
        public List<string> RelatedWords => Tags.Where(t => t.TagType == "Related").Select(t => t.Word).ToList();
    }

    /// <summary>
    /// Represents a bilingual definition of a word, providing explanations in both English and Chinese.
    /// </summary>
    public class Definition
    {
        public required string Id { get; set; }

        /// <summary>
        /// The definition of the word in English.
        /// </summary>
        public required string English { get; set; }

        /// <summary>
        /// The corresponding definition of the word in Chinese.
        /// </summary>
        public required string Chinese { get; set; }

        public required string WordMeaningId { get; set; }
        [ForeignKey(nameof(WordMeaningId))]
        public WordMeaning WordMeaning { get; set; } = null!;
    }

    /// <summary>
    /// Represents an example sentence using a specific word, including both the English sentence and its Chinese translation.
    /// </summary>
    public class ExampleSentence
    {
        public required string Id { get; set; }

        /// <summary>
        /// The example sentence in English.
        /// </summary>
        public required string English { get; set; }

        /// <summary>
        /// The corresponding translation of the example sentence in Chinese.
        /// </summary>
        public required string Chinese { get; set; }

        public required string WordMeaningId { get; set; }
        [ForeignKey(nameof(WordMeaningId))]
        public WordMeaning WordMeaning { get; set; } = null!;
    }

    public class WordMeaningTag
    {
        public required string Id { get; set; }

        /// <summary>
        /// The type of tag (Synonym, Antonym, Related).
        /// </summary>
        public required string TagType { get; set; }

        /// <summary>
        /// The word or phrase associated with the tag.
        /// </summary>
        public required string Word { get; set; }

        public required string WordMeaningId { get; set; }
        [ForeignKey(nameof(WordMeaningId))]
        public WordMeaning WordMeaning { get; set; } = null!;
    }

    public class ClozeTest
    {
        public required string Id { get; set; }

        /// <summary>
        /// Gets or sets the question text for the cloze test.
        /// </summary>
        public required string Question { get; set; }

        /// <summary>
        /// Gets or sets the correct answer for the cloze test.
        /// </summary>
        public required string Answer { get; set; }

        public required string WordId { get; set; }
        [ForeignKey(nameof(WordId))]
        public Word Word { get; set; } = null!;
        public ICollection<ClozeTestOption> Options { get; set; } = new List<ClozeTestOption>();
    }

    public class ClozeTestOption
    {
        public required string Id { get; set; }

        /// <summary>
        /// The text of the option.
        /// </summary>
        public required string OptionText { get; set; }

        public required string ClozeTestId { get; set; }
        [ForeignKey(nameof(ClozeTestId))]
        public ClozeTest ClozeTest { get; set; } = null!;
    }

    public class TranslationTest
    {
        public required string Id { get; set; }

        /// <summary>
        /// Sentence to be translated (Traditional Chinese)
        /// </summary>
        public required string Chinese { get; set; }

        /// <summary>
        /// User's English translation
        /// </summary>
        public required string English { get; set; }

        public required string WordId { get; set; }
        [ForeignKey(nameof(WordId))]
        public Word Word { get; set; } = null!;
    }

    public class SentenceTest
    {
        public required string Id { get; set; }

        /// <summary>
        /// The prompt given to the user for sentence formation.
        /// </summary>
        public required string Prompt { get; set; }

        /// <summary>
        /// A sample answer provided by AI for reference.
        /// </summary>
        public required string SampleAnswer { get; set; }

        /// <summary>
        /// Context or scenario for the sentence formation.
        /// </summary>
        public required string Context { get; set; }

        /// <summary>
        /// Difficulty level from 1 (easy) to 5 (hard).
        /// </summary>
        public required int DifficultyLevel { get; set; }

        /// <summary>
        /// Grammar pattern or structure to be used (optional).
        /// </summary>
        public string? GrammarPattern { get; set; }

        public required string WordId { get; set; }
        [ForeignKey(nameof(WordId))]
        public Word Word { get; set; } = null!;
    }
}
