namespace WebGallery.Services;

public sealed class ThumbnailQueueSettings
{
    public const int MinimumConcurrency = 1;
    public const int MaximumConcurrency = 16;
    public const int DefaultConcurrency = 2;

    private int _maxConcurrency;

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
    Visible
}

public sealed class ThumbnailQueueFullException : Exception
{
    public ThumbnailQueueFullException() : base("The thumbnail queue is full.") { }
}
