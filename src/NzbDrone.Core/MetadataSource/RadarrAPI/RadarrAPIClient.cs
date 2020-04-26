using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MetadataSource.RadarrAPI
{
    public interface IRadarrAPIClient
    {
        List<MovieResultResource> DiscoverMovies(string action, Func<HttpRequest, HttpRequest> enhanceRequest);
        List<AlternativeTitle> AlternativeTitlesForMovie(int tmdbId);
        Tuple<List<AlternativeTitle>, AlternativeYear> AlternativeTitlesAndYearForMovie(int tmdbId);
        AlternativeTitle AddNewAlternativeTitle(AlternativeTitle title, int tmdbId);
        AlternativeYear AddNewAlternativeYear(int year, int tmdbId);
    }

    public class RadarrAPIClient : IRadarrAPIClient
    {
        private readonly IHttpRequestBuilderFactory _apiBuilder;
        private readonly IHttpClient _httpClient;

        public RadarrAPIClient(IHttpClient httpClient, IRadarrCloudRequestBuilder requestBuilder)
        {
            _httpClient = httpClient;
            _apiBuilder = requestBuilder.RadarrAPI;
        }

        public List<MovieResultResource> DiscoverMovies(string action, Func<HttpRequest, HttpRequest> enhanceRequest = null)
        {
            var request = _apiBuilder.Create().SetSegment("route", "discovery").SetSegment("action", action).Build();

            if (enhanceRequest != null)
            {
                request = enhanceRequest(request);
            }

            return Execute<List<MovieResultResource>>(request);
        }

        public List<AlternativeTitle> AlternativeTitlesForMovie(int tmdbId)
        {
            var request = _apiBuilder.Create().SetSegment("route", "mappings").SetSegment("action", "find").AddQueryParam("tmdbid", tmdbId).Build();

            var mappings = Execute<Mapping>(request);

            var titles = new List<AlternativeTitle>();

            foreach (var altTitle in mappings.Mappings.Titles)
            {
                titles.Add(new AlternativeTitle(altTitle.Info.AkaTitle, SourceType.Mappings, altTitle.Id));
            }

            return titles;
        }

        public Tuple<List<AlternativeTitle>, AlternativeYear> AlternativeTitlesAndYearForMovie(int tmdbId)
        {
            var request = _apiBuilder.Create().SetSegment("route", "mappings").SetSegment("action", "find").AddQueryParam("tmdbid", tmdbId).Build();

            var mappings = Execute<Mapping>(request);

            var titles = new List<AlternativeTitle>();

            foreach (var altTitle in mappings.Mappings.Titles)
            {
                titles.Add(new AlternativeTitle(altTitle.Info.AkaTitle, SourceType.Mappings, altTitle.Id));
            }

            var year = mappings.Mappings.Years.Where(y => y.Votes >= 3).OrderBy(y => y.Votes).FirstOrDefault();

            AlternativeYear newYear = null;

            if (year != null)
            {
                newYear = new AlternativeYear
                {
                    Year = year.Info.AkaYear,
                    SourceId = year.Id
                };
            }

            return new Tuple<List<AlternativeTitle>, AlternativeYear>(titles, newYear);
        }

        public AlternativeTitle AddNewAlternativeTitle(AlternativeTitle title, int tmdbId)
        {
            var request = _apiBuilder.Create().SetSegment("route", "mappings").SetSegment("action", "add")
                .AddQueryParam("tmdbid", tmdbId).AddQueryParam("type", "title")
                .AddQueryParam("language", IsoLanguages.Get(title.Language).TwoLetterCode)
                .AddQueryParam("aka_title", title.Title).Build();

            var newMapping = Execute<AddTitleMapping>(request);

            var newTitle = new AlternativeTitle(newMapping.Info.AkaTitle, SourceType.Mappings, newMapping.Id, title.Language);
            newTitle.VoteCount = newMapping.VoteCount;
            newTitle.Votes = newMapping.Votes;

            return newTitle;
        }

        public AlternativeYear AddNewAlternativeYear(int year, int tmdbId)
        {
            var request = _apiBuilder.Create().SetSegment("route", "mappings").SetSegment("action", "add")
                .AddQueryParam("tmdbid", tmdbId).AddQueryParam("type", "year")
                .AddQueryParam("aka_year", year).Build();

            var newYear = Execute<AddYearMapping>(request);

            return new AlternativeYear
            {
                Year = newYear.Info.AkaYear,
                SourceId = newYear.Id
            };
        }

        private HttpResponse Execute(HttpRequest request)
        {
            if (request.Method == HttpMethod.GET)
            {
                return _httpClient.Get(request);
            }
            else if (request.Method == HttpMethod.POST)
            {
                return _httpClient.Post(request);
            }
            else
            {
                throw new NotImplementedException($"Method {request.Method} not implemented");
            }
        }

        private T Execute<T>(HttpRequest request)
        {
            request.AllowAutoRedirect = true;
            request.Headers.Accept = HttpAccept.Json.Value;
            request.SuppressHttpError = true;

            var response = Execute(request);

            try
            {
                var error = JsonConvert.DeserializeObject<RadarrError>(response.Content);

                if (error != null && error.Errors != null && error.Errors.Count != 0)
                {
                    throw new RadarrAPIException(error);
                }
            }
            catch (JsonSerializationException)
            {
                //No error!
            }

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new HttpException(request, response);
            }

            return JsonConvert.DeserializeObject<T>(response.Content);
        }
    }
}
