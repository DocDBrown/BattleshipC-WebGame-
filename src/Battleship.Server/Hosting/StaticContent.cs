using Microsoft.AspNetCore.StaticFiles;

namespace Battleship.Server.Hosting;

public static class StaticContent
{
    public static readonly FileExtensionContentTypeProvider Types = CreateProvider();

    public static void Prepare(StaticFileResponseContext context)
    {
        var path = context.Context.Request.Path.Value ?? string.Empty;
        var headers = context.Context.Response.Headers;

        if (path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("blazor.boot.json", StringComparison.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
            return;
        }

        if (path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = "public, max-age=31536000, immutable";
            return;
        }

        headers["Cache-Control"] = "public, max-age=300";
    }

    public static async Task WriteIndexAsync(HttpContext context, IWebHostEnvironment environment)
    {
        var index = environment.WebRootFileProvider.GetFileInfo("index.html");
        if (!index.Exists || index.IsDirectory)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        await context.Response.SendFileAsync(index);
    }

    private static FileExtensionContentTypeProvider CreateProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".wasm"] = "application/wasm";
        provider.Mappings[".dat"] = "application/octet-stream";
        provider.Mappings[".blat"] = "application/octet-stream";
        provider.Mappings[".webmanifest"] = "application/manifest+json";
        provider.Mappings[".br"] = "application/octet-stream";
        provider.Mappings[".gz"] = "application/gzip";
        return provider;
    }
}
