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

            // connect blob store
            BlobContainerClient containerClient;
            {
                var connectionString = Environment.GetEnvironmentVariable("StorageConnectionString");
                if ( connectionString == null )
                    throw new Exception("StorageConnectionString is unconfigured");

                containerClient = new BlobContainerClient(connectionString, "filter");
            }

            // parse body
            string fileName;
            {
                string filterId = request.Query["filter_id"];

                string requestBody = await new StreamReader(request.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                filterId = filterId ?? data?.filterId;
                
                if ( filterId == null )
                    return new BadRequestObjectResult("A filter_id is required");

                fileName = $"{ filterId }.xml";
            }

            // parse filter definition
            {
                var blobClient = containerClient.GetBlobClient(fileName);
                try
                {
                    blobClient.GetProperties();
                }
                catch
                {
                    return new NotFoundObjectResult ($"Filter {fileName} not found");
                }

                using (var filterReader = XmlReader.Create(blobClient.Download().Value.Content))
                {
                    var filterFile =  XDocument.Load(filterReader);
                    var filterDefinition = filterFile.Element("filter");
                    var feedUrl = filterDefinition.Attribute("url").Value;

                    WebRequest feedRequest = WebRequest.Create(feedUrl);
                    using (WebResponse feedResponse = feedRequest.GetResponse())
                    {
                        log.LogInformation(((HttpWebResponse)feedResponse).StatusDescription);
                        Stream feedStream = feedResponse.GetResponseStream();
                        using (var feedReader = XmlReader.Create(feedStream))
                        {
                            var feedFile = XDocument.Load(feedReader);
                            var feedElement = feedFile.Element(atom + "feed");

                            XElement feedTitle = feedElement.Element(atom + "title");
                            if (feedTitle == null)
                                return new ObjectResult($"feed {feedUrl} has no title element") { StatusCode = 500};

                            log.LogInformation($"{ feedTitle.Value}");
                            foreach( var entry in feedElement.Elements(atom + "entry").Where( entry => Filter(entry, filterDefinition)).ToList() )
                            {
                                entry.Remove();
                            }

                            return new OkObjectResult($"{ feedFile }");
                        }
                    }
                }
            }
        }
    
        private static bool Filter(XElement entry, XElement definition) {
            var title = entry.Element(atom + "title");
            foreach( var rule in definition.Elements("title") )
            {
                var startswith = rule.Attribute("startswith");
                if (startswith != null && title.Value != null && title.Value.StartsWith(startswith.Value))
                    return true;

                var contains = rule.Attribute("contains");
                if (contains != null && title.Value != null && title.Value.Contains(contains.Value))
                    return true;
            }
            return false;
        }
    }
}
