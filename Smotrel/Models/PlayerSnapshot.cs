using Smotrel.Enums;
using Smotrel.Interfaces;
using Smotrel.Models;

namespace Smotrel.Models
{
    /// <summary>
    /// Immutable snapshot of everything needed to fully reconstruct a player's state.
    /// Passed between Normal / Fullscreen / PiP players so each hand-off is lossless.
    /// </summary>
    public sealed record PlayerSnapshot
    {
        /// <summary>The video that is (or was) loaded.</summary>
        public VideoModel? Video { get; init; }

        /// <summary>Playback position at the moment the snapshot was taken.</summary>
        public TimeSpan StartPos { get; init; }

        /// <summary>
        /// Volume (0–1).  Always stores the "real" (un-muted) volume;
        /// if the player was muted the last non-zero volume is captured here.
        /// </summary>
        public double Volume { get; init; } = 1.0;

        /// <summary>SpeedRatio (e.g. 0.5, 1.0, 1.5, 2.0).</summary>
        public double Speed { get; init; } = 1.0;

        /// <summary>Playing / Paused at the time of the snapshot.</summary>
        public PlayerState State { get; init; } = PlayerState.Paused;

        /// <summary>Chapter timecode markers associated with the current video.</summary>
        public IList<ITimecode>? Timecodes { get; init; }

        // ── Factory Helpers ───────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal, paused snapshot for <paramref name="video"/>
        /// starting at position zero, using default volume/speed.
        /// </summary>
        public static PlayerSnapshot Default(VideoModel video) => new()
        {
            Video = video,
            StartPos = TimeSpan.Zero,
            Volume = 1.0,
            Speed = 1.0,
            State = PlayerState.Paused,
            Timecodes = video.Timestamps.Cast<ITimecode>().ToList()
        };
    }
}