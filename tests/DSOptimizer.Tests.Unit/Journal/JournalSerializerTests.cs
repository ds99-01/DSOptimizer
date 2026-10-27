using System;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using Xunit;

namespace DSOptimizer.Tests.Unit.Journal;

public class JournalSerializerTests
{
    private static JournalDocument Sample()
    {
        var entry = new JournalEntry
        {
            EntryId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TweakId = "ux.menu-delay",
            UserSid = "S-1-5-21-1000",
            Status = JournalEntryStatus.PendingReboot,
            AppliedAt = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(2)),
            CatalogHash = "sha256:abc",
        };
        entry.Baseline.Add(SnapshotRecord.ForRegistry(@"HKU\S-1-5-21-1000\Control Panel\Desktop", "MenuShowDelay", RegValue.FromString("400")));
        entry.Baseline.Add(SnapshotRecord.ForRegistry(@"HKLM\X", "Q", RegValue.FromQWord(ulong.MaxValue)));
        entry.Baseline.Add(SnapshotRecord.ForRegistry(@"HKLM\X", "Gone", RegValue.Absent));
        entry.Baseline.Add(SnapshotRecord.ForService("SysMain", existed: true, ServiceStartType.AutomaticDelayed, wasRunning: true));
        entry.Baseline.Add(SnapshotRecord.ForScript("2"));
        entry.Events.Add(new JournalEvent { At = entry.AppliedAt, Action = "apply", Outcome = "Applied", Message = "ok" });
        var document = new JournalDocument { Machine = "PC-01" };
        document.Entries.Add(entry);
        return document;
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var json = JournalSerializer.Serialize(Sample());
        var entry = Assert.Single(JournalSerializer.Deserialize(json).Entries);

        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), entry.EntryId);
        Assert.Equal(JournalEntryStatus.PendingReboot, entry.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(2)), entry.AppliedAt);
        Assert.Equal(RegValue.FromString("400"), entry.Baseline[0].Value);
        Assert.Equal(RegValue.FromQWord(ulong.MaxValue), entry.Baseline[1].Value);
        Assert.Equal(RegValue.Absent, entry.Baseline[2].Value);
        Assert.Equal(ServiceStartType.AutomaticDelayed, entry.Baseline[3].StartType);
        Assert.True(entry.Baseline[3].WasRunning);
        Assert.Equal("2", entry.Baseline[4].Capture);
        Assert.Equal("ok", Assert.Single(entry.Events).Message);
        Assert.Equal(entry.AppliedAt, entry.Events[0].At);
    }

    [Fact]
    public void RoundTrip_DateLikeStrings_StayUnchanged()
    {
        var document = new JournalDocument { Machine = "PC" };
        var entry = new JournalEntry { TweakId = "a.b" };
        entry.Baseline.Add(SnapshotRecord.ForRegistry(@"HKLM\X", "S", RegValue.FromString("2026-10-16T10:00:00Z")));
        entry.Baseline.Add(SnapshotRecord.ForScript("2026-10-16T10:00:00+02:00"));
        document.Entries.Add(entry);

        var restored = Assert.Single(JournalSerializer.Deserialize(JournalSerializer.Serialize(document)).Entries);

        Assert.Equal(RegValue.FromString("2026-10-16T10:00:00Z"), restored.Baseline[0].Value);
        Assert.Equal("2026-10-16T10:00:00+02:00", restored.Baseline[1].Capture);
    }

    [Fact]
    public void Serialize_CreatedKeyRoot_RoundTrips()
    {
        var document = new JournalDocument { Machine = "PC" };
        var entry = new JournalEntry { TweakId = "a.b" };
        entry.Baseline.Add(SnapshotRecord.ForRegistry(@"HKLM\SOFTWARE\X\Y", "V", RegValue.Absent, @"HKLM\SOFTWARE\X"));
        entry.Baseline.Add(SnapshotRecord.ForRegistry(@"HKLM\SOFTWARE\X\Z", "V", RegValue.Absent));
        document.Entries.Add(entry);

        var json = JournalSerializer.Serialize(document);
        var restored = Assert.Single(JournalSerializer.Deserialize(json).Entries);

        Assert.Contains("\"createdKeyRoot\"", json, StringComparison.Ordinal);
        Assert.Equal(@"HKLM\SOFTWARE\X", restored.Baseline[0].CreatedKeyRoot);
        Assert.Null(restored.Baseline[1].CreatedKeyRoot);
    }

    [Fact]
    public void Deserialize_LegacyBaselineWithoutCreatedKeyRoot_KeepsNull()
    {
        const string json = "{ \"schemaVersion\": 1, \"machine\": \"x\", \"entries\": [ { \"tweakId\": \"a.b\", \"status\": \"Active\", "
            + "\"baseline\": [ { \"op\": \"Registry\", \"path\": \"HKLM\\\\X\\\\Y\", \"name\": \"V\", \"value\": { \"type\": \"DWord\", \"value\": 1 } } ] } ] }";
        var entry = Assert.Single(JournalSerializer.Deserialize(json).Entries);
        Assert.Null(Assert.Single(entry.Baseline).CreatedKeyRoot);
    }

    [Fact]
    public void Serialize_UsesCamelCaseAndEnumNames()
    {
        var json = JournalSerializer.Serialize(Sample());
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"PendingReboot\"", json, StringComparison.Ordinal);
        Assert.Contains("\"op\": \"Registry\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isActive", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KindAndTitle_TweakEntryOmitsThem_AppRemovalRoundTrips_SchemaStaysOne()
    {
        var document = new JournalDocument { Machine = "PC" };
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.Active });
        document.Entries.Add(new JournalEntry
        {
            TweakId = "appx:Microsoft.BingNews_8wekyb3d8bbwe",
            Kind = JournalEntry.AppRemovalKind,
            Title = "Wiadomości",
            Status = JournalEntryStatus.Active,
        });

        var json = JournalSerializer.Serialize(document);
        var back = JournalSerializer.Deserialize(json);

        Assert.Equal(1, back.SchemaVersion);
        Assert.Null(back.Entries[0].Kind);
        Assert.Null(back.Entries[0].Title);
        Assert.Equal("app-removal", back.Entries[1].Kind);
        Assert.Equal("Wiadomości", back.Entries[1].Title);
        Assert.Contains("\"kind\": \"app-removal\"", json, StringComparison.Ordinal);
        Assert.Equal(1, json.Split(new[] { "\"kind\"" }, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Deserialize_JournalWithoutKindAndTitle_LoadsUnchanged()
    {
        const string legacy = "{ \"schemaVersion\": 1, \"machine\": \"PC\", \"entries\": [ { \"entryId\": \"7d5f4c3e-1a2b-4c5d-8e9f-0a1b2c3d4e5f\", \"tweakId\": \"a.b\", \"userSid\": \"S-1-5-21-1\", \"status\": \"PendingReboot\", \"appliedAt\": \"2026-10-01T10:00:00+00:00\", \"catalogHash\": \"h\", \"baseline\": [], \"events\": [ { \"at\": \"2026-10-01T10:00:00+00:00\", \"action\": \"apply\", \"outcome\": \"PendingReboot\" } ] } ] }";

        var document = JournalSerializer.Deserialize(legacy);

        var entry = Assert.Single(document.Entries);
        Assert.Null(entry.Kind);
        Assert.Null(entry.Title);
        Assert.Equal("a.b", entry.TweakId);
        Assert.Equal(JournalEntryStatus.PendingReboot, entry.Status);
        Assert.Single(entry.Events);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{ \"schemaVersion\": 2, \"machine\": \"x\", \"entries\": [] }")]
    [InlineData("{ \"schemaVersion\": 1, \"machine\": \"x\", \"entries\": [ { \"baseline\": [ { \"op\": \"Registry\", \"value\": { \"type\": \"DWord\", \"value\": \"x\" } } ] } ] }")]
    public void Deserialize_BrokenInput_ThrowsJournalFormatException(string json)
        => Assert.Throws<JournalFormatException>(() => JournalSerializer.Deserialize(json));

    [Fact]
    public void FindActive_ReturnsLatestActiveEntry()
    {
        var document = new JournalDocument { Machine = "PC" };
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.Undone });
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.PendingReboot, UserSid = "latest" });
        document.Entries.Add(new JournalEntry { TweakId = "a.c", Status = JournalEntryStatus.Active });

        Assert.Equal("latest", document.FindActive("a.b")!.UserSid);
        Assert.Null(document.FindActive("a.d"));
    }

    [Fact]
    public void FindActive_UserScope_DifferentSid_ReturnsNull()
    {
        var document = new JournalDocument { Machine = "PC" };
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.Active, UserSid = "S-1-5-21-1" });

        Assert.Null(document.FindActive("a.b", "S-1-5-21-2"));
    }

    [Fact]
    public void FindActive_UserScope_SameSid_ReturnsEntry()
    {
        var document = new JournalDocument { Machine = "PC" };
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.Active, UserSid = "S-1-5-21-1" });

        Assert.NotNull(document.FindActive("a.b", "s-1-5-21-1"));
    }

    [Fact]
    public void FindActive_NullSid_IgnoresEntrySid()
    {
        var document = new JournalDocument { Machine = "PC" };
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.Active, UserSid = "S-1-5-21-1" });

        Assert.NotNull(document.FindActive("a.b"));
    }

    [Fact]
    public void Event_Reason_RoundTrips()
    {
        var document = new JournalDocument { Machine = "PC" };
        var entry = new JournalEntry { TweakId = "a.b" };
        entry.Events.Add(new JournalEvent { Action = "install-app", Outcome = "Failed", Message = "m", Reason = "Nie wykonano – powód" });
        document.Entries.Add(entry);

        var json = JournalSerializer.Serialize(document);
        var restored = Assert.Single(JournalSerializer.Deserialize(json).Entries);

        Assert.Contains("\"reason\":", json, StringComparison.Ordinal);
        Assert.Equal("Nie wykonano – powód", Assert.Single(restored.Events).Reason);
    }

    [Fact]
    public void Event_NoReason_OmitsKeyAndOldJsonReadsNull()
    {
        var document = new JournalDocument { Machine = "PC" };
        var entry = new JournalEntry { TweakId = "a.b" };
        entry.Events.Add(new JournalEvent { Action = "apply", Outcome = "Applied" });
        document.Entries.Add(entry);

        var json = JournalSerializer.Serialize(document);

        Assert.DoesNotContain("reason", json, StringComparison.Ordinal);
        Assert.Null(Assert.Single(Assert.Single(JournalSerializer.Deserialize(json).Entries).Events).Reason);
    }
}
