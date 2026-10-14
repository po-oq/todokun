using Todokun.Presentation;

namespace Todokun.Tests;

/// <summary>ダイアログの答えを決めておく作り物。出された回数も数える。</summary>
public sealed class FakeUiServices : IUiServices
{
    public SaveChoice SaveAnswer { get; set; } = SaveChoice.Save;
    public bool DeleteAnswer { get; set; } = true;
    public int AskSaveCount { get; private set; }
    public int ConfirmDeleteCount { get; private set; }
    public List<string> Opened { get; } = [];
    public List<string> Revealed { get; } = [];

    /// <summary>設定すると、ダイアログが出ている間（答える前）に呼ばれる。</summary>
    public Action? WhileAsking { get; set; }

    public string? LastAskSaveMessage { get; private set; }
    public string? LastConfirmDeleteMessage { get; private set; }

    public Task<SaveChoice> AskSaveAsync(string message)
    {
        AskSaveCount++;
        LastAskSaveMessage = message;
        WhileAsking?.Invoke();
        return Task.FromResult(SaveAnswer);
    }

    public Task<bool> ConfirmDeleteAsync(string message)
    {
        ConfirmDeleteCount++;
        LastConfirmDeleteMessage = message;
        WhileAsking?.Invoke();
        return Task.FromResult(DeleteAnswer);
    }

    public void OpenWithDefaultApp(string target) => Opened.Add(target);

    public void RevealInFileManager(string file) => Revealed.Add(file);
}
