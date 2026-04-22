window.openAIChatManager = function () {

    // Defaults (can be overridden by instanceConfig)
    var defaultConfig = {
        // UI defaults for generated media
        generatedImageAltText: 'Generated Image',
        generatedImageMaxWidth: 400,
        downloadImageTitle: 'Download image',
        downloadChartTitle: 'Download chart as image',
        downloadChartButtonText: 'Download',
        userLabel: 'You',
        assistantLabel: 'Assistant',
        thumbsUpTitle: 'Thumbs up',
        thumbsDownTitle: 'Thumbs down',
        copyTitle: 'Click here to copy response to clipboard.',
        codeCopiedText: 'Copied!',
        widget: {
            chatWidgetContainer: null,
            chatWidgetStateName: null,
            chatHistorySection: null,
            showHistoryButton: null,
            closeHistoryButton: null,
            newChatButton: null,
            toggleButtonSelector: null,
            resetSizeButtonSelector: null,
            dragHandleSelector: '.ai-chat-widget-header',
            enableDragging: true,
            enableResizing: true,
            persistLayout: true,
            defaultWidth: null,
            defaultHeight: null,
            minWidth: 320,
            minHeight: 420
        },
        messageTemplate: `
        <div class="ai-chat-messages">
            <div v-for="(message, index) in messages" :key="'msg-' + index" class="ai-chat-message-item">
                <div>
                    <div v-if="message.role === 'user'" class="ai-chat-msg-role ai-chat-msg-role-user">{{ userLabel }}</div>
                    <div v-else-if="message.role !== 'indicator'" :class="getAssistantRoleClasses(message)">
                        <span :class="getAssistantIconClasses(message, index)"><span :class="getAssistantIcon(message)"></span></span>
                        {{ getAssistantLabel(message) }}
                    </div>
                    <div class="ai-chat-message-body lh-base">
                        <h4 v-if="message.title">{{ message.title }}</h4>
                        <div v-html="message.htmlContent"></div>
                        <ol v-if="message.citationReferences && message.citationReferences.length" class="ai-chat-citation-list">
                            <li v-for="citation in message.citationReferences" :key="'citation-' + (citation.referenceKey || citation.displayIndex)" class="ai-chat-citation-item">
                                <a v-if="citation.link" :href="citation.link" target="_blank" rel="noopener noreferrer">{{ citation.label }}</a>
                                <span v-else>{{ citation.label }}</span>
                            </li>
                        </ol>
                        <span class="message-buttons-container" v-if="!isIndicator(message)">
                            <template v-if="metricsEnabled && message.role === 'assistant'">
                                <span class="ai-chat-message-assistant-feedback" :data-message-id="message.id">
                                    <button class="btn btn-sm btn-link text-success p-0 me-2 button-message-toolbox rate-up-btn" @click="rateMessage(message, true, $event)" :title="thumbsUpTitle">
                                        <span class="fa-regular fa-thumbs-up"></span>
                                    </button>
                                    <button class="btn btn-sm btn-link text-danger p-0 me-2 button-message-toolbox rate-down-btn" @click="rateMessage(message, false, $event)" :title="thumbsDownTitle">
                                        <span class="fa-regular fa-thumbs-down"></span>
                                    </button>
                                </span>
                            </template>
                            <button v-if="textToSpeechEnabled && !isConversationMode && message.role === 'assistant' && !message.isStreaming" class="btn btn-sm btn-link text-secondary p-0 me-1 button-message-toolbox" :class="{ 'tts-playing': ttsPlayingMessageIndex === index }" :data-tts-message-index="index" @click="toggleMessageTts(message, index)" :title="ttsPlayingMessageIndex === index ? 'Pause audio' : 'Read aloud'">
                                <span :class="ttsPlayingMessageIndex === index ? 'fa-solid fa-circle-pause' : 'fa-solid fa-circle-play'"></span>
                            </button>
                            <button class="btn btn-sm btn-link text-secondary p-0 button-message-toolbox" @click="copyResponse(message)" :title="copyTitle">
                                <span class="fa-solid fa-copy"></span>
                            </button>
                        </span>
                    </div>
                </div>
            </div>
            <div v-for="notification in notifications" :key="'notif-' + notification.type" class="ai-chat-notification" :class="'ai-chat-notification-' + (notification.type || 'info') + ' ' + (notification.cssClass || '')">
                <div class="ai-chat-notification-content">
                    <span v-if="notification.icon" :class="notification.icon" class="ai-chat-notification-icon"></span>
                    <span class="ai-chat-notification-text">{{ notification.content }}</span>
                    <button v-if="notification.dismissible" class="btn btn-sm btn-link p-0 ms-2 ai-chat-notification-dismiss" @click="dismissNotification(notification.type)" title="Dismiss">
                        <span class="fa-solid fa-xmark"></span>
                    </button>
                </div>
                <div v-if="notification.actions && notification.actions.length" class="ai-chat-notification-actions">
                    <button v-for="action in notification.actions" :key="action.name" class="btn btn-sm" :class="action.cssClass || 'btn-outline-secondary'" @click="handleNotificationAction(notification.type, action.name)">
                        <span v-if="action.icon" :class="action.icon" class="me-1"></span>
                        {{ action.label }}
                    </button>
                </div>
            </div>
        </div>
    `,
        indicatorTemplate: `
        <div class="ai-chat-msg-role ai-chat-msg-role-assistant">
            <span class="ai-streaming-icon"><span class="fa fa-robot" style="display: inline-block;"></span></span>
            Assistant
        </div>
    `
    };

    // Sanitize URLs to prevent javascript: protocol injection.
    function sanitizeUrl(url) {
        if (!url) return '';
        var trimmed = url.trim();
        if (/^javascript:/i.test(trimmed) || /^vbscript:/i.test(trimmed) || /^data:text\/html/i.test(trimmed)) {
            return '';
        }
        return url;
    }

    // Safely HTML-encode a string using the DOM (avoids regex-based HTML filtering).
    function escapeHtmlEntities(text) {
        var span = document.createElement('span');
        span.textContent = text;
        return span.innerHTML;
    }

    function parsePixelValue(value) {
        if (typeof value === 'number' && Number.isFinite(value)) {
            return value;
        }

        if (typeof value !== 'string') {
            return null;
        }

        var parsed = parseFloat(value);
        return Number.isFinite(parsed) ? parsed : null;
    }

    function clamp(value, min, max) {
        return Math.min(Math.max(value, min), max);
    }

    function normalizeReference(reference) {
        if (!reference || typeof reference !== 'object') {
            return null;
        }

        const normalized = Object.assign({}, reference);
        normalized.index = normalized.index ?? normalized.Index ?? 0;
        normalized.text = normalized.text ?? normalized.Text ?? null;
        normalized.title = normalized.title ?? normalized.Title ?? null;
        normalized.link = sanitizeUrl(normalized.link ?? normalized.Link ?? null);

        return normalized;
    }

    function normalizeReferences(references) {
        if (!references || typeof references !== 'object') {
            return {};
        }

        const normalized = {};

        for (const [key, value] of Object.entries(references)) {
            normalized[key] = normalizeReference(value) ?? {};
        }

        return normalized;
    }

    function getCitationLabel(reference, key) {
        return reference.title || reference.text || key;
    }

    function buildCitationDisplay(content, references) {
        let processedContent = (content || '').trim();
        const messageReferences = normalizeReferences(references);

        if (!processedContent || !Object.keys(messageReferences).length) {
            return { content: processedContent, citations: [] };
        }

        const citedRefs = Object.entries(messageReferences).filter(([key]) => processedContent.includes(key));

        if (!citedRefs.length) {
            return { content: processedContent, citations: [] };
        }

        citedRefs.sort(([, a], [, b]) => a.index - b.index);

        const citations = [];
        let displayIndex = 1;

        for (const [key, value] of citedRefs) {
            const placeholder = `__CITE_${displayIndex}_${value.index || displayIndex}__`;
            processedContent = processedContent.replaceAll(key, placeholder);
            citations.push({
                referenceKey: key,
                displayIndex: displayIndex,
                label: getCitationLabel(value, key),
                link: value.link || null,
                placeholder: placeholder,
            });

            displayIndex++;
        }

        for (const citation of citations) {
            processedContent = processedContent.replaceAll(citation.placeholder, `<sup>${citation.displayIndex}</sup>`);
        }

        processedContent = processedContent.replaceAll('</sup><sup>', '</sup><sup>,</sup><sup>');

        return {
            content: processedContent,
            citations: citations.map(({ placeholder, ...citation }) => citation),
        };
    }

    function buildCopyContent(content, citations) {
        let copyContent = (content || '').trim();

        if (!copyContent || !Array.isArray(citations) || citations.length === 0) {
            return copyContent;
        }

        for (const citation of citations) {
            copyContent = copyContent.replaceAll(citation.referenceKey, `[${citation.displayIndex}]`);
        }

        copyContent += '\n\nReferences:\n';

        for (const citation of citations) {
            copyContent += `${citation.displayIndex}. ${citation.label}`;

            if (citation.link) {
                copyContent += ` - ${citation.link}`;
            }

            copyContent += '\n';
        }

        return copyContent.trimEnd();
    }

    function updateMessagePresentation(message, references) {
        const messageReferences = normalizeReferences(references ?? message.references);
        const rawContent = typeof message.rawContent === 'string'
            ? message.rawContent
            : typeof message.content === 'string'
                ? message.content
                : '';
        const citationDisplay = buildCitationDisplay(rawContent, messageReferences);

        message.rawContent = rawContent;
        message.content = rawContent;
        message.displayContent = citationDisplay.content;
        message.references = messageReferences;
        message.citationReferences = citationDisplay.citations;
        message.copyContent = buildCopyContent(rawContent, citationDisplay.citations);
        message.htmlContent = parseMarkdownContent(citationDisplay.content, message);

        return message;
    }

    const renderer = new marked.Renderer();

    // Modify the link rendering to open in a new tab
    renderer.link = function (data) {
        var href = sanitizeUrl(data.href);
        if (!href) return data.text || '';
        return `<a href="${href}" target="_blank" rel="noopener noreferrer">${data.text}</a>`;
    };

    // Custom code block renderer with highlight.js integration and copy button.
    renderer.code = function (data) {
        var code = data.text || '';
        var lang = (data.lang || '').trim();
        var highlighted = code;

        if (typeof hljs !== 'undefined') {
            if (lang && hljs.getLanguage(lang)) {
                try {
                    highlighted = hljs.highlight(code, { language: lang }).value;
                } catch (_) { }
            } else {
                try {
                    highlighted = hljs.highlightAuto(code).value;
                } catch (_) { }
            }
        } else {
            highlighted = escapeHtmlEntities(code);
        }

        var langDisplay = lang ? escapeHtmlEntities(lang) : 'code';
        return `<div class="ai-code-block"><div class="ai-code-header"><span class="ai-code-lang"><span class="fa-solid fa-code"></span> ${langDisplay}</span><button type="button" class="ai-code-copy-btn" title="Copy code"><span class="fa-regular fa-copy"></span></button></div><pre><code class="hljs${lang ? ' language-' + lang : ''}">${highlighted}</code></pre></div>`;
    };

    // Custom image renderer for generated images with thumbnail styling and download button.
    // Handles both URL and data-URI sources (data URIs are converted to blobs for download).
    renderer.image = function (data) {
        const src = sanitizeUrl(data.href);
        if (!src) return '';
        const alt = data.text || defaultConfig.generatedImageAltText;
        const maxWidth = defaultConfig.generatedImageMaxWidth;
        return `<div class="generated-image-container">
        <img src="${src}" alt="${alt}" class="img-thumbnail" style="max-width: ${maxWidth}px; height: auto;" />
        <div class="mt-2">
            <a href="${src}" target="_blank" download="${alt}" title="${defaultConfig.downloadImageTitle}" class="btn btn-sm btn-outline-secondary ai-download-image">
                <span class="fa-solid fa-download"></span>
            </a>
        </div>
    </div>`;
    };

    // Chart counter for unique IDs
    let chartCounter = 0;

    // Collector for charts discovered during marked parsing.
    let _pendingCharts = [];

    // Global chart config map: any page (e.g., Chat Interactions) that uses
    // the shared marked instance can call window.renderPendingCharts() after
    // its DOM update to render charts it didn't create itself.
    window.__chartConfigs = window.__chartConfigs || {};

    function createChartHtml(chartId) {
        return `<div class="chart-container" style="position: relative; width: 100%; max-width: 560px; min-height: 420px;">`
            + `<canvas id="${chartId}"></canvas>`
            + `</div>`
            + `<div class="mt-2">`
            + `<button type="button" class="btn btn-sm btn-outline-secondary download-chart-btn" data-chart-id="${chartId}" title="${defaultConfig.downloadChartTitle}">`
            + `<span class="fa-solid fa-download"></span> ${defaultConfig.downloadChartButtonText}`
            + `</button>`
            + `</div>`;
    }

    // Register [chart:{...json...}] as a native marked block extension so the
    // markdown parser handles chart markers inline with surrounding text.
    marked.use({
        extensions: [{
            name: 'chart',
            level: 'block',
            start(src) {
                const idx = src.indexOf('[chart:');
                return idx >= 0 ? idx : undefined;
            },
            tokenizer(src) {
                const extracted = tryExtractChartMarker(src);
                if (!extracted || extracted.startIndex !== 0) {
                    return undefined;
                }

                const chartId = `chat_chart_${++chartCounter}`;

                return {
                    type: 'chart',
                    raw: src.substring(0, extracted.endIndex),
                    chartId: chartId,
                    json: extracted.json,
                };
            },
            renderer(token) {
                _pendingCharts.push({ chartId: token.chartId, config: token.json });
                window.__chartConfigs[token.chartId] = token.json;
                return createChartHtml(token.chartId);
            }
        }]
    });

    // Extract a [chart:{...json...}] marker. This avoids regex issues with nested brackets.
    function tryExtractChartMarker(text) {
        const token = '[chart:';
        const start = text.indexOf(token);
        if (start < 0) {
            return null;
        }

        // Find JSON object boundary by balancing braces
        const jsonStart = start + token.length;
        let i = jsonStart;
        while (i < text.length && (text[i] === ' ' || text[i] === '\n' || text[i] === '\r' || text[i] === '\t')) {
            i++;
        }

        if (i >= text.length || text[i] !== '{') {
            return null;
        }

        let depth = 0;
        let inString = false;
        let escape = false;

        for (; i < text.length; i++) {
            const ch = text[i];

            if (inString) {
                if (escape) {
                    escape = false;
                    continue;
                }
                if (ch === '\\') {
                    escape = true;
                    continue;
                }
                if (ch === '"') {
                    inString = false;
                }
                continue;
            }

            if (ch === '"') {
                inString = true;
                continue;
            }

            if (ch === '{') {
                depth++;
            } else if (ch === '}') {
                depth--;
                if (depth === 0) {
                    const jsonEnd = i;
                    // Expect closing bracket after JSON
                    const closeBracketIndex = text.indexOf(']', jsonEnd + 1);
                    if (closeBracketIndex < 0) {
                        return null;
                    }

                    const json = text.substring(jsonStart, jsonEnd + 1).trim();
                    return {
                        startIndex: start,
                        endIndex: closeBracketIndex + 1,
                        json: json
                    };
                }
            }
        }

        return null;
    }

    function renderChartsInMessage(message) {
        if (!message || !message._pendingCharts || !message._pendingCharts.length) {
            return;
        }

        // Copy and clear pending charts immediately to prevent duplicate renders.
        const charts = [...message._pendingCharts];
        message._pendingCharts = [];

        // Defer to requestAnimationFrame so the browser has fully laid out the
        // canvas elements before Chart.js reads their dimensions.
        requestAnimationFrame(() => {
            for (const c of charts) {
                renderChartOnCanvas(c.chartId, c.config);
            }
        });
    }

    function renderChartOnCanvas(chartId, config) {
        const canvas = document.getElementById(chartId);
        if (!canvas) {
            return false;
        }

        if (typeof Chart === 'undefined') {
            console.warn('Chart.js is not loaded. To render interactive charts, include the Chart.js library on the page (e.g., <script src="https://cdn.jsdelivr.net/npm/chart.js@4/dist/chart.umd.min.js"></script>).');
            return false;
        }

        // When the canvas is inside a hidden container (e.g., a widget panel with
        // display:none), it has zero dimensions and Chart.js cannot render correctly.
        // Keep the config in __chartConfigs so renderPendingCharts() can retry later
        // once the container becomes visible.
        if (canvas.offsetParent === null) {
            window.__chartConfigs[chartId] = config;
            return false;
        }

        try {
            if (canvas._chartInstance) {
                canvas._chartInstance.destroy();
            }

            const cfg = typeof config === 'string' ? JSON.parse(config) : config;
            cfg.options ??= {};
            cfg.options.responsive = true;
            cfg.options.maintainAspectRatio = true;
            cfg.options.aspectRatio ??= 4 / 3;

            canvas._chartInstance = new Chart(canvas, cfg);
            delete window.__chartConfigs[chartId];
            return true;
        } catch (e) {
            console.error('Error creating chart:', e);
            return false;
        }
    }

    // Global function: renders any chart canvases whose configs are in the
    // global __chartConfigs map. Called by pages (e.g., Chat Interactions)
    // that share the marked instance but have their own rendering pipeline.
    window.renderPendingCharts = function () {
        if (typeof Chart === 'undefined') {
            return;
        }

        const configs = window.__chartConfigs;
        if (!configs) {
            return;
        }

        requestAnimationFrame(() => {
            for (const chartId of Object.keys(configs)) {
                renderChartOnCanvas(chartId, configs[chartId]);
            }
        });
    };

    // Parse markdown content via marked (which natively handles [chart:...] markers
    // through the registered extension) and collect pending chart configs for later
    // Chart.js rendering.
    function parseMarkdownContent(content, message) {
        _pendingCharts = [];
        const html = marked.parse(content, { renderer });
        message._pendingCharts = _pendingCharts.length > 0 ? [..._pendingCharts] : [];
        return DOMPurify.sanitize(html, { ADD_TAGS: ['canvas'], ADD_ATTR: ['target'] });
    }

    const initialize = (instanceConfig) => {

        const config = Object.assign({}, defaultConfig, instanceConfig);
        config.widget = Object.assign({}, defaultConfig.widget || {}, instanceConfig && instanceConfig.widget ? instanceConfig.widget : {});
        const hasWidgetConfig = !!(instanceConfig && instanceConfig.widget && instanceConfig.widget.chatWidgetContainer && instanceConfig.widget.chatWidgetStateName);
        const widgetBehavior = window.openAIChatWidgetBehavior || null;
        // Keep defaultConfig in sync so renderers use overridden values
        defaultConfig = config;

        if (!config.signalRHubUrl) {
            console.error('The signalRHubUrl is required.');
            return;
        }

        if (!config.appElementSelector) {
            console.error('The appElementSelector is required.');
            return;
        }

        if (!config.chatContainerElementSelector) {
            console.error('The chatContainerElementSelector is required.');
            return;
        }

        if (!config.inputElementSelector) {
            console.error('The inputElementSelector is required.');
            return;
        }

        if (!config.sendButtonElementSelector) {
            console.error('The sendButtonElementSelector is required.');
            return;
        }

        const appDefinition = {
            data() {
                return {
                    inputElement: null,
                    buttonElement: null,
                    chatContainer: null,
                    placeholder: null,
                    isSessionStarted: false,
                    isPlaceholderVisible: true,
                    isStreaming: false,
                    isNavigatingAway: false,
                    autoScroll: true,
                    stream: null,
                    messages: [],
                    notifications: [],
                    prompt: '',
                    documents: config.existingDocuments || [],
                    isUploading: false,
                    isDocumentOperationPending: false,
                    documentOperationQueue: null,
                    uploadErrors: [],
                    isDragOver: false,
                    documentBar: null,
                    metricsEnabled: !!config.metricsEnabled,
                    userLabel: config.userLabel,
                    assistantLabel: config.assistantLabel,
                    thumbsUpTitle: config.thumbsUpTitle,
                    thumbsDownTitle: config.thumbsDownTitle,
                    copyTitle: config.copyTitle,
                    isRecording: false,
                    mediaRecorder: null,
                    preRecordingPrompt: '',
                    micButton: null,
                    speechToTextEnabled: config.chatMode === 'AudioInput' || config.chatMode === 'Conversation',
                    textToSpeechEnabled: config.chatMode === 'Conversation' || !!config.textToSpeechEnabled,
                    ttsVoiceName: config.ttsVoiceName || null,
                    audioChunks: [],
                    audioPlayQueue: [],
                    isPlayingAudio: false,
                    currentAudioElement: null,
                    currentAudioUrl: null,
                    ttsButton: null,
                    ttsPlayingMessageIndex: -1,
                    ttsAudioCache: {},
                    ttsInstanceId: 'ai-chat-' + Math.random().toString(36).slice(2),
                    singleResponseMode: !!config.singleResponseMode,
                    conversationModeEnabled: config.chatMode === 'Conversation',
                    conversationButton: null,
                    isConversationMode: false,
                    notificationDismissTimers: {},
                    pendingSessionPromise: null,
                    pendingSessionResolver: null,
                    pendingSessionRejector: null,
                    pendingSessionTimeoutId: null,
                };
            },
            computed: {
                lastAssistantIndex() {
                    for (var i = this.messages.length - 1; i >= 0; i--) {
                        if (this.messages[i].role === 'assistant') {
                            return i;
                        }
                    }
                    return -1;
                }
            },
            methods: {
                handleBeforeUnload() {
                    this.isNavigatingAway = true;
                },
                handleDragOver(e) {
                    if (!config.sessionDocumentsEnabled) return;
                    e.preventDefault();
                    e.stopPropagation();
                    this.isDragOver = true;
                    var inputArea = this.inputElement ? this.inputElement.closest('.ai-admin-widget-input, .text-bg-light') : null;
                    if (inputArea) inputArea.classList.add('ai-chat-drag-over');
                },
                handleDragLeave(e) {
                    if (!config.sessionDocumentsEnabled) return;
                    e.preventDefault();
                    e.stopPropagation();
                    this.isDragOver = false;
                    var inputArea = this.inputElement ? this.inputElement.closest('.ai-admin-widget-input, .text-bg-light') : null;
                    if (inputArea) inputArea.classList.remove('ai-chat-drag-over');
                },
                handleDrop(e) {
                    if (!config.sessionDocumentsEnabled) return;
                    e.preventDefault();
                    e.stopPropagation();
                    this.isDragOver = false;
                    var inputArea = this.inputElement ? this.inputElement.closest('.ai-admin-widget-input, .text-bg-light') : null;
                    if (inputArea) inputArea.classList.remove('ai-chat-drag-over');
                    if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                        this.uploadFiles(Array.from(e.dataTransfer.files));
                    }
                },
                triggerFileInput() {
                    if (!config.sessionDocumentsEnabled || this.isDocumentOperationPending) return;
                    var fileInput = document.getElementById('ai-chat-doc-input');
                    if (fileInput) fileInput.click();
                },
                handleFileInputChange(e) {
                    var files = e.target.files ? Array.from(e.target.files) : [];
                    if (files && files.length > 0) {
                        this.uploadFiles(files);
                    }
                    e.target.value = '';
                },
                queueDocumentOperation(operation) {
                    var self = this;
                    var previousOperation = this.documentOperationQueue || Promise.resolve();
                    var nextOperation = previousOperation
                        .catch(function () { })
                        .then(async function () {
                            self.isDocumentOperationPending = true;
                            try {
                                return await operation();
                            } finally {
                                self.isDocumentOperationPending = false;
                            }
                        });

                    this.documentOperationQueue = nextOperation.finally(function () {
                        if (self.documentOperationQueue === nextOperation) {
                            self.documentOperationQueue = null;
                        }
                    });

                    return this.documentOperationQueue;
                },
                async uploadFiles(files) {
                    if (!config.uploadDocumentUrl) return;

                    var filesToUpload = Array.isArray(files) ? files.slice() : Array.from(files || []);
                    if (filesToUpload.length === 0) return;

                    return this.queueDocumentOperation(async () => {
                        var sessionId = this.getSessionId();
                        var profileId = this.getProfileId();

                        if (!sessionId) {
                            try {
                                sessionId = await this.ensureSessionForDocuments(profileId);
                            } catch (err) {
                                console.error('Failed to create a chat session for document upload:', err);
                                this.uploadErrors = [{ fileName: '', error: 'Could not create a chat session for the upload.' }];
                                this.renderDocumentBar();
                                return;
                            }
                        }

                        if (!sessionId) {
                            console.warn('Cannot upload documents without a session or profile.');
                            this.uploadErrors = [{ fileName: '', error: 'Could not create a chat session for the upload.' }];
                            this.renderDocumentBar();
                            return;
                        }

                        this.isUploading = true;
                        this.uploadErrors = [];
                        this.renderDocumentBar();
                        try {
                            var formData = new FormData();
                            formData.append('sessionId', sessionId);
                            for (var i = 0; i < filesToUpload.length; i++) {
                                formData.append('files', filesToUpload[i]);
                            }

                            var response = await fetch(config.uploadDocumentUrl, {
                                method: 'POST',
                                body: formData
                            });

                            if (!response.ok) {
                                var errorText = await response.text();
                                var uploadError = this.extractReadableErrorMessage(errorText, 'Upload failed. Please try again.');
                                console.error('Upload failed:', errorText);
                                this.uploadErrors = [{ fileName: '', error: uploadError }];
                                return;
                            }

                            var result = await response.json();

                            if (result.sessionId && result.sessionId !== this.getSessionId()) {
                                this.initializeSession(result.sessionId);
                            }

                            if (Array.isArray(result.documents)) {
                                this.documents = result.documents;
                            } else if (result.uploaded && result.uploaded.length > 0) {
                                this.documents = this.documents.concat(result.uploaded);
                            }

                            if (result.failed && result.failed.length > 0) {
                                this.uploadErrors = result.failed;
                            }
                        } catch (err) {
                            console.error('Upload error:', err);
                            this.uploadErrors = [{ fileName: '', error: 'Upload failed. Please try again.' }];

                            if (this.getSessionId()) {
                                this.reloadCurrentSession();
                            }
                        } finally {
                            this.isUploading = false;
                            this.renderDocumentBar();
                        }
                    });
                },
                async removeDocument(doc) {
                    if (!config.removeDocumentUrl) return;

                    return this.queueDocumentOperation(async () => {
                        try {
                            var sessionId = this.getSessionId();
                            var response = await fetch(config.removeDocumentUrl, {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ itemId: sessionId, documentId: doc.documentId })
                            });

                            if (response.ok) {
                                var result = await response.json();

                                if (Array.isArray(result.documents)) {
                                    this.documents = result.documents;
                                } else {
                                    var idx = this.documents.indexOf(doc);
                                    if (idx > -1) {
                                        this.documents.splice(idx, 1);
                                    }
                                }
                            } else {
                                var errorText = await response.text();
                                var removeError = this.extractReadableErrorMessage(errorText, 'Failed to remove document. Please try again.');
                                console.error('Failed to remove document:', response.status, errorText);
                                this.uploadErrors = [{ fileName: doc.fileName || '', error: removeError }];
                                if (sessionId) {
                                    this.reloadCurrentSession();
                                }
                                this.renderDocumentBar();
                            }
                        } catch (err) {
                            console.error('Remove document error:', err);
                            this.uploadErrors = [{ fileName: doc.fileName || '', error: 'Failed to remove document. Please try again.' }];
                            if (this.getSessionId()) {
                                this.reloadCurrentSession();
                            }
                            this.renderDocumentBar();
                        }
                    });
                },
                formatFileSize(bytes) {
                    if (bytes < 1024) return bytes + ' B';
                    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
                    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
                },
                renderDocumentBar() {
                    if (!this.documentBar) return;

                    if (!config.sessionDocumentsEnabled) {
                        this.documentBar.classList.add('d-none');
                        return;
                    }

                    this.documentBar.classList.remove('d-none');

                    var html = '<div class="ai-chat-doc-bar p-2">';
                    html += '<div class="d-flex flex-wrap align-items-center gap-1">';

                    for (var i = 0; i < this.documents.length; i++) {
                        var doc = this.documents[i];
                        var name = doc.fileName || 'Document';
                        if (name.length > 20) name = name.substring(0, 17) + '...';
                        html += '<span class="badge bg-secondary bg-opacity-25 text-dark d-inline-flex align-items-center gap-1 px-2 py-1" style="font-size: 0.8rem;" title="' + this.escapeHtml(doc.fileName || '') + '">';
                        html += '<span class="fa-solid fa-file-lines" style="font-size: 0.7rem;"></span> ';
                        html += this.escapeHtml(name);
                        html += ' <button type="button" class="btn-close btn-close-sm ms-1" style="font-size: 0.5rem;" data-doc-index="' + i + '" aria-label="Remove"' + (this.isDocumentOperationPending ? ' disabled' : '') + '></button>';
                        html += '</span>';
                    }

                    for (var m = 0; m < this.uploadErrors.length; m++) {
                        var failedItem = this.uploadErrors[m];
                        var failedName = failedItem.fileName || 'File';
                        var errorMsg = failedItem.error || 'Upload failed';
                        if (failedName.length > 15) failedName = failedName.substring(0, 12) + '...';
                        html += '<span class="badge bg-danger bg-opacity-25 text-danger d-inline-flex align-items-center gap-1 px-2 py-1" style="font-size: 0.8rem;" title="' + this.escapeHtml((failedItem.fileName || '') + ': ' + errorMsg) + '">';
                        html += '<span class="fa-solid fa-circle-exclamation" style="font-size: 0.7rem;"></span> ';
                        html += this.escapeHtml(failedName);
                        html += ' <button type="button" class="btn-close btn-close-sm ms-1" style="font-size: 0.5rem;" data-error-index="' + m + '" aria-label="Dismiss"></button>';
                        html += '</span>';
                    }

                    if (this.isUploading) {
                        html += '<span class="badge bg-info bg-opacity-25 text-dark d-inline-flex align-items-center gap-1 px-2 py-1" style="font-size: 0.8rem;">';
                        html += '<span class="spinner-border spinner-border-sm" style="width: 0.7rem; height: 0.7rem;"></span> Uploading...';
                        html += '</span>';
                    }

                    html += '<button type="button" class="btn btn-sm btn-outline-secondary rounded-pill ai-chat-doc-add-btn d-inline-flex align-items-center gap-1" style="font-size: 0.75rem; padding: 0.15rem 0.5rem;" title="Attach documents"' + (this.isDocumentOperationPending ? ' disabled' : '') + '>';
                    html += '<span class="fa-solid fa-paperclip"></span>';
                    if (this.documents.length === 0 && !this.isUploading) {
                        html += ' <span>Attach files</span>';
                    }
                    html += '</button>';
                    html += '</div>';
                    if (config.supportedExtensionsText) {
                        html += '<div class="small text-muted mt-2">Supported formats: ' + this.escapeHtml(config.supportedExtensionsText) + '</div>';
                    }

                    html += '</div>';

                    this.documentBar.replaceChildren(DOMPurify.sanitize(html, { RETURN_DOM_FRAGMENT: true }));

                    // Bind remove handlers
                    var self = this;
                    var closeButtons = this.documentBar.querySelectorAll('[data-doc-index]');
                    for (var j = 0; j < closeButtons.length; j++) {
                        closeButtons[j].addEventListener('click', (function (idx) {
                            return function (e) {
                                e.preventDefault();
                                e.stopPropagation();
                                if (self.isDocumentOperationPending) {
                                    return;
                                }

                                var docToRemove = self.documents[idx];
                                if (docToRemove) self.removeDocument(docToRemove);
                            };
                        })(parseInt(closeButtons[j].getAttribute('data-doc-index'))));
                    }

                    // Bind error dismiss handlers
                    var errorCloseButtons = this.documentBar.querySelectorAll('[data-error-index]');
                    for (var n = 0; n < errorCloseButtons.length; n++) {
                        errorCloseButtons[n].addEventListener('click', (function (idx) {
                            return function (e) {
                                e.preventDefault();
                                e.stopPropagation();
                                self.uploadErrors.splice(idx, 1);
                                self.renderDocumentBar();
                            };
                        })(parseInt(errorCloseButtons[n].getAttribute('data-error-index'))));
                    }

                    // Bind add button
                    var addBtn = this.documentBar.querySelector('.ai-chat-doc-add-btn');
                    if (addBtn) {
                        addBtn.addEventListener('click', function (e) {
                            e.preventDefault();
                            if (self.isDocumentOperationPending) {
                                return;
                            }
                            self.triggerFileInput();
                        });
                    }
                },
                escapeHtml(text) {
                    var div = document.createElement('div');
                    div.textContent = text;
                    return div.innerHTML;
                },
                extractReadableErrorMessage(errorText, fallbackMessage) {
                    if (!errorText || typeof errorText !== 'string') {
                        return fallbackMessage;
                    }

                    var trimmed = errorText.trim();
                    if (!trimmed) {
                        return fallbackMessage;
                    }

                    if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
                        try {
                            var parsed = JSON.parse(trimmed);
                            var message = parsed?.error || parsed?.message || parsed?.title || parsed?.detail;
                            return typeof message === 'string' && message.trim()
                                ? message.trim()
                                : fallbackMessage;
                        } catch (err) {
                            return fallbackMessage;
                        }
                    }

                    if (/<[^>]+>/.test(trimmed)) {
                        return fallbackMessage;
                    }

                    return trimmed;
                },
                normalizeAssistantAppearance(appearance) {
                    if (!appearance) {
                        return null;
                    }

                    var label = typeof appearance.label === 'string' ? appearance.label.trim() : '';
                    var icon = typeof appearance.icon === 'string' ? appearance.icon.trim() : '';
                    var cssClass = typeof appearance.cssClass === 'string' ? appearance.cssClass.trim() : '';
                    var disableStreamingAnimation = !!appearance.disableStreamingAnimation;

                    if (!label && !icon && !cssClass && !disableStreamingAnimation) {
                        return null;
                    }

                    return {
                        label: label,
                        icon: icon,
                        cssClass: cssClass,
                        disableStreamingAnimation: disableStreamingAnimation,
                    };
                },
                getAssistantLabel(message) {
                    var appearance = message ? this.normalizeAssistantAppearance(message.appearance) : null;
                    return appearance && appearance.label ? appearance.label : this.assistantLabel;
                },
                getAssistantRoleClasses(message) {
                    var appearance = message ? this.normalizeAssistantAppearance(message.appearance) : null;
                    var classes = ['ai-chat-msg-role'];

                    if (appearance && appearance.cssClass) {
                        classes.push(appearance.cssClass);
                    } else {
                        classes.push('ai-chat-msg-role-assistant');
                    }

                    return classes;
                },
                getAssistantIconClasses(message, index) {
                    var appearance = message ? this.normalizeAssistantAppearance(message.appearance) : null;
                    return [this.shouldAnimateAssistantIcon(message, index) ? 'ai-streaming-icon' : 'ai-bot-icon', appearance && appearance.cssClass ? appearance.cssClass : ''];
                },
                getAssistantIcon(message) {
                    var appearance = message ? this.normalizeAssistantAppearance(message.appearance) : null;
                    return appearance && appearance.icon ? appearance.icon : 'fa fa-robot';
                },
                shouldAnimateAssistantIcon(message, index) {
                    var appearance = message ? this.normalizeAssistantAppearance(message.appearance) : null;
                    return !!message &&
                        message.isStreaming &&
                        index === this.lastAssistantIndex &&
                        !(appearance && appearance.disableStreamingAnimation);
                },
                async startConnection() {
                    this.connection = new signalR.HubConnectionBuilder()
                        .withUrl(config.signalRHubUrl)
                        .withAutomaticReconnect()
                        .build();

                    // Allow long-running operations (e.g., multi-step MCP tool calls)
                    // without the client disconnecting prematurely.
                    this.connection.serverTimeoutInMilliseconds = 600000;
                    this.connection.keepAliveIntervalInMilliseconds = 15000;

                    this.connection.on("LoadSession", (data) => {
                        this.initializeSession(data.sessionId, true);
                        this.messages = [];
                        this.documents = data.documents || [];
                        this.uploadErrors = [];

                        (data.messages ?? []).forEach(msg => {
                            this.addMessage(msg);

                            this.$nextTick(() => {
                                renderChartsInMessage(msg);
                            });
                        });

                        // Update feedback icons in the DOM after all messages have rendered.
                        this.$nextTick(() => {
                            this.refreshAllFeedbackIcons();
                        });

                        // When the session is new (no messages) and an initial prompt is configured,
                        // automatically send it as the first user message to trigger an AI response.
                        if (this.messages.length === 0 && config.initialPrompt) {
                            this.prompt = config.initialPrompt;
                            this.sendMessage();
                        }
                    });

                    this.connection.on("ReceiveError", (error) => {
                        console.error("SignalR Error: ", error);

                        if (this.isRecording) {
                            this.stopRecording();
                        }

                        if (widgetBehavior && typeof widgetBehavior.handleReceiveError === 'function') {
                            widgetBehavior.handleReceiveError(this, error, config);
                        }
                    });

                    this.connection.on("MessageRated", (messageId, userRating) => {
                        var msg = this.messages.find(m => m.id === messageId);
                        if (msg) {
                            msg.userRating = userRating;
                        }
                    });

                    this.connection.on("ReceiveTranscript", (sessionId, text, isFinal) => {
                        if (this.isConversationMode) {
                            if (!isFinal && text) {
                                this._conversationPartialTranscript = text;
                                var escaped = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
                                var html = '<p class="ai-partial-transcript">' + escaped + '</p>';

                                // Show partial transcript as a live user message.
                                if (!this._conversationPartialMessage) {
                                    this.hidePlaceholder();
                                    this._conversationPartialMessage = {
                                        role: 'user',
                                        content: text,
                                        htmlContent: html,
                                        isPartial: true
                                    };
                                    this.messages.push(this._conversationPartialMessage);
                                } else {
                                    this._conversationPartialMessage.content = text;
                                    this._conversationPartialMessage.htmlContent = html;
                                }
                                this.scrollToBottom();
                            }
                            return;
                        }

                        if (text && !this._audioInputSent) {
                            this.prompt = this.preRecordingPrompt + text;
                            if (this.inputElement) {
                                this.inputElement.value = this.prompt;
                                this.inputElement.dispatchEvent(new Event('input'));
                            }
                        }
                    });

                    this.connection.on("ReceiveConversationUserMessage", (sessionId, text) => {
                        if (text) {
                            this.stopAudio();

                            // If there's an interrupted assistant message still streaming,
                            // mark it as done to stop the spinner animation.
                            if (this._conversationAssistantMessage) {
                                var oldMsg = this.messages[this._conversationAssistantMessage.index];
                                if (oldMsg) {
                                    oldMsg.isStreaming = false;
                                }
                                this._conversationAssistantMessage = null;
                            }

                            // Replace the partial transcript message with the final one.
                            if (this._conversationPartialMessage) {
                                var escaped = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
                                this._conversationPartialMessage.content = text;
                                this._conversationPartialMessage.htmlContent = '<p>' + escaped + '</p>';
                                this._conversationPartialMessage.isPartial = false;
                                this._conversationPartialMessage = null;
                            } else {
                                this.addMessage({
                                    role: 'user',
                                    content: text
                                });
                            }
                            this.scrollToBottom();
                        }
                    });

                    this.connection.on("ReceiveConversationAssistantToken", (sessionId, messageId, token, responseId, appearance) => {
                        if (!this._conversationAssistantMessage) {
                            this.stopAudio();
                            this.hideTypingIndicator();

                            // Ensure no stale streaming indicators remain from prior messages.
                            for (var j = 0; j < this.messages.length; j++) {
                                if (this.messages[j].isStreaming) {
                                    this.messages[j].isStreaming = false;
                                }
                            }

                            var msgIndex = this.messages.length;
                            var newMessage = {
                                id: messageId,
                                role: "assistant",
                                content: "",
                                htmlContent: "",
                                isStreaming: true,
                                userRating: null,
                                appearance: this.normalizeAssistantAppearance(appearance),
                            };
                            this.messages.push(newMessage);
                            this._conversationAssistantMessage = { index: msgIndex, content: '' };
                        }

                        this._conversationAssistantMessage.content += token;
                        var msg = this.messages[this._conversationAssistantMessage.index];
                        if (msg) {
                            if (!msg.appearance) {
                                msg.appearance = this.normalizeAssistantAppearance(appearance);
                            }
                            msg.content = this._conversationAssistantMessage.content;
                            msg.htmlContent = parseMarkdownContent(msg.content, msg);
                            this.$nextTick(() => {
                                renderChartsInMessage(msg);
                                this.scrollToBottom();
                            });
                        }
                    });

                    this.connection.on("ReceiveConversationAssistantComplete", (sessionId, messageId) => {
                        if (this._conversationAssistantMessage) {
                            var msg = this.messages[this._conversationAssistantMessage.index];
                            if (msg) {
                                msg.isStreaming = false;
                            }
                            this._conversationAssistantMessage = null;
                        }
                    });

                    this.connection.on("ReceiveAudioChunk", (sessionId, base64Audio, contentType) => {
                        if (base64Audio) {
                            const binaryString = atob(base64Audio);
                            const bytes = new Uint8Array(binaryString.length);
                            for (let i = 0; i < binaryString.length; i++) {
                                bytes[i] = binaryString.charCodeAt(i);
                            }
                            this.audioChunks.push(bytes);
                        }
                    });

                    this.connection.on("ReceiveAudioComplete", (sessionId) => {
                        this.playCollectedAudio();
                    });

                    this.connection.on("ReceiveNotification", (notification) => {
                        this.receiveNotification(notification);
                    });

                    this.connection.on("UpdateNotification", (notification) => {
                        this.updateNotification(notification);
                    });

                    this.connection.on("RemoveNotification", (notificationType) => {
                        this.removeNotification(notificationType);
                    });

                    this.connection.onreconnecting(() => {
                        console.warn("SignalR: reconnecting...");
                    });

                    this.connection.onreconnected(() => {
                        console.info("SignalR: reconnected.");

                        if (this.isSessionStarted) {
                            this.reloadCurrentSession();
                        } else if (config.autoCreateSession) {
                            this.startNewSession();
                        }
                    });

                    this.connection.onclose((error) => {
                        if (this.isNavigatingAway) {
                            return;
                        }

                        if (error) {
                            console.warn("SignalR connection closed with error:", error.message || error);
                        }
                    });

                    try {
                        await this.connection.start();
                    } catch (err) {
                        console.error("SignalR Connection Error: ", err);
                    }
                },
                addMessageInternal(message) {
                    if (message.role === 'assistant') {
                        message.appearance = this.normalizeAssistantAppearance(message.appearance);
                    }

                    if (message.content && !message.htmlContent) {
                        message.htmlContent = parseMarkdownContent(message.content, message);
                    }
                    this.fireEvent(new CustomEvent("addingOpenAIPromotMessage", { detail: { message: message } }));
                    this.messages.push(message);

                    this.$nextTick(() => {
                        this.fireEvent(new CustomEvent("addedOpenAIPromotMessage", { detail: { message: message } }));
                    });
                },
                addMessage(message) {

                    // Ensure userRating is always defined for Vue reactivity.
                    if (message.userRating === undefined) {
                        message.userRating = null;
                    }

                    if (message.content) {
                        message.rawContent = message.rawContent ?? message.content;
                        updateMessagePresentation(message, message.references);
                    }

                    this.addMessageInternal(message);
                    this.hidePlaceholder();

                    this.$nextTick(() => {
                        // Render any pending charts once the DOM is updated
                        renderChartsInMessage(message);
                        this.scrollToBottom();
                    });
                },
                addMessages(messages) {

                    for (let i = 0; i < messages.length; i++) {
                        this.addMessageInternal(messages[i]);
                    }

                    this.hidePlaceholder();
                    this.$nextTick(() => {
                        this.scrollToBottom();
                    });
                },
                hidePlaceholder() {
                    if (this.placeholder) {
                        this.placeholder.classList.add('d-none');
                    }
                    this.isPlaceholderVisible = false;
                },
                showPlaceholder() {
                    if (this.placeholder) {
                        this.placeholder.classList.remove('d-none');
                    }
                    this.isPlaceholderVisible = true;
                },
                isOrchestratorAvailable() {
                    return config.isOrchestratorAvailable !== false;
                },
                applyOrchestratorAvailability() {
                    if (this.isOrchestratorAvailable()) {
                        return true;
                    }

                    const unavailableMessage = config.orchestratorUnavailableMessage || "This orchestrator is not currently available.";

                    if (this.inputElement) {
                        this.inputElement.disabled = true;
                        this.inputElement.value = '';
                        this.inputElement.placeholder = unavailableMessage;
                    }

                    if (this.buttonElement) {
                        this.buttonElement.disabled = true;
                    }

                    if (this.micButtonElement) {
                        this.micButtonElement.disabled = true;
                        this.micButtonElement.style.display = 'none';
                    }

                    if (this.conversationButtonElement) {
                        this.conversationButtonElement.disabled = true;
                        this.conversationButtonElement.style.display = 'none';
                    }

                    return false;
                },
                fireEvent(event) {
                    document.dispatchEvent(event);
                },
                isIndicator(message) {
                    return message.role === 'indicator';
                },
                sendMessage() {
                    if (!this.applyOrchestratorAvailability()) {
                        return;
                    }

                    let trimmedPrompt = this.prompt.trim();

                    if (!trimmedPrompt) {
                        return;
                    }

                    // Stop any active recording before sending.
                    if (this.isRecording) {
                        this.stopRecording();
                    }

                    // Prevent stale ReceiveTranscript events from repopulating the prompt.
                    this._audioInputSent = true;

                    // In single-response mode, clear all previous messages.
                    if (this.singleResponseMode) {
                        this.messages.splice(0, this.messages.length);
                    }

                    this.addMessage({
                        role: 'user',
                        content: trimmedPrompt
                    });

                    this.streamMessage(this.getProfileId(), trimmedPrompt, null);
                    this.inputElement.value = '';
                    this.prompt = '';
                },
                startRecording() {
                    if (this.isRecording || !this.connection) {
                        return;
                    }

                    navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } })
                        .then(stream => {
                            var mimeType = MediaRecorder.isTypeSupported('audio/ogg;codecs=opus')
                                ? 'audio/ogg;codecs=opus'
                                : MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
                                    ? 'audio/webm;codecs=opus'
                                    : 'audio/webm';

                            this.mediaRecorder = new MediaRecorder(stream, {
                                mimeType: mimeType,
                                audioBitsPerSecond: 128000,
                            });

                            this.preRecordingPrompt = this.prompt;
                            this._audioInputSent = false;

                            var subject = new signalR.Subject();
                            var profileId = this.getProfileId();
                            var sessionId = this.getSessionId() || '';
                            var pendingChunk = Promise.resolve();

                            this.mediaRecorder.addEventListener('dataavailable', (e) => {
                                if (e.data && e.data.size > 0) {
                                    pendingChunk = pendingChunk.then(async () => {
                                        var data = await e.data.arrayBuffer();
                                        var uint8Array = new Uint8Array(data);
                                        var binaryString = uint8Array.reduce(function (str, byte) { return str + String.fromCharCode(byte); }, '');
                                        var base64 = btoa(binaryString);
                                        subject.next(base64);
                                    });
                                }
                            });

                            this.mediaRecorder.addEventListener('stop', () => {
                                stream.getTracks().forEach(track => track.stop());
                                pendingChunk.then(() => subject.complete());
                            });

                            var language = navigator.language || document.documentElement.lang || 'en-US';
                            this.connection.send("SendAudioStream", profileId, sessionId, subject, mimeType, language);
                            this.mediaRecorder.start(250);
                            this.isRecording = true;
                            this.updateMicButton();
                        })
                        .catch(err => {
                            console.error('Microphone access denied:', err);
                        });
                },
                stopRecording() {
                    if (!this.isRecording || !this.mediaRecorder) {
                        return;
                    }

                    this.mediaRecorder.stop();
                    this.isRecording = false;
                    this.updateMicButton();
                },
                toggleRecording() {
                    if (this.isRecording) {
                        this.stopRecording();
                    } else {
                        this.startRecording();
                    }
                },
                updateMicButton() {
                    if (!this.micButton) {
                        return;
                    }

                    if (this.isRecording) {
                        this.micButton.classList.add('stt-recording');
                        this.micButton.innerHTML = '<span class="fa-solid fa-stop"></span>';
                    } else {
                        this.micButton.classList.remove('stt-recording');
                        this.micButton.innerHTML = '<span class="fa-solid fa-microphone"></span>';
                    }
                },
                streamMessage(profileId, trimmedPrompt, sessionProfileId) {

                    if (this.stream) {
                        this.stream.dispose();
                        this.stream = null;
                    }

                    this.streamingStarted();
                    this.showTypingIndicator();
                    this.autoScroll = true;

                    var content = '';
                    var references = {};
                    var lastResponseId = null;

                    // Get the index after showing typing indicator.
                    var messageIndex = this.messages.length;
                    var currentSessionId = this.getSessionId();

                    this.stream = this.connection.stream("SendMessage", profileId, trimmedPrompt, currentSessionId, sessionProfileId)
                        .subscribe({
                            next: (chunk) => {
                                let message = this.messages[messageIndex];

                                if (!message) {

                                    if (chunk.sessionId && !currentSessionId) {
                                        this.initializeSession(chunk.sessionId);
                                    }

                                    this.hideTypingIndicator();
                                    // Re-assign the index after hiding the typing indicator.
                                    messageIndex = this.messages.length;
                                    let newMessage = {
                                        id: chunk.messageId,
                                        role: "assistant",
                                        title: chunk.title,
                                        content: "",
                                        htmlContent: "",
                                        isStreaming: true,
                                        userRating: null,
                                    };

                                    this.messages.push(newMessage);

                                    message = newMessage;
                                }

                                if (chunk.title && (!message.title || message.title !== chunk.title)) {
                                    message.title = chunk.title;
                                }

                                if (chunk.references && typeof chunk.references === "object" && Object.keys(chunk.references).length) {

                                    for (const [key, value] of Object.entries(chunk.references)) {
                                        references[key] = normalizeReference(value) ?? {};
                                    }
                                }

                                if (chunk.content) {

                                    // When the responseId changes (e.g., after an internal tool call),
                                    // insert a line break to visually separate response segments.
                                    if (chunk.responseId && lastResponseId && chunk.responseId !== lastResponseId) {
                                        content += '\n\n';
                                    }

                                    if (chunk.responseId) {
                                        lastResponseId = chunk.responseId;
                                    }

                                content += chunk.content;
                            }

                                // Update the existing message
                                message.rawContent = content;
                                updateMessagePresentation(message, references);

                                this.messages[messageIndex] = message;

                                this.$nextTick(() => {
                                    renderChartsInMessage(message);
                                    this.scrollToBottom();
                                });
                            },
                            complete: () => {
                                this.processReferences(references, messageIndex);
                                this.streamingFinished();

                                let msg = this.messages[messageIndex];
                                if (msg) {
                                    msg.isStreaming = false;
                                }

                                if (!msg || !msg.content) {
                                    // No content received at all.
                                    this.hideTypingIndicator();
                                }

                                // Trigger text-to-speech only in conversation mode.
                                if (this.isConversationMode && this.textToSpeechEnabled && msg && msg.content) {
                                    this.synthesizeSpeech(msg.content);
                                }

                                this.stream?.dispose();
                                this.stream = null;
                            },
                            error: (err) => {
                                this.processReferences(references, messageIndex);
                                this.streamingFinished();

                                let msg = this.messages[messageIndex];
                                if (msg) {
                                    msg.isStreaming = false;
                                }

                                this.hideTypingIndicator();

                                if (!this.isNavigatingAway) {
                                    this.addMessage(this.getServiceDownMessage());
                                }

                                this.stream?.dispose();
                                this.stream = null;

                                console.error("Stream error:", err);
                            }
                        });
                },
                getServiceDownMessage() {
                    let newMessage = {
                        role: "assistant",
                        content: "Our service is currently unavailable. Please try again later. We apologize for the inconvenience.",
                        htmlContent: "",
                    };

                    return newMessage;
                },
                processReferences(references, messageIndex) {
                    references = normalizeReferences(references);

                    if (Object.keys(references).length) {

                        let message = this.messages[messageIndex];
                        message.rawContent = message.rawContent ?? message.content ?? '';
                        updateMessagePresentation(message, references);

                        this.messages[messageIndex] = message;

                        this.$nextTick(() => {
                            renderChartsInMessage(message);
                            this.scrollToBottom();
                        });
                    }
                },
                streamingStarted() {
                    var stopIcon = this.buttonElement.getAttribute('data-stop-icon');

                    if (stopIcon) {
                        this.buttonElement.replaceChildren(DOMPurify.sanitize(stopIcon, { RETURN_DOM_FRAGMENT: true }));
                    }

                    if (this.inputElement) {
                        this.inputElement.setAttribute('disabled', 'disabled');
                    }
                },
                streamingFinished() {
                    var startIcon = this.buttonElement.getAttribute('data-start-icon');

                    if (startIcon) {
                        this.buttonElement.replaceChildren(DOMPurify.sanitize(startIcon, { RETURN_DOM_FRAGMENT: true }));
                    }

                    if (this.inputElement) {
                        this.inputElement.removeAttribute('disabled');
                        this.inputElement.focus();
                    }

                    // Directly manipulate the DOM to stop all streaming animations.
                    if (this.chatContainer) {
                        var icons = this.chatContainer.querySelectorAll('.ai-streaming-icon');
                        for (var i = 0; i < icons.length; i++) {
                            icons[i].classList.remove('ai-streaming-icon');
                            icons[i].classList.add('ai-bot-icon');
                        }
                    }

                    // Also update Vue data for consistency.
                    for (var i = 0; i < this.messages.length; i++) {
                        if (this.messages[i].isStreaming) {
                            this.messages[i].isStreaming = false;
                        }
                    }
                },
                handleExternalTtsStop(e) {
                    if (e?.detail?.sourceId === this.ttsInstanceId) {
                        return;
                    }

                    this.stopAudio(false);
                },
                updateTtsPlaybackButtons() {
                    if (!this.chatContainer) {
                        return;
                    }

                    var buttons = this.chatContainer.querySelectorAll('[data-tts-message-index]');

                    buttons.forEach(button => {
                        var buttonIndex = Number(button.getAttribute('data-tts-message-index'));
                        var isPlaying = buttonIndex === this.ttsPlayingMessageIndex;
                        var iconHtml = isPlaying
                            ? '<span class="fa-solid fa-circle-pause"></span>'
                            : '<span class="fa-solid fa-circle-play"></span>';

                        button.classList.toggle('tts-playing', isPlaying);
                        button.setAttribute('title', isPlaying ? 'Pause audio' : 'Read aloud');
                        button.replaceChildren(DOMPurify.sanitize(iconHtml, { RETURN_DOM_FRAGMENT: true }));
                    });
                },
                synthesizeSpeech(text, cacheIndex) {
                    if (!this.textToSpeechEnabled || !text || !this.connection) {
                        return;
                    }

                    this.audioChunks = [];
                    this.isPlayingAudio = true;
                    this._ttsCacheIndex = cacheIndex !== undefined ? cacheIndex : -1;

                    this.connection.invoke("SynthesizeSpeech", this.getProfileId(), this.getSessionId(), text, this.ttsVoiceName)
                        .catch(err => {
                            console.error("TTS synthesis error:", err);
                            this.isPlayingAudio = false;
                            this.ttsPlayingMessageIndex = -1;
                            this._ttsCacheIndex = -1;
                            this.$nextTick(() => this.updateTtsPlaybackButtons());
                        });
                },
                toggleMessageTts(message, index) {
                    if (this.ttsPlayingMessageIndex === index) {
                        this.stopAudio();
                        return;
                    }

                    this.stopAudio(false);
                    window.dispatchEvent(new CustomEvent('crestapps-ai-chat-stop-tts', {
                        detail: { sourceId: this.ttsInstanceId }
                    }));
                    this.ttsPlayingMessageIndex = index;
                    this.$nextTick(() => this.updateTtsPlaybackButtons());

                    if (this.ttsAudioCache[index]) {
                        this.playAudioBlob(this.ttsAudioCache[index]);
                        return;
                    }

                    this.synthesizeSpeech(message.content, index);
                },
                playCollectedAudio() {
                    if (this.audioChunks.length === 0) {
                        if (!this.currentAudioElement && this.audioPlayQueue.length === 0) {
                            this.isPlayingAudio = false;
                            this.ttsPlayingMessageIndex = -1;
                            this.$nextTick(() => this.updateTtsPlaybackButtons());
                        }
                        return;
                    }

                    const totalLength = this.audioChunks.reduce((sum, chunk) => sum + chunk.length, 0);
                    const combined = new Uint8Array(totalLength);
                    let offset = 0;
                    for (const chunk of this.audioChunks) {
                        combined.set(chunk, offset);
                        offset += chunk.length;
                    }
                    this.audioChunks = [];

                    const blob = new Blob([combined], { type: 'audio/mp3' });

                    if (this._ttsCacheIndex >= 0) {
                        this.ttsAudioCache[this._ttsCacheIndex] = blob;
                        this._ttsCacheIndex = -1;
                    }

                    // If audio is already playing, queue this blob for sequential playback.
                    if (this.currentAudioElement) {
                        this.audioPlayQueue.push(blob);
                        return;
                    }

                    this.playAudioBlob(blob);
                },
                playAudioBlob(blob) {
                    const url = URL.createObjectURL(blob);
                    const audio = new Audio(url);

                    this.currentAudioUrl = url;
                    this.currentAudioElement = audio;
                    this.isPlayingAudio = true;

                    audio.addEventListener('ended', () => {
                        this.currentAudioElement = null;
                        this.currentAudioUrl = null;
                        URL.revokeObjectURL(url);
                        this.playNextInQueue();
                    });

                    audio.addEventListener('error', () => {
                        this.currentAudioElement = null;
                        this.currentAudioUrl = null;
                        URL.revokeObjectURL(url);
                        this.playNextInQueue();
                    });

                    audio.play().catch(err => {
                        console.error("Audio playback error:", err);
                        this.currentAudioElement = null;
                        this.currentAudioUrl = null;
                        URL.revokeObjectURL(url);
                        this.audioPlayQueue = [];
                        this.isPlayingAudio = false;
                        this.ttsPlayingMessageIndex = -1;
                        this.$nextTick(() => this.updateTtsPlaybackButtons());
                    });
                },
                playNextInQueue() {
                    if (this.audioPlayQueue.length > 0) {
                        var nextBlob = this.audioPlayQueue.shift();
                        this.playAudioBlob(nextBlob);
                    } else {
                        this.isPlayingAudio = false;
                        this.ttsPlayingMessageIndex = -1;
                        this.$nextTick(() => this.updateTtsPlaybackButtons());
                        this.conversationModeOnAudioEnded();
                    }
                },
                stopAudio() {
                    if (this.currentAudioElement) {
                        this.currentAudioElement.pause();
                        this.currentAudioElement.currentTime = 0;
                        this.currentAudioElement = null;
                    }
                    if (this.currentAudioUrl) {
                        URL.revokeObjectURL(this.currentAudioUrl);
                        this.currentAudioUrl = null;
                    }
                    this.audioChunks = [];
                    this.audioPlayQueue = [];
                    this.isPlayingAudio = false;
                    this.ttsPlayingMessageIndex = -1;
                    this.$nextTick(() => this.updateTtsPlaybackButtons());
                },
                toggleConversationMode() {
                    if (this.isConversationMode) {
                        this.stopConversationMode();
                    } else {
                        this.startConversationMode();
                    }
                },
                startConversationMode() {
                    if (!this.conversationModeEnabled || this.isConversationMode || !this.connection) {
                        return;
                    }

                    this.isConversationMode = true;
                    this.updateConversationButton();
                    this._conversationPartialTranscript = '';
                    this._conversationAssistantMessage = null;
                    this._conversationPartialMessage = null;

                    // Remove any previous conversation ended notification.
                    this.removeNotification('conversation-ended');
                    navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } })
                        .then(stream => {
                            var mimeType = MediaRecorder.isTypeSupported('audio/ogg;codecs=opus')
                                ? 'audio/ogg;codecs=opus'
                                : MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
                                    ? 'audio/webm;codecs=opus'
                                    : 'audio/webm';

                            this.mediaRecorder = new MediaRecorder(stream, {
                                mimeType: mimeType,
                                audioBitsPerSecond: 128000,
                            });

                            this._conversationSubject = new signalR.Subject();
                            this._conversationStream = stream;

                            // Create an AnalyserNode for volume-based interrupt detection.
                            // During TTS playback, detect when the user speaks above
                            // the threshold to stop TTS (interrupt). Audio chunks are
                            // always forwarded — browser echo cancellation handles
                            // speaker echo so the STT stream has no gaps.
                            var AudioCtx = window.AudioContext || window.webkitAudioContext;
                            if (AudioCtx) {
                                this._conversationAudioCtx = new AudioCtx();
                                this._conversationAnalyser = this._conversationAudioCtx.createAnalyser();
                                this._conversationAnalyser.fftSize = 256;
                                var micSource = this._conversationAudioCtx.createMediaStreamSource(stream);
                                micSource.connect(this._conversationAnalyser);
                            }

                            var pendingChunk = Promise.resolve();
                            var analyser = this._conversationAnalyser;
                            var interruptVolumeThreshold = 30;

                            this.mediaRecorder.addEventListener('dataavailable', (e) => {
                                if (e.data && e.data.size > 0) {
                                    // During TTS playback, check mic volume to detect
                                    // user interruption (speaking above threshold).
                                    if (this.isPlayingAudio && analyser) {
                                        var freqData = new Uint8Array(analyser.frequencyBinCount);
                                        analyser.getByteFrequencyData(freqData);
                                        var sum = 0;
                                        for (var k = 0; k < freqData.length; k++) { sum += freqData[k]; }
                                        var avg = sum / freqData.length;

                                        if (avg >= interruptVolumeThreshold) {
                                            // User is speaking — interrupt TTS playback.
                                            this.stopAudio();
                                        }
                                    }

                                    // Always send audio to STT — browser echo cancellation
                                    // handles speaker echo; continuous audio avoids gaps
                                    // that increase recognition latency.
                                    pendingChunk = pendingChunk.then(async () => {
                                        var data = await e.data.arrayBuffer();
                                        var uint8Array = new Uint8Array(data);
                                        var binaryString = uint8Array.reduce(function (str, byte) { return str + String.fromCharCode(byte); }, '');
                                        var base64 = btoa(binaryString);
                                        try {
                                            this._conversationSubject.next(base64);
                                        } catch (err) {
                                            // Subject may have been completed already.
                                        }
                                    });
                                }
                            });

                            this.mediaRecorder.addEventListener('stop', () => {
                                stream.getTracks().forEach(track => track.stop());
                                pendingChunk.then(() => {
                                    try {
                                        this._conversationSubject.complete();
                                    } catch (err) {
                                        // Already completed.
                                    }
                                });
                            });

                            var profileId = this.getProfileId();
                            var sessionId = this.getSessionId() || '';
                            var language = navigator.language || document.documentElement.lang || 'en-US';

                            this.connection.send("StartConversation", profileId, sessionId, this._conversationSubject, mimeType, language);
                            this.mediaRecorder.start(250);
                            this.isRecording = true;
                        })
                        .catch(err => {
                            console.error('Microphone access denied:', err);
                            this.isConversationMode = false;
                            this.updateConversationButton();
                        });
                },
                stopConversationMode() {
                    if (!this.isConversationMode) {
                        return;
                    }

                    this.isConversationMode = false;
                    this.updateConversationButton();

                    // Signal the server to cancel all in-progress STT/TTS streams immediately.
                    if (this.connection) {
                        this.connection.invoke("StopConversation").catch(function () { });
                    }

                    if (this.isRecording && this.mediaRecorder) {
                        this.mediaRecorder.stop();
                        this.isRecording = false;
                    }

                    this.stopAudio();
                    this._conversationPartialTranscript = '';
                    this._conversationPartialMessage = null;

                    // Clean up the AudioContext used for volume monitoring.
                    if (this._conversationAudioCtx) {
                        this._conversationAudioCtx.close().catch(function () { });
                        this._conversationAudioCtx = null;
                        this._conversationAnalyser = null;
                    }

                    // Mark any in-flight assistant message as done to stop the spinner.
                    if (this._conversationAssistantMessage) {
                        var msg = this.messages[this._conversationAssistantMessage.index];
                        if (msg) {
                            msg.isStreaming = false;
                        }
                        this._conversationAssistantMessage = null;
                    }

                    // Safety net: clear all lingering streaming indicators.
                    for (var i = 0; i < this.messages.length; i++) {
                        if (this.messages[i].isStreaming) {
                            this.messages[i].isStreaming = false;
                        }
                    }

                    // Show a "conversation ended" notification system message.
                    this.receiveNotification({
                        type: 'conversation-ended',
                        content: 'Conversation ended.',
                        icon: 'fa-solid fa-circle-check',
                        dismissible: true,
                        autoDismissMs: 5000
                    });
                },
                updateConversationButton() {
                    if (!this.conversationButton) {
                        return;
                    }

                    if (this.isConversationMode) {
                        this.conversationButton.classList.add('active', 'btn-primary');
                        this.conversationButton.classList.remove('btn-dark', 'btn-outline-dark', 'btn-outline-secondary');
                        this.conversationButton.title = this.conversationButton.getAttribute('data-end-title') || 'End Conversation';
                        var endHtml = this.conversationButton.getAttribute('data-end-html');
                        if (endHtml) {
                            this.conversationButton.replaceChildren(DOMPurify.sanitize(endHtml, { RETURN_DOM_FRAGMENT: true }));
                        }
                    } else {
                        this.conversationButton.classList.remove('active', 'btn-primary');
                        this.conversationButton.classList.remove('btn-dark', 'btn-outline-secondary');
                        this.conversationButton.classList.add('btn-outline-dark');
                        this.conversationButton.blur();
                        this.conversationButton.title = this.conversationButton.getAttribute('data-start-title') || 'Start Conversation';
                        var startHtml = this.conversationButton.getAttribute('data-start-html');
                        if (startHtml) {
                            this.conversationButton.replaceChildren(DOMPurify.sanitize(startHtml, { RETURN_DOM_FRAGMENT: true }));
                        }
                    }
                },
                conversationModeSendPrompt() {
                    // Legacy: only used by AudioInput mode's ReceiveTranscript.
                },
                conversationModeOnAudioEnded() {
                    // Legacy: in conversation mode, audio playback continuation
                    // is handled by the persistent stream. This method is only
                    // called from playCollectedAudio for non-conversation TTS.
                },
                generatePrompt(element) {
                    if (!element) {
                        console.error('The element paramter is required.');

                        return;
                    }

                    let templateProfileId = element.getAttribute('data-profile-id');
                    let sessionId = this.getSessionId();
                    let sessionProfileId = this.getProfileId();

                    if (!templateProfileId || !sessionId) {

                        console.error('The given element is missing data-profile-id or the session has not yet started.');
                        return;
                    }

                    // streamMessage() already shows the typing indicator.
                    this.streamMessage(templateProfileId, null, sessionProfileId);
                },
                createSessionUrl(baseUrl, param, value) {

                    const fullUrl = baseUrl.toLowerCase().startsWith('http') ? baseUrl : window.location.origin + baseUrl;
                    const url = new URL(fullUrl);

                    url.searchParams.set(param, value);

                    return url.toString();
                },
                showTypingIndicator() {
                    this.addMessage({
                        role: 'indicator',
                        htmlContent: config.indicatorTemplate
                    });
                },
                hideTypingIndicator() {
                    const originalLength = this.messages.length;
                    this.messages = this.messages.filter(msg => msg.role !== 'indicator');
                    const removedCount = originalLength - this.messages.length;
                    return removedCount;
                },
                receiveNotification(notification) {
                    if (!notification || !notification.type) {
                        return;
                    }
                    this.clearNotificationDismiss(notification.type);
                    var existingIndex = this.notifications.findIndex(n => n.type === notification.type);
                    if (existingIndex >= 0) {
                        this.notifications.splice(existingIndex, 1, notification);
                    } else {
                        this.notifications.push(notification);
                    }
                    this.scheduleNotificationDismiss(notification);
                    this.$nextTick(() => {
                        this.scrollToBottom();
                    });
                },
                updateNotification(notification) {
                    if (!notification || !notification.type) {
                        return;
                    }
                    this.clearNotificationDismiss(notification.type);
                    var existingIndex = this.notifications.findIndex(n => n.type === notification.type);
                    if (existingIndex >= 0) {
                        this.notifications.splice(existingIndex, 1, notification);
                        this.scheduleNotificationDismiss(notification);
                    }
                },
                scheduleNotificationDismiss(notification) {
                    if (!notification || !notification.type || !notification.autoDismissMs || notification.autoDismissMs <= 0) {
                        return;
                    }
                    this.notificationDismissTimers[notification.type] = setTimeout(() => {
                        this.removeNotification(notification.type);
                    }, notification.autoDismissMs);
                },
                clearNotificationDismiss(notificationType) {
                    var timerId = this.notificationDismissTimers[notificationType];
                    if (!timerId) {
                        return;
                    }
                    clearTimeout(timerId);
                    delete this.notificationDismissTimers[notificationType];
                },
                removeNotification(notificationType) {
                    this.clearNotificationDismiss(notificationType);
                    this.notifications = this.notifications.filter(n => n.type !== notificationType);
                },
                dismissNotification(notificationType) {
                    this.removeNotification(notificationType);
                },
                handleNotificationAction(notificationType, actionName) {
                    if (!this.connection) {
                        return;
                    }
                    var sessionId = this.getSessionId();
                    this.connection.invoke("HandleNotificationAction", sessionId, notificationType, actionName).catch(function (err) {
                        console.error("Error handling notification action:", err);
                    });
                },
                scrollToBottom() {
                    if (!this.autoScroll) {
                        return;
                    }
                    setTimeout(() => {
                        this.chatContainer.scrollTop = this.chatContainer.scrollHeight - this.chatContainer.clientHeight;
                    }, 50);
                },
                handleUserInput(event) {
                    this.prompt = event.target.value;
                },
                getProfileId() {
                    return this.inputElement
                        ? this.inputElement.getAttribute('data-profile-id')
                        : null;
                },
                setSessionId(sessionId) {
                    if (!this.inputElement) {
                        return;
                    }

                    this.inputElement.setAttribute('data-session-id', sessionId || '');
                },
                resetSession() {
                    this.stopRecording();
                    this.rejectPendingSessionRequest('Session was reset.');
                    this.setSessionId('');
                    this.isSessionStarted = false;
                    this.sessionRating = null;
                    this.messages = [];
                    this.documents = [];
                    if (!config.autoCreateSession) {
                        this.showPlaceholder();
                    }

                    if (config.autoCreateSession) {
                        this.startNewSession();
                    }

                    if (widgetBehavior && typeof widgetBehavior.onSessionReset === 'function') {
                        widgetBehavior.onSessionReset(this, config);
                    }
                },
                startNewSession() {
                    if (!this.applyOrchestratorAvailability()) {
                        return;
                    }

                    const profileId = this.getProfileId();
                    if (!profileId || !this.connection) {
                        return;
                    }

                    this.requestNewSession(profileId).catch(err => console.error(err));
                },
                async ensureSessionForDocuments(profileId) {
                    var sessionId = this.getSessionId();
                    if (sessionId) {
                        return sessionId;
                    }

                    if (!profileId || !this.connection) {
                        return null;
                    }

                    return await this.requestNewSession(profileId);
                },
                requestNewSession(profileId) {
                    if (this.pendingSessionPromise) {
                        return this.pendingSessionPromise;
                    }

                    if (!profileId || !this.connection) {
                        return Promise.resolve(null);
                    }

                    this.pendingSessionPromise = new Promise((resolve, reject) => {
                        this.pendingSessionResolver = resolve;
                        this.pendingSessionRejector = reject;
                        this.pendingSessionTimeoutId = window.setTimeout(() => {
                            this.rejectPendingSessionRequest('Timed out while creating a chat session.');
                        }, 15000);
                    });

                    this.connection.invoke("StartSession", profileId, null).catch(err => {
                        this.rejectPendingSessionRequest(err);
                    });

                    return this.pendingSessionPromise;
                },
                resolvePendingSessionRequest(sessionId) {
                    if (this.pendingSessionResolver) {
                        this.pendingSessionResolver(sessionId);
                    }

                    this.clearPendingSessionRequest();
                },
                rejectPendingSessionRequest(error) {
                    if (this.pendingSessionRejector) {
                        this.pendingSessionRejector(error);
                    }

                    this.clearPendingSessionRequest();
                },
                clearPendingSessionRequest() {
                    if (this.pendingSessionTimeoutId) {
                        window.clearTimeout(this.pendingSessionTimeoutId);
                    }

                    this.pendingSessionPromise = null;
                    this.pendingSessionResolver = null;
                    this.pendingSessionRejector = null;
                    this.pendingSessionTimeoutId = null;
                },
                initializeApp() {
                    this.inputElement = document.querySelector(config.inputElementSelector);
                    this.buttonElement = document.querySelector(config.sendButtonElementSelector);
                    this.chatContainer = document.querySelector(config.chatContainerElementSelector);
                    this.placeholder = document.querySelector(config.placeholderElementSelector);

                    if (!this.inputElement || !this.buttonElement || !this.chatContainer || !this.placeholder) {
                        console.error('AI chat app could not initialize because one or more required elements were not found.', {
                            inputElementSelector: config.inputElementSelector,
                            sendButtonElementSelector: config.sendButtonElementSelector,
                            chatContainerElementSelector: config.chatContainerElementSelector,
                            placeholderElementSelector: config.placeholderElementSelector
                        });

                        return false;
                    }

                    this.applyOrchestratorAvailability();

                    const sessionId = this.getSessionId();
                    if (!hasWidgetConfig && sessionId) {
                        this.loadSession(sessionId);
                    } else if (this.isOrchestratorAvailable() && config.autoCreateSession && !hasWidgetConfig && !sessionId) {
                        this.startNewSession();
                    }

                    // Initialize document bar if enabled.
                    if (config.sessionDocumentsEnabled && config.documentBarSelector) {
                        this.documentBar = document.querySelector(config.documentBarSelector);
                        if (this.documentBar) {
                            this.renderDocumentBar();

                            // Create hidden file input for document uploads.
                            var fileInput = document.createElement('input');
                            fileInput.type = 'file';
                            fileInput.id = 'ai-chat-doc-input';
                            fileInput.className = 'd-none';
                            fileInput.multiple = true;
                            if (config.allowedExtensions) {
                                fileInput.accept = config.allowedExtensions;
                            }
                            fileInput.addEventListener('change', (e) => this.handleFileInputChange(e));
                            this.documentBar.parentElement.appendChild(fileInput);

                            // Set up drag-and-drop on the input area.
                            var inputArea = this.inputElement ? this.inputElement.closest('.ai-admin-widget-input, .text-bg-light') : null;
                            if (inputArea) {
                                inputArea.addEventListener('dragover', (e) => this.handleDragOver(e));
                                inputArea.addEventListener('dragleave', (e) => this.handleDragLeave(e));
                                inputArea.addEventListener('drop', (e) => this.handleDrop(e));
                            }
                        }
                    }

                    // Pause auto-scroll when the user manually scrolls up during streaming.
                    this.chatContainer.addEventListener('scroll', () => {
                        if (!this.stream) {
                            return;
                        }
                        const threshold = 30;
                        const atBottom = this.chatContainer.scrollHeight - this.chatContainer.clientHeight - this.chatContainer.scrollTop <= threshold;
                        this.autoScroll = atBottom;
                    });

                    this.inputElement.addEventListener('keydown', event => {

                        if (this.stream != null) {
                            return;
                        }

                        if (event.key === "Enter" && !event.shiftKey) {
                            event.preventDefault();
                            this.buttonElement.click();
                        }
                    });

                    this.inputElement.addEventListener('input', (e) => {
                        this.handleUserInput(e);

                        if (e.target.value.trim()) {
                            this.buttonElement.removeAttribute('disabled');
                        } else {
                            this.buttonElement.setAttribute('disabled', true);
                        }
                    });

                    this.buttonElement.addEventListener('click', () => {

                        if (this.stream != null) {
                            this.stream.dispose();
                            this.stream = null;

                            this.streamingFinished();
                            this.hideTypingIndicator();

                            // Clean up: remove empty assistant message or stop streaming animation.
                            if (this.messages.length > 0) {
                                const lastMsg = this.messages[this.messages.length - 1];
                                if (lastMsg.role === 'assistant' && !lastMsg.content) {
                                    this.messages.pop();
                                } else if (lastMsg.isStreaming) {
                                    lastMsg.isStreaming = false;
                                }
                            }

                            return;
                        }

                        this.sendMessage();
                    });

                    const promptGenerators = document.getElementsByClassName('profile-generated-prompt');

                    for (var i = 0; i < promptGenerators.length; i++) {
                        promptGenerators[i].addEventListener('click', (e) => {
                            e.preventDefault();
                            this.generatePrompt(e.target);
                        });
                    }

                    const chatSessions = document.getElementsByClassName('chat-session-history-item');

                    for (var i = 0; i < chatSessions.length; i++) {
                        chatSessions[i].addEventListener('click', (e) => {
                            e.preventDefault();

                            var sessionId = e.target.getAttribute('data-session-id');

                            if (!sessionId) {
                                console.error('an element with the class chat-session-history-item with no data-session-id set.');

                                return;
                            }

                            this.loadSession(sessionId);
                            this.showChatScreen();
                        });
                    }

                    for (let i = 0; i < config.messages.length; i++) {
                        this.addMessage(config.messages[i]);
                    }

                    // Update feedback icons in the DOM after initial messages have rendered.
                    this.$nextTick(() => {
                        this.refreshAllFeedbackIcons();
                    });

                    // Delegate click for code block copy buttons.
                    if (this.chatContainer) {
                        this.chatContainer.addEventListener('click', (e) => {
                            var btn = e.target.closest('.ai-code-copy-btn');
                            if (!btn) {
                                return;
                            }

                            var block = btn.closest('.ai-code-block') || btn.closest('pre');
                            if (!block) {
                                return;
                            }

                            var codeEl = block.querySelector('code');
                            if (codeEl) {
                                navigator.clipboard.writeText(codeEl.textContent);
                                var copiedText = config.codeCopiedText || 'Copied!';
                                btn.innerHTML = '<span class="fa-solid fa-check"></span> ' + copiedText;
                                setTimeout(() => {
                                    btn.innerHTML = '<span class="fa-regular fa-copy"></span>';
                                }, 2000);
                            }
                        });
                    }

                    // Initialize speech-to-text microphone button.
                    if (this.speechToTextEnabled && config.micButtonElementSelector) {
                        this.micButton = document.querySelector(config.micButtonElementSelector);
                        if (this.micButton) {
                            this.micButton.style.display = '';
                            this.micButton.addEventListener('click', () => {
                                this.toggleRecording();
                            });
                        }
                    }

                    // Initialize conversation mode button.
                    if (this.conversationModeEnabled && config.conversationButtonElementSelector) {
                        this.conversationButton = document.querySelector(config.conversationButtonElementSelector);
                        if (this.conversationButton) {
                            this.conversationButton.addEventListener('click', () => {
                                this.toggleConversationMode();
                            });
                        }
                    }

                    return true;
                },
                loadSession(sessionId) {
                    this.connection.invoke("LoadSession", sessionId).catch(err => console.error(err));
                },
                reloadCurrentSession() {

                    var sessionId = this.getSessionId();
                    if (sessionId) {
                        this.loadSession(sessionId);
                    }
                },
                initializeSession(sessionId, force) {
                    if (this.isSessionStarted && !force) {
                        if (sessionId && this.getSessionId() === sessionId) {
                            this.resolvePendingSessionRequest(sessionId);
                        }

                        return
                    }
                    this.fireEvent(new CustomEvent("initializingSessionOpenAIChat", { detail: { sessionId: sessionId } }))
                    this.setSessionId(sessionId);
                    this.isSessionStarted = true;
                    this.resolvePendingSessionRequest(sessionId);

                    if (widgetBehavior && typeof widgetBehavior.onSessionInitialized === 'function') {
                        widgetBehavior.onSessionInitialized(this, sessionId, config);
                    }
                },
                getSessionId() {
                    let sessionId = this.inputElement
                        ? this.inputElement.getAttribute('data-session-id')
                        : null;

                    if (!sessionId && widgetBehavior && typeof widgetBehavior.resolveSessionId === 'function') {
                        sessionId = widgetBehavior.resolveSessionId(this, config);
                    }

                    return sessionId;
                },
                copyResponse(message) {
                    const text = message && typeof message === 'object'
                        ? message.copyContent ?? message.content ?? ''
                        : message ?? '';

                    navigator.clipboard.writeText(text);
                },
                updateFeedbackIcons(container, userRating) {
                    if (!container) {
                        return;
                    }

                    var upBtn = container.querySelector('.rate-up-btn');
                    var downBtn = container.querySelector('.rate-down-btn');

                    // Keep feedback icons as simple span-based Font Awesome markup so
                    // later state updates do not depend on i2svg replacement.
                    if (upBtn) {
                        var upClass = userRating === true ? 'fa-solid fa-thumbs-up' : 'fa-regular fa-thumbs-up';
                        var upIcon = document.createElement('span');
                        upIcon.className = upClass;
                        upIcon.style.fontSize = '0.9rem';
                        upBtn.textContent = '';
                        upBtn.appendChild(upIcon);
                    }

                    if (downBtn) {
                        var downClass = userRating === false ? 'fa-solid fa-thumbs-down' : 'fa-regular fa-thumbs-down';
                        var downIcon = document.createElement('span');
                        downIcon.className = downClass;
                        downIcon.style.fontSize = '0.9rem';
                        downBtn.textContent = '';
                        downBtn.appendChild(downIcon);
                    }
                },
                refreshAllFeedbackIcons() {
                    var containers = this.$el.querySelectorAll('.ai-chat-message-assistant-feedback');

                    for (var i = 0; i < containers.length; i++) {
                        var msgId = containers[i].getAttribute('data-message-id');
                        var msg = this.messages.find(m => m.id === msgId);

                        if (msg) {
                            this.updateFeedbackIcons(containers[i], msg.userRating);
                        }
                    }
                },
                rateMessage(message, isPositive, event) {
                    var sessionId = this.getSessionId();

                    if (!sessionId || !message.id || !this.connection) {
                        return;
                    }

                    // Toggle: clicking the same rating again clears it.
                    var newRating = message.userRating === isPositive ? null : isPositive;
                    message.userRating = newRating;

                    // Find the feedback container by message ID for reliable DOM targeting.
                    var feedbackContainer = this.$el.querySelector(
                        '.ai-chat-message-assistant-feedback[data-message-id="' + message.id + '"]'
                    );

                    this.updateFeedbackIcons(feedbackContainer, newRating);

                    // Trigger spark animation after Font Awesome has re-processed icons.
                    if (newRating !== null && feedbackContainer) {
                        setTimeout(() => {
                            var btnClass = isPositive ? '.rate-up-btn' : '.rate-down-btn';
                            var btn = feedbackContainer.querySelector(btnClass);

                            if (btn) {
                                btn.classList.remove('spark-effect');
                                void btn.offsetWidth;
                                btn.classList.add('spark-effect');
                                btn.addEventListener('animationend', function onEnd() {
                                    btn.removeEventListener('animationend', onEnd);
                                    btn.classList.remove('spark-effect');
                                });
                            }
                        }, 50);
                    }

                    this.connection.invoke("RateMessage", sessionId, message.id, isPositive).catch(function (err) {
                        console.error('Failed to rate message:', err);
                    });
                }
            },
            watch: {
                documents: {
                    handler() {
                        this.renderDocumentBar();
                        if (widgetBehavior && typeof widgetBehavior.onDocumentsChanged === 'function') {
                            widgetBehavior.onDocumentsChanged(this, this.documents, config);
                        }
                    },
                    deep: true
                },
                isUploading() { this.renderDocumentBar(); },
                isDocumentOperationPending() { this.renderDocumentBar(); },
                isPlayingAudio() {
                    // Reserved for future use — volume-based interrupt detection
                    // no longer mutes tracks; browser echo cancellation handles echo.
                },
                isConversationMode(active) {
                    // Hide/show mic button.
                    if (this.micButton) {
                        this.micButton.style.display = active ? 'none' : (this.speechToTextEnabled ? '' : 'none');
                    }

                    // Hide/show send button.
                    if (this.buttonElement) {
                        this.buttonElement.style.display = active ? 'none' : '';
                    }

                    // Disable/enable textarea.
                    if (this.inputElement) {
                        this.inputElement.disabled = active;
                        if (active) {
                            this.inputElement.placeholder = '';
                        }
                    }
                }
            },
            mounted() {
                (async () => {
                    await this.startConnection();
                    const isInitialized = this.initializeApp();
                    if (isInitialized && hasWidgetConfig && widgetBehavior && typeof widgetBehavior.onMounted === 'function') {
                        widgetBehavior.onMounted(this, config);
                    }
                })();

                window.addEventListener('beforeunload', this.handleBeforeUnload);
                window.addEventListener('crestapps-ai-chat-stop-tts', this.handleExternalTtsStop);
            },
            beforeUnmount() {
                window.removeEventListener('beforeunload', this.handleBeforeUnload);
                window.removeEventListener('crestapps-ai-chat-stop-tts', this.handleExternalTtsStop);

                this.stopAudio(false);

                if (this.stream) {
                    this.stream.dispose();
                    this.stream = null;
                }
                if (this.connection) {
                    this.connection.stop();
                }

                if (widgetBehavior && typeof widgetBehavior.onBeforeUnmount === 'function') {
                    widgetBehavior.onBeforeUnmount(this, config);
                }
            },
            template: config.messageTemplate
        };

        const app = Vue.createApp(appDefinition).mount(config.appElementSelector);

        if (widgetBehavior && typeof widgetBehavior.attach === 'function') {
            widgetBehavior.attach(app, config);
        }

        return app;
    };

    return {
        initialize: initialize
    };
}();

