using System;
using System.Runtime.InteropServices;
using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

#pragma warning disable CA2201 // COMException jest tu celowo: tak zgłasza błąd schowka WPF (CLIPBRD_E_CANT_OPEN)
public class ClipboardCopyTests
{
    /// <summary>CLIPBRD_E_CANT_OPEN – schowek otwarty przez inny proces (np. RustDesk).</summary>
    private const int ClipboardCantOpen = unchecked((int)0x800401D0);

    private sealed class FakeClipboard : IClipboardText
    {
        public string? Content { get; set; }

        public Exception? SetError { get; set; }

        /// <summary>SetText zostawia tekst w schowku mimo wyjątku (jak przy RustDesk).</summary>
        public bool KeepsTextOnError { get; set; }

        public Exception? GetError { get; set; }

        public int SetCalls { get; private set; }

        public void SetText(string text)
        {
            SetCalls++;
            if (SetError is null || KeepsTextOnError)
            {
                Content = text;
            }

            if (SetError is not null)
            {
                throw SetError;
            }
        }

        public string? GetText() => GetError is null ? Content : throw GetError;
    }

    private static COMException CantOpen() => new("OpenClipboard nie powiodło się", ClipboardCantOpen);

    [Fact]
    public void Copy_SetTextSucceeds_CopiedWithoutError()
    {
        var clipboard = new FakeClipboard();

        var result = ClipboardCopy.Copy(clipboard, "tekst");

        Assert.True(result.Copied);
        Assert.Null(result.Error);
        Assert.Equal("tekst", clipboard.Content);
    }

    [Fact]
    public void Copy_SetTextThrowsButTextIsInClipboard_CopiedWithRecoveredError()
    {
        var error = CantOpen();
        var clipboard = new FakeClipboard { SetError = error, KeepsTextOnError = true };

        var result = ClipboardCopy.Copy(clipboard, "tekst");

        Assert.True(result.Copied);
        Assert.Same(error, result.Error);
        Assert.Equal(1, clipboard.SetCalls);
    }

    [Fact]
    public void Copy_SetTextThrowsAndClipboardHasOtherText_FailedWithError()
    {
        var error = CantOpen();
        var clipboard = new FakeClipboard { SetError = error, Content = "stary tekst" };

        var result = ClipboardCopy.Copy(clipboard, "tekst");

        Assert.False(result.Copied);
        Assert.Same(error, result.Error);
        Assert.Equal(1, clipboard.SetCalls);
    }

    [Fact]
    public void Copy_SetTextThrowsAndClipboardEmpty_Failed()
    {
        var clipboard = new FakeClipboard { SetError = CantOpen() };

        var result = ClipboardCopy.Copy(clipboard, "tekst");

        Assert.False(result.Copied);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Copy_ReadBackAlsoBlocked_FailedWithSetError()
    {
        var error = CantOpen();
        var clipboard = new FakeClipboard { SetError = error, KeepsTextOnError = true, GetError = CantOpen() };

        var result = ClipboardCopy.Copy(clipboard, "tekst");

        Assert.False(result.Copied);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Copy_ClipboardHasTextWithDifferentLineEndings_Copied()
    {
        var clipboard = new FakeClipboard { SetError = CantOpen(), Content = "a\nb" };

        var result = ClipboardCopy.Copy(clipboard, "a\r\nb");

        Assert.True(result.Copied);
    }

    [Fact]
    public void Copy_NonClipboardExceptionFromSet_Propagates()
    {
        var clipboard = new FakeClipboard { SetError = new InvalidOperationException("nie STA") };

        Assert.Throws<InvalidOperationException>(() => ClipboardCopy.Copy(clipboard, "tekst"));
    }

    [Fact]
    public void Copy_NonClipboardExceptionFromReadBack_Propagates()
    {
        var clipboard = new FakeClipboard { SetError = CantOpen(), GetError = new InvalidOperationException("nie STA") };

        Assert.Throws<InvalidOperationException>(() => ClipboardCopy.Copy(clipboard, "tekst"));
    }

    [Fact]
    public void RecoveredMessage_ContainsHResultAndReason()
    {
        var message = ClipboardCopy.RecoveredMessage(CantOpen());

        Assert.Contains("0x800401D0", message, StringComparison.Ordinal);
        Assert.Contains("OpenClipboard nie powiodło się", message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => ClipboardCopy.RecoveredMessage(null!));
    }

    [Fact]
    public void Copy_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ClipboardCopy.Copy(null!, "tekst"));
        Assert.Throws<ArgumentNullException>(() => ClipboardCopy.Copy(new FakeClipboard(), null!));
    }
}
