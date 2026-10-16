---
sidebar_label: AI Personas
sidebar_position: 5
title: AI Personas
description: Give AI profiles and chat interactions a reusable character, with its own instructions, name, greeting and voice, that is linked rather than copied.
---

# AI Personas

> A reusable character: **who** the assistant is, how it talks and which voice it speaks with.

An **AI persona** is an AI template whose source is `Persona`. An AI profile, a chat interaction or a profile
template links to **one optional** persona by its name. The persona says *who* the assistant is; the profile's
system message and prompt templates still say *what* it does. Keeping the two apart lets one persona serve many
profiles, and one profile switch character without touching its rules.

## What a persona holds

Persona templates store a `PersonaTemplateMetadata` in their properties:

| Property | Purpose |
| --- | --- |
| `Instructions` | The persona prompt, rendered as Liquid and placed at the top of the system message |
| `AssistantName` | The name the assistant uses for itself; hosts may show it on assistant messages |
| `Greeting` | An optional first assistant message, used when the profile has no welcome message of its own |
| `Language` | A BCP-47 tag such as `en-US`, used as the speech language when the caller does not name one |
| `VoiceName` | The provider-specific voice the persona speaks with |
| `VoiceDeploymentName` | The deployment the voice was chosen from, so it is only used on a deployment of the same provider |
| `AvatarUrl` | An optional avatar image URL |

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
`PersonaChatInteractionSettingsHandler`.

## How a persona is applied

`IAIPersonaResolver` is the only code that turns a link into a persona. Every consumer goes through it, so a
persona behaves the same in chat, chat interactions, realtime sessions and voice.

- **Text, on every request.** The profile and chat interaction completion context builders resolve the persona and
  place its rendered instructions first:

  ```text
  persona instructions

  selected prompt templates

  the profile's or interaction's own system message
  ```

  Business rules come last, so they win a conflict with personality. Because the system message is rebuilt for
  every request, a persona edit applies from the next message. The resolved persona is also stored in the
  completion context under `AICompletionContextKeys.Persona`, and realtime sessions read their instructions from
  the same completion context.
- **Voice, when a session opens.** `SelectVoiceAsync` chooses, in order: an explicit voice (the caller's, or the
  one set on the profile or interaction), then the persona's voice, then the deployment's default voice. The
  persona's voice is skipped, with a warning in the log, when it was chosen on a deployment of another provider.
  Voice names belong to one provider, so an OpenAI realtime voice is never sent to Azure Speech. For a cascaded
  realtime deployment, the text-to-speech deployment it names is the one compared.
- **Missing personas degrade quietly.** A deleted or unknown persona is logged and the request carries on
  without one. A Liquid error in the instructions drops the instructions but keeps the rest of the persona.

New chat sessions record the persona their profile linked to in `AIChatSession.PersonaId`, for reporting.

## Persona files

Persona templates can ship as files beside other AI profile templates. Set `Source: Persona` in the front matter;
the body becomes the instructions:

```markdown
---
Title: Friendly assistant
Description: Warm, patient and plain-spoken.
Category: Personas
Source: Persona
AssistantName: Ava
Greeting: Hi, I'm Ava. How can I help?
Language: en-US
---

You are Ava, a warm and patient assistant. Use plain words, keep answers short,
and ask one question at a time when you need more detail.
```

A saved persona with the same name replaces the file, as with other AI profile templates. A profile template can
name the persona its profiles start with by adding `PersonaId: friendly-assistant` to its own front matter.

## Related docs

- [AI Profiles](./ai-profiles.md)
- [AI Templates](./ai-templates.md)
- [Realtime Voice](./realtime-voice.md)
- [Chat Interactions](./chat.md)
