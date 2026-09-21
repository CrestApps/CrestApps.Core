# A general document-structure engine

## Why the current one is not general

`TocSeededStructureAnalyzer` splits a document into articles in three tiers: read the table of
contents, else infer from heading type size, else one article. It is a magazine analyzer with a
safe fallback, and it says so — `KnowledgeArticleTypes` has exactly two values, `Article` and
`Advertisement`.

Three properties stop it generalising.

**One boundary per page.** `InferHeadingBoundaries` keeps a single `title` per page and takes the
one highest on it, and `HeadingTopBand` requires that heading to sit in the top 30%. The comment
states the assumption plainly: *"a heading halfway down it is a subheading inside one"*. That is
true of a magazine and false of a newspaper, where four to six stories share a page and every
headline after the first is absorbed into the one above it.

**No hierarchy.** `DocumentStructure.Articles` is a flat list, and `KnowledgeObjectTypes` has no
`section`. A user manual is a tree — part, chapter, section, procedure — and all of it collapses
into one article whose sections survive only as `text` chunks. "The Methods section of that paper"
cannot be returned as a hit because no such object exists.

**Type size is the only structural signal.** Only `PdfIngestionDocumentReader` writes
`ModalPointSize`, so a document read through Azure Document Intelligence — the reader you reach for
when the file is scanned — cannot use the heading tier at all. It gets the table of contents or one
article, nothing else.

## What is already solved

Reading order is not the problem. The reader segments with `DocstrumBoundingBoxes`, extracts words
with `NearestNeighbourWordExtractor`, and orders blocks with `UnsupervisedReadingOrderDetector`
under `ColumnWise` rules. A newspaper's columns already extract in the right order. Only the
splitting is wrong.

## What is available and unused

Two authoritative structure sources ship inside PdfPig and nothing in this repository touches them.

**The outline (`PdfDocument.TryGetBookmarks`).** `BookmarkNode` carries `Title`, `Level`, `Children`
and `IsLeaf`; `DocumentBookmarkNode` adds `PageNumber`. That is a complete hierarchical outline with
page destinations, stated by the document rather than inferred from it. Virtually every user manual,
technical manual, technical report and most white papers carry one. No heuristic can beat it.

**Tagged content (`Page.GetMarkedContents`).** `MarkedContentElement` carries `Tag` (the structure
role), `IsArtifact`, `ActualText`, `AlternateDescription` and `Language`. For a tagged PDF this gives
real roles instead of font-size guesses, marks running heads and folios as artifacts — replacing the
`EdgeBand` heuristic — and supplies figure alt text without a vision call.

Azure Document Intelligence is a third: its layout model already reports paragraph roles such as
`title` and `sectionHeading`, and works on scans. `DocumentIntelligenceDocumentMapper` currently
drops that role information.

## The shape it took

`IDocumentStructureAnalyzer` now runs a ladder, each rung authoritative over the next and each
degrading to the one below. The contract that nothing may fail an ingest is kept: the last rung is
still one division covering everything.

| Rung | `DocumentStructureSources` | Authority | Covers |
| --- | --- | --- | --- |
| 1 | `Outline` | stated | manuals, reports, books, most white papers |
| 2 | `StatedHeadings` | stated | Word, HTML, tagged PDFs, anything Document Intelligence read |
| 3 | `TableOfContents` | stated, fuzzy-matched | magazines, journals |
| 4 | `InferredHeadings` | inferred | anything with type-size contrast |
| 5 | `Whole` | none | the answer that is never wrong |

Rung 2 collapsed what were going to be three separate rungs. A Word paragraph styled `Heading 2`, an
`h2`, a tagged PDF's `H2` and a layout service's section heading are one fact written four ways, so
`ElementMetadataKeys.HeadingLevel` carries it from every reader and one rung divides on it. Adding a
format means teaching its reader to write that key, not writing another strategy.

### Where the keys live, and why it is not here

