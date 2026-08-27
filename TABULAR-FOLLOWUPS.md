# Tabular workspace — known follow-ups

Notes left after the multi-worksheet / column-typing work. Nothing here is a regression; these are
things observed while testing that workbook against the live agent.

## 1. The model wastes a tool call on `PRAGMA table_info` every time

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

## 2. Embedded subtotal rows are indistinguishable from data

Sheets exported from Excel often carry inline rollup rows. In the revenue workbook, `Client
Breakdown` has 7 of them (`True Blue Total`, `Henderson Total`, … `RDI Total`) with a blank `Site`,
and `Overall Projections` has a `Totals:` row.

They import as ordinary rows, so a naive aggregate double- or triple-counts:

```
SELECT SUM(Total_Revenue) FROM "..._Client_Breakdown"   -> 20,880,998
Actual total                                            ->  6,960,333
```

The model has so far spotted them unprompted and excluded them, so this is a latent trap rather than
an active bug. Detecting them reliably needs a heuristic over data shape (blank key column plus a
label ending in "Total", or a value equal to the sum of preceding rows), which is a design decision
that was deliberately kept out of the typing change.

## 3. Populated columns with no header are dropped

Column count comes from the header row, so cells past the last header are discarded. In `Client
Breakdown` that silently loses 44 populated cells in column K — sub-queue labels ("Imaging", "Food
and Nutrition") and a per-campaign commentary column.

This also makes some rows unidentifiable: `Milford / Eli Lilly` appears twice and is only
distinguishable by that dropped column, where the two rows read "Eli Lilly Affordability" and
"Eli Lilly Direct".

The obvious fix — size the table from the worksheet `<dimension>` — does not work: this workbook
declares `A1:Y86` while real data ends at K, so it would create 14 empty columns. Sizing from a row
sample is also inconsistent, since a wider row further down the sheet would still be truncated.
Needs its own design.
