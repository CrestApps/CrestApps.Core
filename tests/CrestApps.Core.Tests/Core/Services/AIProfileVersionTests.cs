using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIProfileVersionTests
{
    private static readonly DateTime _now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ProfileVersions_NumberEachSave_AndKeepTheRestoreNoteOffTheProfile()
    {
        var store = new InMemoryVersionStore();
        var handler = new AIProfileVersionHandler(store, new HttpContextAccessor(), new FixedTimeProvider(new DateTimeOffset(_now)));
        var profile = new AIProfile { ItemId = "profile1", DisplayText = "Support" };

        await handler.CreatingAsync(new CreatingContext<AIProfile>(profile), TestContext.Current.CancellationToken);
        await handler.CreatedAsync(new CreatedContext<AIProfile>(profile), TestContext.Current.CancellationToken);

        var metadata = profile.GetOrCreate<AIProfileVersionMetadata>();
        metadata.PendingNote = "Restored from version 1";
        profile.Put(metadata);

        await handler.UpdatingAsync(new UpdatingContext<AIProfile>(profile, null), TestContext.Current.CancellationToken);
        await handler.UpdatedAsync(new UpdatedContext<AIProfile>(profile), TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], store.Versions.Select(version => version.Version));
        Assert.All(store.Versions, version => Assert.Equal(_now, version.CreatedUtc));
        Assert.Null(store.Versions[0].Note);
        Assert.Equal("Restored from version 1", store.Versions[1].Note);
        Assert.Equal(2, profile.GetOrCreate<AIProfileVersionMetadata>().Version);
        Assert.Null(profile.GetOrCreate<AIProfileVersionMetadata>().PendingNote);
        Assert.Null(store.Versions[1].Snapshot.GetOrCreate<AIProfileVersionMetadata>().PendingNote);

        // A snapshot is a copy: changing the profile afterwards leaves the kept version alone.
        profile.DisplayText = "Changed";
        Assert.Equal("Support", store.Versions[1].Snapshot.DisplayText);
    }

    [Fact]
    public async Task ProfileVersions_ContinueAfterTheHighestKeptVersion()
    {
        var store = new InMemoryVersionStore();
        store.Versions.Add(new AIProfileVersion { ItemId = "v7", ProfileId = "profile1", Version = 7 });
        var handler = new AIProfileVersionHandler(store, new HttpContextAccessor(), new FixedTimeProvider(new DateTimeOffset(_now)));
        var profile = new AIProfile { ItemId = "profile1" };

        await handler.UpdatingAsync(new UpdatingContext<AIProfile>(profile, null), TestContext.Current.CancellationToken);
        await handler.UpdatedAsync(new UpdatedContext<AIProfile>(profile), TestContext.Current.CancellationToken);

        Assert.Equal(8, store.Versions.Last().Version);
    }

    [Fact]
    public void ProfileVersions_RestoreKeepsWhatIdentifiesTheProfile()
    {
        var profile = new AIProfile { ItemId = "p1", Name = "support", DisplayText = "Support", ChatDeploymentName = "gpt-new", OwnerId = "owner", CreatedUtc = _now };
        profile.Put(new AIProfileVersionMetadata { Version = 5 });

        var snapshot = new AIProfile { ItemId = "other", Name = "old-name", DisplayText = "Support (old)", ChatDeploymentName = "gpt-old", OwnerId = "someone", WelcomeMessage = "Hi!" };
        snapshot.Put(new AIProfileMetadata { SystemMessage = "Be brief." });
        snapshot.Put(new AIProfileVersionMetadata { Version = 2 });

        AIProfileVersionRestorer.Restore(profile, snapshot);

        Assert.Equal("p1", profile.ItemId);
        Assert.Equal("support", profile.Name);
        Assert.Equal("owner", profile.OwnerId);
        Assert.Equal(_now, profile.CreatedUtc);
        Assert.Equal("Support (old)", profile.DisplayText);
        Assert.Equal("gpt-old", profile.ChatDeploymentName);
        Assert.Equal("Hi!", profile.WelcomeMessage);
        Assert.Equal("Be brief.", profile.GetOrCreate<AIProfileMetadata>().SystemMessage);
        Assert.Equal(5, profile.GetOrCreate<AIProfileVersionMetadata>().Version);

        // The snapshot is copied, not shared.
        profile.Put(new AIProfileMetadata { SystemMessage = "Changed." });
        Assert.Equal("Be brief.", snapshot.GetOrCreate<AIProfileMetadata>().SystemMessage);
    }

    [Fact]
    public void Compare_ListsEverySettingGroupedLikeTheEditor()
    {
        var then = NewProfile();
        var now = NewProfile();
        now.AlterSettings<AIProfileGuardrailSettings>(settings =>
        {
            settings.Topics = [AIGuardrailTopics.Violence, AIGuardrailTopics.Hate];
            settings.PlaceholderReply = "Let's keep to your order.";
        });
        now.AlterSettings<AIProfileSpeakingStyleSettings>(settings => settings.Styles = [AISpeakingStyles.AIDisclosure]);
        now.Put(new AIProfileMetadata { SystemMessage = "Be brief.", Temperature = 0.2f });

        var titles = new Dictionary<string, string>
        {
            [AIGuardrailTopics.Violence] = "Violence and weapons",
            [AISpeakingStyles.AIDisclosure] = "AI disclosure",
        };

        var rows = AIProfileVersionComparer.Compare(then, now, titles);

        // Unchanged settings are listed too, so a compare shows the whole profile.
        var title = Assert.Single(rows, row => row.Label == "Title");
        Assert.Equal(AIProfileVersionComparer.General, title.Section);
        Assert.False(title.Changed);

        var privacy = Assert.Single(rows, row => row.Label == "Data Privacy › Retention days");
        Assert.Equal(AIProfileVersionComparer.DataProcessing, privacy.Section);
        Assert.Equal("30", privacy.Then);
        Assert.False(privacy.Changed);

        var topics = Assert.Single(rows, row => row.Label == "Guardrail › Topics");
        Assert.Equal(AIProfileVersionComparer.PromptSecurity, topics.Section);
        Assert.True(topics.Changed);
        Assert.Null(topics.Then);
        Assert.Equal("Violence and weapons (guardrail-topic-violence), guardrail-topic-hate", topics.Now);

        var styles = Assert.Single(rows, row => row.Label == "Speaking Style › Styles");
        Assert.Equal(AIProfileVersionComparer.Instructions, styles.Section);
        Assert.Equal("AI disclosure (speaking-style-ai-disclosure)", styles.Now);

        var systemMessage = Assert.Single(rows, row => row.Label == "System message");
        Assert.Equal(AIProfileVersionComparer.Instructions, systemMessage.Section);
        Assert.Equal("Be careful.", systemMessage.Then);
        Assert.Equal("Be brief.", systemMessage.Now);

        Assert.Equal(AIProfileVersionComparer.Parameters, Assert.Single(rows, row => row.Label == "Temperature").Section);
        Assert.Equal(AIProfileVersionComparer.Capabilities, Assert.Single(rows, row => row.Label == "AI Tool Instance › Tool instance names").Section);

        // What identifies the profile, or changes on every save, is left out.
        Assert.DoesNotContain(rows, row => row.Label is "Item id" or "Owner id" or "Created utc" || row.Label.StartsWith("Version", StringComparison.Ordinal));

        // Rows follow the sections.
        var order = rows.Select(row => AIProfileVersionComparer.Sections.ToList().IndexOf(row.Section)).ToList();
        Assert.Equal(order.OrderBy(index => index), order);
    }

    [Theory]
    [InlineData("AIProfileGuardrailSettings", "Guardrail")]
    [InlineData("FunctionInvocationMetadata", "Function Invocation")]
    [InlineData("PromptSecurityProfileSettings", "Prompt Security")]
    [InlineData("AIProfileDataExtractionSettings", "Data Extraction")]
    public void Compare_NamesGroupsAfterTheirSettings(string key, string expected)
    {
        Assert.Equal(expected, AIProfileVersionComparer.GroupOf(key));
    }

    private static AIProfile NewProfile()
    {
        var profile = new AIProfile
        {
            ItemId = "p1",
            Name = "support",
            DisplayText = "Support",
            OwnerId = "u1",
            ChatDeploymentName = "gpt-4.1-mini",
        };

        profile.Put(new AIProfileMetadata { SystemMessage = "Be careful.", Temperature = 0.7f });
        profile.Put(new AIProfileVersionMetadata { Version = 3 });
        profile.Put(new AIToolInstanceMetadata { ToolInstanceNames = ["OrderStatus"] });
        profile.AlterSettings<AIProfileDataPrivacySettings>(settings => settings.RetentionDays = 30);

        return profile;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class InMemoryVersionStore : IAIProfileVersionStore
    {
        public List<AIProfileVersion> Versions { get; } = [];

        public Task SaveAsync(AIProfileVersion version, CancellationToken cancellationToken = default)
        {
            Versions.Add(version);

            return Task.CompletedTask;
        }

        public Task<AIProfileVersion> FindByIdAsync(string itemId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Versions.FirstOrDefault(version => version.ItemId == itemId));
        }

        public Task<int> GetLatestVersionAsync(string profileId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Versions.Where(version => version.ProfileId == profileId).Select(version => version.Version).DefaultIfEmpty().Max());
        }

        public Task<(IReadOnlyList<AIProfileVersion> Versions, int Count)> PageAsync(string profileId, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<(IReadOnlyList<AIProfileVersion>, int)>((Versions, Versions.Count));
        }
    }
}
