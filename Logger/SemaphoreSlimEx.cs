using System;
using System.Threading;

namespace Logger;

/// <summary>
/// Provides a way to determine if a <see cref="System.Threading.SemaphoreSlim"/> has been disposed.
/// Ironically the <see cref="System.Threading.SemaphoreSlim"/> does contain a CheckDisposed() method, but it's private.
/// https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim?view=netframework-4.8.1
/// </summary>
public class SemaphoreSlimEx : SemaphoreSlim
{
    public bool IsDisposed { get; internal set; }
    public SemaphoreSlimEx(int initialCount) : base(initialCount) { }
    public SemaphoreSlimEx(int initialCount, int maxCount) : base(initialCount, maxCount) { }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        IsDisposed = true;
    }
}
