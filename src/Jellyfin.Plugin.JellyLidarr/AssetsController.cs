using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyLidarr;

[ApiController, AllowAnonymous, Route("JellyLidarr/assets")]
public sealed class AssetsController : ControllerBase
{
    [HttpGet("{name}")]
    public IActionResult Get(string name)
    {
        if (name is not ("admin.css" or "admin.js" or "style.css" or "app.js" or "navigation.js" or "portal.html")) return NotFound();
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream($"Jellyfin.Plugin.JellyLidarr.Web.{name}");
        return stream is null ? NotFound() : File(stream, name.EndsWith(".css", StringComparison.Ordinal) ? "text/css" : name.EndsWith(".html", StringComparison.Ordinal) ? "text/html; charset=utf-8" : "text/javascript");
    }
}
