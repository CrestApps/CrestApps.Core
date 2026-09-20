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

## Proposed shape

Replace the single analyzer with a ladder of strategies behind the existing
`IDocumentStructureAnalyzer`, each one authoritative over the next, each degrading to the one below.
The contract that nothing may fail an ingest is kept: the last rung is still one article.

| Rung | Source | Authority | Covers |
| --- | --- | --- | --- |
| 1 | Tagged content roles | stated | accessible PDFs of any genre |
| 2 | PDF outline / bookmarks | stated | manuals, reports, books, most white papers |
| 3 | Provider roles (Document Intelligence) | stated | scans, and anything the service reads |
| 4 | Table of contents | stated, fuzzy-matched | magazines, journals |
| 5 | Heading inference | inferred | anything with type-size contrast |
| 6 | Single article | none | the answer that is never wrong |

Rung 5 is where newspapers are won or lost, and it needs two changes: allow **several boundaries per
page** rather than one, and use the block geometry the reader already produces to order them by
column rather than by height alone.

### Model changes

- `DocumentStructure` becomes a tree. A node carries a title, a depth, an ordinal, a page range and
  its children. An article is a depth-1 node; a manual's chapter and its procedures are depth 1 and 2.
- A `section` value joins `KnowledgeObjectTypes`. This is additive — the existing values are written
  into stored rows and read back verbatim, so they are pinned, but adding one is safe.
- Genre-specific notions leave the general path. `Advertisement` and section-banner capture belong to
  a magazine strategy, not to every document.

### Keeping it honest

Each strategy reports what it relied on, so a wrong split can be explained rather than guessed at,
and `DocumentStructure.IsInferred` grows into "which rung answered". Fixtures for each genre —
magazine, newspaper, research paper, user manual, technical report — are what make this measurable
instead of anecdotal; the current behaviour was measured against one document and silently produced
a single article for everything laid out differently.
