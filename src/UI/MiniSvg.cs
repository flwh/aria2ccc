using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AriaGui.UI
{
    /// 极简 SVG 线条渲染：解析 M/L/H/V/C/Z（含相对命令与省略重复坐标），供 IconPark 风格图标自绘。
    /// 图标源为 48x48 视图、stroke-width 4、round 端点/拐角（与 iconpark.oceanengine.com 线条主题一致）。
    public static class MiniSvg
    {
        private const float ViewBox = 48f;
        private const float StrokeWidth = 4f;

        private static readonly Dictionary<string, GraphicsPath> Cache = new Dictionary<string, GraphicsPath>();
        private static readonly Regex Token = new Regex(
            @"([MLHVCZmlhvcz])|(-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)", RegexOptions.Compiled);

        /// 在 bounds 内绘制图标线条（按 48 视图等比缩放并居中，笔宽随缩放同步）。
        public static void DrawIcon(Graphics g, Rectangle bounds, Color color, string[] paths)
        {
            float s = Math.Min(bounds.Width, bounds.Height) / ViewBox;
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X + (bounds.Width - ViewBox * s) / 2f, bounds.Y + (bounds.Height - ViewBox * s) / 2f);
            g.ScaleTransform(s, s);
            using (Pen pen = new Pen(color, StrokeWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                for (int i = 0; i < paths.Length; i++)
                {
                    g.DrawPath(pen, Parse(paths[i]));
                }
            }
            g.Restore(state);
        }

        /// 解析 SVG path 数据（结果缓存复用；调用方不得释放返回对象）。
        public static GraphicsPath Parse(string d)
        {
            GraphicsPath cached;
            if (Cache.TryGetValue(d, out cached)) return cached;

            GraphicsPath path = new GraphicsPath();
            MatchCollection ms = Token.Matches(d);
            float cx = 0f, cy = 0f, sx = 0f, sy = 0f;
            char cmd = 'M';
            int i = 0;

            Func<float> next = delegate
            {
                return float.Parse(ms[i++].Groups[2].Value, CultureInfo.InvariantCulture);
            };

            while (i < ms.Count)
            {
                if (ms[i].Groups[1].Success)
                {
                    cmd = ms[i].Value[0];
                    i++;
                    if (cmd == 'Z' || cmd == 'z')
                    {
                        path.CloseFigure();
                        cx = sx;
                        cy = sy;
                    }
                    continue;
                }

                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        {
                            bool rel = cmd == 'm';
                            float x = next(), y = next();
                            if (rel) { x += cx; y += cy; }
                            path.StartFigure();
                            cx = sx = x;
                            cy = sy = y;
                            cmd = rel ? 'l' : 'L'; // 首点之后为隐式 lineto
                            break;
                        }
                    case 'L':
                        {
                            float x = next(), y = next();
                            if (cmd == 'l') { x += cx; y += cy; }
                            path.AddLine(cx, cy, x, y);
                            cx = x;
                            cy = y;
                            break;
                        }
                    case 'H':
                        {
                            float x = next();
                            if (cmd == 'h') x += cx;
                            path.AddLine(cx, cy, x, cy);
                            cx = x;
                            break;
                        }
                    case 'V':
                        {
                            float y = next();
                            if (cmd == 'v') y += cy;
                            path.AddLine(cx, cy, cx, y);
                            cy = y;
                            break;
                        }
                    case 'C':
                        {
                            float x1 = next(), y1 = next(), x2 = next(), y2 = next(), x = next(), y = next();
                            if (cmd == 'c')
                            {
                                x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy;
                            }
                            path.AddBezier(cx, cy, x1, y1, x2, y2, x, y);
                            cx = x;
                            cy = y;
                            break;
                        }
                    default:
                        i++; // 未知命令：跳过其参数，避免死循环
                        break;
                }
            }

            Cache[d] = path;
            return path;
        }

        /// IconPark 官方图标路径数据（源于 github.com/bytedance/IconPark 的 source/ 目录，48x48 线条主题）。
        public static class Icons
        {
            /// 下载（source/Arrows/download.svg）
            public static readonly string[] Download = new string[]
            {
                "M6 24.0083V42H42V24",
                "M33 23L24 32L15 23",
                "M23.9917 6V32",
            };

            /// 地球（source/Travel/earth.svg）
            public static readonly string[] Earth = new string[]
            {
                "M24 44C35.0457 44 44 35.0457 44 24C44 12.9543 35.0457 4 24 4C12.9543 4 4 12.9543 4 24C4 35.0457 12.9543 44 24 44Z",
                "M4 24H44",
                "M24 44C28.4183 44 32 35.0457 32 24C32 12.9543 28.4183 4 24 4C19.5817 4 16 12.9543 16 24C16 35.0457 19.5817 44 24 44Z",
                "M9.85791 10.1421C13.4772 13.7614 18.4772 16 24 16C29.5229 16 34.5229 13.7614 38.1422 10.1421",
                "M38.1422 37.8579C34.5229 34.2386 29.5229 32 24 32C18.4772 32 13.4772 34.2386 9.85791 37.8579",
            };

            /// 历史（source/Time/history.svg）
            public static readonly string[] History = new string[]
            {
                "M5.81836 6.72729V14H13.0911",
                "M4 24C4 35.0457 12.9543 44 24 44V44C35.0457 44 44 35.0457 44 24C44 12.9543 35.0457 4 24 4C16.598 4 10.1351 8.02111 6.67677 13.9981",
                "M24.005 12L24.0038 24.0088L32.4832 32.4882",
            };

            /// 打开文件夹（source/Office/folder-open.svg）
            public static readonly string[] FolderOpen = new string[]
            {
                "M4 9V41L9 21H39.5V15C39.5 13.8954 38.6046 13 37.5 13H24L19 7H6C4.89543 7 4 7.89543 4 9Z",
                "M40 41L44 21H8.8125L4 41H40Z",
            };

            /// 设置（source/Base/setting.svg）
            public static readonly string[] Setting = new string[]
            {
                "M36.686 15.171C37.9364 16.9643 38.8163 19.0352 39.2147 21.2727H44V26.7273H39.2147C38.8163 28.9648 37.9364 31.0357 36.686 32.829L40.0706 36.2137L36.2137 40.0706L32.829 36.686C31.0357 37.9364 28.9648 38.8163 26.7273 39.2147V44H21.2727V39.2147C19.0352 38.8163 16.9643 37.9364 15.171 36.686L11.7863 40.0706L7.92939 36.2137L11.314 32.829C10.0636 31.0357 9.18372 28.9648 8.78533 26.7273H4V21.2727H8.78533C9.18372 19.0352 10.0636 16.9643 11.314 15.171L7.92939 11.7863L11.7863 7.92939L15.171 11.314C16.9643 10.0636 19.0352 9.18372 21.2727 8.78533V4H26.7273V8.78533C28.9648 9.18372 31.0357 10.0636 32.829 11.314L36.2137 7.92939L40.0706 11.7863L36.686 15.171Z",
                "M24 29C26.7614 29 29 26.7614 29 24C29 21.2386 26.7614 19 24 19C21.2386 19 19 21.2386 19 24C19 26.7614 21.2386 29 24 29Z",
            };
        }
    }
}
