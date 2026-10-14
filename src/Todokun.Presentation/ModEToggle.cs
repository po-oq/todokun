namespace Todokun.Presentation;

/// <summary>
/// Ctrl/Cmd+E の「入る／戻る」（要件 13.2・計画 11.6）。ビューでは常に「編集に入る」（冪等）。編集中は、
/// 編集に入った直後（<see cref="WindowMs"/> 以内）の再入力を、同じ押下の二重到達として戻さない。
/// </summary>
public sealed class ModEToggle(DocumentEditorSession session, Func<long> nowMs)
{
    /// <summary>別の経路から同じキーが届いたとみなす間（ms）。P0：2 経路の遅れは 0.2〜0.5ms（最初の数回は 5〜34ms）。</summary>
    public const long WindowMs = 50;

    private long _beganAt = long.MinValue;

    /// <summary>ページ（WebView）経路の BeginEdit。ビューのときだけ効く。</summary>
    public void OnPageBeginEdit()
    {
        if (session.IsViewing && session.BeginEdit()) _beganAt = nowMs();
    }

    /// <summary>ウィンドウ経路の Mod+E。処理したら true（画面は <c>Handled</c> にする）。確認中は false。</summary>
    public bool OnModE()
    {
        if (session.IsConfirming) return false;
        if (session.IsViewing)
        {
            if (session.BeginEdit()) _beganAt = nowMs();
            return true;
        }
        if (nowMs() - _beganAt < WindowMs) return true;
        _ = session.LeaveEditAsync();
        return true;
    }
}
