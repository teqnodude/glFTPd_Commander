using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace glFTPd_Commander.Models
{
    public class UnitItem
    {
        public string? Display { get; set; }
        public string? Code { get; set; } // use 'Code' instead of 'Tag'
        public override string? ToString() => Display;
    }
}
