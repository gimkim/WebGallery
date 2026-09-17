namespace WebGallery.Services;

public sealed class ThumbnailWorkQueue : BackgroundService
{
    public const int MaximumPendingJobs = 256;

    private readonly object _sync = new();
    private readonly Queue<IWorkItem> _visible = new();
    private readonly Queue<IWorkItem> _normal = new();
    private readonly Queue<IWorkItem> _background = new();
    private readonly Queue<IWorkItem> _mediumVisible = new(), _mediumNormal = new(), _mediumBackground = new();
    private int _activeSmall;
    private int _activeBackground;
    private int _foregroundReservations;
    private readonly Dictionary<long, IWorkItem> _runningBackground = [];
    public IDisposable SuspendBackground() {
        lock (_sync) {
            _foregroundReservations++;
            foreach (var item in _runningBackground.Values) item.Preempt();
        }
        return new BackgroundPause(this);
    }
    private sealed class BackgroundPause(ThumbnailWorkQueue queue) : IDisposable {
        private int disposed;
        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (queue._sync) queue._foregroundReservations--;
            queue.WakeDispatcher();
        }
    }
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ThumbnailQueueSettings _settings;
    private readonly Dictionary<long, Task> _running = [];
    private int _activeCount;
    private long _nextRunId;

    public ThumbnailWorkQueue(ThumbnailQueueSettings settings)
    {
        _settings = settings;
    }

    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> operation, ThumbnailPriority priority, CancellationToken cancellationToken, bool medium = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = new WorkItem<T>(operation, cancellationToken, WakeDispatcher, medium);
        lock (_sync)
        {
            if (priority != ThumbnailPriority.Background)
                foreach (var background in _runningBackground.Values) background.Preempt();
            PruneCanceled(_visible);
            PruneCanceled(_normal);
            PruneCanceled(_background);
            PruneCanceled(_mediumVisible); PruneCanceled(_mediumNormal); PruneCanceled(_mediumBackground);
            if (_visible.Count + _normal.Count + _background.Count + _mediumVisible.Count + _mediumNormal.Count + _mediumBackground.Count >= MaximumPendingJobs)
            {
                if (priority != ThumbnailPriority.Background && (_mediumBackground.TryDequeue(out var displaced) || _background.TryDequeue(out displaced))) {
                    displaced.RejectPreempted(); displaced.Dispose();
                } else {
                    item.Dispose();
                    throw new ThumbnailQueueFullException();
                }
            }
            (medium ? (priority == ThumbnailPriority.Visible ? _mediumVisible : priority == ThumbnailPriority.Background ? _mediumBackground : _mediumNormal)
                : (priority == ThumbnailPriority.Visible ? _visible : priority == ThumbnailPriority.Background ? _background : _normal)).Enqueue(item);
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
                while (_background.TryDequeue(out var item)) { item.Cancel(stoppingToken); item.Dispose(); }
                foreach (var queue in new[] { _mediumVisible, _mediumNormal, _mediumBackground })
                    while (queue.TryDequeue(out var item)) { item.Cancel(stoppingToken); item.Dispose(); }
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
                PruneCanceled(_background);
                PruneCanceled(_mediumVisible); PruneCanceled(_mediumNormal); PruneCanceled(_mediumBackground);
                if (_activeCount >= _settings.MaxConcurrency) return;
                item = DequeueNext();
                var background = false;
                // On-demand small, then on-demand medium, then background small/medium.
                var activeForegroundSmall = _activeSmall - _runningBackground.Values.Count(x => !x.Medium);
                if (item is null && activeForegroundSmall == 0 && _visible.Count + _normal.Count == 0) {
                    if (!_mediumVisible.TryDequeue(out item)) _mediumNormal.TryDequeue(out item);
                }
                var foregroundQueued = _visible.Count + _normal.Count + _mediumVisible.Count + _mediumNormal.Count;
                if (item is null && _foregroundReservations == 0 && foregroundQueued == 0 && _activeCount == _activeBackground && _activeBackground < _settings.BackgroundWorkers) {
                    if (!_background.TryDequeue(out item) && _activeSmall == 0) _mediumBackground.TryDequeue(out item);
                    if (item is not null) { background = true; _activeBackground++; }
                }
                if (item is null) return;
                if (!item.Medium) _activeSmall++;
                _activeCount++;
                runId = ++_nextRunId;
                if (background) _runningBackground.Add(runId, item);
                var running = Task.Run(() => RunItemAsync(runId, item, background, stoppingToken), CancellationToken.None);
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

    private async Task RunItemAsync(long runId, IWorkItem item, bool background, CancellationToken stoppingToken)
    {
        try
        {
            await item.RunAsync(stoppingToken);
        }
        finally
        {
            lock (_sync)
            {
                _running.Remove(runId);
                _runningBackground.Remove(runId);
                item.Dispose();
                _activeCount--;
                if (!item.Medium) _activeSmall--;
                if (background) _activeBackground--;
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
        bool Medium { get; }
        void Preempt();
        void RejectPreempted();
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
        private readonly CancellationTokenSource _preempt = new();
        public void Preempt() => _preempt.Cancel();
        public void RejectPreempted() => _completion.TrySetException(new BackgroundThumbnailPreemptedException());

        public bool Medium { get; }
        public WorkItem(Func<CancellationToken, Task<T>> operation, CancellationToken requestToken, Action cancellationWake, bool medium)
        {
            Medium = medium;
            _operation = operation;
            _requestToken = requestToken;
            _requestRegistration = requestToken.Register(cancellationWake);
        }

        public Task<T> Task => _completion.Task;
        public bool IsCanceled => _requestToken.IsCancellationRequested;

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            if (_completion.Task.IsCompleted) return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_requestToken, stoppingToken, _preempt.Token);
            try
            {
                var result = await _operation(linked.Token);
                _completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                if (_preempt.IsCancellationRequested && !_requestToken.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                    _completion.TrySetException(new BackgroundThumbnailPreemptedException());
                else _completion.TrySetCanceled(linked.Token);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        public void Cancel(CancellationToken cancellationToken) => _completion.TrySetCanceled(cancellationToken);
        public void Dispose() { _requestRegistration.Dispose(); _preempt.Dispose(); }
    }
}
public sealed class BackgroundThumbnailPreemptedException : Exception;
