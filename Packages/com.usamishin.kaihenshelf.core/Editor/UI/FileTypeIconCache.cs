using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    sealed class FileTypeIconCache : IDisposable
    {
        readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        readonly Queue<string> pending = new Queue<string>();
        readonly Action repaint;
        Task<byte[]> work;
        string active;
        bool disposed;
        public FileTypeIconCache(Action repaint)
        { this.repaint = repaint; EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Dispose; }
        public Texture Get(string path, string kind)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (!disposed && !icons.ContainsKey(extension) && icons.Count < 128)
            { icons.Add(extension, null); pending.Enqueue(extension); }
            if (icons.TryGetValue(extension, out var texture) && texture != null) return texture;
            Type type = kind == "3Dデータ" ? typeof(Mesh) : kind == "テクスチャ" || kind == "画像編集データ" ? typeof(Texture2D) : kind == "ドキュメント" ? typeof(TextAsset) : typeof(DefaultAsset);
            return EditorGUIUtility.ObjectContent(null, type).image;
        }
        void Tick()
        {
            if (disposed) return;
            if (work != null && work.IsCompleted)
            {
                if (work.Status == TaskStatus.RanToCompletion && work.Result != null)
                {
                    var texture = new Texture2D(ShellFileIconReader.Size, ShellFileIconReader.Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    texture.LoadRawTextureData(work.Result); texture.Apply(false, true); icons[active] = texture;
                }
                work = null; repaint();
            }
            if (work == null && pending.Count > 0)
            {
                active = pending.Dequeue(); string extension = active;
                work = Task.Run(() => { try { return ShellFileIconReader.Read(extension); } catch { return null; } });
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            foreach (var texture in icons.Values) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            icons.Clear(); pending.Clear();
            // A running native request finishes and frees its handles on its worker; no Unity objects there.
        }
    }
}
