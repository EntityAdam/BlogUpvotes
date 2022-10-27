using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;

namespace BlogUpvotes
{
    public partial class Upvote
    {
        private readonly ILogger<Upvote> _logger;

        public Upvote(ILogger<Upvote> log)
        {
            _logger = log;
        }

        [FunctionName("Upvote")]
        [OpenApiOperation(operationId: "Run", tags: new[] { "upvote" })]
        [OpenApiParameter(name: "pageUri", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "The **Page URI** parameter")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/plain", bodyType: typeof(string), Description = "The OK response")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [CosmosDB(databaseName: "blogUpvotes", collectionName: "upvotes", ConnectionStringSetting = "CosmosDbConnectionString")] IAsyncCollector<dynamic> upvotesCollection,
            [CosmosDB(databaseName: "blogUpvotes", collectionName: "upvotes", ConnectionStringSetting = "CosmosDbConnectionString")] DocumentClient upVotesClient
            )
        {
            string clientIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
            string pageUri = req.Query["pageUri"];
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            pageUri = pageUri ?? data?.page;

            var clientIpsCollectionLink = UriFactory.CreateDocumentCollectionUri("blogUpvotes", "upvotes");
            var query = upVotesClient.CreateDocumentQuery<UpvoteItem>(clientIpsCollectionLink, new SqlQuerySpec()
            {
                Parameters = new SqlParameterCollection()
                {
                    new() { Name = "@ClientIp", Value = clientIp },
                    new() { Name = "@PageUri", Value = pageUri },
                    new() { Name = "@NextAllowedVoteTime", Value = DateTime.UtcNow.AddDays(1).ToString("o") }
                },
                QueryText = @"
                             select TOP 1
                               c.ClientIp
                              ,c.Timestamp
                              ,(c.Timestamp > @NextAllowedVoteTime) AS CanVote
                             from clientIps c 
                             where c.PartitionKey = CONCAT(@ClientIp, '-', @PageUri)
                             order by c.Timestamp desc
                            ",
            }).ToList();

            if (query.Any())
            {
                var item = query.First();
                if (!item.CanVote)
                {
                    var nextVoteTime = DateTime.Parse(item.Timestamp).AddDays(1).ToLocalTime().ToString("g");
                    var alreadyVotedTodayMessage = $"Thank you, but you can only upvote once on an article per day per IP address. Next time you can vote is {nextVoteTime}";
                    return new OkObjectResult(alreadyVotedTodayMessage);
                }
            }

            var timestamp = DateTime.UtcNow;

            await upvotesCollection.AddAsync(new UpvoteItem()
            {
                ClientIp = clientIp,
                Page = pageUri,
                Timestamp = timestamp.ToString("o"),
                PartitionKey = $"{clientIp}-{pageUri}"
            });

            var upvotesCollectionUri = UriFactory.CreateDocumentCollectionUri("blogUpvotes", "upvotes");
            var feedOptions = new FeedOptions { PartitionKey = new("count") }; //special case: I want to select the partition as there's only 1 record in this partition.
            var queryResult = upVotesClient.CreateDocumentQuery<UpvotesCount>(upvotesCollectionUri, feedOptions).Where(doc => doc.id == "count").ToList();
            var currentCount = queryResult.FirstOrDefault();
            if (currentCount is null)
            {
                currentCount = new UpvotesCount() { Count = 1, id = "count", PartitionKey = "count" };
            }
            else
            {
                var documentUri = UriFactory.CreateDocumentUri("blogUpvotes", "upvotes", "count");
                await upVotesClient.DeleteDocumentAsync(documentUri, new() { PartitionKey = new("count") });
                currentCount.Count++;
            }
            await upvotesCollection.AddAsync(currentCount);

            string responseMessage = $"Thank you for your upvote!";
            return new OkObjectResult(responseMessage);
        }
    }
}