# Contributing to DSOptimizer

**English** | Polski: zgłoszenia i propozycje możesz pisać po polsku; zasady poniżej obowiązują tak samo.

DSOptimizer is maintained by one person. Bug reports, corrections and well-sourced suggestions help the most.

## Reporting bugs and ideas

- Use the [bug report](https://github.com/ds99-01/DSOptimizer/issues/new?template=bug_report.yml) or
  [suggestion](https://github.com/ds99-01/DSOptimizer/issues/new?template=feature_request.yml) form. The program version,
  the Windows 11 edition and build, and the interface language make a bug possible to reproduce.
- Remove personal data from logs and reports before attaching them (user and computer names, SIDs, disk serial numbers).
- Security problems: do not open an issue, see [SECURITY.md](SECURITY.md).
- Questions: [Discussions](https://github.com/ds99-01/DSOptimizer/discussions).

## Building from source

See [Building from source](docs/readme/cli.md#building-from-source): the .NET SDK from `global.json`, then
`dotnet build DSOptimizer.sln -c Release` and `dotnet test DSOptimizer.sln -c Release` (some tests require PowerShell
running as administrator). Warnings are treated as errors, so a build with a warning fails.

CI on every pull request runs the build, the unit tests (with at least 90% line coverage of `DSOptimizer.Core`) and the
Windows tests.

## Rules for catalog items

The items are defined in JSON files in `src/DSOptimizer.Catalog/catalog/`.

- **Every item has a description and a detection.** A setting (`kind=tweak`) detects the real state on the computer
  (`Applied` / `Not applied`) and has Undo, or is marked `reversible=false` with a clear warning. Success means the
  detection returns `Applied` after the change, not that a script ran.
- **Every item has a document** `docs/tweaks/<id>.md` (Polish, the original) and `docs/en/tweaks/<id>.md` (English), with
  the sections from the template in [docs/en/tweaks/README.md](docs/en/tweaks/README.md): what it changes, sources,
  defaults, detection and undo, risks, and the observed effect.
- **Evidence of a real effect.** An item enters the catalog when a test on Windows 11 shows a real effect (in Settings,
  the interface, a process or a result), not only a written value. Do not guess registry keys, services or commands;
  each one needs a source.
- **Both languages.** Interface text is translated in the program; README and documents change in English and Polish
  together.

## Pull requests that are welcome

- fixes for a reproducible bug, with a test where possible;
- corrections to item documents, with a source;
- improvements to the English or Polish text;
- new catalog items that meet the rules above.

For a larger change, open an issue first, so the idea can be discussed before you spend time on it.

Contributions are accepted under the [Mozilla Public License 2.0](LICENSE), the license of this repository. The
DSOptimizer name and the DŚO mark are not covered by the license.
