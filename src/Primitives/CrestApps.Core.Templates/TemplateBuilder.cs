using System.Buffers;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.Templates;

/// <summary>
/// A high-performance builder for composing system prompts from a mix of
/// <see cref="Template"/> instances, template IDs, and raw strings.
/// Uses pooled buffers to minimize allocations during prompt assembly.
/// </summary>
public sealed class TemplateBuilder
{
    private readonly List<Segment> _segments = [];

    private string _separator = Environment.NewLine + Environment.NewLine;

    /// <summary>
    /// Sets the separator used between segments when building the final string.
    /// Defaults to a double newline.
    /// </summary>
    /// <param name="separator">The separator inserted between rendered segments.</param>
    /// <returns>The current builder instance.</returns>
    public TemplateBuilder WithSeparator(string separator)
    {
        _separator = separator ?? string.Empty;

        return this;
    }

    /// <summary>
    /// Appends a raw string segment.
    /// </summary>
    /// <param name="text">The text to append.</param>
    /// <returns>The current builder instance.</returns>
    public TemplateBuilder Append(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _segments.Add(new Segment(text));
        }

        return this;
    }

    /// <summary>
    /// Appends the body of an <see cref="Template"/>.
    /// </summary>
    /// <param name="template">The template whose content should be appended.</param>
    /// <returns>The current builder instance.</returns>
    public TemplateBuilder Append(Template template)
    {
        if (template is not null && !string.IsNullOrEmpty(template.Content))
        {
            _segments.Add(new Segment(template.Content));
        }

        return this;
    }

    /// <summary>
    /// Appends a template by ID. The template will be resolved and rendered
    /// when <see cref="BuildAsync"/> is called.
    /// </summary>
    /// <param name="templateId">The template identifier.</param>
    /// <param name="arguments">The arguments used when rendering the template.</param>
    /// <returns>The current builder instance.</returns>
    public TemplateBuilder AppendTemplate(string templateId, IDictionary<string, object> arguments = null)
    {
        if (!string.IsNullOrEmpty(templateId))
        {
            _segments.Add(new Segment(templateId, arguments, isTemplateId: true));
        }

        return this;
    }

    /// <summary>
    /// Builds the final composed string by resolving any template IDs
    /// through the provided <paramref name="templateService"/> and joining
    /// all segments with the configured separator.
    /// </summary>
    /// <param name="templateService">The template service used to resolve template identifiers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The composed output.</returns>
    public async Task<string> BuildAsync(ITemplateService templateService, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateService);

        if (_segments.Count == 0)
        {
            return string.Empty;
        }

        // Resolve template IDs to rendered strings.
        var resolved = new string[_segments.Count];

        for (var i = 0; i < _segments.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var segment = _segments[i];

            if (segment.IsTemplateId)
            {
                resolved[i] = await templateService.RenderAsync(segment.Text, segment.Arguments, cancellationToken);
            }
            else
            {
                resolved[i] = segment.Text;
            }
        }

        // Calculate total length for the final string.
        var totalLength = 0;
        var nonEmptyCount = 0;

        for (var i = 0; i < resolved.Length; i++)
        {
            if (!string.IsNullOrEmpty(resolved[i]))
            {
                if (nonEmptyCount > 0)
                {
                    totalLength += _separator.Length;
                }

                totalLength += resolved[i].Length;
                nonEmptyCount++;
            }
        }

        if (nonEmptyCount == 0)
        {
            return string.Empty;
        }

        // Single segment: return directly without allocation.

        if (nonEmptyCount == 1)
        {
            for (var i = 0; i < resolved.Length; i++)
            {
                if (!string.IsNullOrEmpty(resolved[i]))
                {
                    return resolved[i];
                }
            }
        }

        // Build the final string using a rented buffer.
        var buffer = ArrayPool<char>.Shared.Rent(totalLength);

        try
        {
            var position = 0;
            var written = false;

            for (var i = 0; i < resolved.Length; i++)
            {
                if (string.IsNullOrEmpty(resolved[i]))
                {
                    continue;
                }

                if (written)
                {
                    _separator.AsSpan().CopyTo(buffer.AsSpan(position));
                    position += _separator.Length;
                }

                resolved[i].AsSpan().CopyTo(buffer.AsSpan(position));
                position += resolved[i].Length;
                written = true;
            }

            return new string(buffer, 0, position);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Builds the final composed string from only raw string and
    /// <see cref="Template"/> segments. Does not require a template service.
    /// Throws <see cref="InvalidOperationException"/> if any segment requires
    /// template resolution.
    /// </summary>
    /// <returns>The composed output.</returns>
    public string Build()
    {
        if (_segments.Count == 0)
        {
            return string.Empty;
        }

        // Verify no template IDs are present.

        for (var i = 0; i < _segments.Count; i++)
        {
            if (_segments[i].IsTemplateId)
            {
                throw new InvalidOperationException(
                    $"Segment at index {i} references template ID '{_segments[i].Text}' which requires an ITemplateService. Use BuildAsync(ITemplateService) instead.");
            }
        }

        // Calculate total length.
        var totalLength = 0;
        var nonEmptyCount = 0;

        for (var i = 0; i < _segments.Count; i++)
        {
            if (!string.IsNullOrEmpty(_segments[i].Text))
            {
                if (nonEmptyCount > 0)
                {
                    totalLength += _separator.Length;
                }

                totalLength += _segments[i].Text.Length;
                nonEmptyCount++;
            }
        }

        if (nonEmptyCount == 0)
        {
            return string.Empty;
        }

        if (nonEmptyCount == 1)
        {
            for (var i = 0; i < _segments.Count; i++)
            {
                if (!string.IsNullOrEmpty(_segments[i].Text))
                {
                    return _segments[i].Text;
                }
            }
        }

        return string.Create(totalLength, (Segments: _segments, Separator: _separator), static (span, state) =>
        {
            var position = 0;
            var written = false;

            for (var i = 0; i < state.Segments.Count; i++)
            {
                var text = state.Segments[i].Text;

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                if (written)
                {
                    state.Separator.AsSpan().CopyTo(span[position..]);

                    position += state.Separator.Length;
                }

                text.AsSpan().CopyTo(span[position..]);

                position += text.Length;
                written = true;
            }
        });
    }

    /// <summary>
    /// Represents a single builder segment, either literal text or a template reference.
    /// </summary>
    private readonly struct Segment
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Segment"/> struct.
        /// </summary>
        /// <param name="text">The segment text.</param>
        public Segment(string text)
        {
            Text = text;
            Arguments = null;
            IsTemplateId = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Segment"/> struct.
        /// </summary>
        /// <param name="text">The segment text or template identifier.</param>
        /// <param name="arguments">The arguments used when rendering the template.</param>
        /// <param name="isTemplateId"><see langword="true"/> when <paramref name="text"/> is a template identifier; otherwise, <see langword="false"/>.</param>
        public Segment(string text, IDictionary<string, object> arguments, bool isTemplateId)
        {
            Text = text;
            Arguments = arguments;
            IsTemplateId = isTemplateId;
        }

        /// <summary>
        /// Gets the segment text or template identifier.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the arguments used when rendering a template segment.
        /// </summary>
        public IDictionary<string, object> Arguments { get; }

        /// <summary>
        /// Gets a value indicating whether this segment references a template identifier.
        /// </summary>
        public bool IsTemplateId { get; }
    }
}
