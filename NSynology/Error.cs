using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NSynology
{
    public class Error
    {
        public int Code { get; set; }
        public ErrorContent? Errors { get; set; }
    }
    public class ErrorContent
    {
        public string? Name { get; set; }
        public string? Reason { get; set; }
    }
    //public class ErrorListObject
    //{
    //    [JsonPropertyName("error_list")]
    //    public IEnumerable<ErrorContent> ErrorList { get; set; }
    //}
}
