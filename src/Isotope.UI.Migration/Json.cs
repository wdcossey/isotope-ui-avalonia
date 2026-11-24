using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Isotope.UI.Migration;

/// <summary>
/// A small JSON reader for the embedded tables: objects become <see cref="Dictionary{TKey,TValue}"/>, arrays
/// <see cref="List{T}"/>, numbers <see cref="double"/>. An analyzer can't bring System.Text.Json into the compiler on
/// netstandard2.0 without shipping it, and the tables need nothing more.
/// </summary>
internal static class Json
{
    public static object? Parse(string text)
    {
        var i = 0;
        var value = ReadValue(text, ref i);
        SkipWhitespace(text, ref i);
        if (i != text.Length) throw Error(text, i, "text after the value");
        return value;
    }

    static object? ReadValue(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        if (i >= s.Length) throw Error(s, i, "a value");
        switch (s[i])
        {
            case '{': return ReadObject(s, ref i);
            case '[': return ReadArray(s, ref i);
            case '"': return ReadString(s, ref i);
            case 't': Expect(s, ref i, "true"); return true;
            case 'f': Expect(s, ref i, "false"); return false;
            case 'n': Expect(s, ref i, "null"); return null;
            default: return ReadNumber(s, ref i);
        }
    }

    static Dictionary<string, object?> ReadObject(string s, ref int i)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        i++;
        SkipWhitespace(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return result; }
        while (true)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length || s[i] != '"') throw Error(s, i, "a property name");
            var key = ReadString(s, ref i);
            SkipWhitespace(s, ref i);
            if (i >= s.Length || s[i] != ':') throw Error(s, i, "':'");
            i++;
            result[key] = ReadValue(s, ref i);
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; return result; }
            throw Error(s, i, "',' or '}'");
        }
    }

    static List<object?> ReadArray(string s, ref int i)
    {
        var result = new List<object?>();
        i++;
        SkipWhitespace(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return result; }
        while (true)
        {
            result.Add(ReadValue(s, ref i));
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; return result; }
            throw Error(s, i, "',' or ']'");
        }
    }

    static string ReadString(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++;
        while (i < s.Length)
        {
            var c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            var e = s[i++];
            switch (e)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 > s.Length) throw Error(s, i, "four hex digits");
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: throw Error(s, i - 1, "an escape");
            }
        }
        throw Error(s, i, "the end of the string");
    }

    static double ReadNumber(string s, ref int i)
    {
        var start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        if (start == i) throw Error(s, i, "a value");
        return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static void Expect(string s, ref int i, string word)
    {
        if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error(s, i, "'" + word + "'");
        i += word.Length;
    }

    static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static FormatException Error(string s, int i, string expected) =>
        new FormatException("JSON: expected " + expected + " at offset " + i.ToString(CultureInfo.InvariantCulture) + ".");
}
