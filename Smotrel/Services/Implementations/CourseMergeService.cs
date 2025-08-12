using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Smotrel.Data.Entities;
using Smotrel.Services.Interfaces;

namespace Smotrel.Services.Implementations
{
    public enum MatchKind
    {
        ExactPath,
        FileNameAndSize,
        NormalizedName,
        FuzzyName,
        None
    }

    public class MatchEntry
    {
        public Guid? ExistingPartId { get; set; }
        public string? ExistingPath { get; set; }
        public Guid? NewPartId { get; set; }
        public string? NewPath { get; set; }
        public MatchKind Kind { get; set; }
        public double Confidence { get; set; } // 0..1
    }

    public class MergeResult
    {
        public CourseEntity MergedCourse { get; set; } = new CourseEntity();
        public List<MatchEntry> Matches { get; set; } = new List<MatchEntry>();
        public List<PartEntity> UnmatchedExistingParts { get; set; } = new List<PartEntity>();
        public List<PartEntity> UnmatchedNewParts { get; set; } = new List<PartEntity>();

        public int TotalExistingParts => Matches.Select(m => m.ExistingPartId).Distinct().Count() + UnmatchedExistingParts.Count;
        public int TotalNewParts => Matches.Select(m => m.NewPartId).Distinct().Count() + UnmatchedNewParts.Count;
        public int MatchedCount => Matches.Count;
        public double MatchedPercent => TotalNewParts == 0 ? 1.0 : (double)MatchedCount / TotalNewParts;
        public string Note { get; set; } = string.Empty;
    }

    public class CourseMergeService : ICourseMergeService
    {
        private readonly double _fuzzyThreshold;

        public CourseMergeService(double fuzzyThreshold = 0.75)
        {
            _fuzzyThreshold = fuzzyThreshold;
        }

        public MergeResult Merge(CourseEntity existing, CourseEntity scanned)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (scanned == null) throw new ArgumentNullException(nameof(scanned));

            // Клонируем сканированную структуру, чтобы не мутировать входной scanned
            var merged = CloneCourseEntity(scanned);

            // Плоские списки частей
            var existingParts = existing.Chapters.SelectMany(c => c.Parts).ToList();
            var newParts = merged.Chapters.SelectMany(c => c.Parts).ToList();

            // Индексы для поиска
            var existingByPath = existingParts
                .Where(p => !string.IsNullOrWhiteSpace(p.Path))
                .ToDictionary(p => NormalizePath(p.Path), p => p, StringComparer.OrdinalIgnoreCase);

            var existingByNameAndSize = new Dictionary<string, PartEntity>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in existingParts)
            {
                var fileName = p.FileName ?? Path.GetFileName(p.Path ?? "");
                var key = $"{fileName.ToLowerInvariant()}|{p.FileSizeBytes}";
                if (!existingByNameAndSize.ContainsKey(key))
                    existingByNameAndSize[key] = p;
            }

            var existingByNormalizedName = existingParts
                .GroupBy(p => NormalizeForMatching(p.FileName ?? Path.GetFileName(p.Path ?? "")))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var unmatchedExisting = new HashSet<PartEntity>(existingParts);
            var result = new MergeResult { MergedCourse = merged };

            // 1) Exact path
            foreach (var newP in newParts.ToList())
            {
                if (string.IsNullOrWhiteSpace(newP.Path)) continue;
                var norm = NormalizePath(newP.Path);
                if (existingByPath.TryGetValue(norm, out var existP))
                {
                    ApplyMatch(existP, newP);
                    unmatchedExisting.Remove(existP);
                    result.Matches.Add(new MatchEntry
                    {
                        ExistingPartId = existP.Id,
                        ExistingPath = existP.Path,
                        NewPartId = newP.Id,
                        NewPath = newP.Path,
                        Kind = MatchKind.ExactPath,
                        Confidence = 1.0
                    });
                }
            }

