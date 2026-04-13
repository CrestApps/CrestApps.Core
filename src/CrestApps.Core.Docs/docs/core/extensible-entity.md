---
sidebar_label: ExtensibleEntity
sidebar_position: 3
title: ExtensibleEntity & JSON Configuration
description: Learn how to use ExtensibleEntity for dynamic property storage and how to configure JSON serialization with ExtensibleEntityJsonOptions.
---

# ExtensibleEntity & JSON Configuration

> Base class for entities that support dynamic, schema-free properties alongside their typed fields.

## Overview

`ExtensibleEntity` provides a `Properties` dictionary that allows you to attach arbitrary strongly-typed data to any entity without changing its schema. This powers features like AI profile metadata, chat session annotations, and custom application data.

## Quick Reference

| Method | When to Use |
|--------|-------------|
| `TryGet<T>(out T result)` | **Read-only** access — returns `false` when the key is missing (zero allocations) |
| `GetOrCreate<T>()` | **Read-write** access — creates a new `T` when the key is missing |
| `Alter<T>(Action<T>)` | Modify a stored object in-place (creates if missing) |
| `Put<T>(T value)` | Store a strongly-typed object (key = type name) |
| `Put(string name, object value)` | Store a value under a custom key |
| `Has<T>()` | Check whether a key exists |
| `Remove<T>()` | Remove a stored object |

### Prefer `TryGet` for Read-Only Access

When you only need to **read** a property and do not need a default instance, use `TryGet<T>`:

```csharp
if (profile.TryGet<MyMetadata>(out var metadata))
{
    // Use metadata — only entered when data is present.
    Console.WriteLine(metadata.Label);
}
```

`GetOrCreate<T>()` allocates a new `T` every time the key is absent. Reserve it for cases where you intend to write back:

```csharp
var metadata = profile.GetOrCreate<MyMetadata>();
metadata.Label = "Updated";
profile.Put(metadata);
```

## Configuring JSON Serialization

All `ExtensibleEntity` property reads and writes go through a shared `JsonSerializerOptions` instance. The defaults work for most scenarios, but you can customize them.

### Default Settings

| Setting | Default Value |
|---------|---------------|
| `DefaultIgnoreCondition` | `WhenWritingNull` |
| `PreferredObjectCreationHandling` | `Populate` |
| `PropertyNameCaseInsensitive` | `true` |
| `ReadCommentHandling` | `Skip` |
| `AllowTrailingCommas` | `true` |
| `WriteIndented` | `false` |
| `NumberHandling` | `AllowReadingFromString` |
| Built-in converters | `JsonStringEnumConverter` |

### Using the Options Pattern (Recommended)

When you use the CrestApps framework with `AddCrestAppsCore()`, register your customizations through the standard options pattern:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
    )
);

// Add a custom JSON converter for ExtensibleEntity properties.
builder.Services.Configure<ExtensibleEntityJsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new MyCustomJsonConverter());
});
```

The framework registers an `IHostedService` that reads `IOptions<ExtensibleEntityJsonOptions>` at startup and pushes the configured `JsonSerializerOptions` to `ExtensibleEntityExtensions.JsonSerializerOptions`. Your customizations are applied before any request processing begins.

### Direct Static Property (Advanced)

If you are not using the CrestApps DI extensions (e.g., in a console app or test harness), set the static property directly **before any serialization occurs**:

```csharp
var options = ExtensibleEntityJsonOptions.CreateDefaultSerializerOptions();
options.Converters.Add(new MyCustomJsonConverter());

ExtensibleEntityExtensions.JsonSerializerOptions = options;
```

:::warning
`JsonSerializerOptions` becomes immutable after the first serialization call (System.Text.Json caches internal metadata). Configure it during application startup, before any `ExtensibleEntity` methods are invoked.
:::

## Full Example

```csharp
using CrestApps.Core;

// Define a custom metadata type.
public sealed class InvoiceMetadata
{
    public string InvoiceNumber { get; set; }
    public decimal Amount { get; set; }
    public bool IsPaid { get; set; }
}

// Store metadata on an entity.
entity.Put(new InvoiceMetadata
{
    InvoiceNumber = "INV-2025-001",
    Amount = 149.99m,
    IsPaid = false,
});

// Read-only check — no allocation if missing.
if (entity.TryGet<InvoiceMetadata>(out var invoice))
{
    Console.WriteLine($"Invoice {invoice.InvoiceNumber}: ${invoice.Amount}");
}

// Modify in-place.
entity.Alter<InvoiceMetadata>(inv =>
{
    inv.IsPaid = true;
});
```
