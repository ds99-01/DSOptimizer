# Security policy

**English** | [Polski](#polski)

DSOptimizer runs with administrator rights and changes Windows settings, so security reports are taken seriously.
It is maintained by one person in their spare time; the notes below say honestly what you can expect.

## Supported versions

Only the [latest release](https://github.com/ds99-01/DSOptimizer/releases/latest) receives fixes. A fix is published
as a new release; older versions are not patched. If you can, check that the problem still occurs in the latest release
before reporting it.

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Send an e-mail to [security@dsoptimizer.com](mailto:security@dsoptimizer.com), in English or Polish. Include:

- the DSOptimizer version and whether you used the zip or the installer;
- the Windows 11 edition and build (`winver`);
- what an attacker can do and under which conditions (for example: a standard user without administrator rights,
  a file planted next to the program, a modified folder);
- steps to reproduce, and a proof of concept if you have one.

Remove personal data (user names, computer names, disk serial numbers, paths with your name) from logs and reports
before sending them.

## What to expect

- Every report is read, and I try to reply to each one that describes a real problem. There is no fixed deadline:
  this is a one-person project, and replies and fixes may take some time.
- If the problem is confirmed, it is fixed in a new release, and the release notes say what was fixed. Tell me whether
  you want to be credited and under which name.
- Please give me a reasonable time to release a fix before you publish details.

## Scope

In scope: the DSOptimizer program in this repository (the window and command-line mode), its catalog of items and
scripts, the zip and the installer published in [Releases](https://github.com/ds99-01/DSOptimizer/releases).

Examples of what counts: a way for a standard user to gain administrator rights through the program, running code from
an untrusted location, a change that does not match what the program shows, the program weakening Defender, UAC or
SmartScreen, or a downloaded file used without checking it.

Out of scope here:

- the dsoptimizer.com website and the autounattend generator published there: that generator is a separate tool and is
  not part of this repository (problems with the website can still go to the same address);
- third-party programs the program installs through winget or opens (for example WinUtil or RustDesk): please report
  those to their authors;
- the missing digital signature and the SmartScreen warning: this is known and described in the
  [README](README.md#download).

## Polski

Problemu z bezpieczeństwem nie zgłaszaj publicznie w Issues. Napisz na
[security@dsoptimizer.com](mailto:security@dsoptimizer.com), po polsku albo po angielsku: wersja programu, zip czy
instalator, wydanie i build Windows 11 (`winver`), co może zrobić atakujący i jak to powtórzyć. Przed wysłaniem usuń
z dziennika i raportu dane osobowe (nazwy kont i komputera, numery seryjne dysków).

Poprawki dostaje tylko [najnowsze wydanie](https://github.com/ds99-01/DSOptimizer/releases/latest). Program rozwija
jedna osoba, więc nie ma stałego terminu odpowiedzi ani poprawki; potwierdzony problem zostanie naprawiony w nowym
wydaniu. Strona dsoptimizer.com i generator autounattend to osobne narzędzia, poza tym repozytorium.
