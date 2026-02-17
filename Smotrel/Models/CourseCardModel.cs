using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smotrel.Models
{
    public class CourseCardModel
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}
