using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio_Driver
{
    internal class AppConfig
    {
        public int BaudRate { get; set; }
        public string COMPort { get; set; }
        public List<string> Applications { get; set; }
    }
}
