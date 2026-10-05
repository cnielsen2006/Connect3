using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cascade.Core
{
    /// <summary>The kind of a <see cref="JsonValue"/>.</summary>
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A parsed JSON value (a minimal DOM for Core's own small formats: events and levels). Object
    /// members keep their order. Numbers keep their source text so integers never lose precision.
    /// </summary>
    public sealed class JsonValue
    {
        readonly string? _text; // string value or number text
        readonly bool _bool;
        readonly List<JsonValue>? _items;
        readonly List<KeyValuePair<string, JsonValue>>? _members;

        JsonValue(JsonKind kind, string? text = null, bool b = false, List<JsonValue>? items = null,
            List<KeyValuePair<string, JsonValue>>? members = null)
        {
            Kind = kind; _text = text; _bool = b; _items = items; _members = members;
        }

        public JsonKind Kind { get; }
        public bool IsNull => Kind == JsonKind.Null;

        public bool AsBool() => Kind == JsonKind.Bool ? _bool : throw new FormatException("JSON value is not a boolean");
        public string AsString() => Kind == JsonKind.String ? _text! : throw new FormatException("JSON value is not a string");

        /// <summary>The number as a 32-bit integer.</summary>
        public int AsInt() => checked((int)AsLong());

        /// <summary>The number as a 64-bit integer (a floating form such as 3.0 is accepted if integral).</summary>
        public long AsLong()
        {
            if (Kind != JsonKind.Number) throw new FormatException("JSON value is not a number");
            if (long.TryParse(_text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
            double d = double.Parse(_text!, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (d != Math.Floor(d)) throw new FormatException("JSON number is not an integer: " + _text);
            return (long)d;
        }

        public double AsDouble()
        {
            if (Kind != JsonKind.Number) throw new FormatException("JSON value is not a number");
            return double.Parse(_text!, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>A 64-bit unsigned integer written as decimal text in a string (or, for small values, as a number).</summary>
        public ulong AsU64()
        {
            if (Kind == JsonKind.String || Kind == JsonKind.Number)
                return ulong.Parse(_text!, NumberStyles.None, CultureInfo.InvariantCulture);
            throw new FormatException("JSON value is not a 64-bit integer");
        }

        public int Count => _items != null ? _items.Count : _members != null ? _members.Count : 0;

        /// <summary>Array element.</summary>
        public JsonValue this[int index] => _items != null ? _items[index] : throw new FormatException("JSON value is not an array");

        /// <summary>Array elements.</summary>
        public IReadOnlyList<JsonValue> Items => _items ?? throw new FormatException("JSON value is not an array");

        /// <summary>Object member by name; throws if missing.</summary>
        public JsonValue this[string name] => TryGet(name) ?? throw new FormatException("missing JSON member \"" + name + "\"");

        /// <summary>Object member by name, or null if missing.</summary>
        public JsonValue? TryGet(string name)
        {
            if (_members == null) throw new FormatException("JSON value is not an object");
            foreach (var kv in _members) if (kv.Key == name) return kv.Value;
            return null;
        }

        /// <summary>Parses a JSON document.</summary>
        public static JsonValue Parse(string text)
        {
            var p = new Parser(text);
            var v = p.ParseValue();
            p.SkipWs();
            if (!p.AtEnd) throw new FormatException("unexpected trailing JSON text at " + p.Pos);
            return v;
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;
            public Parser(string s) { _s = s; }
            public bool AtEnd => _i >= _s.Length;
            public int Pos => _i;

            public void SkipWs()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            char Peek() => _i < _s.Length ? _s[_i] : throw new FormatException("unexpected end of JSON");

            void Expect(char c)
            {
                if (Peek() != c) throw new FormatException("expected '" + c + "' at " + _i);
                _i++;
            }

            bool Literal(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return false;
                _i += word.Length;
                return true;
            }

            public JsonValue ParseValue()
            {
                SkipWs();
                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return new JsonValue(JsonKind.String, ParseString());
                    case 't': if (Literal("true")) return new JsonValue(JsonKind.Bool, b: true); break;
                    case 'f': if (Literal("false")) return new JsonValue(JsonKind.Bool, b: false); break;
                    case 'n': if (Literal("null")) return new JsonValue(JsonKind.Null); break;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        break;
                }
                throw new FormatException("unexpected JSON character '" + c + "' at " + _i);
            }

            JsonValue ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                return new JsonValue(JsonKind.Number, _s.Substring(start, _i - start));
            }

            string ParseString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    char c = Peek();
                    _i++;
                    if (c == '"') break;
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = Peek();
                    _i++;
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
                            if (_i + 4 > _s.Length) throw new FormatException("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new FormatException("bad escape \\" + e);
                    }
                }
                return sb.ToString();
            }

            JsonValue ParseArray()
            {
                Expect('[');
                var items = new List<JsonValue>();
                SkipWs();
                if (Peek() == ']') { _i++; return new JsonValue(JsonKind.Array, items: items); }
                while (true)
                {
                    items.Add(ParseValue());
                    SkipWs();
                    char c = Peek();
                    _i++;
                    if (c == ']') break;
                    if (c != ',') throw new FormatException("expected ',' or ']' at " + (_i - 1));
                }
                return new JsonValue(JsonKind.Array, items: items);
            }

            JsonValue ParseObject()
            {
                Expect('{');
                var members = new List<KeyValuePair<string, JsonValue>>();
                SkipWs();
                if (Peek() == '}') { _i++; return new JsonValue(JsonKind.Object, members: members); }
                while (true)
                {
                    SkipWs();
                    string name = ParseString();
                    SkipWs();
                    Expect(':');
                    members.Add(new KeyValuePair<string, JsonValue>(name, ParseValue()));
                    SkipWs();
                    char c = Peek();
                    _i++;
                    if (c == '}') break;
                    if (c != ',') throw new FormatException("expected ',' or '}' at " + (_i - 1));
                }
                return new JsonValue(JsonKind.Object, members: members);
            }
        }
    }
}
