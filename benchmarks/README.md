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
