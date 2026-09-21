# Typed Text and Realtime Voice on the Same Chat UI — Design

**Branch:** `claude/conversation-mode-realtime-toggle-f5d10a`
**Status:** Design only — no feature code in this pass.
**Goal:** Let one chat surface offer typed text *and* a realtime speech-to-speech conversation, toggled
by the user, instead of a realtime-capable deployment turning the whole UI voice-only. Conversation mode
becomes the thing that decides *how* the conversation is carried — a native speech-to-speech model, or
the existing speech-to-text + text-to-speech path — while the chat deployment goes back to meaning only
"the text model this profile talks to."

---

## 1. How it works today

### 1.1 One field answers two questions

`AIProfile.ChatDeploymentName` and `ChatInteraction.ChatDeploymentName` are simultaneously *the model I
converse with* and *the realtime model*. Everything downstream reads that one field through a single
predicate, `IAIDeploymentCapabilityService.IsRealtimeDeploymentAsync`
(`AIDeploymentCapabilityServiceExtensions.cs:33`), which returns true when the named deployment declares
`AIDeploymentFeatureNames.Realtime`.

The deployment pickers therefore offer a *union* of two slots — `GetConversationalDeploymentsAsync`
(`AIDeploymentManagerExtensions.cs:85`) concatenates the `chat` and `realtime` slots, and its own remarks
explain why: the picker answers "what can this profile talk to," and a realtime deployment can, it just
speaks instead of typing. Eleven call sites use it (both profile editors, both template editors, both
interaction editors, in MVC and Blazor).

### 1.2 The three places that force voice-only

| Where | What it does |
|---|---|
| Views | `Chat.cshtml:255` puts `d-none` on the textarea and the send button; same in `_ChatWidget.cshtml:265`, `AIChat/Chat.razor`, and `ChatInteractions/Chat.razor`. The elements stay in the DOM because the client reads the profile and session ids off them. |
| Mode resolution | `Chat.cshtml:56` and `ChatInteractions/Chat.razor:671` squash `ChatMode` to `TextInput` whenever the deployment is realtime, so `AudioInput` and `Conversation` never apply. |
| Hubs | `AIChatHubCore.cs:1762` and `ChatInteractionHubBase.cs:1109` reject a typed prompt outright with "realtime text not supported." |

### 1.3 What conversation mode actually is today

`ChatMode.Conversation` is a client-driven cascade: the browser captures the microphone, the hub's
`StartConversation` transcribes through the speech-to-text deployment, each final utterance is fed into
the *ordinary* text pipeline (`HandleSendMessageAsync` — full orchestration, persistence, citations,
documents), and assistant tokens are buffered into sentences and spoken through the text-to-speech
deployment. It is correct and feature-complete; it is also three sequential network round trips per turn,
which is the latency users are complaining about.

### 1.4 What is already decoupled (this is most of the work, and it is done)

The orchestration layer never had this coupling:

- `RealtimeOrchestrationRequest.RealtimeDeploymentName` already exists, and
  `DefaultRealtimeOrchestrator.cs:108` resolves it through `ResolveSlotAsync(AIDeploymentSlotNames.Realtime, ...)`:
  **explicit name -> site `DefaultRealtimeDeploymentName` -> first realtime-capable deployment.** That is
  exactly the "default to the site's realtime model, allow an override" behavior this feature wants. No
  new resolution logic is needed.
- `RealtimeChatRunContext.RealtimeDeploymentName` already carries it through `RealtimeChatSessionRunner`.
- The client JS already treats `realtimeEnabled` and `chatMode` as **independent** flags
  (`ai-chat.js:798-815`), and `toggleConversationMode()` (`ai-chat.js:2261`) already branches to the
  realtime controller when `realtimeEnabled` is set. **Nothing in the JS disables typing when realtime is
  on** — the voice-only feel comes entirely from the server-rendered `d-none` and the hub guard.

