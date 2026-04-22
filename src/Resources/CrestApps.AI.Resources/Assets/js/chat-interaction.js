window.chatInteractionManager = function () {

    // Defaults (can be overridden by instanceConfig)
    var defaultConfig = {
        // UI defaults for generated media
        generatedImageAltText: 'Generated Image',
        generatedImageMaxWidth: 400,
        downloadImageTitle: 'Download image',
        downloadChartTitle: 'Download chart as image',
        downloadChartButtonText: 'Download',
        codeCopiedText: 'Copied!',
        assistantLabel: 'Assistant',

        messageTemplate: `
            <div class="ai-chat-messages">
                <div v-for="(message, index) in messages" :key="index" class="ai-chat-message-item">
                    <div>
                        <div v-if="message.role === 'user'" class="ai-chat-msg-role ai-chat-msg-role-user">You</div>
                        <div v-else-if="message.role !== 'indicator'" :class="getAssistantRoleClasses(message)">
                            <span :class="getAssistantIconClasses(message, index)"><span :class="getAssistantIcon(message)"></span></span>
                            {{ getAssistantLabel(message) }}
                        </div>
                        <div class="ai-chat-message-body lh-base">
                            <h4 v-if="message.title">{{ message.title }}</h4>
                            <div v-html="message.htmlContent"></div>
                            <span class="message-buttons-container" v-if="!isIndicator(message)">
                                <button v-if="textToSpeechEnabled && !isConversationMode && message.role === 'assistant' && !message.isStreaming" class="btn btn-sm btn-link text-secondary p-0 me-1 button-message-toolbox" :class="{ 'tts-playing': ttsPlayingMessageIndex === index }" :data-tts-message-index="index" @click="toggleMessageTts(message, index)" :title="ttsPlayingMessageIndex === index ? 'Pause audio' : 'Read aloud'">
                                    <span :class="ttsPlayingMessageIndex === index ? 'fa-solid fa-circle-pause' : 'fa-solid fa-circle-play'"></span>
                                </button>
                                <button class="btn btn-sm btn-link text-secondary p-0 button-message-toolbox" @click="copyResponse(message.content)" title="Click here to copy response to clipboard.">
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
        `,
        // Localizable strings
        untitledText: 'Untitled',
        clearHistoryTitle: 'Clear History',
        clearHistoryMessage: 'Are you sure you want to clear the chat history? This action cannot be undone. Your documents, parameters, and tools will be preserved.',
        clearHistoryOkText: 'Yes',
        clearHistoryCancelText: 'Cancel'
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

    function normalizeReference(reference) {
        if (!reference || typeof reference !== 'object') {
            return null;
        }

        const normalized = Object.assign({}, reference);
        normalized.index = normalized.index ?? normalized.Index ?? 0;
        normalized.text = normalized.text ?? normalized.Text ?? null;
        normalized.link = normalized.link ?? normalized.Link ?? null;

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

    // Global chart config map shared with ai-chat.js
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
                const canvas = document.getElementById(c.chartId);
                if (!canvas) {
                    continue;
                }

                if (typeof Chart === 'undefined') {
                    console.warn('Chart.js is not loaded. To render interactive charts, include the Chart.js library on the page (e.g., <script src="https://cdn.jsdelivr.net/npm/chart.js@4/dist/chart.umd.min.js"></script>).');
                    continue;
                }

                // When the canvas is inside a hidden container (e.g., display:none),
                // it has zero dimensions. Keep the config for later rendering.
                if (canvas.offsetParent === null) {
                    window.__chartConfigs[c.chartId] = c.config;
                    continue;
                }

                try {
                    // Destroy existing chart instance if re-rendering
                    if (canvas._chartInstance) {
                        canvas._chartInstance.destroy();
                    }

                    const cfg = typeof c.config === 'string' ? JSON.parse(c.config) : c.config;
                    cfg.options ??= {};
                    cfg.options.responsive = true;
                    cfg.options.maintainAspectRatio = true;
                    cfg.options.aspectRatio ??= 4 / 3;

                    canvas._chartInstance = new Chart(canvas, cfg);
                } catch (e) {
                    console.error('Error creating chart:', e);
                }
            }
        });
    }

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

        const app = Vue.createApp({
            data() {
                return {
                    inputElement: null,
                    buttonElement: null,
                    chatContainer: null,
                    placeholder: null,
                    isInteractionStarted: false,
                    isPlaceholderVisible: true,
                    isStreaming: false,
                    isNavigatingAway: false,
                    autoScroll: true,
                    stream: null,
                    messages: [],
                    prompt: '',
                    initialFieldValues: new Map(),
                    settingsDirty: false,
                    saveSettingsTimeout: null,
                    saveIndicatorTimeout: null,
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
                    ttsPlayingMessageIndex: -1,
                    ttsAudioCache: {},
                    ttsInstanceId: 'chat-interaction-' + Math.random().toString(36).slice(2),
                    conversationModeEnabled: config.chatMode === 'Conversation',
                    conversationButton: null,
                    isConversationMode: false,
                    notifications: [],
                    notificationDismissTimers: {},
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
                async startConnection() {
                    this.connection = new signalR.HubConnectionBuilder()
                        .withUrl(config.signalRHubUrl)
                        .withAutomaticReconnect()
                        .build();

                    // Allow long-running operations (e.g., multi-step MCP tool calls)
                    // without the client disconnecting prematurely.
                    this.connection.serverTimeoutInMilliseconds = 600000;
                    this.connection.keepAliveIntervalInMilliseconds = 15000;

                    this.connection.on("LoadInteraction", (data) => {
                        this.initializeInteraction(data.itemId, true);
                        this.messages = [];// Update the title field if it exists
                        const titleInput = document.querySelector('[data-chat-interaction-title], .setting-input[data-setting="title"], input[name="ChatInteraction.Title"], input[name="Title"]');
                        if (titleInput && data.title) {
                            titleInput.value = data.title;
                        }

                        (data.messages ?? []).forEach(msg => {
                            this.addMessage(msg);

                            this.$nextTick(() => {
                                renderChartsInMessage(msg);
                            });
                        });
                    });

                    this.connection.on("SettingsSaved", (itemId, title) => {
                        // Update the history list item if it exists
                        // Use a more specific selector to only target history list items, not other elements like the Clear History button
                        const historyItem = document.querySelector(`.chat-interaction-history-item[data-interaction-id="${itemId}"]`);
                        if (historyItem) {
                            historyItem.textContent = title || config.untitledText;
                        }

                        const titleInput = document.querySelector('[data-chat-interaction-title], .setting-input[data-setting="title"], input[name="ChatInteraction.Title"], input[name="Title"]');
                        if (titleInput && title) {
                            titleInput.value = title;
                        }

                        this.showSaveIndicator('Saved', 'text-success');
                    });

                    this.connection.on("ReceiveError", (error) => {
                        console.error("SignalR Error: ", error);
                        this.showSaveIndicator('Save failed', 'text-danger');

                        if (this.isRecording) {
                            this.stopRecording();
                        }
                    });

                    this.connection.on("ReceiveTranscript", (itemId, text, isFinal) => {
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

                    this.connection.on("ReceiveConversationUserMessage", (itemId, text) => {
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

                    this.connection.on("ReceiveConversationAssistantToken", (itemId, messageId, token, responseId, appearance) => {
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

                    this.connection.on("ReceiveConversationAssistantComplete", (itemId, messageId) => {
                        if (this._conversationAssistantMessage) {
                            var msg = this.messages[this._conversationAssistantMessage.index];
                            if (msg) {
                                msg.isStreaming = false;
                            }
                            this._conversationAssistantMessage = null;
                        }
                    });

                    this.connection.on("ReceiveAudioChunk", (itemId, base64Audio, contentType) => {
                        if (base64Audio) {
                            const binaryString = atob(base64Audio);
                            const bytes = new Uint8Array(binaryString.length);
                            for (let i = 0; i < binaryString.length; i++) {
                                bytes[i] = binaryString.charCodeAt(i);
                            }
                            this.audioChunks.push(bytes);
                        }
                    });

                    this.connection.on("ReceiveAudioComplete", (itemId) => {
                        this.playCollectedAudio();
                    });

                    this.connection.on("HistoryCleared", (itemId) => {
                        // Clear messages and show placeholder
                        this.messages = [];
                        this.showPlaceholder();

                        // Hide the clear history button since there's no history now
                        const clearHistoryBtn = document.getElementById('clearHistoryBtn');
                        if (clearHistoryBtn) {
                            clearHistoryBtn.classList.add('d-none');
                        }
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
                        this.reloadCurrentInteraction();
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
                    this.fireEvent(new CustomEvent("addingChatInteractionMessage", { detail: { message: message } }));
                    this.messages.push(message);

                    this.$nextTick(() => {
                        this.fireEvent(new CustomEvent("addedChatInteractionMessage", { detail: { message: message } }));
                    });
                },
                addMessage(message) {
                    if (message.content) {
                        let processedContent = message.content.trim();

                        message.references = normalizeReferences(message.references);

                        if (message.references && typeof message.references === "object" && Object.keys(message.references).length) {

                            // Only include references that were actually cited in the response.
                            const citedRefs = Object.entries(message.references).filter(([key]) => processedContent.includes(key));

                            if (citedRefs.length) {
                                // Sort by original index so display indices follow a natural order.
                                citedRefs.sort(([, a], [, b]) => a.index - b.index);

                                // Phase 1: Replace all markers with unique placeholders.
                                let displayIndex = 1;
                                for (const [key, value] of citedRefs) {
                                    const placeholder = `__CITE_${value.index}__`;
                                    processedContent = processedContent.replaceAll(key, placeholder);
                                    value._displayIndex = displayIndex++;
                                    value._placeholder = placeholder;
                                }

                                // Phase 2: Replace placeholders with sequential display indices.
                                for (const [, value] of citedRefs) {
                                    processedContent = processedContent.replaceAll(value._placeholder, `<sup><strong>${value._displayIndex}</strong></sup>`);
                                }

                                processedContent = processedContent.replaceAll('</strong></sup><sup>', '</strong></sup><sup>,</sup><sup>');

                                processedContent += '<br><br>';

                                for (const [key, value] of citedRefs) {
                                    const label = value.text || `[doc:${value.index}]`;
                                    processedContent += value.link
                                        ? `**${value._displayIndex}**. [${label}](${value.link})<br>`
                                        : `**${value._displayIndex}**. ${label}<br>`;
                                }
                            }
                        }

                        message.content = processedContent;
                        message.htmlContent = parseMarkdownContent(processedContent, message);
                    }

                    this.addMessageInternal(message);
                    this.hidePlaceholder();

                    this.$nextTick(() => {
                        // Render any pending charts once the DOM is updated
                        renderChartsInMessage(message);
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
                fireEvent(event) {
                    document.dispatchEvent(event);
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
                    return appearance && appearance.label ? appearance.label : defaultConfig.assistantLabel;
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
                isIndicator(message) {
                    return message.role === 'indicator';
                },
                async sendMessage() {
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

                    // Flush any pending settings save before sending a message
                    // to prevent concurrent hub calls that can cause database deadlocks.
                    await this.flushPendingSave();

                    this.addMessage({
                        role: 'user',
                        content: trimmedPrompt
                    });

                    // Show the clear history button since we now have prompts
                    const clearHistoryBtn = document.getElementById('clearHistoryBtn');
                    if (clearHistoryBtn) {
                        clearHistoryBtn.classList.remove('d-none');
                    }

                    this.streamMessage(trimmedPrompt);
                    this.inputElement.value = '';
                    this.prompt = '';
                },
                streamMessage(trimmedPrompt) {
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

                    var messageIndex = this.messages.length;
                    var currentItemId = this.getItemId();

                    this.stream = this.connection.stream("SendMessage", currentItemId, trimmedPrompt)
                        .subscribe({
                            next: (chunk) => {
                                let message = this.messages[messageIndex];

                                if (!message) {
                                    if (chunk.sessionId && !currentItemId) {
                                        this.setItemId(chunk.sessionId);
                                    }

                                    this.hideTypingIndicator();
                                    messageIndex = this.messages.length;
                                    let newMessage = {
                                        role: "assistant",
                                        content: "",
                                        htmlContent: "",
                                        isStreaming: true,
                                    };

                                    this.messages.push(newMessage);
                                    message = newMessage;
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

                                    let processedContent = chunk.content;

                                    for (const [key, value] of Object.entries(references)) {
                                        processedContent = processedContent.replaceAll(key, `<sup><strong>${value.index}</strong></sup>`);
                                    }

                                    content += processedContent.replaceAll('</strong></sup><sup>', '</strong></sup><sup>,</sup><sup>');
                                }

                                message.content = content;

                                message.htmlContent = parseMarkdownContent(content, message);

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
                    return {
                        role: "assistant",
                        content: "Our service is currently unavailable. Please try again later. We apologize for the inconvenience.",
                        htmlContent: "",
                    };
                },
                processReferences(references, messageIndex) {
                    references = normalizeReferences(references);

                    if (Object.keys(references).length) {
                        let message = this.messages[messageIndex];
                        const content = message.content || '';

                        // Only include references that were actually cited in the response.
                        // Check both raw [doc:N] markers and already-rendered <sup> tags from streaming.
                        const citedRefs = Object.entries(references).filter(([key, value]) =>
                            content.includes(key) || content.includes(`<sup><strong>${value.index}</strong></sup>`)
                        );

                        if (!citedRefs.length) {
                            return;
                        }

                        // Sort by original index so display indices follow a natural order.
                        citedRefs.sort(([, a], [, b]) => a.index - b.index);

                        // Phase 1: Replace all markers with unique placeholders to avoid collisions during remapping.
                        let processed = content.trim();
                        let displayIndex = 1;
                        for (const [key, value] of citedRefs) {
                            const placeholder = `__CITE_${value.index}__`;
                            processed = processed.replaceAll(key, placeholder);
                            processed = processed.replaceAll(`<sup><strong>${value.index}</strong></sup>`, placeholder);
                            value._displayIndex = displayIndex++;
                            value._placeholder = placeholder;
                        }

                        // Phase 2: Replace placeholders with sequential display indices.
                        for (const [, value] of citedRefs) {
                            processed = processed.replaceAll(value._placeholder, `<sup><strong>${value._displayIndex}</strong></sup>`);
                        }

                        processed = processed.replaceAll('</strong></sup><sup>', '</strong></sup><sup>,</sup><sup>');

                        processed += '<br><br>';

                        for (const [key, value] of citedRefs) {
                            const label = value.text || `[doc:${value.index}]`;
                            processed += value.link
                                ? `**${value._displayIndex}**. [${label}](${value.link})<br>`
                                : `**${value._displayIndex}**. ${label}<br>`;
                        }

                        message.content = processed;
                        message.htmlContent = parseMarkdownContent(processed, message);

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
                },
                streamingFinished() {
                    var startIcon = this.buttonElement.getAttribute('data-start-icon');

                    if (startIcon) {
                        this.buttonElement.replaceChildren(DOMPurify.sanitize(startIcon, { RETURN_DOM_FRAGMENT: true }));
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

                    // Save any settings that were deferred during streaming.
                    if (this.settingsDirty) {
                        this.debouncedSaveSettings();
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

                    this.connection.invoke("SynthesizeSpeech", this.getItemId(), text, this.ttsVoiceName)
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
                        if (!this.isPlayingAudio && this.audioPlayQueue.length === 0) {
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

                    // If audio is currently playing, queue this blob for later.
                    if (this.isPlayingAudio && this.currentAudioElement) {
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
                        const nextBlob = this.audioPlayQueue.shift();
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

                            var itemId = this.getItemId();

                            var language = navigator.language || document.documentElement.lang || 'en-US';
                            this.connection.send("StartConversation", itemId, this._conversationSubject, mimeType, language);
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
                    // Legacy: in conversation mode, continuation is handled
                    // by the persistent stream.
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
                getItemId() {
                    return this.inputElement.getAttribute('data-interaction-id')
                        || this.inputElement.getAttribute('data-item-id');
                },
                getSaveIndicatorElement() {
                    if (config.saveIndicatorElementSelector) {
                        return document.querySelector(config.saveIndicatorElementSelector);
                    }

                    return document.querySelector('[data-chat-interaction-save-indicator]');
                },
                showSaveIndicator(text, className) {
                    const indicator = this.getSaveIndicatorElement();
                    if (!indicator) {
                        return;
                    }

                    indicator.textContent = text || '';
                    indicator.className = 'settings-save-indicator ' + (className || 'text-muted');

                    if (this.saveIndicatorTimeout) {
                        clearTimeout(this.saveIndicatorTimeout);
                        this.saveIndicatorTimeout = null;
                    }

                    if (!text) {
                        return;
                    }

                    this.saveIndicatorTimeout = setTimeout(() => {
                        indicator.textContent = '';
                        this.saveIndicatorTimeout = null;
                    }, 3000);
                },
                clearPendingSettingsSave() {
                    if (this.saveSettingsTimeout) {
                        clearTimeout(this.saveSettingsTimeout);
                        this.saveSettingsTimeout = null;
                    }
                },
                getFieldLabel(input) {
                    const label = input
                        .closest('.mb-3, .col-6, .col, .form-group, .form-floating')
                        ?.querySelector('label');

                    if (!label) {
                        return input.dataset.setting || 'This field';
                    }

                    return (label.textContent || '')
                        .replace(/\s+/g, ' ')
                        .trim()
                        || input.dataset.setting
                        || 'This field';
                },
                getValidationFeedbackElement(input) {
                    let feedback = input.nextElementSibling;
                    if (feedback && feedback.classList.contains('invalid-feedback')) {
                        return feedback;
                    }

                    feedback = document.createElement('div');
                    feedback.className = 'invalid-feedback';
                    input.insertAdjacentElement('afterend', feedback);

                    return feedback;
                },
                clearSettingValidationError(input) {
                    input.classList.remove('is-invalid');
                    input.removeAttribute('aria-invalid');

                    const feedback = input.nextElementSibling;
                    if (feedback && feedback.classList.contains('invalid-feedback')) {
                        feedback.textContent = '';
                    }
                },
                setSettingValidationError(input, message) {
                    const feedback = this.getValidationFeedbackElement(input);
                    input.classList.add('is-invalid');
                    input.setAttribute('aria-invalid', 'true');
                    feedback.textContent = message;
                },
                getSettingValidationMessage(input) {
                    if (!input || input.disabled || input.type !== 'number') {
                        return null;
                    }

                    const value = (input.value || '').trim();
                    if (!value) {
                        return null;
                    }

                    const number = Number(value);
                    const fieldLabel = this.getFieldLabel(input);

                    if (Number.isNaN(number)) {
                        return `${fieldLabel} must be a valid number.`;
                    }

                    const min = input.getAttribute('min');
                    if (min !== null && number < Number(min)) {
                        return `${fieldLabel} must be ${min} or greater.`;
                    }

                    const max = input.getAttribute('max');
                    if (max !== null && number > Number(max)) {
                        return `${fieldLabel} must be ${max} or less.`;
                    }

                    return null;
                },
                validateSettingInput(input) {
                    const message = this.getSettingValidationMessage(input);
                    if (!message) {
                        this.clearSettingValidationError(input);
                        return true;
                    }

                    this.setSettingValidationError(input, message);
                    return false;
                },
                validateSettings() {
                    let isValid = true;

                    this.getSettingInputs().forEach(input => {
                        isValid = this.validateSettingInput(input) && isValid;
                    });

                    return isValid;
                },
                queueSettingsSave() {
                    if (!this.validateSettings()) {
                        this.clearPendingSettingsSave();
                        this.settingsDirty = false;
                        this.showSaveIndicator('Fix errors', 'text-danger');
                        return;
                    }

                    this.settingsDirty = true;
                    this.showSaveIndicator('Saving...', 'text-warning');
                    this.debouncedSaveSettings();
                },
                getSettingInputs() {
                    const explicitInputs = document.querySelectorAll('.setting-input[data-setting]');
                    if (explicitInputs.length > 0) {
                        return explicitInputs;
                    }

                    return document.querySelectorAll(
                        'input[name^="ChatInteraction."]:not([name*=".Tools["]):not([name*=".Connections["]), ' +
                        'select[name^="ChatInteraction."]:not([name*=".Tools["]):not([name*=".Connections["]), ' +
                        'textarea[name^="ChatInteraction."]:not([name*=".Tools["]):not([name*=".Connections["])'
                    );
                },
                getSelectedGroupValues(groupName, fallbackSelector) {
                    const explicitSelections = document.querySelectorAll(`.capability-checkbox[data-save-group="${groupName}"]:checked, .capability-checkbox[data-group="${groupName}"]:checked`);
                    if (explicitSelections.length > 0) {
                        const values = [];

                        explicitSelections.forEach(checkbox => {
                            const value = checkbox.getAttribute('data-item-id') || checkbox.value;
                            if (value) {
                                values.push(value);
                            }
                        });

                        return values;
                    }

                    if (!fallbackSelector) {
                        return [];
                    }

                    const values = [];
                    const checkboxes = document.querySelectorAll(fallbackSelector);

                    checkboxes.forEach(checkbox => {
                        const baseName = checkbox.name.replace('.IsSelected', '.ItemId');
                        const hiddenInput = document.querySelector(`input[type="hidden"][name="${baseName}"]`);

                        if (hiddenInput && hiddenInput.value) {
                            values.push(hiddenInput.value);
                        }
                    });

                    return values;
                },
                getPromptTemplateSelections() {
                    const promptTemplates = [];

                    document.querySelectorAll('.prompt-template-card').forEach(card => {
                        const templateIdInput = card.querySelector('.prompt-template-id-input');
                        const promptParametersInput = card.querySelector('.prompt-template-parameters-input');
                        const templateId = templateIdInput ? templateIdInput.value : card.getAttribute('data-template-id');

                        if (!templateId) {
                            return;
                        }

                        promptTemplates.push({
                            templateId: templateId,
                            promptParameters: promptParametersInput ? (promptParametersInput.value || '').trim() : ''
                        });
                    });

                    return promptTemplates;
                },
                receiveNotification(notification) {
                    if (!notification || !notification.type) {
                        return;
                    }
                    this.clearNotificationDismiss(notification.type);
                    // Replace existing notification with same type, or add new one.
                    var idx = this.notifications.findIndex(n => n.type === notification.type);
                    if (idx >= 0) {
                        this.notifications.splice(idx, 1, notification);
                    } else {
                        this.notifications.push(notification);
                    }
                    this.scheduleNotificationDismiss(notification);
                    this.scrollToBottom();
                },
                updateNotification(notification) {
                    if (!notification || !notification.type) {
                        return;
                    }
                    this.clearNotificationDismiss(notification.type);
                    var idx = this.notifications.findIndex(n => n.type === notification.type);
                    if (idx >= 0) {
                        this.notifications.splice(idx, 1, notification);
                        this.scheduleNotificationDismiss(notification);
                        this.scrollToBottom();
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
                    var itemId = this.getItemId();
                    this.connection.invoke('HandleNotificationAction', itemId, notificationType, actionName)
                        .catch(err => console.error('Failed to handle notification action:', err));
                },
                setItemId(itemId) {
                    this.inputElement.setAttribute('data-interaction-id', itemId || '');
                    this.inputElement.setAttribute('data-item-id', itemId || '');
                },
                resetInteraction() {
                    this.setItemId('');
                    this.isInteractionStarted = false;
                    this.messages = [];
                    this.showPlaceholder();
                },
                initializeApp() {
                    this.inputElement = document.querySelector(config.inputElementSelector);
                    this.buttonElement = document.querySelector(config.sendButtonElementSelector);
                    this.chatContainer = document.querySelector(config.chatContainerElementSelector);
                    this.placeholder = document.querySelector(config.placeholderElementSelector);

                    const itemId = this.getItemId();
                    if (itemId) {
                        this.loadInteraction(itemId);
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

                    this.inputElement.addEventListener('keyup', event => {
                        if (this.stream != null) {
                            return;
                        }

                        if (event.key === "Enter" && !event.shiftKey) {
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

                    this.inputElement.addEventListener('paste', (e) => {
                        // Use setTimeout to allow the paste to complete before checking the value
                        setTimeout(() => {
                            this.prompt = this.inputElement.value;
                            if (this.inputElement.value.trim()) {
                                this.buttonElement.removeAttribute('disabled');
                            } else {
                                this.buttonElement.setAttribute('disabled', true);
                            }
                        }, 0);
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

                    const chatInteractionItems = document.getElementsByClassName('chat-interaction-history-item');

                    for (var i = 0; i < chatInteractionItems.length; i++) {
                        chatInteractionItems[i].addEventListener('click', (e) => {
                            e.preventDefault();

                            var itemId = e.target.getAttribute('data-interaction-id');

                            if (!itemId) {
                                console.error('An element with the class chat-interaction-history-item with no data-interaction-id set.');
                                return;
                            }

                            this.loadInteraction(itemId);
                        });
                    }

                    for (let i = 0; i < config.messages.length; i++) {
                        this.addMessage(config.messages[i]);
                    }

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

                    document.addEventListener('input', event => {
                        if (event.target.matches('.setting-input[data-setting]')) {
                            this.validateSettingInput(event.target);
                            this.queueSettingsSave();
                        }
                    });

                    document.addEventListener('change', event => {
                        if (event.target.matches('.setting-input[data-setting], .capability-checkbox[data-save-group], .capability-checkbox[data-group], .group-toggle, .ci-agent-global-toggle')) {
                            if (event.target.matches('.setting-input[data-setting]')) {
                                this.validateSettingInput(event.target);
                            }

                            this.queueSettingsSave();
                            return;
                        }

                        if (event.target.closest('.prompt-template-parameters-input, .prompt-template-id-input')) {
                            this.queueSettingsSave();
                        }
                    });

                    document.addEventListener('click', event => {
                        if (!event.target.closest('.prompt-template-add-btn, .remove-prompt-template-btn')) {
                            return;
                        }

                        setTimeout(() => {
                            this.queueSettingsSave();
                        }, 0);
                    });

                    // Add event listener for clear history button
                    const clearHistoryBtn = document.getElementById('clearHistoryBtn');
                    if (clearHistoryBtn) {
                        clearHistoryBtn.addEventListener('click', () => {
                            const itemId = clearHistoryBtn.getAttribute('data-interaction-id');
                            if (itemId) {
                                this.clearHistory(itemId);
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
                },
                loadInteraction(itemId) {
                    this.connection.invoke("LoadInteraction", itemId).catch(err => console.error(err));
                },
                reloadCurrentInteraction() {
                    const itemId = this.getItemId();
                    if (itemId) {
                        this.loadInteraction(itemId);
                    }
                },
                clearHistory(itemId) {
                    const self = this;
                    const clearHistoryConfirmed = () => {
                        // Cancel any active stream before clearing history.
                        if (self.stream) {
                            self.stream.dispose();
                            self.stream = null;
                            self.hideTypingIndicator();
                            self.streamingFinished();
                        }

                        self.connection.invoke("ClearHistory", itemId)
                            .catch(err => console.error('Error clearing history:', err));
                    };

                    if (typeof confirmDialog === 'function') {
                        confirmDialog({
                            title: config.clearHistoryTitle,
                            message: config.clearHistoryMessage,
                            okText: config.clearHistoryOkText,
                            cancelText: config.clearHistoryCancelText,
                            callback: function (confirmed) {
                                if (confirmed) {
                                    clearHistoryConfirmed();
                                }
                            }
                        });

                        return;
                    }

                    if (window.confirm(config.clearHistoryMessage || 'Clear all messages?')) {
                        clearHistoryConfirmed();
                    }
                },
                debouncedSaveSettings() {
                    // Clear any existing timeout to reset the debounce timer
                    this.clearPendingSettingsSave();

                    // Don't save while streaming — it will be saved when streaming completes.
                    if (this.stream) {
                        return;
                    }

                    // Set a new timeout to save after 850ms of no changes
                    this.saveSettingsTimeout = setTimeout(() => {
                        if (this.settingsDirty) {
                            this.saveSettings();
                            this.settingsDirty = false;
                        }
                        this.saveSettingsTimeout = null;
                    }, 850);
                },
                getSelectedToolNames() {
                    return this.getSelectedGroupValues(
                        'toolNames',
                        'input[type="checkbox"][name$="].IsSelected"][name^="ChatInteraction.Tools["]:checked'
                    );
                },
                getSelectedMcpConnectionIds() {
                    return this.getSelectedGroupValues(
                        'mcpConnectionIds',
                        'input[type="checkbox"][name$="].IsSelected"][name^="ChatInteraction.Connections["]:checked'
                    );
                },
                getSelectedA2AConnectionIds() {
                    return this.getSelectedGroupValues('a2aConnectionIds');
                },
                getSelectedAgentNames() {
                    return this.getSelectedGroupValues(
                        'agentNames',
                        'input[type="checkbox"][name$="].IsSelected"][name^="ChatInteraction.Agents["]:checked'
                    );
                },
                saveSettings() {
                    const itemId = this.getItemId();
                    if (!itemId) {
                        return Promise.resolve();
                    }

                    if (!this.validateSettings()) {
                        this.showSaveIndicator('Fix errors', 'text-danger');
                        return Promise.resolve();
                    }

                    const settings = {};

                    // Collect all form inputs with the "ChatInteraction." prefix generically.
                    // This avoids coupling the JS to specific field names — new fields added by
                    // any module are automatically included.
                    const inputs = this.getSettingInputs();

                    inputs.forEach(input => {
                        const key = input.dataset.setting
                            || ((input.name || '').replace('ChatInteraction.', '').replace(/^[A-Z]/, match => match.toLowerCase()));

                        if (!key) {
                            return;
                        }

                        if (input.type === 'checkbox') {
                            settings[key] = input.checked;
                        } else if (input.type === 'number') {
                            settings[key] = input.value ? parseFloat(input.value) : null;
                        } else {
                            settings[key] = input.value || null;
                        }
                    });

                    // Add tool, MCP connection, and agent collections (special handling).
                    settings.toolNames = this.getSelectedToolNames();
                    settings.mcpConnectionIds = this.getSelectedMcpConnectionIds();
                    settings.a2aConnectionIds = this.getSelectedA2AConnectionIds();
                    settings.agentNames = this.getSelectedAgentNames();

                    const promptTemplates = this.getPromptTemplateSelections();
                    if (promptTemplates.length > 0) {
                        settings.promptTemplates = promptTemplates;
                        settings.promptTemplateIds = promptTemplates.map(template => template.templateId);
                    }

                    return this.connection.invoke("SaveSettings", itemId, settings)
                        .catch(err => {
                            console.error('Error saving settings:', err);
                            this.showSaveIndicator('Save failed', 'text-danger');
                        });
                },
                flushPendingSave() {
                    if (this.saveSettingsTimeout) {
                        clearTimeout(this.saveSettingsTimeout);
                        this.saveSettingsTimeout = null;
                    }

                    if (this.settingsDirty) {
                        this.settingsDirty = false;
                        return this.saveSettings();
                    }

                    return Promise.resolve();
                },
                initializeInteraction(itemId, force) {
                    if (this.isInteractionStarted && !force) {
                        return;
                    }
                    this.fireEvent(new CustomEvent("initializingChatInteraction", { detail: { itemId: itemId } }));
                    this.setItemId(itemId);
                    this.isInteractionStarted = true;
                },
                copyResponse(message) {
                    navigator.clipboard.writeText(message);
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
                            var itemId = this.getItemId();
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
                            this.connection.send("SendAudioStream", itemId, subject, mimeType, language);
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
                }
            },
            watch: {
                isPlayingAudio() {
                    // Reserved for future use — volume-based interrupt detection
                    // no longer mutes tracks; browser echo cancellation handles echo.
                },
                isConversationMode(active) {
                    if (this.micButton) {
                        this.micButton.style.display = active ? 'none' : (this.speechToTextEnabled ? '' : 'none');
                    }

                    if (this.buttonElement) {
                        this.buttonElement.style.display = active ? 'none' : '';
                    }

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
                    this.initializeApp();
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
            },
            template: config.messageTemplate
        }).mount(config.appElementSelector);

        return app;
    };

    return {
        initialize: initialize
    };
}();

window.chatInteractionDocumentManager = function () {
    const managerStateKey = '__chatInteractionDocumentManagerState';

    function normalizeDocumentInfo(document) {
        if (!document || typeof document !== 'object') {
            return null;
        }

        return {
            documentId: document.documentId || document.DocumentId || '',
            fileName: document.fileName || document.FileName || '',
            fileSize: document.fileSize || document.FileSize || 0
        };
    }

    function formatFileSize(bytes) {
        if (!bytes || bytes < 1024) {
            return (bytes || 0) + ' B';
        }

        if (bytes < 1024 * 1024) {
            return (bytes / 1024).toFixed(1) + ' KB';
        }

        return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    }

    function getDocumentKey(fileName, fileSize) {
        return ((fileName || '').trim().toLowerCase()) + '::' + (fileSize || 0);
    }

    function isDuplicateFile(file, knownKeys) {
        return knownKeys.has(getDocumentKey(file.name, file.size));
    }

    function getDocumentsFromDom(container) {
        if (!container) {
            return [];
        }

        return Array.from(container.querySelectorAll('[data-chat-document-id]'))
            .map(element => normalizeDocumentInfo({
                documentId: element.dataset.chatDocumentId,
                fileName: element.dataset.chatDocumentName,
                fileSize: Number(element.dataset.chatDocumentSize || 0)
            }))
            .filter(document => document && document.documentId);
    }

    function initialize(config) {
        if (!config || !config.itemId) {
            return;
        }

        const fileInput = document.getElementById('chat-doc-upload');
        const documentsList = document.getElementById('chat-documents-list');
        const status = document.getElementById('chat-doc-upload-status');
        const progressContainer = document.getElementById('chat-doc-upload-progress');
        const progressBar = document.getElementById('chat-doc-upload-progress-bar');
        const uploadQueue = document.getElementById('chat-doc-upload-queue');

        if (!fileInput || !documentsList || !status || !progressContainer || !progressBar || !uploadQueue) {
            return;
        }

        const previousState = fileInput[managerStateKey];
        if (previousState && typeof previousState.dispose === 'function') {
            previousState.dispose();
        }

        let interactionDocuments = (Array.isArray(config.existingDocuments) ? config.existingDocuments : [])
            .map(normalizeDocumentInfo)
            .filter(document => document && document.documentId);

        if (interactionDocuments.length === 0) {
            interactionDocuments = getDocumentsFromDom(documentsList);
        }

        let uploadItems = [];
        let isUploadingDocuments = false;

        function createTextElement(tagName, className, text) {
            const element = document.createElement(tagName);
            if (className) {
                element.className = className;
            }

            element.textContent = text;
            return element;
        }

        function renderDocuments() {
            documentsList.innerHTML = '';

            if (interactionDocuments.length === 0) {
                documentsList.appendChild(createTextElement('div', 'text-muted small', 'No documents uploaded yet.'));
                return;
            }

            interactionDocuments.forEach(document => {
                const row = document.createElement('div');
                row.className = 'd-flex justify-content-between align-items-start gap-2 border rounded px-2 py-2 bg-white';

                const details = document.createElement('div');
                details.className = 'me-2 min-w-0';

                const name = createTextElement('div', 'fw-semibold small', document.fileName || 'Document');
                const icon = document.createElement('i');
                icon.className = 'bi bi-file-earmark-text me-1';
                name.prepend(icon);

                const size = createTextElement('div', 'text-muted small', formatFileSize(document.fileSize));

                details.appendChild(name);
                details.appendChild(size);

                const removeButton = createTextElement('button', 'btn btn-sm btn-outline-danger remove-chat-document-btn', ' Remove');
                removeButton.type = 'button';
                removeButton.dataset.documentId = document.documentId;
                const removeIcon = document.createElement('i');
                removeIcon.className = 'bi bi-trash';
                removeButton.prepend(removeIcon);
                removeButton.addEventListener('click', () => removeDocument(document.documentId));

                row.appendChild(details);
                row.appendChild(removeButton);
                documentsList.appendChild(row);
            });
        }

        function showUploadStatus(message, cssClass) {
            if (!message) {
                status.textContent = '';
                status.className = 'small mt-2 d-none';
                return;
            }

            status.textContent = message;
            status.className = 'small mt-2 ' + (cssClass || 'text-muted');
        }

        function showUploadProgress(progress, message, cssClass) {
            if (progress === null || progress === undefined) {
                progressContainer.className = 'progress mt-2 d-none';
                progressContainer.setAttribute('aria-valuenow', '0');
                progressBar.className = 'progress-bar progress-bar-striped progress-bar-animated';
                progressBar.style.width = '0%';
                progressBar.textContent = '0%';
                return;
            }

            const roundedProgress = Math.max(0, Math.min(100, Math.round(progress)));
            progressContainer.className = 'progress mt-2';
            progressContainer.setAttribute('aria-valuenow', roundedProgress.toString());
            progressBar.className = 'progress-bar ' + (cssClass || 'progress-bar-striped progress-bar-animated');
            progressBar.style.width = roundedProgress + '%';
            progressBar.textContent = message || (roundedProgress + '%');
        }

        function getUploadStatePresentation(state) {
            switch (state) {
                case 'uploaded':
                    return { label: 'Uploaded', badgeClass: 'text-bg-success', progressClass: 'bg-success' };
                case 'failed':
                    return { label: 'Failed', badgeClass: 'text-bg-danger', progressClass: 'bg-danger' };
                case 'uploading':
                    return { label: 'Uploading', badgeClass: 'text-bg-warning', progressClass: 'bg-warning progress-bar-striped progress-bar-animated' };
                default:
                    return { label: 'Queued', badgeClass: 'text-bg-secondary', progressClass: 'bg-secondary progress-bar-striped progress-bar-animated' };
            }
        }

        function formatUploadMessage(item) {
            if (item.status === 'failed' && item.error) {
                return item.error;
            }

            if (item.status === 'uploaded') {
                return 'Upload completed successfully.';
            }

            if (item.status === 'uploading') {
                return item.progress + '% uploaded';
            }

            return 'Waiting to upload...';
        }

        function renderUploadQueue() {
            uploadQueue.innerHTML = '';

            uploadItems.forEach(item => {
                const presentation = getUploadStatePresentation(item.status);
                const card = document.createElement('div');
                card.className = 'border rounded px-2 py-2 bg-white';

                const header = document.createElement('div');
                header.className = 'd-flex justify-content-between align-items-start gap-2';

                const fileDetails = document.createElement('div');
                fileDetails.className = 'flex-grow-1 min-w-0';
                fileDetails.appendChild(createTextElement('div', 'fw-semibold small', item.fileName));
                fileDetails.appendChild(createTextElement('div', 'text-muted small', formatFileSize(item.fileSize)));

                const badge = createTextElement('span', 'badge ' + presentation.badgeClass, presentation.label);

                header.appendChild(fileDetails);
                header.appendChild(badge);

                const progress = document.createElement('div');
                progress.className = 'progress mt-2';
                progress.setAttribute('role', 'progressbar');
                progress.setAttribute('aria-valuemin', '0');
                progress.setAttribute('aria-valuemax', '100');
                progress.setAttribute('aria-valuenow', item.progress.toString());

                const progressState = document.createElement('div');
                progressState.className = 'progress-bar ' + presentation.progressClass;
                progressState.style.width = item.progress + '%';
                progressState.textContent = item.progress + '%';
                progress.appendChild(progressState);

                const message = createTextElement('div', item.status === 'failed' ? 'small text-danger mt-2' : 'small text-muted mt-2', formatUploadMessage(item));

                card.appendChild(header);
                card.appendChild(progress);
                card.appendChild(message);
                uploadQueue.appendChild(card);
            });
        }

        function createUploadItem(file, index) {
            return {
                id: file.name + '::' + file.size + '::' + file.lastModified + '::' + index + '::' + Date.now(),
                fileName: file.name,
                fileSize: file.size,
                status: 'queued',
                progress: 0,
                error: null
            };
        }

        function updateUploadItem(itemId, updates) {
            uploadItems = uploadItems.map(item => item.id === itemId ? Object.assign({}, item, updates) : item);
            renderUploadQueue();
        }

        function removeUploadItems(predicate) {
            uploadItems = uploadItems.filter(item => !predicate(item));
            renderUploadQueue();
        }

        function uploadSingleDocument(file, itemIdToUpdate, fileIndex, totalFiles) {
            return new Promise((resolve, reject) => {
                const formData = new FormData();
                formData.append('chatInteractionId', config.itemId);
                formData.append('files', file);

                const xhr = new XMLHttpRequest();
                xhr.open('POST', config.uploadDocumentUrl, true);

                xhr.upload.addEventListener('progress', event => {
                    if (!event.lengthComputable) {
                        return;
                    }

                    const fileProgress = Math.round((event.loaded / event.total) * 100);
                    updateUploadItem(itemIdToUpdate, {
                        status: 'uploading',
                        progress: fileProgress
                    });

                    const overallProgress = ((fileIndex + (event.loaded / event.total)) / totalFiles) * 100;
                    showUploadProgress(overallProgress, 'Uploading ' + (fileIndex + 1) + ' of ' + totalFiles, 'progress-bar-striped progress-bar-animated');
                });

                xhr.addEventListener('load', () => {
                    if (xhr.status >= 200 && xhr.status < 300) {
                        try {
                            resolve(JSON.parse(xhr.responseText));
                        } catch (error) {
                            reject(new Error('Upload failed: invalid server response.'));
                        }

                        return;
                    }

                    reject(new Error((xhr.responseText || 'Upload failed.').trim()));
                });

                xhr.addEventListener('error', () => {
                    reject(new Error('Upload failed. Please check your connection and try again.'));
                });

                xhr.send(formData);
            });
        }

        async function uploadDocuments(files) {
            if (!config.uploadDocumentUrl || !files || files.length === 0) {
                return;
            }

            if (isUploadingDocuments) {
                showUploadStatus('A document upload is already in progress.', 'text-warning');
                return;
            }

            isUploadingDocuments = true;

            const filesToUpload = [];
            const duplicateItems = [];
            const knownDocumentKeys = new Set(interactionDocuments.map(document => getDocumentKey(document.fileName, document.fileSize)));

            Array.from(files).forEach(file => {
                if (isDuplicateFile(file, knownDocumentKeys)) {
                    duplicateItems.push(file);
                    return;
                }

                knownDocumentKeys.add(getDocumentKey(file.name, file.size));
                filesToUpload.push(file);
            });

            if (duplicateItems.length > 0) {
                uploadItems = uploadItems.concat(duplicateItems.map((file, index) => Object.assign(createUploadItem(file, index), {
                    status: 'failed',
                    progress: 100,
                    error: 'This document is already attached.'
                })));
                renderUploadQueue();
            }

            if (filesToUpload.length === 0) {
                isUploadingDocuments = false;
                showUploadStatus(duplicateItems.map(file => file.name + ': This document is already attached.').join(' | '), 'text-warning');
                showUploadProgress(null);
                return;
            }

            const pendingItems = filesToUpload.map(createUploadItem);
            uploadItems = uploadItems.concat(pendingItems);
            renderUploadQueue();
            showUploadProgress(0, 'Preparing upload...', 'progress-bar-striped progress-bar-animated');
            fileInput.disabled = true;

            try {
                let uploadedCount = 0;
                const failedUploads = duplicateItems.map(file => file.name + ': This document is already attached.');

                for (let i = 0; i < filesToUpload.length; i++) {
                    const file = filesToUpload[i];
                    const pendingItem = pendingItems[i];

                    updateUploadItem(pendingItem.id, {
                        status: 'uploading',
                        progress: 0,
                        error: null
                    });

                    showUploadStatus('Uploading ' + (i + 1) + ' of ' + filesToUpload.length + ' document(s)...', 'text-warning');

                    try {
                        const result = await uploadSingleDocument(file, pendingItem.id, i, filesToUpload.length);
                        const uploaded = (Array.isArray(result.uploaded) ? result.uploaded : [])
                            .map(normalizeDocumentInfo)
                            .filter(document => document && document.documentId);
                        const failed = Array.isArray(result.failed) ? result.failed : [];

                        if (uploaded.length > 0) {
                            uploadedCount += uploaded.length;
                            interactionDocuments = interactionDocuments.concat(uploaded);
                            updateUploadItem(pendingItem.id, {
                                status: 'uploaded',
                                progress: 100,
                                error: null
                            });
                        } else {
                            const missingUploadMessage = failed.length > 0
                                ? failed.map(entry => entry.fileName + ': ' + entry.error).join(' | ')
                                : 'The server did not confirm that the file was uploaded.';
                            if (failed.length === 0) {
                                failedUploads.push(missingUploadMessage);
                            }
                            updateUploadItem(pendingItem.id, {
                                status: 'failed',
                                progress: 100,
                                error: missingUploadMessage
                            });
                        }

                        if (failed.length > 0) {
                            const failedMessage = failed.map(entry => entry.fileName + ': ' + entry.error).join(' | ');
                            failedUploads.push(failedMessage);
                            updateUploadItem(pendingItem.id, {
                                status: 'failed',
                                progress: 100,
                                error: failedMessage
                            });
                        }
                    } catch (error) {
                        const errorMessage = error && error.message
                            ? error.message
                            : 'Upload failed. Please try again.';
                        console.error('Upload failed:', error);
                        failedUploads.push(file.name + ': ' + errorMessage);
                        updateUploadItem(pendingItem.id, {
                            status: 'failed',
                            progress: pendingItem.progress || 0,
                            error: errorMessage
                        });
                    }
                }

                renderDocuments();

                if (failedUploads.length > 0) {
                    removeUploadItems(item => item.status === 'uploaded');
                    showUploadStatus(
                        (uploadedCount > 0 ? ('Uploaded ' + uploadedCount + ' document(s). ') : '') + failedUploads.join(' | '),
                        uploadedCount > 0 ? 'text-warning' : 'text-danger');
                    showUploadProgress(null);
                    return;
                }

                removeUploadItems(() => true);
                showUploadStatus('Uploaded ' + uploadedCount + ' document(s).', 'text-success');
                showUploadProgress(null);
            } catch (error) {
                console.error('Upload failed:', error);
                showUploadStatus('Upload failed. Please try again.', 'text-danger');
                showUploadProgress(null);
            } finally {
                isUploadingDocuments = false;
                fileInput.disabled = false;
                fileInput.value = '';
            }
        }

        async function removeDocument(documentId) {
            if (!config.removeDocumentUrl || !documentId || !window.confirm('Remove this document?')) {
                return;
            }

            try {
                const response = await fetch(config.removeDocumentUrl, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({
                        itemId: config.itemId,
                        documentId: documentId
                    })
                });

                if (!response.ok) {
                    console.error('Failed to remove document:', await response.text());
                    showUploadStatus('Failed to remove document.', 'text-danger');
                    return;
                }

                const result = await response.json();
                const serverDocuments = (Array.isArray(result.documents) ? result.documents : [])
                    .map(normalizeDocumentInfo)
                    .filter(document => document && document.documentId);

                interactionDocuments = serverDocuments.length > 0
                    ? serverDocuments
                    : interactionDocuments.filter(document => document.documentId !== documentId);
                renderDocuments();
                showUploadStatus('Document removed.', 'text-success');
            } catch (error) {
                console.error('Remove failed:', error);
                showUploadStatus('Failed to remove document.', 'text-danger');
            }
        }

        const onFileInputChange = event => {
            const selectedFiles = event.target.files ? Array.from(event.target.files) : [];
            uploadDocuments(selectedFiles);
        };

        fileInput.addEventListener('change', onFileInputChange);
        fileInput[managerStateKey] = {
            dispose: () => {
                fileInput.removeEventListener('change', onFileInputChange);
            }
        };

        renderDocuments();
        renderUploadQueue();
        showUploadStatus(null);
        showUploadProgress(null);
    }

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
