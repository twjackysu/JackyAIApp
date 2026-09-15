namespace JackyAIApp.Server.Services.OpenAI
{
    /// <summary>
    /// A single word from Whisper's verbose_json word-level timestamps, in seconds relative to the
    /// start of the clip.
    /// </summary>
    public record TranscribedWord(string Word, double Start, double End);

    /// <summary>
    /// Result of a Whisper transcription, including the richer verbose_json data (duration, word
    /// timestamps) needed to compute delivery/fluency feedback without a second AI call.
    /// </summary>
    public record AudioTranscriptionResult(string Text, double DurationSeconds, IReadOnlyList<TranscribedWord> Words);
}
