using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services
{
    public class DataMigrationService
    {
        private readonly AzureCosmosDBContext _cosmosDbContext;
        private readonly AzureSQLDBContext _sqlDbContext;
        private readonly ILogger<DataMigrationService> _logger;

        public DataMigrationService(
            AzureCosmosDBContext cosmosDbContext,
            AzureSQLDBContext sqlDbContext,
            ILogger<DataMigrationService> logger)
        {
            _cosmosDbContext = cosmosDbContext;
            _sqlDbContext = sqlDbContext;
            _logger = logger;
        }

        public async Task MigrateAllDataAsync()
        {
            _logger.LogInformation("Starting data migration from Cosmos DB to SQL DB");
            
            try
            {
                // Ensure database is created
                await _sqlDbContext.Database.EnsureCreatedAsync();
                
                // // First migrate Users since Words depend on them through the join table
                await MigrateUsersAsync();
                
                // // Then migrate Words
                await MigrateWordsAsync();
                
                // Finally establish the many-to-many relationships
                await MigrateUserWordsRelationshipsAsync();
                
                _logger.LogInformation("Data migration completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data migration");
                throw;
            }
        }

        private async Task MigrateUsersAsync()
        {
            _logger.LogInformation("Migrating users");
            
            var cosmosUsers = await _cosmosDbContext.User.ToListAsync();
            
            foreach (var cosmosUser in cosmosUsers)
            {
                try 
                {
                    var sqlUser = new User
                    {
                        Id = cosmosUser.Id,
                        Name = cosmosUser.Name,
                        Email = cosmosUser.Email,
                        CreditBalance = cosmosUser.CreditBalance,
                        TotalCreditsUsed = cosmosUser.TotalCreditsUsed,
                        LastUpdated = cosmosUser.LastUpdated,
                        IsAdmin = cosmosUser.IsAdmin
                    };
                    
                    await _sqlDbContext.Users.AddAsync(sqlUser);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error migrating user {cosmosUser.Name}");
                }
            }
            
            await _sqlDbContext.SaveChangesAsync();
            _logger.LogInformation($"Migrated {cosmosUsers.Count} users");
        }

        private async Task MigrateWordsAsync()
        {
            _logger.LogInformation("Migrating words");
            
            var cosmosWords = await _cosmosDbContext.Word.ToListAsync();
            
            foreach (var cosmosWord in cosmosWords)
            {
                try
                {
                    var wordId = cosmosWord.Id;
                    var sqlWord = new Word
                    {
                        Id = wordId,
                        WordText = cosmosWord.Word,
                        KKPhonics = cosmosWord.KKPhonics,
                        DateAdded = cosmosWord.DateAdded,
                        LastUpdated = cosmosWord.LastUpdated,
                        DataInvalid = cosmosWord.DataInvalid
                    };
                    
                    // Process word meanings
                    if (cosmosWord.Meanings != null && cosmosWord.Meanings.Count > 0)
                    {
                        foreach (var cosmosMeaning in cosmosWord.Meanings)
                        {
                            var meaningId = Guid.NewGuid().ToString();
                            var sqlMeaning = new WordMeaning
                            {
                                Id = meaningId,
                                PartOfSpeech = cosmosMeaning.PartOfSpeech,
                                WordId = sqlWord.Id
                            };
                            
                            // Process definitions
                            if (cosmosMeaning.Definitions != null && cosmosMeaning.Definitions.Count > 0)
                            {
                                foreach (var cosmosDefinition in cosmosMeaning.Definitions)
                                {
                                    var definitionId = Guid.NewGuid().ToString();
                                    sqlMeaning.Definitions.Add(new Definition
                                    {
                                        Id = definitionId,
                                        English = cosmosDefinition.English,
                                        Chinese = cosmosDefinition.Chinese,
                                        WordMeaningId = meaningId
                                    });
                                }
                            }
                            
                            // Process example sentences
                            if (cosmosMeaning.ExampleSentences != null && cosmosMeaning.ExampleSentences.Count > 0)
                            {
                                foreach (var cosmosExample in cosmosMeaning.ExampleSentences)
                                {
                                    var exampleId = Guid.NewGuid().ToString();
                                    sqlMeaning.ExampleSentences.Add(new ExampleSentence
                                    {
                                        Id = exampleId,
                                        English = cosmosExample.English,
                                        Chinese = cosmosExample.Chinese,
                                        WordMeaningId = meaningId
                                    });
                                }
                            }
                            
                            // Process synonyms
                            if (cosmosMeaning.Synonyms != null && cosmosMeaning.Synonyms.Count > 0)
                            {
                                foreach (var synonym in cosmosMeaning.Synonyms)
                                {
                                    var wordMeaningTagId = Guid.NewGuid().ToString();
                                    sqlMeaning.Tags.Add(new WordMeaningTag
                                    {
                                        Id = wordMeaningTagId,
                                        TagType = "Synonym",
                                        Word = synonym,
                                        WordMeaningId = meaningId
                                    });
                                }
                            }
                            
                            // Process antonyms
                            if (cosmosMeaning.Antonyms != null && cosmosMeaning.Antonyms.Count > 0)
                            {
                                foreach (var antonym in cosmosMeaning.Antonyms)
                                {
                                    var wordMeaningTagId = Guid.NewGuid().ToString();
                                    sqlMeaning.Tags.Add(new WordMeaningTag
                                    {
                                        Id = wordMeaningTagId,
                                        TagType = "Antonym",
                                        Word = antonym,
                                        WordMeaningId = meaningId
                                    });
                                }
                            }
                            
                            // Process related words
                            if (cosmosMeaning.RelatedWords != null && cosmosMeaning.RelatedWords.Count > 0)
                            {
                                foreach (var relatedWord in cosmosMeaning.RelatedWords)
                                {
                                    var wordMeaningTagId = Guid.NewGuid().ToString();
                                    sqlMeaning.Tags.Add(new WordMeaningTag
                                    {
                                        Id = wordMeaningTagId,
                                        TagType = "Related",
                                        Word = relatedWord,
                                        WordMeaningId = meaningId
                                    });
                                }
                            }
                            
                            sqlWord.Meanings.Add(sqlMeaning);
                        }
                    }
                    
                    // Process cloze tests
                    if (cosmosWord.ClozeTests != null && cosmosWord.ClozeTests.Count > 0)
                    {
                        foreach (var cosmosClozeTest in cosmosWord.ClozeTests)
                        {
                            var clozeTestId = Guid.NewGuid().ToString();
                            var sqlClozeTest = new ClozeTest
                            {
                                Id = clozeTestId,
                                Question = cosmosClozeTest.Question,
                                Answer = cosmosClozeTest.Answer,
                                WordId = sqlWord.Id
                            };
                            
                            if (cosmosClozeTest.Options != null && cosmosClozeTest.Options.Count > 0)
                            {
                                foreach (var option in cosmosClozeTest.Options)
                                {
                                    var clozeTestOptionId = Guid.NewGuid().ToString();
                                    sqlClozeTest.Options.Add(new ClozeTestOption
                                    {
                                        Id = clozeTestOptionId,
                                        OptionText = option,
                                        ClozeTestId = clozeTestId
                                    });
                                }
                            }
                            
                            sqlWord.ClozeTests.Add(sqlClozeTest);
                        }
                    }
                    
                    // Process translation tests
                    if (cosmosWord.TranslationTests != null && cosmosWord.TranslationTests.Count > 0)
                    {
                        foreach (var cosmosTranslationTest in cosmosWord.TranslationTests)
                        {
                            var translationTestId = Guid.NewGuid().ToString();
                            sqlWord.TranslationTests.Add(new TranslationTest
                            {
                                Id = translationTestId,
                                Chinese = cosmosTranslationTest.Chinese,
                                English = cosmosTranslationTest.English,
                                WordId = sqlWord.Id
                            });
                        }
                    }
                    
                    await _sqlDbContext.Words.AddAsync(sqlWord);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error migrating word {cosmosWord.Id}");
                }
            }
            
            await _sqlDbContext.SaveChangesAsync();
            _logger.LogInformation($"Migrated {cosmosWords.Count} words");
        }

        private async Task MigrateUserWordsRelationshipsAsync()
        {
            _logger.LogInformation("Migrating user-word relationships");
            
            var cosmosUsers = await _cosmosDbContext.User.ToListAsync();
            
            foreach (var cosmosUser in cosmosUsers)
            {
                try
                {
                    var userId = cosmosUser.Id;
                    if (string.IsNullOrEmpty(userId))
                    {
                        _logger.LogWarning($"Skipping user-word relationships for user with invalid ID format: {cosmosUser.Id}");
                        continue;
                    }

                    if (cosmosUser.WordIds != null && cosmosUser.WordIds.Count > 0)
                    {
                        var sqlUser = await _sqlDbContext.Users.FindAsync(userId);
                        
                        if (sqlUser == null)
                        {
                            _logger.LogWarning($"User with ID {cosmosUser.Id} not found in SQL DB");
                            continue;
                        }
                        
                        foreach (var wordId in cosmosUser.WordIds)
                        {
                            try
                            {
                                if (string.IsNullOrEmpty(wordId))
                                {
                                    _logger.LogWarning($"Invalid GUID format for word ID: {wordId}");
                                    continue;
                                }
                                
                                var sqlWord = await _sqlDbContext.Words.FindAsync(wordId);
                                
                                if (sqlWord == null)
                                {
                                    _logger.LogWarning($"Word with ID {wordId} not found in SQL DB");
                                    continue;
                                }
                                
                                var userWord = new UserWord
                                {
                                    UserId = sqlUser.Id,
                                    WordId = sqlWord.Id,
                                    DateAdded = DateTime.UtcNow // We don't have the original date in the CosmosDB model
                                };
                                
                                await _sqlDbContext.UserWords.AddAsync(userWord);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error processing word ID {wordId} for user {cosmosUser.Id}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error migrating user-word relationships for user {cosmosUser.Id}");
                }
            }
            
            await _sqlDbContext.SaveChangesAsync();
            _logger.LogInformation("Migrated user-word relationships");
        }

        private Guid TryParseGuid(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return Guid.Empty;
            }

            try
            {
                return Guid.Parse(input);
            }
            catch (FormatException)
            {
                _logger.LogWarning($"Invalid GUID format: {input}");
                return Guid.Empty;
            }
        }
    }
}
