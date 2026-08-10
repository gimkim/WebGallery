namespace WebGallery.Services;

public sealed class ThumbnailWorkQueue : BackgroundService
{
    public const int MaximumPendingJobs = 256;

    private readonly object _sync = new();
    private readonly Queue<IWorkItem> _visible = new();
    private readonly Queue<IWorkItem> _normal = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ThumbnailQueueSettings _settings;
    private readonly Dictionary<long, Task> _running = [];
    private int _activeCount;
    private long _nextRunId;

    public ThumbnailWorkQueue(ThumbnailQueueSettings settings)
    {
        _settings = settings;
    }

    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> operation, ThumbnailPriority priority, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = new WorkItem<T>(operation, cancellationToken, WakeDispatcher);
        lock (_sync)
        {
            PruneCanceled(_visible);
            PruneCanceled(_normal);
            if (_visible.Count + _normal.Count >= MaximumPendingJobs)
            {
                item.Dispose();
                throw new ThumbnailQueueFullException();
            }
            (priority == ThumbnailPriority.Visible ? _visible : _normal).Enqueue(item);
        }
        _signal.Release();
        return item.Task;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _settings.Changed += WakeDispatcher;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _signal.WaitAsync(stoppingToken);
                DispatchAvailable(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            _settings.Changed -= WakeDispatcher;
            List<Task> running;
            lock (_sync)
            {
                while (_visible.TryDequeue(out var item)) { item.Cancel(stoppingToken); item.Dispose(); }
                while (_normal.TryDequeue(out var item)) { item.Cancel(stoppingToken); item.Dispose(); }
                running = _running.Values.ToList();
            }
            if (running.Count > 0)
            {
                try { await Task.WhenAll(running); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            }
        }
    }

    private void DispatchAvailable(CancellationToken stoppingToken)
    {
        while (true)
        {
            IWorkItem? item;
            long runId;
            lock (_sync)
            {
                PruneCanceled(_visible);
                PruneCanceled(_normal);
                if (_activeCount >= _settings.MaxConcurrency) return;
                item = DequeueNext();
                if (item is null) return;
                _activeCount++;
                runId = ++_nextRunId;
                var running = Task.Run(() => RunItemAsync(runId, item, stoppingToken), CancellationToken.None);
                _running.Add(runId, running);
            }
        }
    }

    private IWorkItem? DequeueNext()
    {
        while (_visible.Count > 0)
        {
            var item = _visible.Dequeue();
            if (!item.IsCanceled) return item;
            item.Cancel(CancellationToken.None);
            item.Dispose();
        }
        while (_normal.Count > 0)
        {
            var item = _normal.Dequeue();
            if (!item.IsCanceled) return item;
            item.Cancel(CancellationToken.None);
            item.Dispose();
        }
        return null;
    }

    private static void PruneCanceled(Queue<IWorkItem> queue)
    {
        var count = queue.Count;
        for (var index = 0; index < count; index++)
        {
            var item = queue.Dequeue();
            if (item.IsCanceled)
            {
                item.Cancel(CancellationToken.None);
                item.Dispose();
            }
            else
            {
                queue.Enqueue(item);
            }
        }
    }

    private async Task RunItemAsync(long runId, IWorkItem item, CancellationToken stoppingToken)
    {
        try
        {
            await item.RunAsync(stoppingToken);
        }
        finally
        {
            item.Dispose();
            lock (_sync)
            {
                _running.Remove(runId);
                _activeCount--;
            }
            WakeDispatcher();
        }
    }

    private void WakeDispatcher()
    {
        try { _signal.Release(); }
        catch (ObjectDisposedException) { }
    }

    private interface IWorkItem : IDisposable
    {
        bool IsCanceled { get; }
        Task RunAsync(CancellationToken stoppingToken);
        void Cancel(CancellationToken cancellationToken);
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private readonly Func<CancellationToken, Task<T>> _operation;
        private readonly CancellationToken _requestToken;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _requestRegistration;

        public WorkItem(Func<CancellationToken, Task<T>> operation, CancellationToken requestToken, Action cancellationWake)
        {
            _operation = operation;
            _requestToken = requestToken;
            _requestRegistration = requestToken.Register(cancellationWake);
        }

        public Task<T> Task => _completion.Task;
        public bool IsCanceled => _requestToken.IsCancellationRequested;

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            if (_completion.Task.IsCompleted) return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_requestToken, stoppingToken);
            try
            {
                var result = await _operation(linked.Token);
                _completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                _completion.TrySetCanceled(linked.Token);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        public void Cancel(CancellationToken cancellationToken) => _completion.TrySetCanceled(cancellationToken);
        public void Dispose() => _requestRegistration.Dispose();
    }
}
