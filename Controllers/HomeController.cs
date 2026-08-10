using Microsoft.AspNetCore.Mvc;

namespace WebGallery.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Error() => View();
}
