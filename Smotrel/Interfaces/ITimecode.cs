namespace Smotrel.Interfaces
{
    /// <summary>
    /// Описывает одну главу в видео (точку разбивки) на таймлайне.
    /// Аналог глав YouTube.
    /// </summary>
    public interface ITimecode
    {
        /// <summary>Позиция на таймлайне.</summary>
        TimeSpan Position { get; }

        /// <summary>Короткая подпись главы (показывается при ховере и под таймлайном).</summary>
        string Label { get; }
    }
}