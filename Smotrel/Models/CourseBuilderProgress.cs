namespace Smotrel.Models
{
    /// <summary>
    /// Прогресс сканирования курса, передаётся через <see cref="IProgress{T}"/>.
    /// </summary>
    public record CourseBuilderProgress(
        /// <summary>Обработано файлов.</summary>
        int Current,

        /// <summary>Всего файлов.</summary>
        int Total,

        /// <summary>Полный путь к файлу, для которого читается длительность.</summary>
        string CurrentFilePath)
    {
        /// <summary>Процент завершённости (0–100).</summary>
        public int Percent => Total > 0 ? (int)((double)Current / Total * 100) : 0;

        /// <summary>Имя файла (без пути) для отображения в UI.</summary>
        public string FileName => System.IO.Path.GetFileName(CurrentFilePath);
    }
}
