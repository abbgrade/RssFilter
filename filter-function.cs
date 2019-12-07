using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Blobs;
using System.Net;
using System.Web.Http;
using System.Linq;

namespace RssFilter.Function
{
    public static class filter_function
    {
        static XNamespace atom = "http://www.w3.org/2005/Atom";

        [FunctionName("filter_function")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest request,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            // connect blob store with filter configurations
            BlobContainerClient filterContainerClient;
            {
                var connectionString = Environment.GetEnvironmentVariable("FilterConnectionString");
                if (connectionString == null)
                    throw new Exception("FilterConnectionString is not configured");

                var container = Environment.GetEnvironmentVariable("FilterContainer");
                if (container == null)
                    throw new Exception("FilterContainer is not configured");

                filterContainerClient = new BlobContainerClient(connectionString, container);
            }

            // parse request body for parameters
            string filterFileName;
            {
                string filterId = request.Query["filter_id"];

                string requestBody = await new StreamReader(request.Body).ReadToEndAsync();
                dynamic requestData = JsonConvert.DeserializeObject(requestBody);
                filterId = filterId ?? requestData?.filterId;
                
                if ( filterId == null )
                    return new BadRequestObjectResult("A filter_id is required");

                filterFileName = $"{ filterId }.xml";
            }

            // process filter
            {
                var blobClient = filterContainerClient.GetBlobClient(filterFileName);
                try
                {
                    blobClient.GetProperties();
                }
                catch
                {
                    return new NotFoundObjectResult ($"Filter {filterFileName} not found");
                }

                using (var filterReader = XmlReader.Create(blobClient.Download().Value.Content))
                {
                    var filterFile = XDocument.Load(filterReader);
                    var filterDefinition = filterFile.Element("filter");
                    var feedUrl = filterDefinition.Attribute("url").Value;
                    return ReturnFilteredFeed(log, filterDefinition, feedUrl);
                }
            }
        }

        private static IActionResult ReturnFilteredFeed(ILogger log, XElement filterDefinition, string feedUrl)
        {
            WebRequest feedRequest = WebRequest.Create(feedUrl);
            using (WebResponse feedResponse = feedRequest.GetResponse())
            {
                log.LogInformation(((HttpWebResponse)feedResponse).StatusDescription);
                Stream feedStream = feedResponse.GetResponseStream();
                using (var feedReader = XmlReader.Create(feedStream))
                {
                    var feedFile = XDocument.Load(feedReader);
                    var atomFeedElement = feedFile.Element(atom + "feed");
                    var rssElement = feedFile.Element("rss");
                    if ( atomFeedElement != null )
                        return ReturnFilteredAtomFeed(log, filterDefinition, feedUrl, feedFile, atomFeedElement);
                    if ( rssElement != null )
                        return ReturnFilteredRssChannel(log, filterDefinition, feedUrl, feedFile, rssElement);
                    throw new Exception("unsupported feed format");
                }
            }
        }

        private static IActionResult ReturnFilteredAtomFeed(ILogger log, XElement filterDefinition, string feedUrl, XDocument feedFile, XElement atomFeedElement)
        {
            {
                XElement feedTitle = atomFeedElement.Element(atom + "title");
                if (feedTitle == null)
                    return new ObjectResult($"feed {feedUrl} has no title element") { StatusCode = 500 };

                log.LogInformation($"{ feedTitle.Value}");
            }

            foreach (var entry in atomFeedElement.Elements(atom + "entry").Where(entry => FilterAtomEntry(entry, filterDefinition)).ToList())
            {
                entry.Remove();
            }

            return new OkObjectResult($"{ feedFile }");
        }

        private static IActionResult ReturnFilteredRssChannel(ILogger log, XElement filterDefinition, string feedUrl, XDocument feedFile, XElement rssElement)
        {
            XElement channel = rssElement.Element("channel");

            {
                XElement feedTitle = channel.Element("title");
                if (feedTitle == null)
                    return new ObjectResult($"feed {feedUrl} has no title element") { StatusCode = 500 };
                log.LogInformation($"{ feedTitle.Value}");
            }

            foreach (var entry in channel.Elements("item").Where(entry => FilterRssItem(entry, filterDefinition)).ToList())
            {
                entry.Remove();
            }

            return new OkObjectResult($"{ feedFile }");
        }

        private static bool FilterAtomEntry(XElement entry, XElement filterDefinition)
        {
            var title = entry.Element(atom + "title");
            return FilterTitle(filterDefinition, title.Value);
        }

        private static bool FilterRssItem(XElement item, XElement filterDefinition)
        {
            var title = item.Element("title");
            return FilterTitle(filterDefinition, title.Value);
        }

        private static bool FilterTitle(XElement filterDefinition, string title)
        {
            foreach (var rule in filterDefinition.Elements("title"))
            {
                var startswith = rule.Attribute("startswith");
                if (startswith != null && title.StartsWith(startswith.Value))
                    return true;

                var contains = rule.Attribute("contains");
                if (contains != null && title.Contains(contains.Value))
                    return true;
            }
            return false;
        }
    }
}
