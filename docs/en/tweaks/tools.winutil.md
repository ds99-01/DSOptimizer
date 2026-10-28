<!-- source: docs/tweaks/tools.winutil.md sha256: 9ebf1bab1cef10efa7e85681ddbc1ab9be23988906cb4eeb034947f4ab367bda -->

# tools.winutil – Open WinUtil (Chris Titus)

## Verdict for 26H2
Port as `kind=action`, `risk=caution`, with the C# handler `winutil` (Stage 5, spec 3.3, decision K5; previously a thin launcher from batch C5, decision D5): the item downloads the official `winutil.ps1` from the latest stable GitHub release, checks its SHA256 and opens it in a separate PowerShell window. It does nothing more. This is someone else's code from the internet, run as admin, and DSOptimizer doesn't assess its content.

This is the **second exception** to the rule "the program doesn't download anything by itself" (the first: RustDesk from GitHub). Scope: one file (`winutil.ps1`), one repo (`ChrisTitusTech/winutil`), one source (`releases/latest`); the rules are in the code (`src/DSOptimizer.Core/Tools/WinUtilSource.cs`), not in JSON.

## What it changes
DSOptimizer itself changes nothing in the system apart from the script file in the downloads folder. Handler `WinUtilHandler` (`src/DSOptimizer.Core/Tools/WinUtilHandler.cs`):
1. `GET https://api.github.com/repos/ChrisTitusTech/winutil/releases/latest` (15 s limit, `Accept: application/vnd.github+json`).
2. `WinUtilSource.TryParseRelease`: no `prerelease`/`draft`, a tag made of digits (`26.10.07`), exactly one asset `winutil.ps1`, `size` ≤ 5 MB, `digest` `sha256:` (without it = refusal), duplicate JSON keys = a bad response.
3. `WinUtilSource.IsAllowedDownloadUrl`: https, host `github.com`, path exactly `/ChrisTitusTech/winutil/releases/download/<tag>/winutil.ps1`, without `%`, a query or a fragment.
4. Download to `%ProgramData%\DSOptimizer\downloads\<guid>.ps1` (trusted ACL, the same folder as RustDesk) through `WebDownloader`: redirects only to `release-assets.githubusercontent.com`/`objects.githubusercontent.com` over https, 5 MB and 2 min limits.
5. The SHA256 of the file on disk, computed right before the start = the `digest` from the API (a mismatch = the file is deleted, nothing starts).
6. `powershell.exe -NoExit -NoProfile -ExecutionPolicy RemoteSigned -File "<file>"` through `ShellProcessLauncher` (CreateProcess with the fixed path of PowerShell 5.1, outside the Job Object, without waiting). The window inherits DSOptimizer's elevation (manifest `requireAdministrator`).

Every failure after the path is created deletes the file. After the start the file stays (PowerShell reads it after starting, decision K4) and is deleted at the next download (WinUtil or RustDesk) once it's more than 24 h old (cleanup of orphaned downloads in `DownloadFolder.NewDownloadPath`; without a next download the file stays). The program log gets an INFO line: tag, path, bytes, SHA256. Before the download, the handler reports (action output, `IProgress<string>`) one progress line "Downloading WinUtil <tag> from GitHub…".

Results (exact texts in the handler): success "Opened the WinUtil <tag> window (file from the GitHub release, SHA256 matches)."; no network or after the time limit "Not done – no connection to GitHub."; HTTP 403/429 "Not done – GitHub is temporarily refusing requests (rate limit)."; other codes, a rejected redirect and a download address outside the pattern from point 3 (nothing downloaded) "Not done – GitHub returned an unexpected response."; refusal by the release rules "Not done – <reason>."; no asset "Not done – the latest WinUtil release has no winutil.ps1 file."; file over the limit or wrong SHA256 "Not done – the downloaded file didn't pass the check."; error starting the window "Not done – couldn't open the PowerShell window: <message>." All failures = `Failed`.

What WinUtil changes depends on the release and on what you click in its window; the item doesn't promise it.
We don't use: `-EncodedCommand`, the development branch or the preset variant (`-Preset`). The WinUtil code isn't in the repo.

