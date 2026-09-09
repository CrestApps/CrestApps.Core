using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// Covers the read-time normalization that projects a stored legacy deployment purpose onto model
/// capability features.
/// </summary>
/// <remarks>
/// The purpose enum is gone; the stored data is not. These tests pin the upgrade path for records written
/// before the change. The normalization is load bearing rather than cosmetic: <c>textGeneration</c> is
/// opt-out while the five features that replace the remaining purpose names are opt-in, so without it an
/// existing embedding, transcription, or image deployment would simultaneously vanish from its own picker
/// and appear in the chat picker.
/// </remarks>
public sealed class AIDeploymentPurposeCompatibilityTests
{
    [Fact]
    public void Normalize_WhenEmbeddingOnlyAndNoMetadata_ShouldAddTextEmbeddingButNotTextGeneration()
    {
        // Arrange
        var deployment = new AIDeployment
        {
            Name = "text-embedding-3-large",
        };

        // Act
        var changed = AIDeploymentPurposeCompatibility.Normalize(deployment, ["Embedding"]);

        // Assert
        Assert.True(changed);
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.TextEmbedding));
        Assert.False(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
    }

    [Fact]
    public void Normalize_WhenChatPurposeDeclaresRealtime_ShouldNotAddTextGeneration()
    {
        // Arrange. A speech-to-speech-only deployment was stored as the Chat purpose with the realtime
        // feature and deliberately no textGeneration: it serves only the realtime API and answers a text
        // completion with an HTTP 400.
        var deployment = CreateDeployment(AIDeploymentFeatureNames.Realtime);

        // Act
        AIDeploymentPurposeCompatibility.Normalize(deployment, ["Chat"]);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.Realtime));
        Assert.False(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
    }

    [Fact]
    public void Normalize_WhenChatAndEmbeddingWithExistingMetadata_ShouldAddBothAdditively()
    {
        // Arrange. Guards against the "skip deployments that already declare metadata" shortcut: the editors
        // give every new deployment tool calling and streaming by default, so such a deployment would never
        // receive text embedding.
        var deployment = CreateDeployment(AIDeploymentFeatureNames.ToolCalling, AIDeploymentFeatureNames.Streaming);

        // Act
        AIDeploymentPurposeCompatibility.Normalize(deployment, ["Chat", "Embedding"]);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.TextEmbedding));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.ToolCalling));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.Streaming));
    }

    [Theory]
    [InlineData("Embedding", AIDeploymentFeatureNames.TextEmbedding)]
    [InlineData("Image", AIDeploymentFeatureNames.ImageOutput)]
    [InlineData("Vision", AIDeploymentFeatureNames.ImageInput)]
    [InlineData("SpeechToText", AIDeploymentFeatureNames.SpeechToText)]
    [InlineData("TextToSpeech", AIDeploymentFeatureNames.TextToSpeech)]
    public void Normalize_WhenOptInPurposeIsNamed_ShouldAddMatchingFeatureAndNothingElse(string legacyPurpose, string expectedFeature)
    {
        // Arrange
        var deployment = new AIDeployment { Name = "deployment" };

        // Act
        AIDeploymentPurposeCompatibility.Normalize(deployment, [legacyPurpose]);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.Equal([expectedFeature], metadata.Features);
    }

    [Theory]
    [InlineData("Chat")]
    [InlineData("Utility")]
    [InlineData("chat")]
    public void Normalize_WhenTextPurposeIsNamed_ShouldAddTextGeneration(string legacyPurpose)
    {
        // Arrange
        var deployment = new AIDeployment { Name = "deployment" };

        // Act
        AIDeploymentPurposeCompatibility.Normalize(deployment, [legacyPurpose]);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.Equal([AIDeploymentFeatureNames.TextGeneration], metadata.Features);
    }

    [Theory]
    [InlineData("Chat, Utility")]
    [InlineData("3")]
    public void Normalize_WhenFlagsRoundTrippedAsAListOrANumber_ShouldStillBeUnderstood(string legacyPurpose)
    {
        // Arrange. A flags enum reaches JSON as a name, a comma-separated list, or the numeric value, and
        // every one of those shapes exists in stored data. Chat|Utility is 3.
        var deployment = new AIDeployment { Name = "deployment" };

        // Act
        AIDeploymentPurposeCompatibility.Normalize(deployment, [legacyPurpose]);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.Equal([AIDeploymentFeatureNames.TextGeneration], metadata.Features);
    }

    [Fact]
    public void Normalize_WhenNumericFlagsCombineSeveralPurposes_ShouldAddEveryImpliedFeature()
    {
        // Arrange. 5 is Chat|Embedding.
        var deployment = new AIDeployment { Name = "deployment" };

        // Act
        AIDeploymentPurposeCompatibility.Normalize(deployment, ["5"]);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.TextEmbedding));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
    }

    [Fact]
    public void Normalize_WhenNoLegacyPurposeIsPresent_ShouldLeaveDeploymentUnconstrained()
    {
        // Arrange. An untagged deployment has to keep declaring no metadata at all, because that is exactly
        // what keeps textGeneration opt-out for it.
        var deployment = new AIDeployment { Name = "deployment" };

        // Act
        var changed = AIDeploymentPurposeCompatibility.Normalize(deployment, []);

        // Assert
        Assert.False(changed);
        Assert.False(deployment.TryGet<AIDeploymentMetadata>(out _));
    }

    [Fact]
    public void Normalize_WhenPropertiesIsNull_ShouldNotThrow()
    {
        // Arrange. ConfigurationAIDeploymentSource leaves Properties null for a configured deployment that
        // declares none, and the extensible-entity accessors dereference it unconditionally.
        var deployment = new AIDeployment
        {
            Name = "text-embedding-3-large",
            Properties = null,
        };

        // Act
        AIDeploymentPurposeCompatibility.Normalize(deployment, ["Embedding"]);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.TextEmbedding));
    }

    [Fact]
    public void Normalize_WhenRunTwice_ShouldNotDuplicateFeatures()
    {
        // Arrange. Both compat layers can run over the same instance, and a record is read many times.
        var deployment = new AIDeployment { Name = "text-embedding-3-large" };

        // Act
        AIDeploymentPurposeCompatibility.Normalize(deployment, ["Embedding"]);
        var changedOnSecondRun = AIDeploymentPurposeCompatibility.Normalize(deployment, ["Embedding"]);

        // Assert
        Assert.False(changedOnSecondRun);
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.Equal([AIDeploymentFeatureNames.TextEmbedding], metadata.Features);
    }

    [Theory]
    [InlineData("Purpose")]
    [InlineData("Capability")]
    [InlineData("Type")]
    public void Deserialize_WhenStoredRecordCarriesALegacyPurposeField_ShouldNormalizeOnTheStorePath(string fieldName)
    {
        // Arrange. This is the store deserialization half of the compat design; it has to work without any
        // catalog handler in play, under every field name this value has had.
        var json = $$"""
            {
              "Name": "whisper",
              "ModelName": "whisper-1",
              "{{fieldName}}": "SpeechToText"
            }
            """;

        // Act
        var deployment = JsonSerializer.Deserialize<AIDeployment>(json, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.SpeechToText));
        Assert.False(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
    }

    [Fact]
    public void Deserialize_WhenStoredRecordCarriesTheArrayShape_ShouldNormalizeEveryName()
    {
        // Arrange
        var json = """
            {
              "Name": "gpt-5",
              "Purpose": [ "Chat", "Vision" ]
            }
            """;

        // Act
        var deployment = JsonSerializer.Deserialize<AIDeployment>(json, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.ImageInput));
    }

    [Fact]
    public void Deserialize_WhenStoredRecordIsRealtimeOnly_ShouldNotAddTextGenerationOnTheStorePath()
    {
        // Arrange
        var json = """
            {
              "Name": "gpt-realtime",
              "ModelName": "gpt-realtime",
              "Purpose": "Chat",
              "Properties": {
                "AIDeploymentMetadata": {
                  "Features": [ "realtime" ]
                }
              }
            }
            """;

        // Act
        var deployment = JsonSerializer.Deserialize<AIDeployment>(json, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.Realtime));
        Assert.False(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
    }

    [Fact]
    public void Deserialize_WhenARecordCarriesBothPurposeAndType_ShouldPreferPurpose()
    {
        // Arrange. JSON property order is not guaranteed, so precedence is applied by rank rather than by
        // arrival order.
        var json = """
            {
              "Name": "deployment",
              "Type": "SpeechToText",
              "Purpose": "Embedding"
            }
            """;

        // Act
        var deployment = JsonSerializer.Deserialize<AIDeployment>(json, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.Equal([AIDeploymentFeatureNames.TextEmbedding], metadata.Features);
    }

    [Fact]
    public void SaveThenReload_WhenAnOperatorRemovesAnImpliedCapability_ShouldNotRestoreIt()
    {
        // Arrange. The projection is a one-time translation of a record written before capabilities
        // existed, not a standing rule. Once an operator has re-saved the deployment, the legacy purpose
        // must no longer have a say — otherwise unticking a capability in the editor is silently undone on
        // the next read.
        var stored = """
            {
              "Name": "gpt-4o",
              "ModelName": "gpt-4o",
              "Purpose": "Chat, Embedding"
            }
            """;

        var deployment = JsonSerializer.Deserialize<AIDeployment>(stored, ExtensibleEntityExtensions.JsonSerializerOptions);
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var projected));
        Assert.Contains(AIDeploymentFeatureNames.TextEmbedding, projected.Features);

        // Act. The operator unticks text embedding, and the record is saved and read back.
        deployment.Put(new AIDeploymentMetadata { Features = [AIDeploymentFeatureNames.TextGeneration] });
        var persisted = JsonSerializer.Serialize(deployment, ExtensibleEntityExtensions.JsonSerializerOptions);
        var reloaded = JsonSerializer.Deserialize<AIDeployment>(persisted, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert. The legacy field is not written back, so nothing re-applies it.
        Assert.DoesNotContain("Purpose", persisted, StringComparison.Ordinal);
        Assert.True(reloaded.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.Equal([AIDeploymentFeatureNames.TextGeneration], metadata.Features);
    }

    [Fact]
    public void Normalize_WhenRunAgainAfterAnUpdateThatRemovedACapability_ShouldNotRestoreIt()
    {
        // Arrange. The update path re-runs normalization on the already-loaded model. Because the legacy
        // purpose is forgotten once applied, a payload that removes a capability is not overruled by the
        // purpose the record was originally written with.
        var stored = """
            {
              "Name": "gpt-4o",
              "Purpose": "Chat, Embedding"
            }
            """;

        var deployment = JsonSerializer.Deserialize<AIDeployment>(stored, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Act. An update declares text generation only, and carries no legacy purpose of its own.
        deployment.Put(new AIDeploymentMetadata { Features = [AIDeploymentFeatureNames.TextGeneration] });
        AIDeploymentPurposeCompatibility.Normalize(deployment);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.Equal([AIDeploymentFeatureNames.TextGeneration], metadata.Features);
    }

    [Theory]
    [InlineData("Purpose")]
    [InlineData("Capability")]
    [InlineData("Type")]
    public async Task PopulateAsync_WhenLegacyShapeNamesEmbedding_ShouldDeclareTextEmbedding(string propertyName)
    {
        // Arrange. The JSON-node half of the compat design covers recipes, configuration, and API payloads,
        // across all three legacy field names.
        var handler = CreateHandler();
        var deployment = new AIDeployment();
        var data = new JsonObject
        {
            ["Name"] = "text-embedding-3-large",
            [propertyName] = "Embedding",
        };

        // Act
        await handler.InitializingAsync(new InitializingContext<AIDeployment>(deployment, data), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.TextEmbedding));
        Assert.False(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
    }

    [Fact]
    public async Task PopulateAsync_WhenLegacyArrayShapeCombinesPurposes_ShouldDeclareBothFeatures()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = new AIDeployment();
        var data = new JsonObject
        {
            ["Name"] = "gpt-5",
            ["Purpose"] = new JsonArray("Chat", "Vision"),
        };

        // Act
        await handler.InitializingAsync(new InitializingContext<AIDeployment>(deployment, data), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.ImageInput));
    }

    [Fact]
    public async Task PopulateAsync_WhenPayloadDeclaresRealtime_ShouldNotAddTextGeneration()
    {
        // Arrange. Normalization has to run after the payload's own capability metadata is merged, otherwise
        // the realtime declaration is not yet visible to the rule.
        var handler = CreateHandler();
        var deployment = new AIDeployment();
        var data = new JsonObject
        {
            ["Name"] = "gpt-realtime",
            ["Purpose"] = "Chat",
            ["Properties"] = new JsonObject
            {
                ["AIDeploymentMetadata"] = new JsonObject
                {
                    ["Features"] = new JsonArray(AIDeploymentFeatureNames.Realtime),
                },
            },
        };

        // Act
        await handler.InitializingAsync(new InitializingContext<AIDeployment>(deployment, data), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(metadata.SupportsFeature(AIDeploymentFeatureNames.Realtime));
        Assert.False(metadata.SupportsFeature(AIDeploymentFeatureNames.TextGeneration));
    }

    private static AIDeploymentCatalogHandler CreateHandler()
    {
        return new AIDeploymentCatalogHandler(
            Mock.Of<IHttpContextAccessor>(),
            TimeProvider.System,
            Mock.Of<IAIDeploymentStore>(),
            Mock.Of<IAIProviderConnectionStore>(),
            Options.Create(new AIOptions()),
            new PassThroughStringLocalizer<AIDeploymentCatalogHandler>());
    }

    private sealed class PassThroughStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private static AIDeployment CreateDeployment(params string[] features)
    {
        var deployment = new AIDeployment
        {
            Name = "deployment",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Features = features,
        });

        return deployment;
    }
}
