/*
 * Markers the host expands on the model's behalf.
 *
 * Retrieval hands the model a short label such as [fig:1] rather than a figure's address, because a model
 * asked to reproduce a long opaque identifier reproduces its shape and varies the digits instead --
 * producing addresses that look right and resolve to nothing. The model therefore never types an address,
 * and this turns the label it does type back into the picture.
 *
 * Loaded on its own so every chat surface shares one implementation: the two shared chat scripts and the
 * MVC chat-interaction view, which renders its markdown with its own inline marked setup.
 */
window.CoreAIChatMarkers = window.CoreAIChatMarkers || (function () {
    'use strict';

    // Used when a figure has no caption, so the image is still announced as something.
    const defaultImageAltText = 'Figure';

    /*
     * Makes a caption safe to sit inside the alt text of ![alt](link).
     *
     * Captions really do contain brackets, and an unescaped one closes the alt text early: a caption of
     * "Figure 2 (revised) [draft]" would end the image after "Figure 2 (revised) " and leave the rest of the
     * syntax on the page as markup. Parentheses are harmless there, only the brackets are. A caption that spans
     * lines would end the paragraph the image lives in, so its whitespace is collapsed to single spaces.
     */
    function escapeImageAltText(title) {
        const text = typeof title === 'string' ? title.replace(/\s+/g, ' ').trim() : '';

        if (!text) {
            return defaultImageAltText;
        }

        // The backslash is escaped as well, or a caption ending in one would escape the bracket that closes
        // the alt text and the image would swallow the rest of the line.
        return text.replace(/[\\\[\]]/g, '\\$&');
    }

    /*
     * Makes a link safe to sit inside the destination of ![alt](link): the few characters that would end the
     * destination early are percent-encoded, which leaves the URL addressing the same resource. The link is
     * still run through sanitizeUrl by the image renderer, so this is about parsing, not about safety.
     */
    function encodeImageLink(link) {
        return link.replace(/[\s()<>"\\]/g, function (character) {
            return '%' + character.charCodeAt(0).toString(16).toUpperCase().padStart(2, '0');
        });
    }

    /*
     * Returns the content with every marker the reference map describes as a servable image replaced by
     * markdown image syntax, so the ordinary image renderer draws it with its thumbnail, its download button
     * and its size cap. Pure: nothing here touches the DOM or the message.
     *
     * A marker is replaced only when its reference is an image and carries a link. Anything else is left
     * exactly as written -- a marker the model invented for a figure that was never in the results, or one
     * whose picture the host cannot serve, reaches the reader as the few characters the model typed rather
     * than as a broken image.
     */
    function expandImageMarkers(content, references) {
        if (typeof content !== 'string' || !content) {
            return typeof content === 'string' ? content : '';
        }

        if (!references || typeof references !== 'object') {
            return content;
        }

        let expanded = content;

        for (const marker of Object.keys(references)) {
            const reference = references[marker];

            if (!marker || !reference || typeof reference !== 'object') {
                continue;
            }

            if ((reference.isImage ?? reference.IsImage) !== true) {
                continue;
            }

            const rawLink = reference.link ?? reference.Link;
            const link = typeof rawLink === 'string' ? rawLink.trim() : '';

            if (!link) {
                continue;
            }

            const image = `![${escapeImageAltText(reference.title ?? reference.Title)}](${encodeImageLink(link)})`;

            // Every occurrence, and through a replacer function so a '$' in a caption or a link is not read as
            // a replacement pattern.
            expanded = expanded.replaceAll(marker, function () { return image; });
        }

        return expanded;
    }

    // Deliberately small: one pure function, for the host below and for the tests.
    return {
        expandImageMarkers: expandImageMarkers
    };
})();
