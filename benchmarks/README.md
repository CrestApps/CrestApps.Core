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

## Azure AI Search AI data source ReadByIds filter construction

`AzureAISearchAIDataSourceSourceHandler.ReadByIdsAsync` now reuses the shared document ID filter
builder instead of a per-identifier LINQ projection joined with `" or "`. The identifiers below
represent the already whitespace-filtered and case-insensitively de-duplicated array the handler
passes to filter construction, so the measured region isolates the step that changed.

| Document IDs | Legacy allocated | Current allocated | Change |
| --- | ---: | ---: | ---: |
| 1 | 208 B | 96 B | 53.8% fewer allocations |
| 10 | 1,568 B | 640 B | 59.2% fewer allocations |
| 100 | 15,408 B | 6,416 B | 58.4% fewer allocations |
| 1,000 | 155,632 B | 66,000 B | 57.6% fewer allocations |

The current path emits the identical apostrophe-escaped OData filter text, locked by the
characterization test that compares it to the legacy `string.Join` projection across empty,
apostrophe-heavy, ordered, and duplicate identifier sets. It fills the filter string once through the
shared builder rather than allocating a projection string per identifier plus the joined result. The
allocation reduction above is deterministic across runs; wall-clock time was directionally faster in
the low-noise measurements (down to roughly 0.45x at 10 identifiers) but is sensitive to
garbage-collection noise on the development machine, so allocations are the reproducible metric.

## Open XML text property reads

### Word optimization retained

| Paragraphs / runs per paragraph | Repeated reads | Cached read | Change |
| --- | ---: | ---: | ---: |
| 1,000 / 1 | 2.528 ms / 2.45 MB | 2.236 ms / 1.32 MB | Faster; 1.13 MB fewer allocations |
| 1,000 / 8 | 14.739 ms / 14.35 MB | 11.182 ms / 7.18 MB | Faster; 7.17 MB fewer allocations |
| 10,000 / 1 | 27.695 ms / 23.56 MB | 21.137 ms / 12.27 MB | Faster; 11.29 MB fewer allocations |
| 10,000 / 8 | 151.185 ms / 142.51 MB | 118.881 ms / 70.79 MB | Faster; 71.72 MB fewer allocations |

The retained production change reads each Word paragraph's `InnerText` once, then reuses that exact
string for whitespace filtering, paragraph construction, and `Text`. The benchmark uses synthetic
in-memory documents and verifies exact legacy/current document structure, text, markdown, metadata,
ordering, duplicates, and element counts before measurement. Behavior tests additionally cover empty
and whitespace-only paragraphs, control elements, Unicode, large text, cancellation, and invalid
packages. Package creation and disk or network I/O are excluded. All four measured Word cases
improved timing, while allocations fell by roughly half; the allocation reduction is the stronger
evidence because timings remain sensitive to runtime and machine noise.

### PowerPoint candidate rejected

| Slides / text fragments per slide | Repeated reads | Single-read candidate | Result |
| --- | ---: | ---: | ---: |
| 1,000 / 1 | 60.57 ms / 42.71 MB | 53.74 ms / 42.71 MB | Faster timing; allocations unchanged |
| 1,000 / 8 | 104.39 ms / 46.43 MB | 117.81 ms / 46.43 MB | Slower timing; allocations unchanged |
| 10,000 / 1 | 1,107.81 ms / 768.64 MB | 698.94 ms / 768.65 MB | Faster timing; allocations effectively unchanged |
| 10,000 / 8 | 980.95 ms / 805.81 MB | 1,051.42 ms / 805.80 MB | Slower timing; allocations effectively unchanged |

The PowerPoint candidate cached each drawing text element's `Text` value but did not provide a
consistent timing improvement or a meaningful allocation reduction, so production remains unchanged.
Setup verifies exact equivalence among the legacy path, the candidate, and current production before
measurement. Both benchmark classes use five warmups and twelve measured iterations.

Run both comparisons from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*OpenXml*TextPropertyReadBenchmarks*'
```

## Open XML tabular artifact row capacity

### Unconditional 4,096-row capacity rejected

| Data rows | Default capacity | 4,096-row capacity | Allocation change |
| ---: | ---: | ---: | ---: |
| 0 | 809.1 us / 175.41 KB | 786.2 us / 207.43 KB | 18.3% more |
| 1 | 667.6 us / 176.38 KB | 1,271.3 us / 208.35 KB | 18.1% more |
| 32 | 3,678.1 us / 203.45 KB | 1,152.9 us / 234.93 KB | 15.5% more |
| 1,000 | 2,699.2 us / 1,051.19 KB | 16,976.0 us / 1,067.05 KB | 1.5% more |
| 4,096 | 8,367.5 us / 3,760.73 KB | 25,343.2 us / 3,728.49 KB | 0.9% less |
| 10,000 | 17,384.4 us / 9,028.24 KB | 21,391.5 us / 8,996.18 KB | 0.4% less |

Each benchmark parses a prebuilt, in-memory XLSX containing one header and the indicated number of
data rows. Setup verifies exact header, row, cell, and ordering equivalence before measurement.
Timing was noisy and showed no consistent preallocation win. Allocation results were stable:
preallocating 4,096 references added about 32 KB to empty and small artifacts, while saving only about
32 KB at 4,096 and 10,000 rows. Production therefore lets the row list grow normally. Regression
tests cover empty and header-only workbooks, sparse cells, 4,095/4,096/4,097 rows, exact ordering, and
cancellation.

Run the comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*OpenXmlTabularDocumentArtifactBuilderCapacityBenchmarks*'
```

## Open XML spreadsheet row extraction

| Scenario | Legacy | Current row reuse | Shared-string cache candidate |
| --- | ---: | ---: | ---: |
| Sparse 1,000 x 34 sheet | 8.539 ms / 4.81 MB | 7.832 ms / 3.72 MB | 8.021 ms / 3.31 MB |
| Dense 10,000 x 16 sheet | 623.667 ms / 163.21 MB | 481.068 ms / 160.08 MB | 648.009 ms / 138.11 MB |

The benchmark compares the legacy per-row list allocation, the retained per-read reusable list, and a
deep-scan candidate that additionally materializes every shared string once. It uses synthetic
in-memory Open XML workbooks, five warmups, and twelve measured iterations, so workbook generation and
disk or network I/O are excluded. Setup verifies exact output before measurement. The shared-string
cache reduced allocations by 11.0% on the sparse workbook and 13.7% on the dense workbook relative to
current row reuse, but it was 2.4% and 34.7% slower respectively. The candidate was therefore rejected;
production keeps row reuse without eager shared-string materialization.

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

## YesSql extracted-data store update copy

| Source fields | Legacy LINQ `ToDictionary` | Current pre-sized loop | Change |
| ---: | ---: | ---: | ---: |
| 10 | 369.0 ns / 1.13 KB | 312.5 ns / 1.07 KB | 15.3% faster / 5.3% fewer allocations |
| 100 | 3,993.4 ns / 9.42 KB | 3,020.4 ns / 9.37 KB | 24.4% faster / 0.5% fewer allocations |
| 1,000 | 39,499.0 ns / 93.75 KB | 33,729.9 ns / 93.70 KB | 14.6% faster / 0.1% fewer allocations |

This experiment targets the `YesSqlAIChatSessionExtractedDataStore` update path, where an incoming
record's field map is copied into the already-tracked `existing` entity so the persisted snapshot is
detached from caller-owned lists. It is distinct from the recorder's snapshot construction covered under
*Extracted-data snapshot recording*. The benchmark's source dictionary mixes case-only key variants,
populated multi-value lists, empty lists, and null value lists, and `[GlobalSetup]` verifies both copy
implementations return identical keys, ordering, and per-key values.

The retained candidate pre-sizes the destination dictionary to the source field count with
`StringComparer.OrdinalIgnoreCase`, then copies each list with a collection expression over the struct
enumerator. This avoids the LINQ `ToDictionary` boxed `IEnumerable` enumerator, its per-element key and
value delegate invocations, and interface dispatch, giving a consistent 15–24% timing reduction across
sizes and a fixed single-object (~60 B) allocation saving from the eliminated enumerator. The allocation
gain is most visible at 10 fields (5.3%) and negligible at large sizes where the copied value lists
dominate. Both runs reproduced the direction; a noisier first run measured 0.61–0.90 ratios.

Observable behavior is unchanged: non-null source maps produce an ordinal-ignore-case dictionary, a null
source map yields an empty dictionary, source key order and per-field value order stay stable, empty
lists remain empty, null value lists become empty lists, null list items are retained, and case-only
duplicate keys still throw through `Dictionary.Add`. The copied lists remain detached snapshots. Store
unit tests, the persistence round-trip test, and the manager test lock in these guarantees.

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

## Delimited artifact construction

| Data rows | Shape | Format | Legacy | Current | Change |
| ---: | --- | --- | ---: | ---: | ---: |
| 1,000 | Narrow | CSV | 109.21 us / 451.48 KB | 82.99 us / 333.41 KB | 24.0% faster / 26.2% fewer allocations |
| 1,000 | Narrow | TSV | 108.63 us / 451.39 KB | 83.91 us / 333.31 KB | 22.8% faster / 26.2% fewer allocations |
| 1,000 | Wide | CSV | 702.49 us / 2,519.03 KB | 745.91 us / 2,307.11 KB | 6.2% slower / 8.4% fewer allocations |
| 1,000 | Wide | TSV | 700.47 us / 2,518.72 KB | 746.03 us / 2,306.80 KB | 6.5% slower / 8.4% fewer allocations |
| 1,000 | Quoted/newline-heavy | CSV | 298.54 us / 1,193.75 KB | 248.32 us / 1,044.39 KB | 16.8% faster / 12.5% fewer allocations |
| 1,000 | Quoted/newline-heavy | TSV | 308.85 us / 1,193.58 KB | 249.73 us / 1,044.22 KB | 19.1% faster / 12.5% fewer allocations |
| 10,000 | Narrow | CSV | 1,758.85 us / 4,807.23 KB | 2,075.48 us / 3,535.02 KB | 18.0% slower / 26.5% fewer allocations |
| 10,000 | Narrow | TSV | 1,758.70 us / 4,807.13 KB | 2,080.45 us / 3,534.93 KB | 18.3% slower / 26.5% fewer allocations |
| 10,000 | Wide | CSV | 15,272.12 us / 26,302.10 KB | 13,466.12 us / 24,091.71 KB | 11.8% faster / 8.4% fewer allocations |
| 10,000 | Wide | TSV | 15,271.51 us / 26,301.98 KB | 13,513.70 us / 24,091.44 KB | 11.5% faster / 8.4% fewer allocations |
| 10,000 | Quoted/newline-heavy | CSV | 6,351.44 us / 12,542.46 KB | 5,394.32 us / 10,957.71 KB | 15.1% faster / 12.6% fewer allocations |
| 10,000 | Quoted/newline-heavy | TSV | 6,290.02 us / 12,542.37 KB | 5,433.22 us / 10,957.53 KB | 13.6% faster / 12.6% fewer allocations |
| 100,000 | Narrow | CSV | 38,334.48 us / 49,183.23 KB | 33,676.03 us / 36,977.61 KB | 12.2% faster / 24.8% fewer allocations |
| 100,000 | Narrow | TSV | 38,572.05 us / 49,183.14 KB | 33,697.56 us / 36,977.51 KB | 12.6% faster / 24.8% fewer allocations |
| 100,000 | Wide | CSV | 224,542.22 us / 275,520.54 KB | 204,110.94 us / 253,936.49 KB | 9.1% faster / 7.8% fewer allocations |
| 100,000 | Wide | TSV | 224,225.84 us / 275,519.32 KB | 204,390.14 us / 253,936.21 KB | 8.8% faster / 7.8% fewer allocations |
| 100,000 | Quoted/newline-heavy | CSV | 101,001.10 us / 128,746.12 KB | 75,863.40 us / 113,416.54 KB | 24.9% faster / 11.9% fewer allocations |
| 100,000 | Quoted/newline-heavy | TSV | 100,944.62 us / 128,745.60 KB | 75,433.77 us / 113,416.39 KB | 25.3% faster / 11.9% fewer allocations |

