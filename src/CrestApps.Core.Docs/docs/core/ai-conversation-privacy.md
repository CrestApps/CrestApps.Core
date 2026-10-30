---
sidebar_label: Conversation Privacy
sidebar_position: 7
title: Conversation Privacy
description: Redact personal details from ended AI conversations, keep only their metadata, and delete them after a retention period.
---

# Conversation Privacy

> What is kept of an AI profile's conversations once they end, and for how long.

Each AI profile chooses a storage mode and an optional retention in `AIProfileDataPrivacySettings`:

| Setting | Values | Meaning |
| --- | --- | --- |
| `StorageMode` | `Everything` (default) | The transcript, summaries and extracted fields are kept as they were. |
| | `RedactPersonalData` | Names, addresses, phone numbers, email addresses, card and identity numbers are replaced with placeholders such as `[NAME]` and `[EMAIL]` in the transcript, the title, the post-session results and the extracted fields. |
| | `MetadataOnly` | The transcript is deleted, and the title, post-session results and extracted values are cleared. Times, status, outcomes and scores are kept. |
| `StorageModeSinceUtc` | a time | When the mode was chosen. Conversations active before it are left as they were. |
| `RetentionDays` | a number of days, or `null` | Conversations with no activity for this long are deleted. |
| `NamesToKeep` | names | Names that are not personal data and stay in a redacted conversation: the name the assistant gives itself (the persona's) and the business's. The customer's name is still redacted wherever either side says it. |

## Adding it to an application

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddConversationPrivacy(options =>
        {
            options.SettleTime = TimeSpan.FromMinutes(15);
            options.BatchSize = 50;
        })
    )
);
```

`AddConversationPrivacy` registers `AIConversationPrivacyService`. It does not schedule anything: the host runs it.

## Running it

Call `ProcessAsync` once per profile on a schedule, from a hosted service or a background task, and keep the cursor
it returns for that profile:

```csharp
var privacy = scope.ServiceProvider.GetRequiredService<AIConversationPrivacyService>();

foreach (var profile in await profileManager.GetAsync(AIProfileType.Chat))
{
    cursors.TryGetValue(profile.ItemId, out var cursor);

    var result = await privacy.ProcessAsync(profile, cursor, cancellationToken);

    if (result.Cursor is null)
    {
        cursors.Remove(profile.ItemId);
    }
    else
    {
        cursors[profile.ItemId] = result.Cursor.Value;
    }
}

// Commit your unit of work here: the service stages changes and does not commit them.
```

For each profile, `ProcessAsync`:

1. Walks the profile's conversations in the order of their last activity, from the cursor (or from
   `StorageModeSinceUtc` on the first call), up to `SettleTime` ago. It stops at the first conversation still active
   or with post-session processing pending, so none is passed over, and at the first one that fails, which is
   tried again on the next call. Each finished conversation gets the storage mode once: it is marked with
   `AIConversationPrivacyMetadata`, and its stored remote address is cleared.
2. Deletes up to `BatchSize` conversations past `RetentionDays`, with their prompts and whatever the handlers delete.
3. Returns the new cursor, or `null` when the profile keeps everything so the host forgets it.

`ApplyAsync(session, mode)` and `DeleteAsync(sessionId)` are public for one-off use, such as a "forget this
conversation" request. `DeleteAsync` deletes a conversation of any user, visitor or system channel through
`IAIChatSessionStore.DeleteAsync`, and tells the `IAIChatSessionHandler`s before and after.

## Finding personal details

Email addresses, phone numbers, payment card numbers (checked with the Luhn algorithm) and US social security
numbers are recognized by pattern in `AIPersonalDataRedactor`. Names, street addresses, dates of birth and identity
numbers can't be, so `RedactPersonalData` asks the utility deployment for them once per conversation, over all of its
text at once, so a name given in the first message is also taken out of the summary written from it. Each part of a
full name is redacted on its own too. Without a utility deployment only the patterns are used. The request is metered
with the `AIUsagePurposes.PersonalDataRedaction` purpose.

The names in `NamesToKeep` are told to the utility deployment as not personal data, and are dropped from what it
returns even when it lists one anyway. A handler can add the names it knows (for example the persona's assistant name)
to `context.NamesToKeep` while collecting, and `ApplyAsync(session, mode, namesToKeep, ct)` takes them directly.

A transcript is redacted one speaker's turns at a time, read as one stream with the other speakers' short turns (up to
400 characters) left out. The patterns are found in the stream, so a detail dictated over several turns ("my email is
mike at", the assistant asking for the rest, then "example.com") is found whole and replaced in each turn it spans. An
email address found anywhere, such as the assistant reading it back, is also taken out of the other turns that say a
part of it: as the whole turn ("Test", "example.com"), chained with "at" or "dot", or ending the turn ("mike at"). A part
alone in a longer sentence is left alone. An email address spoken aloud ("mike at example dot com") is recognized as well as a
written one, and an email the utility deployment called an identity number is still labeled `[EMAIL]`. A spoken form
such as "look at example.com" can't be told from an address and is redacted too; "at" followed by an article or a
pronoun is not.

`AIPersonalDataRedactor.Redact(text, terms)` can be used on its own for any text, and `RedactConversation(speakers, turns, terms)` for
a whole transcript.

## Data kept elsewhere

The service changes the chat session and its prompts. Data a host or feature keeps about a conversation elsewhere,
such as evaluations or analytics records, takes part through an `IAIConversationPrivacyHandler`:

```csharp
public sealed class EvaluationPrivacyHandler : AIConversationPrivacyHandlerBase
{
    public override async Task CollectingAsync(AIConversationPrivacyContext context, CancellationToken cancellationToken)
    {
        var evaluation = await _store.FindBySessionAsync(context.Session.SessionId, cancellationToken);
        context.Items["evaluation"] = evaluation;
        context.Texts.Add(evaluation?.Summary);
    }

    public override async Task ApplyingAsync(AIConversationPrivacyContext context, CancellationToken cancellationToken)
    {
        if (context.Items["evaluation"] is not Evaluation evaluation)
        {
            return;
        }

        evaluation.Summary = context.Mode == AIDataStorageMode.MetadataOnly
            ? null
            : context.Redact(evaluation.Summary);

        await _store.SaveAsync(evaluation, cancellationToken);
    }

    public override Task DeletingAsync(AIChatSession session, CancellationToken cancellationToken)
        => _store.DeleteBySessionAsync(session.SessionId, cancellationToken);
}
```

- `CollectingAsync` runs first: load your data, keep it in `context.Items`, and add its free text to
  `context.Texts` so the personal details in it are found with the transcript's.
- `ApplyingAsync` runs once the details are found: redact with `context.Redact(text)`, or clear for `MetadataOnly`,
  and save.
- `DeletingAsync` runs before a conversation is deleted.

`CrestApps.Core.AI.Documents` registers one that deletes a deleted conversation's documents.

## Orchard Core

The Orchard Core AI modules add the profile editor tab, the background task that calls `ProcessAsync`, and handlers
for their evaluations and analytics records. See [orchardcore.crestapps.com](https://orchardcore.crestapps.com).
