using System.Security.Cryptography;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Shows, sets and removes the editing restrictions of a document: read only, comments only, tracked changes
/// only or filling in forms only, optionally with a password Word asks for to stop them.
/// </summary>
/// <remarks>
/// The restrictions are kept in the document settings, the way Word keeps them. They are not encryption: the
/// file still opens anywhere, and an application that ignores them can change it. The password is never kept or
/// repeated; only its salted hash is written, computed the way Word computes it.
/// </remarks>
internal sealed class ManageWordProtectionTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.ManageWordProtection;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "action": { "type": "string", "enum": ["get", "set", "remove"], "description": "get (default) shows the current restrictions; set applies them; remove takes them off." },
            "restriction": { "type": "string", "enum": ["read_only", "comments", "tracked_changes", "forms"], "description": "For set: what editors may still do — nothing (read_only), add comments, make tracked changes, or fill in form fields. Default read_only." },
            "enforce": { "type": "boolean", "description": "For set: whether Word enforces the restriction. Default true." },
            "password": { "type": "string", "description": "For set: the password Word asks for to stop the protection. Only the user's own words; never invent one. Optional." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManageWordProtectionTool"/> class.
    /// </summary>
    public ManageWordProtectionTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Shows ('get'), sets ('set') or removes ('remove') the editing restrictions Word enforces on a document: 'restriction' read_only, comments (only comments can be added), tracked_changes (every edit is tracked) or forms (only form fields can be filled in), with an optional 'password' Word asks for to stop the protection. Protection is not encryption: the file still opens, and other applications may ignore it. The password is never shown again; tell the user to keep it.";

    /// <summary>
    /// Runs the protection action.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var action = (arguments.GetString("action") ?? "get").Trim().ToLowerInvariant();

        if (action == "get")
        {
            var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);

            using var package = await context.OpenAsync(source, cancellationToken);

            return $"{source.Describe()}: {Describe(package.MainPart.DocumentSettingsPart?.Settings)}";
        }

        if (action is not ("set" or "remove"))
        {
            throw new WordToolException("'action' must be get, set or remove.");
        }

        var (summary, document) = await context.EditAsync(arguments.Document(), action == "set" ? "Protected the document" : "Removed the document protection", edit =>
        {
            var settings = edit.Package.GetOrCreateSettings();

            if (action == "remove")
            {
                if (settings.GetFirstChild<DocumentProtection>() is null)
                {
                    edit.Changed = false;

                    return Task.FromResult("The document has no editing restrictions; nothing changed.");
                }

                WordSchemaOrder.Remove<DocumentProtection>(settings);

                return Task.FromResult("The editing restrictions are removed; anyone can edit the document.");
            }

            var restriction = (arguments.GetString("restriction") ?? "read_only").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            var (mode, label) = restriction switch
            {
                "read_only" or "readonly" or "none" => (DocumentProtectionValues.ReadOnly, "read only"),
                "comments" => (DocumentProtectionValues.Comments, "only comments can be added"),
                "tracked_changes" or "track_changes" or "revisions" => (DocumentProtectionValues.TrackedChanges, "every edit is a tracked change"),
                "forms" or "filling_in_forms" => (DocumentProtectionValues.Forms, "only form fields can be filled in"),
                _ => throw new WordToolException("'restriction' must be read_only, comments, tracked_changes or forms."),
            };

            var enforce = arguments.GetBoolean("enforce") ?? true;
            var protection = new DocumentProtection
            {
                Edit = mode,
                Enforcement = enforce,
            };

            var password = arguments.GetRawString("password");
            var notes = new List<string>();

            if (!string.IsNullOrEmpty(password))
            {
                var salt = RandomNumberGenerator.GetBytes(16);

                // The attributes Word itself writes: SHA-512 through the Windows crypto provider types, 100,000 rounds.
                protection.CryptographicProviderType = CryptProviderValues.RsaAdvancedEncryptionStandard;
                protection.CryptographicAlgorithmClass = CryptAlgorithmClassValues.Hash;
                protection.CryptographicAlgorithmType = CryptAlgorithmValues.TypeAny;
                protection.CryptographicAlgorithmSid = WordProtectionHash.Sha512AlgorithmSid;
                protection.CryptographicSpinCount = WordProtectionHash.SpinCount;
                protection.Hash = Convert.ToBase64String(WordProtectionHash.Hash(password, salt));
                protection.Salt = Convert.ToBase64String(salt);

                notes.Add("Word asks for the password to stop the protection; it is not stored and cannot be shown again, so the user must keep it");

                if (password.Length > WordProtectionHash.MaxPasswordLength)
                {
                    notes.Add($"Word only checks the first {WordProtectionHash.MaxPasswordLength} characters of a protection password");
                }
            }

            WordSchemaOrder.Set(settings, protection);

            if (enforce && mode == DocumentProtectionValues.TrackedChanges)
            {
                // Word turns tracking on with this restriction, so edits made here are recorded too.
                WordSchemaOrder.Set(settings, new TrackRevisions());
                notes.Add("change tracking is on");
            }

            var answer = enforce
                ? $"The document is protected: {label}{(string.IsNullOrEmpty(password) ? ", without a password (anyone can stop the protection in Word)" : ", with a password")}."
                : $"The restriction ({label}) is saved but not enforced.";

            if (notes.Count > 0)
            {
                answer += " " + string.Join("; ", notes) + ".";
            }

            return Task.FromResult(answer + " Protection is not encryption: other applications may ignore it.");
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}";
    }

    private static string Describe(Settings settings)
    {
        var protection = settings?.GetFirstChild<DocumentProtection>();
        var recommendation = settings?.GetFirstChild<WriteProtection>() is not null
            ? " Word also recommends opening it read-only."
            : string.Empty;

        if (protection is null)
        {
            return "no editing restrictions." + recommendation;
        }

        var edit = protection.Edit?.Value;
        var label = edit == DocumentProtectionValues.ReadOnly ? "read only"
            : edit == DocumentProtectionValues.Comments ? "only comments can be added"
            : edit == DocumentProtectionValues.TrackedChanges ? "every edit is a tracked change"
            : edit == DocumentProtectionValues.Forms ? "only form fields can be filled in"
            : "no editing restriction";

        // Word enforces a restriction whose enforcement is not written down.
        var enforced = protection.Enforcement?.Value ?? true;
        var password = protection.Hash?.HasValue == true || protection.HashValue?.HasValue == true ? "with a password" : "without a password";

        return $"{label}, {(enforced ? "enforced" : "not enforced")}, {password}.{(protection.Formatting?.Value == true ? " Formatting is limited to the allowed styles." : string.Empty)}{recommendation}";
    }
}
