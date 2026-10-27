---
sidebar_label: AI Profile Versions
sidebar_position: 8
title: AI Profile Versions
description: Number every save of an AI profile, keep a snapshot of each, compare any two and restore an earlier one.
---

# AI Profile Versions

> Every save of an AI profile is a numbered version you can compare and restore.

## Adding it to an application

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddProfileVersions(versions => versions
            .AddYesSqlStores()   // or .AddEntityCoreStores()
        )
    )
);
```

With YesSql, create the index table in your schema setup:

```csharp
await schemaBuilder.CreateAIProfileVersionIndexSchemaAsync(storeOptions);
```

`AddProfileVersions` registers `AIProfileVersionHandler`, a catalog handler for `AIProfile`. The store registers
`IAIProfileVersionStore`.

## What is kept

Each time a profile is created or updated through its manager, the handler:

1. numbers the save one above the highest version kept for the profile, and stores the number on the profile in
   `AIProfileVersionMetadata.Version`;
2. after the save, keeps an `AIProfileVersion` with the number, the time, the name of the signed-in user, an optional
   note and a copy of the whole profile in `Snapshot`.

To note why a save happened, set `AIProfileVersionMetadata.PendingNote` before saving. The note goes on the version
and is cleared from the profile, so it never reaches the stored profile.

New chat sessions record the version of the profile that answers them in their own `AIProfileVersionMetadata`, so
reports can compare versions.

## Reading the history

```csharp
var (versions, count) = await versionStore.PageAsync(profile.ItemId, page: 1, pageSize: 20);
var version = await versionStore.FindByIdAsync(versionId);
var latest = await versionStore.GetLatestVersionAsync(profile.ItemId);
```

## Comparing two versions

`AIProfileVersionComparer.Compare` lists every field and setting of two profiles side by side, changed or not, so a
setting added by any feature is compared without being listed anywhere:

```csharp
var rows = AIProfileVersionComparer.Compare(older.Snapshot, newer.Snapshot, templateTitles);

foreach (var row in rows.Where(row => row.Changed))
{
    Console.WriteLine($"{row.Section} / {row.Label}: {row.Then} -> {row.Now}");
}
```

- Rows are grouped into `AIProfileVersionComparer.Sections`: General, Instructions, Parameters, Capabilities,
  Prompt Security, Data Processing & Metrics and Other. The section is chosen from the name of the setting.
- A setting's group is named after its type: `AIProfileGuardrailSettings` reads as `Guardrail`, and its `Topics`
  field as `Guardrail › Topics`.
- What identifies the profile or changes on every save (its identifier, name, owner, times and version number) is
  left out.
- Pass template titles by name to show `Violence and weapons (guardrail-topic-violence)` instead of the bare name of a
  prompt template, speaking style or guardrail topic.
- `Multiline` and `IsCode` tell a view to show long text or JSON in a block.

## Restoring a version

```csharp
var metadata = profile.GetOrCreate<AIProfileVersionMetadata>();
metadata.PendingNote = $"Restored from version {version.Version}";
profile.Put(metadata);

AIProfileVersionRestorer.Restore(profile, version.Snapshot);

await profileManager.UpdateAsync(profile);
```

`Restore` copies what the snapshot holds onto the profile and keeps what identifies it: its identifier, name, type,
source, owner, creation time and version number. Saving it keeps the restore as a new version.

## Orchard Core

The Orchard Core AI module adds the versions page, the compare screen and the restore action. See
[orchardcore.crestapps.com](https://orchardcore.crestapps.com).
