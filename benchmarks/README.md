# CrestApps.Core Benchmarks

The benchmark project measures allocation-sensitive services in the primitive projects.

Run all benchmarks from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*'
```

## Primitive optimization results

Measurements were captured with BenchmarkDotNet 0.15.8 on .NET 10.0.5 using an Apple M2.
The benchmark suite uses the short-run job to keep routine validation practical, so small timing
differences should be treated as directional. Allocation differences are the primary acceptance
criterion.

| Scenario | Baseline | Optimized | Change |
| --- | ---: | ---: | ---: |
| Template normalization, 100 lines | 4.722 us / 29.94 KB | 3.492 us / 4.14 KB | 26.0% faster / 86.2% fewer allocations |
| Template normalization, 1,000 lines | 48.599 us / 300.99 KB | 35.797 us / 43.16 KB | 26.3% faster / 85.7% fewer allocations |
| Seekable PDF ingestion, 10 MB image payload | 28.62 MB allocated | 18.62 MB allocated | 34.9% fewer allocations |
| Non-seekable PDF ingestion, 10 MB image payload | 50.49 MB allocated | 50.49 MB allocated | Safe buffering retained |

The PDF payload benchmark embeds a valid JPEG payload before the PDF cross-reference table.
Seekable streams are read directly. Non-seekable streams continue to be copied asynchronously so
cancellation remains available and the parser receives the seekable input it requires. PDF timing
is intentionally omitted because the short-run results were too variable; the allocation reduction
is deterministic and equals the removed input-sized copy.

The Word and PDF generation benchmarks showed only small percentage allocation changes for
highly compressible text. The writers still avoid `MemoryStream.ToArray()` because that removes an
entire output-sized duplicate buffer with a simple implementation, and the benefit scales with
larger or less-compressible generated files. No writer timing improvement is claimed because the
short-run results were too variable.

## Second primitive optimization pass

| Scenario | Baseline | Optimized | Change |
| --- | ---: | ---: | ---: |
| AI function integer argument | 104.21 ns / 32 B | 64.01 ns / 0 B | 38.6% faster / allocation-free |
| AI function object argument | 294.36 ns / 168 B | 222.37 ns / 64 B | 24.5% faster / 61.9% fewer allocations |
| AI function array argument | 391.59 ns / 360 B | 364.23 ns / 272 B | 7.0% faster / 24.4% fewer allocations |
| MCP resource merge, 100 entries per source | 76.393 us / 21.6 KB | 8.128 us / 20.12 KB | 89.4% faster / 6.9% fewer allocations |
| MCP resource merge, 1,000 entries per source | 10,136.739 us / 211.45 KB | 135.405 us / 190.17 KB | 98.7% faster / 10.1% fewer allocations |
| PostgreSQL short filter tokenization | 335.2 ns / 1,000 B | 173.1 ns / 200 B | 48.4% faster / 80.0% fewer allocations |
| PostgreSQL nested filter tokenization | 1,842.5 ns / 4,136 B | 687.9 ns / 808 B | 62.7% faster / 80.5% fewer allocations |
| PostgreSQL long filter tokenization | 11,023.1 ns / 42,136 B | 11,222.8 ns / 8,872 B | 78.9% fewer allocations; timing neutral |
| Template discovery, 1,000 files and 20 parsers | 9,228.67 KB allocated | 8,579.59 KB allocated | 7.0% fewer allocations |

Template discovery timing remains file-system bound and was too variable to claim a latency
improvement. The immutable extension lookup was retained because it removes parser-count-dependent
work, preserves first-registration precedence, and keeps the implementation straightforward.

The second-pass figures compare the legacy and optimized implementations in the same benchmark
process. PostgreSQL tokenization timings were noisy for the long expression, but the optimized path
consistently removes the regular-expression match-object allocations.

## Third primitive optimization pass

| Scenario | Baseline | Optimized | Change |
| --- | ---: | ---: | ---: |
| MCP prompt merge, 100 entries per source | 69.708 us / 19.91 KB | 4.644 us / 18.36 KB | 93.3% faster / 7.8% fewer allocations |
| MCP prompt merge, 1,000 entries per source | 8,139.844 us / 195.7 KB | 52.040 us / 174.35 KB | 99.4% faster / 10.9% fewer allocations |
| Bounded document context, 1 MB input / 50 KB output | 331.00 us / 2,152.48 KB | 43.19 us / 198.53 KB | 87.0% faster / 90.8% fewer allocations |
| Cached catalog page, 50 of 10,000 entries | 3,275.50 ns / 80,576 B | 63.85 ns / 496 B | 98.1% faster / 99.4% fewer allocations |

The prompt merge preserves catalog, provider, and SDK precedence, keeps ordinal case-sensitive name
matching, and retains duplicate catalog entries. A hash set replaces only the repeated linear scans
used when provider and SDK prompts are added.

Bounded document context formatting still uses stable chunk ordering and returns the exact same
prefix and truncation marker. It computes the joined length first and writes only the requested
content prefix instead of materializing the entire document before truncation.

Unfiltered catalog paging now slices the cached array directly. Filtered, sorted, source-aware, and
custom query contexts retain the extensible filtering pipeline while avoiding a second full-list
materialization.

## Fourth primitive optimization pass

| Scenario | Baseline | Optimized | Change |
| --- | ---: | ---: | ---: |
| Data extraction matching, 10 configured entries/results | 7.214 us / 15.05 KB | 3.679 us / 6.51 KB | 49.0% faster / 56.7% fewer allocations |
| Data extraction matching, 100 configured entries/results | 736.239 us / 1,534.98 KB | 36.276 us / 55.94 KB | 95.1% faster / 96.4% fewer allocations |

The data-extraction benchmark compares the legacy repeated scans with the current implementation in
the same process. The optimized path builds a per-call index that preserves direct, normalized, and
semantic matching precedence as well as first-configured-entry precedence, without caching mutable
profile settings globally.

## A2A tool registry optimization

| Scenario | Baseline | Optimized | Change |
| --- | ---: | ---: | ---: |
| 100 connections, 1,000 valid skill names | 67.78 us / 362.38 KB | 52.77 us / 269.41 KB | 22.1% faster / 25.7% fewer allocations |
| 100 connections, 20% invalid skill names | 84.26 us / 362.38 KB | 62.35 us / 278.63 KB | 26.0% faster / 23.1% fewer allocations |
| 1,000 connections, 10,000 valid skill names | 3,740.86 us / 3,794.73 KB | 2,257.29 us / 2,858.02 KB | 39.7% faster / 24.7% fewer allocations |
| 1,000 connections, 20% invalid skill names | 3,731.52 us / 3,794.74 KB | 2,426.43 us / 2,951.60 KB | 35.0% faster / 22.2% fewer allocations |

The A2A benchmark compares the legacy and production paths in the same process with in-memory
connections and cached agent cards. Valid names now return unchanged without allocating a character
array or replacement string. Invalid names preserve the same Unicode-aware replacement behavior
while creating only the resulting string.

## Azure AI Search document ID filter preparation

| Document IDs | Legacy | Current | Change |
| --- | ---: | ---: | ---: |
| 1 | 105.77 ns / 344 B | 42.41 ns / 176 B | 59.9% faster / 48.8% fewer allocations |
| 10 | 435.78 ns / 1,776 B | 259.91 ns / 792 B | 40.4% faster / 55.4% fewer allocations |
| 100 | 3,136.09 ns / 16,336 B | 2,438.25 ns / 7,288 B | 22.3% faster / 55.4% fewer allocations |
| 1,000 | 29,538.15 ns / 163,760 B | 24,660.78 ns / 74,072 B | 16.5% faster / 54.8% fewer allocations |

The current path preserves identifier order and identity, filters only null and empty identifiers,
and emits the same apostrophe-escaped OData filter text. It sizes and fills the final filter string
directly, avoiding the per-identifier projection and interpolation strings used by the legacy path.

## Open XML spreadsheet row extraction

| Scenario | Legacy | Current | Change |
| --- | ---: | ---: | ---: |
| Sparse 1,000 x 34 sheet | 6.810 ms / 4.81 MB | 6.218 ms / 3.72 MB | 8.7% faster / 22.7% fewer allocations |
| Dense 10,000 x 16 sheet | 514.012 ms / 163.21 MB | 473.676 ms / 160.08 MB | 7.8% faster / 1.9% fewer allocations |

The benchmark compares the legacy per-row list allocation with the current per-read reusable list in
the same process. It uses synthetic in-memory Open XML workbooks, five warmups, and twelve measured
iterations, so workbook generation and disk or network I/O are excluded. The current implementation
preserves SDK traversal, worksheet and row ordering, sparse column positions, trailing-empty-cell
trimming, and exact tab-separated output.

## Sentence boundary detection

| Scenario | Legacy | Current | Change |
| --- | ---: | ---: | ---: |
| Abbreviation | 25.63 ns / 32 B | 19.75 ns / 0 B | 22.9% faster / allocation-free |
| Regular sentence | 21.02 ns / 40 B | 18.87 ns / 0 B | 10.2% faster / allocation-free |
| Streamed sequence | 134.81 ns / 136 B | 91.04 ns / 0 B | 32.5% faster / allocation-free |
| Mixed-case abbreviation | 28.08 ns / 32 B | 18.09 ns / 0 B | 35.6% faster / allocation-free |
| Long input | 21.25 ns / 32 B | 16.24 ns / 0 B | 23.6% faster / allocation-free |

The current path uses the existing ordinal-ignore-case abbreviation set's span lookup, avoiding the
final-word string allocation. It preserves literal-space word splitting, trailing space/tab/carriage
return trimming, wrapper handling, and the existing hard, soft, and force-flush boundary precedence.
These figures use the medium-run job because the individual operations are short; the allocation
elimination is the primary acceptance criterion.

## JSON code-fence extraction

| Scenario | Legacy regex | Ordinal scanner | Change |
| --- | ---: | ---: | ---: |
| Short fenced JSON | 2.67 us / 464 B | 46.95 ns / 80 B | 98.2% faster / 82.8% fewer allocations |
| 10 KB prose with fence | 1.98 us / 432 B | 584.38 ns / 48 B | 70.5% faster / 88.9% fewer allocations |
| 100 KB JSON payload | 6.53 ms / 205,277 B | 58.52 us / 204,898 B | 99.1% faster / 379 B fewer |
| 10 KB input without a fence | 994.32 ns / 0 B | 1.36 us / 0 B | Allocation-free in both paths; timing was noisy |
| Multiple fences | 1.01 us / 432 B | 26.31 ns / 48 B | 97.4% faster / 88.9% fewer allocations |
| Whitespace-heavy content | 26.79 us / 432 B | 8.47 us / 48 B | 68.4% faster / 88.9% fewer allocations |

The scanner preserves the legacy regular expression's exact extraction behavior: it selects the first
non-overlapping triple-backtick pair, removes only an immediately adjacent lowercase `json` prefix
without requiring a label boundary, trims Unicode whitespace, and treats every other language label
as content. It also intentionally closes on the first subsequent triple-backtick sequence even when
that sequence is inside the content, and a run of four or more opening backticks starts a match at the
first three. Compatibility tests cover direct edge cases and a 5,040-input differential matrix.

These figures use five warmups and twelve measured iterations on the environment described above.
Timing variance was high for some scenarios, so the latency figures are directional and the allocation
reductions are the primary acceptance criterion. The large payload's returned string dominates its
allocation total; the scanner still removes the regular-expression match overhead.

## Default tool registry dependency expansion

| Graph shape | Scale | Legacy | Current | Change |
| --- | ---: | ---: | ---: | ---: |
| Fan-out | 100 | 23.01 us / 54.06 KB | 16.45 us / 32.38 KB | 28.5% faster / 40.1% fewer allocations |
| Fan-out | 1,000 | 265.21 us / 519.45 KB | 224.62 us / 313.02 KB | 15.3% faster / 39.7% fewer allocations |
| Deep chain | 100 | 20.31 us / 57.73 KB | 16.02 us / 36.11 KB | 21.1% faster / 37.5% fewer allocations |
| Deep chain | 1,000 | 251.78 us / 558.27 KB | 196.98 us / 351.90 KB | 21.8% faster / 37.0% fewer allocations |
| Diamond/shared | 100 | 23.26 us / 56.48 KB | 17.78 us / 34.86 KB | 23.6% faster / 38.3% fewer allocations |
| Diamond/shared | 1,000 | 283.47 us / 519.29 KB | 229.81 us / 338.93 KB | 18.9% faster / 34.7% fewer allocations |
| Many roots/shared dependencies | 100 | 69.16 us / 52.10 KB | 54.94 us / 31.05 KB | 20.6% faster / 40.4% fewer allocations |
| Many roots/shared dependencies | 1,000 | 665.63 us / 481.38 KB | 564.77 us / 274.45 KB | 15.2% faster / 43.0% fewer allocations |

The current path builds the case-insensitive name index directly and pre-sizes resolved-entry
collections instead of materializing `GroupBy` groupings and then copying each grouping into another
list. It preserves provider and source order, depth-first pre-order expansion, all distinct entries in
duplicate-name groups, first-entry precedence for duplicate identifiers, missing-dependency behavior,
cycle handling, and the exact dependency-name side effect on `AICompletionContext`.

Dependency-name memoization was intentionally not added. Reusing a name-level visited set can change
the order of re-entrant duplicate-name groups, while the existing resolved-entry identifier set already
prevents repeated expansion of each concrete entry. Search ranking also remains unchanged: replacing
the explicit scored list with a direct ordered projection saved only 1.2-1.8% of allocations and
produced neutral or noisy timings, so that experiment was reverted.

## MCP capability resolution

| Raw capabilities | Legacy | Current | Change |
| ---: | ---: | ---: | ---: |
| 100 | 310.90 us / 294.52 KB | 244.55 us / 241.94 KB | 21.3% faster / 17.9% fewer allocations |
| 1,000 | 3,073.17 us / 2,847.51 KB | 2,397.33 us / 2,259.13 KB | 22.0% faster / 20.7% fewer allocations |
| 10,000 | 35,543.44 us / 29,063.70 KB | 32,050.24 us / 22,342.07 KB | 9.8% faster / 23.1% fewer allocations |

These top-5 figures compare the captured legacy implementation and production resolver in the same
process using the production Lucene tokenizer, eight MCP servers, mixed tools, prompts, resources, and
resource templates, duplicate identities and names, equal-score ties, zero-score entries, and five
warmups with twelve measured iterations. Top-3 results showed the same allocation profile and similar
timing direction.

The current path builds the exact `name[: uri][: description]` tokenization text without a temporary
parts list, pre-sizes per-resolution collections, and reuses tokens only for ordinally identical text
within the current resolution call. It does not cache mutable or server-specific capabilities globally.
Matching still uses distinct tokenizer terms, the same forward/reverse score maximum, inclusive
thresholds, case-insensitive `connection + NUL + capability name` identities, strict higher-score
replacement, embedding-first precedence on equal scores, descending score sorting, and the existing
top-K truncation.

With precomputed tokens isolating merge and ranking, the local token dictionary was 8.7% and 5.5%
slower at 100 and 1,000 entries while still reducing allocations by 11.7% and 13.0%; the production
Lucene path above is the relevant retained result. A bounded top-K selection was not introduced because
exact equivalence with the existing equal-score `List.Sort` behavior could not be proven.

## JSON node raw-value conversion

| Scenario | Legacy LINQ | Current loops | Change |
| --- | ---: | ---: | ---: |
| Flat object, 256 fields | 9.639 us / 17.20 KB | 8.785 us / 17.14 KB | 8.9% faster / allocations effectively unchanged |
| Mixed 1,000-node tree | 31.409 us / 91.23 KB | 22.907 us / 74.88 KB | 27.1% faster / 17.9% fewer allocations |
| Large array, 10,000 entries | 195.390 us / 359.48 KB | 181.545 us / 359.43 KB | 7.1% faster / allocations effectively unchanged |
| Nested objects, 128 levels | 19.438 us / 60.27 KB | 15.808 us / 53.21 KB | 18.7% faster / 11.7% fewer allocations |
| Fallback-heavy values | 39.222 us / 38.76 KB | 37.514 us / 38.70 KB | 4.4% faster / allocations effectively unchanged |
| Catalog payload | 35.067 us / 93.42 KB | 28.280 us / 79.31 KB | 19.4% faster / 15.1% fewer allocations |
| AI configuration payload | 15.118 us / 42.81 KB | 11.602 us / 35.92 KB | 23.3% faster / 16.1% fewer allocations |

The benchmark compares the captured recursive LINQ implementation with production in the same process.
The pre-change control run produced identical allocations and timings within noise. The retained path
pre-sizes and fills dictionaries and lists directly; the material gains are concentrated in realistic
container-heavy catalog, configuration, mixed-tree, and deeply nested payloads.

Conversion semantics remain unchanged. Objects return insertion-ordered
`Dictionary<string, object>` instances using `StringComparer.OrdinalIgnoreCase`, and case-only duplicate
keys still throw instead of overwriting. Arrays return `List<object>` instances. Scalar precedence remains
string, `long`, `double`, `bool`, and `DateTime`, followed by the exact `ToJsonString()` fallback. This
preserves the existing string fallbacks for directly created `int`, `float`, `decimal`, `DateTimeOffset`,
and custom values, as well as null handling, recursive shape, and detached mutable containers.

## MCP server tool merging

| Scenario | Total local + SDK tools | Legacy | Current | Change |
| --- | ---: | ---: | ---: | ---: |
| Non-null empty SDK enumerable | 100 | 49.39 ns / 856 B | 52.59 ns / 856 B | Allocations unchanged; timing neutral |
| No duplicates | 100 | 7.499 us / 5.58 KB | 1.115 us / 5.09 KB | 85.1% faster / 8.7% fewer allocations |
| Duplicate-heavy overlap | 100 | 2.903 us / 5.58 KB | 1.057 us / 5.09 KB | 63.6% faster / 8.7% fewer allocations |
| SDK-internal duplicates | 100 | 4.763 us / 5.58 KB | 1.111 us / 5.09 KB | 76.7% faster / 8.7% fewer allocations |
| Case-only names | 100 | 7.082 us / 5.58 KB | 1.072 us / 5.09 KB | 84.9% faster / 8.7% fewer allocations |
| Non-null empty SDK enumerable | 1,000 | 376.95 ns / 7.87 KB | 374.05 ns / 7.87 KB | Allocations unchanged; timing neutral |
| No duplicates | 1,000 | 966.871 us / 54.80 KB | 13.413 us / 43.65 KB | 98.6% faster / 20.3% fewer allocations |
| Duplicate-heavy overlap | 1,000 | 257.952 us / 54.80 KB | 14.301 us / 43.65 KB | 94.5% faster / 20.3% fewer allocations |
| SDK-internal duplicates | 1,000 | 400.238 us / 54.80 KB | 13.546 us / 43.65 KB | 96.6% faster / 20.3% fewer allocations |
| Case-only names | 1,000 | 891.076 us / 54.80 KB | 13.959 us / 43.65 KB | 98.4% faster / 20.3% fewer allocations |
| Non-null empty SDK enumerable | 10,000 | 3.125 us / 78.18 KB | 3.134 us / 78.18 KB | Allocations unchanged; timing neutral |
| No duplicates | 10,000 | 99,851.372 us / 546.98 KB | 236.593 us / 468.80 KB | 99.8% faster / 14.3% fewer allocations |
| Duplicate-heavy overlap | 10,000 | 32,737.206 us / 546.98 KB | 211.864 us / 468.79 KB | 99.4% faster / 14.3% fewer allocations |
| SDK-internal duplicates | 10,000 | 62,750.924 us / 546.98 KB | 179.649 us / 468.76 KB | 99.7% faster / 14.3% fewer allocations |
| Case-only names | 10,000 | 94,293.520 us / 546.98 KB | 228.918 us / 468.79 KB | 99.8% faster / 14.3% fewer allocations |

These same-process measurements isolate the merge from dependency injection and network work. The empty-SDK
scenario uses the stated number of already-created local protocol tools and a non-null empty SDK enumerable.
The current path performs the same local-list copy as legacy, with identical allocations at all three scales;
the small timing differences are noise at sub-microsecond and low-microsecond durations. It tests `MoveNext()`
before allocating the name set, so this path does not allocate a `HashSet`.

The remaining scenarios split each total evenly between local and SDK tools. Duplicate-heavy overlap covers
three quarters of SDK names, SDK-internal names repeat four times, and case-only names differ only by casing.
For a non-empty SDK enumerable, the retained path seeds a `StringComparer.Ordinal` hash set from the local
list without modifying it. Local precedence, duplicate local names, metadata, and order remain unchanged.
The SDK enumerator is obtained and disposed once; tools are appended in encounter order, exact duplicates
against local or earlier SDK tools are skipped, and case-only variants remain distinct. Null SDK enumeration,
hidden-tool filtering, keyed-service failure logging and skipping, cancellation behavior, exceptions, and
the identity of appended SDK `ProtocolTool` instances remain unchanged.

## YesSql ordering experiments

### Extracted-data index mapping

| Extracted fields | Legacy double sort | Current single sort | Change |
| ---: | ---: | ---: | ---: |
| 10 | 2.546 us / 7.05 KB | 1.898 us / 6.52 KB | 25.5% faster / 7.5% fewer allocations |
| 100 | 24.712 us / 63.48 KB | 18.749 us / 59.78 KB | 24.1% faster / 5.8% fewer allocations |
| 1,000 | 369.804 us / 627.77 KB | 264.194 us / 592.43 KB | 28.6% faster / 5.6% fewer allocations |

The retained mapping sorts field names once with `StringComparer.OrdinalIgnoreCase` and uses each exact
key to enumerate its values. The benchmark includes multiple values, duplicate values, empty strings,
and case-only field-name variants. Exact equivalence checks and behavior tests preserve stable ordering
for case-insensitive ties, field-name and value-text formatting, per-field value order, empty output,
metadata, configured collection names, and the existing null failure.

### Rejected store query ordering

| Store and seeded records | Materialize then order | YesSql query ordering | Result |
| --- | ---: | ---: | ---: |
| Completion usage, 100 | 159.9 us / 173.92 KB | 190.5 us / 177.11 KB | 19.1% slower / 1.8% more allocations |
| Completion usage, 1,000 | 1,411.1 us / 1,619.46 KB | 1,653.6 us / 1,607.77 KB | 17.2% slower / 0.7% fewer allocations |
| Completion usage, 10,000 | 20,898.4 us / 15,968.69 KB | 23,864.7 us / 15,809.18 KB | 14.2% slower / 1.0% fewer allocations |
| Chat-session events, 100 | 140.8 us / 151.57 KB | 152.4 us / 155.40 KB | 8.2% slower / 2.5% more allocations |
| Chat-session events, 1,000 | 1,167.3 us / 1,363.37 KB | 1,312.0 us / 1,356.13 KB | 12.4% slower / 0.5% fewer allocations |
| Chat-session events, 10,000 | 16,259.5 us / 13,477.42 KB | 18,428.8 us / 13,359.62 KB | 13.3% slower / 0.9% fewer allocations |

The SQLite benchmarks use isolated local YesSql databases, persisted documents and map indexes, realistic
date/profile filters, and groups of equal timestamps. Query-side candidates order by the mapped timestamp
and then by map-index identifier; setup verifies the exact returned identifier sequence against the stable
materialize-then-LINQ behavior, and both paths return their already-materialized lists without benchmark-only
row copies. `AICompletionUsageIndex.CreatedUtc` and
`AIChatSessionMetricsIndex.SessionStartedUtc` are mapped schema columns, but translated ordering was
consistently slower and its allocation changes were marginal, so both production experiments were rejected.

The prompt-store experiment was rejected before database benchmarking. `AIChatSessionPromptIndex` and its
deployed schema expose only item identifier, session identifier, and role, not `CreatedUtc`; YesSql's typed
ordering expression targets the mapped index. Adding and migrating an index column solely for this campaign
would introduce backward-compatibility work without evidence of compelling value, so no schema change or
unfaithful benchmark substitute was added.

## Tabular batch splitting

| Data rows | Batch size | Max rows | Legacy | Current | Change |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1,000 | 25 | Disabled | 13.57 us / 103.58 KB | 13.53 us / 91.16 KB | 0.3% faster / 12.0% fewer allocations |
| 1,000 | 100 | 500 | 11.85 us / 94.63 KB | 11.94 us / 82.16 KB | Timing neutral / 13.2% fewer allocations |
| 10,000 | 25 | Disabled | 164.14 us / 1,038.78 KB | 163.79 us / 918.05 KB | Timing neutral / 11.6% fewer allocations |
| 10,000 | 100 | 500 | 134.41 us / 868.07 KB | 127.34 us / 785.29 KB | 5.3% faster / 9.5% fewer allocations |
| 100,000 | 25 | Disabled | 5,736.69 us / 11,079.56 KB | 5,352.33 us / 9,890.21 KB | 6.7% faster / 10.7% fewer allocations |
| 100,000 | 100 | 500 | 6,004.98 us / 9,305.97 KB | 5,304.76 us / 8,519.96 KB | 11.7% faster / 8.4% fewer allocations |

The benchmark uses synthetic in-memory content and performs no AI or network work. The current path indexes
the array returned by `Split('\n', StringSplitOptions.None)` directly, pre-sizes the batch collections, and
materializes each mutable row list once. It preserves the legacy behavior for null, empty, and whitespace
content; header-only input; LF and CRLF text; retained carriage returns; blank rows and trailing newlines;
the 25-row fallback for non-positive batch sizes; disabled non-positive maximums; positive maximum-row
truncation; repeated headers; one-based row indexes; final partial batches; and exact LF-based `GetContent()`
output. The public maximum-row option is an `int`, so a null maximum is not representable.

## AI document indexing materialization experiment

| Chunks | Legacy `ToList` + `Select` | Direct output array | Count-aware chunk array |
| ---: | ---: | ---: | ---: |
| 10 | 2.035 us / 10.31 KB | 2.137 us / 10.18 KB | 2.033 us / 10.11 KB |
| 100 | 20.475 us / 100.80 KB | 21.563 us / 100.67 KB | 16.521 us / 100.67 KB |
| 1,000 | 207.765 us / 1,005.73 KB | 189.214 us / 1,005.59 KB | 291.994 us / 1,006.30 KB |
| 10,000 | 9,024.704 us / 10,055.12 KB | 6,253.021 us / 10,054.98 KB | 6,636.340 us / 10,062.72 KB |

Both candidates were rejected. The measured allocation changes were negligible, and timing varied
inconsistently by scale. Production remains unchanged.

## Extracted-data snapshot recording

| Source fields | Density | Legacy LINQ | Current retained-count loop | Change |
| ---: | --- | ---: | ---: | ---: |
| 1,000 | Dense | 43.971 us / 178.30 KB | 34.328 us / 108.66 KB | 21.9% faster / 39.1% fewer allocations |
| 1,000 | 1% retained | 3.877 us / 2.11 KB | 3.206 us / 1.45 KB | 17.3% faster / 31.1% fewer allocations |
| 1,000 | All empty | 3.322 us / 200 B | 1.372 us / 0 B | 58.7% faster / allocation-free |
| 10,000 | Dense | 1.374 ms / 1,701.64 KB | 834.245 us / 1,057.92 KB | 39.3% faster / 37.8% fewer allocations |
| 10,000 | 1% retained | 37.410 us / 18.13 KB | 33.302 us / 11.11 KB | 11.0% faster / 38.7% fewer allocations |
| 10,000 | All empty | 31.933 us / 200 B | 13.623 us / 0 B | 57.3% faster / allocation-free |

The same-process benchmark uses a fixed clock and call-counting in-memory stores so it isolates
snapshot construction and verifies the save-versus-delete result. Dense maps retain every field,
mostly-empty maps retain one field in 100, and all-empty maps exercise deletion without constructing
a snapshot. Keys use descending insertion order and mixed casing, while retained lists include ordered
values and sparse null items.

The current path first counts retained fields, then lazily creates the ordinal-ignore-case destination
dictionary when the second pass reaches the first retained list. Capacity is based on the retained
count, not the source map count, and every retained list is still copied with `ToList()`. The extra
read-only pass is accepted because dense maps keep substantial timing and allocation gains, the
10,000-field mostly-empty case remains 11.0% faster with 38.7% fewer allocations, and all-empty maps
avoid the destination dictionary entirely while more than halving elapsed time. Short-run timings are
directional, but none of the measured sparse or delete scenarios regressed.

Legacy observable behavior remains unchanged: empty dictionaries and dictionaries containing only
empty value lists delete once, while null source dictionaries, field states, or value lists retain
their existing failures. Empty lists are omitted, null list items are retained, case-only duplicate
keys still throw, source key and per-field value order remain stable, and the saved dictionary and
lists are detached snapshots. Metadata, timestamps, cancellation, and store exception propagation
are also preserved.

## AI data source indexing queue normalization experiment

| Input IDs | Distribution | Current `Where` + `Distinct` + `ToArray` | One-pass hash set | Change |
| ---: | --- | ---: | ---: | ---: |
| 10 | Unique | 808.7 ns / 1,032 B | 817.5 ns / 920 B | 1.1% slower / 10.9% fewer allocations |
| 10 | 50% duplicates | 1.843 us / 600 B | 588.5 ns / 488 B | 68.1% faster / 18.7% fewer allocations |
| 10 | 90% invalid/whitespace | 425.8 ns / 376 B | 323.9 ns / 264 B | 23.9% faster / 29.8% fewer allocations |
| 10 | Case-only duplicates | 698.9 ns / 600 B | 602.3 ns / 488 B | 13.8% faster / 18.7% fewer allocations |
| 100 | Unique | 4.116 us / 8,368 B | 4.020 us / 8,256 B | 2.3% faster / 1.3% fewer allocations |
| 100 | 50% duplicates | 3.410 us / 3,976 B | 3.423 us / 3,864 B | Timing neutral / 2.8% fewer allocations |
| 100 | 90% invalid/whitespace | 1.106 us / 1,032 B | 1.226 us / 920 B | 10.8% slower / 10.9% fewer allocations |
| 100 | Case-only duplicates | 3.435 us / 3,976 B | 3.597 us / 3,864 B | 4.7% slower / 2.8% fewer allocations |
| 1,000 | Unique | 34.828 us / 81,344 B | 38.018 us / 81,232 B | 9.2% slower / 0.1% fewer allocations |
| 1,000 | 50% duplicates | 29.755 us / 38,672 B | 33.889 us / 38,560 B | 13.9% slower / 0.3% fewer allocations |
| 1,000 | 90% invalid/whitespace | 6.970 us / 8,368 B | 11.649 us / 8,256 B | 67.1% slower / 1.3% fewer allocations |
| 1,000 | Case-only duplicates | 29.182 us / 38,672 B | 33.771 us / 38,560 B | 15.7% slower / 0.3% fewer allocations |
| 10,000 | Unique | 569.586 us / 753,314 B | 453.730 us / 753,202 B | 20.3% faster / less than 0.1% fewer allocations |
| 10,000 | 50% duplicates | 442.081 us / 362,829 B | 321.131 us / 362,717 B | 27.4% faster / less than 0.1% fewer allocations |
| 10,000 | 90% invalid/whitespace | 94.182 us / 81,344 B | 50.232 us / 81,232 B | 46.7% faster / 0.1% fewer allocations |
| 10,000 | Case-only duplicates | 349.228 us / 362,829 B | 283.816 us / 362,717 B | 18.7% faster / less than 0.1% fewer allocations |

The benchmark uses 64 invocations per iteration, five warmups, and twelve measured iterations on
.NET 10.0.5 and an Apple M2. It includes validation, normalization, work-item construction, and an
available in-memory unbounded channel write with the production channel options. Channels are drained
outside measurements; no consumer, store, or network work is included. Setup verifies exact operation,
target, first-occurrence casing, and identifier order against the captured production expression.

The one-pass candidate specializes array inputs and falls back to direct collection enumeration. It
removes a fixed 112 bytes, but results are mixed at 100 IDs and every 1,000-ID case regresses by
9.2-67.1%. The 10- and 10,000-ID timings include high-variance cases and their apparent gains do not
offset the stable mid-scale regressions or negligible large-input allocation change.

Production is unchanged: `AIDataSourceIndexingQueue` retains its existing LINQ normalization pipelines,
and no helper consolidation, normalized wrapper, marker, or caller change was retained.

## Speech text sanitization

| Scenario | Legacy regex pipeline | Current hybrid | Change |
| --- | ---: | ---: | ---: |
| Plain text, 87 bytes | 522.6 ns / 200 B | 204.9 ns / 0 B | 60.8% faster / allocation-free |
| Chat chunk, 200 bytes | 1.066 us / 976 B | 975.1 ns / 976 B | 8.5% faster / allocations unchanged |
| Mixed markdown, 2 KB | 10.455 us / 21,053 B | 10.243 us / 19,235 B | 2.0% faster / 8.6% fewer allocations |
| Transcript, 20 KB | 101.199 us / 39,793 B | 71.093 us / 39,784 B | 29.7% faster / allocations effectively unchanged |
| Code-heavy, 20 KB | 21.969 us / 29,575 B | 21.355 us / 29,573 B | Timing and allocations effectively unchanged |
| Emoji-heavy, 33,920 UTF-16 code units | 239.644 us / 174,216 B | 104.668 us / 39,704 B | 56.3% faster / 77.2% fewer allocations |
| Whitespace-heavy, 17,280 UTF-16 code units | 149.160 us / 28,215 B | 61.875 us / 14,104 B | 58.5% faster / 50.0% fewer allocations |

The benchmark uses five warmups and twelve measured iterations on .NET 10.0.5 and an Apple M2.
The pre-change control run compared two copies of the original source-generated regex pipeline:
allocations were identical in every scenario and timings remained within 3%, confirming that the
captured legacy path is a faithful baseline.

The retained implementation is a conservative hybrid. Text that cannot contain any markdown match
bypasses the markdown expressions, while a direct pass preserves the legacy removal of every asterisk
and underscore. A final exact-size pass combines removal of every UTF-16 high/low surrogate pair, the
configured BMP symbol ranges, .NET whitespace collapsing, and trimming. The broader fenced-code,
inline-code, image, link, heading, horizontal-rule, and list expressions remain source generated and
in their original order.

A single hand-written markdown parser was rejected because the compatibility contract includes
malformed and nested-looking delimiters, multiline `\s` behavior, incomplete fences and inline code,
and ordering cases where a first pass intentionally exposes a marker for a later call. Direct tests,
a 48,000-input interaction matrix, and all 65,536 BMP code units in prose and ordered-list contexts
verify the current output against the captured legacy helper. Blank inputs still return the original
reference, all supplementary pairs still disappear even when they are not emoji, and the documented
legacy repeated-call behavior remains unchanged.

## Prompt security input normalization

| Scenario | Legacy three-stage pipeline | Current fused pipeline | Change |
| --- | ---: | ---: | ---: |
| Benign ASCII, 256 B | 1.515 us / 3.92 KB | 1.490 us / 1.73 KB | Timing neutral / 55.9% fewer allocations |
| Benign ASCII, 2 KB | 10.954 us / 24.40 KB | 11.788 us / 12.23 KB | 7.6% slower / 49.9% fewer allocations |
| Benign ASCII, 8 KB | 64.529 us / 112.42 KB | 57.500 us / 48.23 KB | 10.9% faster / 57.1% fewer allocations |
| Whitespace-heavy, 8,192 UTF-16 code units | 49.478 us / 90.54 KB | 41.711 us / 45.30 KB | 15.7% faster / 50.0% fewer allocations |
| Invisible-heavy, 8,192 UTF-16 code units | 48.600 us / 80.77 KB | 47.281 us / 41.98 KB | 2.7% faster / 48.0% fewer allocations |
| Homoglyph-heavy, 8,192 UTF-16 code units | 58.174 us / 96.40 KB | 56.541 us / 48.23 KB | 2.8% faster / 50.0% fewer allocations |
| Mixed obfuscated injection, 2,048 UTF-16 code units | 13.025 us / 28.14 KB | 9.914 us / 16.12 KB | 23.9% faster / 42.7% fewer allocations |
| Unicode and surrogate-heavy, 8,192 UTF-16 code units | 68.045 us / 102.02 KB | 69.109 us / 58.66 KB | Timing neutral / 42.5% fewer allocations |

The current path retains Form KC normalization as the first stage and preserves the exact configured
Unicode categories and homoglyph map. One pre-sized builder now performs invisible-character removal
and whitespace collapsing in removal-first order, including the legacy leading/trailing run counts.
Homoglyph folding writes its fixed-length result directly while recording the same replacement count.

The benchmark setup verifies every context and telemetry field plus observable string-reference
relationships against the captured legacy implementation. Tests additionally cover all 43 configured
homoglyph entries, malformed and valid surrogate cases, every BMP whitespace, format, control, and code
unit behavior, a 23,040-input interaction matrix, and 131,072 BMP-context differential inputs. The
timings use five warmups and twelve measured iterations; allocation reductions are deterministic, while
the small benign and Unicode timing differences should be treated as neutral.

## Output security local scanning

| Scenario | Legacy at `333798a` | Current | Change |
| --- | ---: | ---: | ---: |
| Benign output, 256 B | 1.361 us / 72 B | 743.00 ns / 72 B | 45.4% faster |
| Benign output, 2 KB | 11.282 us / 72 B | 5.797 us / 72 B | 48.6% faster |
| Benign output, 20 KB | 104.892 us / 72 B | 56.023 us / 72 B | 46.6% faster |
| Benign output, 20 KB + unique 8 KB system prompt | 273.913 us / 19,232 B | 208.459 us / 72 B | 23.9% faster / 99.6% fewer allocations |
| System-prompt leak | 124.51 ns / 616 B | 90.49 ns / 248 B | 27.3% faster / 59.7% fewer allocations |
| Disclosure indicator | 604.06 ns / 248 B | 415.45 ns / 248 B | 31.2% faster |
| Tool schema disclosure | 113.91 ns / 248 B | 112.47 ns / 248 B | Timing neutral |
| Tool definition pattern | 204.27 ns / 248 B | 206.18 ns / 248 B | Timing neutral |
| Sensitive-data SSN | 488.75 ns / 248 B | 472.71 ns / 248 B | 3.3% faster |
| Sensitive-data credit card | 714.00 ns / 248 B | 693.38 ns / 248 B | 2.9% faster |
| Unsafe output content | 339.63 ns / 248 B | 328.75 ns / 248 B | 3.2% faster |
| Mixed findings | 685.90 ns / 5,176 B | 445.65 ns / 352 B | 35.0% faster / 93.2% fewer allocations |
| Benign output + 96 repeated system-prompt lines | 297.465 us / 20,832 B | 227.190 us / 72 B | 23.6% faster / 99.7% fewer allocations |

The retained implementation enumerates and trims system-prompt lines with spans instead of materializing
the `Split` array and substrings. It preserves LF splitting, CRLF trimming, the inclusive 50-character
threshold, and ordinal-ignore-case matching. Exact preconditions avoid invoking the tool-definition regex
when no literal `{` exists and the unsafe-output regex when none of `<`, `:`, `=`, or `(` exists; every
possible match requires those literals. No prompt or output content is cached.

The captured legacy/current setup covers 256 B, 2 KB, and 20 KB benign outputs, a 20 KB benign output with
an 8 KB system prompt, every finding and sensitive-data reason variant, mixed findings, and repeated prompt
lines. Global setup verifies every observable result field before measurement. The tests add 149 cases and
more than 271,000 generated legacy/current comparisons, including every BMP code unit in system-line
trimming/case positions and SSN/card digit positions, all output options, ordering, duplicates, line
endings, audit behavior, cancellation, and propagated errors.

Two exact-result candidates were rejected:

- Counting nine Unicode decimal digits before running the sensitive-data regexes slowed benign 256 B,
  2 KB, 20 KB, and 20 KB + 8 KB-system-prompt cases by 9.5%, 3.5%, 12.5%, and 6.1%, respectively, and
  slowed the SSN and card cases by 5.1% and 4.1%.
- Suppressing consecutive duplicate substantial system-prompt line scans made the repeated-line case
  73.7% faster than the retained implementation, but slowed the more representative unique 8 KB system
  prompt by 7.7% and mixed findings by 6.1%, while adding per-line comparison state.

Run the comparison with:

```bash
dotnet run -c Release -f net10.0 --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj -- --filter '*DefaultOutputSecurityFilterBenchmarks*'
```

## RAG document text joining experiment

The parser-free benchmark compares the current production baseline,
`string.Join("\n", document.EnumerateContent().Select(...).Where(...))`, with a materialized-list
candidate and a manual `StringBuilder` candidate. Global setup verifies ordinal output equivalence
for every element count and content scenario, including null, empty, and whitespace-only values.

Both candidates were rejected. Materializing a list stores every retained string reference solely
for the join. The manual builder improved some large-input timings but allocated substantially more
memory and added complexity, so it did not provide a balanced replacement. Production remains
unchanged and continues to use the literal LF separator with the maintainable LINQ projection; this
experiment does not introduce platform-specific separators or additional whitespace normalization.

## Post-session processing helper optimization

Run the complete helper comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*PostSession*'
```

The measurements below use BenchmarkDotNet 0.15.8, five warmups, twelve measured iterations,
.NET 10.0.5, and an Apple M2.

### Message text projection

| Messages | Legacy repeated `ChatMessage.Text` reads | Current single read | Change |
| ---: | ---: | ---: | ---: |
| 10 | 432.9 ns / 1,344 B | 178.9 ns / 352 B | 58.7% faster / 73.8% fewer allocations |
| 100 | 4,302.0 ns / 13,080 B | 1,743.8 ns / 3,160 B | 59.5% faster / 75.8% fewer allocations |
| 1,000 | 43,665.4 ns / 131,880 B | 17,559.4 ns / 31,960 B | 59.8% faster / 75.8% fewer allocations |

The single-read implementation was retained. Microsoft.Extensions.AI 10.7.0 defines
`ChatMessage.Text` as the ordered concatenation of every `TextContent` item in `Contents`; non-text
items are ignored. Differential coverage includes a null message, null or empty contents,
text-free mixed contents, whitespace-only text, multiple text fragments, embedded newlines, and
direct text. The public processing test also parses JSON split across text items with null and
binary content interleaved.

### Task-result summary builder experiment

| Results | Retained LINQ + `string.Join` | Capped builder candidate | Change |
| ---: | ---: | ---: | ---: |
| 10 | 341.8 ns / 1.59 KB | 162.6 ns / 1.71 KB | 52.4% faster / 8% more allocations |
| 100 | 3,034.5 ns / 15.32 KB | 1,270.7 ns / 14.67 KB | 58.1% faster / 4.2% fewer allocations |
| 1,000 | 33,983.0 ns / 153.66 KB | 12,663.6 ns / 145.78 KB | 62.7% faster / 5.1% fewer allocations |

The builder candidate preserved null and empty handling, exact names, separators, newlines,
whitespace-based value detection, and one source enumeration. It was rejected because the common
small-result case allocates more, while the larger measured counts are atypical for configured
post-session tasks. The extra capacity heuristic and loop do not provide a material application-level
gain for a diagnostic formatter, so production retains the simpler LINQ implementation.

### Prompt projection experiment

| Prompts | Legacy | Unchanged current | Direct loop | Pre-sized direct loop |
| ---: | ---: | ---: | ---: | ---: |
| 10 | 330.5 ns / 2.19 KB | 324.6 ns / 2.19 KB | 321.8 ns / 2.13 KB | 308.2 ns / 2.09 KB |
| 100 | 3,424.7 ns / 21.26 KB | 3,461.3 ns / 21.26 KB | 3,532.4 ns / 22.57 KB | 3,425.2 ns / 21.27 KB |
| 1,000 | 34,876.4 ns / 214.26 KB | 34,970.3 ns / 214.26 KB | 37,347.3 ns / 223.61 KB | 36,363.7 ns / 215.27 KB |

Production remains unchanged. The legacy and current methods are intentionally identical controls,
so their timing differences are noise and allocations match exactly. Neither loop candidate produces
a consistent material latency gain across scales; the direct loop also allocates 6.2% more at 100
prompts and 4.4% more at 1,000. The existing filter, role mapping, trimming, dictionary shape, and
ordering are therefore retained.

### Response log preview

Run the response preview comparison with:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*PostSessionResponseLogPreviewBenchmarks*' --join
```

Each response contains the stated number of ASCII bytes. The newline scenarios use LF every 64
characters, CRLF every 64 characters, or isolated CR and LF code units near the response end for
512-byte inputs and around the 2,000-character preview boundary for larger inputs.

| Response | Pattern | Legacy full normalization | Current bounded preview | Change |
| ---: | --- | ---: | ---: | ---: |
| 512 B | Frequent CRLF | 280.70 ns / 2,144 B | 281.08 ns / 2,144 B | Neutral |
| 512 B | Frequent LF | 164.72 ns / 1,064 B | 166.32 ns / 1,064 B | Neutral |
| 512 B | Newlines near boundary | 160.05 ns / 2,104 B | 160.02 ns / 2,104 B | Neutral |
| 512 B | No newlines | 53.23 ns / 0 B | 53.98 ns / 0 B | Neutral |
| 2 KB | Frequent CRLF | 1,563.86 ns / 16,488 B | 828.91 ns / 4,032 B | 47.0% faster / 75.5% fewer allocations |
| 2 KB | Frequent LF | 1,052.35 ns / 12,240 B | 534.66 ns / 4,032 B | 49.2% faster / 67.1% fewer allocations |
| 2 KB | Newlines near boundary | 898.59 ns / 16,304 B | 390.14 ns / 4,032 B | 56.6% faster / 75.3% fewer allocations |
| 2 KB | No newlines | 562.67 ns / 8,056 B | 284.86 ns / 4,032 B | 49.4% faster / 50.0% fewer allocations |
| 20 KB | Frequent CRLF | 11,760.69 ns / 91,944 B | 827.25 ns / 4,032 B | 93.0% faster / 95.6% fewer allocations |
| 20 KB | Frequent LF | 6,865.07 ns / 49,680 B | 535.11 ns / 4,032 B | 92.2% faster / 91.9% fewer allocations |
| 20 KB | Newlines near boundary | 4,376.19 ns / 90,032 B | 391.41 ns / 4,032 B | 91.1% faster / 95.5% fewer allocations |
| 20 KB | No newlines | 2,060.56 ns / 8,056 B | 283.76 ns / 4,032 B | 86.2% faster / 50.0% fewer allocations |
| 1 MB | Frequent CRLF | 767,522.59 ns / 4,301,213 B | 825.83 ns / 4,032 B | 99.9% faster / 99.9% fewer allocations |
| 1 MB | Frequent LF | 408,427.10 ns / 2,138,330 B | 534.98 ns / 4,032 B | 99.9% faster / 99.8% fewer allocations |
| 1 MB | Newlines near boundary | 367,588.10 ns / 4,202,758 B | 395.25 ns / 4,032 B | 99.9% faster / 99.9% fewer allocations |
| 1 MB | No newlines | 86,992.14 ns / 8,056 B | 285.55 ns / 4,032 B | 99.7% faster / 50.0% fewer allocations |

Responses of 1,000 characters or fewer retain the runtime replacement path because their maximum
escaped length is already bounded to 2,000 characters; this keeps the 512-byte cases neutral.
Larger responses count or search only the code units needed to decide truncation, then fill the final
string directly. The implementation never constructs the complete escaped text when it would be
truncated.

The output remains exactly equivalent to ordinal carriage-return replacement followed by ordinal
line-feed replacement and then a UTF-16 code-unit slice. CR and LF are escaped independently, an
escape sequence or surrogate pair may still be split at code unit 2,000, and the ellipsis is appended
only when the fully escaped text exceeds that boundary. Behavior tests cover null and empty values,
all boundary lengths, CR/LF/CRLF ordering, expansion across the boundary, Unicode, lone surrogates,
surrogate splitting, exact ordinal prefixes, and a 1 MB response.

## Data extraction local formatting and projection

Run the independent name-merging and prompt-projection comparisons from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*DataExtractionMergeNamePartsBenchmarks*' --join

dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*DataExtractionPromptProjectionBenchmarks*' --join
```

The measurements use BenchmarkDotNet 0.15.8, five warmups, twelve measured iterations,
.NET 10.0.5, and an Apple M2.

### Name-part merging

Each benchmark invocation performs 1,000 merges; BenchmarkDotNet reports per-merge values.

| Name shape | Legacy Split + projection | Retained in-place Split | Manual span experiment | Retained change |
| --- | ---: | ---: | ---: | ---: |
| Short realistic | 75.89 ns / 344 B | 40.14 ns / 168 B | 27.32 ns / 168 B | 47.1% faster / 51.2% fewer allocations |
| Punctuation-heavy | 98.70 ns / 496 B | 63.21 ns / 304 B | 43.10 ns / 264 B | 36.0% faster / 38.7% fewer allocations |
| Unicode | 79.91 ns / 312 B | 41.90 ns / 136 B | 30.57 ns / 128 B | 47.6% faster / 56.4% fewer allocations |
| Long multi-part | 233.21 ns / 1,352 B | 178.17 ns / 808 B | 132.00 ns / 480 B | 23.6% faster / 40.2% fewer allocations |

The retained implementation keeps `String.Split` with its literal-space separator,
`RemoveEmptyEntries`, and `TrimEntries`, but replaces the first or last element in the resulting
array before the existing `string.Join`. This removes the second LINQ/collection-expression array
while keeping the method small.

The manual scanner was rejected even though it was faster in isolation. It reimplemented literal
separator discovery, Unicode trimming, empty-part removal, and first/last-part buffering for a method
called only when semantic name aliases are applied. The additional tokenizer-sized compatibility
surface was not justified after the simple retained change already removed 38.7-56.4% of allocations
and improved timing by 23.6-47.6%.

Exact behavior is protected by explicit null, empty, whitespace, separator, casing, punctuation,
Unicode, multiple-whitespace, and delimiter cases plus an 880-combination differential matrix across
every extraction field kind.

### Prompt argument projection

| Entries | State | Legacy | Unchanged current |
| ---: | --- | ---: | ---: |
| 10 | Sparse | 226.6 ns / 1.20 KB | 216.2 ns / 1.20 KB |
| 10 | Dense | 301.7 ns / 1.58 KB | 300.5 ns / 1.58 KB |
| 100 | Sparse | 1,121.3 ns / 5.80 KB | 1,114.2 ns / 5.80 KB |
| 100 | Dense | 1,758.3 ns / 8.88 KB | 1,787.0 ns / 8.88 KB |
| 1,000 | Sparse | 10,838.2 ns / 51.42 KB | 10,871.6 ns / 51.42 KB |
| 1,000 | Dense | 16,696.1 ns / 81.93 KB | 16,693.1 ns / 81.93 KB |

Legacy and current are intentionally identical controls. Allocations match exactly and the small
timing differences are noise, so production keeps the existing anonymous projections, dictionary
shape and comparer, key order, source order, duplicate fields, current-state filtering, and
per-call mutable profile/session reads.

The field-only `List.ConvertAll` experiment reached 205.8 ns / 1.13 KB at 10 sparse entries but only
16,440.0 ns / 81.86 KB at 1,000 dense entries, making its large-scale allocation change negligible.
The single-pass state loop reached 15,892.7 ns at 1,000 dense entries but increased allocation from
81.93 KB to 91.03 KB. Full pre-sizing reached 15,348.5 ns / 82.69 KB for 1,000 dense entries but
increased 1,000-sparse allocation from 51.42 KB to 58.28 KB. Counting first regressed to
12,431.5 ns for 1,000 sparse entries and 18,770.3 ns for 1,000 dense entries. All prompt projection
experiments were therefore rejected as marginal, density-dependent, slower, or allocation-negative.

Tests capture exact serialized anonymous-object property order and argument-key order for zero, one,
and many fields; null values; aliases and examples; duplicate names; sparse current state; last-turn
selection and trimming; template identity; cancellation propagation; and template errors.
