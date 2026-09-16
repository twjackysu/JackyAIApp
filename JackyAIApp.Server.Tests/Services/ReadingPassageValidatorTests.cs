using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Exam;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// The reported failure: the reading comprehension test produced a passage and questions
    /// entirely in Chinese. The existing checks passed it because they only counted questions and
    /// options, and it was then written to <c>ReadingPassages</c> — a bank shared by every user
    /// and reused for most requests — so one bad generation kept being served.
    ///
    /// These tests pin the language split the prompt asks for: everything a learner reads to
    /// answer is English, and only the explanation is Chinese.
    /// </summary>
    public class ReadingPassageValidatorTests
    {
        [Fact]
        public void Valid_PassageIsAccepted()
        {
            Assert.Null(ReadingPassageValidator.Validate(Passage()));
        }

        // ── The reported failure ──────────────────────────────────────────────

        [Fact]
        public void PassageWrittenInChinese_IsRejected()
        {
            var dto = Passage();
            dto.PassageText = "上週末，Maria 決定離開城市的喧囂，和兩個朋友開車上山。";

            Assert.Equal("passage text is not in English", ReadingPassageValidator.Validate(dto));
        }

        [Fact]
        public void TitleInChinese_IsRejected()
        {
            var dto = Passage();
            dto.Title = "週末的山中之旅";

            Assert.Equal("title is not in English", ReadingPassageValidator.Validate(dto));
        }

        [Fact]
        public void QuestionInChinese_IsRejected()
        {
            var dto = Passage();
            dto.Questions[1].QuestionText = "這篇文章主要在講什麼？";

            Assert.Equal("question 2 is not in English", ReadingPassageValidator.Validate(dto));
        }

        [Fact]
        public void OptionInChinese_IsRejected()
        {
            // Options matter as much as the passage: a Chinese option gives the answer away to a
            // reader who cannot read the English passage at all.
            var dto = Passage();
            dto.Questions[0].Options[2].Text = "一次烹飪課。";

            Assert.Equal("question 1 option 3 is not in English", ReadingPassageValidator.Validate(dto));
        }

        [Fact]
        public void FullwidthPunctuationAlone_IsEnoughToReject()
        {
            // A passage that is otherwise English but punctuated 「like this」 came from the model
            // switching language mid-generation, not from a valid English passage.
            var dto = Passage();
            dto.PassageText = "Maria drove to the mountains，and it rained。";

            Assert.Equal("passage text is not in English", ReadingPassageValidator.Validate(dto));
        }

        // ── The mirror image ──────────────────────────────────────────────────

        [Fact]
        public void ExplanationInEnglish_IsRejected()
        {
            // Same prompt slip pointing the other way. The UI shows explanations to a Chinese
            // speaker who just got the question wrong, which is exactly when English does not help.
            var dto = Passage();
            dto.Questions[0].Options[0].Explanation = "Correct. The passage describes a hike.";

            Assert.Equal("question 1 option 1 explanation is not in Chinese", ReadingPassageValidator.Validate(dto));
        }

        [Fact]
        public void ExplanationQuotingEnglishFromThePassage_IsAccepted()
        {
            // Good explanations cite the passage, so "mostly Chinese" is the bar rather than
            // "contains no English" — otherwise the best explanations would be the ones rejected.
            var dto = Passage();
            dto.Questions[0].Options[0].Explanation = "正確。文章說他們「tired but glad they had come prepared」。";

            Assert.Null(ReadingPassageValidator.Validate(dto));
        }

        // ── Shape (kept from the original inline check) ───────────────────────

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        public void WrongNumberOfQuestions_IsRejected(int questionCount)
        {
            var dto = Passage();
            while (dto.Questions.Count > questionCount) dto.Questions.RemoveAt(dto.Questions.Count - 1);
            while (dto.Questions.Count < questionCount) dto.Questions.Add(Question("Another question?"));

            Assert.Equal($"expected 3-4 questions, got {questionCount}", ReadingPassageValidator.Validate(dto));
        }

        [Fact]
        public void TooFewOptions_IsRejected()
        {
            var dto = Passage();
            dto.Questions[0].Options.RemoveAt(0);

            Assert.Equal("expected 3-4 options per question, got 2", ReadingPassageValidator.Validate(dto));
        }

        [Fact]
        public void NoCorrectOption_IsRejected()
        {
            var dto = Passage();
            foreach (var o in dto.Questions[0].Options) o.IsCorrect = false;

            Assert.Equal("expected exactly 1 correct option, got 0", ReadingPassageValidator.Validate(dto));
        }

        [Fact]
        public void TwoCorrectOptions_IsRejected()
        {
            var dto = Passage();
            dto.Questions[0].Options[1].IsCorrect = true;

            Assert.Equal("expected exactly 1 correct option, got 2", ReadingPassageValidator.Validate(dto));
        }

        [Fact]
        public void ShapeIsCheckedBeforeLanguage()
        {
            // So the log names the more basic problem rather than a symptom of it.
            var dto = Passage();
            dto.Questions.RemoveAt(0);
            dto.PassageText = "整段中文。";

            Assert.Equal("expected 3-4 questions, got 2", ReadingPassageValidator.Validate(dto));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ReadingGenerationResult Passage() => new()
        {
            Title = "A Weekend Trip",
            PassageText = "Maria drove to the mountains with two friends for a short weekend hike.",
            Questions =
            [
                Question("What is the passage mainly about?"),
                Question("Who did Maria travel with?"),
                Question("How long was the trip?"),
            ],
        };

        private static ReadingQuestionGen Question(string text) => new()
        {
            QuestionText = text,
            Options =
            [
                new() { Text = "A hiking trip.", IsCorrect = true, Explanation = "正確。" },
                new() { Text = "A cooking class.", IsCorrect = false, Explanation = "錯誤。" },
                new() { Text = "A job interview.", IsCorrect = false, Explanation = "錯誤。" },
            ],
        };
    }
}
