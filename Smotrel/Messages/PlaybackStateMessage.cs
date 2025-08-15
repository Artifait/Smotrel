using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smotrel.Messages
{
    public record PlaybackStateMessage(string FilePath, Guid? PartId, long PositionSeconds, double Speed, double Volume, bool IsPlaying);

}
