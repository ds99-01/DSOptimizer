using System;
using System.Text;

namespace DSOptimizer.Core.Repair;

/// <summary>
/// Dzieli surowe bajty konsoli na linie. Kodowanie ustala się z pierwszych bajtów: BOM UTF-16LE albo bajt zerowy
/// na drugiej pozycji (ASCII w UTF-16LE) = UTF-16LE, inaczej strona OEM z konstruktora. Linię kończy <c>\r</c>, <c>\n</c>
/// albo <c>\r\n</c> (paski postępu DISM i SFC rysuje się samym <c>\r</c>); puste linie nie są emitowane.
/// Znak rozcięty między buforami składa stanowy dekoder. Klasa nie jest bezpieczna wątkowo.
/// </summary>
public sealed class ConsoleLineSplitter
{
    private readonly Encoding _oem;
    private readonly StringBuilder _line = new();
    private byte[] _head = Array.Empty<byte>();
    private Decoder? _decoder;

    public ConsoleLineSplitter(Encoding oem) => _oem = oem ?? throw new ArgumentNullException(nameof(oem));

    /// <summary>Gotowa linia (bez znaków końca linii, niepusta).</summary>
    public event Action<string>? Line;

    public void Write(byte[] buffer, int count)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (count < 0 || count > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return;
        }

        if (_decoder is null)
        {
            var merged = new byte[_head.Length + count];
            Buffer.BlockCopy(_head, 0, merged, 0, _head.Length);
            Buffer.BlockCopy(buffer, 0, merged, _head.Length, count);
            if (merged.Length < 2)
            {
                _head = merged;
                return;
            }

            _head = Array.Empty<byte>();
            Start(merged);
            return;
        }

        Decode(buffer, 0, count);
    }

    /// <summary>Kończy strumień: emituje ostatnią linię bez końca linii.</summary>
    public void Flush()
    {
        if (_decoder is null && _head.Length > 0)
        {
            var head = _head;
            _head = Array.Empty<byte>();
            _decoder = _oem.GetDecoder();
            Decode(head, 0, head.Length);
        }

        if (_decoder is not null)
        {
            var chars = new char[8];
            var produced = _decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
            Append(chars, produced);
        }

        Emit();
    }

    private void Start(byte[] first)
    {
        var offset = 0;
        if (first[0] == 0xFF && first[1] == 0xFE)
        {
            _decoder = Encoding.Unicode.GetDecoder();
            offset = 2;
        }
        else if (first[1] == 0 && first[0] != 0)
        {
            _decoder = Encoding.Unicode.GetDecoder();
        }
        else
        {
            _decoder = _oem.GetDecoder();
        }

        Decode(first, offset, first.Length - offset);
    }

    private void Decode(byte[] bytes, int offset, int count)
    {
        var chars = new char[_decoder!.GetCharCount(bytes, offset, count)];
        var produced = _decoder.GetChars(bytes, offset, count, chars, 0);
        Append(chars, produced);
    }

    private void Append(char[] chars, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (chars[i] is '\r' or '\n')
            {
                Emit();
            }
            else
            {
                _line.Append(chars[i]);
            }
        }
    }

    private void Emit()
    {
        if (_line.Length == 0)
        {
            return;
        }

        var text = _line.ToString();
        _line.Clear();
        if (text.Trim().Length > 0)
        {
            Line?.Invoke(text);
        }
    }
}
