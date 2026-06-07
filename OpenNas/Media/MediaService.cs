using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenNas.Media
{
    public partial class MediaService
    {
        private readonly IMauiContext _mauiContext;

        public MediaService(IMauiContext mauiContext)
        {
            _mauiContext = mauiContext;
        }
        public partial Task<List<string>> GetMediasAsync(string albumName);

        public partial Task<Dictionary<string, string>> GetImageAsync3();
        public partial Task<List<string>> GetVideosFromAlbumAsync(string albumName);
    }
}
