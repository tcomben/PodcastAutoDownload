using Microsoft.SyndicationFeed;
using Microsoft.SyndicationFeed.Rss;
using Newtonsoft.Json;
using NLog;
using NLog.Config;
using NLog.Targets;
using PdDL.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Linq;

namespace PdDL
{
    class Program
    {
        private static Logger _logger = null;
        private static PodcastConfiguration _podcastConfig = null; 
        static void Main(string[] args)
        {
            try
            {
                if (args == null || args.Length != 1)
                {
                    Console.WriteLine(@"There must be one argument containing the path to the podcast config file.");
                    Environment.Exit(1);
                }

                InitPodcastConfig(args[0]);
                InitLogger();
                CheckForNewPodcasts();
            }
            catch(Exception ex)
            {
                _logger.Error(ex, "Error downloading podcasts");
                _logger.Factory.Flush();
            }
        }

        private static void InitPodcastConfig(string jsonConfig)
        {
            _podcastConfig = JsonConvert.DeserializeObject<PodcastConfiguration>(File.ReadAllText(jsonConfig));
        }

        private static void InitLogger()
        {
            // Step 1. Create configuration object 
            var config = new LoggingConfiguration();

            // Step 2. Create targets
            var consoleTarget = new ColoredConsoleTarget("consoleTarget")
            {
                Layout = @"${date:format=HH\:mm\:ss} ${level} ${message} ${exception}"
            };
            config.AddTarget(consoleTarget);

            var fileTarget = new FileTarget("fileTarget")
            {
                FileName = Path.Combine(_podcastConfig.LogFolder, "podcast.log"),
                Layout = "${longdate} ${level} ${message}  ${exception}"
            };
            config.AddTarget(fileTarget);

            // Step 3. Define rules
            config.AddRuleForOneLevel(LogLevel.Info, fileTarget); // only errors to file
            config.AddRuleForAllLevels(consoleTarget); // all to console

            // Step 4. Activate the configuration
            LogManager.Configuration = config;

            _logger = LogManager.GetLogger("PodcastLogger");
        }

        private static void CheckForNewPodcasts()
        {
            List<Task> tasks = new List<Task>();
            _logger.Info("Checking for new podcasts...");
            foreach(var podcast in _podcastConfig.Podcasts)
            {
                tasks.Add(CheckForNewPodcast(podcast));
            }

            _logger.Info("Waiting for podcasts to finish...");

            Task.WaitAll(tasks.ToArray());

            _logger.Info("Done checking podcasts.");

        }

        private static Task CheckForNewPodcast(Podcast podcast)
        {
            return Task.Run(async () =>
            {
                try
                {
                    using (var xmlReader = XmlReader.Create(podcast.RssUrl, new XmlReaderSettings() { Async = true }))
                    {
                        var feedReader = new RssFeedReader(xmlReader);

                        ISyndicationItem newestItem = null;
                        bool foundItem = false;
                        while (await feedReader.Read() && foundItem == false)
                        {
                            switch (feedReader.ElementType)
                            {
                                // Read Item
                                case SyndicationElementType.Item:
                                    newestItem = await feedReader.ReadItem();
                                    foundItem = true;
                                    break;
                            }
                        }

                        if (newestItem == null)
                        {
                            throw new Exception($"Could not find newest item for podcast {podcast.RssUrl}");
                        }

                        Uri downloadUri = newestItem.Links.FirstOrDefault(x => x.RelationshipType == "enclosure")?.Uri ?? null;

                        if (downloadUri == null)
                        {
                            throw new Exception($"Could not find newest item enclosure for podcast {podcast.RssUrl}");
                        }

                        CreateDirIfNotExist(_podcastConfig.DownloadFolder);
                        CreateDirIfNotExist(Path.Combine(_podcastConfig.DownloadFolder, podcast.DownloadSubFolder));

                        string lastDlLog = Path.Combine(_podcastConfig.DownloadFolder, podcast.DownloadSubFolder, _podcastConfig.LastDownloadedFileName);
                        string[] lastItems = File.Exists(lastDlLog) ? File.ReadAllLines(lastDlLog) : new string[] { };
                        string downloadFolder = Path.Combine(_podcastConfig.DownloadFolder, podcast.DownloadSubFolder);

                        // **** Download newest podcast
                        if (lastItems.Length == 0 || lastItems.First() != newestItem.Title)
                        {
                            await DownloadAsync(downloadUri, downloadFolder);

                            if (lastItems == null || lastItems.Length == 0)
                            {
                                lastItems = new string[1];
                            }

                            lastItems[0] = newestItem.Title;
                            File.WriteAllLines(lastDlLog, lastItems);
                        }
                    }
                }
                catch(Exception ex)
                {
                    _logger.Error(ex, $"Error downloading newest podcast for {podcast.RssUrl}");
                }
            });
        }

        private static void CreateDirIfNotExist(string dir)
        {
            if(Directory.Exists(dir) == false)
            {
                Directory.CreateDirectory(dir);
            }
        }


        private static async Task DownloadAsync(Uri requestUri, string downloadDir)
        {
            string filename = Path.Combine(downloadDir, requestUri.Segments.Last());

            _logger.Info("Downloading podcast {0} -> {1}", requestUri, filename);

            using (var httpClient = new HttpClient())
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    using (Stream contentStream = await (await httpClient.SendAsync(request)).Content.ReadAsStreamAsync(), stream = new FileStream(filename, FileMode.Create, FileAccess.Write))
                    {
                        await contentStream.CopyToAsync(stream);
                    }
                }
            }
        }
    }
}
