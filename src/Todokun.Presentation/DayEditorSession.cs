using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>1 日分の編集の状態（計画 11.4）。共通の状態機械は <see cref="DocumentEditorSession"/>。</summary>
public sealed partial class DayEditorSession : DocumentEditorSession
{
    private static readonly string[] Weekdays = ["日", "月", "火", "水", "木", "金", "土"];

    private readonly DayStore _store;
    private readonly DayIndex _index;

    [ObservableProperty]
    private DateOnly _day;

    public DayEditorSession(DayStore store, DayIndex index, IUiServices ui, Func<(bool CanWrite, string? Reason)>? writable = null)
        : base(ui, writable)
    {
        _store = store;
        _index = index;
    }

    public string DayText => $"{Day:yyyy-MM-dd} ({Weekdays[(int)Day.DayOfWeek]})";
    public override string HeaderText => DayText;

    partial void OnDayChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(DayText));
        OnPropertyChanged(nameof(HeaderText));
    }

    protected override DayContent StoreRead() => _store.Read(Day);
    protected override bool StoreExists() => _store.Exists(Day);
    protected override void StoreWrite(string text) => _store.Write(Day, text);
    protected override void StoreDelete() => _store.Delete(Day);
    protected override void AfterWritten() => _index.Add(Day);
    protected override void AfterDeleted() => _index.Remove(Day);
    protected override string UnreadableMessage(string reason) => $"この日のファイルは読めないため、編集できません：{reason}";
    protected override string AskSaveText => "この日のメモが保存されていません。保存しますか？";
    protected override string ConfirmDeleteText => "この日のファイルを削除します。よろしいですか？";

    // ── 日を開く（View） ──

    /// <summary>その日を読んで、ビューモードで開く。呼ぶ前に、編集中の確認は済んでいること（<see cref="TryOpenDayAsync"/>）。</summary>
    public void Open(DateOnly day)
    {
        _generation++;
        Day = day;
        ShowContent(_store.Read(day));
    }

    /// <summary>
    /// 別の日を開く（要求 → 確認 → 確定の「確認」と「開く」）。編集中で未保存なら「保存しますか？」を出す。
    /// 開けたら true（呼び出し側は選択を確定する）。確認中・キャンセル・保存の失敗なら false。
    /// </summary>
    public async Task<bool> TryOpenDayAsync(DateOnly day)
    {
        if (IsConfirming) return false;
        if (!await LeaveEditAsync()) return false;
        Open(day);
        return true;
    }

    // ── プレビューに出すもの ──

    public override PreviewSource Preview => Content switch
    {
        DayContent.Loaded l => new PreviewSource.Markdown(l.Text, Path.GetDirectoryName(_store.PathOf(Day))!, _store.PathOf(Day),
            Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md"),
        DayContent.Failed f => new PreviewSource.Message($"この日のファイルを読めません：{f.Reason}"),
        _ => new PreviewSource.Message("この日のTODOはまだありません。『編集』で書けます"),
    };
}
