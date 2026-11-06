using System.Reflection;
using Manctl.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Manctl.Server.Guard;

/// <summary>Wraps a normal SDK tool. Before the call: harmless type slips are forgiven ("123" -> 123) and the
/// arguments are checked against the tool's schema, so a wrong parameter name gets a real explanation instead of
/// "An error occurred invoking". After the call: every answer gets the same {ok, errorCode, message, hint} shape.</summary>
internal sealed class GuardedTool(McpServerTool inner) : McpServerTool
{
    public override Tool ProtocolTool => inner.ProtocolTool;

    public override async ValueTask<CallToolResponse> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var name = inner.ProtocolTool.Name;

        if (request.Params is { } original)
        {
            var coerced = ArgumentValidator.Coerce(inner.ProtocolTool, original.Arguments);
            if (!ReferenceEquals(coerced, original.Arguments))
            {
                // Arguments is init-only, so the call continues on a copy that carries the corrected values.
                request = new RequestContext<CallToolRequestParams>(request.Server)
                {
                    Services = request.Services,
                    Params = new CallToolRequestParams { Name = original.Name, Arguments = coerced, Meta = original.Meta },
                };
            }
        }

        if (ArgumentValidator.Check(inner.ProtocolTool, request.Params?.Arguments) is { } invalid)
            return invalid;

        var response = await inner.InvokeAsync(request, cancellationToken);
        return Normalize(name, response);
    }

    private static CallToolResponse Normalize(string tool, CallToolResponse response)
    {
        if (response.Content is not { Count: > 0 }) return response;
        var first = response.Content[0];
        if (first.Type != "text" || first.Text is null) return response;

        var text = first.Text;

        // The SDK's catch-all: the real exception is not available here (it is in manctl's stderr log).
        if (response.IsError == true && text.StartsWith("An error occurred invoking", StringComparison.Ordinal))
        {
            response.Content[0] = new Content
            {
                Type = "text",
                Text = ToolErrors.Json(ErrorCodes.Unknown,
                    $"'{tool}' failed with an unexpected internal error.",
                    "The parameters were accepted, so this is not a naming mistake. Check the preconditions (window still open? device connected? file exists?) and try again. Details are in manctl's stderr log.",
                    tool),
            };
            return response;
        }

        // Old "[Code] message" text errors -> the common JSON shape.
        if (response.IsError == true && text.Length > 2 && text[0] == '[' && text.IndexOf(']') is var close and > 1)
        {
            var code = text[1..close];
            var msg = text[(close + 1)..].Trim();
            response.Content[0] = new Content { Type = "text", Text = ToolErrors.Json(code, msg, null, tool) };
            return response;
        }

        var shaped = ResponseShape.Apply(text, tool);
        if (!ReferenceEquals(shaped, text)) response.Content[0] = new Content { Type = "text", Text = shaped };
        return response;
    }
}

public static class GuardedToolRegistration
{
    /// <summary>Same discovery as the SDK's WithToolsFromAssembly (types marked [McpServerToolType], methods marked
    /// [McpServerTool]), but each tool is wrapped in <see cref="GuardedTool"/>.</summary>
    public static IMcpServerBuilder WithGuardedToolsFromAssembly(this IMcpServerBuilder builder, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null) continue;
                if (!method.IsStatic)
                    throw new NotSupportedException($"{type.Name}.{method.Name}: guarded tools must be static methods.");

                var m = method;
                builder.Services.AddSingleton<McpServerTool>(sp =>
                    new GuardedTool(McpServerTool.Create(m, (object?)null, new McpServerToolCreateOptions { Services = sp })));
            }
        }
        return builder;
    }
}
