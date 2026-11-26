using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VetPos.AI;
using VetPos.App.Infrastructure;
using VetPos.Core.Rules;

namespace VetPos.App.Features.Assistant;

public sealed record ChatMessage(bool FromUser, string Text, string? ToolsUsed = null, bool IsError = false, IReadOnlyList<AiLink>? Links = null,
    IReadOnlyList<ActionCard>? Actions = null);

/// <summary>A change the AI prepared, shown as a card with Confirm / Cancel. Nothing is saved before Confirm.</summary>
public sealed partial class ActionCard(AiAction action) : ObservableObject
{
    public AiAction Action { get; } = action;
    public string Title => Action.Title;
    public IReadOnlyList<string> Lines => Action.Lines;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsWaiting))] private string _state = "waiting"; // waiting · busy · done · cancelled · failed
    [ObservableProperty] private string? _result;

    public bool IsWaiting => State == "waiting";
}

/// <summary>Chat with the shop's data. Runs in the background; billing never waits for it.</summary>
public sealed partial class AssistantViewModel(Ui ui, AssistantService assistant, AssistantActions actions) : PageViewModel(ui)
{
    /// <summary>Saves a change the AI prepared (owner only; checked again when saving).</summary>
    [RelayCommand]
    private async Task ConfirmAction(ActionCard? card)
    {
        if (card is not { IsWaiting: true }) return;
        card.State = "busy";
        try
        {
            var actor = Ui.Session.Actor;
            card.Result = await Task.Run(() => actions.ExecuteAsync(card.Action, actor));
            if (!Ui.Session.IsSignedIn || Ui.Session.Actor != actor) return;
            card.State = "done";
            Ui.Toasts.Show(card.Result);
            ChangesSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (PosException ex)
        {
            card.Result = ex.Message;
            card.State = "failed";
        }
        catch (Exception ex)
        {
            var reference = ErrorHandling.NewReference();
            Microsoft.Extensions.Logging.LoggerExtensions.LogError(Ui.Logger, ex, "AI change failed {Reference}", reference);
            card.Result = (ErrorHandling.Describe(ex) ?? "That could not be saved.") + $" (ref {reference})";
            card.State = "failed";
        }
    }

    /// <summary>The drawer puts the cursor in the question box (after "Add these products: ").</summary>
    public event EventHandler? FocusRequested;

    public void RequestFocus() => FocusRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CancelAction(ActionCard? card)
    {
        if (card is not { IsWaiting: true } || IsThinking) return;
        card.State = "cancelled";
        card.Result = "Cancelled. Nothing was changed.";
    }

    /// <summary>A confirmed change was saved: open screens can reload.</summary>
    public event EventHandler? ChangesSaved;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    [ObservableProperty] private string _input = "";
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private string? _offline;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MicrophoneLabel))] private bool _isListening;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ReadAnswerLabel))] private bool _isSpeaking;
    [ObservableProperty] private string? _voiceStatus;
    private CancellationTokenSource? _voice;

    public string MicrophoneLabel => IsListening ? "Stop mic" : "Speak";
    public string ReadAnswerLabel => IsSpeaking ? "Stop reading" : "Read answer";

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Dictate()
    {
        if (IsListening) { StopVoice(); return; }
        if (IsSpeaking || IsThinking) return;
        await RunVoiceAsync(listening: true, async ct =>
        {
            var text = await AssistantSpeech.ListenAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text))
            {
                VoiceStatus = "No speech was recognised. Try again or type your question.";
                return;
            }
            Input = string.IsNullOrWhiteSpace(Input) ? text : $"{Input.TrimEnd()} {text}";
            VoiceStatus = "Check the words, then press Ask.";
            RequestFocus();
        });
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ReadAnswer()
    {
        if (IsSpeaking) { StopVoice(); return; }
        if (IsListening) return;
        var reply = Messages.LastOrDefault(message => !message.FromUser && !message.IsError);
        if (reply is null)
        {
            VoiceStatus = "Ask a question first, then use Read answer.";
            return;
        }
        await RunVoiceAsync(listening: false, ct => AssistantSpeech.ReadAsync(reply.Text, ct));
    }

    private async Task RunVoiceAsync(bool listening, Func<CancellationToken, Task> action)
    {
        using var operation = new CancellationTokenSource();
        _voice = operation;
        IsListening = listening;
        IsSpeaking = !listening;
        VoiceStatus = listening ? "Listening. Say your question." : "Reading the answer…";
        try
        {
            await action(operation.Token);
            if (!listening) VoiceStatus = null;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            VoiceStatus = null;
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(Ui.Logger, ex, "Windows speech failed");
            VoiceStatus = listening
                ? "Windows could not hear you. Check the microphone and installed speech language, or type your question."
                : "Windows could not read the answer aloud. Check your speaker and installed voice.";
        }
        finally
        {
            _voice = null;
            IsListening = IsSpeaking = false;
        }
    }

    public void StopVoice() => _voice?.Cancel();

    public IReadOnlyList<string> Suggestions => Ui.Session.IsOwner
        ?
        [
            "How much did we sell today?", "What is running low?",
            "Top 5 products this month", "What was the profit this month?", "Stock and price of Royal Canin Kitten",
        ]
        : ["How much did we sell today?", "What is running low?", "Top 5 products this month"];

    public bool IsEmpty => Messages.Count == 0;

    public bool IsOwner => Ui.Session.IsOwner;

    [RelayCommand]
    private void OpenAiSettings() => NavigateRequested?.Invoke(this, "settings:ai");

    /// <summary>A link under an answer was clicked: the shell opens that screen (the chat stays open).</summary>
    public event EventHandler<string>? NavigateRequested;

    [RelayCommand]
    private void OpenLink(AiLink? link)
    {
        if (link is not null) NavigateRequested?.Invoke(this, link.Target);
    }

    public override async Task OnShownAsync() => Offline = await assistant.ProblemAsync();

    /// <summary>Settings can change while the panel is open: check again (panel opened, Ask pressed).</summary>
    public async Task RefreshAsync() => Offline = await assistant.ProblemAsync();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Ask(string? question, CancellationToken ct)
    {
        question = (question ?? Input).Trim();
        if (question.Length == 0 || IsThinking || IsListening) return;
        StopVoice();
        VoiceStatus = null;
        var actor = Ui.Session.Actor;
        ChatMessage? pending = null;
        IsThinking = true;
        try
        {
            Offline = await assistant.ProblemAsync(ct);
            ct.ThrowIfCancellationRequested();
            var history = Messages.Where(m => !m.IsError).TakeLast(10).Select(m => new ChatTurn(m.FromUser, m.Text)).ToList();
            pending = new ChatMessage(true, question);
            Messages.Add(pending);
            OnPropertyChanged(nameof(IsEmpty));
            Input = "";
            if (Offline is not null)
            {
                Messages.Add(new ChatMessage(false, Offline + (Ui.Session.IsOwner ? "" : " Ask the owner."), IsError: true,
                    Links: Ui.Session.IsOwner ? [new AiLink("Open AI settings", "settings:ai")] : null));
                return;
            }

            var answer = await Task.Run(() => assistant.AskAsync(question, history, actor, ct), ct);
            ct.ThrowIfCancellationRequested();
            var tools = answer.Tools.Count == 0 ? null : "Looked at: " + string.Join(", ", answer.Tools.Select(t => t.Name.Replace('_', ' ')).Distinct());
            var cards = answer.Actions is { Count: > 0 } list ? list.Select(a => new ActionCard(a)).ToList() : null;
            Messages.Add(new ChatMessage(false, Plain(answer.Text), tools, Links: answer.Links, Actions: cards));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (pending is not null && Messages.Remove(pending) && Input.Length == 0) Input = question;
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (AiUnavailableException ex)
        {
            Messages.Add(new ChatMessage(false, ex.Message, IsError: true,
                Links: Ui.Session.IsOwner && ex.Message.Contains("Settings", StringComparison.Ordinal) ? [new AiLink("Open AI settings", "settings:ai")] : null));
        }
        catch (PosException ex)
        {
            Messages.Add(new ChatMessage(false, ex.Message, IsError: true));
        }
        catch (Exception ex)
        {
            var reference = ErrorHandling.NewReference();
            Microsoft.Extensions.Logging.LoggerExtensions.LogError(Ui.Logger, ex, "Assistant failed {Reference}", reference);
            Messages.Add(new ChatMessage(false, $"Something went wrong ({reference}). Try again.", IsError: true));
        }
        finally
        {
            IsThinking = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Models like to add markdown (**bold**, # headings); the chat shows plain text.</summary>
    internal static string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(
        text.Replace("**", "", StringComparison.Ordinal).Replace("__", "", StringComparison.Ordinal),
        @"^#{1,6}\s*|^\s*\*\s+", m => m.Value.TrimStart().StartsWith('*') ? "• " : "", System.Text.RegularExpressions.RegexOptions.Multiline).Trim();

    [RelayCommand]
    private void Clear()
    {
        AskCancelCommand.Execute(null);
        StopVoice();
        VoiceStatus = null;
        Input = "";
        Messages.Clear();
        OnPropertyChanged(nameof(IsEmpty));
    }
}
