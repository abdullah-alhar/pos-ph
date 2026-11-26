using System.Speech.Recognition;
using System.Speech.Synthesis;

namespace VetPos.App.Features.Assistant;

internal static class AssistantSpeech
{
    public static async Task<string?> ListenAsync(CancellationToken ct)
    {
        using var engine = new SpeechRecognitionEngine
        {
            InitialSilenceTimeout = TimeSpan.FromSeconds(6),
            BabbleTimeout = TimeSpan.FromSeconds(8),
        };
        engine.SetInputToDefaultAudioDevice();
        engine.LoadGrammar(new DictationGrammar());
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.RecognizeCompleted += (_, e) =>
        {
            if (e.Error is { } error) completion.TrySetException(error);
            else completion.TrySetResult(e.Cancelled ? null : e.Result?.Text);
        };

        engine.RecognizeAsync(RecognizeMode.Single);
        using var registration = ct.Register(engine.RecognizeAsyncCancel);
        try
        {
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(25), ct);
        }
        finally
        {
            if (!completion.Task.IsCompleted && !ct.IsCancellationRequested) engine.RecognizeAsyncCancel();
        }
    }

    public static async Task ReadAsync(string text, CancellationToken ct)
    {
        using var speaker = new SpeechSynthesizer();
        speaker.SetOutputToDefaultAudioDevice();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        speaker.SpeakCompleted += (_, e) =>
        {
            if (e.Error is { } error) completion.TrySetException(error);
            else completion.TrySetResult();
        };
        speaker.SpeakAsync(text);
        using var registration = ct.Register(speaker.SpeakAsyncCancelAll);
        await completion.Task.WaitAsync(ct);
    }
}
