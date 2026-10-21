using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SwanUnderwater
{
    // Bounded, dependency-free JSON for IL2CPP and offline tests. Reject duplicate keys.
    public static class OceanLeaderboardJson
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public static object Parse(string text)
        {
            if (text == null || text.Length > 20000000) throw new FormatException("Leaderboard JSON exceeds bound.");
            return new JsonReader(text).Read();
        }
        public static string Write(object value) { var b = new StringBuilder(); WriteValue(b, value); return b.ToString(); }
        static void WriteValue(StringBuilder b, object value)
        {
            if (value == null) { b.Append("null"); return; }
            if (value is string s) { String(b, s); return; }
            if (value is bool flag) { b.Append(flag ? "true" : "false"); return; }
            if (value is IDictionary<string, object> map)
            {
                b.Append('{'); bool first = true;
                foreach (var pair in map) { if (!first) b.Append(','); first = false; String(b, pair.Key); b.Append(':'); WriteValue(b, pair.Value); }
                b.Append('}'); return;
            }
            if (value is System.Collections.IEnumerable array)
            {
                b.Append('['); bool first = true;
                foreach (object item in array) { if (!first) b.Append(','); first = false; WriteValue(b, item); }
                b.Append(']'); return;
            }
            if (value is double d && !double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) <= 9007199254740991d && d == Math.Truncate(d)) { b.Append(d.ToString("0", Invariant)); return; }
            if (value is int || value is long) { b.Append(Convert.ToString(value, Invariant)); return; }
            throw new FormatException("Unsupported leaderboard JSON type.");
        }
        static void String(StringBuilder b, string value)
        {
            b.Append('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4", Invariant));
                else b.Append(c);
            }
            b.Append('"');
        }
        public static Dictionary<string, object> Object(object value) => value as Dictionary<string, object> ?? throw new FormatException("Expected JSON object.");
        public static List<object> Array(Dictionary<string, object> map, string key) => map.TryGetValue(key, out object value) && value is List<object> list ? list : throw new FormatException("Missing array: " + key);
        public static string Text(Dictionary<string, object> map, string key, string fallback = null) => map.TryGetValue(key, out object value) && value is string s ? s : fallback;
        public static long Number(Dictionary<string, object> map, string key, long fallback = -1)
        {
            if (!map.TryGetValue(key, out object value)) return fallback;
            if (!(value is double n) || n < 0 || n > 9007199254740991d || n != Math.Truncate(n)) throw new FormatException("Invalid integer: " + key);
            return (long)n;
        }
        sealed class JsonReader
        {
            readonly string text; int at, values;
            public JsonReader(string text) { this.text = text; }
            public object Read() { object result = Value(0); Space(); if (at != text.Length) throw new FormatException("Trailing JSON data."); return result; }
            void Space() { while (at < text.Length && (text[at] == ' ' || text[at] == '\t' || text[at] == '\r' || text[at] == '\n')) at++; }
            bool Take(char c) { Space(); if (at < text.Length && text[at] == c) { at++; return true; } return false; }
            void Need(char c) { if (!Take(c)) throw new FormatException("Expected JSON punctuation: " + c); }
            object Value(int depth)
            {
                if (depth > 16 || ++values > 800000) throw new FormatException("JSON nesting/item limit exceeded.");
                Space(); if (at >= text.Length) throw new FormatException("Incomplete JSON.");
                if (text[at] == '{')
                {
                    at++; var obj = new Dictionary<string, object>(StringComparer.Ordinal); if (Take('}')) return obj;
                    do { string key = String(); Need(':'); if (obj.ContainsKey(key)) throw new FormatException("Duplicate JSON key."); obj.Add(key, Value(depth + 1)); } while (Take(','));
                    Need('}'); return obj;
                }
                if (text[at] == '[')
                {
                    at++; var array = new List<object>(); if (Take(']')) return array;
                    do { if (array.Count >= 150000) throw new FormatException("JSON array limit exceeded."); array.Add(Value(depth + 1)); } while (Take(','));
                    Need(']'); return array;
                }
                if (text[at] == '"') return String();
                foreach (string literal in new[] { "true", "false", "null" })
                    if (at + literal.Length <= text.Length && string.CompareOrdinal(text, at, literal, 0, literal.Length) == 0)
                    { at += literal.Length; if (literal == "null") return null; return literal == "true"; }
                int start = at; if (text[at] == '-') at++;
                if (at >= text.Length || text[at] < '0' || text[at] > '9') throw new FormatException("Invalid JSON number.");
                if (text[at] == '0') at++; else while (at < text.Length && char.IsDigit(text[at])) at++;
                if (at < text.Length && text[at] == '.') { at++; Digits(); }
                if (at < text.Length && (text[at] == 'e' || text[at] == 'E')) { at++; if (at < text.Length && (text[at] == '+' || text[at] == '-')) at++; Digits(); }
                if (!double.TryParse(text.Substring(start, at - start), NumberStyles.Float, Invariant, out double number) || double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("Invalid JSON number.");
                return number;
            }
            void Digits() { int start = at; while (at < text.Length && text[at] >= '0' && text[at] <= '9') at++; if (at == start) throw new FormatException("Expected number digits."); }
            string String()
            {
                Need('"'); var result = new StringBuilder();
                while (at < text.Length)
                {
                    char c = text[at++]; if (c == '"') return result.ToString(); if (c < 32) throw new FormatException("Control character in JSON string.");
                    if (c != '\\') { result.Append(c); continue; }
                    if (at == text.Length) throw new FormatException("Incomplete JSON escape.");
                    char escape = text[at++];
                    switch (escape)
                    {
                        case '"': case '\\': case '/': result.Append(escape); break;
                        case 'b': result.Append('\b'); break; case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break; case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                        case 'u':
                            if (at + 4 > text.Length || !ushort.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, Invariant, out ushort code)) throw new FormatException("Invalid Unicode escape.");
                            result.Append((char)code); at += 4; break;
                        default: throw new FormatException("Invalid JSON escape.");
                    }
                }
                throw new FormatException("Unterminated JSON string.");
            }
        }
    }
}
