using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class StaticContentTests
{
    private static DefaultHttpContext Prepare(string path)
    {
        var context = TestData.HttpContext();
        context.Request.Path = path;
        var file = new NotFoundFileInfo("x");
        Battleship.Server.Hosting.StaticContent.Prepare(new StaticFileResponseContext(context, file));
        return context;
    }

    [Fact]
    public void Prepare_IndexHtml_NoStore() => Assert.Equal("no-cache, no-store, must-revalidate", Prepare("/index.html").Response.Headers["Cache-Control"].ToString());

    [Fact]
    public void Prepare_AppSettingsJson_NoStore() => Assert.Equal("no-cache, no-store, must-revalidate", Prepare("/appsettings.json").Response.Headers["Cache-Control"].ToString());

    [Fact]
    public void Prepare_ScopedStylesCss_NoStore() => Assert.Equal("no-cache, no-store, must-revalidate", Prepare("/Battleship.Web.styles.css").Response.Headers["Cache-Control"].ToString());

    [Fact]
    public void Prepare_BlazorWebAssemblyJs_NoStore() => Assert.Equal("no-cache, no-store, must-revalidate", Prepare("/_framework/blazor.webassembly.js").Response.Headers["Cache-Control"].ToString());

    [Fact]
    public void Prepare_DotnetJs_NoStore() => Assert.Equal("no-cache, no-store, must-revalidate", Prepare("/_framework/dotnet.js").Response.Headers["Cache-Control"].ToString());

    [Fact]
    public void Prepare_DotnetJsSuffixWithoutSlash_NotNoStore()
    {
        var context = Prepare("/_framework/not-dotnet.js");
        Assert.Equal("public, max-age=31536000, immutable", context.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public void Prepare_FrameworkAsset_ImmutableOneYear()
    {
        var context = Prepare("/_framework/dotnet.wasm");
        Assert.Equal("public, max-age=31536000, immutable", context.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public void Prepare_OtherAsset_MaxAge300()
    {
        var context = Prepare("/css/app.css");
        Assert.Equal("public, max-age=300", context.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public void Prepare_PathMatching_CaseInsensitive()
    {
        var context = Prepare("/INDEX.HTML");
        Assert.Equal("no-cache, no-store, must-revalidate", context.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public async Task WriteIndexAsync_Missing_Returns404()
    {
        var context = TestData.HttpContext();
        await Battleship.Server.Hosting.StaticContent.WriteIndexAsync(context, new TestEnvironment(new NullFileProvider()));
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task WriteIndexAsync_Directory_Returns404()
    {
        var context = TestData.HttpContext();
        await Battleship.Server.Hosting.StaticContent.WriteIndexAsync(context, new TestEnvironment(new DirectoryFileProvider()));
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task WriteIndexAsync_Exists_ServesHtmlUtf8NoStore()
    {
        var directory = Directory.CreateTempSubdirectory("battleship-static-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "index.html"), "<html></html>");
            var context = TestData.HttpContext();
            await Battleship.Server.Hosting.StaticContent.WriteIndexAsync(context, new TestEnvironment(new PhysicalFileProvider(directory.FullName)));
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
            Assert.Equal("no-cache, no-store, must-revalidate", context.Response.Headers["Cache-Control"].ToString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(".wasm", "application/wasm")]
    [InlineData(".dat", "application/octet-stream")]
    [InlineData(".blat", "application/octet-stream")]
    [InlineData(".webmanifest", "application/manifest+json")]
    [InlineData(".br", "application/octet-stream")]
    [InlineData(".gz", "application/gzip")]
    public void Types_MapsWasmDatBlatWebmanifestBrGz(string extension, string expected)
    {
        Assert.True(Battleship.Server.Hosting.StaticContent.Types.TryGetContentType("asset" + extension, out var contentType));
        Assert.Equal(expected, contentType);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(IFileProvider provider) => WebRootFileProvider = provider;
        public string ApplicationName { get; set; } = "Battleship.Server.Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; } = "wwwroot";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DirectoryFileProvider : IFileProvider
    {
        public IFileInfo GetFileInfo(string subpath) => new DirectoryInfo(subpath);
        public Microsoft.Extensions.FileProviders.IDirectoryContents GetDirectoryContents(string subpath) => new NotFoundDirectoryContents();
        public Microsoft.Extensions.Primitives.IChangeToken Watch(string filter) => new NoChangeToken();

        private sealed class DirectoryInfo : IFileInfo
        {
            public DirectoryInfo(string name) => Name = name;
            public bool Exists => true;
            public long Length => 0;
            public string? PhysicalPath => null;
            public string Name { get; }
            public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
            public bool IsDirectory => true;
            public Stream CreateReadStream() => throw new InvalidOperationException();
        }

        private sealed class NoChangeToken : Microsoft.Extensions.Primitives.IChangeToken
        {
            public bool HasChanged => false;
            public bool ActiveChangeCallbacks => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new Noop();
            private sealed class Noop : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}
