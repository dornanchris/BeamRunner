using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BeamRunner.Core.Localization
{
    /// <summary>
    /// Minimal, strict JSON reader for the string tables (Core has no engine references, so no JsonUtility — which
    /// cannot read dictionaries anyway). Produces <see cref="Dictionary{TKey,TValue}"/> (objects, insertion order kept in
    /// <see cref="JsonObject.Keys"/>), <see cref="List{T}"/> (arrays), <see cref="string"/>, <see cref="double"/>,
    /// <see cref="bool"/> and null. Rejects trailing commas, comments, duplicate keys and anything after the root value,
    /// so a broken translation file fails loudly in tests and in the strings check instead of half-loading.
    /// </summary>
    public static class LocJson
    {
        /// <summary>A JSON object that remembers its key order (for stable diagnostics).</summary>
        public sealed class JsonObject
        {
            private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);
            private readonly List<string> _keys = new List<string>();

            public IReadOnlyList<string> Keys
            {
                get { return _keys; }
            }

            public int Count
            {
                get { return _keys.Count; }
            }

            public bool TryGet(string key, out object value)
            {
                return _values.TryGetValue(key, out value);
            }

            internal bool Add(string key, object value)
            {
                if (_values.ContainsKey(key))
                {
                    return false;
                }

                _values.Add(key, value);
                _keys.Add(key);
                return true;
            }
        }

        /// <summary>Parses <paramref name="text"/>. Throws <see cref="FormatException"/> with a line number on bad input.</summary>
        public static object Parse(string text)
        {
            if (text == null)
            {
                throw new FormatException("JSON: no text.");
            }

            var reader = new Reader(text);
            reader.SkipWhitespace();
            object value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw reader.Error("unexpected text after the root value");
            }

            return value;
        }

        /// <summary>Like <see cref="Parse"/> but returns false (and the reason) instead of throwing.</summary>
        public static bool TryParse(string text, out object value, out string error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (FormatException e)
            {
                value = null;
                error = e.Message;
                return false;
            }
        }

        private sealed class Reader
        {
            private const int MaxDepth = 16;
            private readonly string _s;
            private int _i;

            public Reader(string s)
            {
                _s = s;
                // A UTF-8 byte order mark decoded as U+FEFF is tolerated at the very start.
                _i = s.Length > 0 && s[0] == '﻿' ? 1 : 0;
            }

            public bool AtEnd
            {
                get { return _i >= _s.Length; }
            }

            public FormatException Error(string what)
            {
                int line = 1;
                int col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                {
                    if (_s[k] == '\n')
                    {
                        line++;
                        col = 1;
                    }
                    else
                    {
                        col++;
                    }
                }

                return new FormatException("JSON: " + what + " at line " + line.ToString(CultureInfo.InvariantCulture) +
                                           ", column " + col.ToString(CultureInfo.InvariantCulture) + ".");
            }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Error("nesting too deep");
                }

                if (AtEnd)
                {
                    throw Error("unexpected end of text");
                }

                char c = _s[_i];
                switch (c)
                {
                    case '{':
                        return ReadObject(depth);
                    case '[':
                        return ReadArray(depth);
                    case '"':
                        return ReadString();
                    case 't':
                        Expect("true");
                        return true;
                    case 'f':
                        Expect("false");
                        return false;
                    case 'n':
                        Expect("null");
                        return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ReadNumber();
                        }

                        throw Error("unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                {
                    throw Error("expected '" + word + "'");
                }

                _i += word.Length;
            }

            private JsonObject ReadObject(int depth)
            {
                var obj = new JsonObject();
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}')
                {
                    _i++;
                    return obj;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"')
                    {
                        throw Error("expected a key string");
                    }

                    int keyAt = _i;
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':')
                    {
                        throw Error("expected ':'");
                    }

                    _i++;
                    SkipWhitespace();
                    object value = ReadValue(depth + 1);
                    if (!obj.Add(key, value))
                    {
                        _i = keyAt;
                        throw Error("duplicate key \"" + key + "\"");
                    }

                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw Error("unterminated object");
                    }

                    char c = _s[_i++];
                    if (c == '}')
                    {
                        return obj;
                    }

                    if (c != ',')
                    {
                        _i--;
                        throw Error("expected ',' or '}'");
                    }
                }
            }

            private List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']')
                {
                    _i++;
                    return list;
                }

                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw Error("unterminated array");
                    }

                    char c = _s[_i++];
                    if (c == ']')
                    {
                        return list;
                    }

                    if (c != ',')
                    {
                        _i--;
                        throw Error("expected ',' or ']'");
                    }
                }
            }

            private string ReadString()
            {
                _i++; // opening quote
                StringBuilder sb = null;
                int start = _i;
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("unterminated string");
                    }

                    char c = _s[_i];
                    if (c == '"')
                    {
                        string result = sb == null ? _s.Substring(start, _i - start) : sb.Append(_s, start, _i - start).ToString();
                        _i++;
                        return result;
                    }

                    if (c < ' ')
                    {
                        throw Error("control character in string (use \\n)");
                    }

                    if (c != '\\')
                    {
                        _i++;
                        continue;
                    }

                    if (sb == null)
                    {
                        sb = new StringBuilder();
                    }

                    sb.Append(_s, start, _i - start);
                    _i++;
                    if (AtEnd)
                    {
                        throw Error("unterminated escape");
                    }

                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '/':
                            sb.Append('/');
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'u':
                        {
                            if (_i + 4 > _s.Length)
                            {
                                throw Error("short \\u escape");
                            }

                            int code;
                            if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                            {
                                throw Error("bad \\u escape");
                            }

                            sb.Append((char)code);
                            _i += 4;
                            break;
                        }

                        default:
                            _i--;
                            throw Error("unknown escape '\\" + e + "'");
                    }

                    start = _i;
                }
            }

            private double ReadNumber()
            {
                int start = _i;
                if (_s[_i] == '-')
                {
                    _i++;
                }

                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }

                double value;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    _i = start;
                    throw Error("bad number");
                }

                return value;
            }
        }
    }
}