The hubs are the only thing that hardcodes `profile.ChatDeploymentName` as the realtime deployment
(`AIChatHubCore.cs:1054`, `ChatInteractionHubBase.cs:1109`).

### 1.5 The constraint we must not break

This codebase recently *removed* a second stored answer to this question. `AIProfile` used to carry a
separate `RealtimeDeploymentName`, and `ChatMode` used to have a `Realtime` member. Both were deleted
because they could disagree with the deployment's actual capabilities. The migration and its reasoning
are still in the tree:

- `AIProfile.cs:56-90` folds a legacy `RealtimeDeploymentName` onto `ChatDeploymentName` and moves the old
  chat deployment to the utility slot.
- `ChatModeJsonConverter.cs` reads an unrecognized `"Realtime"` chat mode as `TextInput` rather than
  throwing, with remarks stating outright that "a second stored answer could disagree with it."
- `RealtimeCapabilityMigrationTests.cs` asserts `RealtimeDeploymentName` never round-trips back to storage.

Any design here has to add flexibility **without** re-creating two fields that can contradict each other.

---

## 2. Decision — one stored answer, not three

### 2.1 The proposal on the table

The original ask was: conversation mode gains a sub-choice of *realtime* vs *speech-to-text + text-to-speech*;
picking realtime then offers a realtime model selector (defaulting to the site's), and picking STT+TTS
offers an STT selector and a TTS selector (each defaulting to the site's). That is three or four new
stored fields per profile, one of which (`transport`) is a claim about a deployment that the deployment
itself can already answer.

### 2.2 Why the transport choice does not belong on the profile

The realtime-vs-cascade distinction **already exists one layer down, as a deployment property.**
`CascadedRealtimeMetadata` (`Realtime/CascadedRealtimeMetadata.cs`) makes a deployment that declares the
`realtime` feature but is actually speech-to-text + chat + text-to-speech chained together.
`DefaultAIClientFactory.cs:227` composes it into a `CascadedRealtimeClient` that implements `IRealtimeClient`,
and `DefaultRealtimeOrchestrator` cannot tell the difference — it resolves one realtime deployment and
starts one session either way.

So storing `transport` on the profile would be a second answer to a question the deployment already
answers, which is precisely the failure mode section 1.5 describes. It would also push the work onto the
wrong person: every profile author would re-pick an STT and a TTS deployment, instead of an operator
configuring the cascade once for the whole site.

### 2.3 The recommended shape

**Conversation mode gets exactly one new field: the conversation deployment. It is optional, it is
resolved through the realtime slot, and empty means "use the site default."**

```
ChatMode.Conversation + a resolvable realtime-slot deployment
    -> realtime session, text input stays visible
ChatMode.Conversation + nothing resolvable
    -> today's client-driven STT/TTS conversation (unchanged)
ChatMode.AudioInput / TextInput
    -> unchanged
```

Whether the resolved deployment speaks natively or is a cascade of three other deployments is read off
the deployment, never stored on the profile. The picker asks one question instead of four, and the answer
is validated by the same capability service every other slot uses.

### 2.4 What changes for operators

- The chat deployment picker stops offering realtime models. A chat deployment is a text model again.
- A profile that wants voice sets chat mode to Conversation and, optionally, names a conversation
  deployment. Most will leave it empty and inherit the site default.
- To offer the STT+TTS path as a *deliberate* choice rather than a fallback, an operator creates a
  cascaded realtime deployment once (section 3.6) and either makes it the site default or names it on
  specific profiles.

---

## 3. Design, piece by piece

### 3.1 The new field

| Resource | Where it lives | Why |
|---|---|---|
| `AIProfile` | `ChatModeProfileSettings.ConversationDeploymentName` | Chat mode already lives in this settings bag; the field belongs next to the mode it qualifies. No schema change — `ExtensibleEntity` keys the bag by `typeof(T).Name`, so adding a property to the existing type is safe (renaming the type would not be). |
| `ChatInteraction` | `ChatInteraction.ConversationDeploymentName` | Sibling of the existing `RealtimeVoiceName` on the entity. Interaction chat mode is a *site* setting (`ChatInteractionSettings` via `SiteSettingsStore`), but the model choice is per-interaction, matching how `RealtimeVoiceName` already works. |

No new site setting is needed: `DefaultAIDeploymentSettings.DefaultRealtimeDeploymentName` already exists
and is already the second link in the slot resolution chain.

Both fields are nullable, and null means "resolve the default." They are never written with the resolved
value — storing the resolution would defeat the point of having a site default.

### 3.2 Deployment pickers

- Chat deployment pickers switch from `GetConversationalDeploymentsAsync` back to
  `GetAllBySlotAsync(AIDeploymentSlotNames.Chat, ...)` — eleven call sites listed in section 1.1.
- A new conversation deployment picker uses `GetAllBySlotAsync(AIDeploymentSlotNames.Realtime, ...)`,
  shown only when chat mode is Conversation, with an empty option labelled for the site default.
- `GetConversationalDeploymentsAsync` becomes unused by the editors. Keep it (it is public API on an
  abstractions package) but update its remarks to say it describes the pre-split picker.

### 3.3 Resolving the conversation deployment

Unchanged code, new input. The hubs stop passing `profile.ChatDeploymentName` and pass the new field:

- `AIChatHubCore.StartRealtimeConversation` (`:1013`) and `StartRealtimeWebRtc` (`:1177`) —
  replace the `realtimeDeploymentName = profile.ChatDeploymentName` assignment and the
  `IsRealtimeDeploymentAsync` gate that follows it.
- `ChatInteractionHubBase.PrepareRealtimeSessionAsync` (`:1083`) — same, for `interaction.ChatDeploymentName`.

The gate changes meaning: instead of "is the chat deployment realtime," it becomes "did the realtime slot
resolve to anything." `ResolveSlotAsync` already returns null when it cannot, and already validates the
capability, so the explicit `IsRealtimeDeploymentAsync` check before it can go.

Both hubs must also keep rejecting a *named* conversation deployment that is not realtime-capable, with a
message that names the deployment — a misconfigured profile should say so, not silently fall back to the
site default.

### 3.4 UI: mode resolution and the toggle

Replace the squash in all four surfaces (`Chat.cshtml:56`, `_ChatWidget.cshtml:50`,
`AIChat/Chat.razor`, `ChatInteractions/Chat.razor:671`) with:

```
realtimeEnabled  = chatMode == Conversation && conversationDeploymentResolves
chatMode         = Conversation when (realtimeEnabled) or (hasStt && hasTts)
                 = AudioInput   when hasStt
                 = TextInput    otherwise
```

Then remove the `d-none` on the textarea and send button, the `flex-grow-1` on the conversation button,
and the "This is a realtime voice profile" help text — replacing it with a hint shown only while a voice
session is live.

The JS needs less than the server does. `realtimeEnabled` and `chatMode` are already independent, and
`toggleConversationMode()` already dispatches correctly. What it needs is the *reverse* of the current
assumption: today a realtime session owning the UI is the only state, so nothing arbitrates between a
typed send and a live voice session. Add to `ai-chat.js`, `ai-chat-widget.js`, and `chat-interaction.js`:

- **Send while a voice session is live.** Simplest correct behavior: end the voice session, then send the
  typed prompt as a normal text turn. Attempting both concurrently would interleave two writers into one
  session and trip `StoreCommitterHubFilter`.
- **Button state.** The conversation button already swaps label and icon via its `data-start-*` /
  `data-end-*` attributes; the textarea should be visually de-emphasised (not disabled — see above) while
  a session is live.

Assets under `Assets/` require `npx gulp rebuild` and the regenerated `.map` files committed, or
`assets_validation` fails.

### 3.5 The hubs

Delete the two text-rejection guards (`AIChatHubCore.cs:1762`, `ChatInteractionHubBase.cs:1109`). They
exist because a realtime chat deployment genuinely cannot answer a text turn; once the chat deployment is
guaranteed text-capable again, they are guarding against nothing. Text turns then flow through
`ProcessChatPromptAsync` exactly as they do for a text-only profile.

`ChatSessionRealtimeTurnStore` already persists realtime turns as `AIChatSessionPrompt` records, so a
mixed transcript interleaves correctly in the UI and survives a reload with no change.

### 3.6 Cascaded deployments need an editor

`CascadedRealtimeMetadata` has **no admin UI** — grep finds it only in the orchestrator, the client
factory, and the voice resolver. It is reachable today only by seeding or by hand-editing stored JSON.
For "speech-to-text + text-to-speech" to be a thing a user can *choose*, the deployment editor needs a
section, shown when a deployment declares the `realtime` capability, naming its three legs.

Two constraints to surface in that editor, both already enforced in code:

1. The speech-to-text leg must expose a **streaming** realtime transcription client —
   `CreateCascadedRealtimeClientAsync` calls `CreateRealtimeClientAsync` on it
   (`DefaultAIClientFactory.cs:264`). A batch transcription endpoint will not work. This is stricter than
   today's `ChatMode.Conversation`, which works with any speech-to-text deployment.
2. The speech-to-text leg may not itself be cascaded (`DefaultAIClientFactory.cs:257`).

**Because of constraint 1, today's client-driven STT/TTS conversation mode stays.** It is the fallback
when no realtime deployment resolves, and it remains the only option for a site whose STT provider does
not stream. The views already have that branch; this design keeps it rather than deleting it.

### 3.7 History bridging — the only real engineering

`DefaultRealtimeOrchestrator.cs:85` hard-sets `ctx.ConversationHistory = []`. A realtime session starts
blind.

Today this is invisible: a realtime profile has no text turns to miss. Once both modes share a thread it
becomes the feature's most obvious bug — you type three messages, hit the mic, and the model has no idea
what you were discussing. The reverse direction already works, because text turns after a voice stretch
read the persisted `AIChatSessionPrompt` records that `ChatSessionRealtimeTurnStore` wrote.

There is no seeding path in the current API. `RealtimeSessionConfiguratorContext` exposes only
`Instructions`; `IRealtimeConversation` has `GroundTurnAsync` for per-turn knowledge but no "append a
prior conversation item."

Two options:

| | Approach | Cost | Limits |
|---|---|---|---|
| **A** | Fold the last *N* prompts (honoring `PastMessagesCount`) into `Instructions` as a transcript preamble, built in `DefaultRealtimeOrchestrator` from `request.ChatSession` / `request.Interaction`. | Small. No API change. Works identically for native and cascaded deployments. | Consumes instruction budget; the model treats history as system text rather than as turns. |
| **B** | Add a seed API — conversation items on `RealtimeSessionOptions`, plumbed through `IRealtimeSessionConfigurator`, with a per-provider implementation. | Larger, and `CascadedRealtimeSession` would need its own handling since it builds chat turns itself. | Correct shape; provider support varies. |

**Recommend A for the first release**, behind a bounded character budget, with B as a follow-up. A is
enough to make the toggle feel like one conversation, and it is the only option that behaves the same on
a cascaded deployment.

### 3.8 Voice selection

`ChatModeProfileSettings.VoiceName` is currently overloaded: `Chat.cshtml:69-70` reads it as the TTS voice
and as the realtime voice, picking a different default for each. With both paths live in one UI, that
ambiguity becomes reachable. Keep the single field (it is one user-facing question — "what should this
profile sound like") but resolve it per path, as the views already do, and let
`DefaultRealtimeVoiceResolver` continue to handle the cascade case where an OpenAI voice name would be
rejected by a third-party TTS leg.

---

## 4. Migration and compatibility

Existing profiles whose chat deployment is a realtime model must keep working. On read:

- The profile still names a realtime deployment as its chat deployment, which the new chat picker no
  longer offers. Fold it forward the same way `AIProfile.OnDeserialized` folded the legacy
  `RealtimeDeploymentName`: move it to `ChatModeProfileSettings.ConversationDeploymentName`, set chat mode
  to `Conversation`, and leave `ChatDeploymentName` empty so the chat slot's own default applies.
- If the profile named a utility deployment, that becomes the natural text model — the earlier migration
  already parked the pre-realtime chat deployment there for exactly this reason.
- Same shape for `ChatInteraction`, which has no settings bag: the entity field moves.

A profile migrated this way gains a text input it did not have. That is the feature, but it is a visible
change to an existing deployment's behavior and belongs in release notes.

`ChatModeJsonConverter` stays as is. Nothing here reintroduces `ChatMode.Realtime`.

---

## 5. Build order

1. **Model + resolution.** Add both fields; hubs read them instead of `ChatDeploymentName`; drop the
   `IsRealtimeDeploymentAsync` gates in favor of `ResolveSlotAsync` returning null. Add the misconfigured-
   deployment error. No UI yet — realtime still works, now driven by the new field.
2. **Migration.** `OnDeserialized` fold for `AIProfile`, equivalent for `ChatInteraction`, plus round-trip
   tests mirroring `RealtimeCapabilityMigrationTests`.
3. **Editors.** Chat picker narrowed to the chat slot; new conversation deployment picker under chat mode.
   Six editors across MVC and Blazor (profiles, templates, interactions).
4. **Chat surfaces.** New mode resolution, `d-none` removed, help text reworked. Four surfaces.
5. **Client.** Send-while-live arbitration and button state in the three JS bundles; `npx gulp rebuild`.
6. **Remove the text-rejection guards.** Do this last — it is the step that makes a mixed thread possible,
   and it should land with the UI that exercises it.
7. **History bridging (3.7, option A).**
8. **Cascade editor (3.6).** Independently shippable; the rest of the feature works without it, using
   native realtime deployments only.

Steps 1-6 deliver typed text plus realtime on one UI. Step 7 is what makes it feel like one conversation.

---

## 6. Verification

- **Unit.** Slot resolution with the field set, empty (site default), and naming a non-realtime deployment.
  Migration round-trips for both resources. `RealtimeChatSessionRunnerTests` currently asserts
  `RealtimeDeploymentName == profile.ChatDeploymentName` (`:67`) — that assertion inverts.
- **Host.** A green suite never builds the hosts' containers. Run both sample hosts; the mode resolution
  changes touch Razor runtime-compiled views in the MVC host, which recompiles edited `.cshtml` at a lower
  C# version than the build uses.
- **Manual, per surface** (AI Chat session, chat widget, Chat Interactions): type, then toggle voice, then
  type again, and confirm one interleaved transcript that survives a reload; confirm the model carries
  context across the toggle once step 7 lands; confirm a profile with no realtime deployment still gets the
  old STT/TTS conversation.
- **Realtime specifics.** Server-relay WebRTC can only be validated from a remote host — loopback hides
  blocked trickled candidates behind peer-reflexive candidates.

---

## 7. Open questions

1. **Send while a voice session is live** — end the session and send (recommended, section 3.4), queue the
   text until the session ends, or inject the typed text into the live session as a user turn? The third
   is the nicest UX and needs the same seed API as 3.7 option B.
2. **Does the conversation deployment belong on the profile at all**, or only on the site settings? A
   single site-wide realtime model covers most installs, and per-profile override is the flexibility that
   was asked for — but it is also the field that can be misconfigured. Recommend keeping it, with the
   explicit error in 3.3.
3. **Should `AudioInput` (dictation) and realtime coexist?** A profile could plausibly offer both a mic
   that dictates into the text box and a mic that starts a voice session. Two microphone buttons is a
   confusing UI; recommend realtime suppresses the dictation button for now.
4. **Citations in a mixed thread.** `[doc:n]` markers read poorly aloud and are already handled
   differently in realtime. In a mixed transcript the same thread will contain both renderings.
