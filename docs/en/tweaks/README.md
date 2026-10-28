<!-- source: docs/tweaks/README.md sha256: a3a8ce212807ba19bf55dfc7b91e60aa6044c06f164319b0b11faae19292e876 -->

# Catalog item documentation

Each catalog item has a file `docs/en/tweaks/<id>.md`, where `<id>` is exactly the `id` from the JSON in `src/DSOptimizer.Catalog/catalog/`
(the `Definitions_HaveDocsFile` test makes sure no item is left without a document). The file layout is fixed: the nine sections
below, in this order. This file is the template: copy the headings into a new `<id>.md` and replace the one-line instructions with content.

## Verdict for 26H2
One sentence: whether the item makes sense on Windows 11 26H2 (port / don't port / placebo) and why.

## What it changes
List exactly what is touched: registry keys (path, name, type, value), services, commands and scripts.
A panel or launcher with a `launch` field: the file and arguments verbatim (file rules: spec, section 4, A6).

## Sources
URLs with the access date and the source type (MS Learn, manufacturer documentation, test on a 26H2 VM, WinUtil code).

## Defaults on a fresh 26H2
Values read from a clean 26H2 Home and Pro VM, with the reading date (write a missing key explicitly as "none").

## Detection and undo
How the catalog detects the state (`Applied` / `NotApplied`) and how undo works; what is a snapshot and what is only the `original` fallback.

## Risks and warnings
Risk level, side effects and warnings shown to the user (consistent with the `risk` and `warnings` fields in the catalog).

## VM test
Result of the VM run: date, version (Home/Pro), `Passed` or `Skipped` with the reason.

## Observed effect
Evidence of a real effect (Stage 4a rule): Home and Pro separately, each with the observation method (screenshot, UIA, process, Settings),
result and date. Writing the value alone is not evidence. An item without an observed effect on a given edition gets `editions`
(`NotSupported` on the others); without any evidence it is dropped from the catalog.

## Reddit
Reports of problems and opinions about placebo on 24H2/25H2/26H2 (added in part E); until then: "to be added".