The benchmark decodes synthetic in-memory UTF-8 streams, uses the production plain-text reader, and
reconstructs the ingestion document exactly as `TabularDocumentArtifactFactory` does, so storage and
network I/O are excluded. The legacy baseline captures the previous parser result projections and the
artifact's second header/row materialization. The current path reuses the parser-owned header and row
lists directly; delimiter detection and record parsing are unchanged. Allocation reductions are
consistent; the geometric mean across all 18 cases is 10.8% faster with 15.8% fewer allocations, and
every 100,000-row case is 8.8-25.3% faster. The 1,000-row wide and 10,000-row narrow timings regressed
despite 8.4% and 26.5% lower allocations respectively; those scale-specific results are retained here
rather than hidden.

A direct stream-to-artifact experiment omitted only the ingestion document graph and reconstruction.
It changed measured latency by -4.0% to +3.9% versus the retained path and changed allocations by no
more than 0.74 KB, including measurement noise at the largest sizes. No CSV/TSV-specific artifact
builder was added because that extra implementation would duplicate the plain-text reader for a
marginal, inconsistent gain.

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

### Conversion-goal matching

Run the isolated conversion-goal mapping comparison with:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*PostSessionConversionGoalMatchingBenchmarks*' --join
```

The benchmark uses equal configured and returned goal counts and excludes prompt rendering, JSON
parsing, chat completion, and recording. All-matched and case-variant responses use reverse configured
order; sparse responses match 10%; duplicate inputs include duplicate configured and returned names,
null names, whitespace names, and case variants. The null-entry fallback scenario places a null
configured element midway through the list and returns only names that match before it.

| Goals | Scenario | Legacy repeated scan | Current per-call dictionary | Change |
| ---: | --- | ---: | ---: | ---: |
| 10 | All matched | 385.7 ns / 1,608 B | 293.9 ns / 1,168 B | 23.8% faster / 27.4% fewer allocations |
| 10 | Sparse matched | 229.1 ns / 1,008 B | 215.3 ns / 568 B | 6.0% faster / 43.7% fewer allocations |
| 10 | Duplicates | 274.9 ns / 1,608 B | 288.4 ns / 1,168 B | 4.9% slower / 27.4% fewer allocations |
| 10 | Case variants | 437.2 ns / 1,608 B | 367.6 ns / 1,168 B | 15.9% faster / 27.4% fewer allocations |
| 100 | All matched | 18,930.5 ns / 14,992 B | 2,947.7 ns / 9,320 B | 84.4% faster / 37.8% fewer allocations |
| 100 | Sparse matched | 13,081.1 ns / 9,528 B | 1,996.4 ns / 3,856 B | 84.7% faster / 59.5% fewer allocations |
| 100 | Duplicates | 15,136.5 ns / 14,992 B | 3,436.1 ns / 9,320 B | 77.3% faster / 37.8% fewer allocations |
| 100 | Case variants | 22,162.3 ns / 14,992 B | 3,603.2 ns / 9,320 B | 83.7% faster / 37.8% fewer allocations |
| 1,000 | All matched | 1,276.18 us / 144,600 B | 42.11 us / 87,536 B | 96.7% faster / 39.5% fewer allocations |
| 1,000 | Sparse matched | 1,760.38 us / 94,192 B | 25.37 us / 37,208 B | 98.6% faster / 60.5% fewer allocations |
| 1,000 | Duplicates | 1,380.01 us / 144,600 B | 45.74 us / 87,536 B | 96.7% faster / 39.5% fewer allocations |
| 1,000 | Case variants | 1,903.48 us / 144,600 B | 48.54 us / 87,616 B | 97.5% faster / 39.4% fewer allocations |
| 10,000 | All matched | 126.91 ms / 1,542,456 B | 670.19 us / 945,546 B | 99.5% faster / 38.7% fewer allocations |
| 10,000 | Sparse matched | 173.30 ms / 936,600 B | 518.35 us / 339,667 B | 99.7% faster / 63.7% fewer allocations |
| 10,000 | Duplicates | 143.06 ms / 1,542,456 B | 629.15 us / 945,546 B | 99.6% faster / 38.7% fewer allocations |
| 10,000 | Case variants | 191.59 ms / 1,542,456 B | 745.03 us / 945,546 B | 99.6% faster / 38.7% fewer allocations |

The indexed implementation is retained because the 100-goal non-null-entry cases are 77.3-84.7%
faster and allocate 37.8-59.5% less while remaining a small per-call implementation. At 10 goals,
setup cost dominates only the duplicate-heavy case, where the 13.5 ns regression is accompanied by
27.4% fewer allocations. `TryAdd` preserves first-configured precedence, response iteration preserves
duplicates and response order, and a separate null-name slot retains the legacy null-name match
without passing a null key to `Dictionary`. If the mutable configured list contains a null element
when results are mapped, the current path intentionally falls back to the legacy per-response
`FirstOrDefault` scan. This preserves match-before-null short-circuiting and the exact
`NullReferenceException` timing for matches or misses that reach the null element. Empty returned goal
collections still return before any configured-goal scan. No indexed-performance gain is claimed for
the fallback scenario.

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

## Content title extraction

Run the title extraction comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*StringExtensionsExtractTitleBenchmarks*'
```

The measurements use BenchmarkDotNet 0.15.8, five warmups, twelve measured iterations,
.NET 10.0.5, and an Apple M2.

| Scenario | Legacy string then trim | Current span trim | Change |
| --- | ---: | ---: | ---: |
| Short clean title | 8.94 ns / 56 B | 8.66 ns / 56 B | Allocations neutral |
| Padded title | 23.10 ns / 112 B | 14.94 ns / 48 B | 35.3% faster / 57.1% fewer allocations |
| 1 KB body, early newline | 8.62 ns / 48 B | 8.49 ns / 48 B | Allocations neutral |
| 1 MB body, early newline | 8.52 ns / 48 B | 8.46 ns / 48 B | Allocations neutral |
| 1 MB single line | 53.49 us / 424 B | 52.57 us / 424 B | Allocations neutral |
| Exactly 200 code units | 36.38 ns / 424 B | 35.31 ns / 424 B | Allocations neutral |
| 201 code units | 36.42 ns / 424 B | 35.63 ns / 424 B | Allocations neutral |
| Unicode whitespace | 25.12 ns / 104 B | 13.96 ns / 48 B | 44.4% faster / 53.8% fewer allocations |

The retained change trims the bounded first-line span before materializing its string. It removes the
discarded pre-trim string only when edge whitespace is present and remains allocation-neutral for clean,
bounded, and large-document cases.

Characterization tests preserve null-to-empty behavior, CR/LF/CRLF handling independent of the platform,
the existing leading-line-ending `> 0` behavior, the 200 UTF-16-code-unit cap before trimming, tabs and
Unicode whitespace, split surrogate pairs, large single-line inputs, and equal but independently
allocated clean-title output.

## Speech audio chunk Base64 encoding

Run the audio chunk encoding comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*AudioChunkBase64EncodingBenchmarks*'
```

The measurements use BenchmarkDotNet 0.15.8, five warmups, twelve measured iterations,
.NET 10.0.5, and an Apple M2. Each operation encodes 256 x 256 B, 64 x 4 KB,
16 x 64 KB, or 4 x 1 MB chunks. Sliced cases use a 17-byte non-zero offset with
guard data on both sides, and setup verifies exact output equivalence before measuring.

| Chunk size / count | Memory | `ToArray` + Base64 | Span Base64 | Change |
| --- | --- | ---: | ---: | ---: |
| 256 B / 256 | Contiguous | 17.63 us / 248 KB | 12.10 us / 178 KB | 31.4% faster / 28.2% fewer allocations |
| 256 B / 256 | Sliced | 18.42 us / 248 KB | 12.15 us / 178 KB | 34.0% faster / 28.2% fewer allocations |
| 4 KB / 64 | Contiguous | 62.69 us / 942 KB | 52.30 us / 684.5 KB | 16.6% faster / 27.3% fewer allocations |
| 4 KB / 64 | Sliced | 63.25 us / 942 KB | 53.02 us / 684.5 KB | 16.2% faster / 27.3% fewer allocations |
| 64 KB / 16 | Contiguous | 440.28 us / 3,756.08 KB | 390.33 us / 2,731.71 KB | 11.3% faster / 27.3% fewer allocations |
| 64 KB / 16 | Sliced | 440.70 us / 3,756.08 KB | 395.28 us / 2,731.71 KB | 10.3% faster / 27.3% fewer allocations |
| 1 MB / 4 | Contiguous | 1,105.20 us / 15,019.92 KB | 891.85 us / 10,923.62 KB | 19.3% faster / 27.3% fewer allocations |
| 1 MB / 4 | Sliced | 1,137.91 us / 15,019.93 KB | 910.76 us / 10,923.60 KB | 20.0% faster / 27.3% fewer allocations |

The retained change passes each selected `ReadOnlyMemory<byte>.Span` directly to
`Convert.ToBase64String`, removing one chunk-sized array copy while preserving the Base64
string allocation required by SignalR. Behavior tests cover empty memory, full arrays,
non-zero-offset slices, deterministic binary data containing `0` and `255`, exact legacy
Base64 equivalence, both hub types, and both speech streaming methods.

SignalR semantics are unchanged: empty audio data is skipped; each non-empty first
`DataContent` emits `ReceiveAudioChunk(identifier, base64Audio, mediaType ?? "audio/mp3")`
in source order; `ReceiveAudioComplete(identifier)` remains after the full speech stream
or after each synthesized sentence. Cancellation flow, voice options, logging, media type
selection, and exception behavior are untouched.

## Named AI completion prompt tail selection

Run the complete comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*NamedAICompletionClientPromptsBenchmarks*'
```

