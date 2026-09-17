namespace WebGallery.Services;

public sealed class ThumbnailQueueSettings
{
    public const int MinimumConcurrency = 1;
    public const int MaximumConcurrency = 16;
    public const int DefaultConcurrency = 2;

    private int _maxConcurrency;
    private int _reducedJpeg;
    private int _backgroundWorkers;
    public int BackgroundWorkers => Volatile.Read(ref _backgroundWorkers);
    public Func<int>? BackgroundWorkersProvider { get; set; }
    public int EffectiveBackgroundWorkers => BackgroundWorkersProvider?.Invoke() ?? BackgroundWorkers;
    public void SetBackgroundWorkers(int value)
    {
        var normalized = Math.Clamp(value, 0, MaximumConcurrency);
        if (Interlocked.Exchange(ref _backgroundWorkers, normalized) != normalized) Changed?.Invoke();
    }
    public bool ReducedJpeg => Volatile.Read(ref _reducedJpeg) != 0;
    public Func<bool>? DecodeProvider { get; set; }
    public string CacheVersion => (DecodeProvider?.Invoke() ?? ReducedJpeg) ? "contain-idct-v1" : "contain-v1";
    public void NotifyChanged() => Changed?.Invoke();
    public void SetReducedJpeg(bool enabled) {
        if (Interlocked.Exchange(ref _reducedJpeg,enabled ? 1 : 0) != (enabled ? 1 : 0)) NotifyChanged();
    }

    public ThumbnailQueueSettings(int initialConcurrency = DefaultConcurrency)
    {
        _maxConcurrency = Clamp(initialConcurrency);
    }

    public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

    public event Action? Changed;

    public void Update(int value)
    {
        var normalized = Clamp(value);
        if (Interlocked.Exchange(ref _maxConcurrency, normalized) != normalized)
            Changed?.Invoke();
    }

    public static int Clamp(int value) => Math.Clamp(value, MinimumConcurrency, MaximumConcurrency);
}

public enum ThumbnailPriority
{
    Normal,
    Visible,
    Background
}

public sealed class ThumbnailQueueFullException : Exception
{
    public ThumbnailQueueFullException() : base("The thumbnail queue is full.") { }
}
