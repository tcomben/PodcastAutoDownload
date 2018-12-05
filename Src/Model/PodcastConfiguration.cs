using System;
using System.Collections.Generic;
using System.Text;

namespace PdDL.Model
{
    public class PodcastConfiguration
    {
        public string DownloadFolder { get; set; }
        public string LogFolder { get; set; }
        public string LastDownloadedFileName { get; set; }
        public Podcast[] Podcasts { get; set; }
    }

    public class Podcast
    {
        public Guid Id { get; set; }
        public string RssUrl { get; set; }
        public string DownloadSubFolder { get; set; }
    }
}
