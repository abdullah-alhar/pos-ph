using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VetPos.AI;
using VetPos.App.DesignSystem.Controls;
using VetPos.Core.Rules;

namespace VetPos.App.Infrastructure;

/// <summary>What every screen needs: dialogs, toasts, the signed-in person and a logger.</summary>
public sealed class Ui(IDialogService dialogs, IToastService toasts, Session session, ILogger<Ui> logger)
{
    public IDialogService Dialogs { get; } = dialogs;
    public IToastService Toasts { get; } = toasts;
    public Session Session { get; } = session;
    public ILogger Logger { get; } = logger;

    /// <summary>Any screen can open the AI chat (Ctrl+J drawer), optionally with text already typed.</summary>
    public event EventHandler<string?>? AskAiRequested;

    public void AskAi(string? prefill = null) => AskAiRequested?.Invoke(this, prefill);

    /// <summary>
    /// Runs an action and turns problems into calm messages: a cashier-fixable problem becomes a
    /// toast with its own words; anything unexpected is logged with a reference number.
    /// Returns false when it did not complete.
    /// </summary>
    public async Task<bool> Run(Func<Task> work)
    {
        try
        {
            await work();
            return true;
        }
        catch (PosException ex)
        {
            Toasts.Show(ex.Message, ToastKind.Error);
        }
        catch (AiUnavailableException ex)
        {
            Toasts.Show(ex.Message, ToastKind.Warning);
        }
        catch (Exception ex)
        {
            var reference = ErrorHandling.NewReference();
            logger.LogError(ex, "Action failed {Reference}", reference);
            Toasts.Show(ErrorHandling.Describe(ex) is { } known
                ? $"{known} (ref {reference})"
                : $"That did not work, but your data is safe. Try again; if it repeats, tell support: {reference}.", ToastKind.Error);
        }
        return false;
    }
}

/// <summary>Base for screens: busy flag and a hook when the screen is shown.</summary>
public abstract partial class PageViewModel(Ui ui) : ObservableObject
{
    protected Ui Ui { get; } = ui;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _loadError;

    /// <summary>Called each time the page is opened (refresh lists).</summary>
    public virtual Task OnShownAsync() => Task.CompletedTask;

    /// <summary>Loads with a busy flag; a failure shows the error state instead of a blank screen.</summary>
    protected async Task LoadAsync(Func<Task> load)
    {
        IsBusy = true;
        LoadError = null;
        try
        {
            await load();
        }
        catch (Exception ex)
        {
            var reference = ErrorHandling.NewReference();
            Ui.Logger.LogError(ex, "Loading failed {Reference}", reference);
            LoadError = $"This could not be loaded ({reference}). Your data is safe.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