These same-process measurements use BenchmarkDotNet 0.15.8's short-run job on .NET 10.0.5
and an Apple M2. Dense inputs make every message eligible. Sparse inputs make one message
in ten eligible and distribute the remainder across system, tool, unknown, and content-free
user or assistant messages. Iterator cases create a fresh forward-only enumerable per call.
Global setup verifies exact role, text, content count, order, and source-object identity.

| Messages | K | Eligibility | Input | Legacy materialize-all | Current bounded ring | Change |
| ---: | ---: | --- | --- | ---: | ---: | ---: |
| 10 | 1 | Dense | List | 191.2 ns / 608 B | 190.5 ns / 608 B | 0.4% faster / allocations unchanged |
| 10 | 1 | Dense | Iterator | 246.1 ns / 656 B | 250.7 ns / 656 B | 1.9% slower / allocations unchanged |
| 10 | 1 | Sparse | List | 117.5 ns / 424 B | 115.2 ns / 424 B | 2.0% faster / allocations unchanged |
| 10 | 1 | Sparse | Iterator | 154.5 ns / 472 B | 154.4 ns / 472 B | 0.1% faster / allocations unchanged |
| 10 | 2 | Dense | List | 195.2 ns / 592 B | 181.4 ns / 432 B | 7.1% faster / 27.0% fewer allocations |
| 10 | 2 | Dense | Iterator | 258.8 ns / 640 B | 209.5 ns / 480 B | 19.0% faster / 25.0% fewer allocations |
| 10 | 2 | Sparse | List | 137.0 ns / 520 B | 119.8 ns / 432 B | 12.6% faster / 16.9% fewer allocations |
| 10 | 2 | Sparse | Iterator | 178.4 ns / 568 B | 145.5 ns / 480 B | 18.4% faster / 15.5% fewer allocations |
| 10 | 20 | Dense | List | 216.6 ns / 704 B | 223.0 ns / 800 B | 3.0% slower / 13.6% more allocations |
| 10 | 20 | Dense | Iterator | 273.5 ns / 752 B | 242.7 ns / 848 B | 11.3% faster / 12.8% more allocations |
| 10 | 20 | Sparse | List | 137.9 ns / 520 B | 118.4 ns / 448 B | 14.1% faster / 13.8% fewer allocations |
| 10 | 20 | Sparse | Iterator | 176.2 ns / 568 B | 145.7 ns / 496 B | 17.3% faster / 12.7% fewer allocations |
| 10 | 200 | Dense | List | 221.6 ns / 704 B | 221.8 ns / 800 B | 0.1% slower / 13.6% more allocations |
| 10 | 200 | Dense | Iterator | 268.3 ns / 752 B | 244.9 ns / 848 B | 8.7% faster / 12.8% more allocations |
| 10 | 200 | Sparse | List | 135.8 ns / 520 B | 119.2 ns / 448 B | 12.2% faster / 13.8% fewer allocations |
| 10 | 200 | Sparse | Iterator | 175.5 ns / 568 B | 144.4 ns / 496 B | 17.7% faster / 12.7% fewer allocations |
| 1,000 | 1 | Dense | List | 9,370.7 ns / 16,448 B | 9,331.2 ns / 16,448 B | 0.4% faster / allocations unchanged |
| 1,000 | 1 | Dense | Iterator | 16,713.8 ns / 16,496 B | 16,738.0 ns / 16,496 B | 0.1% slower / allocations unchanged |
| 1,000 | 1 | Sparse | List | 5,552.6 ns / 2,048 B | 5,499.1 ns / 2,048 B | 1.0% faster / allocations unchanged |
| 1,000 | 1 | Sparse | Iterator | 7,264.9 ns / 2,096 B | 7,309.6 ns / 2,096 B | 0.6% slower / allocations unchanged |
| 1,000 | 2 | Dense | List | 8,949.8 ns / 8,512 B | 10,475.8 ns / 432 B | 17.1% slower / 94.9% fewer allocations |
| 1,000 | 2 | Dense | Iterator | 16,283.1 ns / 8,560 B | 12,543.3 ns / 480 B | 23.0% faster / 94.4% fewer allocations |
| 1,000 | 2 | Sparse | List | 5,527.1 ns / 1,312 B | 5,748.1 ns / 432 B | 4.0% slower / 67.1% fewer allocations |
| 1,000 | 2 | Sparse | Iterator | 7,233.7 ns / 1,360 B | 7,403.2 ns / 480 B | 2.3% slower / 64.7% fewer allocations |
| 1,000 | 20 | Dense | List | 8,935.4 ns / 8,704 B | 10,884.5 ns / 1,160 B | 21.8% slower / 86.7% fewer allocations |
| 1,000 | 20 | Dense | Iterator | 16,127.3 ns / 8,752 B | 16,056.3 ns / 1,208 B | 0.4% faster / 86.2% fewer allocations |
| 1,000 | 20 | Sparse | List | 5,505.8 ns / 1,504 B | 5,584.6 ns / 1,160 B | 1.4% slower / 22.9% fewer allocations |
| 1,000 | 20 | Sparse | Iterator | 7,250.2 ns / 1,552 B | 7,646.5 ns / 1,208 B | 5.5% slower / 22.2% fewer allocations |
| 1,000 | 200 | Dense | List | 8,968.6 ns / 10,144 B | 10,931.9 ns / 6,256 B | 21.9% slower / 38.3% fewer allocations |
| 1,000 | 200 | Dense | Iterator | 16,077.8 ns / 10,192 B | 12,688.9 ns / 6,304 B | 21.1% faster / 38.1% fewer allocations |
| 1,000 | 200 | Sparse | List | 5,573.7 ns / 2,144 B | 5,695.0 ns / 3,384 B | 2.2% slower / 57.8% more allocations |
| 1,000 | 200 | Sparse | Iterator | 7,355.3 ns / 2,192 B | 7,697.9 ns / 3,432 B | 4.7% slower / 56.6% more allocations |
| 100,000 | 1 | Dense | List | 1,112,234.6 ns / 1,600,615 B | 1,101,836.2 ns / 1,600,567 B | 0.9% faster / allocations unchanged |
| 100,000 | 1 | Dense | Iterator | 1,786,643.4 ns / 1,600,627 B | 1,780,027.5 ns / 1,600,630 B | 0.4% faster / allocations unchanged |
| 100,000 | 1 | Sparse | List | 558,109.7 ns / 160,448 B | 555,761.8 ns / 160,448 B | 0.4% faster / allocations unchanged |
| 100,000 | 1 | Sparse | Iterator | 732,389.7 ns / 160,496 B | 733,427.0 ns / 160,496 B | 0.1% slower / allocations unchanged |
| 100,000 | 2 | Dense | List | 965,993.2 ns / 800,587 B | 1,067,554.3 ns / 432 B | 10.5% slower / 99.9% fewer allocations |
| 100,000 | 2 | Dense | Iterator | 1,643,718.0 ns / 800,629 B | 1,282,382.3 ns / 480 B | 22.0% faster / 99.9% fewer allocations |
| 100,000 | 2 | Sparse | List | 551,145.1 ns / 80,512 B | 582,443.6 ns / 432 B | 5.7% slower / 99.5% fewer allocations |
| 100,000 | 2 | Sparse | Iterator | 724,921.8 ns / 80,560 B | 752,516.2 ns / 480 B | 3.8% slower / 99.4% fewer allocations |
| 100,000 | 20 | Dense | List | 974,078.9 ns / 800,798 B | 1,057,952.1 ns / 1,160 B | 8.6% slower / 99.9% fewer allocations |
| 100,000 | 20 | Dense | Iterator | 1,665,903.7 ns / 800,934 B | 1,647,166.5 ns / 1,208 B | 1.1% faster / 99.8% fewer allocations |
| 100,000 | 20 | Sparse | List | 549,588.6 ns / 80,704 B | 586,941.8 ns / 1,160 B | 6.8% slower / 98.6% fewer allocations |
| 100,000 | 20 | Sparse | Iterator | 720,881.9 ns / 80,752 B | 742,695.7 ns / 1,208 B | 3.0% slower / 98.5% fewer allocations |
| 100,000 | 200 | Dense | List | 956,548.8 ns / 802,216 B | 1,044,477.3 ns / 6,256 B | 9.2% slower / 99.2% fewer allocations |
| 100,000 | 200 | Dense | Iterator | 1,661,164.6 ns / 802,306 B | 1,228,016.1 ns / 6,304 B | 26.1% faster / 99.2% fewer allocations |
| 100,000 | 200 | Sparse | List | 550,765.2 ns / 82,144 B | 581,523.8 ns / 6,256 B | 5.6% slower / 92.4% fewer allocations |
| 100,000 | 200 | Sparse | Iterator | 717,144.7 ns / 82,192 B | 740,981.1 ns / 6,304 B | 3.3% slower / 92.3% fewer allocations |

The `K <= 1` control intentionally remains the original materialize-all branch; its allocations are
identical and timing differences are noise. For `K > 1`, the retained implementation enumerates the
filtered sequence once and keeps at most `K` eligible references in a small ring. At 100,000 messages,
it removes 92.3-99.9% of bounded-history allocations. List timings are 5.6-10.5% slower, adding
approximately 31-102 microseconds, while iterator timings range from 26.1% faster to 3.8% slower.
At 1,000 messages, the primary dense-list cases trade approximately 1.5-2.0 microseconds for
38.3-94.9% fewer allocations. The sparse `K=200` case is recorded rather than hidden: because only
100 messages qualify, the growing ring allocates about 1.2 KB more than materializing that small
eligible set.

