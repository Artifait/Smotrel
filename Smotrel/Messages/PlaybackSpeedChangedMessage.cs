using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smotrel.Messages
{
    public sealed class PlaybackSpeedChangedMessage
    {
        public double Speed { get; }
        public PlaybackSpeedChangedMessage(double speed) => Speed = speed;
    }

}
