
namespace Smotrel.Models
{
    public class CourseModel
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public ChapterCourseModel MainChapter { get; set; } = null!;

        public VideoModel? GetVideoByAbsoluteIndex(int absoluteIndex)
        {
            if (MainChapter == null) return null;
            return FindVideoByPredicate(MainChapter, v => v.AbsoluteIndex == absoluteIndex);
        }

        public VideoModel? GetNextVideo(VideoModel current)
        {
            if (current == null) return null;
            var next = GetVideoByAbsoluteIndex(current.AbsoluteIndex + 1);
            return next;
        }

        public VideoModel? GetPreviousVideo(VideoModel current)
        {
            if (current == null) return null;
            if (current.AbsoluteIndex <= 0) return null;
            return GetVideoByAbsoluteIndex(current.AbsoluteIndex - 1);
        }

        public VideoModel? GetVideoByRelativeIndices(params int[] indices)
        {
            if (indices == null || indices.Length == 0) return null;
            ChapterCourseModel cur = MainChapter;
            for (int i = 0; i < indices.Length - 1; i++)
            {
                int idx = indices[i];
                if (idx < 0 || idx >= cur.Chapters.Count) return null;
                cur = cur.Chapters[idx];
                if (cur == null) return null;
            }
            int videoIdx = indices[indices.Length - 1];
            if (videoIdx < 0 || videoIdx >= cur.Videos.Count) return null;
            return cur.Videos[videoIdx];
        }

        private static VideoModel? FindVideoByPredicate(ChapterCourseModel chapter, Func<VideoModel, bool> pred)
        {
            foreach (var v in chapter.Videos)
            {
                if (pred(v)) return v;
            }
            foreach (var ch in chapter.Chapters)
            {
                var found = FindVideoByPredicate(ch, pred);
                if (found != null) return found;
            }
            return null;
        }
    }
}
