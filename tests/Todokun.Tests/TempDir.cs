namespace Todokun.Tests;

/// <summary>一時フォルダ（本物のデータに触れない）。</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "todokun-test-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                // 書込不可にしたフォルダ・ファイルを戻してから消す
                foreach (var d in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories).Append(Path))
                    File.SetUnixFileMode(d, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Directory.Delete(Path, true);
        }
        catch { }
    }
}
