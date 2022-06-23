using Microsoft.Extensions.Logging;
using Moq;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TrueVote.Api.Services;
using TrueVote.Api.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using HotChocolate.AzureFunctions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Newtonsoft.Json;
using TrueVote.Api.Models;

namespace TrueVote.Api.Tests.ServiceTests
{
    public class GraphQLTest : TestHelper
    {
        private readonly HttpContext httpContext;

        public GraphQLTest(ITestOutputHelper output) : base(output)
        {
            httpContext = new DefaultHttpContext();

            // https://stackoverflow.com/questions/59159565/initializing-defaulthttpcontext-response-body-to-memorystream-throws-nullreferen
            httpContext.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        }

        [Fact]
        public async Task RunsCandidateQuery()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddGraphQLFunction().AddQueryType<Query>();

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var requestExecutor = serviceProvider.GetRequiredService<IGraphQLRequestExecutor>();

            var graphQLQuery = "{ candidate { name, partyAffiliation } }";
            var graphQLRequestObj = $"{{\"query\":\"{graphQLQuery}\"}}";

            var byteArray = Encoding.ASCII.GetBytes(graphQLRequestObj);
            httpContext.Request.Body = new MemoryStream(byteArray);
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.ContentLength = httpContext.Request.Body.Length;
            httpContext.Request.Path = "/api/graphql";
            httpContext.Request.Method = "POST";

            var ret = await _graphQLApi.Run(httpContext.Request, requestExecutor);
            Assert.NotNull(ret);

            var responseStream = httpContext.Response.Body as MemoryStream;
            var responseBody = Encoding.ASCII.GetString(responseStream.ToArray());
            Assert.Equal("{\"data\":{\"candidate\":{\"name\":\"John Smith\",\"partyAffiliation\":\"Independant\"}}}", responseBody);

            var graphQLRoot = JsonConvert.DeserializeObject<GraphQLCandidateRoot>(responseBody).Data;
            var candiateRoot = JsonConvert.DeserializeObject<CandidateObj>(JsonConvert.SerializeObject(graphQLRoot));
            Assert.Equal("John Smith", candiateRoot.candidate.Name);

            Assert.Equal((int) HttpStatusCode.OK, httpContext.Response.StatusCode);
            logHelper.Verify(LogLevel.Debug, Times.Exactly(2));
        }

        // TODO Add Get() tests for all Services
    }
}