`Enumerable.TakeLast` was evaluated first and rejected. In the same matrix it made the 100,000-message
dense-list cases 29-53% slower and the 1,000-message sparse `K=200` cases about 22% slower with more
than twice the allocations. The retained ring removes those queue and destination-growth costs while
remaining a one-pass implementation.

Compatibility tests preserve the exact existing contract: only user and assistant roles qualify;
null text with no content is excluded; empty or null `TextContent`, any non-text content, and whitespace
text qualify because `Contents.Count` or non-empty `Text` does; system, tool, and unknown roles are
excluded. The system message is prepended only when non-null and non-empty, so whitespace is retained.
Eligible order, duplicate references, and source-object identity remain stable. Null sources still throw
the LINQ `ArgumentNullException` synchronously from prompt selection, null elements still fail during
eager filtering, custom enumerables are acquired and consumed once, iterator exceptions propagate during
the call and disposal still occurs, and `int.MinValue` through `1` continue to mean all eligible history
while positive values above `1`, including `int.MaxValue`, mean the eligible tail.

## Azure OpenAI history conversion before tail selection

Run the text and image comparisons from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*AzureOpenAICompletionClient*HistoryBenchmarks*'
```

These same-process measurements use BenchmarkDotNet 0.15.8's short-run job on .NET 10.0.5
and an Apple M2. Dense inputs make every raw message eligible. Sparse inputs make one message
in ten eligible and distribute the remainder across system, tool, unknown, empty, whitespace,
unsupported-content, and image-only assistant messages. Global setup compares the captured
legacy implementation with the production converter and fails unless SDK message types, content
kind and order, text, media type, and image bytes are exactly equivalent.

| Raw messages | K | Eligibility | Legacy convert-all | Current bounded conversion |
| ---: | ---: | --- | ---: | ---: |
| 10 | 10 | Dense text | 827.4 ns / 5.71 KB | 1,007.0 ns / 6.01 KB |
| 10 | 10 | Sparse | 292.5 ns / 1.28 KB | 319.8 ns / 1.87 KB |
| 10 | 50 | Dense text | 859.6 ns / 5.71 KB | 1,120.9 ns / 8.82 KB |
| 10 | 50 | Sparse | 286.9 ns / 1.28 KB | 634.0 ns / 4.68 KB |
| 1,000 | 10 | Dense text | 112.240 us / 489.53 KB | 34.961 us / 25.34 KB |
| 1,000 | 10 | Sparse | 25.929 us / 71.16 KB | 19.510 us / 19.48 KB |
| 1,000 | 50 | Dense text | 101.468 us / 489.84 KB | 28.150 us / 45.81 KB |
| 1,000 | 50 | Sparse | 48.259 us / 71.48 KB | 30.926 us / 39.95 KB |
| 10,000 | 10 | Dense text | 11.308 ms / 4,983.66 KB | 0.299 ms / 201.13 KB |
| 10,000 | 10 | Sparse | 0.912 ms / 700.47 KB | 0.152 ms / 142.53 KB |
| 10,000 | 50 | Dense text | 6.214 ms / 4,983.99 KB | 0.288 ms / 221.59 KB |
| 10,000 | 50 | Sparse | 0.237 ms / 700.78 KB | 0.130 ms / 163 KB |

Image cases use a 256 KB image on every tenth eligible message. They are intentionally scaled
to 10, 100, and 1,000 raw messages instead of 10,000 to keep repeated eager Base64 data-URI
creation practical while preserving the same eligibility and retention ratios.

| Raw messages | K | Eligibility | Legacy convert-all | Current bounded conversion |
| ---: | ---: | --- | ---: | ---: |
| 10 | 10 | Dense | 192.7 us / 1.84 MB | 191.8 us / 1.84 MB |
| 10 | 10 | Sparse | 218.0 us / 1.84 MB | 198.8 us / 1.84 MB |
| 10 | 50 | Dense | 301.0 us / 1.84 MB | 166.3 us / 1.84 MB |
| 10 | 50 | Sparse | 702.4 us / 1.84 MB | 850.3 us / 1.84 MB |
| 100 | 10 | Dense | 4.918 ms / 18.39 MB | 0.548 ms / 4.10 MB |
| 100 | 10 | Sparse | 0.232 ms / 1.84 MB | 0.158 ms / 1.84 MB |
| 100 | 50 | Dense | 2.205 ms / 18.39 MB | 0.990 ms / 10.45 MB |
| 100 | 50 | Sparse | 0.154 ms / 1.84 MB | 0.159 ms / 1.84 MB |
| 1,000 | 10 | Dense | 20.247 ms / 183.86 MB | 1.627 ms / 26.64 MB |
| 1,000 | 10 | Sparse | 2.547 ms / 18.41 MB | 0.317 ms / 4.11 MB |
| 1,000 | 50 | Dense | 23.066 ms / 183.87 MB | 2.394 ms / 33.00 MB |
| 1,000 | 50 | Sparse | 1.923 ms / 18.41 MB | 1.019 ms / 10.46 MB |

The change is retained because realistic bounded histories show material reductions. At 10,000
text messages, allocations fall 76.7-96.0%; at 1,000 image-bearing messages they fall 43.2-85.5%.
Large-case latency also improves substantially, but the short-run timing errors were high, so the
allocation totals are the primary acceptance evidence. Ten-message controls receive no avoided SDK
work when all eligible messages fit within `K`; the pending ring can therefore be neutral or slower
and allocate more.

`PastMessagesCount <= 1` still takes the original immediate convert-all path. For larger values, the
converter enumerates once, classifies every raw message in encounter order, and retains a ring of the
last `K` messages that would have converted successfully. User eligibility still accepts non-whitespace
text parts and any non-empty `DataContent` with a non-whitespace media type, ignores unsupported parts,
and falls back to aggregate message text only when no supported part remains. Assistant eligibility
still uses non-whitespace aggregate text; system, tool, unknown, and otherwise ineligible entries are
not counted. Null entries still throw, and the system prompt remains outside the history count.

All raw property and content reads needed by conversion still occur at encounter time, including the
otherwise-unused successful-user text read. Consequently, exceptions from null entries, custom content
collections, getters, and source iterators still occur in source order even for candidates later evicted
from the ring. Text and media values are captured immediately, and image bytes are copied immediately,
so later mutation cannot affect retained prompts. Only the retained SDK message/content objects and
their eager Base64 image data URIs are deferred. Role and content order, duplicate occurrences, image
ownership, deployment and request options, cancellation, streaming behavior, logging, and error
handling remain unchanged.

## Azure OpenAI streaming tool-call argument accumulation

Run the complete comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*StreamingToolCallAccumulatorBenchmarks*'
```

These same-process measurements use BenchmarkDotNet 0.15.8, three warmups, eight measured
iterations, .NET 10.0.5, and an Apple M2. Fragment counts and total argument bytes are per
tool call. Each fragment round interleaves 1, 4, or 8 indexed SDK tool-call updates, including
the 1,000-fragment/1 KB small-fragment stress case. Global setup verifies exact tool-call ID,
function name, first-index appearance order, and byte-for-byte argument equivalence before
measurement.

| Fragments/call | Bytes/call | Calls | Legacy lists | Current buffer writer | Change |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 1 KB | 1 | 274.0 ns / 3.96 KB | 129.8 ns / 1.75 KB | 52.6% faster / 55.8% fewer allocations |
| 1 | 1 KB | 4 | 928.9 ns / 14.63 KB | 455.2 ns / 6.27 KB | 51.0% faster / 57.1% fewer allocations |
| 1 | 1 KB | 8 | 1,821.0 ns / 29.16 KB | 914.4 ns / 12.62 KB | 49.8% faster / 56.7% fewer allocations |
| 1 | 64 KB | 1 | 6,824.2 ns / 192.96 KB | 2,250.9 ns / 64.75 KB | 67.0% faster / 66.4% fewer allocations |
| 1 | 64 KB | 4 | 26,996.2 ns / 770.63 KB | 9,911.5 ns / 258.27 KB | 63.3% faster / 66.5% fewer allocations |
| 1 | 64 KB | 8 | 53,410.2 ns / 1541.16 KB | 18,563.2 ns / 516.62 KB | 65.2% faster / 66.5% fewer allocations |
| 1 | 1 MB | 1 | 202,362.2 ns / 3075.22 KB | 61,920.4 ns / 1025.31 KB | 69.4% faster / 66.7% fewer allocations |
| 1 | 1 MB | 4 | 1,392,867.2 ns / 12292.39 KB | 343,323.5 ns / 4099.76 KB | 75.4% faster / 66.6% fewer allocations |
| 1 | 1 MB | 8 | 2,708,770.8 ns / 24583.77 KB | 1,035,460.6 ns / 8197.67 KB | 61.8% faster / 66.7% fewer allocations |
| 10 | 1 KB | 1 | 518.6 ns / 6.41 KB | 355.1 ns / 3.97 KB | 31.5% faster / 38.1% fewer allocations |
| 10 | 1 KB | 4 | 1,935.5 ns / 24.41 KB | 1,341.6 ns / 15.15 KB | 30.7% faster / 37.9% fewer allocations |
| 10 | 1 KB | 8 | 3,825.0 ns / 48.72 KB | 2,700.7 ns / 30.37 KB | 29.4% faster / 37.7% fewer allocations |
| 10 | 64 KB | 1 | 17,639.5 ns / 327.77 KB | 13,894.3 ns / 199.29 KB | 21.2% faster / 39.2% fewer allocations |
| 10 | 64 KB | 4 | 67,324.1 ns / 1309.87 KB | 48,912.5 ns / 796.42 KB | 27.3% faster / 39.2% fewer allocations |
| 10 | 64 KB | 8 | 130,925.5 ns / 2619.64 KB | 93,234.2 ns / 1592.92 KB | 28.8% faster / 39.2% fewer allocations |
| 10 | 1 MB | 1 | 368,859.0 ns / 5227.48 KB | 191,878.5 ns / 3176.75 KB | 48.0% faster / 39.2% fewer allocations |
| 10 | 1 MB | 4 | 2,885,207.6 ns / 20894.91 KB | 1,821,509.3 ns / 12701.56 KB | 36.9% faster / 39.2% fewer allocations |
| 10 | 1 MB | 8 | 5,370,622.8 ns / 41788.21 KB | 3,110,857.2 ns / 25401.26 KB | 42.1% faster / 39.2% fewer allocations |
| 100 | 1 KB | 1 | 1,981.9 ns / 8.76 KB | 1,366.7 ns / 3.66 KB | 31.0% faster / 58.2% fewer allocations |
| 100 | 1 KB | 4 | 7,692.0 ns / 33.81 KB | 5,573.9 ns / 13.93 KB | 27.5% faster / 58.8% fewer allocations |
| 100 | 1 KB | 8 | 15,033.2 ns / 67.53 KB | 10,746.9 ns / 27.93 KB | 28.5% faster / 58.6% fewer allocations |
| 100 | 64 KB | 1 | 12,217.7 ns / 294.87 KB | 7,426.1 ns / 164.27 KB | 39.2% faster / 44.3% fewer allocations |
| 100 | 64 KB | 4 | 52,033.4 ns / 1178.25 KB | 29,213.1 ns / 656.37 KB | 43.9% faster / 44.3% fewer allocations |
| 100 | 64 KB | 8 | 104,439.5 ns / 2356.41 KB | 61,482.9 ns / 1312.8 KB | 41.1% faster / 44.3% fewer allocations |
| 100 | 1 MB | 1 | 284,405.3 ns / 4665.74 KB | 153,944.1 ns / 2613.78 KB | 45.9% faster / 44.0% fewer allocations |
| 100 | 1 MB | 4 | 2,888,868.8 ns / 18651.12 KB | 701,490.3 ns / 10450.17 KB | 75.7% faster / 44.0% fewer allocations |
| 100 | 1 MB | 8 | 4,892,364.6 ns / 37301.54 KB | 2,563,627.8 ns / 20897.15 KB | 47.6% faster / 44.0% fewer allocations |
| 1,000 | 1 KB | 1 | 16,122.7 ns / 35.38 KB | 13,236.9 ns / 2.97 KB | 17.9% faster / 91.6% fewer allocations |
| 1,000 | 1 KB | 4 | 61,067.7 ns / 140.28 KB | 43,518.7 ns / 11.15 KB | 28.7% faster / 92.1% fewer allocations |
| 1,000 | 1 KB | 8 | 126,089.2 ns / 280.47 KB | 86,311.8 ns / 22.37 KB | 31.5% faster / 92.0% fewer allocations |
| 1,000 | 64 KB | 1 | 26,065.1 ns / 290.87 KB | 17,398.5 ns / 132.93 KB | 33.2% faster / 54.3% fewer allocations |
| 1,000 | 64 KB | 4 | 101,808.3 ns / 1162.25 KB | 65,467.0 ns / 530.99 KB | 35.7% faster / 54.3% fewer allocations |
| 1,000 | 64 KB | 8 | 215,416.4 ns / 2324.41 KB | 133,540.8 ns / 1062.05 KB | 38.0% faster / 54.3% fewer allocations |
| 1,000 | 1 MB | 1 | 1,736,250.3 ns / 4174.18 KB | 125,139.6 ns / 2098.01 KB | 92.8% faster / 49.7% fewer allocations |
| 1,000 | 1 MB | 4 | 5,131,412.1 ns / 16694.23 KB | 500,194.4 ns / 8391.36 KB | 90.3% faster / 49.7% fewer allocations |
| 1,000 | 1 MB | 8 | 7,278,436.9 ns / 33386.74 KB | 1,662,534.0 ns / 16782.84 KB | 77.2% faster / 49.7% fewer allocations |

