using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Encrypts a PDF with a password to open it, permissions that limit what readers may do, or both.
/// </summary>
internal sealed class ProtectPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ProtectPdf;

    private const string PasswordCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#$%&*+-=?@";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.SaveAs}},
            "password": {
              "type": "string",
              "description": "Only when the PDF is already protected: its current owner password, as the user supplied it."
            },
            "user_password": { "type": "string", "description": "The password needed to open the PDF, as the user chose it. Omit to let anyone open it." },
            "owner_password": { "type": "string", "description": "The password that lifts the permissions, as the user chose it. When permissions are restricted without one, a random one is generated and not kept." },
            "permissions": {
              "type": "object",
              "description": "What readers without the owner password may do. Anything not given is allowed.",
              "properties": {
                "print": { "type": "boolean" },
                "high_quality_print": { "type": "boolean" },
                "modify": { "type": "boolean" },
                "copy": { "type": "boolean" },
                "annotate": { "type": "boolean" },
                "fill_forms": { "type": "boolean" },
                "assemble": { "type": "boolean", "description": "Insert, rotate or delete pages and create bookmarks." }
              },
              "additionalProperties": false
            },
            "encryption": { "type": "string", "enum": ["aes256", "aes128", "rc4_128"], "description": "Defaults to aes256. Use aes128 or rc4_128 only for very old readers." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private static readonly (string Key, string Label)[] _permissions =
    [
        ("print", "printing"),
        ("high_quality_print", "high-quality printing"),
        ("modify", "changing the document"),
        ("copy", "copying text and images"),
        ("annotate", "adding comments"),
        ("fill_forms", "filling in forms"),
        ("assemble", "inserting, rotating or deleting pages"),
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtectPdfTool"/> class.
    /// </summary>
    public ProtectPdfTool()
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
    public override string Description => "Password-protects a PDF: a user password to open it and/or permissions (print, copy, modify, annotate, fill forms, assemble) enforced with an owner password, encrypted with AES-256 by default. Only use passwords the user chose, and never repeat them back. Saves a protected working copy; do every other edit first, because later edits of the protected copy need the owner password.";

    /// <summary>
    /// Protects the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var userPassword = arguments.GetString("user_password");
        var ownerPassword = arguments.GetString("owner_password");
        var permissions = ReadPermissions(arguments);
        var restricted = permissions.Values.Any(allowed => !allowed);
        var encryption = (arguments.GetString("encryption") ?? "aes256").Trim().ToLowerInvariant().Replace("-", string.Empty, StringComparison.Ordinal);

        if (userPassword is null && ownerPassword is null && !restricted)
        {
            throw new PdfToolException("Say how to protect the PDF: 'user_password' to require a password to open it, and/or 'permissions' to restrict printing, copying or changes.");
        }

        if (encryption is not ("aes256" or "aes128" or "rc4_128" or "rc4128"))
        {
            throw new PdfToolException($"'{encryption}' is not an encryption method. Use aes256 (the default), aes128 or rc4_128.");
        }

        var generated = restricted && ownerPassword is null;

        if (generated)
        {
            ownerPassword = RandomNumberGenerator.GetString(PasswordCharacters, 32);
        }

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);
            var wasProtected = PdfProtection.IsEncrypted(bytes, arguments.GetString("password"));

            using var document = PdfFiles.OpenForEditing(bytes, arguments.GetString("password"));

            var signatures = PdfObjects.DescribeBrokenSignatures(document);
            var sync = PdfMetadataSync.Capture(document);

            // PDFsharp drops the protection a file had when it opens it; this makes sure nothing of the old
            // passwords carries over whatever it does.
            document.SecurityHandler.SetEncryptionToNoneAndResetPasswords();

            var method = encryption switch
            {
                "aes128" => "AES-128",
                "rc4_128" or "rc4128" => "RC4 128-bit",
                _ => "AES-256",
            };

            switch (method)
            {
                case "AES-128":
                    document.SecurityHandler.SetEncryptionToV4UsingAES();

                    break;
                case "RC4 128-bit":
                    document.SecurityHandler.SetEncryptionToV2With128Bits();

                    break;
                default:
                    document.SecurityHandler.SetEncryptionToV5();

                    break;
            }

            var settings = document.SecuritySettings;

            if (userPassword is not null)
            {
                settings.UserPassword = userPassword;
            }

            if (ownerPassword is not null)
            {
                settings.OwnerPassword = ownerPassword;
            }

            settings.PermitPrint = permissions["print"];
            settings.PermitFullQualityPrint = permissions["print"] && permissions["high_quality_print"];
            settings.PermitModifyDocument = permissions["modify"];
            settings.PermitExtractContent = permissions["copy"];
            settings.PermitAnnotations = permissions["annotate"];
            settings.PermitFormsFill = permissions["fill_forms"];
            settings.PermitAssembleDocument = permissions["assemble"];

            var saved = sync.Save(document, context.TimeProvider.GetUtcNow());
            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, $"Protected with {method}", cancellationToken);
            var answer = new StringBuilder();

            answer.AppendLine(PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine($"It is encrypted with {method}{(wasProtected ? ", replacing the protection it had" : string.Empty)}.");
            answer.AppendLine(userPassword is null
                ? "Opening: no password is needed."
                : "Opening: the user password the user chose is needed.");

            var denied = _permissions.Where(entry => !permissions[entry.Key] || (entry.Key == "high_quality_print" && !permissions["print"])).Select(entry => entry.Label).ToList();

            answer.AppendLine(denied.Count == 0
                ? "Permissions: readers may do everything."
                : "Not allowed without the owner password: " + string.Join(", ", denied) + ".");

            if (generated)
            {
                answer.AppendLine("No owner password was given, so a random one was generated and not kept: nobody can lift these permissions or edit this copy further. To change them, protect the unprotected original again.");
            }
            else if (restricted && string.Equals(userPassword, ownerPassword, StringComparison.Ordinal))
            {
                answer.AppendLine("The user and owner passwords are the same, so anyone who can open the file also has full permissions; use a different owner password for the restrictions to hold.");
            }

            answer.AppendLine("The working copy is now protected: later edits need the owner password (as 'password'), so make any other changes before protecting. Use export_pdf to give it to the user. The permissions are honoured by standard viewers, not enforced against every program.");

            PdfPropertiesToolText.AppendNotes(answer, sync.DescribeConformance(), signatures);

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }

    private static Dictionary<string, bool> ReadPermissions(PdfToolArguments arguments)
    {
        var permissions = _permissions.ToDictionary(entry => entry.Key, _ => true, StringComparer.Ordinal);

        if (!arguments.TryGetElement("permissions", out var element))
        {
            return permissions;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new PdfToolException("'permissions' must be an object such as {\"print\": true, \"copy\": false, \"modify\": false}.");
        }

        foreach (var property in element.EnumerateObject())
        {
            var key = property.Name.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

            if (!permissions.ContainsKey(key))
            {
                throw new PdfToolException($"'{property.Name}' is not a permission. Use print, high_quality_print, modify, copy, annotate, fill_forms or assemble.");
            }

            permissions[key] = property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.Value.GetString(), out var parsed) => parsed,
                _ => throw new PdfToolException($"Permission '{property.Name}' must be true or false."),
            };
        }

        return permissions;
    }
}
