using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    // Get and all Texture APIs run on the editor thread. File/network reads and header checks run in workers.
    public sealed class ThumbnailCache : IDisposable
    {
        public const string ReadyStatus = "", LoadingStatus = "取得中", MissingStatus = "画像なし", FailedStatus = "画像を表示できません";
        const int MaxConcurrent = 4, MaxTextures = 128, MaxEntries = 256, ThumbnailSize = 256;
        const long MaxTextureBytes = 64L * 1024 * 1024;
        sealed class Entry
        {
            public string key, local, remote, status = LoadingStatus;
            public bool allowRemote;
            public Texture2D texture;
            public long textureBytes;
            public double retryAfter;
            public Task<byte[]> task;
            public CancellationTokenSource cancel;
            public LinkedListNode<Entry> node;
        }
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        readonly LinkedList<Entry> lru = new LinkedList<Entry>();
        readonly Queue<Entry> pending = new Queue<Entry>();
        readonly List<Entry> active = new List<Entry>();
        readonly Action repaint;
        readonly HttpClient client;
        readonly bool localTextureMode;
        int MaxImageBytes => localTextureMode ? 32 * 1024 * 1024 : ImageHeader.MaxBytes;
        bool disposed, remoteEnabled;
        long textureBytes;
        public int CachedTextureCount { get; private set; }
        public int ActiveRequestCount { get { return active.Count; } }

        public ThumbnailCache(Action repaint, bool localTextureMode = false)
        {
            this.repaint = repaint;
            this.localTextureMode = localTextureMode;
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false, AutomaticDecompression = DecompressionMethods.None };
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            EditorApplication.update += Tick;
            EditorApplication.quitting += Dispose;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }
        public void SetRemoteEnabled(bool allowed)
        {
            if (disposed || allowed == remoteEnabled) return;
            remoteEnabled = allowed;
            if (!allowed) Clear(); // Includes active requests that might fall back from local to remote.
        }
        public Texture2D Get(string localPath, string remoteUrl, bool allowRemote, out string status)
        {
            status = MissingStatus;
            if (disposed) return null;
            if (localTextureMode) allowRemote = false;
            SetRemoteEnabled(allowRemote);
            localPath = localPath ?? ""; remoteUrl = remoteUrl ?? "";
            if (localPath.Length == 0 && (remoteUrl.Length == 0 || !allowRemote)) return null;
            string key = localPath + "\n" + (allowRemote ? remoteUrl : "");
            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { key = key, local = localPath, remote = remoteUrl, allowRemote = allowRemote };
                entry.node = lru.AddLast(entry); entries.Add(key, entry); pending.Enqueue(entry);
                Trim();
            }
            else
            {
                lru.Remove(entry.node); lru.AddLast(entry.node);
                if (entry.status == FailedStatus && EditorApplication.timeSinceStartup >= entry.retryAfter)
                { entry.status = LoadingStatus; pending.Enqueue(entry); }
            }
            status = entry.status; return entry.texture;
        }
        void Tick()
        {
            if (disposed) return;
            bool changed = false, decoded = false;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var entry = active[i];
                if (!entry.task.IsCompleted) continue;
                bool current = entries.TryGetValue(entry.key, out var existing) && ReferenceEquals(entry, existing);
                if (current && !entry.task.IsCanceled && !entry.task.IsFaulted && entry.task.Result != null && decoded) continue;
                active.RemoveAt(i); entry.cancel.Dispose(); entry.cancel = null;
                if (!current) { var ignored = entry.task.Exception; entry.task = null; continue; }
                byte[] data = entry.task.Status == TaskStatus.RanToCompletion ? entry.task.Result : null;
                var fault = entry.task.Exception; entry.task = null;
                if (data != null) { entry.texture = Decode(data); decoded = true; }
                if (entry.texture != null)
                {
                    entry.textureBytes = (long)entry.texture.width * entry.texture.height * 4;
                    textureBytes += entry.textureBytes; CachedTextureCount++; entry.status = ReadyStatus;
                }
                else { entry.status = FailedStatus; entry.retryAfter = EditorApplication.timeSinceStartup + 300; }
                changed = true;
            }
            Trim();
            while (active.Count < (localTextureMode ? 1 : MaxConcurrent) && pending.Count > 0)
            {
                var entry = pending.Dequeue();
                if (!entries.TryGetValue(entry.key, out var current) || !ReferenceEquals(entry, current) || entry.task != null || entry.status != LoadingStatus) continue;
                entry.cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var token = entry.cancel.Token;
                entry.task = Task.Run(() => ReadAsync(entry, token), token); active.Add(entry);
            }
            if (changed) repaint?.Invoke();
        }
        async Task<byte[]> ReadAsync(Entry entry, CancellationToken token)
        {
            if (!string.IsNullOrWhiteSpace(entry.local))
            {
                try
                {
                    if (Path.IsPathRooted(entry.local))
                        using (var input = new FileStream(entry.local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16384, true))
                        {
                            string extension = Path.GetExtension(entry.local).ToLowerInvariant();
                            if (localTextureMode && (extension == ".psd" || extension == ".psb"))
                                return PhotoshopPreview.Read(input, token);
                            if (input.Length <= MaxImageBytes)
                            {
                                byte[] bytes = await ReadBounded(input, token).ConfigureAwait(false);
                                if (ReadHeader(bytes, out _, out _)) return bytes;
                            }
                        }
                }
                catch (OperationCanceledException) { return null; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
            if (!entry.allowRemote || token.IsCancellationRequested || !ImageSourcePolicy.TryRemote(entry.remote, out var uri)) return null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    // Automatic redirects are disabled. A CDN redirect is not retried at another host.
                    if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > ImageHeader.MaxBytes) return null;
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        byte[] bytes = await ReadBounded(input, token).ConfigureAwait(false);
                        return ImageHeader.TryRead(bytes, out _, out _) ? bytes : null;
                    }
                }
            }
            catch (OperationCanceledException) { return null; }
            catch (HttpRequestException) { return null; }
            catch (IOException) { return null; }
            catch (ObjectDisposedException) { return null; }
        }
        async Task<byte[]> ReadBounded(Stream input, CancellationToken token)
        {
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[16384]; int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (output.Length + read > MaxImageBytes) return null;
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }
        bool ReadHeader(byte[] bytes, out int width, out int height)
        { return localTextureMode ? ImageHeader.TryReadLocalTexture(bytes, out width, out height) : ImageHeader.TryRead(bytes, out width, out height); }
        Texture2D Decode(byte[] bytes)
        {
            Texture2D source = null, result = null;
            try
            {
                if (!ReadHeader(bytes, out int width, out int height)) return null;
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!ImageConversion.LoadImage(source, bytes, localTextureMode) || source.width != width || source.height != height) return null;
                if (localTextureMode)
                {
                    if (Math.Max(width, height) <= 512) { result = source; source = null; return result; }
                    float scale = 512f / Math.Max(width, height);
                    int smallWidth = Math.Max(1, (int)(width * scale)), smallHeight = Math.Max(1, (int)(height * scale));
                    var previous = RenderTexture.active;
                    var target = RenderTexture.GetTemporary(smallWidth, smallHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    try
                    {
                        Graphics.Blit(source, target); RenderTexture.active = target;
                        result = new Texture2D(smallWidth, smallHeight, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                        result.ReadPixels(new Rect(0, 0, smallWidth, smallHeight), 0, 0, false);
                        result.Apply(false, true); return result;
                    }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
                }
                if (Math.Max(width, height) <= ThumbnailSize) { source.Apply(false, true); result = source; source = null; return result; }
                float ratio = (float)ThumbnailSize / Math.Max(width, height);
                int w = Math.Max(1, (int)(width * ratio)), h = Math.Max(1, (int)(height * ratio));
                Color32[] original = source.GetPixels32(), small = new Color32[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float sx = x * (width - 1f) / Math.Max(1, w - 1), sy = y * (height - 1f) / Math.Max(1, h - 1);
                    int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(width - 1, x0 + 1), y1 = Math.Min(height - 1, y0 + 1);
                    small[y * w + x] = Color32.Lerp(Color32.Lerp(original[y0 * width + x0], original[y0 * width + x1], sx - x0), Color32.Lerp(original[y1 * width + x0], original[y1 * width + x1], sx - x0), sy - y0);
                }
                result = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                result.SetPixels32(small); result.Apply(false, true); return result;
            }
            catch { if (result != null) UnityEngine.Object.DestroyImmediate(result); return null; }
            finally { if (source != null) UnityEngine.Object.DestroyImmediate(source); }
        }
        void Trim()
        {
            while (lru.Count > 0 && (entries.Count > MaxEntries || CachedTextureCount > (localTextureMode ? 32 : MaxTextures) || textureBytes > (localTextureMode ? 32L * 1024 * 1024 : MaxTextureBytes))) Remove(lru.First.Value);
        }
        void Remove(Entry entry)
        {
            entries.Remove(entry.key); lru.Remove(entry.node); entry.cancel?.Cancel();
            if (entry.texture != null) { UnityEngine.Object.DestroyImmediate(entry.texture); entry.texture = null; textureBytes -= entry.textureBytes; CachedTextureCount--; }
        }
        public void Clear()
        {
            while (lru.Count > 0) Remove(lru.First.Value);
            pending.Clear(); repaint?.Invoke();
        }
        public void RetryFailed()
        {
            if (disposed) return;
            // Drop only failures. Visible cards request them again on repaint, without
            // discarding loaded textures or restarting requests already in progress.
            var node = lru.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.status == FailedStatus) Remove(node.Value);
                node = next;
            }
            repaint?.Invoke();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; EditorApplication.update -= Tick; EditorApplication.quitting -= Dispose; AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            Clear(); client.Dispose();
            foreach (var entry in active) entry.task.ContinueWith(task => { var ignored = task.Exception; entry.cancel?.Dispose(); }, TaskScheduler.Default);
            active.Clear();
        }
    }
}