The retained implementation copies every SDK fragment immediately from
`BinaryData.ToMemory().Span` into an unpooled `ArrayBufferWriter<byte>`. It therefore does
not retain SDK-owned fragment memory. At finalization, `BinaryData.FromBytes(ReadOnlyMemory<byte>)`
wraps the writer's written slice without another copy. `BinaryData` keeps the backing array alive
after the accumulator and writer become unreachable. The accumulator never clears, resets, pools,
or reuses a finalized writer: later appends write beyond the already captured slice or resize to a
new array, and clearing the index dictionary only drops its writer references. Behavior tests verify
that finalized output remains byte-identical after repeated finalization, subsequent appends, source
fragment mutation, and dictionary clearing.

Avoiding the final exact-sized copy means a finalized `BinaryData` can retain unused
`ArrayBufferWriter<byte>` capacity, which can approach twice the written length after growth.
The measured allocation totals include that capacity. Even with this caveat, every matrix case
improved: latency fell 17.9-92.8% and allocations fell 37.7-92.1%. `MemoryStream` and pooled or
custom owners were not used because they add stream or lifetime management without improving the
required ownership model.

Compatibility remains byte-oriented. Indexes still identify dictionary entries; output follows
first index appearance rather than numeric index order; later non-empty IDs and names replace earlier
values; empty metadata is ignored; incomplete calls are filtered; duplicate IDs across different
indexes remain separate; and null or empty fragments append no bytes. Fragment bytes remain in arrival
order, including split UTF-8 and invalid UTF-8 or arbitrary binary data. The normal OpenAI SDK
deserializer produces argument fragments from JSON strings, but the accumulator does not assume UTF-8
validity. Tool calls are still finalized only on `ChatFinishReason.ToolCalls`, processed before the
dictionary is cleared, and accumulated independently for the next iteration. Emitted response-update
shape and order, cancellation-token flow, usage recording, logging, and exception propagation are
unchanged outside the isolated accumulator.

## Chat hub conversation-history construction

Run the complete comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*ChatConversationHistoryBuilderBenchmarks*'
```

The BenchmarkDotNet 0.15.8 short-run matrix covers both chat interaction and AI chat session
prompt models, 10/100/1,000/10,000 stored prompts, 0% and approximately 25% generated prompts,
the new prompt present and absent, and both contract-compliant ordered input and deliberately
unordered input. Global setup verifies exact message count, role, text, and stable order
equivalence for every case.

These representative figures use the chat interaction model, ordered store output, and an absent
new prompt. The AI chat session model has identical allocation totals and follows the same
production path.

| Prompts | Generated | Legacy | Current | Change |
| ---: | ---: | ---: | ---: | ---: |
| 10 | 0% | 464.9 ns / 3.23 KB | 281.1 ns / 2.29 KB | 39.5% faster / 29.1% fewer allocations |
| 100 | 0% | 3,571.2 ns / 25.38 KB | 2,626.6 ns / 20.57 KB | 26.5% faster / 19.0% fewer allocations |
| 1,000 | 0% | 42,700.1 ns / 246.87 KB | 28,483.3 ns / 203.38 KB | 33.3% faster / 17.6% fewer allocations |
| 10,000 | 0% | 1,292,552.8 ns / 2,461.75 KB | 558,657.8 ns / 2,031.51 KB | 56.8% faster / 17.5% fewer allocations |
| 10 | 25% | 373.0 ns / 2.63 KB | 219.0 ns / 1.68 KB | 41.3% faster / 36.1% fewer allocations |
| 100 | 25% | 3,133.2 ns / 20.30 KB | 2,016.3 ns / 15.49 KB | 35.6% faster / 23.7% fewer allocations |
| 1,000 | 25% | 59,519.7 ns / 196.09 KB | 38,250.6 ns / 152.60 KB | 35.7% faster / 22.2% fewer allocations |
| 10,000 | 25% | 950,764.7 ns / 1,953.95 KB | 402,260.4 ns / 1,523.70 KB | 57.7% faster / 22.0% fewer allocations |

Both public prompt-store contracts require creation-time ordering, and all YesSql and EntityCore
implementations provide it. The shared helper still checks monotonic timestamps. Ordered arrays take
the allocation-reduced projection and stable insertion path; a custom unordered implementation falls
back to stable `OrderBy` behavior. Defensive unordered timings were mixed because they retain sorting,
but allocations were 3.4-12.0% lower across the full matrix.

The change is retained because realistic ordered histories consistently remove the redundant source
copy and LINQ sort buffers, while centralizing identical hub semantics. Equal timestamps preserve
source order, an absent new prompt remains after existing equal-timestamp prompts, any matching
identifier suppresses insertion without deduplicating stored records, and generated prompts are
filtered only from the projected history. Iterator inputs are materialized once, null entries and
enumeration exceptions retain their existing failures, and cancellation-token flow remains unchanged
around store, group, and handler calls. Short-run timing outliers were observed in a few large matrix
cells, so deterministic allocation reductions are the primary acceptance evidence.

## Elasticsearch OData filter translation

Run the legacy/current comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*ElasticsearchODataFilterTranslatorBenchmarks*' --buildTimeout 600
```

These same-process measurements use BenchmarkDotNet 0.15.8, five warmups, twelve measured
iterations, .NET 10.0.5, and an Apple M2. Global setup compares the captured legacy Elasticsearch
translator with the production translator and fails unless their complete query JSON is ordinally
identical for every scenario.

| Scenario | Legacy `MatchCollection` | Current `EnumerateMatches` | Change |
| --- | ---: | ---: | ---: |
| Short | 291.3 ns / 1,184 B | 141.4 ns / 384 B | 51.5% faster / 67.6% fewer allocations |
| Nested | 1,236.6 ns / 5,520 B | 711.4 ns / 2,192 B | 42.5% faster / 60.3% fewer allocations |
| Long | 15,407.1 ns / 84,656 B | 16,594.6 ns / 51,392 B | 39.3% fewer allocations; timing noisy |

The change is retained because every scale materially reduces allocation. The long-filter mean was
7.7% slower, but its 99.9% confidence-interval half-width was 8.72 us for the current path versus
1.82 us for legacy, so the run does not establish a meaningful regression. Short and nested filters
were both faster.

This optimization deliberately reuses the already-retained PostgreSQL tokenizer strategy only where
the implementations were proven identical: both translators use the exact
`'[^']*'|[(),]|\w[\w.]*` generated regex. Elasticsearch-specific characterization independently
locks its parser precedence, parentheses, field mapping, JSON and wildcard escaping, output
formatting, fallback values, and exception behavior. Production changes only the regular-expression
match enumeration and exact `Substring(match.Index, match.Length)` extraction; all Elasticsearch
parsing and rendering code remains unchanged.

## AI completion handler dispatch

