namespace WebGallery.Services;

public sealed record MediaTrackDto(
    int Index, string Type, string Codec, string? Profile, int? Level,
    int? Width, int? Height, long? BitRate, double? FrameRate,
    string? PixelFormat, string? Language, string? Title,
    bool IsDefault, bool Supported);

public sealed record MediaMetadataDto(
    double? Duration,
    IReadOnlyList<MediaTrackDto> Video,
    IReadOnlyList<MediaTrackDto> Audio,
    IReadOnlyList<MediaTrackDto> Subtitles,
    long? SourceBitRate,
    bool CanStreamWithoutTranscode,
    string? StreamWarning);

public sealed record MediaSegmentResult(
    byte[] Bytes, string Mode, string OutputCodec, string? Profile,
    string? Level, string? Quality, long? TargetBitRate, long? MaxBitRate,
    double SourceStart, double PresentationLead, double NextStart);

public sealed class MediaPlaybackException : Exception
{
    public MediaPlaybackException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException) => StatusCode = statusCode;

    public int StatusCode { get; }
}
