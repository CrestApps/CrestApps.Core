namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// The JSON schema properties most PDF tools share, written once so every tool describes them alike.
/// </summary>
internal static class PdfToolSchemas
{
    /// <summary>
    /// The <c>pdf</c> property: which PDF the call works on.
    /// </summary>
    public const string Pdf = """
        "pdf": {
          "type": "string",
          "description": "Which PDF: the name of a working PDF (as get_pdf_info lists it), or an uploaded PDF's file name or document id. Omit to use the active working PDF, or the only PDF in the conversation."
        }
        """;

    /// <summary>
    /// The <c>pages</c> property: a page selection.
    /// </summary>
    public const string Pages = """
        "pages": {
          "type": "string",
          "description": "Optional page selection, one-based: \"3\", \"1-3,5\", \"7-\" (to the end), \"last\", \"odd\", \"even\" or \"all\". Omit for every page."
        }
        """;

    /// <summary>
    /// The <c>save_as</c> property: the working PDF an edit is saved as.
    /// </summary>
    public const string SaveAs = """
        "save_as": {
          "type": "string",
          "description": "Optional name for the resulting working PDF. By default an edit of an upload is saved as a working copy named after the upload (the upload itself is never changed), and an edit of a working copy is saved over that copy."
        }
        """;

    /// <summary>
    /// The <c>password</c> property: the password that opens a protected PDF.
    /// </summary>
    public const string Password = """
        "password": {
          "type": "string",
          "description": "Optional password for a protected PDF, only when the user supplied one."
        }
        """;
}