`ElementMetadataKeys` sits in `CrestApps.Core.Abstractions`, not in the ingestion package, because
every reader writes them and not every reader is about AI. `CrestApps.Core.DataIngestion` turns HTML
into text; it needs the key for a heading level and nothing else in this document.

Holding the keys in the ingestion package meant that reader referencing it for one `const string`,
and taking with it eight CrestApps assemblies, Lucene, ZString and a framework reference to
ASP.NET Core — to spell a constant. Its dependency closure is now itself and one abstractions
assembly.

The general rule this follows: what every reader must agree on is a contract, and a contract belongs
below the things that implement it. A reader should be able to state that a line is a heading without
taking on the machinery that later decides what to do about it.

### What each reader now states

| Reader | Writes |
| --- | --- |
| PDF | the outline, and `H1`–`H6` from marked content on tagged pages |
| Word | the paragraph's outline level, else its `Heading N` style |
| HTML | `h1`–`h6`, and the blocks a page is written in rather than one run of its text |
| Document Intelligence | `Title` and `SectionHeading` as levels one and two |

### Model changes

- `DocumentArticle` carries `Depth` and `ParentOrdinal`, so a chapter and its sections are one
  ordered list with the nesting recorded on each. Every consumer that walks a document in reading
  order keeps working without knowing nesting exists.
- `ElementStart` bounds a division by element rather than by page, which is what lets several
  divisions share a page — three headings on one page in a manual, four stories on a newspaper page.
  Sources that only state a page, such as an outline, leave it unset.
- `KnowledgeObjectTypes.Section` stores a nested division as what it is, hanging off the division
  containing it rather than off the document.
- `DocumentStructure.Source` reports which rung answered.

### The ladder is open

`DocumentStructureAnalyzer` asks every registered `IDocumentStructureStrategy` in `Order`, so a host
adds a rung by registering one rather than by replacing the analyzer and losing the built-in four
with it. The built-in orders leave gaps — 100, 200, 300, 400 — so a rung can be placed between two
of them without renumbering anything.

A corpus with a convention nobody outside the business knows, a form series whose first line names
the section, a ledger split by its own rule: those are strategies belonging to that host. A strategy
that throws is logged and treated as having declined, because one rung failing is not the document's
fault and the rung below it may well have an answer.

Advertisements left the general path with this. A page carrying no article and no running head means
something in a magazine and nothing in a manual, so `SplitAdvertisements` runs only on the
table-of-contents rung rather than on every document that reaches `BuildArticles`.

### What real documents taught it

Running actual publications through the pipeline found two defects that no synthetic fixture would
ever have shown, because both come from how type is set rather than from how a document is
structured.

**A headline that wraps is one headline.** Its second line is another run of type at the same size
directly beneath the first, and the rule that lets a newspaper page carry four stories was reading
it as a fifth. The article was split in two, the first half titled with half a sentence, and the
pages given to the half that said least. Two lines now merge when they share a size, sit within
ordinary leading of each other, and overlap across the measure — all three, because same size alone
would swallow a standfirst and adjacency alone would swallow the first line of body text.

**Display type is routinely set twice.** A slight offset fakes a weight the font does not have, and
both runs are real text, so every word of the headline is read twice:
`COMMAND COMMAND & & CONQUER`. A word repeated immediately after itself is now collapsed, in
headings only.

Measured on a fifteen page research paper, a seventy page magazine and a twenty-three page trade
journal: structure came from the outline where one existed and from type size where none did, no
element in any of the three ended up belonging to no division, and figures, charts and tables were
separated and captioned. The two fixes took the magazines from thirty-nine and twenty-eight
divisions to twenty-seven and twenty.

### What is still open

**Covers and advertising pages still produce divisions.** Type-size inference has no way to tell a
masthead, a strapline or an advertisement from a headline, so the opening pages of a magazine yield
divisions that are not articles. The contents-page rung handles this properly where a contents page
can be read; where one cannot, this is the cost of inferring.

**Overprinted type can still fragment.** Where the two runs land far enough apart to segment
separately, they survive as two blocks, and the second becomes a short division of its own.
