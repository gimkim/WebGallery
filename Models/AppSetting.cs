namespace WebGallery.Models;

public sealed class AppSetting
{
    public required string Key { get; set; }
    public string Value { get; set; } = "";
}
