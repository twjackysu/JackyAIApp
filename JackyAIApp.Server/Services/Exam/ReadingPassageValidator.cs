using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// Checks a freshly generated reading passage before it is written to the bank.
    ///
    /// Why this is worth a validator rather than a stronger prompt: the passage bank is global and
    /// permanent. <c>ReadingPassages</c> is shared by every user and reused — below
    /// <c>ReadingBankGrowthThreshold</c> unseen rows the app generates each time, above it roughly
    /// four requests in five are served from the bank — so a single bad generation is not a bad
    /// answer once, it is a bad answer handed to everyone from then on. The prompt is a request;
    /// this is the gate.
    ///
    /// The observed failure was a passage that came back entirely in Chinese, which defeats the
    /// point of a reading exercise and passed the existing checks because those only counted
    /// questions and options.
    /// </summary>
    public static class ReadingPassageValidator
    {
        /// <summary>Human-readable reason the passage was rejected, or null when it is usable.</summary>
        public static string? Validate(ReadingGenerationResult dto)
        {
            if (dto.Questions.Count is < 3 or > 4)
            {
                return $"expected 3-4 questions, got {dto.Questions.Count}";
            }

            foreach (var q in dto.Questions)
            {
                if (q.Options.Count is < 3 or > 4)
                {
                    return $"expected 3-4 options per question, got {q.Options.Count}";
                }
                if (q.Options.Count(o => o.IsCorrect) != 1)
                {
                    return $"expected exactly 1 correct option, got {q.Options.Count(o => o.IsCorrect)}";
                }
            }

            return ValidateLanguage(dto);
        }

        /// <summary>
        /// The learner-facing English must be English and the explanations must be Chinese — the
        /// split the prompt asks for. Checked separately from shape so the log says which one
        /// failed; a model that ignores the language rule usually gets the shape right.
        /// </summary>
        private static string? ValidateLanguage(ReadingGenerationResult dto)
        {
            // Anything a learner reads to answer the question has to be in the language being
            // tested. A single CJK character is enough to fail it: correct English output has
            // none, so there is no threshold to tune and no false positive to trade off.
            if (ContainsCjk(dto.Title)) return "title is not in English";
            if (ContainsCjk(dto.PassageText)) return "passage text is not in English";

            for (var i = 0; i < dto.Questions.Count; i++)
            {
                var q = dto.Questions[i];
                if (ContainsCjk(q.QuestionText)) return $"question {i + 1} is not in English";

                for (var j = 0; j < q.Options.Count; j++)
                {
                    if (ContainsCjk(q.Options[j].Text))
                    {
                        return $"question {i + 1} option {j + 1} is not in English";
                    }

                    // The reverse direction: explanations are the one field that must be Chinese,
                    // and an all-English explanation is the same prompt slip pointing the other
                    // way. Only "contains some Chinese" rather than "is entirely Chinese", because
                    // a good explanation quotes the passage ("文章說「tired but glad」...").
                    if (!ContainsCjk(q.Options[j].Explanation))
                    {
                        return $"question {i + 1} option {j + 1} explanation is not in Chinese";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// True if the text holds a Chinese character. Covers the CJK ideograph blocks plus CJK
        /// and fullwidth punctuation, which is what a Chinese sentence is written with; Latin
        /// text never reaches these ranges.
        /// </summary>
        private static bool ContainsCjk(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            foreach (var c in text)
            {
                if (c is >= '一' and <= '鿿') return true;   // CJK Unified Ideographs
                if (c is >= '㐀' and <= '䶿') return true;   // ... Extension A
                if (c is >= '豈' and <= '﫿') return true;   // Compatibility Ideographs
                if (c is >= '　' and <= '〿') return true;   // CJK punctuation（「」、。）
                if (c is >= '＀' and <= '￯') return true;   // Fullwidth forms（，！？）
            }

            return false;
        }
    }
}