// Download chart as image via event delegation (DOMPurify strips inline onclick).
document.addEventListener('click', function (e) {
    const btn = e.target.closest('.download-chart-btn');
    if (!btn) {
        return;
    }
    const chartId = btn.getAttribute('data-chart-id');
    const canvas = chartId ? document.getElementById(chartId) : null;
    if (!canvas) {
        console.error('Chart canvas not found:', chartId);
        return;
    }
    const link = document.createElement('a');
    link.download = 'chart-' + chartId + '.png';
    link.href = canvas.toDataURL('image/png');
    link.click();
});

// Intercept download clicks for data-URI images and convert to blob downloads.
document.addEventListener('click', function (e) {
    const link = e.target.closest('.ai-download-image');
    if (!link) {
        return;
    }

    const container = link.closest('.generated-image-container');
    const img = container?.querySelector('img');
    if (!img) {
        return;
    }

    const src = img.src;
    if (!src || !src.startsWith('data:')) {
        return; // Normal URL – let the default <a> behaviour handle it.
    }

    e.preventDefault();

    fetch(src)
        .then(function (res) { return res.blob(); })
        .then(function (blob) {
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = link.getAttribute('download') || 'generated-image.png';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(function () { URL.revokeObjectURL(url); }, 100);
        })
        .catch(function (err) { console.error('Failed to download image:', err); });
});
