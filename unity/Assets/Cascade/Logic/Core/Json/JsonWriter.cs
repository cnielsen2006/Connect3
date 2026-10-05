using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cascade.Core
{
    /// <summary>
    /// A tiny compact JSON writer (no whitespace), culture-independent. Core uses it for events and
    /// levels so it needs no JSON library. Members are written in the order they are added.
    /// </summary>
    public sealed class JsonWriter
    {
        readonly StringBuilder _sb = new StringBuilder();
        readonly List<bool> _first = new List<bool>(); // per open container: nothing written yet
        bool _afterName;

        void Prefix()
        {
            if (_afterName) { _afterName = false; return; }
            if (_first.Count > 0)
            {
                if (_first[_first.Count - 1]) _first[_first.Count - 1] = false;
                else _sb.Append(',');
            }
        }

        public JsonWriter BeginObject() { Prefix(); _sb.Append('{'); _first.Add(true); return this; }
        public JsonWriter EndObject() { _first.RemoveAt(_first.Count - 1); _sb.Append('}'); return this; }
        public JsonWriter BeginArray() { Prefix(); _sb.Append('['); _first.Add(true); return this; }
        public JsonWriter EndArray() { _first.RemoveAt(_first.Count - 1); _sb.Append(']'); return this; }

        /// <summary>Writes an object member name; the next call writes its value.</summary>
        public JsonWriter Name(string name)
        {
            Prefix();
            WriteString(name);
            _sb.Append(':');
            _afterName = true;
            return this;
        }

        public JsonWriter Null() { Prefix(); _sb.Append("null"); return this; }
        public JsonWriter Value(bool v) { Prefix(); _sb.Append(v ? "true" : "false"); return this; }
        public JsonWriter Value(int v) { Prefix(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(long v) { Prefix(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(string? v)
        {
            if (v == null) return Null();
            Prefix();
            WriteString(v);
            return this;
        }

        /// <summary>A double in round-trip form; integral values keep a ".0" (round-trip form).</summary>
        public JsonWriter Value(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("JSON cannot hold " + v);
            Prefix();
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0) s += ".0";
            _sb.Append(s);
            return this;
        }

        /// <summary>A 64-bit unsigned integer as decimal text in a JSON string (TRD §4.3: JSON numbers lose precision above 2^53).</summary>
        public JsonWriter ValueU64AsString(ulong v) => Value(v.ToString(CultureInfo.InvariantCulture));

        public JsonWriter Member(string name, int v) => Name(name).Value(v);
        public JsonWriter Member(string name, string? v) => Name(name).Value(v);

        void WriteString(string s)
        {
            _sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }

        public override string ToString() => _sb.ToString();
    }
}
