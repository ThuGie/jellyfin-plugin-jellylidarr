using System.Text.Json;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyLidarr.Tests;

public sealed class SearchAndNavigationTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"images\":null}")]
    [InlineData("{\"images\":[]}")]
    [InlineData("{\"images\":{}}")]
    [InlineData("{\"images\":[null,42,{}]}")]
    [InlineData("{\"images\":[{\"coverType\":\"cover\",\"remoteUrl\":42}]}")]
    public void Missing_or_malformed_artwork_does_not_break_search(string json)
    {
        using var doc=JsonDocument.Parse(json);
        Assert.Null(LidarrArtwork.Read(doc.RootElement));
    }

    [Fact]
    public void Artwork_prefers_cover_but_supports_other_images()
    {
        using var cover=JsonDocument.Parse("{\"images\":[{\"coverType\":\"banner\",\"remoteUrl\":\"https://example.com/banner\"},{\"coverType\":\"cover\",\"remoteUrl\":\"https://example.com/cover\"}]}");
        Assert.Equal("https://example.com/cover",LidarrArtwork.Read(cover.RootElement));
        using var banner=JsonDocument.Parse("{\"images\":[{\"coverType\":\"banner\",\"url\":\"https://example.com/banner\"}]}");
        Assert.Equal("https://example.com/banner",LidarrArtwork.Read(banner.RootElement));
    }

    [Fact]
    public void Shell_injection_is_base_path_safe_and_idempotent()
    {
        var html=NavigationStartupFilter.Inject("<html><body>Jellyfin</body></html>","/media");
        Assert.Contains("src=\"/media/JellyLidarr/assets/navigation.js",html);
        Assert.Equal(html,NavigationStartupFilter.Inject(html,"/media"));
        Assert.Equal("unchanged",NavigationStartupFilter.Inject("unchanged",""));
    }

    [Theory]
    [InlineData("/web/",true)]
    [InlineData("/jellyfin/web/index.html",true)]
    [InlineData("/web",true)]
    [InlineData("/Videos/1/stream",false)]
    [InlineData("/web/main.js",false)]
    public void Navigation_changes_only_web_shell(string path,bool expected) => Assert.Equal(expected,NavigationStartupFilter.IsShell(path));

    [Fact]
    public async Task Middleware_injects_navigation_without_touching_web_files()
    {
        var app = new ApplicationBuilder(new ServiceCollection().AddLogging().BuildServiceProvider());
        new NavigationStartupFilter().Configure(builder => builder.Run(async context =>
        {
            context.Response.ContentType = "text/html";
            context.Response.Headers.ETag = "old";
            await context.Response.WriteAsync("<html><body>Home</body></html>");
        }))(app);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.PathBase = "/jellyfin";
        context.Request.Path = "/web/index.html";
        context.Response.Body = new MemoryStream();
        await app.Build()(context);
        context.Response.Body.Position = 0;
        var html = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("/jellyfin/JellyLidarr/assets/navigation.js", html);
        Assert.False(context.Response.Headers.ContainsKey("ETag"));
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }
}