            // 2) FileName + Size
            foreach (var newP in newParts.Where(p => !IsAlreadyMatched(result, p)).ToList())
            {
                var fname = newP.FileName ?? Path.GetFileName(newP.Path ?? "");
                var key = $"{fname.ToLowerInvariant()}|{newP.FileSizeBytes}";
                if (existingByNameAndSize.TryGetValue(key, out var existP))
                {
                    if (unmatchedExisting.Contains(existP))
                    {
                        ApplyMatch(existP, newP);
                        unmatchedExisting.Remove(existP);
                        result.Matches.Add(new MatchEntry
                        {
                            ExistingPartId = existP.Id,
                            ExistingPath = existP.Path,
                            NewPartId = newP.Id,
                            NewPath = newP.Path,
                            Kind = MatchKind.FileNameAndSize,
                            Confidence = 0.98
                        });
                    }
                }
            }

            // 3) Normalized name exact
            foreach (var newP in newParts.Where(p => !IsAlreadyMatched(result, p)).ToList())
            {
                var normName = NormalizeForMatching(newP.FileName ?? Path.GetFileName(newP.Path ?? ""));
                if (string.IsNullOrWhiteSpace(normName)) continue;
                if (existingByNormalizedName.TryGetValue(normName, out var candidates))
                {
                    var candidate = candidates.FirstOrDefault(c => unmatchedExisting.Contains(c));
                    if (candidate != null)
                    {
                        ApplyMatch(candidate, newP);
                        unmatchedExisting.Remove(candidate);
                        result.Matches.Add(new MatchEntry
                        {
                            ExistingPartId = candidate.Id,
                            ExistingPath = candidate.Path,
                            NewPartId = newP.Id,
                            NewPath = newP.Path,
                            Kind = MatchKind.NormalizedName,
                            Confidence = 0.90
                        });
                    }
                }
            }

            // 4) Fuzzy matching (Levenshtein similarity)
            var unmatchedExistingList = unmatchedExisting.ToList();
            foreach (var newP in newParts.Where(p => !IsAlreadyMatched(result, p)).ToList())
            {
                var newNorm = NormalizeForMatching(newP.FileName ?? Path.GetFileName(newP.Path ?? ""));
                if (string.IsNullOrWhiteSpace(newNorm)) continue;

                double bestScore = 0.0;
                PartEntity? bestCandidate = null;

                foreach (var existP in unmatchedExistingList)
                {
                    var existNorm = NormalizeForMatching(existP.FileName ?? Path.GetFileName(existP.Path ?? ""));
                    if (string.IsNullOrWhiteSpace(existNorm)) continue;

                    var sim = ComputeSimilarity(newNorm, existNorm);

                    // Boost by size closeness
                    var newSize = newP.FileSizeBytes;
                    var existSize = existP.FileSizeBytes;
                    if (newSize > 0 && existSize > 0)
                    {
                        if (newSize == existSize) sim = Math.Max(sim, 0.9);
                        else
                        {
                            var rel = Math.Abs((double)newSize - existSize) / Math.Max(newSize, existSize);
                            if (rel < 0.02) sim = Math.Max(sim, 0.85);
                        }
                    }

                    if (sim > bestScore)
                    {
                        bestScore = sim;
                        bestCandidate = existP;
                    }
                }

                if (bestCandidate != null && bestScore >= _fuzzyThreshold)
                {
                    ApplyMatch(bestCandidate, newP);
                    unmatchedExisting.Remove(bestCandidate);
                    unmatchedExistingList.Remove(bestCandidate);
                    result.Matches.Add(new MatchEntry
                    {
                        ExistingPartId = bestCandidate.Id,
                        ExistingPath = bestCandidate.Path,
                        NewPartId = newP.Id,
                        NewPath = newP.Path,
                        Kind = MatchKind.FuzzyName,
                        Confidence = bestScore
                    });
                }
            }

            // Fill unmatched lists
            result.UnmatchedExistingParts.AddRange(unmatchedExisting);
            result.UnmatchedNewParts.AddRange(newParts.Where(p => !IsAlreadyMatched(result, p)).ToList());

