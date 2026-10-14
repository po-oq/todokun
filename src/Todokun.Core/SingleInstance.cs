using System.Security.Cryptography;
using System.Text;

namespace Todokun.Core;

public enum SingleInstanceStatus
{
    Acquired,
    /// <summary>同じデータルートで、すでに起動している。</summary>
    AlreadyRunning,
    /// <summary>Mutex を使えなかった（確認を諦めて起動を続ける。ログに残す）。</summary>
    Unavailable,
}

/// <summary>データルートごとの名前付き Mutex（要件 3 章、計画 1-6）。ロックファイルは使わない。</summary>
public sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;

    private SingleInstance(SingleInstanceStatus status, Mutex? mutex, string? error)
    {
        Status = status;
        _mutex = mutex;
        Error = error;
    }

    public SingleInstanceStatus Status { get; }
    public string? Error { get; }

    /// <summary>名前：<c>todokun-</c> ＋ フルパス（小文字・末尾の区切りなし）の SHA-256 先頭 16 桁。</summary>
    public static string NameFor(string dataRoot)
    {
        var full = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        return "todokun-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16].ToLowerInvariant();
    }

    public static SingleInstance TryAcquire(string dataRoot)
    {
        try
        {
            var mutex = new Mutex(true, NameFor(dataRoot), out var createdNew);
            if (createdNew) return new SingleInstance(SingleInstanceStatus.Acquired, mutex, null);
            mutex.Dispose();
            return new SingleInstance(SingleInstanceStatus.AlreadyRunning, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException or PlatformNotSupportedException)
        {
            return new SingleInstance(SingleInstanceStatus.Unavailable, null, ex.Message);
        }
    }

    public void Dispose()
    {
        var m = Interlocked.Exchange(ref _mutex, null);
        if (m is null) return;
        try { m.ReleaseMutex(); } catch (ApplicationException) { /* 別スレッドが所有者 */ }
        m.Dispose();
    }
}
