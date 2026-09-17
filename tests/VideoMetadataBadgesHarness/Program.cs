using WebGallery.Services;

static void Require(IReadOnlyList<VideoMetadataBadge> badges, string key, string label, bool? verified = null)
{
    var badge = badges.SingleOrDefault(item => item.Key == key);
    if (badge is null || badge.Label != label || (verified.HasValue && badge.Verified != verified.Value))
        throw new Exception($"Expected {key}={label} (verified={verified}); got {string.Join(", ", badges.Select(item => $"{item.Key}={item.Label}/{item.Verified}"))}");
}

var guessed = VideoMetadataBadges.FromFileName(
    "Chainsmoker.Cat.S01E01.1080p.NF.WEB-DL.JPN.AAC2.0.H.264.MSubs-ToonsHub.mkv");
Require(guessed, "season", "Season 1", false);
Require(guessed, "episode", "Episode 1", false);
Require(guessed, "resolution", "1080p", false);
Require(guessed, "video-codec", "H.264", false);
Require(guessed, "source", "WEB-DL", false);
Require(guessed, "audio-codec", "AAC", false);
Require(guessed, "audio-channels", "2.0 audio", false);
Require(guessed, "subtitles", "Multi subtitles", false);

var metadata = new MediaMetadataDto(
    5_527,
    [new MediaTrackDto(0, "video", "hevc", null, null, 3840, 2160, null, 23.976, null, null, null, true, true)],
    [new MediaTrackDto(1, "audio", "eac3", null, null, null, null, null, null, null, "jpn", null, true, true)],
    [new MediaTrackDto(2, "subtitle", "ass", null, null, null, null, null, null, null, "eng", null, true, true)],
    null,
    false,
    null);
var verified = VideoMetadataBadges.Merge("Show.S02E09.720p.x264.mkv", metadata);
Require(verified, "season", "Season 2", false);
Require(verified, "episode", "Episode 9", false);
Require(verified, "resolution", "2160p", true);
Require(verified, "video-codec", "HEVC / H.265", true);
Require(verified, "audio-codec", "E-AC-3", true);
Require(verified, "audio-language", "JPN audio", true);
Require(verified, "subtitles", "1 subtitle", true);
Require(verified, "duration", "1h 32m", true);
if (verified.Any(item => item.Label is "720p" or "H.264"))
    throw new Exception("Verified metadata did not replace conflicting filename guesses.");

Console.WriteLine("PASS: filename season/episode/quality/codec/audio parsing and verified ffprobe metadata merging.");