            // Assign merged course and status notes
            result.MergedCourse = merged;
            if (result.MatchedPercent < 0.5)
            {
                result.Note = "Less than 50% parts matched automatically — manual review recommended.";
                result.MergedCourse.Status = "changed";
            }
            else if (result.MatchedPercent < 0.95)
            {
                result.Note = "Partial automatic match.";
                result.MergedCourse.Status = "inprogress";
            }
            else
            {
                result.Note = "Mostly matched automatically.";
                result.MergedCourse.Status = "inprogress";
            }

            return result;
        }

        // ---------------- helpers ----------------

        private static bool IsAlreadyMatched(MergeResult res, PartEntity newPart)
        {
            return res.Matches.Any(m => m.NewPartId.HasValue && m.NewPartId.Value == newPart.Id
                                     || !string.IsNullOrWhiteSpace(m.NewPath) && NormalizePath(m.NewPath) == NormalizePath(newPart.Path));
        }

        private static void ApplyMatch(PartEntity existing, PartEntity targetNew)
        {
            // preserve existing Id to keep continuity
            if (existing.Id != Guid.Empty)
                targetNew.Id = existing.Id;

            // transfer progress if available
            if (existing.LastPositionSeconds > 0)
                targetNew.LastPositionSeconds = existing.LastPositionSeconds;

            if (existing.Watched)
                targetNew.Watched = true;

            // transfer duration if new lacks it
            if (!targetNew.DurationSeconds.HasValue && existing.DurationSeconds.HasValue)
                targetNew.DurationSeconds = existing.DurationSeconds;
        }

        private static string NormalizePath(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return string.Empty;
            try
            {
                return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();
            }
            catch
            {
                return p.Trim().ToLowerInvariant();
            }
        }

        private static string NormalizeForMatching(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var n = name.Trim().ToLowerInvariant();

            n = Regex.Replace(n, @"\.[a-z0-9]{1,5}$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"^\s*[\[\(].*?[\]\)]\s*", "");
            n = Regex.Replace(n, @"^\s*\d{1,4}[\s\.\-_:]+", "");
            n = Regex.Replace(n, @"[^a-z0-9]+", " ");
            n = Regex.Replace(n, @"\s+", " ").Trim();
            return n;
        }

        private static CourseEntity CloneCourseEntity(CourseEntity src)
        {
            var c = new CourseEntity
            {
                Id = src.Id,
                RootPath = src.RootPath,
                Platform = src.Platform,
                Title = src.Title,
                CreatedAt = src.CreatedAt,
                TotalDurationSeconds = src.TotalDurationSeconds,
                WatchedSeconds = src.WatchedSeconds,
                FsHash = src.FsHash,
                LastScannedAt = src.LastScannedAt,
                Status = src.Status
            };

            c.Chapters = src.Chapters.Select(ch => new ChapterEntity
            {
                Id = ch.Id,
                Title = ch.Title,
                Order = ch.Order,
                RelPath = ch.RelPath,
                CourseEntityId = ch.CourseEntityId,
                Parts = ch.Parts.Select(p => new PartEntity
                {
                    Id = p.Id,
                    FileName = p.FileName,
                    Path = p.Path,
                    Index = p.Index,
                    Title = p.Title,
                    DurationSeconds = p.DurationSeconds,
                    FileSizeBytes = p.FileSizeBytes,
                    LastPositionSeconds = p.LastPositionSeconds,
                    Watched = p.Watched
                }).ToList()
            }).ToList();

            return c;
        }

        // Levenshtein distance + similarity
        private static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;

            var la = a.Length;
            var lb = b.Length;
            var d = new int[la + 1, lb + 1];

            for (int i = 0; i <= la; i++) d[i, 0] = i;
            for (int j = 0; j <= lb; j++) d[0, j] = j;

            for (int i = 1; i <= la; i++)
            {
                for (int j = 1; j <= lb; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[la, lb];
        }

        private static double ComputeSimilarity(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";
            if (a == b) return 1.0;
            var max = Math.Max(a.Length, b.Length);
            if (max == 0) return 1.0;
            var dist = LevenshteinDistance(a, b);
            var sim = 1.0 - (double)dist / max;
            return Math.Max(0.0, Math.Min(1.0, sim));
        }
    }
}
