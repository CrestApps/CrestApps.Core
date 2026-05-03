using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore.Models;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.Data.EntityCore.Services;

internal static class CatalogRecordFactory
{
    /// <summary>
    /// Gets the entity type discriminator for the specified CLR type.
    /// </summary>
    public static string GetEntityType<T>()
    {
        return GetEntityType(typeof(T));
    }

    /// <summary>
    /// Gets the entity type discriminator for the specified CLR type.
    /// </summary>
    /// <param name="type">The type.</param>
    public static string GetEntityType(Type type)
    {
        return type.FullName ?? type.Name;
    }

    /// <summary>
    /// Creates a new <see cref="CatalogRecord"/> and its associated <see cref="DocumentRecord"/>
    /// from the supplied domain model.
    /// </summary>
    public static CatalogRecord Create<T>(T model)
        where T : CatalogItem
    {
        ArgumentNullException.ThrowIfNull(model);

        var entityType = GetEntityType<T>();
        var record = new CatalogRecord
        {
            Document = new DocumentRecord
            {
                Type = entityType,
                Content = EntityCoreStoreSerializer.Serialize(model),
            },
            EntityType = entityType,
            ItemId = model.ItemId,
            Name = (model as INameAwareModel)?.Name,
            DisplayText = (model as IDisplayTextAwareModel)?.DisplayText,
            Source = (model as ISourceAwareModel)?.Source,
        };

        switch (model)
        {
            case AIProfile profile:
                record.Type = profile.Type.ToString();
                record.CreatedUtc = profile.CreatedUtc;
                break;
            case AIChatSessionPrompt prompt:
                record.SessionId = prompt.SessionId;
                record.CreatedUtc = prompt.CreatedUtc;
                break;
            case ChatInteractionPrompt prompt:
                record.ChatInteractionId = prompt.ChatInteractionId;
                record.CreatedUtc = prompt.CreatedUtc;
                break;
            case AIDocument document:
                record.ReferenceId = document.ReferenceId;
                record.ReferenceType = document.ReferenceType;
                record.CreatedUtc = document.UploadedUtc;
                break;
            case AIDocumentChunk chunk:
                record.AIDocumentId = chunk.AIDocumentId;
                record.ReferenceId = chunk.ReferenceId;
                record.ReferenceType = chunk.ReferenceType;
                break;
            case AIMemoryEntry memory:
                record.UserId = memory.UserId;
                record.Name = memory.Name;
                record.UpdatedUtc = memory.UpdatedUtc;
                record.CreatedUtc = memory.CreatedUtc;
                break;
            case SearchIndexProfile indexProfile:
                record.Type = indexProfile.Type;
                record.CreatedUtc = indexProfile.CreatedUtc;
                break;
        }

        return record;
    }

    /// <summary>
    /// Updates an existing <see cref="CatalogRecord"/> and its <see cref="DocumentRecord"/>
    /// from the supplied domain model.
    /// </summary>
    public static void Update<T>(CatalogRecord record, T model)
        where T : CatalogItem
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(model);

        var updated = Create(model);
        record.Name = updated.Name;
        record.DisplayText = updated.DisplayText;
        record.Source = updated.Source;
        record.SessionId = updated.SessionId;
        record.ChatInteractionId = updated.ChatInteractionId;
        record.ReferenceId = updated.ReferenceId;
        record.ReferenceType = updated.ReferenceType;
        record.AIDocumentId = updated.AIDocumentId;
        record.UserId = updated.UserId;
        record.Type = updated.Type;
        record.CreatedUtc = updated.CreatedUtc;
        record.UpdatedUtc = updated.UpdatedUtc;
        record.Document.Content = updated.Document.Content;

        // Detach the Document created by Create() so EF doesn't try to add a duplicate.
        updated.Document = null;
    }

    /// <summary>
    /// Materializes a domain model from the <see cref="DocumentRecord.Content"/>
    /// of the supplied <see cref="CatalogRecord"/>.
    /// </summary>
    public static T Materialize<T>(CatalogRecord record)
        where T : CatalogItem
    {
        ArgumentNullException.ThrowIfNull(record);

        return EntityCoreStoreSerializer.Deserialize<T>(record.Document.Content);
    }
}
