using Microsoft.AspNetCore.Identity;

namespace WebGallery.Models;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string RootFolder { get; set; } = "";
}
