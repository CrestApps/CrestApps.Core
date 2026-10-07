using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// One call of a presentation tool.
/// </summary>
internal sealed class PresentationToolCall
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationToolCall"/> class.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="session">The workspace session.</param>
    /// <param name="logger">The tool's logger.</param>
    public PresentationToolCall(IServiceProvider services, PresentationArguments arguments, PresentationToolSession session, ILogger logger)
    {
        Services = services;
        Arguments = arguments;
        Session = session;
        Logger = logger;
    }

    /// <summary>
    /// Gets the request services.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the call's arguments.
    /// </summary>
    public PresentationArguments Arguments { get; }

    /// <summary>
    /// Gets the workspace session.
    /// </summary>
    public PresentationToolSession Session { get; }

    /// <summary>
    /// Gets the tool's logger.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Gets the deck the call names in its <c>presentation</c> argument, or the active deck.
    /// </summary>
    /// <returns>The deck.</returns>
    public PresentationDeckState Deck()
    {
        return Session.RequireDeck(Arguments.String("presentation", "deck", "presentation_name"));
    }

    /// <summary>
    /// Reads the deck's structure and text, without its pictures.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The model.</returns>
    public Task<PresentationModel> ReadAsync(PresentationDeckState deck, CancellationToken cancellationToken)
    {
        return Session.ReadAsync(deck, PresentationReadOptions.TextOnly, cancellationToken);
    }

    /// <summary>
    /// Reads the slide numbers the call names, checked against the deck.
    /// </summary>
    /// <param name="model">The deck's model.</param>
    /// <param name="required">Whether at least one slide must be named.</param>
    /// <param name="names">The argument's name and aliases.</param>
    /// <returns>The slide numbers.</returns>
    public List<int> Slides(PresentationModel model, bool required, params string[] names)
    {
        var slides = Arguments.Slides(model.Slides.Count, names.Length == 0 ? ["slides", "slide", "slide_numbers"] : names);

        if (required && slides.Count == 0)
        {
            throw new PresentationArgumentException("Name the slide(s) by number, such as 3, [2, 4] or \"2-5\".");
        }

        foreach (var slide in slides)
        {
            if (slide < 1 || slide > model.Slides.Count)
            {
                throw new PresentationArgumentException(model.Slides.Count == 0
                    ? "The deck has no slides yet."
                    : $"There is no slide {slide}: the deck has {model.Slides.Count} slide(s).");
            }
        }

        return slides;
    }

    /// <summary>
    /// Reads the one slide the call names.
    /// </summary>
    /// <param name="model">The deck's model.</param>
    /// <returns>The slide number.</returns>
    public int Slide(PresentationModel model)
    {
        var slides = Slides(model, true, "slide", "slide_number", "slides");

        if (slides.Count != 1)
        {
            throw new PresentationArgumentException("Name exactly one slide.");
        }

        return slides[0];
    }
}
