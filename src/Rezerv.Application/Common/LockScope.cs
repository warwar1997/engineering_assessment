namespace Rezerv.Application.Common;

/// <summary>Collects lock handles acquired during an operation and releases them in reverse order.</summary>
internal sealed class LockScope : IAsyncDisposable
{
    private readonly Stack<IAsyncDisposable> _handles = new();

    public void Add(IAsyncDisposable handle) => _handles.Push(handle);

    public async ValueTask DisposeAsync()
    {
        while (_handles.TryPop(out var handle))
            await handle.DisposeAsync();
    }
}