Run the complete legacy/current comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*DefaultAICompletionServiceHandlerDispatchBenchmarks*'
```

These same-process measurements use BenchmarkDotNet 0.15.8, five warmups, twelve measured
iterations, .NET 10.0.5, and an Apple M2. Streaming figures are normalized by
`OperationsPerInvoke`, so time and allocation columns are per emitted update. Global setup fails
unless legacy and current paths emit the same update references and produce equivalent handler
order, context-sharing patterns, update/response identities, default handler cancellation tokens,
and fault log counts. A pre-change control run produced identical allocations in all 30
legacy/current pairs.

| Updates | Handlers | Outcome | Legacy | Current | Change |
| ---: | ---: | --- | ---: | ---: | ---: |
| 1 | 0 | Successful | 139.67 ns / 768 B | 120.90 ns / 656 B | 13.4% faster / 14.6% fewer allocations |
| 1 | 0 | Faulting | 139.42 ns / 768 B | 119.00 ns / 656 B | 14.6% faster / 14.6% fewer allocations |
| 1 | 1 | Successful | 148.23 ns / 800 B | 136.18 ns / 680 B | 8.1% faster / 15.0% fewer allocations |
| 1 | 1 | Faulting | 3,166.74 ns / 1,200 B | 3,257.22 ns / 1,080 B | 2.9% slower / 10.0% fewer allocations |
| 1 | 4 | Successful | 192.79 ns / 800 B | 171.31 ns / 680 B | 11.1% faster / 15.0% fewer allocations |
| 1 | 4 | Faulting | 14,529.19 ns / 2,400 B | 13,623.36 ns / 2,280 B | 6.2% faster / 5.0% fewer allocations |
| 32 | 0 | Successful | 41.94 ns / 132 B | 30.72 ns / 20 B | 26.8% faster / 84.8% fewer allocations |
| 32 | 0 | Faulting | 44.11 ns / 132 B | 30.79 ns / 20 B | 30.2% faster / 84.8% fewer allocations |
| 32 | 1 | Successful | 50.45 ns / 164 B | 36.85 ns / 44 B | 27.0% faster / 73.2% fewer allocations |
| 32 | 1 | Faulting | 4,160.69 ns / 565 B | 4,123.48 ns / 445 B | 0.9% faster / 21.2% fewer allocations |
| 32 | 4 | Successful | 67.30 ns / 164 B | 43.24 ns / 44 B | 35.8% faster / 73.2% fewer allocations |
| 32 | 4 | Faulting | 11,256.96 ns / 1,765 B | 11,411.03 ns / 1,645 B | 1.4% slower / 6.8% fewer allocations |
| 256 | 0 | Successful | 48.62 ns / 115 B | 34.61 ns / 3 B | 28.8% faster / 97.4% fewer allocations |
| 256 | 0 | Faulting | 48.45 ns / 115 B | 34.71 ns / 3 B | 28.4% faster / 97.4% fewer allocations |
| 256 | 1 | Successful | 48.62 ns / 147 B | 41.69 ns / 27 B | 14.3% faster / 81.6% fewer allocations |
| 256 | 1 | Faulting | 3,662.81 ns / 547 B | 3,622.08 ns / 427 B | 1.1% faster / 21.9% fewer allocations |
| 256 | 4 | Successful | 71.95 ns / 147 B | 48.87 ns / 27 B | 32.1% faster / 81.6% fewer allocations |
| 256 | 4 | Faulting | 14,230.69 ns / 1,747 B | 11,891.72 ns / 1,627 B | 16.4% faster / 6.9% fewer allocations |
| 4,096 | 0 | Successful | 38.54 ns / 112 B | 27.45 ns / 0 B | 28.8% faster / allocation-free after amortization |
| 4,096 | 0 | Faulting | 38.17 ns / 112 B | 27.46 ns / 0 B | 28.1% faster / allocation-free after amortization |
| 4,096 | 1 | Successful | 44.15 ns / 144 B | 32.73 ns / 24 B | 25.9% faster / 83.3% fewer allocations |
| 4,096 | 1 | Faulting | 2,918.61 ns / 544 B | 2,796.53 ns / 424 B | 4.2% faster / 22.1% fewer allocations |
| 4,096 | 4 | Successful | 58.00 ns / 144 B | 43.12 ns / 24 B | 25.7% faster / 83.3% fewer allocations |
| 4,096 | 4 | Faulting | 11,083.40 ns / 1,744 B | 11,469.82 ns / 1,624 B | 3.5% slower / 6.9% fewer allocations |

The zero-handler `Faulting` rows intentionally duplicate the successful behavior because there is
no handler to fault. At 4,096 updates, the current zero-handler stream still has one small
per-stream allocation, which BenchmarkDotNet rounds to 0 B per update after normalization.

| Non-stream handlers | Outcome | Legacy | Current | Change |
| ---: | --- | ---: | ---: | ---: |
| 0 | Successful | 78.49 ns / 552 B | 57.71 ns / 440 B | 26.5% faster / 20.3% fewer allocations |
| 0 | Faulting | 87.38 ns / 552 B | 81.33 ns / 440 B | 6.9% faster / 20.3% fewer allocations |
| 1 | Successful | 109.13 ns / 584 B | 77.12 ns / 464 B | 29.3% faster / 20.5% fewer allocations |
| 1 | Faulting | 3,671.30 ns / 984 B | 3,508.25 ns / 864 B | 4.4% faster / 12.2% fewer allocations |
| 4 | Successful | 109.67 ns / 584 B | 94.46 ns / 464 B | 13.9% faster / 20.5% fewer allocations |
| 4 | Faulting | 14,023.82 ns / 2,184 B | 13,876.42 ns / 2,064 B | 1.1% faster / 5.5% fewer allocations |

The retained implementation does not snapshot handlers. Microsoft dependency injection supplies
the scoped `IEnumerable<IAICompletionHandler>` as an ordered array, so production caches only that
array reference, skips context/delegate dispatch for an empty array, and indexes non-empty arrays
directly. Arbitrary enumerable inputs retain legacy lazy re-enumeration for every response or
update. This preserves mutations between chunks, single-use-enumerable failures, and enumerator
exceptions and disposal; a constructor-time or first-use `ToArray()` snapshot was rejected because
it changes all three behaviors.

Direct typed message/update loops remove the capturing per-dispatch delegate while retaining the
exact `DefaultAICompletionService` logging category, message template, handler type, exception
swallowing, and later-handler continuation. The existing shared `HandlerExtensions.InvokeAsync`
was not reused because its public contract logs a different template and has different null-handler
fallback behavior. Changing that helper would have altered public behavior. The zero-handler path
also retains the legacy `ReceivedUpdateContext` null-update failure and exact `update` parameter
name.

Successful realistic streams remove a deterministic 120 B per update with one or four handlers,
while the zero-handler path removes 112 B per update. Faulting handlers still remove the same fixed
120 B, but exception throwing and structured logging dominate total cost, leaving only 5.0-22.1%
allocation reductions and noisy latency changes from 3.5% slower to 16.4% faster. The change is
retained for the large, consistent zero/successful-handler gains; no fault-path latency improvement
is claimed.

## Security prompt preamble composition

Run the complete legacy/current comparison from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*SecurityPromptPreambleCompositionBenchmarks*' --join
```

These same-process measurements use BenchmarkDotNet 0.15.8, five warmups, twelve measured
iterations, .NET 10.0.5, and an Apple M2. Each measured invocation operates on 16 independently
prepared builders and reports normalized per-composition values. Input construction is excluded.
ASCII lengths make the requested byte counts equal the input character counts; the builder itself
stores UTF-16. Contiguous inputs occupy one pre-sized chunk. Many-append inputs use 64-character
appends and setup rejects a non-empty builder unless it contains multiple chunks.

Global setup fails unless legacy and current return the original builder and produce exactly
`preamble + Environment.NewLine + Environment.NewLine + existing` for non-empty builders, or just
`preamble` for empty builders, using ordinal comparison. The composition-and-flush category also
includes the final `StringBuilder.ToString()` performed by the orchestration context builder.

| Existing | Shape | Preamble | Legacy composition | Current composition | Composition change | Legacy + flush | Current + flush | Flush change |
| ---: | --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| 0 B | Contiguous | 256 B | 85.33 ns / 552 B | 86.39 ns / 552 B | 1.2% slower / allocations unchanged | 174.72 ns / 1.06 KB | 179.00 ns / 1.06 KB | 2.4% slower / allocations unchanged |
| 0 B | Contiguous | 4 KB | 507.01 ns / 8.04 KB | 502.65 ns / 8.04 KB | 0.9% faster / allocations unchanged | 940.56 ns / 16.06 KB | 965.02 ns / 16.06 KB | 2.6% slower / allocations unchanged |
| 0 B | Many appends | 256 B | 100.36 ns / 584 B | 86.24 ns / 584 B | 14.1% faster / allocations unchanged | 189.65 ns / 1.09 KB | 179.20 ns / 1.09 KB | 5.5% faster / allocations unchanged |
| 0 B | Many appends | 4 KB | 541.00 ns / 8.07 KB | 527.86 ns / 8.07 KB | 2.4% faster / allocations unchanged | 980.69 ns / 16.09 KB | 967.91 ns / 16.09 KB | 1.3% faster / allocations unchanged |
| 256 B | Contiguous | 256 B | 211.42 ns / 2.16 KB | 123.48 ns / 688 B | 41.6% faster / 69.0% fewer allocations | 320.84 ns / 3.20 KB | 254.99 ns / 1.70 KB | 20.5% faster / 46.7% fewer allocations |
| 256 B | Contiguous | 4 KB | 935.38 ns / 16.16 KB | 551.26 ns / 8.17 KB | 41.1% faster / 49.4% fewer allocations | 1.331 us / 24.70 KB | 968.24 ns / 16.70 KB | 27.2% faster / 32.4% fewer allocations |
| 256 B | Many appends | 256 B | 284.08 ns / 2.69 KB | 129.32 ns / 688 B | 54.5% faster / 75.0% fewer allocations | 379.75 ns / 3.72 KB | 271.39 ns / 1.70 KB | 28.5% faster / 54.2% fewer allocations |
| 256 B | Many appends | 4 KB | 887.45 ns / 16.69 KB | 534.67 ns / 8.17 KB | 39.8% faster / 51.0% fewer allocations | 1.441 us / 25.22 KB | 990.75 ns / 16.70 KB | 31.3% faster / 33.8% fewer allocations |
| 8 KB | Contiguous | 256 B | 1.448 us / 31.72 KB | 113.06 ns / 688 B | 92.2% faster / 97.9% fewer allocations | 2.138 us / 48.25 KB | 938.06 ns / 17.20 KB | 56.1% faster / 64.3% fewer allocations |
| 8 KB | Contiguous | 4 KB | 1.653 us / 31.72 KB | 520.12 ns / 8.17 KB | 68.5% faster / 74.2% fewer allocations | 2.661 us / 55.75 KB | 1.623 us / 32.20 KB | 39.0% faster / 42.2% fewer allocations |
| 8 KB | Many appends | 256 B | 1.642 us / 47.74 KB | 196.72 ns / 688 B | 88.0% faster / 98.6% fewer allocations | 2.497 us / 64.27 KB | 1.096 us / 17.20 KB | 56.1% faster / 73.2% fewer allocations |
| 8 KB | Many appends | 4 KB | 1.829 us / 47.74 KB | 602.72 ns / 8.17 KB | 67.0% faster / 82.9% fewer allocations | 2.851 us / 71.77 KB | 1.804 us / 32.20 KB | 36.7% faster / 55.1% fewer allocations |
| 1 MB | Contiguous | 256 B | 202.532 us / 2.02 MB | 120.55 ns / 688 B | 99.9% faster / 99.97% fewer allocations | 350.372 us / 4.02 MB | 138.119 us / 2.00 MB | 60.6% faster / 50.2% fewer allocations |
| 1 MB | Contiguous | 4 KB | 200.720 us / 2.02 MB | 705.93 ns / 8.17 KB | 99.6% faster / 99.6% fewer allocations | 350.792 us / 4.02 MB | 138.607 us / 2.02 MB | 60.5% faster / 49.9% fewer allocations |
| 1 MB | Many appends | 256 B | 408.920 us / 4.01 MB | 2.762 us / 688 B | 99.3% faster / 99.98% fewer allocations | 521.434 us / 6.02 MB | 188.172 us / 2.00 MB | 63.9% faster / 66.7% fewer allocations |
| 1 MB | Many appends | 4 KB | 406.102 us / 4.01 MB | 3.245 us / 8.17 KB | 99.2% faster / 99.8% fewer allocations | 524.851 us / 6.02 MB | 190.481 us / 2.02 MB | 63.7% faster / 66.5% fewer allocations |

