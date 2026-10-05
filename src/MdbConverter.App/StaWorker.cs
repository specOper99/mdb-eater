using System.Collections.Concurrent;

namespace MdbConverter.App;

internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "MDB-STA"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public Task RunAsync(Action work) => RunAsync(() =>
    {
        work();
        return 0;
    });

    private void Loop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(8));
        _queue.Dispose();
    }
}
