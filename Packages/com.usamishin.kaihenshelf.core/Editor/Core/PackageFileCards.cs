using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KaihenShelf
{
    public sealed class PackageFileCard
    {
        public CatalogFile file;
        public string version = "", newerPath = "";
        public bool duplicateName, oldVersion, duplicateDownload;
        public bool material { get { return file.kind == "UnityPackage" && Path.GetFileName(file.path).IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public readonly List<PackageFileCard> older = new List<PackageFileCard>();
        internal string family, downloadFamily;
        internal Version number;
        internal int copyNumber;
    }

    // Adapted from the Windows app's DownloadUpdateCandidateService (MIT).
    // Infer only from names already collected by FileCatalog; never read package contents.
    public static class PackageFileCards
    {
        static readonly Regex VersionSuffix = new Regex(@"(?:^|[\s_.-])(?:(?:v(?:er(?:sion)?)?\.?\s*)\d+(?:[._-]\d+){0,3}|\d+(?:[._-]\d+){1,3})[a-z]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex VersionToken = new Regex(@"(?:^|[\s_.-])(?:(?:v(?:er(?:sion)?)?\.?\s*)(\d+(?:[._-]\d+){0,3})|(\d+(?:[._-]\d+){1,3}))(?:[a-z])?(?:$|[\s_.-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.RightToLeft);
        static readonly Regex CopySuffix = new Regex(@"\s*\((\d+)\)\s*$", RegexOptions.CultureInvariant);
        // Mochi Fitter releases use MochiFitter65r, without a separator or v prefix.
        // Keep this exception anchored to the complete tool name to avoid model-number matches.
        static readonly Regex MochiFitterRelease = new Regex(@"^MochiFitter([0-9]+)r$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static string Family(string value)
        {
            value = value.Normalize(NormalizationForm.FormKC);
            if (MochiFitterRelease.IsMatch(value)) return "mochifitter";
            return Regex.Replace(VersionSuffix.Replace(value, ""), @"[^\p{L}\p{N}]", "").ToLowerInvariant();
        }
        static Version ReadVersion(string value, out string label)
        {
            label = "";
            value = value.Normalize(NormalizationForm.FormKC);
            var release = MochiFitterRelease.Match(value);
            if (release.Success)
            {
                if (!int.TryParse(release.Groups[1].Value, out int number)) return null;
                label = release.Groups[1].Value + "r";
                return new Version(number, 0, 0, 0);
            }
            var match = VersionToken.Match(value);
            if (!match.Success) return null;
            var text = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var parts = text.Split('.', '_', '-'); var numbers = new int[4];
            for (int i = 0; i < parts.Length; i++) if (!int.TryParse(parts[i], out numbers[i])) return null;
            label = "v" + string.Join(".", parts);
            return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        }
        static string[] Components(CatalogFile file) { return file.relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries); }
        static bool DownloadPair(PackageFileCard a, PackageFileCard b)
        { return a.downloadFamily != null && b.downloadFamily != null && a.downloadFamily == b.downloadFamily && a.copyNumber != b.copyNumber; }

        public static List<PackageFileCard> Build(IEnumerable<CatalogFile> files)
        {
            var cards = files.Where(x => x.kind == "UnityPackage")
                .Select(x => new PackageFileCard { file = x }).ToList();
            var duplicates = new HashSet<string>(cards.GroupBy(x => Path.GetFileName(x.file.path), StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1).Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards)
            {
                var name = Path.GetFileNameWithoutExtension(card.file.path);
                card.duplicateName = duplicates.Contains(Path.GetFileName(card.file.path));
                card.family = Family(name); card.number = ReadVersion(name, out card.version);
                var parts = Components(card.file);
                if (card.number == null)
                {
                    for (int i = parts.Length - 2; i >= 0; i--)
                    {
                        var number = ReadVersion(parts[i], out string label); var family = Family(parts[i]);
                        if (number != null && card.family.Length > 0 && family.Length > 0 &&
                            (family.Contains(card.family) || card.family.Contains(family)))
                        { card.number = number; card.version = label; break; }
                    }
                    if (card.number == null && card.duplicateName && parts.Length >= 2)
                        card.number = ReadVersion(parts[parts.Length - 2], out card.version);
                }
                if (parts.Length >= 2)
                {
                    string folder = parts[0].Normalize(NormalizationForm.FormKC).Trim();
                    var copy = CopySuffix.Match(folder);
                    card.downloadFamily = folder.ToLowerInvariant(); card.copyNumber = 0;
                    if (copy.Success && int.TryParse(copy.Groups[1].Value, out int number))
                    { card.downloadFamily = folder.Substring(0, copy.Index).TrimEnd().ToLowerInvariant(); card.copyNumber = number; }
                }
            }
            foreach (var family in cards.Where(x => x.family.Length >= 4).GroupBy(x => x.family))
            {
                var newest = family.Where(x => x.number != null).OrderByDescending(x => x.number).ThenByDescending(x => x.file.modifiedUtc).FirstOrDefault();
                if (newest == null) continue;
                foreach (var older in family.Where(x => x != newest && (x.number == null || x.number < newest.number)))
                    older.newerPath = newest.file.path;
            }
            foreach (var copy in cards.Where(x => x.newerPath.Length == 0 && x.copyNumber >= 1))
            {
                var original = cards.Where(x => DownloadPair(x, copy) && x.copyNumber < copy.copyNumber &&
                    string.Equals(Path.GetFileName(x.file.path), Path.GetFileName(copy.file.path), StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.copyNumber).ThenByDescending(x => x.file.modifiedUtc).FirstOrDefault();
                // Do not create a cycle when a newer version happens to be in the copied folder.
                if (original != null && original.newerPath.Length == 0) copy.newerPath = original.file.path;
            }
            var byPath = cards.ToDictionary(x => x.file.path, StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards.Where(x => x.newerPath.Length > 0))
            {
                var parent = byPath[card.newerPath];
                card.duplicateDownload = DownloadPair(card, parent);
                card.oldVersion = !card.duplicateDownload;
                parent.older.Add(card);
            }
            foreach (var parent in cards.Where(x => x.older.Count > 0))
            {
                parent.older.Sort((a, b) => b.file.modifiedUtc.CompareTo(a.file.modifiedUtc));
                if (!parent.older.Any(x => DownloadPair(parent, x)))
                    foreach (var same in cards.Where(x => string.Equals(Path.GetFileName(x.file.path), Path.GetFileName(parent.file.path), StringComparison.OrdinalIgnoreCase)))
                        same.duplicateName = false;
            }
            // Same-name files are a display group, not evidence of an older version
            // or identical contents. Preserve every file and its existing flags.
            var roots = cards.Where(x => x.newerPath.Length == 0).ToList();
            foreach (var group in roots.GroupBy(x => Path.GetFileName(x.file.path), StringComparer.OrdinalIgnoreCase))
            {
                var ordered = group.OrderByDescending(x => x.number).ThenByDescending(x => x.file.modifiedUtc)
                    .ThenBy(x => x.file.relative, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var duplicate in ordered.Skip(1))
                {
                    ordered[0].duplicateName = duplicate.duplicateName = true;
                    ordered[0].older.Add(duplicate);
                    roots.Remove(duplicate);
                }
            }
            return roots.OrderBy(x => Path.GetFileName(x.file.path), StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(x => x.file.modifiedUtc).ToList();
        }
    }
}
