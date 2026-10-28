using Microsoft.AspNetCore.Http;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class SecurityHeadersMiddlewareTests
{
    private static async Task<DefaultHttpContext> InvokeAsync(Action<HttpContext>? seed = null, bool callNext = true)
    {
        var context = TestData.HttpContext();
        seed?.Invoke(context);
        var invoked = false;
        var middleware = new Battleship.Server.Hosting.SecurityHeadersMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        if (callNext)
        {
            Assert.True(invoked);
        }

        return context;
    }

    [Fact]
    public async Task Invoke_SetsExactContentSecurityPolicy()
    {
        var context = await InvokeAsync();
        Assert.Equal(
            "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self' ws: wss:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'",
            context.Response.Headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public async Task Csp_ScriptSrcHasNoUnsafeInlineOrUnsafeEval()
    {
        var context = await InvokeAsync();
        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csp_FrameAncestorsNone()
    {
        var context = await InvokeAsync();
        Assert.Contains("frame-ancestors 'none'", context.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csp_ObjectSrcNone()
    {
        var context = await InvokeAsync();
        Assert.Contains("object-src 'none'", context.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoke_SetsNoSniff()
    {
        var context = await InvokeAsync();
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
    }

    [Fact]
    public async Task Invoke_SetsFrameOptionsDeny()
    {
        var context = await InvokeAsync();
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
    }

    [Fact]
    public async Task Invoke_SetsReferrerPolicyNoReferrer()
    {
        var context = await InvokeAsync();
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public async Task Invoke_SetsPermissionsPolicy()
    {
        var context = await InvokeAsync();
        Assert.Equal("camera=(), microphone=(), geolocation=()", context.Response.Headers["Permissions-Policy"].ToString());
    }

    [Fact]
    public async Task Invoke_OverwritesPreexistingHeaders()
    {
        var context = await InvokeAsync(c =>
        {
            c.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            c.Response.Headers["X-Content-Type-Options"] = "sniff";
        });
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
    }

    [Fact]
    public async Task Invoke_CallsNext()
    {
        var called = false;
        var middleware = new Battleship.Server.Hosting.SecurityHeadersMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(TestData.HttpContext());
        Assert.True(called);
    }
}
