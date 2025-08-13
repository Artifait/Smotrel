
using System;
using System.Linq;
using Smotrel.ViewModels;

namespace Smotrel.Controllers
{
    public class PlaylistController
    {
        private readonly MainViewModel _vm;

        public PlaylistController(MainViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        }

        /// <summary>
        /// Вызывается когда текущее видео закончилось.
        /// Логика: если последний в главе -> первая часть следующей главы; иначе -> next.
        /// </summary>
        public void OnMediaEnded()
        {
            try
            {
                var current = _vm.SelectedVideo;
                if (current == null) return;

                var list = _vm.Playlist.ToList();

                if (!string.IsNullOrWhiteSpace(current.ChapterId))
                {
                    var chapterParts = list.Where(p => p.ChapterId == current.ChapterId).ToList();
                    var idxInChapter = chapterParts.FindIndex(p => p.PartId == current.PartId);
                    if (idxInChapter == chapterParts.Count - 1)
                    {
                        var chapterOrder = list.Select(p => p.ChapterId).Distinct().ToList();
                        var curChapterIndex = chapterOrder.IndexOf(current.ChapterId);
                        if (curChapterIndex >= 0 && curChapterIndex < chapterOrder.Count - 1)
                        {
                            var nextChapterId = chapterOrder[curChapterIndex + 1];
                            var nextPart = list.FirstOrDefault(p => p.ChapterId == nextChapterId);
                            if (nextPart != null)
                            {
                                var nextGlobalIndex = list.IndexOf(nextPart);
                                _vm.SetCurrentIndex(nextGlobalIndex);
                                return;
                            }
                        }
                    }
                }

                // default: next part
                if (_vm.Playlist.Count > 0)
                {
                    var currentIndex = list.FindIndex(v => v.PartId == current.PartId);
                    if (currentIndex >= 0 && currentIndex < _vm.Playlist.Count - 1)
                    {
                        _vm.SetCurrentIndex(currentIndex + 1);
                    }
                }
            }
            catch
            {
                // swallow
            }
        }
    }
}
