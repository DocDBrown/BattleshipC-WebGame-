using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class TestRenderer : Renderer
{
    private readonly SyncDispatcher _dispatcher = new();

    public TestRenderer(IServiceProvider services)
        : base(services, NullLoggerFactory.Instance)
    {
    }

    public Exception? LastException { get; private set; }

    public override Dispatcher Dispatcher => _dispatcher;

    public int Attach(IComponent component) => AssignRootComponentId(component);

    public Task RenderAsync(int componentId, ParameterView? parameters = null) =>
        RenderRootComponentAsync(componentId, parameters ?? ParameterView.Empty);

    public string GetHtml(int componentId)
    {
        var frames = GetCurrentRenderTreeFrames(componentId);
        var builder = new StringBuilder();
        RenderRange(frames, 0, frames.Count, builder);
        return builder.ToString();
    }

    protected override void HandleException(Exception exception) => LastException = exception;

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

    private void RenderRange(ArrayRange<RenderTreeFrame> frames, int start, int end, StringBuilder builder)
    {
        var index = start;
        while (index < end)
        {
            var frame = frames.Array[index];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Element:
                    RenderElement(frames, index, builder);
                    index += frame.ElementSubtreeLength;
                    break;
                case RenderTreeFrameType.Text:
                    builder.Append(Escape(frame.TextContent));
                    index++;
                    break;
                case RenderTreeFrameType.Markup:
                    builder.Append(frame.MarkupContent);
                    index++;
                    break;
                case RenderTreeFrameType.Region:
                    RenderRange(frames, index + 1, index + frame.RegionSubtreeLength, builder);
                    index += frame.RegionSubtreeLength;
                    break;
                case RenderTreeFrameType.Component:
                    if (frame.ComponentId > 0)
                    {
                        RenderRange(GetCurrentRenderTreeFrames(frame.ComponentId), 0, GetCurrentRenderTreeFrames(frame.ComponentId).Count, builder);
                    }

                    index += frame.ComponentSubtreeLength;
                    break;
                default:
                    index++;
                    break;
            }
        }
    }

    private void RenderElement(ArrayRange<RenderTreeFrame> frames, int index, StringBuilder builder)
    {
        var frame = frames.Array[index];
        builder.Append('<').Append(frame.ElementName);
        var cursor = index + 1;
        while (cursor < index + frame.ElementSubtreeLength && frames.Array[cursor].FrameType == RenderTreeFrameType.Attribute)
        {
            var attribute = frames.Array[cursor];
            if (attribute.AttributeValue is bool flag)
            {
                if (flag)
                {
                    builder.Append(' ').Append(attribute.AttributeName);
                }
            }
            else if (attribute.AttributeValue is not null
                     && !attribute.AttributeName.StartsWith("on", StringComparison.Ordinal))
            {
                builder.Append(' ').Append(attribute.AttributeName).Append("=\"").Append(Escape(attribute.AttributeValue.ToString()!)).Append('"');
            }

            cursor++;
        }

        builder.Append('>');
        RenderRange(frames, cursor, index + frame.ElementSubtreeLength, builder);
        builder.Append("</").Append(frame.ElementName).Append('>');
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    private sealed class SyncDispatcher : Dispatcher
    {
        public override bool CheckAccess() => true;

        public override Task InvokeAsync(Action workItem)
        {
            workItem();
            return Task.CompletedTask;
        }

        public override Task InvokeAsync(Func<Task> workItem) => workItem();

        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) =>
            Task.FromResult(workItem());

        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem) => workItem();
    }
}
