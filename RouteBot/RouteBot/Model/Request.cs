using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RouteBot.Model
{
    public class Request
    {
        public CommandType CommandType { get; set; }
        public IDictionary<string, string> Content { get; set; }
    }

    public enum CommandType
    {
        Help,
        Route,
        Error
    }
}
