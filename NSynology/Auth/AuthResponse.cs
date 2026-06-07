using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NSynology.Auth
{
    public class AuthResponse
    {
        public string Sid { get; set; }
        public string Did { get; set; }
        public string? SynoToken { get; set; }
    }
}
