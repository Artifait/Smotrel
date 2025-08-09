using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smotrel.Messages
{
    public class PlayVideoMessage
    {
        public string FilePath { get; }
        public PlayVideoMessage(string path) => FilePath = path;
    }
}
