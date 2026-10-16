---
sidebar_label: AI Personas
sidebar_position: 5
title: AI Personas
description: Give AI profiles and chat interactions a reusable character, with its own instructions, name and voice, that is linked rather than copied.
---

# AI Personas

> A reusable character: **who** the assistant is, how it talks and which voice it speaks with.

An **AI persona** is a reusable character that AI profiles, chat interactions and profile templates link to. A
resource links to **one optional** persona by its name. The persona says *who* the assistant is; the profile's
system message and prompt templates still say *what* it does. Keeping the two apart lets one persona serve many
profiles, and one profile switch character without touching its rules.

Personas have their own catalog. They are not AI templates: a template is a blueprint that is copied when it is
used, while a persona stays linked, so an edit applies to everything that uses it from the next message.

## Adding personas to an application

Personas are an opt-in feature. Add them to the AI suite and choose where saved personas are stored:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddPersonas(personas => personas
            .AddYesSqlStores()   // or .AddEntityCoreStores()
        )
    )
);
```

`AddPersonas` registers:

- `IAIPersonaManager`, which lists, creates, updates and deletes personas;
- `IAIPersonaResolver`, which turns a link into a persona and chooses the voice;
- the completion handler that places a linked persona's instructions in the system message;
- a provider that reads persona files from the content root's `Templates/Personas` folder.

A YesSql host also creates the persona index table once, with `CreateAIPersonaIndexSchemaAsync`, next to its other
index tables.

Without `AddPersonas`, persona links are kept but ignored: chats run with the profile's own instructions and voice.
Code that may run in either kind of host resolves a persona through the `IServiceProvider` extensions
`ResolvePersonaAsync` and `SelectPersonaVoiceAsync`, which treat a missing feature as "no persona".

## What a persona holds

`AIPersona` is a catalog item with these properties:

| Property | Purpose |
| --- | --- |
| `Name` | The technical name profiles and interactions link to; it never changes once saved |
| `DisplayText`, `Description` | How the persona is shown in editors |
| `Instructions` | The persona prompt, rendered as Liquid and placed at the top of the system message; required |
| `AssistantName` | The name the assistant uses for itself; hosts may show it on assistant messages |
| `Language` | A BCP-47 tag such as `en-US`, used as the speech language when the caller does not name one |
| `VoiceName` | The provider-specific voice the persona speaks with |
| `VoiceDeploymentName` | The deployment the voice was chosen from, so it is only used on a deployment of the same provider |
| `AvatarUrl` | An optional avatar image URL |

## Managing personas

`IAIPersonaManager` is a named catalog manager. Its reads merge the saved personas with those of every
`IAIPersonaProvider`, such as persona files, and a saved persona replaces a provided one with the same name. Only
saved personas can be updated or deleted.

```csharp
var persona = await personaManager.NewAsync("friendly-assistant", new JsonObject
{
    ["DisplayText"] = "Friendly assistant",
    ["Instructions"] = "You are Ava, a warm and patient assistant.",
    ["AssistantName"] = "Ava",
});

var result = await personaManager.ValidateAsync(persona);

if (result.Succeeded)
{
    await personaManager.CreateAsync(persona);
}
```

Validation requires a unique name and instructions. An update never changes the name, because that is what links
point to.

## Linking a persona

A profile, interaction or profile template links to a persona with `AIPersonaMetadata`:

```csharp
profile.Put(new AIPersonaMetadata
{
    PersonaId = "friendly-assistant",
    Parameters = new Dictionary<string, object>
    {
        ["company"] = "Contoso",
    },
});
```

The link holds the persona's technical name, not a copy, so editing the persona changes every profile that
links to it. Names survive export and import across tenants; item ids do not.

Profile JSON accepts a top-level `PersonaId`: an empty value unlinks the persona and leaving the key out keeps the
current link. Chat interactions save it from the `personaId` hub setting with the same rule, through
`PersonaChatInteractionSettingsHandler`. A profile template can name the persona its profiles start with by adding
`PersonaId: friendly-assistant` to its front matter; profiles created from it copy the link.

## How a persona is applied

`IAIPersonaResolver` is the only code that turns a link into a persona. Every consumer goes through it, so a
persona behaves the same in chat, chat interactions, realtime sessions and voice.

- **Text, on every request.** Once a profile's or interaction's own handlers have built the system message, the
  persona handler resolves the persona and places the persona block first. When the persona has an assistant
  name, the block starts with "Your name is Ava.", so the model knows its name even when the instructions do not
  say it. The instructions can also use the name as the Liquid variable `assistant_name`; a link parameter with
  that name wins.

  ```text
  Your name is Ava.
  persona instructions

  selected prompt templates

  the profile's or interaction's own system message
  ```

  Business rules come last, so they win a conflict with personality. Because the system message is rebuilt for
  every request, a persona edit applies from the next message. The resolved persona, a `ResolvedAIPersona`, is
  also stored in the completion context under `AICompletionContextKeys.Persona`, and realtime sessions read their
  instructions from the same completion context.
- **Voice, when a session opens.** `SelectVoiceAsync` chooses, in order: an explicit voice (the caller's, or the
  one set on the profile or interaction), then the persona's voice, then the deployment's default voice. The
  persona's voice is skipped, with a warning in the log, when it was chosen on a deployment of another provider.
  Voice names belong to one provider, so an OpenAI realtime voice is never sent to Azure Speech. For a cascaded
  realtime deployment, the text-to-speech deployment it names is the one compared.
- **Missing personas degrade quietly.** A deleted or unknown persona is logged and the request carries on
  without one. A Liquid error in the instructions drops the instructions but keeps the rest of the persona.

New chat sessions record the persona their profile linked to in `AIChatSession.PersonaId`, for reporting.

## Persona files

Personas can ship as Markdown files. Put them in the content root's `Templates/Personas` folder, or embed them in an
assembly under `Templates/Personas` and register it with `AddEmbeddedPersonas(assembly)` on the persona builder.
The path below that folder, without its extension, becomes the persona's technical name. The front matter holds the
persona's fields and the body holds its instructions:

```markdown
---
Title: Friendly assistant
Description: Warm, patient and plain-spoken.
AssistantName: Ava
Language: en-US
---

You are Ava, a warm and patient assistant. Use plain words, keep answers short,
and ask one question at a time when you need more detail.
```

A saved persona with the same name replaces the file.

## Sample hosts

Both sample hosts, MVC and Blazor, add personas and include the UI:

- **Personas** in the admin menu lists, creates, edits and deletes personas. A persona that comes from a file opens
  as a copy; saving it creates a saved persona that replaces the file.
- The **AI persona** field on an AI profile, and on a Profile template, picks the persona. When the profile leaves
  its voice empty, the voice picker shows which persona voice will be used.

The menu entry and the pickers only appear when the host adds personas.

## Related docs

- [AI Profiles](./ai-profiles.md)
- [AI Templates](./ai-templates.md)
- [Realtime Voice](./realtime-voice.md)
- [Chat Interactions](./chat.md)
