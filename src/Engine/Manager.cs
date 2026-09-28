using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AriaGui.Engine
{
    /// 任务变更事件（在后台线程同步派发）。
    public sealed class TaskEvent
    {
        public string TaskId;
        public TaskStatus Status;
    }

    /// 任务调度器：每个任务对应一个 aria2c 子进程；暂停=杀进程保留 .aria2 控制文件，继续=--continue 重新拉起。
    public sealed class Manager
    {
        private readonly string _bin;
        private readonly Config _cfg;
        private readonly HistoryStore _hist;

        private readonly object _mu = new object();
        private readonly Dictionary<string, DownloadTask> _tasks = new Dictionary<string, DownloadTask>();
        private readonly List<string> _order = new List<string>();
        private readonly Queue<string> _queue = new Queue<string>();
        private int _seq;
        private int _running;
        /// RPC 常驻实例：应用运行期间持续提供 RPC 端口（AriaNg 等外部工具可随时连接）。
        private Process _rpcHost;

        private readonly object _verMu = new object();
        private string _aria2Version; // 版本号缓存（null=未查询）；查询失败缓存空串

        private readonly object _subMu = new object();
        private readonly List<Action<TaskEvent>> _subs = new List<Action<TaskEvent>>();

        /// 释放内嵌 aria2c 并构造调度器；释放失败抛异常（由启动路径 MessageBox 展示）。
        public Manager(Config cfg, HistoryStore hist)
        {
            _bin = Embed.Extract();
            _cfg = cfg;
            _hist = hist;
        }

        /// aria2c 版本号（如 "1.37.0"）；查询失败返回空串。首次访问运行 aria2c --version，结果缓存。
        public string Aria2Version
        {
            get
            {
                lock (_verMu)
                {
                    if (_aria2Version == null) _aria2Version = QueryVersion();
                    return _aria2Version;
                }
            }
        }

        /// 运行 aria2c --version 并解析版本号（5 秒超时）；任何失败返回空串，不抛异常。
        private string QueryVersion()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(_bin, "--version");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    // --version 输出不足 1KB（远小于管道缓冲），不会因未及时读取而死锁
                    if (!p.WaitForExit(5000))
                    {
                        try { p.Kill(); } catch { }
                        return "";
                    }
                    return ParseVersionLine(p.StandardOutput.ReadToEnd());
                }
            }
            catch
            {
                return "";
            }
        }

        /// 解析 `aria2c --version` 输出：首个非空行 "aria2 version X.Y.Z" → "X.Y.Z"；不符返回空串。
        internal static string ParseVersionLine(string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            string prefix = "aria2 version ";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line == "") continue;
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(prefix.Length).Trim();
                return "";
            }
            return "";
        }

        /// 注册事件接收器（后台线程同步调用，处理器必须立即返回）。
        public void Subscribe(Action<TaskEvent> handler)
        {
            lock (_subMu)
            {
                _subs.Add(handler);
            }
        }

        /// 注销事件接收器。
        public void Unsubscribe(Action<TaskEvent> handler)
        {
            lock (_subMu)
            {
                _subs.Remove(handler);
            }
        }

        /// 新建任务并入队。dir 为空时使用配置的默认目录。
        public DownloadTask AddTask(string url, string dir, string output)
        {
            url = (url == null ? "" : url).Trim();
            if (url == "") throw new InvalidOperationException("URL 不能为空");
            string lower = url.ToLowerInvariant();
            if (url.IndexOf("://", StringComparison.Ordinal) < 0 &&
                !lower.StartsWith("magnet:") &&
                !lower.EndsWith(".torrent"))
                throw new InvalidOperationException("无法识别的下载地址: " + url);
            if (string.IsNullOrEmpty(dir)) dir = _cfg.SaveDir;

            lock (_mu)
            {
                _seq++;
                DownloadTask t = new DownloadTask();
                t.Id = string.Format(CultureInfo.InvariantCulture, "t{0:0000}", _seq);
                t.Url = url;
                t.Dir = dir;
                t.Output = (output == null ? "" : output).Trim();
                t.IsPlaylist = M3u8.IsPlaylistUrl(url);
                if (t.IsPlaylist && t.Output == "") t.Output = M3u8.GuessOutputName(url);
                t.Status = TaskStatus.Queued;
                t.Eta = -1;
                t.CreatedAt = DateTime.Now;
                _tasks[t.Id] = t;
                _order.Add(t.Id);
                _queue.Enqueue(t.Id);
                PumpLocked();
                return t;
            }
        }

        /// 启动/恢复一个排队、暂停或失败的任务。
        public void Start(string id)
        {
            lock (_mu)
            {
                DownloadTask t;
                if (!_tasks.TryGetValue(id, out t)) return;
                if (t.Status == TaskStatus.Queued || t.Status == TaskStatus.Running) return;
                t.Status = TaskStatus.Queued;
                t.PauseRequested = false;
                t.RemoveRequested = false;
                _queue.Enqueue(id);
                PumpLocked();
            }
        }

        /// 暂停：杀进程但保留 .aria2 控制文件，之后可续传。
        public void Pause(string id)
        {
            lock (_mu)
            {
                DownloadTask t;
                if (!_tasks.TryGetValue(id, out t)) return;
                if (t.Status == TaskStatus.Queued)
                {
                    DequeueLocked(id);
                    t.Status = TaskStatus.Paused;
                    EmitLocked(t.Id, t.Status);
                }
                else if (t.Status == TaskStatus.Running)
                {
                    t.PauseRequested = true;
                    Kill(t);
                }
            }
        }

        /// 删除任务；运行中的先杀进程（不清理 .aria2 文件）。
        public void Remove(string id)
        {
            lock (_mu)
            {
                DownloadTask t;
                if (!_tasks.TryGetValue(id, out t)) return;
                if (t.Status == TaskStatus.Running)
                {
                    t.RemoveRequested = true;
                    Kill(t);
                }
                else if (t.Status == TaskStatus.Queued)
                {
                    DequeueLocked(id);
                    t.Status = TaskStatus.Removed;
                    EmitLocked(t.Id, TaskStatus.Removed);
                }
                else
                {
                    t.Status = TaskStatus.Removed;
                    EmitLocked(t.Id, TaskStatus.Removed);
                }
            }
        }

        /// 任务副本（按创建顺序，剔除已删除），供 UI 渲染。
        public List<DownloadTask> Snapshot()
        {
            lock (_mu)
            {
                List<DownloadTask> outp = new List<DownloadTask>(_order.Count);
                foreach (string id in _order)
                {
                    DownloadTask t;
                    if (!_tasks.TryGetValue(id, out t)) continue;
                    if (t.Status == TaskStatus.Removed) continue;
                    outp.Add(Clone(t));
                }
                return outp;
            }
        }

        /// 当前所有运行中任务的合计速度（状态栏展示；m3u8 分片任务不参与速度展示）。
        public long TotalSpeed()
        {
            lock (_mu)
            {
                long sum = 0;
                foreach (DownloadTask t in _tasks.Values)
                {
                    if (t.Status == TaskStatus.Running && !t.IsPlaylist) sum += t.Speed;
                }
                return sum;
            }
        }

        /// 当前所有运行中任务的合计上传速度（状态栏「↑」展示；m3u8 分片不参与）。
        public long TotalUpSpeed()
        {
            lock (_mu)
            {
                long sum = 0;
                foreach (DownloadTask t in _tasks.Values)
                {
                    if (t.Status == TaskStatus.Running && !t.IsPlaylist) sum += t.UpSpeed;
                }
                return sum;
            }
        }

        /// 启动（或重启）常驻 RPC 实例：应用运行期间持续监听配置端口（TLS 加密），与下载任务解耦；
        /// 返回 null=成功，否则为提示/错误。失败不抛异常（仅提示，不影响主功能）。
        public string StartRpcHost()
        {
            StopRpcHost();
            if (!_cfg.RpcEnabled) return null;
            string certPath = null;
            string warn = null;
            try
            {
                certPath = RpcCert.Ensure(Config.DataDir());
            }
            catch (Exception ex)
            {
                certPath = null; // 降级为非加密 RPC，保持可用
                warn = "RPC 加密证书生成失败，已降级为非加密模式：" + ex.Message;
            }
            try
            {
                Process p = new Process();
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = _bin;
                psi.Arguments = JoinArgs(BuildRpcArgs(_cfg, certPath));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                p.StartInfo = psi;
                // 读干输出，避免管道缓冲填满后子进程阻塞
                p.OutputDataReceived += delegate { };
                p.ErrorDataReceived += delegate { };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                lock (_mu) { _rpcHost = p; }
                // 端口被占用等错误会让 aria2c 立刻退出：稍候确认存活再宣布成功
                if (p.WaitForExit(500))
                {
                    lock (_mu) { _rpcHost = null; }
                    p.Dispose();
                    return "RPC 服务启动失败（端口 " + _cfg.RpcPort.ToString(CultureInfo.InvariantCulture) + " 可能被占用，或证书加载失败）";
                }
                return warn;
            }
            catch (Exception ex)
            {
                return "RPC 服务启动失败：" + ex.Message;
            }
        }

        /// 停止常驻 RPC 实例（开关关闭或窗口关闭时调用）。
        public void StopRpcHost()
        {
            Process p;
            lock (_mu) { p = _rpcHost; _rpcHost = null; }
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); }
            catch { }
            try { p.Dispose(); } catch { }
        }

        /// 常驻 RPC 实例参数：无下载任务，仅提供 RPC 端口服务。证书就绪时启用 TLS（HTTPS/WSS）。
        private static List<string> BuildRpcArgs(Config cfg, string certPath)
        {
            List<string> args = new List<string>();
            args.Add("--enable-rpc=true");
            args.Add("--rpc-listen-port=" + cfg.RpcPort.ToString(CultureInfo.InvariantCulture));
            args.Add("--rpc-allow-origin-all=true");
            if (!string.IsNullOrEmpty(cfg.RpcSecret)) args.Add("--rpc-secret=" + cfg.RpcSecret);
            if (certPath != null)
            {
                // TLS 加密：AriaNg 等前端改用 wss/https 连接（HTTPS 页面无混合内容顾虑）。
                // aria2 官方 Windows 构建走 wintls(Schannel)，仅接受无密码 PKCS#12、不支持 PEM，故不传 --rpc-private-key。
                args.Add("--rpc-secure=true");
                args.Add("--rpc-certificate=" + certPath);
            }
            args.Add("--no-conf");
            args.Add("--console-log-level=warn");
            args.Add("--dir=" + cfg.SaveDir); // AriaNg 等外部工具添加任务的默认目录
            if (!string.IsNullOrEmpty(cfg.Proxy)) args.Add("--all-proxy=" + cfg.Proxy);
            if (!string.IsNullOrEmpty(cfg.SpeedLimit)) args.Add("--max-overall-download-limit=" + cfg.SpeedLimit);
            return args;
        }

        // ---- 内部实现 ----

        private static DownloadTask Clone(DownloadTask t)
        {
            DownloadTask c = new DownloadTask();
            c.Id = t.Id;
            c.Url = t.Url;
            c.Dir = t.Dir;
            c.Output = t.Output;
            c.Status = t.Status;
            c.Completed = t.Completed;
            c.Total = t.Total;
            c.Speed = t.Speed;
            c.UpSpeed = t.UpSpeed;
            c.Eta = t.Eta;
            c.Percent = t.Percent;
            c.Error = t.Error;
            c.CreatedAt = t.CreatedAt;
            c.FinishedAt = t.FinishedAt;
            c.IsPlaylist = t.IsPlaylist;
            c.SegmentDone = t.SegmentDone;
            c.SegmentTotal = t.SegmentTotal;
            return c;
        }

        /// 在锁内调用：尽可能启动排队任务，直到达到并发上限。
        private void PumpLocked()
        {
            int max = _cfg.MaxConcurrent;
            if (max < 1) max = 1;
            while (_running < max && _queue.Count > 0)
            {
                string id = _queue.Dequeue();
                DownloadTask t;
                if (!_tasks.TryGetValue(id, out t)) continue;
                if (t.Status != TaskStatus.Queued) continue;
                _running++;
                Task.Run(delegate { RunTask(t); });
            }
        }

        private void DequeueLocked(string id)
        {
            if (!_queue.Contains(id)) return;
            Queue<string> keep = new Queue<string>();
            while (_queue.Count > 0)
            {
                string x = _queue.Dequeue();
                if (x != id) keep.Enqueue(x);
            }
            while (keep.Count > 0) _queue.Enqueue(keep.Dequeue());
        }

        private void EmitLocked(string taskId, TaskStatus status)
        {
            TaskEvent ev = new TaskEvent();
            ev.TaskId = taskId;
            ev.Status = status;
            Action<TaskEvent>[] handlers;
            lock (_subMu)
            {
                handlers = _subs.ToArray();
            }
            foreach (Action<TaskEvent> h in handlers)
            {
                try { h(ev); }
                catch { /* 订阅者异常不影响调度 */ }
            }
        }

        private void SetStatus(DownloadTask t, TaskStatus s)
        {
            lock (_mu)
            {
                t.Status = s;
                EmitLocked(t.Id, s);
            }
        }

        private static void Kill(DownloadTask t)
        {
            try
            {
                if (t.Proc != null && !t.Proc.HasExited) t.Proc.Kill();
            }
            catch
            {
                // 进程已退出或句柄失效：忽略
            }
        }

        /// 启动 aria2c 并监控输出直至进程退出（工作线程中运行）。
        private void RunTask(DownloadTask t)
        {
            SetStatus(t, TaskStatus.Running);
            try
            {
                if (t.IsPlaylist)
                {
                    RunPlaylist(t);
                    return;
                }

                List<string> errTail = new List<string>();
                object tailLock = new object();
                DataReceivedEventHandler handler = delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    HandleLine(t, e.Data, errTail, tailLock);
                };
                int exitCode;
                if (!RunProcess(t, BuildArgs(t, _cfg), handler, out exitCode)) return;

                bool removeReq;
                bool pauseReq;
                lock (_mu)
                {
                    removeReq = t.RemoveRequested;
                    pauseReq = t.PauseRequested;
                }

                if (removeReq)
                {
                    SetStatus(t, TaskStatus.Removed);
                }
                else if (pauseReq)
                {
                    SetStatus(t, TaskStatus.Paused);
                }
                else if (exitCode != 0)
                {
                    Fail(t, ErrorText(errTail, tailLock, exitCode));
                }
                else
                {
                    lock (_mu)
                    {
                        // 成功结束时 aria2c 最后一行进度即 100%
                        if (t.Total > 0)
                        {
                            t.Completed = t.Total;
                            t.Percent = 100;
                        }
                    }
                    Finish(t, TaskStatus.Completed);
                }
            }
            finally
            {
                lock (_mu)
                {
                    _running--;
                    PumpLocked();
                }
            }
        }

        /// 启动 aria2c 子进程并等待退出；启动失败时已置任务 Failed 并返回 false。
        private bool RunProcess(DownloadTask t, List<string> args, DataReceivedEventHandler handler, out int exitCode)
        {
            exitCode = -1;
            Process p = new Process();
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = _bin;
            psi.Arguments = JoinArgs(args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            p.StartInfo = psi;
            p.OutputDataReceived += handler;
            p.ErrorDataReceived += handler;

            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                Fail(t, ex.Message);
                return false;
            }

            lock (_mu) { t.Proc = p; }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            // .NET 4.0+ 的无参 WaitForExit 会等待异步输出读取完成
            p.WaitForExit();
            exitCode = p.ExitCode;
            lock (_mu) { t.Proc = null; }
            return true;
        }

        /// 错误尾迹拼装：无错误行时退回 exit code。
        private static string ErrorText(List<string> errTail, object tailLock, int exitCode)
        {
            string joined;
            lock (tailLock)
            {
                joined = string.Join("\n", errTail.ToArray());
            }
            if (joined == "") joined = "exit code " + exitCode.ToString(CultureInfo.InvariantCulture);
            return joined;
        }

        /// 任务被请求暂停/删除时置状态并返回 true（RunPlaylist 各阶段检查点）。
        private bool Stopped(DownloadTask t)
        {
            bool removeReq;
            bool pauseReq;
            lock (_mu)
            {
                removeReq = t.RemoveRequested;
                pauseReq = t.PauseRequested;
            }
            if (removeReq) { SetStatus(t, TaskStatus.Removed); return true; }
            if (pauseReq) { SetStatus(t, TaskStatus.Paused); return true; }
            return false;
        }

        /// m3u8 下载：抓取清单 → aria2c 批量下载分片 → 二进制拼接合并为单文件。
        private void RunPlaylist(DownloadTask t)
        {
            if (Stopped(t)) return;
            PlaylistInfo info = ResolvePlaylist(t);
            if (info == null) return;

            string segDir = Path.Combine(t.Dir, "." + t.Output + ".m3u8parts");
            string listPath = Path.Combine(segDir, "input.txt");
            List<string> entries = M3u8.BuildEntries(info);
            try
            {
                Directory.CreateDirectory(segDir);
                M3u8.WriteInputFile(listPath, entries);
            }
            catch (Exception ex)
            {
                Fail(t, "准备分片目录失败: " + ex.Message);
                return;
            }
            lock (_mu)
            {
                t.SegmentTotal = entries.Count;
                t.SegmentDone = 0;
            }
            EmitLocked(t.Id, TaskStatus.Running);

            List<string> errTail = new List<string>();
            object tailLock = new object();
            DataReceivedEventHandler handler = delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                HandlePlaylistLine(t, e.Data, errTail, tailLock);
            };
            int exitCode;
            if (!RunProcess(t, M3u8.BuildSegmentArgs(_cfg, listPath, segDir), handler, out exitCode)) return;

            if (Stopped(t)) return;
            if (exitCode != 0)
            {
                Fail(t, ErrorText(errTail, tailLock, exitCode));
                return;
            }

            lock (_mu)
            {
                t.SegmentDone = t.SegmentTotal;
                t.Speed = 0;
            }
            EmitLocked(t.Id, TaskStatus.Running);

            string outPath = Path.Combine(t.Dir, t.Output);
            long bytes;
            try
            {
                bytes = M3u8.MergeSegments(segDir, entries.Count, outPath);
            }
            catch (Exception ex)
            {
                Fail(t, "合并分片失败: " + ex.Message);
                return;
            }
            lock (_mu)
            {
                t.Completed = bytes;
                t.Total = bytes;
                t.Percent = 100;
                t.Eta = -1;
            }
            try { Directory.Delete(segDir, true); }
            catch { /* 分片残留清理失败不影响完成状态 */ }
            Finish(t, TaskStatus.Completed);
        }

        /// 抓取并解析 m3u8（master 跟进最优带宽变体，最多 4 层）；失败时已置 Failed 并返回 null。
        private PlaylistInfo ResolvePlaylist(DownloadTask t)
        {
            string current = t.Url;
            for (int depth = 0; depth < 4; depth++)
            {
                string finalUrl;
                string text;
                try
                {
                    text = M3u8.FetchText(current, _cfg.Proxy, out finalUrl);
                }
                catch (Exception ex)
                {
                    Fail(t, "获取播放列表失败: " + ex.Message);
                    return null;
                }
                PlaylistInfo pi;
                try
                {
                    pi = M3u8.Parse(text, finalUrl);
                }
                catch (Exception ex)
                {
                    Fail(t, "解析播放列表失败: " + ex.Message);
                    return null;
                }
                if (pi.Encrypted)
                {
                    Fail(t, "加密的 HLS 播放列表（EXT-X-KEY）暂不支持");
                    return null;
                }
                if (!pi.IsMaster)
                {
                    if (pi.Segments.Count == 0 && !pi.HasMap)
                    {
                        Fail(t, "播放列表中没有可下载的分片");
                        return null;
                    }
                    if (pi.HasMap)
                    {
                        lock (_mu)
                        {
                            // fMP4（init 段 + mp4 分片）：输出建议改 .mp4
                            if (t.Output.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                                t.Output = t.Output.Substring(0, t.Output.Length - 3) + ".mp4";
                        }
                    }
                    return pi;
                }
                string best = pi.BestVariation();
                if (best == "")
                {
                    Fail(t, "主播放列表中没有可用的清晰度变体");
                    return null;
                }
                current = best;
            }
            Fail(t, "播放列表嵌套层级过深（超过 4 层）");
            return null;
        }

        /// 分片下载的进程输出：完成行计数分片，进度行更新速度，其余作错误尾迹。
        private void HandlePlaylistLine(DownloadTask t, string line, List<string> errTail, object tailLock)
        {
            string s = (line == null ? "" : line).Trim();
            if (s.IndexOf("Download complete:", StringComparison.Ordinal) >= 0)
            {
                lock (_mu)
                {
                    if (t.SegmentDone < t.SegmentTotal) t.SegmentDone++;
                    EmitLocked(t.Id, t.Status);
                }
                return;
            }
            ProgressUpdate u;
            if (Parser.ParseProgressLine(s, out u))
            {
                lock (_mu)
                {
                    t.Speed = u.Speed;
                    t.UpSpeed = u.UpSpeed;
                    EmitLocked(t.Id, t.Status);
                }
                return;
            }
            if (s != "" && LooksLikeError(s))
            {
                lock (tailLock)
                {
                    errTail.Add(s);
                    if (errTail.Count > 5) errTail.RemoveAt(0);
                }
            }
        }

        private void HandleLine(DownloadTask t, string line, List<string> errTail, object tailLock)
        {
            ProgressUpdate u;
            if (Parser.ParseProgressLine(line, out u))
            {
                ApplyProgress(t, u);
                return;
            }
            string s = (line == null ? "" : line).Trim();
            if (s != "" && LooksLikeError(s))
            {
                lock (tailLock)
                {
                    errTail.Add(s);
                    if (errTail.Count > 5) errTail.RemoveAt(0);
                }
            }
        }

        private void ApplyProgress(DownloadTask t, ProgressUpdate u)
        {
            lock (_mu)
            {
                t.Completed = u.Completed;
                if (u.Total > 0) t.Total = u.Total;
                if (u.Percent > 0) t.Percent = u.Percent;
                else if (t.Total > 0 && t.Completed > 0) t.Percent = (double)t.Completed / (double)t.Total * 100.0;
                t.Speed = u.Speed;
                t.UpSpeed = u.UpSpeed;
                t.Eta = u.Eta;
                EmitLocked(t.Id, t.Status);
            }
        }

        private void Fail(DownloadTask t, string err)
        {
            t.Error = err;
            SetStatus(t, TaskStatus.Failed);
            Archive(t, "失败");
        }

        private void Finish(DownloadTask t, TaskStatus s)
        {
            t.FinishedAt = DateTime.Now;
            SetStatus(t, s);
            Archive(t, s == TaskStatus.Completed ? "已完成" : "失败");
        }

        private void Archive(DownloadTask t, string status)
        {
            if (_hist == null) return;
            HistoryEntry e = new HistoryEntry();
            e.Url = t.Url;
            e.Name = t.DisplayName();
            e.Dir = t.Dir;
            e.Size = t.Completed;
            e.Status = status;
            _hist.Add(e);
        }

        private static readonly string[] ErrorKeywords =
            { "error", "failed", "exception", "无法", "拒绝", "unreachable", "timeout" };

        private static bool LooksLikeError(string s)
        {
            string lower = s.ToLowerInvariant();
            foreach (string kw in ErrorKeywords)
            {
                if (lower.IndexOf(kw, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// 组装 aria2c 任务命令行参数（RPC 由常驻实例单独提供，任务进程不再附挂）。
        internal static List<string> BuildArgs(DownloadTask t, Config cfg)
        {
            List<string> args = new List<string>();
            args.Add(t.Url);
            args.Add("--dir=" + t.Dir);
            args.Add("--continue=true");
            args.Add("--summary-interval=0");
            args.Add("--auto-save-interval=10");
            args.Add("--console-log-level=warn");
            args.Add("--split=" + cfg.Split.ToString(CultureInfo.InvariantCulture));
            args.Add("--max-connection-per-server=" + cfg.Split.ToString(CultureInfo.InvariantCulture));
            args.Add("--max-concurrent-downloads=" + cfg.Split.ToString(CultureInfo.InvariantCulture));
            args.Add("--check-integrity=true");
            if (!string.IsNullOrEmpty(t.Output)) args.Add("--out=" + t.Output);
            if (!string.IsNullOrEmpty(cfg.SpeedLimit)) args.Add("--max-overall-download-limit=" + cfg.SpeedLimit);
            // 网络代理：对 HTTP/HTTPS/FTP/BT 全协议生效（aria2 的 --all-proxy）
            if (!string.IsNullOrEmpty(cfg.Proxy)) args.Add("--all-proxy=" + cfg.Proxy);
            string lower = (t.Url == null ? "" : t.Url).ToLowerInvariant();
            if (t.IsMagnet() || lower.EndsWith(".torrent"))
            {
                // BT/磁力：会话记录、种子参数与 tracker 列表
                args.Add("--save-session=aria2.session");
                args.Add("--bt-metadata-only=false");
                args.Add("--seed-ratio=0");
                args.Add("--bt-stop-time-limit=0");
                args.Add("--follow-torrent=mem");
                string btTrackers = cfg.TrackerArg();
                if (btTrackers != "") args.Add("--bt-tracker=" + btTrackers);
            }
            return args;
        }

        /// 引用并拼接为命令行字符串（.NET 4.8 无 ArgumentList，需手写 MSVCRT 规则）。
        internal static string JoinArgs(List<string> args)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(QuoteArg(args[i]));
            }
            return sb.ToString();
        }

        /// 无特殊字符原样返回；否则按 MSVCRT 规则包裹双引号（含引号/尾部反斜杠转义）。
        internal static string QuoteArg(string s)
        {
            s = s == null ? "" : s;
            if (s != "" && s.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return s;
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in s)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }
                sb.Append(c);
            }
            if (backslashes > 0) sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
