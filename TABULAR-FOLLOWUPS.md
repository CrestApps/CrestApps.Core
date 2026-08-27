# Tabular workspace — known follow-ups

Notes left after the multi-worksheet / column-typing work and the follow-up header-detection /
subtotal / unlabeled-column pass. Nothing here is a regression; these are things observed while
testing real workbooks against the live agent.

## 1. The model wastes a tool call on `PRAGMA table_info` every time — STILL OPEN

**What happens.** Ask the tabular agent to describe a table and it reliably tries:

```sql
PRAGMA table_info("<table>");
```

and gets back `The 'PRAGMA' keyword is not permitted.` It then reconstructs the schema the long way
from `sqlite_master` and `_workspace_meta`. Every schema question therefore costs at least one dead
call, and on multi-part prompts this contributed to the agent hitting its tool-call iteration cap
before finishing the answer.

**Where it is enforced.** `TabularSqlGuard.ForbiddenKeywordsRegex()` in
`src/Primitives/CrestApps.Core.AI.Documents/Tabular/TabularSqlGuard.cs`:

```csharp
[GeneratedRegex(@"\b(ATTACH|DETACH|PRAGMA|VACUUM|load_extension)\b", ...)]
```

`PRAGMA` is blocked wholesale alongside genuinely dangerous statements.

**Two ways to fix it, pick one.**

- Allow read-only introspection: permit `PRAGMA table_info` / `pragma_table_info()` while keeping
  every other pragma blocked. Note that some pragmas do mutate state, so this must be an allow-list
  of specific pragmas, not a general `PRAGMA` unblock.
- Cheaper and lower risk: leave the guard alone and tell the model not to try. `ListTabularDataTool`
  already returns every column with its storage type, so the description or the tabular agent prompt
  just needs to say schema comes from `list_tabular_data` and that `PRAGMA` is unavailable.

The second option is the smaller change and removes the wasted call outright.

## 2. Embedded subtotal rows — RESOLVED (with one edge)

Rows that look like inline rollups (a total-style label such as `Totals:` or `Waco Total` alongside
at least one numeric value) are now flagged. When any are detected in a table, an `is_subtotal`
column is added (`1` = rollup, `0` = data). `TabularWorksheetShaper.IsSubtotalRow` holds the
heuristic, and the agent prompt instructs the model to add `WHERE is_subtotal = 0` to aggregates.
Rows are kept, not dropped, so a heuristic misfire never loses data.

**Edge left:** detection runs over the profile buffer (the first `HeaderScanRows + TypeSampleRowCount`
rows of each worksheet). A subtotal that appears only *after* that window in a table that had none
before it will not add/flag the column. The interspersed per-group totals in the test workbooks fall
inside the window; a lone grand-total far down a long sheet could be missed. A full-sheet second pass
would close this if it proves to matter.

## 3. Populated columns with no header — RESOLVED

Cells that extend past the last header are no longer dropped. `TabularWorksheetShaper.ExpandHeader`
widens the header to the widest sampled data row and `BuildColumns` names the extra columns
`column_N`, so their data is imported and queryable. (Column width is still measured from the row
sample, not the sheet `<dimension>`, which over-declares `A1:Y86` in these workbooks; a row wider than
every sampled row could still be truncated.)

## 4. Header not on the first row — RESOLVED

`TabularWorksheetShaper.DetectHeaderRowIndex` scans the first rows of each worksheet and picks the row
with the most textual labels, skipping title/banner rows above it (for example the date-band title
row on `Projections - By Client`). The blank-leading-row case was already handled by the reader.

## 5. Dates and hidden sheets — RESOLVED

Excel date serials on date/time-formatted cells are converted to ISO strings during read
(`OpenXmlTabularWorksheetReader.TryConvertExcelDate`). Hidden and very-hidden worksheets are skipped
by default rather than imported as opaque extra tables.
