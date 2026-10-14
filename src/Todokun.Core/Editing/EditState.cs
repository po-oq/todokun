namespace Todokun.Core.Editing;

/// <summary>編集補助の入力（計画 P2）。選択が無いとき <see cref="SelStart"/> と <see cref="SelEnd"/> は等しい。</summary>
public sealed record EditState(string Text, int Caret, int SelStart, int SelEnd)
{
    public static EditState At(string text, int caret) => new(text, caret, caret, caret);

    public int SelLow => Math.Min(SelStart, SelEnd);
    public int SelHigh => Math.Max(SelStart, SelEnd);
    public bool HasSelection => SelStart != SelEnd;
}

/// <summary>
/// 連続した 1 か所の置換と、置換後のカーソル・選択（置換後のテキストでの位置）。
/// 画面は <c>SelectionStart/End</c> を <see cref="Start"/>〜<see cref="Start"/>+<see cref="Length"/> に合わせて
/// <c>SelectedText</c> に <see cref="Replacement"/> を代入する（Undo が 1 操作で戻る。計画 11.5）。
/// </summary>
public sealed record EditResult(int Start, int Length, string Replacement, int Caret, int SelStart, int SelEnd)
{
    public string Apply(string text) => string.Concat(text.AsSpan(0, Start), Replacement, text.AsSpan(Start + Length));
}
