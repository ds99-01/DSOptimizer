<!-- source: docs/tweaks/repair.dns-flush.md sha256: ef9fa2571fbd9f7dc82ea17b0227a3e1ed363b9564ee268d810bd67d31bb6ba5 -->

# repair.dns-flush – Clear the DNS cache

## Verdict for 26H2
Port as `kind=action`, `risk=safe` (new "Repairs" category, Stage 4b batch 1): clearing the name resolution cache (`Clear-DnsClientCache`) works on 26H2 Home and Pro, and the effect can be confirmed by verification (`Get-DnsClientCache`). No undo: the cache rebuilds itself.

## What it changes
Clears the DNS client cache (the `Dnscache` service). Doesn't change DNS settings, the `hosts` file, the registry or the adapter configuration. Script (`script.apply`), by command name (the tests mock them):
1. `Get-DnsClientCache` – reading before (the number of entries for the message).
2. `Clear-DnsClientCache`.
3. `Get-DnsClientCache` – verification.
4. `Get-DsoHostsText` – a helper that reads `%SystemRoot%\System32\drivers\etc\hosts` (read-only; mocked in the tests).

Success criterion (decision from the measurement, `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), section 1): every entry that stays in the cache after clearing is for a name from the `hosts` file (the words after the address, without `#` comments, without a trailing dot, case-insensitive) or for its reverse name (`*.in-addr.arpa`, `*.ip6.arpa`), of any type and status. An empty cache is also a success. Any other entry = `Failed` "The DNS cache wasn't cleared: N entries remain." Success: "Cleared the DNS cache (entries before: N, after: M – only entries from the hosts file)."

We don't count the drop in the number of entries: `Get-DnsClientCache` doesn't show public names resolved by `Resolve-DnsName` or by apps (measurement N1), so "before" is sometimes smaller than it seems, and a drop proves nothing.

## Sources
- Test on 26H2 Home and Pro VMs (build 26300.9457), 2026-10-22: `ipconfig /flushdns` (code 0) and `Clear-DnsClientCache` give the same cache state (runs H1, P1, A2, N1; `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), section 1).
- No MS documentation checked in this task (the cmdlet pages for `Clear-DnsClientCache`/`Get-DnsClientCache` weren't fetched); evidence: the VM test described above. The behavior "entries from `hosts` stay" comes only from the measurement.

## Defaults on a fresh 26H2
DNS cache on a fresh VM after clearing: Home 2 entries (A for the name from `hosts` and PTR `67.45.123.10.in-addr.arpa`, TTL about 604,700 s), Pro the same (reading 2026-10-22). With a network (N1, Pro): 3 entries, all from `hosts` (A, PTR and a negative AAAA with status 9501); `wpad` and the negative name disappear.

## Detection and undo
Not applicable: `kind=action` has no detection or undo (no journal). The evidence of the effect is the verification in the script: code 0 without it isn't enough. The cache rebuilds itself with the next queries.

## Risks and warnings
- `risk=safe`: the operation doesn't change any settings, and the cache rebuilds itself.
- Entries from `hosts` stay in the cache after clearing (normal Windows behavior), and the program counts them as cleared.
- Opening websites for the first time after clearing may be a little slower.
- The action doesn't repair the network and doesn't change the DNS servers.

## VM test
Case `repair-dns-flush` (`Flow=Execute`, `Check`: an independent `Get-DnsClientCache` read and the same "only entries from `hosts`" test): added to `tests/vm/cases.psd1` in Task 5.

VM run (Task 10, 2026-10-22, full `tests/vm/Invoke-VmTests.ps1` on the `etap4b-1` branch; reports outside git): Home and Pro 722 passed / 0 failed / 25 skipped each (6 cases with `Skip` outside the Repairs category). `repair-dns-flush`: **Passed** on Home and Pro – result `Executed` "Wyczyszczono pamięć DNS (przed: 2 wpisów, po: 2 – tylko wpisy z pliku hosts)" (Cleared the DNS cache (entries before: 2, after: 2 – only entries from the hosts file)), `Check` passed. In Task 10, `Setup` adds the test entry `10.123.45.67 dso-test-hosts.invalid` to `hosts` (as in the measurement), because without a network the plain `localhost`/`.invalid` queries gave "przed: 0 wpisów" (entries before: 0) (first run); `Cleanup` removes the entry with a retry (on Pro the `hosts` file was locked for a moment). Without a network, regular DNS answers don't get into the cache, so the VM doesn't show the removal of an entry from outside `hosts` – that remains to be observed on hardware.

## Observed effect
Method: reading `Get-DnsClientCache` before and after `Clear-DnsClientCache` (and `ipconfig /flushdns`), after resolving names (`Resolve-DnsName`) with the network connected; output captured in the raw results (outside git).
- **Home** (`DSO-26H2-Home`, 26300.9457, no network), 2026-10-22: after clearing (both methods) only 2 entries from `hosts` stay (A for the name and PTR). Effect: the cache is cleared except for the entries from `hosts`.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: without a network the same as Home (2 entries from `hosts`); with a network (`Default Switch`) 3 entries, all from `hosts`, while `wpad` and the negative entry disappear. Public names resolved by `Resolve-DnsName` (15–19 ms, a repeated query from the cache 1 ms) weren't visible in `Get-DnsClientCache` either before or after clearing – that's why the criterion isn't based on the drop in the number of entries.

## Reddit
To be added in part E.
