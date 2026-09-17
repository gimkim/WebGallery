using System.ComponentModel.DataAnnotations;
namespace WebGallery.ViewModels;
public sealed class PasswordChangeViewModel
{
    public string? UserId { get; set; }
    public string? Token { get; set; }
    public string? CurrentPassword { get; set; }
    [Required, MinLength(10), MaxLength(256)] public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = "";
}
