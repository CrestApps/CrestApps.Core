using CrestApps.Core.AI.Tooling.Parameters;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// The parameter placements accepted by the built-in HTTP API request source, and the capability
/// declaration that advertises them to the management UI and to save-time validation.
/// </summary>
public static class HttpApiRequestParameterBindings
{
    /// <summary>
    /// Appends the value to the request as a query string parameter.
    /// </summary>
    public const string Query = "Query";

    /// <summary>
    /// Substitutes the value into the matching <c>{name}</c> token of the configured path template.
    /// </summary>
    public const string Path = "Path";

    /// <summary>
    /// Adds the value as a request header. Model-supplied values are not accepted for this placement.
    /// </summary>
    public const string Header = "Header";

    /// <summary>
    /// Writes the value into the JSON request body at the configured dotted path.
    /// </summary>
    public const string Body = "Body";

    /// <summary>
    /// Builds the capability declaration for the HTTP source.
    /// </summary>
    /// <returns>The capabilities advertised by the source.</returns>
    public static AIToolInstanceParameterCapabilities CreateCapabilities()
    {
        return new AIToolInstanceParameterCapabilities
        {
            // These are the argument names the source builds itself, so a declared parameter may not
            // shadow one of them in the single schema the model sees.
            ReservedNames = ["path", "query", "body"],
            Hint = new LocalizedString(
                HttpApiRequestToolConstants.SourceName,
                "Define exactly what this API accepts. Parameters you let the model fill become typed, described inputs it can pass, instead of leaving it to guess at a free-form query object."),
            Bindings =
            [
                new AIToolParameterBindingOption(Query)
                {
                    DisplayName = new LocalizedString(Query, "Query string parameter"),
                    Hint = new LocalizedString(Query, "Appended to the request URL as ?name=value."),
                },
                new AIToolParameterBindingOption(Path)
                {
                    DisplayName = new LocalizedString(Path, "Path segment"),
                    Hint = new LocalizedString(Path, "Replaces the matching {token} in the path template."),
                    RequiresValue = true,
                },
                new AIToolParameterBindingOption(Body)
                {
                    DisplayName = new LocalizedString(Body, "Request body field"),
                    Hint = new LocalizedString(Body, "Written into the JSON body. Use a dotted path for nested fields, such as customer.id."),
                },
                new AIToolParameterBindingOption(Header)
                {
                    DisplayName = new LocalizedString(Header, "Request header"),
                    Hint = new LocalizedString(Header, "Sent as a request header. The model cannot fill headers, because a prompt-injected model could otherwise set arbitrary ones."),

                    // Deliberately excludes AIToolParameterFill.Model. A model-controlled header value is
                    // almost never intended, and it is a direct request-smuggling vector.
                    AllowedFills = [AIToolParameterFill.Fixed, AIToolParameterFill.Context],
                },
            ],
        };
    }
}
