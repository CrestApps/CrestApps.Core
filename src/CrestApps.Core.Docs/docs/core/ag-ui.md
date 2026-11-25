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

## Setup

AG-UI has no feature of its own to turn on. The services it needs (tool approvals, page context, shared state and frontend tools) come with the AI suite, and every hub that derives from `AIChatHubCore` has `RunAgent`. A host that already runs the AI chat has AG-UI.

**With the builder:**

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
    )
);

// Optional: how long a run keeps going after its page closes (two minutes by default).
builder.Services.Configure<AGUIChatOptions>(options => options.DisconnectedRunTimeout = TimeSpan.FromMinutes(1));

var app = builder.Build();

app.MapHub<AIChatHub>("/hubs/ai-chat");

// Optional: POST /api/ai/agui with a RunAgentInput; the answer is the run's events as Server-Sent Events.
app.MapAGUIChat<AIChatHub>()
    .RequireAuthorization();
```

**Without the builder**, the same services come from the extension methods `AddAISuite()` calls:

```csharp
builder.Services
    .AddCoreServices()
    .AddCoreAIServices()
    .AddCoreAIOrchestration();
```

Map `MapAGUIChat` only when other AG-UI clients (for example a CopilotKit front end) should reach your profiles over HTTP; the chat UI and the widget use the hub.

A hub whose client contract extends `IAIChatHubClient` uses `MapAGUIChat<THub, TClient>()`. The endpoint reads only JSON (`Content-Type: application/json`), which a cross-site page cannot send without the browser asking your site first, so it needs no antiforgery token. Add authorization and rate limiting to the returned builder as your host needs.

The chat UI uses `RunAgent` by default. Set `protocol: 'legacy'` in the chat configuration to keep the text-only `SendMessage` stream; a hub without `RunAgent` makes the UI fall back to it by itself.

| What you want | What to add |
| --- | --- |
| Tool calls, reasoning, citations and shared state in the chat | Nothing: the chat UI and widget use AG-UI by default. |
| Other AG-UI clients over HTTP | `app.MapAGUIChat<AIChatHub>()`. |
| A person approves a tool before it runs | `.RequireApproval()` on the tool; see [Tool approvals](#tool-approvals). |
| The agent reads and works on the page, and keeps going on the pages it opens | WebMCP page tools; see [Runs that continue on the next page](#runs-that-continue-on-the-next-page). |
| No page context sent to the model | `sendPageContext: false` in the chat configuration. |

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
| `CUSTOM` | `crestapps.assistant_message` (the assistant message's title and appearance), `crestapps.references` (its citations and files), `crestapps.message_replaced` (the output checks replaced what streamed), `crestapps.tool_calls_resolved` (the approvals of a paused message, after a resume), `crestapps.tool_call_display` (the title of a tool call, such as the agent's title for a call to an agent, right after its `TOOL_CALL_END`; it is kept as `ChatTurnToolCall.DisplayText`). |

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

The resumed run makes the approved calls, tells the model about the rejected ones (which never run), and lets the model carry on. An interrupt the client did not answer counts as rejected.

**A batch waits as a whole, and only what needs an approval asks.** When the model calls several tools at once and one of them needs an approval, nothing in the batch runs before the person answers. This is how `Microsoft.Extensions.AI`'s `FunctionInvokingChatClient` handles a batch (see its remarks on approvals), and the Azure OpenAI client's own tool loop does the same. Only the calls that need an approval show **Allow** and **Deny** and have an interrupt. A call that needs none shows as running, and the resumed run makes it whatever the person answered, because alone in its batch it would have run: denying the call that needs an approval does not stop it. `Microsoft.Extensions.AI` suggests `ChatOptions.AllowMultipleToolCalls = false` to avoid the wait; the framework does not set it, because the model would then call one tool at a time in every turn.

The client cannot decide such a call. Where an approval request is made, the server marks each request whose call goes to a function of that request that it runs and that requires no approval (`ToolApprovalRequests.NeedsNoApprovalProperty` in the request's `AdditionalProperties`). The paused message keeps the call as `Requested` with `ChatTurnToolCall.NeedsNoApproval`, and the resume approves it. A call to a tool the client runs, or to a tool the request does not offer, is not marked and keeps asking. Whether a call needs an approval is decided when the model makes it: if the profile's approval policy changes while the batch waits, the call runs as it was decided. The same applies inside an agent, with the agent's own tools.

One resume of a chat runs at a time: a second one that starts while the first runs (another tab, a client that retries) gets an error and runs nothing. When a resume fails before anything the person approved ran (the model is unavailable, the run is stopped), the message waits for the answers again and the client is sent the calls that wait, so the person can answer again. Once an approved call has run, the answers stand, so an approved call never runs twice. The claim is per process: a host with several instances also needs sticky routing for a session.

A tool registered as a destructive, external or security tool requires an approval without `.RequireApproval()`; see [Tool Kinds](./tools.md#tool-kinds).

Approvals happen only where someone can answer them. A tool that requires an approval never runs where nobody can be asked: the text-only chat stream, a chat interaction, a realtime conversation, a workflow, a channel such as SMS or voice, post-session processing, an agent an MCP client calls as a tool, and background work. There the model still sees the tool, but a call does not run it: the model gets a result that says the tool was not run because it needs a person's approval, which this conversation cannot ask for, and that it should tell the user. The `ToolDenials` metric counts each such tool with the reason `approval`. To run a tool without asking in those places, register it with `.RequireApproval(false)`, or, for a tool that requires an approval only because of its kind, let the profile's approval policy run it without asking (see [Running a Profile's Tools Without Asking](./tools.md#running-a-profiles-tools-without-asking)). A tool the [MCP server](../mcp/server.md#destructive-and-security-tools) or [WebMCP](../mcp/webmcp.md) exposes is called by its client directly, outside a chat: `ExposeAllTools` leaves out a tool that requires an approval, and a tool the `Tools` list names runs without one, because naming it is the host's own decision. A tool registry provider sets `ToolRegistryEntry.RequiresApproval` for its own tools. WebMCP tools marked `consequentialHint` need an approval in an AG-UI run unless the profile allows consequential page tools; see [WebMCP](../mcp/webmcp.md#annotations).

### Tools inside an agent

An [agent](./agents.md) runs inside one tool call of the assistant, with tools of its own. A tool of the agent that requires an approval asks the same person, through the assistant's run:

1. The agent's run pauses on the tool call. What the agent did so far is kept.
2. The assistant's run ends with an `interrupt` outcome, as it does for one of its own tools. The interrupt's `message` names the agent (by its title, the profile's `DisplayText`, when it has one) and the tool, and its `metadata` has `agentName` (the agent's technical name), `agentDisplayText` (its title, when it has one), `agentToolCallId` (the assistant's call to the agent), `toolName` and `arguments`. The run also streams the agent's tool call (`TOOL_CALL_START` / `ARGS` / `END`) so a client can show it, and the call to the agent has no result yet.
3. The answer starts a run with `resume`, exactly as above. The assistant's call to the agent is made again, and the agent continues where it stopped: the calls it made before the pause are not made again, an approved call runs, and a rejected call is reported to the agent as rejected, so the agent can say so.
4. When the agent then calls another tool that requires an approval, the run pauses again in the same way.

The chat UI shows the agent's tool call with its input and **Allow** and **Deny**, names the agent in the question (the `aguiAgentApprovalPrompt` text of the chat configuration, `Allow {0} to do this?` by default), and shows the call to the agent as waiting.

The paused message stores the assistant's call to the agent and the agent's tool call as waiting (`ChatTurnToolCall.AgentName` and `AgentCallId` mark the agent's call), and the agent's paused run in `ChatTurnToolActivity.AgentRuns`, which is not sent to clients. A tool result of the agent is kept there up to 16,000 characters; the agent reads the kept results when it resumes.

**When nobody can be asked, the tool does not run.** When the assistant's run cannot ask a person, a tool of the agent that requires an approval is treated as one of the assistant's own: the agent's model gets the result that says the tool was not run, and the agent reports that.

The default orchestrator pauses agents this way. The Claude and Copilot orchestrators cannot pause a run for an approval, so an assistant or an agent that runs on one of them gets the "was not run" result for such tools, and an assistant that runs on one of them cannot ask on behalf of its agents either.

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

This needs the [WebMCP page tools](../mcp/webmcp.md#page-tools), which give the agent `page_navigate`, `page_read_content` and the other page tools:

1. Register them: `.AddWebMcp(webMcp => webMcp.AddPageTools<AIChatHub>())` on the AI suite builder, or `services.AddCoreAIWebMcp().AddCoreAIWebMcpPageTools<AIChatHub>()` without the builder.
2. Turn on **Allow page tools** (`WebMcpProfileSettings.AllowPageTools`) on the AI profile the chat uses.
3. Add the WebMCP scripts to the pages, before the chat scripts, as the [WebMCP quick start](../mcp/webmcp.md#quick-start) shows.

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
