## Git Commit Rules

- Never add Claude as a commit author or co-author.
- Never add `Co-authored-by: Claude` or any Claude-related co-author trailer to commits.
- Commits must use only the configured human Git author.

## Package Rules

- Never update `PDFsharp-MigraDoc` past 6.2.4 in `Directory.Packages.props`. 6.2.4 is the last MIT-licensed release; 6.5.0+ is AGPL-3.0-only and throws `PdfSharpLicenseException` at runtime without a purchased license.
