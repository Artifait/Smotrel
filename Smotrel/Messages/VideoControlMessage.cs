using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smotrel.Messages
{
    public enum VideoControlAction
    {
        Previous,
        TogglePlayPause,
        SpeedUp,
        NormalSpeed,
        ToggleFullscreen,
        Next
    }

    public sealed class VideoControlMessage
    {
        public VideoControlAction Action { get; }
        public VideoControlMessage(VideoControlAction action) => Action = action;
    }
}
