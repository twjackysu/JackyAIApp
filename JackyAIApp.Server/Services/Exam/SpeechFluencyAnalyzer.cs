using System.Text.RegularExpressions;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// Computes delivery/fluency feedback (pace, pauses, filler words) purely from Whisper's
    /// verbose_json response — no second AI call, no phoneme-level pronunciation scoring. See
    /// .agent/architecture/ARCHITECTURE.md ("Decision: Speaking delivery feedback") / TASK-004 for why
    /// this is scoped to fluency/delivery only.
    /// </summary>
    public static class SpeechFluencyAnalyzer
    {
        /// <summary>Below this clip length, pace/pause metrics are too noisy to be meaningful.</summary>
        private const double MinDurationSecondsForFeedback = 1.5;

        /// <summary>Gap between consecutive words, in seconds, counted as a "long pause".</summary>
        private const double LongPauseThresholdSeconds = 1.5;

        private const double SlowPaceWpmThreshold = 110;
        private const double FastPaceWpmThreshold = 160;

        private static readonly string[] FillerWords = ["um", "uh", "uhh", "like", "you know"];

        public static DeliveryFeedback? Analyze(string transcriptText, double durationSeconds, IReadOnlyList<TranscribedWord> words)
        {
            if (string.IsNullOrWhiteSpace(transcriptText) || durationSeconds < MinDurationSecondsForFeedback)
            {
                return null;
            }

            var wordCount = CountWords(transcriptText);
            if (wordCount == 0)
            {
                return null;
            }

            var paceWpm = wordCount / (durationSeconds / 60.0);
            var pauseCount = CountLongPauses(words);
            var fillerWordCount = CountFillerWords(transcriptText);

            return new DeliveryFeedback
            {
                PaceWpm = Math.Round(paceWpm, 1),
                PauseCount = pauseCount,
                FillerWordCount = fillerWordCount,
                Comments = BuildComments(paceWpm, pauseCount, fillerWordCount)
            };
        }

        private static int CountWords(string text) =>
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        private static int CountLongPauses(IReadOnlyList<TranscribedWord> words)
        {
            var count = 0;
            for (var i = 1; i < words.Count; i++)
            {
                if (words[i].Start - words[i - 1].End > LongPauseThresholdSeconds)
                {
                    count++;
                }
            }
            return count;
        }

        private static int CountFillerWords(string text)
        {
            var count = 0;
            foreach (var filler in FillerWords)
            {
                count += Regex.Matches(text, $@"\b{Regex.Escape(filler)}\b", RegexOptions.IgnoreCase).Count;
            }
            return count;
        }

        private static List<string> BuildComments(double paceWpm, int pauseCount, int fillerWordCount)
        {
            var comments = new List<string>
            {
                paceWpm switch
                {
                    < SlowPaceWpmThreshold => "語速有點慢，可以試著說快一點，讓對話更流暢",
                    > FastPaceWpmThreshold => "說話有點快，可以放慢一點，讓聽者更容易跟上",
                    _ => "語速適中"
                }
            };

            comments.Add(pauseCount > 0
                ? $"有 {pauseCount} 次明顯停頓，可以試著先想好整句再開口"
                : "沒有明顯的長停頓");

            comments.Add(fillerWordCount > 0
                ? $"出現了 {fillerWordCount} 次贅字（um/like 等），可以練習減少使用"
                : "沒有明顯的贅字");

            return comments;
        }
    }
}