The empty-builder path still appends the preamble directly. Its allocations are identical, and its
timing differences range from 2.6% slower to 14.1% faster with overlapping error intervals, so no
small-message latency change is claimed. Every non-empty scenario improves both allocation and mean
latency. Including the required final flush, 256-byte messages are 20.5-31.3% faster with
32.4-54.2% fewer allocations; 8 KB messages are 36.7-56.1% faster with 42.2-73.2% fewer
allocations; and 1 MB messages are 60.5-63.9% faster with 49.9-66.7% fewer allocations.

The retained implementation inserts a cached double platform-newline at index zero, then inserts the
preamble at index zero. Both operations therefore prepend instead of inserting the separator into
the middle of the newly inserted preamble. A single combined-prefix insertion was rejected: it
allocates a discarded combined string on every non-empty call. In the candidate run, separator-first
insertion used 688 B versus 1,136 B with a 256-byte preamble and 8,368 B versus 16,496 B with a
4 KB preamble. The combined candidate was sometimes faster for a 1 MB many-append builder before
the final flush, but separator-first was faster for contiguous builders, had the lowest allocation in
every non-empty case, and the required final-flush timings were effectively equivalent.

Builder capacity and chunk counts are implementation details, but the same-runtime diagnostic
observations explain the allocation results:

| Existing | Shape | Initial capacity/chunks | Legacy final, 256 B preamble | Current final, 256 B preamble | Legacy final, 4 KB preamble | Current final, 4 KB preamble |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 0 B | Contiguous | 16 / 1 | 256 / 2 | 256 / 2 | 4,096 / 2 | 4,096 / 2 |
| 0 B | Many appends | 1 / 1 | 256 / 2 | 256 / 2 | 4,096 / 2 | 4,096 / 2 |
| 256 B | Contiguous | 256 / 1 | 1,024 / 3 | 514 / 3 | 8,192 / 3 | 4,354 / 3 |
| 256 B | Many appends | 256 / 4 | 1,024 / 3 | 514 / 6 | 8,192 / 3 | 4,354 / 6 |
| 8 KB | Contiguous | 8,192 / 1 | 16,192 / 2 | 8,450 / 3 | 16,192 / 2 | 12,290 / 3 |
| 8 KB | Many appends | 8,192 / 9 | 16,192 / 2 | 8,450 / 11 | 16,192 / 2 | 12,290 / 11 |
| 1 MB | Contiguous | 1,048,576 / 1 | 1,056,576 / 2 | 1,048,834 / 3 | 1,056,576 / 2 | 1,052,674 / 3 |
| 1 MB | Many appends | 1,056,192 / 140 | 1,056,192 / 1 | 1,056,450 / 142 | 1,056,192 / 1 | 1,060,290 / 142 |

The legacy path materialized the existing message, cleared the builder, and appended the full copy,
which often flattened chunked inputs and over-expanded contiguous capacity. The retained path keeps
the original chunks and adds two prefix chunks. The context builder's final `ToString()` therefore
remains part of the realistic benchmark rather than being hidden by the composition-only result.

Behavior tests lock null, empty, whitespace-only, large, Unicode, and lone-surrogate preambles;
empty, whitespace-only, contiguous, chunked, newline-prefixed/suffixed, mixed CR/LF, and 1 MB
existing messages; exact ordinal output; builder and context identity; repeated invocation;
cancellation-token propagation; cancellation ignored by a completing renderer; and unchanged
template exception/cancellation mutation boundaries.

## Tabular tool result formatting and column-type inference

### Query result formatting retained

| Result shape | Legacy join/LINQ | Direct append | Change |
| --- | ---: | ---: | ---: |
| Compact aggregation (5 rows / 3 cols, mixed) | 724.7 ns / 1,392 B | 350.3 ns / 648 B | 51.7% faster / 53.4% fewer allocations |
| Row cap (100 rows / 6 cols, strings) | 11,324.6 ns / 39,576 B | 4,767.3 ns / 16,200 B | 57.9% faster / 59.1% fewer allocations |
| Row cap (100 rows / 6 cols, mixed + nulls) | 20,766.4 ns / 43,368 B | 12,141.5 ns / 24,000 B | 41.5% faster / 44.7% fewer allocations |

The retained production change in `QueryTabularDataTool.FormatResult` appends each column and cell
straight into the shared `ZString` builder with inline `" | "` separators, replacing the per-row
`string.Join(" | ", row.Select(FormatCell))` projection. `IFormattable` cells still format with
`CultureInfo.InvariantCulture`, `null` cells still render as empty, and other cells still use
`ToString()`, so the output is byte-for-byte identical. Query results are capped at the default 100
rows and 200 characters per cell, so each call is bounded, but eliminating the transient per-row
joined string and LINQ enumerator removed 44–59% of allocations across every shape while also
improving timing. Reported values are per single `FormatResult` call at `OperationsPerInvoke = 1000`;
global setup invokes the production formatter through reflection and rejects any divergence from the
legacy and direct reproductions. Characterization tests lock the exact singular/plural, truncation,
separator, ragged-row, null, and invariant-culture output.

### Column-type inference candidate rejected

| Column shape | Columns | Legacy classify-all | Text-absorbing candidate | Result |
| --- | ---: | ---: | ---: | --- |
| All text | 8 | 13,770.2 ns / 200 B | 450.1 ns / 200 B | Faster timing; allocations unchanged |
| All text | 40 | 68,207.6 ns / 712 B | 3,698.2 ns / 712 B | Faster timing; allocations unchanged |
| All typed | 8 | 17,186.7 ns / 200 B | 13,423.2 ns / 200 B | Faster timing; allocations unchanged |
| All typed | 40 | 92,344.1 ns / 712 B | 100,063.6 ns / 712 B | Slower timing; allocations unchanged |
| Half text / half typed | 8 | 18,061.4 ns / 200 B | 8,226.7 ns / 200 B | Faster timing; allocations unchanged |
| Half text / half typed | 40 | 91,448.0 ns / 712 B | 60,786.8 ns / 712 B | Faster timing; allocations unchanged |

The candidate stops sampling a column in `GetDocumentMetadataTool.InferColumnTypes` once it becomes
text, since text absorbs every later value and cannot be promoted back. Output is identical in every
case — global setup verifies the production helper, the legacy reproduction, and the candidate agree —
and text-heavy columns (the common shape for uploaded spreadsheets) classify dramatically faster
because the wasted `bool`/`long`/`decimal`/date parse chain is skipped after the first text value.
Even so, the change was not retained: it produces no allocation reduction in any case, it runs once
per `get_document_metadata` call over at most 32 samples per column rather than on a sustained path,
and it is neutral-to-slightly-slower for all-typed tables where the extra per-cell state check never
pays off. Consistent with the allocation-first acceptance criterion, production remains unchanged and
the characterization tests continue to lock the exact inference contract.

A wider scan of the `CrestApps.Core.AI.Documents` project found the remaining `string.Join`/`Select`
formatting sites — `ListTabularDataTool` column listing, the `GetDocumentMetadataTool` inferred-type
summaries, `PlainTextGeneratedFileWriter` Markdown-table rendering, and the `TabularWorkspace` DDL/DML
identifier lists — all run once per bounded, model-sized invocation rather than on a sustained hot
path, so none were changed without their own proof.

Both benchmark classes use five warmups and twelve measured iterations. Run both comparisons from the
repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*TabularQueryResultFormattingBenchmarks*' '*TabularColumnTypeInferenceBenchmarks*'
```

## Copilot prompt composition

Run the retained history comparison and rejected MCP experiment from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*Copilot*Benchmarks*' --join
```

These same-process measurements use BenchmarkDotNet 0.15.8, five warmups, twelve measured
iterations, .NET 10.0.5, and an Apple M2. Global setup fails unless the captured legacy,
production, and candidate implementations produce ordinally identical text.

### Conversation-history text reads

| History messages | Content shape | Legacy repeated text reads | Current single read | Change |
| ---: | --- | ---: | ---: | ---: |
| 10 | Multiple text contents | 2,055.8 ns / 5.84 KB | 1,049.2 ns / 3.96 KB | 49.0% faster / 32.2% fewer allocations |
| 100 | Multiple text contents | 16,285.2 ns / 55.85 KB | 8,904.6 ns / 37.10 KB | 45.3% faster / 33.6% fewer allocations |
| 1,000 | Multiple text contents | 221,572.9 ns / 557.74 KB | 136,438.6 ns / 370.24 KB | 38.4% faster / 33.6% fewer allocations |
| 10,000 | Multiple text contents | 5,635,880.3 ns / 5,594.09 KB | 3,045,225.7 ns / 3,718.99 KB | 46.0% faster / 33.5% fewer allocations |

