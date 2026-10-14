namespace Todokun.Core;

/// <summary>起動引数・環境変数から決まる設定（要件 3 章）。</summary>
public sealed record LaunchOptions(string? DataDir)
{
    public const string EnvName = "TODOKUN_DATA_DIR";

    /// <summary>
    /// <c>--data-dir &lt;p&gt;</c>・<c>--data-dir=&lt;p&gt;</c> を探す。値が無い・空のときは無かったものとして扱う。
    /// 引数が無ければ環境変数 <c>TODOKUN_DATA_DIR</c>。引数が優先。
    /// </summary>
    public static LaunchOptions Parse(IReadOnlyList<string> args, Func<string, string?> getEnv)
    {
        string? fromArg = null;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a == "--data-dir")
            {
                if (i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]))
                    fromArg = args[i + 1];
                i++;
            }
            else if (a.StartsWith("--data-dir=", StringComparison.Ordinal))
            {
                var v = a["--data-dir=".Length..];
                if (!string.IsNullOrWhiteSpace(v)) fromArg = v;
            }
        }
        if (fromArg is not null) return new LaunchOptions(fromArg);
        var env = getEnv(EnvName);
        return new LaunchOptions(string.IsNullOrWhiteSpace(env) ? null : env);
    }
}
