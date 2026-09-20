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

### What is still open

- **Genre-specific notions sit in the general path.** `KnowledgeArticleTypes.Advertisement` and
  section-banner capture are magazine concepts every document is measured against.
- **The rungs are private methods, not public strategies.** The ladder is explicit and adding one is
  a line, but a host cannot contribute a rung of its own.
- **Fixtures are synthetic.** Every test builds the document it reads — a bookmarked manual, a
  Word file with heading styles, a page carrying three stories. That proves the algorithm and proves
  nothing about a real magazine whose contents page is set as a picture, or a manual whose outline
  points at the wrong pages. Measuring against real documents is what would turn this from correct
  into trustworthy.
