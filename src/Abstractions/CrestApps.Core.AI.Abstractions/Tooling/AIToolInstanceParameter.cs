namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// A single user-declared parameter on an <see cref="AIToolInstance"/>. A parameter has two halves: a
/// <em>declaration</em> (name, type, description) that shapes the function schema the AI model sees, and
/// a <em>binding</em> that tells the owning <see cref="IAIToolInstanceSource"/> where the resolved value
/// belongs at invocation time.
/// </summary>
/// <remarks>
/// Parameters are only meaningful for sources that opt in by declaring
/// <see cref="AIToolInstanceParameterCapabilities"/> at registration time. A source that does not opt in
/// has nowhere to place a resolved value, so declaring parameters on it is rejected when the instance is
/// saved rather than silently dropped at invocation time.
/// </remarks>
public sealed class AIToolInstanceParameter
{
    /// <summary>
    /// Gets or sets the parameter name. For <see cref="AIToolParameterFill.Model"/> parameters this is the
    /// property name in the function schema, so it must be a valid identifier, unique within the instance,
    /// and must not collide with a name the owning source reserves for its own arguments.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the natural-language description shown to the AI model. Required for
    /// <see cref="AIToolParameterFill.Model"/> parameters, since it is the only signal the model has for
    /// what to pass. Ignored for the fill modes the model never sees.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the JSON schema type of the parameter.
    /// </summary>
    public AIToolParameterType Type { get; set; }

    /// <summary>
    /// Gets or sets who supplies the value at invocation time.
    /// </summary>
    public AIToolParameterFill Fill { get; set; }

    /// <summary>
    /// Gets or sets whether the AI model must supply this parameter. Only meaningful when
    /// <see cref="Fill"/> is <see cref="AIToolParameterFill.Model"/>. A required parameter that the model
    /// omits produces a tool error the model can act on, rather than a silently missing value.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Gets or sets the value used when the AI model omits an optional parameter, and the pinned value
    /// when <see cref="Fill"/> is <see cref="AIToolParameterFill.Fixed"/>.
    /// </summary>
    /// <remarks>
    /// The default is applied server-side by the binder rather than emitted as a JSON schema
    /// <c>default</c>, because chat-completion providers do not reliably honor schema defaults and drop
    /// them entirely under strict mode.
    /// </remarks>
    public object DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the closed set of accepted values, emitted as the schema <c>enum</c> and enforced by
    /// the binder. When empty, any value of the declared type is accepted.
    /// </summary>
    public string[] AllowedValues { get; set; }

    /// <summary>
    /// Gets or sets the well-known context key resolved when <see cref="Fill"/> is
    /// <see cref="AIToolParameterFill.Context"/>, for example <c>user.id</c>.
    /// </summary>
    public string ContextKey { get; set; }

    /// <summary>
    /// Gets or sets the source-specific binding that says where the resolved value goes, expressed as
    /// <c>Target</c> or <c>Target:name</c> (for example <c>Query:orderId</c>). The set of valid targets is
    /// declared by the owning source in its <see cref="AIToolInstanceParameterCapabilities"/>.
    /// </summary>
    public string Binding { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="DefaultValue"/> holds a credential. Secret values are data-protected
    /// at rest by the owning source and are never rendered back to the management UI in clear text.
    /// Only meaningful for <see cref="AIToolParameterFill.Fixed"/> parameters, which the model never sees.
    /// </summary>
    public bool IsSecret { get; set; }
}