For one text-content item per message, allocations are unchanged and timing differences are
noise. With only one supported role in ten, allocations fall 13.1-14.3%; latency was inconsistent
across repeated runs, so no sparse-role timing improvement is claimed. Allocation is the primary
acceptance evidence.

The retained change reads `ChatMessage.Text` once and reuses that exact value. Version 10.7.0 of
Microsoft.Extensions.AI builds this property from every text-content item, so the legacy second
read repeated both projection work and aggregate-string allocation. Evaluation order remains
unchanged: text is still read before role filtering. Tests lock no-history reference identity,
null and empty text, mixed content types, unsupported roles, whitespace, embedded newlines,
section formatting, and existing null-element failure behavior.

### MCP single-builder experiment rejected

| Connections | Existing 8 KB system message | Current production | Single-builder candidate | Change |
| ---: | --- | ---: | ---: | ---: |
| 10 | No | 623.9 ns / 1.45 KB | 709.6 ns / 1.45 KB | Allocations unchanged |
| 10 | Yes | 2,021.6 ns / 18.34 KB | 2,881.4 ns / 17.45 KB | 4.9% fewer allocations; slower in this run |
| 100 | Yes | 4,498.5 ns / 32.99 KB | 5,393.7 ns / 24.78 KB | 24.9% fewer allocations; slower in this run |
| 1,000 | Yes | 75,100.3 ns / 185.42 KB | 67,200.8 ns / 101.00 KB | 10.5% faster / 45.5% fewer allocations |

The captured legacy and unchanged production controls allocate identically in all six matrix
cases. Their timing varied enough to make small latency differences directional only. The
candidate provides no allocation benefit without an existing system message and saves only
0.89 KB for the realistic ten-connection case. Its material gains are concentrated at atypical
hundred- and thousand-server counts, so production retains the simpler intermediate-description
composition.

The remaining Copilot project was also scanned end to end. Early role filtering was rejected
because it would skip the legacy `ChatMessage.Text` evaluation and its failure behavior for
unsupported roles. Tool-list projection removes at most a small LINQ iterator around asynchronous
tool factories, while OAuth model projection, serializer streaming, and protector caching sit on
network-, file-, or cryptography-bound paths and either offer marginal application-level savings
or change failure timing. No additional production candidate justified benchmark or compatibility
surface.

## MCP resource URI matching and remote path sanitization

Run the comparisons from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*McpResourceUriBenchmarks*'

dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*McpPathSanitizationBenchmarks*'
```

These same-process measurements use BenchmarkDotNet 0.15.8, five warmups, twelve measured
iterations, .NET 10.0.5, and an Apple M2.

### Resource URI matching

| Scenario | Legacy dynamic regex | Current cached matcher | Change |
| --- | ---: | ---: | ---: |
| Encoded path | 2,777.47 ns / 7,504 B | 336.59 ns / 800 B | 87.9% faster / 89.3% fewer allocations |
| Exact URI | 4,007.04 ns / 6,360 B | 226.58 ns / 80 B | 94.3% faster / 98.7% fewer allocations |
| FTP path | 2,751.07 ns / 7,256 B | 397.07 ns / 696 B | 85.6% faster / 90.4% fewer allocations |
| Multiple variables | 6,678.49 ns / 8,688 B | 525.37 ns / 976 B | 92.1% faster / 88.8% fewer allocations |
| Non-match | 2,304.43 ns / 6,384 B | 73.82 ns / 0 B | 96.8% faster / allocation-free |

The retained implementation retains up to 256 template matchers, keyed by the trimmed template
and current culture. Matcher construction still uses the existing regular expression, preserving
case rules, placeholder recognition, greedy capture and backtracking, duplicate-name capture,
literal escaping, encoded-value decoding, and exception behavior. Exact templates use the cached
regex's allocation-free match check before returning the same mutable empty dictionary shape.

Benchmark setup validates equivalence and warms each production matcher, so the figures represent
repeated matching. First-use matcher construction remains similar to the legacy path and occurs once
for each retained template and culture pair.

The allocation reductions are deterministic; timing is directional because the legacy exact and
multiple-variable measurements were noisy. Forty-three behavior cases cover whitespace, literals,
single and multiple variables, adjacent and duplicate variables, trailing literals, encoded slashes,
non-placeholder braces, multi-segment final captures, empty captures, and culture-specific
case-insensitive matching.

### Remote resource path sanitization

| Scenario | Legacy split + join | Current scanner | Change |
| --- | ---: | ---: | ---: |
| Flat file | 16.348 ns / 32 B | 5.264 ns / 0 B | 67.8% faster / allocation-free |
| Nested path | 83.606 ns / 328 B | 41.954 ns / 0 B | 49.8% faster / allocation-free |
| 64 segments | 1,218.977 ns / 4,936 B | 702.766 ns / 0 B | 42.3% faster / allocation-free |
| Mixed and repeated separators | 109.264 ns / 528 B | 107.222 ns / 104 B | Timing neutral / 80.3% fewer allocations |

The scanner returns already normalized FTP and SFTP paths unchanged. Paths needing normalization
are validated once for empty, current-directory, parent-directory, backslash, repeated-separator,
and null-byte behavior, then written directly to the final string. Tests preserve exact results and
exception messages for explicit edge cases and a 3,645-input legacy/current differential matrix.

The remaining MCP, FTP, and SFTP scan retained no speculative changes. Client reuse and parallel
capability discovery require explicit lifetime, disposal, and server-concurrency guarantees.
Consolidating the existing FTP and SFTP handlers onto the remote-file base would introduce a new
download-size limit, while replacing the stream-reader path could change encoding and BOM behavior.
Tool argument and metadata prompt micro-optimizations remain dominated by remote calls or sorting,
so production keeps their simpler implementations.

## EntityCore chat-session document I/O

Run the retained paging and save comparisons, plus the rejected narrow-deserialization
experiment, from the repository root:

```bash
dotnet run -c Release -f net10.0 \
  --project benchmarks/CrestApps.Core.Benchmarks/CrestApps.Core.Benchmarks.csproj \
  -- --filter '*EntityCoreAIChatSession*Benchmarks*'
```

These same-process measurements use BenchmarkDotNet 0.15.8 on .NET 10.0.5 and an Apple M2.
Paging uses two warmups and six measured iterations. Saves use one invocation per iteration,
five warmups, and fifteen measured iterations so each result represents one database update.
All payloads contain valid ASCII JSON with exact 1 KB, 64 KB, or 1 MB lengths.

### Retained page projection

The production page query now selects only the indexed summary columns and serialized content,
instead of materializing both `AIChatSessionRecord` and `DocumentRecord`. Full
`AIChatSession` deserialization remains deferred so malformed-payload exception timing and
validation semantics are unchanged.

| Payload | Rows | Legacy allocation | Projected allocation | Change |
| ---: | ---: | ---: | ---: | ---: |
| 1 KB | 1 | 41.02 KB | 48.54 KB | 7.52 KB more |
| 1 KB | 20 | 145.29 KB | 142.32 KB | 2.0% fewer |
| 1 KB | 200 | 1,136.22 KB | 1,029.56 KB | 9.4% fewer |
| 64 KB | 1 | 229.80 KB | 237.85 KB | 8.05 KB more |
| 64 KB | 20 | 3,926.47 KB | 3,922.89 KB | 3.58 KB fewer |
| 64 KB | 200 | 38,941.85 KB | 38,834.27 KB | 107.58 KB fewer |
| 1 MB | 1 | 3,110.54 KB | 3,119.01 KB | 8.47 KB more |
| 1 MB | 20 | 61,530.82 KB | 61,527.22 KB | 3.60 KB fewer |
| 1 MB | 200 | 614,945.54 KB | 614,832.63 KB | 112.91 KB fewer |

The MVC session page requests up to 200 rows, where the projection consistently removes about
107-113 KB. One-row controls expose a roughly 8 KB fixed projection cost. Timing varied heavily
once SQLite and large-object collections dominated, so no page-latency claim is made. The view's
two `Any()` checks use LINQ's cheap-count path and do not repeat JSON deserialization.

### Retained update path

Existing session updates now query the indexed record without loading the previous JSON document.
A key-only `DocumentRecord` is attached and only its content property is marked modified. The
manager updates `LastActivityUtc` on every save, so explicitly staging the document write matches
the existing write-path intent.

| Payload | Legacy allocation | Current allocation | Change |
| ---: | ---: | ---: | ---: |
| 1 KB | 91.45 KB | 92.31 KB | Allocations effectively unchanged |
| 64 KB | 406.45 KB | 281.31 KB | 30.8% fewer |
| 1 MB | 5,206.45 KB | 3,161.31 KB | 39.3% fewer |

Save timings varied materially across repeated in-memory SQLite runs, so no latency claim is made.
The removed payload-sized allocation and database read are deterministic. Regression coverage
verifies that a large existing document is replaced, only document content is marked modified,
the document type remains intact, and the updated session round-trips.

### Narrow summary deserialization rejected

Deserializing a timestamp-only type is substantially cheaper on valid documents:

| Payload | Rows | Full session | Timestamp-only projection |
| ---: | ---: | ---: | ---: |
| 1 KB | 1 | 0.939 us / 2,216 B | 0.336 us / 32 B |
| 1 KB | 20 | 18.914 us / 44,320 B | 7.787 us / 640 B |
| 1 KB | 200 | 181.700 us / 443,200 B | 60.134 us / 6,400 B |
| 64 KB | 1 | 12.458 us / 66,728 B | 6.400 us / 32 B |
| 64 KB | 20 | 234.966 us / 1,334,560 B | 124.059 us / 640 B |
| 64 KB | 200 | 2.208 ms / 13,345,600 B | 1.182 ms / 6,400 B |
| 1 MB | 1 | 0.205 ms / 1,050,417 B | 0.082 ms / 32 B |
| 1 MB | 20 | 4.298 ms / 21,007,783 B | 1.674 ms / 640 B |
| 1 MB | 200 | 42.689 ms / 210,079,914 B | 17.375 ms / 6,400 B |

The narrow path was rejected despite these gains. It accepts corrupt known properties that full
deserialization rejects, including invalid `CreatedUtc`, object-shaped `Documents`, and string
enum values. Production therefore continues to deserialize the full `AIChatSession`, preserving
missing and null timestamps, case-insensitive names, last-duplicate-wins behavior, large nested
payloads, malformed JSON failures, and failures from malformed non-summary properties. A value-type
page projection was also rejected because it allocated more at 200 rows and produced worse timing
than the retained anonymous reference projection.
