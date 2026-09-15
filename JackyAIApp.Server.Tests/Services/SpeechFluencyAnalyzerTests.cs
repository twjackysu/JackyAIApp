using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.OpenAI;

namespace JackyAIApp.Server.Tests.Services
{
    public class SpeechFluencyAnalyzerTests
    {
        [Fact]
        public void Analyze_TooShortClip_ReturnsNull()
        {
            var feedback = SpeechFluencyAnalyzer.Analyze("hi there", 1.0, []);

            Assert.Null(feedback);
        }

        [Fact]
        public void Analyze_EmptyTranscript_ReturnsNull()
        {
            var feedback = SpeechFluencyAnalyzer.Analyze("   ", 5.0, []);

            Assert.Null(feedback);
        }

        [Fact]
        public void Analyze_ComputesPaceFromWordCountAndDuration()
        {
            // 10 words over 5 seconds = 120 words/minute.
            var feedback = SpeechFluencyAnalyzer.Analyze(
                "one two three four five six seven eight nine ten", 5.0, []);

            Assert.NotNull(feedback);
            Assert.Equal(120.0, feedback!.PaceWpm);
            Assert.Contains("語速適中", feedback.Comments);
        }

        [Fact]
        public void Analyze_SlowPace_CommentsOnSlowness()
        {
            // 5 words over 6 seconds = 50 wpm, well under the slow threshold.
            var feedback = SpeechFluencyAnalyzer.Analyze("one two three four five", 6.0, []);

            Assert.NotNull(feedback);
            Assert.Contains(feedback!.Comments, c => c.Contains("有點慢"));
        }

        [Fact]
        public void Analyze_FastPace_CommentsOnSpeed()
        {
            // 20 words over 5 seconds = 240 wpm, well over the fast threshold.
            var text = string.Join(' ', Enumerable.Repeat("word", 20));
            var feedback = SpeechFluencyAnalyzer.Analyze(text, 5.0, []);

            Assert.NotNull(feedback);
            Assert.Contains(feedback!.Comments, c => c.Contains("有點快"));
        }

        [Fact]
        public void Analyze_DetectsLongPausesBetweenWords()
        {
            var words = new List<TranscribedWord>
            {
                new("one", 0.0, 0.5),
                new("two", 0.6, 1.0),
                // 2s gap here — a long pause.
                new("three", 3.0, 3.5),
                new("four", 3.6, 4.0),
                // 1s gap here — not long enough.
                new("five", 5.0, 5.5),
            };

            var feedback = SpeechFluencyAnalyzer.Analyze("one two three four five", 6.0, words);

            Assert.NotNull(feedback);
            Assert.Equal(1, feedback!.PauseCount);
            Assert.Contains(feedback.Comments, c => c.Contains("1 次明顯停頓"));
        }

        [Fact]
        public void Analyze_NoLongPauses_ReportsZero()
        {
            var words = new List<TranscribedWord>
            {
                new("one", 0.0, 0.5),
                new("two", 0.6, 1.0),
                new("three", 1.1, 1.5),
            };

            var feedback = SpeechFluencyAnalyzer.Analyze("one two three", 2.0, words);

            Assert.NotNull(feedback);
            Assert.Equal(0, feedback!.PauseCount);
            Assert.Contains("沒有明顯的長停頓", feedback.Comments);
        }

        [Theory]
        [InlineData("um, I think so")]
        [InlineData("uh, maybe")]
        [InlineData("I was, like, going there")]
        [InlineData("you know, it's fine")]
        public void Analyze_DetectsFillerWords(string transcript)
        {
            var feedback = SpeechFluencyAnalyzer.Analyze(transcript, 5.0, []);

            Assert.NotNull(feedback);
            Assert.True(feedback!.FillerWordCount >= 1);
            Assert.Contains(feedback.Comments, c => c.Contains("贅字") && !c.Contains("沒有"));
        }

        [Fact]
        public void Analyze_FillerWordMatchIsWholeWordOnly()
        {
            // "unlike" contains "like" as a substring but is not the filler word "like".
            var feedback = SpeechFluencyAnalyzer.Analyze("this is unlike anything else", 5.0, []);

            Assert.NotNull(feedback);
            Assert.Equal(0, feedback!.FillerWordCount);
            Assert.Contains("沒有明顯的贅字", feedback.Comments);
        }

        [Fact]
        public void Analyze_NoFillerWords_ReportsZero()
        {
            var feedback = SpeechFluencyAnalyzer.Analyze("a clean sentence with no filler", 5.0, []);

            Assert.NotNull(feedback);
            Assert.Equal(0, feedback!.FillerWordCount);
        }
    }
}
