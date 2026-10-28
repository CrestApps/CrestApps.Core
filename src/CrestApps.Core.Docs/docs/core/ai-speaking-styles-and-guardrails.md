---
sidebar_label: Speaking Styles and Guardrails
sidebar_position: 6
title: Speaking Styles and Guardrails
description: Add tested prompt sections to an AI profile's system message, and refuse topics on what the customer says and on the AI's replies.
---

# Speaking Styles and Guardrails

> Two kinds of System Prompt template an AI profile picks from: **how** it talks, and **what** it refuses.

Both features keep their content as System Prompt templates in their own category, so a host lists, edits and
exports them like any other template, and a profile stores only the template names it picked.

| Feature | Template category | Profile settings | What it does |
| --- | --- | --- | --- |
| Speaking styles | `Speaking style` (`AISpeakingStyles.Category`) | `AIProfileSpeakingStyleSettings` | Adds each chosen template's text to the end of the system message. |
| Guardrails | `Guardrail topic` (`AIGuardrailTopics.Category`) | `AIProfileGuardrailSettings` | Asks the utility model whether a message falls under a refused topic, and answers with a placeholder reply when it does. |

## Adding them to an application

Both are opt-in. Add them to the AI suite:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddSpeakingStyles()
        .AddGuardrails()
    )
);
```

`AddSpeakingStyles` registers `AISpeakingStyleCompletionContextBuilderHandler`. `AddGuardrails` registers
`AIGuardrailService`. Both register the framework's default templates with the
[System Prompt template seeder](#default-templates).

## Speaking styles

A profile lists the templates it follows, in order:

```csharp
profile.AlterSettings<AIProfileSpeakingStyleSettings>(settings =>
    settings.Styles = [AISpeakingStyles.AIDisclosure, AISpeakingStyles.EchoVerification]);
```

When a completion context is built for the profile, each template is rendered and appended to the system message,
after the persona and the profile's own instructions, separated by a blank line. A template that is missing or fails
to render is left out and logged; the conversation goes on.

The framework ships eight styles: `AIDisclosure`, `ScopeLimits`, `HighEmpathy`, `EchoVerification`, `NatoSpelling`,
`SpeechNormalization`, `FuzzyNameMatching` and `FillerWords` (see `AISpeakingStyles.BuiltIn`). Any System Prompt
template in the `Speaking style` category can be picked too.

## Guardrails

A guardrail checks one or both sides of a conversation against the topics a profile refuses, on top of
[prompt security](prompt-security.md):

```csharp
profile.AlterSettings<AIProfileGuardrailSettings>(settings =>
{
    settings.CheckCustomer = true;
    settings.CheckReplies = true;
    settings.Topics = [AIGuardrailTopics.Violence, AIGuardrailTopics.RegulatedAdvice];
    settings.OtherTopics = "Prices of our competitors.";
    settings.PlaceholderReply = "I can only help with your order.";
});
```

A topic is a template in the `Guardrail topic` category: its title names the topic and its body defines it.
`OtherTopics` describes topics of this profile alone in a sentence or two. `AIGuardrailService.CheckAsync` sends the
definitions and the text to the profile's utility deployment, and returns an `AIGuardrailVerdict` when the text falls
under a refused topic, or `null` when it may go on.

- A topic the model names that the profile did not refuse is allowed, so a guardrail never refuses more than it was
  set to.
- When the check cannot run (no utility deployment, the model fails), the text goes on unchecked and a warning is
  logged. A guardrail is a second line behind the instructions and prompt security.
- Each request is metered with the `AIUsagePurposes.Guardrail` purpose.

### In the chat hub

With guardrails added, `AIChatHubCore` checks typed messages and replies without any host code:

- **What the customer says** is checked after prompt security in `ValidatePromptSecurityAsync`. A refused message is
  blocked like one prompt security refuses, with a `DetectionRule` of `guardrail:` followed by the topic, and the
  caller is shown the profile's placeholder reply.
- **The AI's reply** is checked after the `IOutputSecurityFilter` in `ValidateOutputAsync`. A refused reply is not
  saved and the placeholder reply is shown instead.

Other channels, such as text messages or turn-based calls, call `AIGuardrailService.CheckAsync` themselves with
`AIGuardrailDirection.Customer` or `AIGuardrailDirection.Reply`, and use `AIGuardrailService.GetPlaceholderReply`.

### Reacting to a guardrail

Register an `IAIGuardrailHandler` to be told when a guardrail triggers, for example to start a workflow or raise an
alert. A failing handler is logged and does not stop the others.

```csharp
public sealed class GuardrailAlertHandler : IAIGuardrailHandler
{
    public Task TriggeredAsync(AIGuardrailVerdict verdict, CancellationToken cancellationToken)
    {
        // verdict.ProfileId, verdict.SessionId, verdict.Direction, verdict.Category, verdict.Reason
        return Task.CompletedTask;
    }
}

services.AddScoped<IAIGuardrailHandler, GuardrailAlertHandler>();
```

## Listing the templates of a category

`AITemplateCategoryExtensions` lists the templates of a category from `ITemplateService`, read from files and saved
alike, once each by name and ordered by title. Use it to fill a picker:

```csharp
var styles = await templateService.ListOptionsByCategoryAsync(AISpeakingStyles.Category);
var topics = await templateService.ListOptionsByCategoryAsync(AIGuardrailTopics.Category);
```

## Default templates

The default styles and topics are not served as read-only templates. They ship as Markdown files embedded under
`SystemPromptDefaults` and are copied once into the saved AI profile templates as System Prompt templates, where
they can be edited or deleted like any other. `IAISystemPromptTemplateSeeder` does the copy:

```csharp
var seeder = serviceProvider.GetRequiredService<IAISystemPromptTemplateSeeder>();

// Creates each default that has no saved template of the same name yet.
var created = await seeder.SeedAsync();
```

Call it once, from a migration or a setup step, with the AI profile template store registered. A template that
already exists by name is never overwritten, but one deleted after seeding is created again if the seeder runs
again. Pass a category to seed only that category's defaults.

A host or package can ship its own defaults the same way: embed Markdown templates under `SystemPromptDefaults` and
register the assembly:

```csharp
services.AddCoreAISystemPromptTemplateDefaults(typeof(Startup).Assembly);
```

Each file's front matter sets the template's title, description, category and whether it is listable, and its body
becomes the system message:

```markdown
---
Title: Echo Verification
Description: Reads back what the customer gave before acting on it
IsListable: false
Category: Speaking style
---

## Read back what you are given

When the customer gives you something you will act on or record, repeat it back once and ask them to confirm.
```

## Orchard Core

The Orchard Core AI modules build the profile editor tabs, the template screens and an **AI Guardrail Triggered**
workflow event on these services. See [orchardcore.crestapps.com](https://orchardcore.crestapps.com).
