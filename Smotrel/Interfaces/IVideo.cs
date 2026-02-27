namespace Smotrel.Interfaces
{
    /// <summary>
    /// Описывает один видеофайл курса.
    /// VideoModel уже реализует этот интерфейс.
    /// </summary>
    public interface IVideo
    {
        /// <summary>Название урока — отображается в верхнем оверлее плеера.</summary>
        string Title { get; }

        /// <summary>Путь до файла (локальный путь или UNC).</summary>
        string FilePath { get; }

        /// <summary>
        /// Абсолютный порядковый номер в курсе.
        /// Используется для навигации Пред/След.
        /// </summary>
        int AbsoluteIndex { get; }
    }
}


