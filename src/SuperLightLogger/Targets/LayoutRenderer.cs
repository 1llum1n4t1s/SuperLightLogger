using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// NLog 互換のレイアウト/パステンプレートを解析・描画する。
    /// 解析はコンストラクタで1回だけ行い、描画時はコンパイル済みデリゲート配列を回すだけ。
    /// リフレクション・動的コード生成を一切使用しないためネイティブ AOT で安全。
    /// </summary>
    internal sealed class LayoutRenderer
    {
        private readonly Action<LogEvent, StringBuilder>[] _segments;

        public string Template { get; }

        public LayoutRenderer(string template)
        {
            Template = template ?? string.Empty;
            var list = new List<Action<LogEvent, StringBuilder>>();
            Parse(Template, list);
            _segments = list.ToArray();
        }

        public string Render(in LogEvent ev)
        {
            var sb = new StringBuilder(256);
            Render(in ev, sb);
            return sb.ToString();
        }

        public void Render(in LogEvent ev, StringBuilder sb)
        {
            for (int i = 0; i < _segments.Length; i++)
                _segments[i](ev, sb);
        }

        // ───────────── パーサ ─────────────

        private static void Parse(string template, List<Action<LogEvent, StringBuilder>> output)
        {
            int i = 0;
            var literal = new StringBuilder();

            while (i < template.Length)
            {
                char c = template[i];

                if (c == '$' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    FlushLiteral(literal, output);
                    i += 2; // skip ${
                    var spec = ReadBalancedSpec(template, ref i);
                    output.Add(BuildRenderer(spec));
                }
                else if (c == '\\' && i + 1 < template.Length
                    && (template[i + 1] == '$' || template[i + 1] == '\\'))
                {
                    // トップレベルの最小限エスケープ: \$ → $, \\ → \
                    // Windows パステンプレート (C:\Users\... 等) を壊さないため、
                    // \: や \U など他の文字は escape として扱わない。
                    // ${...} 内のパラメータ値の \: 等は ParseParameters/Unescape 側で処理する。
                    literal.Append(template[i + 1]);
                    i += 2;
                }
                else
                {
                    literal.Append(c);
                    i++;
                }
            }

            FlushLiteral(literal, output);
        }

        private static void FlushLiteral(StringBuilder literal, List<Action<LogEvent, StringBuilder>> output)
        {
            if (literal.Length == 0) return;
            string lit = literal.ToString();
            literal.Clear();
            output.Add((_, sb) => sb.Append(lit));
        }

        /// <summary>
        /// <c>${</c> の直後から閉じ <c>}</c> までを、ネストとエスケープを考慮して読み取る。
        /// </summary>
        private static string ReadBalancedSpec(string template, ref int i)
        {
            var sb = new StringBuilder();
            int depth = 1;
            while (i < template.Length && depth > 0)
            {
                char c = template[i];
                if (c == '\\' && i + 1 < template.Length)
                {
                    sb.Append(c);
                    sb.Append(template[i + 1]);
                    i += 2;
                    continue;
                }
                if (c == '$' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    sb.Append('$').Append('{');
                    i += 2;
                    depth++;
                    continue;
                }
                if (c == '}')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                    sb.Append(c);
                    i++;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static Action<LogEvent, StringBuilder> BuildRenderer(string spec)
        {
            // name と body を最初の未エスケープの ':' で分割
            int firstColon = FindFirstSeparator(spec, ':');
            string name;
            string? body;
            if (firstColon < 0)
            {
                name = spec.Trim();
                body = null;
            }
            else
            {
                name = spec.Substring(0, firstColon).Trim();
                body = spec.Substring(firstColon + 1);
            }

            switch (name.ToLowerInvariant())
            {
                case "longdate":
                    return (e, sb) => sb.Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture));
                case "shortdate":
                    return (e, sb) => sb.Append(e.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                case "time":
                    return (e, sb) => sb.Append(e.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
                case "date":
                    return BuildDateRenderer(body);
                case "level":
                    return BuildLevelRenderer(body);
                case "logger":
                    return (e, sb) => sb.Append(e.Logger);
                case "message":
                    return (e, sb) => sb.Append(e.Message);
                case "exception":
                    return BuildExceptionRenderer(body);
                case "newline":
                    return (_, sb) => sb.Append(Environment.NewLine);
                case "threadid":
                    return (e, sb) => sb.Append(e.ThreadId.ToString(CultureInfo.InvariantCulture));
                case "threadname":
                    // Thread.CurrentThread.Name を render 時に読むと Async モードでバックグラウンドワーカー名になる。
                    // LogEvent.ThreadName は呼び出し元スレッドでキャプチャ済みなのでそちらを使う。
                    return (e, sb) => sb.Append(e.ThreadName ?? string.Empty);
                case "processname":
                    return BuildProcessNameRenderer();
                case "processid":
                    return BuildProcessIdRenderer();
                case "machinename":
                    return (_, sb) => sb.Append(Environment.MachineName);
                case "basedir":
                    return (_, sb) => sb.Append(AppContext.BaseDirectory);
                case "tempdir":
                    return (_, sb) => sb.Append(Path.GetTempPath());
                case "onexception":
                    return BuildOnExceptionRenderer(body);
                default:
                    // 未知のレンダラ → リテラル化
                    string fallback = "${" + spec + "}";
                    return (_, sb) => sb.Append(fallback);
            }
        }

        // 未エスケープ・未ネストの位置で sep を探す
        private static int FindFirstSeparator(string s, char sep)
        {
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    i++;
                    continue;
                }
                if (c == '$' && i + 1 < s.Length && s[i + 1] == '{')
                {
                    depth++;
                    i++;
                    continue;
                }
                if (c == '}')
                {
                    if (depth > 0) depth--;
                    continue;
                }
                if (depth == 0 && c == sep) return i;
            }
            return -1;
        }

        private static Dictionary<string, string> ParseParameters(string body)
        {
            // body は "key1=value1:key2=value2" 形式
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            while (i < body.Length)
            {
                string remainingForKey = body.Substring(i);
                int eq = FindFirstSeparator(remainingForKey, '=');
                if (eq < 0) break;
                string key = remainingForKey.Substring(0, eq).Trim();
                i += eq + 1;

                string remainingForValue = body.Substring(i);
                int sep = FindFirstSeparator(remainingForValue, ':');
                string value;
                if (sep < 0)
                {
                    value = Unescape(remainingForValue);
                    i = body.Length;
                }
                else
                {
                    value = Unescape(remainingForValue.Substring(0, sep));
                    i += sep + 1;
                }
                if (key.Length > 0) dict[key] = value;
            }
            return dict;
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    sb.Append(s[i + 1]);
                    i++;
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        // ───────────── 個別レンダラ ─────────────

        private static Action<LogEvent, StringBuilder> BuildDateRenderer(string? body)
        {
            string format = "yyyy-MM-dd HH:mm:ss.ffff";
            if (body != null)
            {
                var p = ParseParameters(body);
                if (p.TryGetValue("format", out var f) && f.Length > 0) format = f;
            }
            return (e, sb) => sb.Append(e.Timestamp.ToString(format, CultureInfo.InvariantCulture));
        }

        private static Action<LogEvent, StringBuilder> BuildLevelRenderer(string? body)
        {
            bool upper = false;
            bool lower = false;
            int padding = 0;
            if (body != null)
            {
                var p = ParseParameters(body);
                if (p.TryGetValue("uppercase", out var u) && bool.TryParse(u, out var uv)) upper = uv;
                if (p.TryGetValue("lowercase", out var l) && bool.TryParse(l, out var lv)) lower = lv;
                if (p.TryGetValue("padding", out var pad)
                    && int.TryParse(pad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pv)) padding = pv;
            }
            return (e, sb) =>
            {
                string s = LevelToString(e.Level);
                if (upper) s = s.ToUpperInvariant();
                else if (lower) s = s.ToLowerInvariant();
                // NLog 互換: 正の padding は右寄せ (左に空白を詰める)、
                // 負の padding は左寄せ (右に空白を詰める)。
                if (padding > 0) s = s.PadLeft(padding);
                else if (padding < 0) s = s.PadRight(-padding);
                sb.Append(s);
            };
        }

        private static string LevelToString(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace: return "Trace";
                case LogLevel.Debug: return "Debug";
                case LogLevel.Information: return "Info";
                case LogLevel.Warning: return "Warn";
                case LogLevel.Error: return "Error";
                case LogLevel.Critical: return "Fatal";
                case LogLevel.None: return "None";
                default: return "Unknown";
            }
        }

        private static Action<LogEvent, StringBuilder> BuildExceptionRenderer(string? body)
        {
            string format = "tostring";
            if (body != null)
            {
                var p = ParseParameters(body);
                if (p.TryGetValue("format", out var f) && f.Length > 0) format = f;
            }
            string fmt = format.ToLowerInvariant();
            return (e, sb) =>
            {
                if (e.Exception == null) return;
                switch (fmt)
                {
                    case "message":
                        sb.Append(e.Exception.Message);
                        break;
                    case "type":
                        sb.Append(e.Exception.GetType().FullName);
                        break;
                    case "stacktrace":
                        sb.Append(e.Exception.StackTrace ?? string.Empty);
                        break;
                    default: // tostring を含む既定動作
                        sb.Append(e.Exception.ToString());
                        break;
                }
            };
        }

        private static Action<LogEvent, StringBuilder> BuildOnExceptionRenderer(string? body)
        {
            if (string.IsNullOrEmpty(body)) return (_, _) => { };
            var inner = new List<Action<LogEvent, StringBuilder>>();
            Parse(body!, inner);
            var arr = inner.ToArray();
            return (e, sb) =>
            {
                if (e.Exception != null)
                {
                    for (int i = 0; i < arr.Length; i++) arr[i](e, sb);
                }
            };
        }

        private static Action<LogEvent, StringBuilder> BuildProcessNameRenderer()
        {
            string name;
            try
            {
                using var p = Process.GetCurrentProcess();
                name = p.ProcessName;
            }
            catch
            {
                name = "Unknown";
            }
            return (_, sb) => sb.Append(name);
        }

        private static Action<LogEvent, StringBuilder> BuildProcessIdRenderer()
        {
            int id;
            try
            {
                using var p = Process.GetCurrentProcess();
                id = p.Id;
            }
            catch
            {
                id = 0;
            }
            return (_, sb) => sb.Append(id.ToString(CultureInfo.InvariantCulture));
        }
    }
}