## Sources
- WinUtil releases on GitHub: https://github.com/ChrisTitusTech/winutil/releases (API `releases/latest`); on the day of the measurement (2026-10-26) tag `26.10.07`, `winutil.ps1` 927 798 B, `digest` field with SHA256, the file has no Authenticode signature (`Get-AuthenticodeSignature` = `NotSigned`), so we don't verify a signature – the SHA256 from the API remains.
- WinUtil README (https://raw.githubusercontent.com/ChrisTitusTech/winutil/main/README.md), "Quick Start" section: "WinUtil must be run as Administrator because it performs system-wide changes." The former command from the README (`irm … | iex`, batch C5) was replaced with the file from the release; the command text disappeared from the catalog (spec 3.3 point 4 – a known pattern caught by scanners).
- WinUtil license: MIT, "Copyright (c) 2022 CT Tech Group LLC". We don't copy the code, so there's no entry in `THIRD_PARTY_NOTICES.md`.
- v3.2: `tool_winutil` (the previous, non-public version 3.2); the v3.2 tip "Avoid the 'Tweaks' tab on LTSC!" has no source, so it wasn't carried over.
- Stage 5 measurement, Task 1 (2026-10-26, Home and Pro VMs with a network and current Defender signatures 1.461.204.0): `docs/zrodla/2026-10-26-etap5-pomiar-winutil.md` (in Polish). Variant B (`winutil.ps1` from the release, SHA256 = `digest`, `-ExecutionPolicy RemoteSigned -File`) opens the "WinUtil 26.10.07" window with the GUI on both editions, zero Defender detections; hence `WinUtilSource.ExecutionPolicy = "RemoteSigned"`. The former `irm … | iex` wasn't blocked under these conditions either – the change stays, because it removes a known pattern and gives an honest message without a network.
- Batch C5 (former launcher): `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish).

## Defaults on a fresh 26H2
Not applicable: the item doesn't read or write settings. The system state after running it is the PowerShell window process and the `<guid>.ps1` file in `%ProgramData%\DSOptimizer\downloads` (the folder doesn't exist on a clean VM; it's created at the first download).

## Detection and undo
Not applicable: `kind=action` without detection and undo. `Executed` means the file passed the check and the PowerShell process started; DSOptimizer doesn't check or undo the effect of WinUtil.

## Risks and warnings
- Warnings in the catalog (the gist of the first: you run someone else's code from the internet as admin): "You run someone else's code from the internet as admin – DSOptimizer doesn't check what the script contains." and "DSOptimizer doesn't track or undo changes made in the WinUtil window."
- DSOptimizer doesn't check the content – SHA256 only confirms that the file matches the GitHub release, not that the code is safe; every new release may work differently.
- Requires a network connection; without it nothing opens (previously the window opened with an `irm` error).
- No presets and no development branch. Changes made in the WinUtil window aren't undone by DSOptimizer.

## VM test
- `tools-winutil-offline` (`Flow=Execute`, `Catalog=BuiltIn`, VM without a network): expected `Failed` with code 1 and the message "brak połączenia z GitHubem" (no connection to GitHub); `Check` = no `powershell.exe` with `-File` pointing to `C:\ProgramData\DSOptimizer\downloads\*.ps1` and no `*.ps1` files in `downloads`. Result of the run (`Invoke-VmTests.ps1`, VM without a network, HEAD `fb4bb9c`, 2026-10-26): **Passed on both editions** – Pro 799 / 0 / 25 skipped, Home 799 / 0 / 25 (`tools-winutil-offline` green on Pro and Home).
- `tools-winutil-effect`: **Skipped** (reason: the effect with a network is covered by the `av` phase of the E5 exploration). The WinUtil window and GUI with a network were measured in Task 1 (above, variant B).
- The former case `tools-winutil-launch` (batch C5, 2026-10-19, Passed on Home and Pro) was removed together with the `irm … | iex` command.

## Reddit
To be added in part E.
