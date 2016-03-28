using Lime.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RouteBot.Model
{
    public class Location
    {
        public Identity Owner { get; set; }
        public string Tag { get; set; }
        public string Address { get; set; }
    }
}
