using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using TrueVote.Api.Helpers;
using TrueVote.Api.Interfaces;
using TrueVote.Api.Models;

namespace TrueVote.Api.Services
{
    public class Race : LoggerHelper
    {
        private readonly ITrueVoteDbContext _trueVoteDbContext;
        private readonly TelegramBot _telegramBot;

        public Race(ILogger log, ITrueVoteDbContext trueVoteDbContext, TelegramBot telegramBot) : base(log, telegramBot)
        {
            _trueVoteDbContext = trueVoteDbContext;
            _telegramBot = telegramBot;
        }

        [Function(nameof(CreateRace))]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [OpenApiOperation(operationId: "CreateRace", tags: new[] { "Race" })]
        [OpenApiSecurity("oidc_auth", SecuritySchemeType.OpenIdConnect, OpenIdConnectUrl = "https://login.microsoftonline.com/{tenant_id}/v2.0/.well-known/openid-configuration", OpenIdConnectScopes = "openid,profile")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(BaseRaceModel), Description = "Partially filled Race Model", Example = typeof(BaseRaceModel))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(RaceModel), Description = "Returns the added Race")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(SecureString), Description = "Forbidden")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(SecureString), Description = "Unauthorized")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(SecureString), Description = "Not Found")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotAcceptable, contentType: "application/json", bodyType: typeof(SecureString), Description = "Not Acceptable")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.TooManyRequests, contentType: "application/json", bodyType: typeof(SecureString), Description = "Too Many Requests")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.UnsupportedMediaType, contentType: "application/json", bodyType: typeof(SecureString), Description = "Unsupported Media Type")]
        public async Task<HttpResponseData> CreateRace(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "race")] HttpRequestData req)
        {
            LogDebug("HTTP trigger - CreateRace:Begin");

            BaseRaceModel baseRace;
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                baseRace = JsonConvert.DeserializeObject<BaseRaceModel>(requestBody);
            }
            catch (Exception e)
            {
                LogError("baseRace: invalid format");
                LogDebug("HTTP trigger - CreateRace:End");

                return await req.CreateBadRequestResponseAsync(new SecureString { Value = e.Message });
            }

            LogInformation($"Request Data: {baseRace}");

            var race = new RaceModel { Name = baseRace.Name, RaceType = baseRace.RaceType };

            await _trueVoteDbContext.EnsureCreatedAsync();

            await _trueVoteDbContext.Races.AddAsync(race);
            await _trueVoteDbContext.SaveChangesAsync();

            await _telegramBot.SendChannelMessageAsync($"New TrueVote Race created: {baseRace.Name}");

            LogDebug("HTTP trigger - CreateRace:End");

            return await req.CreateCreatedResponseAsync(race);
        }

        [Function(nameof(AddCandidates))]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [OpenApiOperation(operationId: "AddCandidates", tags: new[] { "Race" })]
        [OpenApiSecurity("oidc_auth", SecuritySchemeType.OpenIdConnect, OpenIdConnectUrl = "https://login.microsoftonline.com/{tenant_id}/v2.0/.well-known/openid-configuration", OpenIdConnectScopes = "openid,profile")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(AddCandidatesModel), Description = "RaceId and collection of Candidate Ids", Example = typeof(AddCandidatesModel))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(RaceModel), Description = "Returns the Race")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(SecureString), Description = "Forbidden")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(SecureString), Description = "Unauthorized")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(SecureString), Description = "Not Found")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotAcceptable, contentType: "application/json", bodyType: typeof(SecureString), Description = "Not Acceptable")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.TooManyRequests, contentType: "application/json", bodyType: typeof(SecureString), Description = "Too Many Requests")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.UnsupportedMediaType, contentType: "application/json", bodyType: typeof(SecureString), Description = "Unsupported Media Type")]
        public async Task<HttpResponseData> AddCandidates(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "race/addcandidates")] HttpRequestData req)
        {
            LogDebug("HTTP trigger - AddCandidates:Begin");

            AddCandidatesModel addCandidatesModel;
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                addCandidatesModel = JsonConvert.DeserializeObject<AddCandidatesModel>(requestBody);
            }
            catch (Exception e)
            {
                LogError("addCandidates: invalid format");
                LogDebug("HTTP trigger - AddCandidates:End");

                return await req.CreateBadRequestResponseAsync(new SecureString { Value = e.Message });
            }

            LogInformation($"Request Data: {addCandidatesModel}");

            // Check if the race exists. If so, return it detached from EF
            var race = await _trueVoteDbContext.Races.Where(r => r.RaceId == addCandidatesModel.RaceId).AsNoTracking().OrderByDescending(r => r.DateCreated).FirstOrDefaultAsync();
            if (race == null)
            {
                return await req.CreateNotFoundResponseAsync(new SecureString { Value = $"Race: '{addCandidatesModel.RaceId}' not found" });
            }

            // Check if each candidate exists or is already part of the race. If any problems, exit with error
            foreach (var cid in addCandidatesModel.CandidateIds)
            {
                // Ensure Candidate exists in Candidate store
                var candidate = await _trueVoteDbContext.Candidates.Where(c => c.CandidateId == cid).OrderByDescending(c => c.DateCreated).FirstOrDefaultAsync();
                if (candidate == null)
                {
                    return await req.CreateNotFoundResponseAsync(new SecureString { Value = $"Candidate: '{cid}' not found" });
                }

                // Check if it's already part of the Race
                var candidateExists = race.Candidates?.Where(c => c.CandidateId == cid).FirstOrDefault();
                if (candidateExists != null)
                {
                    return await req.CreateConflictResponseAsync(new SecureString { Value = $"Candidate: '{cid}' already exists in Race" });
                }

                // Made it this far, add the candidate to the Race. Ok to add here because if another one in the list, it won't get persisted
                race.Candidates.Add(candidate);
            }

            // If made through the loop of checks above, ok to persist. This will write a new Race
            race.DateCreated = UtcNowProviderFactory.GetProvider().UtcNow;
            race.RaceId = Guid.NewGuid().ToString();

            await _trueVoteDbContext.EnsureCreatedAsync();

            await _trueVoteDbContext.Races.AddAsync(race);
            await _trueVoteDbContext.SaveChangesAsync();

            LogDebug("HTTP trigger - AddCandidates:End");

            return await req.CreateCreatedResponseAsync(race);
        }

        [Function(nameof(RaceFind))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [OpenApiOperation(operationId: "RaceFind", tags: new[] { "Race" })]
        [OpenApiSecurity("oidc_auth", SecuritySchemeType.OpenIdConnect, OpenIdConnectUrl = "https://login.microsoftonline.com/{tenant_id}/v2.0/.well-known/openid-configuration", OpenIdConnectScopes = "openid,profile")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(FindRaceModel), Description = "Fields to search for Races", Example = typeof(FindRaceModel))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(RaceModelList), Description = "Returns collection of Races")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(SecureString), Description = "Forbidden")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(SecureString), Description = "Unauthorized")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(SecureString), Description = "Not Found")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotAcceptable, contentType: "application/json", bodyType: typeof(SecureString), Description = "Not Acceptable")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.TooManyRequests, contentType: "application/json", bodyType: typeof(SecureString), Description = "Too Many Requests")]
        public async Task<HttpResponseData> RaceFind(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "race/find")] HttpRequestData req)
        {
            LogDebug("HTTP trigger - RaceFind:Begin");

            FindRaceModel findRace;
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                findRace = JsonConvert.DeserializeObject<FindRaceModel>(requestBody);
            }
            catch (Exception e)
            {
                LogError("findRace: invalid format");
                LogDebug("HTTP trigger - RaceFind:End");

                return await req.CreateBadRequestResponseAsync(new SecureString { Value = e.Message });
            }

            LogInformation($"Request Data: {findRace}");

            // TODO Fix RaceTypeName not resolving properly
            // Get all the races that match the search
            var items = await _trueVoteDbContext.Races
                .Where(r =>
                    findRace.Name == null || (r.Name ?? string.Empty).ToLower().Contains(findRace.Name.ToLower()))
                .OrderByDescending(r => r.DateCreated).ToListAsync();

            LogDebug("HTTP trigger - RaceFind:End");

            return items.Count == 0 ? req.CreateNotFoundResponse() : await req.CreateOkResponseAsync(items);
        }
    }
}
