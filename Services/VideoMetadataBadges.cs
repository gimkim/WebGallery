using System.Globalization;
using System.Text.RegularExpressions;

namespace WebGallery.Services;

public sealed record VideoMetadataBadge(string Key, string Label, bool Verified);

public static class VideoMetadataBadges
{
    private static readonly Regex SeasonEpisodePattern = new(
        @"(?<![A-Z0-9])S(?<season>\d{1,2})[ ._-]*E(?<episode>\d{1,3})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AlternateEpisodePattern = new(
        @"(?<![A-Z0-9])(?<season>\d{1,2})X(?<episode>\d{1,3})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SeasonPattern = new(
        @"(?<![A-Z0-9])(?:SEASON|S)[ ._-]*(?<season>\d{1,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EpisodePattern = new(
        @"(?<![A-Z0-9])(?:EPISODE|EP|E)[ ._-]*(?<episode>\d{1,3})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ResolutionPattern = new(
        @"(?<!\d)(?<height>2160|1440|1080|720|576|480)P(?![A-Z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<VideoMetadataBadge> FromFileName(string fileName)
    {
        var source = Path.GetFileNameWithoutExtension(fileName) ?? fileName;
        var badges = new Dictionary<string, VideoMetadataBadge>(StringComparer.Ordinal);

        var episode = SeasonEpisodePattern.Match(source);
        if (!episode.Success) episode = AlternateEpisodePattern.Match(source);
        if (episode.Success)
        {
            AddNumberBadge(badges, "season", "Season", episode.Groups["season"].Value);
            AddNumberBadge(badges, "episode", "Episode", episode.Groups["episode"].Value);
        }
        else
        {
            AddNumberBadge(badges, "season", "Season", SeasonPattern.Match(source).Groups["season"].Value);
            AddNumberBadge(badges, "episode", "Episode", EpisodePattern.Match(source).Groups["episode"].Value);
        }

        var resolution = ResolutionPattern.Match(source);
        if (resolution.Success)
            badges["resolution"] = Guessed("resolution", $"{resolution.Groups["height"].Value}p");

        if (ContainsToken(source, @"(?:HEVC|H[ ._-]?265|X265)"))
            badges["video-codec"] = Guessed("video-codec", "HEVC / H.265");
        else if (ContainsToken(source, @"(?:AVC|H[ ._-]?264|X264)"))
            badges["video-codec"] = Guessed("video-codec", "H.264");
        else if (ContainsToken(source, "AV1"))
            badges["video-codec"] = Guessed("video-codec", "AV1");
        else if (ContainsToken(source, "VP9"))
            badges["video-codec"] = Guessed("video-codec", "VP9");

        if (ContainsToken(source, @"WEB[ ._-]?DL")) badges["source"] = Guessed("source", "WEB-DL");
        else if (ContainsToken(source, "WEBRIP")) badges["source"] = Guessed("source", "WEBRip");
        else if (ContainsToken(source, @"BLU[ ._-]?RAY|BDRIP")) badges["source"] = Guessed("source", "Blu-ray");

        if (ContainsToken(source, @"DOLBY[ ._-]?VISION|DOVI|DV")) badges["hdr"] = Guessed("hdr", "Dolby Vision");
        else if (ContainsToken(source, @"HDR10\+")) badges["hdr"] = Guessed("hdr", "HDR10+");
        else if (ContainsToken(source, "HDR10|HDR")) badges["hdr"] = Guessed("hdr", "HDR");

        var aacChannels = Regex.Match(source, @"(?<![A-Z0-9])AAC[ ._-]?(?<channels>2[ .]0|5[ .]1|7[ .]1)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (ContainsToken(source, "EAC3|E[ ._-]?AC[ ._-]?3|DDP")) badges["audio-codec"] = Guessed("audio-codec", "E-AC-3");
        else if (ContainsToken(source, "AC3|AC[ ._-]?3")) badges["audio-codec"] = Guessed("audio-codec", "AC-3");
        else if (aacChannels.Success || ContainsToken(source, "AAC")) badges["audio-codec"] = Guessed("audio-codec", "AAC");
        else if (ContainsToken(source, "DTS")) badges["audio-codec"] = Guessed("audio-codec", "DTS");
        else if (ContainsToken(source, "FLAC")) badges["audio-codec"] = Guessed("audio-codec", "FLAC");
        else if (ContainsToken(source, "OPUS")) badges["audio-codec"] = Guessed("audio-codec", "Opus");
        if (aacChannels.Success) badges["audio-channels"] = Guessed("audio-channels", $"{aacChannels.Groups["channels"].Value.Replace(' ', '.')} audio");

        if (ContainsToken(source, "MSUBS|MULTISUBS?")) badges["subtitles"] = Guessed("subtitles", "Multi subtitles");
        if (ContainsToken(source, "JPN|JAPANESE")) badges["audio-language"] = Guessed("audio-language", "JPN audio");
        else if (ContainsToken(source, "THA|THAI")) badges["audio-language"] = Guessed("audio-language", "THA audio");
        else if (ContainsToken(source, "ENG|ENGLISH")) badges["audio-language"] = Guessed("audio-language", "ENG audio");

        return Order(badges);
    }

    public static IReadOnlyList<VideoMetadataBadge> Merge(string fileName, MediaMetadataDto metadata)
    {
        var badges = FromFileName(fileName).ToDictionary(badge => badge.Key, StringComparer.Ordinal);
        var video = metadata.Video.FirstOrDefault(track => track.IsDefault) ?? metadata.Video.FirstOrDefault();
        if (video is not null)
        {
            var resolution = FormatResolution(video.Width, video.Height);
            if (resolution is not null) badges["resolution"] = Verified("resolution", resolution);
            var codec = FormatVideoCodec(video.Codec);
            if (codec is not null) badges["video-codec"] = Verified("video-codec", codec);
        }

        var audio = metadata.Audio.FirstOrDefault(track => track.IsDefault) ?? metadata.Audio.FirstOrDefault();
        if (audio is not null)
        {
            var codec = FormatAudioCodec(audio.Codec);
            if (codec is not null) badges["audio-codec"] = Verified("audio-codec", codec);
            var language = FormatLanguage(audio.Language);
            if (language is not null) badges["audio-language"] = Verified("audio-language", $"{language} audio");
        }

        if (metadata.Subtitles.Count > 0)
        {
            var label = metadata.Subtitles.Count == 1 ? "1 subtitle" : $"{metadata.Subtitles.Count} subtitles";
            badges["subtitles"] = Verified("subtitles", label);
        }

        var duration = FormatDuration(metadata.Duration);
        if (duration is not null) badges["duration"] = Verified("duration", duration);
        return Order(badges);
    }

    private static IReadOnlyList<VideoMetadataBadge> Order(IReadOnlyDictionary<string, VideoMetadataBadge> badges)
    {
        string[] keys = ["season", "episode", "resolution", "video-codec", "source", "hdr", "audio-codec", "audio-channels", "subtitles", "audio-language", "duration"];
        return keys.Where(badges.ContainsKey).Select(key => badges[key]).Take(8).ToArray();
    }

    private static void AddNumberBadge(Dictionary<string, VideoMetadataBadge> badges, string key, string prefix, string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0)
            badges[key] = Guessed(key, $"{prefix} {number}");
    }

    private static bool ContainsToken(string value, string expression) =>
        Regex.IsMatch(value, $@"(?<![A-Z0-9])(?:{expression})(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? FormatResolution(int? width, int? height)
    {
        if (width is not > 0 || height is not > 0) return null;
        var shortEdge = Math.Min(width.Value, height.Value);
        int[] standards = [2160, 1440, 1080, 720, 576, 480];
        var standard = standards.FirstOrDefault(value => Math.Abs(shortEdge - value) <= 16);
        return standard > 0 ? $"{standard}p" : $"{width}×{height}";
    }

    private static string? FormatVideoCodec(string? codec) => codec?.Trim().ToLowerInvariant() switch
    {
        "hevc" or "h265" or "x265" => "HEVC / H.265",
        "h264" or "avc" or "x264" => "H.264",
        "av1" => "AV1",
        "vp9" => "VP9",
        "mpeg4" => "MPEG-4",
        null or "" or "unknown" => null,
        var value => value.ToUpperInvariant()
    };

    private static string? FormatAudioCodec(string? codec) => codec?.Trim().ToLowerInvariant() switch
    {
        "aac" => "AAC",
        "ac3" => "AC-3",
        "eac3" => "E-AC-3",
        "dts" => "DTS",
        "flac" => "FLAC",
        "opus" => "Opus",
        null or "" or "unknown" => null,
        var value => value.ToUpperInvariant()
    };

    private static string? FormatLanguage(string? language)
    {
        var value = language?.Trim();
        return string.IsNullOrWhiteSpace(value) || value.Equals("und", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Length is 2 or 3 ? value.ToUpperInvariant() : value;
    }

    private static string? FormatDuration(double? seconds)
    {
        if (seconds is not > 0 || !double.IsFinite(seconds.Value)) return null;
        var totalMinutes = Math.Max(1, (int)Math.Round(seconds.Value / 60, MidpointRounding.AwayFromZero));
        return totalMinutes >= 60 ? $"{totalMinutes / 60}h {totalMinutes % 60}m" : $"{totalMinutes} min";
    }

    private static VideoMetadataBadge Guessed(string key, string label) => new(key, label, false);
    private static VideoMetadataBadge Verified(string key, string label) => new(key, label, true);
}
