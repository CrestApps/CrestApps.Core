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

The prompt merge preserves catalog, provider, and SDK precedence, keeps ordinal case-sensitive name
matching, and retains duplicate catalog entries. A hash set replaces only the repeated linear scans
used when provider and SDK prompts are added.

Bounded document context formatting still uses stable chunk ordering and returns the exact same
prefix and truncation marker. It computes the joined length first and writes only the requested
content prefix instead of materializing the entire document before truncation.
