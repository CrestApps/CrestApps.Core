# @crestapps/ai-chat-ui

Browser-ready JavaScript widgets and CSS for **CrestApps AI** chat sessions and chat interactions. Drop them into any page that uses the [CrestApps.Core](https://github.com/CrestApps/CrestApps.Core) backend to get a fully functional AI chat experience with streaming responses, document uploads, image generation, interactive charts, copy-to-clipboard, and user feedback buttons.

## Features

| Feature | Details |
|---------|---------|
| **Streaming responses** | Real-time token-by-token output via SignalR or REST |
| **Image generation** | Inline rendering of images produced by `GenerateImageTool` |
| **Interactive charts** | [Chart.js](https://www.chartjs.org/) rendering of charts produced by `GenerateChartTool` (optional peer dependency) |
| **Document uploads** | Drag-and-drop or file-picker uploads attached to chat sessions |
| **Copy to clipboard** | One-click copy of any assistant message |
| **Feedback buttons** | Thumbs-up / thumbs-down per message |
| **Markdown rendering** | Powered by [marked](https://github.com/markedjs/marked) and [DOMPurify](https://github.com/cure53/DOMPurify) |

## Installation

```bash
npm install @crestapps/ai-chat-ui
```

### Optional: Chart.js Support

To render interactive charts, also install Chart.js:

```bash
npm install chart.js
```

## Included Files

After installation the `dist/` directory contains:

| File | Description |
|------|-------------|
| `ai-chat.js` / `.min.js` | AI Chat widget — manages sessions, uploads, streaming, and the full chat UI |
| `chat-interaction.js` / `.min.js` | Chat Interaction widget — lighter standalone chat experience |
| `document-drop-zone.js` / `.min.js` | Drag-and-drop file upload component |
| `technical-name-generator.js` / `.min.js` | Auto-generates URL-safe technical names from display names |
| `chat-widget.css` / `.min.css` | Styles for the floating AI Chat widget |
| `document-drop-zone.css` / `.min.css` | Styles for the document drop-zone component |

## Usage

### Via CDN (simplest)

Use [jsDelivr](https://www.jsdelivr.com/) to load files directly — no install required:

```html
<!-- CSS -->
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/chat-widget.min.css" />
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/document-drop-zone.min.css" />

<!-- Required dependencies -->
<script src="https://cdn.jsdelivr.net/npm/vue@3/dist/vue.global.prod.js"></script>
<script src="https://cdn.jsdelivr.net/npm/marked/marked.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/dompurify/dist/purify.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr/dist/browser/signalr.min.js"></script>

<!-- Optional: Chart.js for interactive charts -->
<script src="https://cdn.jsdelivr.net/npm/chart.js"></script>

<!-- CrestApps AI Chat UI -->
<script src="https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/ai-chat.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/document-drop-zone.min.js"></script>
```

> **Tip:** Replace `@1.0.0-preview-22` with the version you want, or omit it to always get the latest release.

### Via npm

```bash
npm install @crestapps/ai-chat-ui
```

Then copy the files from `node_modules/@crestapps/ai-chat-ui/dist/` into your web application's static assets folder and reference them with `<script>` and `<link>` tags.

### CDN URL Pattern

All files in the package are available via jsDelivr using this pattern:

```
https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@<version>/dist/<file>
```

| Asset | CDN URL |
|-------|---------|
| `ai-chat.min.js` | `https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/ai-chat.min.js` |
| `chat-interaction.min.js` | `https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/chat-interaction.min.js` |
| `document-drop-zone.min.js` | `https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/document-drop-zone.min.js` |
| `technical-name-generator.min.js` | `https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/technical-name-generator.min.js` |
| `chat-widget.min.css` | `https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/chat-widget.min.css` |
| `document-drop-zone.min.css` | `https://cdn.jsdelivr.net/npm/@crestapps/ai-chat-ui@1.0.0-preview-22/dist/document-drop-zone.min.css` |

### Initializing the AI Chat Widget

```javascript
var chatManager = window.openAIChatManager();
chatManager.createInstance({
    containerEl: '#ai-chat-container',
    profileId: 'your-profile-id',
    baseUrl: '/ai/chat-sessions',
    hubUrl: '/ai-chat-hub'
});
```

### Initializing the Chat Interaction Widget

```javascript
var interactionManager = window.chatInteractionManager();
interactionManager.createInstance({
    containerEl: '#chat-interaction-container',
    deploymentName: 'your-deployment-name',
    baseUrl: '/chat-interactions',
    hubUrl: '/chat-interaction-hub'
});
```

## Client-Side Dependencies

Both widgets expect these libraries to be loaded on the page **before** the CrestApps script:

| Library | Required | Purpose |
|---------|----------|---------|
| [Vue 3](https://vuejs.org/) | ✅ Yes | Reactive UI rendering |
| [marked](https://github.com/markedjs/marked) | ✅ Yes | Markdown → HTML conversion |
| [DOMPurify](https://github.com/cure53/DOMPurify) | ✅ Yes | HTML sanitization |
| [SignalR](https://learn.microsoft.com/aspnet/core/signalr/) | ✅ Yes | Real-time streaming |
| [Chart.js](https://www.chartjs.org/) | ❌ Optional | Interactive chart rendering |
| [highlight.js](https://highlightjs.org/) | ❌ Optional | Code syntax highlighting |

## Backend Requirements

These widgets are designed to work with the [CrestApps.Core](https://github.com/CrestApps/CrestApps.Core) .NET backend. You need:

1. **AI Chat sessions** — register with `.AddAISuite(ai => ai.AddChatInteractions(...))` or AI Profiles
2. **At least one AI provider** — e.g., `.AddOpenAI()`, `.AddAzureOpenAI()`, `.AddOllama()`
3. **SignalR hubs** — register with `.AddSignalR()`

See the [CrestApps.Core documentation](https://crestapps.com/docs/core/chat) for full backend setup instructions.

## License

[MIT](https://github.com/CrestApps/CrestApps.Core/blob/main/LICENSE)
