using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Mcp.Models;

namespace CrestApps.Core.AI.Mcp.Services;

/// <summary>
/// Represents the default MCP Metadata Prompt Generator.
/// </summary>
public sealed class DefaultMcpMetadataPromptGenerator : IMcpMetadataPromptGenerator
{
    /// <summary>
    /// Generates the operation.
    /// </summary>
    /// <param name="capabilities">The capabilities.</param>
    public string Generate(IReadOnlyList<McpServerCapabilities> capabilities)
    {
        if (capabilities is null || capabilities.Count == 0)
        {
            return null;
        }

        var hasAnyCapability = capabilities.Any(server =>
            server.Tools.Count > 0 ||
            server.Prompts.Count > 0 ||
            server.Resources.Count > 0 ||
            server.ResourceTemplates.Count > 0);

        if (!hasAnyCapability)
        {
            return null;
        }

        var builder = new StringBuilder();

        builder.AppendLine("You have access to external MCP (Model Context Protocol) servers via the 'mcp_invoke' tool.");
        builder.AppendLine("Use the 'mcp_invoke' tool to call any of the capabilities listed below.");
        builder.AppendLine();
        builder.AppendLine("IMPORTANT invocation rules:");
        builder.AppendLine("- Always specify the correct 'clientId', 'type', and 'id' parameters.");
        builder.AppendLine("- For tools: set type='tool', id=<tool name>, and inputs=<object matching the tool's Parameters schema>.");
        builder.AppendLine("  The 'inputs' object must include all required properties as defined in the tool's Parameters schema. It must be a valid JSON object, with no wrappers (such as code fences) or additional formatting?only pure JSON.");
        builder.AppendLine("  Example: if a tool has Parameters with required property 'featureIds' (array of strings), call mcp_invoke with inputs={\"featureIds\":[\"value1\",\"value2\"]}.");
        builder.AppendLine("- For prompts: set type='prompt' and id=<prompt name>.");
        builder.AppendLine("- For resources: set type='resource' and id=<the full resource URI>. Do NOT use the resource name as id.");
        builder.AppendLine("- For resource templates: set type='resource' and id=<the URI template with all {parameter} placeholders replaced with actual values from the user's request>.");
        builder.AppendLine();
        builder.AppendLine("Available MCP Capabilities:");

        foreach (var server in capabilities)
        {
            if (server.Tools.Count == 0 && server.Prompts.Count == 0 && server.Resources.Count == 0 && server.ResourceTemplates.Count == 0)
            {
                continue;
            }

            builder.AppendLine();
            builder.Append("## Server: ");
            builder.AppendLine(server.ConnectionDisplayText ?? server.ConnectionId);
            builder.Append("  clientId: ");
            builder.AppendLine(server.ConnectionId);

            if (server.Tools.Count > 0)
            {
                builder.AppendLine("  Tools (pass required arguments via 'inputs'):");

                foreach (var tool in server.Tools.OrderBy(t => t.Name))
                {
                    builder.Append("    - ");
                    builder.Append(tool.Name);

                    if (!string.IsNullOrEmpty(tool.Description))
                    {
                        builder.Append(": ");
                        builder.Append(tool.Description);
                    }

                    builder.AppendLine();

                    if (tool.InputSchema.HasValue)
                    {
                        AppendParameterSummary(builder, tool.InputSchema.Value);
                    }
                }
            }

            if (server.Prompts.Count > 0)
            {
                builder.AppendLine("  Prompts:");

                foreach (var prompt in server.Prompts.OrderBy(p => p.Name))
                {
                    builder.Append("    - ");
                    builder.Append(prompt.Name);

                    if (!string.IsNullOrEmpty(prompt.Description))
                    {
                        builder.Append(": ");
                        builder.Append(prompt.Description);
                    }

                    builder.AppendLine();
                }
            }

            if (server.Resources.Count > 0)
            {
                builder.AppendLine("  Resources (use the URI as 'id' when invoking):");

                foreach (var resource in server.Resources.OrderBy(r => r.Name))
                {
                    builder.Append("    - ");
                    builder.Append(resource.Uri ?? resource.Name);

                    if (!string.IsNullOrEmpty(resource.Description))
                    {
                        builder.Append(": ");
                        builder.Append(resource.Description);
                    }

                    builder.AppendLine();
                }
            }

            if (server.ResourceTemplates.Count > 0)
            {
                builder.AppendLine("  Resource Templates (replace {parameter} placeholders with actual values and use the resolved URI as 'id'):");

                foreach (var template in server.ResourceTemplates.OrderBy(r => r.Name))
                {
                    builder.Append("    - ");
                    builder.Append(template.UriTemplate ?? template.Name);

                    if (!string.IsNullOrEmpty(template.Description))
                    {
                        builder.Append(": ");
                        builder.Append(template.Description);
                    }

                    builder.AppendLine();
                }
            }
        }

        return builder.ToString();
    }

    private static void AppendParameterSummary(StringBuilder builder, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    requiredSet.Add(item.GetString());
                }
            }
        }

        foreach (var property in properties.EnumerateObject())
        {
            var name = property.Name;
            var isRequired = requiredSet.Contains(name);
            var typeName = GetTypeName(property.Value);
            var description = property.Value.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
                ? desc.GetString()
                : null;

            builder.Append("      ");
            builder.Append(name);
            builder.Append(" (");
            builder.Append(typeName);

            if (isRequired)
            {
                builder.Append(", required");
            }

            builder.Append(')');

            if (!string.IsNullOrEmpty(description))
            {
                builder.Append(": ");
                builder.Append(description);
            }

            builder.AppendLine();
        }
    }

    private static string GetTypeName(JsonElement propertySchema)
    {
        if (!propertySchema.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return "object";
        }

        var type = typeElement.GetString();

        if (type == "array" && propertySchema.TryGetProperty("items", out var items))
        {
            var itemType = items.TryGetProperty("type", out var itemTypeElement) && itemTypeElement.ValueKind == JsonValueKind.String
                ? itemTypeElement.GetString()
                : "object";

            return $"{itemType}[]";
        }

        return type;
    }
}
