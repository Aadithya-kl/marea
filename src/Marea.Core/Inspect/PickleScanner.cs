using System.Text;

namespace Marea.Core;

/// <summary>Result of reading the opcodes of one or more pickle streams.</summary>
/// <param name="Imports">Every "module.name" the pickle would import when loaded.</param>
/// <param name="Complete">False when the stream could not be read to the end.</param>
public sealed record PickleReport(IReadOnlyList<(string Module, string Name)> Imports, bool Complete);

/// <summary>Lists what a pickle would import, by walking its opcodes. Nothing is ever unpickled.</summary>
/// <remarks>Loading a pickle can call any imported function: that is how malicious checkpoints run
/// code. Like picklescan, this tracks GLOBAL/STACK_GLOBAL (resolving memoised strings) and
/// rates the imports. It is a strong signal, not a proof: a determined attacker may hide calls, so
/// "no code found" means "nothing but standard tensor rebuilding was found".</remarks>
public static class PickleScanner
{
    private const long MaxArg = 64L << 20;
    private const int MaxOps = 50_000_000;

    /// <summary>Scans consecutive pickles from <paramref name="s"/> (legacy torch files hold several).</summary>
    public static PickleReport Scan(Stream s, int maxPickles = 8)
    {
        var imports = new List<(string, string)>();
        try
        {
            for (var i = 0; i < maxPickles; i++)
            {
                var first = s.ReadByte();
                if (first != 0x80 && i > 0)
                {
                    break; // raw tensor data follows the pickles
                }
                if (first < 0)
                {
                    break;
                }
                ScanOne(s, first, imports);
            }
            return new PickleReport(imports, true);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or OverflowException)
        {
            return new PickleReport(imports, false);
        }
    }

    private static void ScanOne(Stream s, int op, List<(string, string)> imports)
    {
        var memo = new Dictionary<long, string?>();
        var strings = new List<string>();
        string? last = null;
        for (var n = 0; n < MaxOps; n++, op = Next(s))
        {
            string? pushed = null;
            var isString = false;
            switch (op)
            {
                case '.': // STOP
                    return;
                case 0x80: Skip(s, 1); break; // PROTO
                case 0x95: Skip(s, 8); break; // FRAME
                case 'K': case 'h': case 'q': case 0x82: // BININT1, BINGET, BINPUT, EXT1
                    {
                        var v = Next(s);
                        if (op == 'h')
                        {
                            pushed = memo.GetValueOrDefault(v);
                            isString = pushed is not null;
                        }
                        else if (op == 'q')
                        {
                            memo[v] = last;
                        }
                        break;
                    }
                case 'M': case 0x83: Skip(s, 2); break; // BININT2, EXT2
                case 'J': case 0x84: Skip(s, 4); break; // BININT, EXT4
                case 'j': // LONG_BINGET
                    pushed = memo.GetValueOrDefault(ReadInt(s, 4));
                    isString = pushed is not null;
                    break;
                case 'r': memo[ReadInt(s, 4)] = last; break; // LONG_BINPUT
                case 0x94: memo[memo.Count] = last; break; // MEMOIZE
                case 'G': Skip(s, 8); break; // BINFLOAT
                case 'U': case 0x8c: pushed = ReadText(s, ReadInt(s, 1)); isString = true; break; // SHORT_BINSTRING, SHORT_BINUNICODE
                case 'T': case 'X': pushed = ReadText(s, ReadInt(s, 4)); isString = true; break; // BINSTRING, BINUNICODE
                case 0x8d: pushed = ReadText(s, ReadInt(s, 8)); isString = true; break; // BINUNICODE8
                case 'C': case 0x8a: Skip(s, ReadInt(s, 1)); break; // SHORT_BINBYTES, LONG1
                case 'B': case 0x8b: Skip(s, ReadInt(s, 4)); break; // BINBYTES, LONG4
                case 0x8e: case 0x96: Skip(s, ReadInt(s, 8)); break; // BINBYTES8, BYTEARRAY8
                case 'S': case 'V': pushed = ReadLine(s).Trim('\'', '"'); isString = true; break; // STRING, UNICODE
                case 'I': case 'L': case 'F': case 'P': ReadLine(s); break; // INT, LONG, FLOAT, PERSID
                case 'g': pushed = memo.GetValueOrDefault(long.TryParse(ReadLine(s), out var g) ? g : -1); isString = pushed is not null; break;
                case 'p': memo[long.TryParse(ReadLine(s), out var p) ? p : -1] = last; break;
                case 'c': case 'i': // GLOBAL, INST: "module\nname\n"
                    imports.Add((ReadLine(s), ReadLine(s)));
                    break;
                case 0x93: // STACK_GLOBAL: module and name are the two most recent strings
                    imports.Add(strings.Count >= 2 ? (strings[^2], strings[^1]) : ("?", "?"));
                    break;
                case '(': case '0': case '1': case '2': case 'N': case 'R': case 'b': case 'a': case 'e':
                case 'd': case '}': case 'l': case ']': case 'o': case 's': case 'u': case 't': case ')':
                case 'Q': case 0x81: case 0x85: case 0x86: case 0x87: case 0x88: case 0x89: case 0x8f:
                case 0x90: case 0x91: case 0x92: case 0x97: case 0x98:
                    break; // opcodes without an argument
                default:
                    throw new InvalidDataException($"unknown pickle opcode 0x{op:x2}");
            }
            if (isString && pushed is not null)
            {
                strings.Add(pushed);
            }
            if (op is not (0x94 or 'q' or 'r' or 'p' or 0x95 or 0x80))
            {
                last = isString ? pushed : null;
            }
        }
        throw new InvalidDataException("pickle too long");
    }

    private static int Next(Stream s)
    {
        var b = s.ReadByte();
        return b >= 0 ? b : throw new EndOfStreamException();
    }

    private static long ReadInt(Stream s, int bytes)
    {
        long v = 0;
        for (var i = 0; i < bytes; i++)
        {
            v |= (long)Next(s) << (8 * i);
        }
        return v;
    }

    private static void Skip(Stream s, long n)
    {
        if (n < 0 || n > MaxArg)
        {
            throw new InvalidDataException("argument too large");
        }
        for (; n > 0; n--)
        {
            Next(s);
        }
    }

    private static string ReadText(Stream s, long n)
    {
        if (n < 0 || n > MaxArg)
        {
            throw new InvalidDataException("string too large");
        }
        var buf = new byte[n];
        s.ReadExactly(buf);
        return Encoding.UTF8.GetString(buf);
    }

    private static string ReadLine(Stream s)
    {
        var sb = new StringBuilder();
        for (int b; (b = Next(s)) != '\n';)
        {
            if (sb.Length > 4096)
            {
                throw new InvalidDataException("line too long");
            }
            sb.Append((char)b);
        }
        return sb.ToString().TrimEnd('\r');
    }
}
