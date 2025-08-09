using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smotrel.Messages
{
    public class OpenFolderMessage
    {
        public string FolderPath { get; }
        public OpenFolderMessage(string folderPath) => FolderPath = folderPath;
    }
}
