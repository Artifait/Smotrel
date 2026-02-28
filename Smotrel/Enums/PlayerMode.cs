namespace Smotrel.Enums
{
    /// <summary>
    /// Визуальный режим SmotrelPlayer.
    /// Управляет видимостью кнопок в нижней панели.
    /// Само переключение окон — ответственность MainPlayer.
    /// </summary>
    public enum PlayerMode
    {
        /// <summary>Встроен в layout. Видны все кнопки.</summary>
        Normal,

        /// <summary>Полный экран. Только кнопка выхода (E73F).</summary>
        Fullscreen,

        /// <summary>Картинка в картинке. Только кнопка выхода (E9A6).</summary>
        PiP,
    }
}