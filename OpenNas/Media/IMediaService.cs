using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenNas.Media
{
    public interface IMediaService
    {
        Task<List<string>> GetMediasAsync(string albumName);
    }
}
