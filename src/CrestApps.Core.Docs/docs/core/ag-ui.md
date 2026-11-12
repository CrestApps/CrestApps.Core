---
sidebar_label: AG-UI
sidebar_position: 10
title: AG-UI (Agent-User Interaction protocol)
description: Run the AI chat as an AG-UI agent, over the chat hub or an HTTP endpoint, with tool calls, reasoning, shared state, page context and tool approvals the user sees and answers.
---

# AG-UI

> The AI chat speaks [AG-UI](https://docs.ag-ui.com), the open protocol between an agent and the app a person uses it in. The user sees what the agent does, shares state with it, and approves what it may do.

AG-UI is an event protocol: a client starts a *run* with a `RunAgentInput` (the thread, the newest message, the tools it runs itself, the state it shares, context about the page) and the agent answers with a stream of events: `RUN_STARTED`, text, reasoning, tool calls and their results, state changes, and `RUN_FINISHED` or `RUN_ERROR`. CrestApps runs the AI chat's whole pipeline (profiles, orchestration, retrieval, tools, guardrails, session history) behind it:

| Transport | Who uses it |
| --- | --- |
| The chat hub's `RunAgent` method (SignalR) | The CrestApps chat UI and chat widget (`ai-chat.js`), by default. |
| `MapAGUIChat<THub>()` (HTTP `POST`, Server-Sent Events) | Any AG-UI client, such as CopilotKit, the AG-UI client SDKs, or your own app. |

Both run the same pipeline through the same hub, so the hub's profile and session authorization apply to both.

## Packages

| Package | What it has |
| --- | --- |
| `CrestApps.Core.AI.AGUI` | `AGUIRunEventWriter` (chat completion updates to AG-UI events), `AGUIRunRequest` (what a run input asks for), and `AGUIJson` (the wire format). It uses the MIT-licensed [AG-UI .NET SDK](https://github.com/ag-ui-protocol/ag-ui) (`AGUI.Abstractions`, `AGUI.Formatting`). |
| `CrestApps.Core.AI.Chat` | `AIChatHubCore.RunAgent`, the resumable runs, and `MapAGUIChat<THub>()`. |
| `CrestApps.AI.Resources` (`@crestapps/ai-chat-ui`) | The chat UI's AG-UI client: tool cards, reasoning, approvals, page context and shared state. |

## Quick start

The chat hub has `RunAgent` already. Map the HTTP endpoint next to the hub when other AG-UI clients should reach your profiles:

```csharp
app.MapHub<AIChatHub>("/hubs/ai-chat");

// POST /api/ai/agui with a RunAgentInput; the answer is the run's events as Server-Sent Events.
app.MapAGUIChat<AIChatHub>()
    .RequireAuthorization();
```

A hub whose client contract extends `IAIChatHubClient` uses `MapAGUIChat<THub, TClient>()`. The endpoint reads only JSON (`Content-Type: application/json`), which a cross-site page cannot send without the browser asking your site first, so it needs no antiforgery token. Add authorization and rate limiting to the returned builder as your host needs.

The chat UI uses `RunAgent` by default. Set `protocol: 'legacy'` in the chat configuration to keep the text-only `SendMessage` stream; a hub without `RunAgent` makes the UI fall back to it by itself.

## The run input

| `RunAgentInput` member | What the chat does with it |
| --- | --- |
| `threadId` | The chat session id. Empty starts a new session, whose id comes back in `RUN_STARTED`. |
| `runId` | Echoed in `RUN_STARTED` and `RUN_FINISHED`. |
| `messages` | The chat keeps the conversation on the server, so only the newest user message is read, plus the results of tool calls that waited for the client (`role: "tool"`). |
| `forwardedProps.profileId` | The AI profile to chat with (required for a new message). `forwardedProps.sessionProfileId` names a session-level prompt profile. |
| `context` | Context about where the user is; see [Page context](#page-context). |
| `state` | The state the client shares; see [Shared state](#shared-state). |
| `tools` | Tools the client runs itself; see [Frontend tools](#frontend-tools). |
| `resume` | The answers to the interrupts of the previous run; see [Tool approvals](#tool-approvals). |

## The events

| Event | When |
| --- | --- |
| `RUN_STARTED` / `RUN_FINISHED` / `RUN_ERROR` | Every run. `RUN_FINISHED` has a `success` outcome (with `pendingToolCallIds` when frontend tools wait for the client) or an `interrupt` outcome. Errors the hub reports to the caller (an unknown profile, a refused session, a rate limit) end the run with `RUN_ERROR`; a refused session start has the code `session_start_rejected`. |
| `TEXT_MESSAGE_START` / `CONTENT` / `END` | The answer. Its message id is the id of the stored assistant message, so a client can rate or copy it. Text the model writes again after a tool call is a new text message whose id ends with `-1`, `-2`, and so on. |
| `REASONING_START` … `REASONING_END` | The model's visible reasoning, when the model streams it. |
| `TOOL_CALL_START` / `ARGS` / `END` / `RESULT` | Every tool call and its result: the profile's tools, MCP and agent tools, page tools, and the shared state tool. Results are cut to 4,000 characters. |
| `STATE_SNAPSHOT` / `STATE_DELTA` | The shared state, and each change the agent makes, as JSON Patch. |
| `CUSTOM` | `crestapps.assistant_message` (the assistant message's title and appearance), `crestapps.references` (its citations and files), `crestapps.message_replaced` (the output checks replaced what streamed), `crestapps.tool_calls_resolved` (the approvals of a paused message, after a resume). |

The tool calls are stored with the assistant message (`ChatTurnToolActivity`), so the session history shows them again and the next turns of the conversation replay the calls and their results to the model.

## Page context

The chat UI sends the page the user is on with each message: its title and address, and the text the user last selected on the page. Scripts on the page add their own:

```javascript
window.crestAppsAGUI.registerContextProvider(() => ({
    description: 'Record being edited',
    value: `Article #${articleId}: ${document.querySelector('#Title').value}`,
}));
```

The context reaches the model in the system message as data, never as instructions, at most 20 entries of 2,000 characters each. Set `sendPageContext: false` in the chat configuration to send none. On the server, `ChatResponseHandlerContext.ClientContext` carries it, and `AICompletionContextKeys.ClientContext` hands it to the orchestration.

## Shared state

A page can share a JSON object with the agent, which both of them change: a form being edited, a plan, a cart. The page registers a state provider; the chat sends the state with each message and applies the agent's changes:

```javascript
window.crestAppsAGUI.registerStateProvider({
    get: () => ({ orderDetails: { giftMessage: gift.value, deliveryDate: date.value } }),
    apply: (state, delta) => {
        gift.value = state.orderDetails?.giftMessage ?? '';
        date.value = state.orderDetails?.deliveryDate ?? '';
    },
});
```

When a run has a state, the model sees it in the system message and gets the `update_shared_state` tool, which changes it with JSON Patch operations (`add`, `replace`, `remove`). Each change is applied on the server (`ChatSharedState`) and sent as `STATE_DELTA`; the chat applies it and calls `apply`. A `crestapps:agui-state` DOM event also fires on `document` with the new state. The state lives on the page: the server uses what the client sends with each run and does not store it.

## Tool approvals

A tool that changes or deletes data, sends a message, or does anything else that is hard to undo can ask the user first. Mark it when you register it:

```csharp
services.AddCoreAITool<DeleteArticleTool>(DeleteArticleTool.TheName)
    .Selectable()
    .RequireApproval();
```

In a chat run that can ask for approvals (an AG-UI run), the tool is wrapped in `Microsoft.Extensions.AI`'s `ApprovalRequiredAIFunction`. When the model calls it, the run does not call the tool: it ends with an `interrupt` outcome whose interrupts name each call and ask for `{ "approved": true | false }`, and the paused message is stored with the calls waiting. The chat UI shows **Allow** and **Deny** on the call. The answer starts a new run with `resume`:

```json
{
  "threadId": "<session id>",
  "runId": "<new run id>",
  "messages": [],
  "resume": [{ "interruptId": "<interrupt id>", "status": "resolved", "payload": { "approved": true } }],
  "forwardedProps": { "profileId": "<profile id>" }
}
```

The resumed run makes the approved calls, tells the model about the rejected ones (which never run), and lets the model carry on. An interrupt the client did not answer counts as rejected. When the model calls several tools at once and one needs an approval, every call of that batch waits for one.

Approvals happen only where someone can answer them: other callers (the text-only chat stream, background tasks, other channels) run the tool as before. A tool registry provider sets `ToolRegistryEntry.RequiresApproval` for its own tools. WebMCP tools marked `consequentialHint` need an approval in an AG-UI run unless the profile allows consequential page tools; see [WebMCP](../mcp/webmcp.md#annotations).

## Frontend tools

An AG-UI client can give the agent tools it runs itself, in `RunAgentInput.tools`. The model sees them as declarations: when it calls one, the run ends with `RUN_FINISHED` whose `pendingToolCallIds` name the calls. The client runs them and sends the results in its next run, as `role: "tool"` messages with the call's `toolCallId`, and the agent carries on:

```json
{
  "threadId": "<session id>",
  "runId": "<new run id>",
  "messages": [{ "id": "t1", "role": "tool", "toolCallId": "call_123", "content": "Sunny, 21 °C" }],
  "tools": [{ "name": "get_weather", "description": "Gets the weather in a city.", "parameters": { "type": "object", "properties": { "city": { "type": "string" } } } }],
  "forwardedProps": { "profileId": "<profile id>" }
}
```

The chat UI does not use frontend tools: the page's tools reach the chat through [WebMCP](../mcp/webmcp.md#the-ai-chat-as-a-webmcp-agent), which runs them in the tab during the run.

## Runs that continue on the next page

A page tool can open another page (`page_navigate`). The tool answers first and opens the page a moment later, so the agent gets the result. In a run on the chat hub, opening a page then ends the turn: the rest of the work needs the new page's tools, which only the chat on that page has.

1. The turn is saved with the tool calls it made and marked to continue on the next page (`ChatTurnToolActivity.ContinuesOnNextPage`), and the run sends the `crestapps.continues_on_next_page` event.
2. The chat on the next page loads the session (a new chat's run saves it as soon as it starts). When the tab that sent the message opened that page, and the session's last message is marked, the chat starts a run with `forwardedProps.continueRun` set to `true` and `forwardedProps.continuePage` set to the page's title and address.
3. `ContinueAGUIRunAsync` clears the mark, so the turn continues only once, and tells the model which page loaded and to carry on with the user's request, opening the right page if this is not it (`GetContinueOnNextPagePrompt`, which a hub can override). The model calls the new page's tools, and may open another page.

The chat continues at most five pages in a row; the next message the user sends starts a new chain. A run over HTTP (`MapAGUIChat`) has no page to continue on, so its turn goes on as usual.

A run also keeps going on the server after its connection drops, for `AGUIChatOptions.DisconnectedRunTimeout` (two minutes by default), and its answer is saved. A stop the user asks for ends it at once. When it ends, the hub sends the session to its group, so the chat on the next page shows the answer, or continues the turn, as soon as it is saved.

Tools that end a turn this way set `AICompletionContextKeys.EndTurnAfterToolCalls` in `AIInvocationScope.Current.Items` when the completion context carries `AICompletionContextKeys.ContinuesOnNextPage`. The model is not asked again after that round of tool calls.

```csharp
services.Configure<AGUIChatOptions>(options => options.DisconnectedRunTimeout = TimeSpan.FromMinutes(1));
```

## Providers

Every provider streams tool calls and results: clients built by `IAIClientFactory` through `Microsoft.Extensions.AI`'s `FunctionInvokingChatClient`, and the Azure OpenAI client on both its Chat Completions and Responses API paths. Tool results in the history reach the providers as tool messages; a history cut to its newest messages drops the results whose calls were cut off.

## Try it

Both sample hosts map `MapAGUIChat<AIChatHub>()`, and their chat widget runs over AG-UI. On the **WebMCP Playground** page, with a widget profile that allows page tools:

- "Check order A-1001 and add a desk lamp to my cart" shows the tool calls as they run.
- "Place the order" waits for **Allow** or **Deny**: `store.place_order` is consequential.
- "Write a gift message for my sister" fills the **Order details** card, which the page shares with the agent.

From a terminal:

```bash
curl -N -X POST https://localhost:5100/api/ai/agui -H "Content-Type: application/json" \
  -d '{"threadId":"","runId":"r1","messages":[{"id":"m1","role":"user","content":"Hello"}],"tools":[],"context":[],"forwardedProps":{"profileId":"<profile id>"}}'
```

## Not covered yet

- Chat Interactions (`ChatInteractionHubBase`) keep the text-only stream.
- The realtime voice conversation does not emit AG-UI events.
