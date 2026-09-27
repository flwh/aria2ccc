using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AriaGui
{
    /// 保插入序的迷你 JSON 对象（C# 版自有格式）：
    /// 2 空格缩进、CJK 原样（不转义为 \uXXXX）、无尾换行。
    public sealed class JsonObject
    {
        private readonly List<KeyValuePair<string, object>> _items = new List<KeyValuePair<string, object>>();

        public void Set(string key, object value)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key == key)
                {
                    _items[i] = new KeyValuePair<string, object>(key, value);
                    return;
                }
            }
            _items.Add(new KeyValuePair<string, object>(key, value));
        }

        public object Get(string key)
        {
            foreach (KeyValuePair<string, object> kv in _items)
            {
                if (kv.Key == key) return kv.Value;
            }
            return null;
        }

        public string GetString(string key, string def)
        {
            object v = Get(key);
            if (v is string) return (string)v;
            return def;
        }

        public long GetLong(string key, long def)
        {
            object v = Get(key);
            if (v is long) return (long)v;
            if (v is double) return (long)(double)v;
            return def;
        }

        public bool GetBool(string key, bool def)
        {
            object v = Get(key);
            if (v is bool) return (bool)v;
            return def;
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            WriteTo(sb, 0);
            return sb.ToString();
        }

        /// 供数组序列化复用；indent 为当前嵌套层级。
        public void WriteTo(StringBuilder sb, int indent)
        {
            if (_items.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\n");
            for (int i = 0; i < _items.Count; i++)
            {
                Indent(sb, indent + 1);
                WriteString(sb, _items[i].Key);
                sb.Append(": ");
                WriteValue(sb, _items[i].Value, indent + 1);
                if (i < _items.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            Indent(sb, indent);
            sb.Append('}');
        }

        public static JsonObject ParseObject(string text)
        {
            JsonReader r = new JsonReader(text == null ? "" : text);
            JsonObject o = r.ReadObject();
            r.SkipWs();
            if (!r.Eof) throw new FormatException("JSON 尾部有多余字符");
            return o;
        }

        public static List<JsonObject> ParseObjectArray(string text)
        {
            JsonReader r = new JsonReader(text == null ? "" : text);
            r.SkipWs();
            r.Expect('[');
            List<JsonObject> list = new List<JsonObject>();
            r.SkipWs();
            if (!r.Eof && r.Peek() == ']') { r.Next(); return list; }
            while (true)
            {
                r.SkipWs();
                if (r.Eof) throw new FormatException("JSON 数组未闭合");
                if (r.Peek() != '{') throw new FormatException("JSON 数组元素必须为对象");
                list.Add(r.ReadObject());
                r.SkipWs();
                if (r.Eof) throw new FormatException("JSON 数组未闭合");
                char c = r.Next();
                if (c == ',') continue;
                if (c == ']') break;
                throw new FormatException("JSON 数组分隔符错误");
            }
            r.SkipWs();
            if (!r.Eof) throw new FormatException("JSON 尾部有多余字符");
            return list;
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        private static void WriteValue(StringBuilder sb, object v, int indent)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is long) { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is JsonObject) { ((JsonObject)v).WriteTo(sb, indent); return; }
            throw new InvalidOperationException("不支持的 JSON 值类型: " + v.GetType().FullName);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// 极简递归下降读取器；容忍 BOM 与前导空白。
        private sealed class JsonReader
        {
            private readonly string _s;
            private int _pos;

            public JsonReader(string s)
            {
                _s = s;
                _pos = 0;
            }

            public bool Eof { get { return _pos >= _s.Length; } }
            public char Peek() { return _s[_pos]; }
            public char Next() { return _s[_pos++]; }

            public void SkipWs()
            {
                while (!Eof)
                {
                    char c = Peek();
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\uFEFF') { _pos++; continue; }
                    break;
                }
            }

            public JsonObject ReadObject()
            {
                SkipWs();
                Expect('{');
                JsonObject o = new JsonObject();
                SkipWs();
                if (!Eof && Peek() == '}') { Next(); return o; }
                while (true)
                {
                    SkipWs();
                    string key = ReadString();
                    Expect(':');
                    o.Set(key, ReadValue());
                    SkipWs();
                    if (Eof) throw new FormatException("JSON 对象未闭合");
                    char c = Next();
                    if (c == ',') continue;
                    if (c == '}') break;
                    throw new FormatException("JSON 对象分隔符错误");
                }
                return o;
            }

            private object ReadValue()
            {
                SkipWs();
                if (Eof) throw new FormatException("JSON 意外结束");
                char c = Peek();
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't') { ExpectWord("true"); return true; }
                if (c == 'f') { ExpectWord("false"); return false; }
                if (c == 'n') { ExpectWord("null"); return null; }
                return ReadNumber();
            }

            private List<object> ReadArray()
            {
                Expect('[');
                List<object> list = new List<object>();
                SkipWs();
                if (!Eof && Peek() == ']') { Next(); return list; }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWs();
                    if (Eof) throw new FormatException("JSON 数组未闭合");
                    char c = Next();
                    if (c == ',') continue;
                    if (c == ']') break;
                    throw new FormatException("JSON 数组分隔符错误");
                }
                return list;
            }

            private string ReadString()
            {
                Expect('"');
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    if (Eof) throw new FormatException("JSON 字符串未闭合");
                    char c = Next();
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        if (Eof) throw new FormatException("JSON 转义未完成");
                        char e = Next();
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
                                if (_pos + 4 > _s.Length) throw new FormatException("JSON \\u 转义不完整");
                                sb.Append((char)Convert.ToInt32(_s.Substring(_pos, 4), 16));
                                _pos += 4;
                                break;
                            default: throw new FormatException("JSON 未知转义 \\" + e);
                        }
                        continue;
                    }
                    sb.Append(c);
                }
                return sb.ToString();
            }

            private object ReadNumber()
            {
                int start = _pos;
                bool isFloat = false;
                while (!Eof)
                {
                    char c = Peek();
                    if (c == '-' || c == '+' || (c >= '0' && c <= '9')) { _pos++; continue; }
                    if (c == '.' || c == 'e' || c == 'E') { isFloat = true; _pos++; continue; }
                    break;
                }
                string tok = _s.Substring(start, _pos - start);
                if (tok == "") throw new FormatException("JSON 数字格式错误");
                if (!isFloat)
                {
                    long l;
                    if (long.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                }
                double d;
                if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                throw new FormatException("JSON 数字格式错误: " + tok);
            }

            public void Expect(char c)
            {
                SkipWs();
                if (Eof || Next() != c) throw new FormatException("JSON 期望字符 '" + c + "'");
            }

            private void ExpectWord(string w)
            {
                if (_pos + w.Length > _s.Length || _s.Substring(_pos, w.Length) != w)
                    throw new FormatException("JSON 期望 " + w);
                _pos += w.Length;
            }
        }
    }
}
