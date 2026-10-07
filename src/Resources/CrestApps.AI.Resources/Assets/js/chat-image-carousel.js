/*
 * Stacks the pictures a single answer shows one after another into one carousel.
 *
 * A tool that previews a document -- slides, sheets, pages -- hands back one figure marker per picture, all on a
 * line of their own, and each one renders at full thumbnail size: eight slides is eight thumbnails running down
 * the conversation. Here a run of two or more pictures with nothing but whitespace between them becomes a stack
 * of cards instead: the current picture in front, its neighbours fanned out behind it, moved through by swiping,
 * clicking a neighbour, the arrows, the dots or the arrow keys. A picture written mid-sentence keeps its place.
 *
 * It works on the sanitized HTML every chat surface already produces, so the surfaces share one implementation
 * without sharing a renderer, and the carousel is written in the same pass as the message -- there is no frame
 * of full-size pictures before they are gathered up. HTML with no such run is handed back exactly as it came.
 *
 * Loaded on its own, like chat-markers.js, and optional: a surface without it renders the pictures as before.
 */
window.CoreAIChatImageCarousel = window.CoreAIChatImageCarousel || (function () {
    'use strict';

    const imageContainerClass = 'generated-image-container';

    // How far a drag has to travel before it counts as a swipe rather than a click.
    const swipeThreshold = 40;

    // How far a drag has to travel before the card starts following the pointer.
    const dragStartThreshold = 8;

    // Cards further than this from the current one are stacked out of sight behind the last visible one.
    const furthestVisibleOffset = 2;

    // Anything that is content in its own right, so pictures either side of it are not one run.
    const contentBetweenSelector = 'img, svg, canvas, video, audio, iframe, table, hr, input';

    const previousIcon = '<svg viewBox="0 0 16 16" width="12" height="12" aria-hidden="true" focusable="false"><path d="M10 3 5 8l5 5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"></path></svg>';
    const nextIcon = '<svg viewBox="0 0 16 16" width="12" height="12" aria-hidden="true" focusable="false"><path d="m6 3 5 5-5 5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"></path></svg>';

    // A streamed answer is re-rendered on every chunk, and every render builds the carousel afresh. The picture
    // the reader moved to is remembered here so the next render opens on it rather than back on the first.
    const activeIndexByCarouselKey = new Map();

    let drag = null;
    let suppressNextClick = false;


    function stackAdjacentImages(html) {
        if (typeof document === 'undefined' || !html) {
            return html;
        }

        const firstContainerIndex = html.indexOf(imageContainerClass);

        if (firstContainerIndex < 0 || html.indexOf(imageContainerClass, firstContainerIndex + imageContainerClass.length) < 0) {
            return html;
        }

        const template = document.createElement('template');
        template.innerHTML = html;

        const runs = findImageRuns(template.content);

        if (runs.length === 0) {
            return html;
        }

        for (const run of runs) {
            buildCarousel(run);
        }

        return template.innerHTML;
    }


    function findImageRuns(root) {
        const runs = [];
        let run = [];

        for (const container of root.querySelectorAll('.' + imageContainerClass)) {
            // Only a picture standing in a paragraph is gathered. One in a list, a table cell or a link belongs
            // to that structure, and lifting it out would change what the answer says.
            if (!container.parentElement || container.parentElement.tagName !== 'P') {
                runs.push(run);
                run = [];
                continue;
            }

            if (run.length > 0 && !isOnlyWhitespaceBetween(run[run.length - 1], container)) {
                runs.push(run);
                run = [];
            }

            run.push(container);
        }

        runs.push(run);

        return runs.filter(candidate => candidate.length > 1);
    }


    // Two pictures belong to one run when nothing a reader would read sits between them: spaces and line breaks
    // within a paragraph, or the boundary between two paragraphs that hold nothing else.
    function isOnlyWhitespaceBetween(previous, next) {
        const range = previous.ownerDocument.createRange();
        range.setStartAfter(previous);
        range.setEndBefore(next);

        const between = range.cloneContents();

        return between.textContent.trim() === '' && !between.querySelector(contentBetweenSelector);
    }


    function buildCarousel(containers) {
        const ownerDocument = containers[0].ownerDocument;
        const firstParagraph = containers[0].parentElement;
        const vacatedParagraphs = new Set();

        // Spans throughout, for the same reason the picture's own wrapper is one: the carousel sits inside the
        // paragraph the first picture was in, and a block element there would close that paragraph.
        const carousel = createElement(ownerDocument, 'span', 'ai-image-carousel');
        carousel.tabIndex = 0;
        carousel.setAttribute('role', 'region');
        carousel.setAttribute('aria-roledescription', 'carousel');
        carousel.setAttribute('aria-label', `${containers.length} images`);
        carousel.dataset.carouselKey = getCarouselKey(containers);

        const stage = createElement(ownerDocument, 'span', 'ai-image-carousel-stage');
        const controls = createElement(ownerDocument, 'span', 'ai-image-carousel-controls');
        const caption = createElement(ownerDocument, 'span', 'ai-image-carousel-caption');
        const navigation = createElement(ownerDocument, 'span', 'ai-image-carousel-navigation');
        const dots = createElement(ownerDocument, 'span', 'ai-image-carousel-dots');

        caption.setAttribute('aria-live', 'polite');
        containers[0].before(carousel);

        containers.forEach((container, index) => {
            removeSeparatorsBefore(container);

            if (container.parentElement !== firstParagraph) {
                vacatedParagraphs.add(container.parentElement);
            }

            container.classList.add('ai-image-carousel-item');

            const image = container.querySelector('img');

            // The browser's own image drag would otherwise take over the pointer the moment a swipe starts.
            if (image) {
                image.setAttribute('draggable', 'false');
            }

            const dot = createElement(ownerDocument, 'button', 'ai-image-carousel-dot');
            dot.type = 'button';
            dot.dataset.carouselTarget = index;
            dot.setAttribute('aria-label', `Show image ${index + 1}`);
            dots.appendChild(dot);

            stage.appendChild(container);
        });

        navigation.append(
            createStepButton(ownerDocument, -1, 'Previous image', previousIcon),
            dots,
            createStepButton(ownerDocument, 1, 'Next image', nextIcon));

        controls.append(caption, navigation);
        carousel.append(stage, controls);

        for (const paragraph of vacatedParagraphs) {
            if (paragraph.textContent.trim() === '' && !paragraph.querySelector(contentBetweenSelector)) {
                paragraph.remove();
            }
        }

        showImage(carousel, activeIndexByCarouselKey.get(carousel.dataset.carouselKey) || 0);
    }


    // Whatever separated this picture from the one before it -- a space, a line break -- is all that is left once
    // the pictures have moved into the carousel, and it would otherwise trail the carousel as a blank line.
    function removeSeparatorsBefore(container) {
        let sibling = container.previousSibling;

        while (sibling && ((sibling.nodeType === Node.TEXT_NODE && sibling.textContent.trim() === '') || sibling.nodeName === 'BR')) {
            const previousSibling = sibling.previousSibling;
            sibling.remove();
            sibling = previousSibling;
        }
    }


    // Cheap rather than exact: a generated image can be a data URI hundreds of kilobytes long, so only the tail
    // of the first address -- where a served document's identifier sits -- and the number of pictures are used.
    function getCarouselKey(containers) {
        const firstImage = containers[0].querySelector('img');
        const firstSource = firstImage ? firstImage.getAttribute('src') || '' : '';

        return `${containers.length}:${firstSource.slice(-120)}`;
    }


    function createElement(ownerDocument, tagName, className) {
        const element = ownerDocument.createElement(tagName);
        element.className = className;

        return element;
    }


    function createStepButton(ownerDocument, step, label, icon) {
        const button = createElement(ownerDocument, 'button', 'ai-image-carousel-button');
        button.type = 'button';
        button.dataset.carouselStep = step;
        button.setAttribute('aria-label', label);
        button.innerHTML = icon;

        return button;
    }


    function getItems(carousel) {
        return carousel.querySelectorAll('.ai-image-carousel-stage > .ai-image-carousel-item');
    }


    function getActiveIndex(carousel) {
        return Number(carousel.dataset.activeIndex) || 0;
    }


    function showImage(carousel, requestedIndex) {
        const items = getItems(carousel);

        if (items.length === 0) {
            carousel.remove();

            return;
        }

        const activeIndex = Math.max(0, Math.min(items.length - 1, requestedIndex));
        const dots = carousel.querySelector('.ai-image-carousel-dots');

        while (dots.children.length > items.length) {
            dots.lastElementChild.remove();
        }

        items.forEach((item, index) => {
            const offset = index - activeIndex;
            const isActive = offset === 0;
            const downloadLink = item.querySelector('a');

            item.dataset.stackOffset = Math.max(-furthestVisibleOffset - 1, Math.min(furthestVisibleOffset + 1, offset));
            item.classList.toggle('is-active', isActive);
            item.setAttribute('aria-hidden', isActive ? 'false' : 'true');

            // A card behind the current one is hidden from assistive technology, so nothing in it may take focus.
            if (downloadLink) {
                downloadLink.tabIndex = isActive ? 0 : -1;
            }

            dots.children[index].classList.toggle('is-active', isActive);
            dots.children[index].setAttribute('aria-current', isActive ? 'true' : 'false');
        });

        const activeImage = items[activeIndex].querySelector('img');
        const altText = activeImage ? activeImage.getAttribute('alt') : '';

        carousel.dataset.activeIndex = activeIndex;
        carousel.dataset.imageCount = items.length;
        carousel.querySelector('[data-carousel-step="-1"]').disabled = activeIndex === 0;
        carousel.querySelector('[data-carousel-step="1"]').disabled = activeIndex === items.length - 1;
        carousel.querySelector('.ai-image-carousel-caption').textContent = altText
            ? `${activeIndex + 1} / ${items.length} · ${altText}`
            : `${activeIndex + 1} / ${items.length}`;

        activeIndexByCarouselKey.set(carousel.dataset.carouselKey, activeIndex);
    }


    function stopEvent(event) {
        event.preventDefault();
        event.stopPropagation();
    }


    // Captured, so it runs before anything bound to the picture itself: a card behind the current one is brought
    // to the front, and is not zoomed or downloaded on the way.
    function onClick(event) {
        const carousel = event.target.closest && event.target.closest('.ai-image-carousel');

        if (!carousel) {
            return;
        }

        // A click raised from the keyboard has no pointer behind it, so a swipe that ended without one -- a touch
        // swipe, or a drag released outside the carousel -- must not swallow it.
        if (suppressNextClick && event.detail > 0) {
            suppressNextClick = false;
            stopEvent(event);

            return;
        }

        const stepButton = event.target.closest('[data-carousel-step]');

        if (stepButton) {
            stopEvent(event);
            showImage(carousel, getActiveIndex(carousel) + Number(stepButton.dataset.carouselStep));

            return;
        }

        const dot = event.target.closest('[data-carousel-target]');

        if (dot) {
            stopEvent(event);
            showImage(carousel, Number(dot.dataset.carouselTarget));

            return;
        }

        const item = event.target.closest('.ai-image-carousel-item');

        if (item && !item.classList.contains('is-active')) {
            stopEvent(event);
            showImage(carousel, Array.prototype.indexOf.call(getItems(carousel), item));
        }
    }


    // Only while the carousel has focus, so the arrow keys still move the caret in the prompt.
    function onKeyDown(event) {
        if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
            return;
        }

        const carousel = event.target.closest && event.target.closest('.ai-image-carousel');

        if (!carousel) {
            return;
        }

        event.preventDefault();
        showImage(carousel, getActiveIndex(carousel) + (event.key === 'ArrowRight' ? 1 : -1));
    }


    function onPointerDown(event) {
        suppressNextClick = false;

        const stage = event.button === 0 && event.target.closest && event.target.closest('.ai-image-carousel-stage');

        if (!stage) {
            return;
        }

        drag = {
            stage: stage,
            pointerId: event.pointerId,
            startX: event.clientX,
            startY: event.clientY,
            distance: 0,
            isSwiping: false,
        };
    }


    // The pointer is not captured: a captured pointer retargets the click that follows to the stage, and the click
    // is what brings a card forward or opens the zoom. Listening on the document follows the pointer anyway.
    function onPointerMove(event) {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }

        const distance = event.clientX - drag.startX;

        if (!drag.isSwiping) {
            if (Math.abs(distance) < dragStartThreshold || Math.abs(distance) < Math.abs(event.clientY - drag.startY)) {
                return;
            }

            drag.isSwiping = true;
            drag.stage.classList.add('is-dragging');
        }

        drag.distance = distance;
        drag.stage.style.setProperty('--ai-image-carousel-drag', `${distance}px`);
    }


    function onPointerUp(event) {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }

        const { stage, distance, isSwiping } = drag;

        endDrag();

        if (!isSwiping) {
            return;
        }

        // The click the browser fires at the end of a drag is not a click on whatever the pointer ended over.
        suppressNextClick = true;

        if (Math.abs(distance) >= swipeThreshold) {
            const carousel = stage.closest('.ai-image-carousel');
            showImage(carousel, getActiveIndex(carousel) + (distance < 0 ? 1 : -1));
        }
    }


    function endDrag() {
        if (!drag) {
            return;
        }

        drag.stage.classList.remove('is-dragging');
        drag.stage.style.removeProperty('--ai-image-carousel-drag');
        drag = null;
    }


    // A picture the server will not serve is taken out of the stack and put back just after it, where the
    // surface's own handler hides it and says it could not be loaded, exactly as it does for a lone picture.
    // Captured on the document because an image's error event does not bubble.
    function onImageError(event) {
        const item = event.target.tagName === 'IMG' && event.target.closest('.ai-image-carousel-item');

        if (!item) {
            return;
        }

        const carousel = item.closest('.ai-image-carousel');

        item.classList.remove('ai-image-carousel-item', 'is-active');
        item.removeAttribute('data-stack-offset');
        item.removeAttribute('aria-hidden');
        carousel.after(item);
        showImage(carousel, getActiveIndex(carousel));
    }


    if (typeof document !== 'undefined') {
        document.addEventListener('click', onClick, true);
        document.addEventListener('keydown', onKeyDown);
        document.addEventListener('pointerdown', onPointerDown);
        document.addEventListener('pointermove', onPointerMove);
        document.addEventListener('pointerup', onPointerUp);
        document.addEventListener('pointercancel', endDrag);
        document.addEventListener('error', onImageError, true);
    }

    return {
        stackAdjacentImages: stackAdjacentImages,
    };
})();
