namespace JackyAIApp.Server.DTO
{
    public class ReviewWordItem
    {
        public int UserWordId { get; set; }
        public required string WordText { get; set; }
        public required string KKPhonics { get; set; }
        public required List<WordMeaning> Meanings { get; set; }
        public int ReviewCount { get; set; }
        public int ConsecutiveCorrect { get; set; }
    }

    public class DueReviewsResponse
    {
        public required List<ReviewWordItem> DueWords { get; set; }
        public int TotalDueCount { get; set; }
    }

    public class ReviewAnswerRequest
    {
        /// <summary>
        /// The UserWord ID being reviewed.
        /// </summary>
        public int UserWordId { get; set; }

        /// <summary>
        /// SM-2 quality rating: 0-5.
        /// 0-2 = incorrect (reset), 3 = correct with difficulty, 4 = correct, 5 = easy.
        /// For MCQ: wrong = 1, correct = 4.
        /// </summary>
        public int Quality { get; set; }
    }

    public class ReviewSubmitRequest
    {
        public required List<ReviewAnswerRequest> Reviews { get; set; }
    }

    public class ReviewSubmitResponse
    {
        public int WordsReviewed { get; set; }
        public int CorrectCount { get; set; }
        public int XPEarned { get; set; }
    }
}
