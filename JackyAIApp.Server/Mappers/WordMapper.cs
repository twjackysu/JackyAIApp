using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Dictionary;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Mappers
{
    /// <summary>
    /// Pure mapping between the OpenAI-generated <see cref="DTO.WordBase"/> model,
    /// the EF <see cref="SQL.Word"/> entity, and the API <see cref="DTO.Word"/> DTO.
    /// </summary>
    public static class WordMapper
    {
        /// <summary>Builds a brand-new <see cref="SQL.Word"/> entity from a generated definition.</summary>
        /// <param name="wordbase">The definition the model generated.</param>
        /// <param name="now">When it is stored, in UTC.</param>
        public static SQL.Word ToEntity(DTO.WordBase wordbase, DateTime now)
        {
            var newWordId = Guid.NewGuid().ToString();
            var word = new SQL.Word
            {
                Id = newWordId,
                WordText = wordbase.Word,
                KKPhonics = wordbase.KKPhonics,
                DateAdded = now,
                LastUpdated = now,
                Meanings = new List<SQL.WordMeaning>()
            };

            foreach (var meaningData in wordbase.Meanings)
            {
                word.Meanings.Add(ToMeaningEntity(meaningData, newWordId, word));
            }

            return word;
        }

        /// <summary>Builds a single <see cref="SQL.WordMeaning"/> (with definitions, examples, tags).</summary>
        public static SQL.WordMeaning ToMeaningEntity(DTO.WordMeaning meaningData, string wordId, SQL.Word word)
        {
            var meaningId = Guid.NewGuid().ToString();
            var meaning = new SQL.WordMeaning
            {
                Id = meaningId,
                PartOfSpeech = meaningData.PartOfSpeech,
                WordId = wordId,
                Word = word,
                Definitions = new List<SQL.Definition>(),
                ExampleSentences = new List<SQL.ExampleSentence>(),
                Tags = new List<SQL.WordMeaningTag>()
            };

            if (meaningData.Definitions != null)
            {
                foreach (var definitionData in meaningData.Definitions)
                {
                    meaning.Definitions.Add(new SQL.Definition
                    {
                        Id = Guid.NewGuid().ToString(),
                        English = definitionData.English,
                        Chinese = definitionData.Chinese,
                        WordMeaningId = meaningId,
                        WordMeaning = meaning
                    });
                }
            }

            if (meaningData.ExampleSentences != null)
            {
                foreach (var exampleData in meaningData.ExampleSentences)
                {
                    meaning.ExampleSentences.Add(new SQL.ExampleSentence
                    {
                        Id = Guid.NewGuid().ToString(),
                        English = exampleData.English,
                        Chinese = exampleData.Chinese,
                        WordMeaningId = meaningId,
                        WordMeaning = meaning
                    });
                }
            }

            AddTags(meaning, meaningId, meaningData.Synonyms, Constants.TagTypes.Synonym);
            AddTags(meaning, meaningId, meaningData.Antonyms, Constants.TagTypes.Antonym);
            AddTags(meaning, meaningId, meaningData.RelatedWords, Constants.TagTypes.Related);

            return meaning;
        }

        /// <summary>Maps an EF <see cref="SQL.Word"/> entity to the API DTO.</summary>
        public static DTO.Word ToDto(SQL.Word sqlWord)
        {
            var meanings = ToMeaningDtos(sqlWord.Meanings);

            return new DTO.Word
            {
                Id = sqlWord.Id,
                PartitionKey = sqlWord.WordText,
                Word = sqlWord.WordText,
                KKPhonics = sqlWord.KKPhonics,
                DateAdded = sqlWord.DateAdded,
                LastUpdated = sqlWord.LastUpdated,
                DataInvalid = sqlWord.DataInvalid,
                SeoTitle = WordSeoTextFormatter.FormatTitle(sqlWord.WordText),
                SeoDescription = WordSeoTextFormatter.FormatDescription(sqlWord.WordText, meanings),
                ClozeTests = sqlWord.ClozeTests?.Select(ct => new DTO.ClozeTest
                {
                    Question = ct.Question,
                    Answer = ct.Answer,
                    Options = ct.Options.Select(o => o.OptionText).ToList()
                }).ToList(),
                TranslationTests = sqlWord.TranslationTests?.Select(tt => new DTO.TranslationTest
                {
                    Chinese = tt.Chinese,
                    English = tt.English
                }).ToList(),
                Meanings = meanings
            };
        }

        /// <summary>Maps a word's meanings (definitions, examples, tags) to the API DTO.</summary>
        public static List<DTO.WordMeaning> ToMeaningDtos(IEnumerable<SQL.WordMeaning> meanings) =>
            meanings.Select(m => new DTO.WordMeaning
            {
                PartOfSpeech = m.PartOfSpeech,
                Definitions = m.Definitions.Select(d => new DTO.Definition
                {
                    English = d.English,
                    Chinese = d.Chinese
                }).ToList(),
                ExampleSentences = m.ExampleSentences.Select(es => new DTO.ExampleSentence
                {
                    English = es.English,
                    Chinese = es.Chinese
                }).ToList(),
                Synonyms = m.Tags.Where(t => t.TagType == Constants.TagTypes.Synonym).Select(t => t.Word).ToList(),
                Antonyms = m.Tags.Where(t => t.TagType == Constants.TagTypes.Antonym).Select(t => t.Word).ToList(),
                RelatedWords = m.Tags.Where(t => t.TagType == Constants.TagTypes.Related).Select(t => t.Word).ToList()
            }).ToList();

        private static void AddTags(SQL.WordMeaning meaning, string meaningId, List<string>? words, string tagType)
        {
            if (words == null) return;

            foreach (var w in words)
            {
                meaning.Tags.Add(new SQL.WordMeaningTag
                {
                    Id = Guid.NewGuid().ToString(),
                    TagType = tagType,
                    Word = w,
                    WordMeaningId = meaningId,
                    WordMeaning = meaning
                });
            }
        }
    }
}
