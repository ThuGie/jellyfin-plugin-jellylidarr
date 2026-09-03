using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JellyLidarr;

// Changes only the served Web shell; never modifies the installed web files.
public sealed class NavigationStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextRequest) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (!HttpMethods.IsGet(context.Request.Method) || !IsShell(path)) { await nextRequest(); return; }
            foreach (var header in new[] { "Accept-Encoding", "Range", "If-Range", "If-None-Match", "If-Modified-Since" }) context.Request.Headers.Remove(header);
            var output = context.Response.Body;
            await using var buffered = new MemoryStream();
            context.Response.Body = buffered;
            try { await nextRequest(); }
            finally { context.Response.Body = output; }
            var bytes = buffered.ToArray();
            if (context.Response.StatusCode == 200 && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true && !context.Response.Headers.ContainsKey("Content-Encoding"))
            {
                var webIndex = path.LastIndexOf("/web", StringComparison.OrdinalIgnoreCase);
                var prefix = context.Request.PathBase.Value + path[..webIndex];
                bytes = Encoding.UTF8.GetBytes(Inject(Encoding.UTF8.GetString(bytes), prefix));
                context.Response.ContentLength = bytes.Length;
                context.Response.Headers.Remove("ETag");
                context.Response.Headers.Remove("Last-Modified");
                context.Response.Headers.Remove("Accept-Ranges");
                context.Response.Headers["Cache-Control"] = "no-store";
            }
            await output.WriteAsync(bytes, context.RequestAborted);
        });
        next(app);
    };

    public static bool IsShell(string path) => path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase);

    public static string Inject(string html, string prefix)
    {
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (end < 0 || html.Contains("id=\"jellylidarr-navigation\"", StringComparison.Ordinal)) return html;
        var source = WebUtility.HtmlEncode(prefix.TrimEnd('/') + "/JellyLidarr/assets/navigation.js?v=1.0.0.9");
        return html.Insert(end, $"<script id=\"jellylidarr-navigation\" defer src=\"{source}\"></script>");
    }
}
