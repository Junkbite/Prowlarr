using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using FluentValidation;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Definitions
{
    public class BakaBT : TorrentIndexerBase<BakaBTSettings>
    {
        public override string Name => "BakaBT";

        public override string[] IndexerUrls => new string[] { "https://bakabt.me/" };
        public override string Description => "Anime Comunity";
        private string LoginUrl => Settings.BaseUrl + "login.php";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;
        public override IndexerCapabilities Capabilities => SetCapabilities();

        public BakaBT(IHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new BakaBTRequestGenerator() { Settings = Settings, Capabilities = Capabilities };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new BakaBTParser(Settings, Capabilities.Categories);
        }

        protected override async Task DoLogin()
        {
            UpdateCookies(null, null);

            var requestBuilder = new HttpRequestBuilder(LoginUrl)
            {
                LogResponseContent = true,
                AllowAutoRedirect = true
            };

            var loginPage = await _httpClient.ExecuteAsync(new HttpRequest(LoginUrl));

            requestBuilder.Method = HttpMethod.POST;
            requestBuilder.PostProcess += r => r.RequestTimeout = TimeSpan.FromSeconds(15);
            requestBuilder.SetCookies(loginPage.GetCookies());

            requestBuilder.AddFormParameter("username", Settings.Username);
            requestBuilder.AddFormParameter("password", Settings.Password);
            requestBuilder.AddFormParameter("returnto", "/index.php");

            var parser = new HtmlParser();
            var dom = parser.ParseDocument(loginPage.Content);
            var loginKey = dom.QuerySelector("input[name=\"loginKey\"]");
            if (loginKey != null)
            {
                requestBuilder.AddFormParameter("loginKey", loginKey.GetAttribute("value"));
            }

            var authLoginRequest = requestBuilder
                .SetHeader("Content-Type", "multipart/form-data")
                .Build();

            var response = await _httpClient.ExecuteAsync(authLoginRequest);

            if (response.Content != null && response.Content.Contains("<a href=\"logout.php\">Logout</a>"))
            {
                UpdateCookies(response.GetCookies(), DateTime.Now + TimeSpan.FromDays(30));

                _logger.Debug("BakaBT authentication succeeded");
            }
            else
            {
                throw new IndexerAuthException("BakaBT authentication failed");
            }
        }

        protected override bool CheckIfLoginNeeded(HttpResponse httpResponse)
        {
            if (!httpResponse.Content.Contains("<a href=\"logout.php\">Logout</a>"))
            {
                return true;
            }

            return false;
        }

        private IndexerCapabilities SetCapabilities()
        {
            var caps = new IndexerCapabilities
            {
                TvSearchParams = new List<TvSearchParam>
                       {
                           TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep
                       },
                MusicSearchParams = new List<MusicSearchParam>
                       {
                           MusicSearchParam.Q
                       },
                BookSearchParams = new List<BookSearchParam>
                       {
                           BookSearchParam.Q
                       }
            };

            caps.Categories.AddCategoryMapping(1, NewznabStandardCategory.TVAnime, "Anime Series");
            caps.Categories.AddCategoryMapping(2, NewznabStandardCategory.TVAnime, "OVA");
            caps.Categories.AddCategoryMapping(3, NewznabStandardCategory.AudioOther, "Soundtrack");
            caps.Categories.AddCategoryMapping(4, NewznabStandardCategory.BooksComics, "Manga");
            caps.Categories.AddCategoryMapping(5, NewznabStandardCategory.TVAnime, "Anime Movie");
            caps.Categories.AddCategoryMapping(6, NewznabStandardCategory.TVOther, "Live Action");
            caps.Categories.AddCategoryMapping(7, NewznabStandardCategory.BooksOther, "Artbook");
            caps.Categories.AddCategoryMapping(8, NewznabStandardCategory.AudioVideo, "Music Video");
            caps.Categories.AddCategoryMapping(9, NewznabStandardCategory.BooksEBook, "Light Novel");

            return caps;
        }
    }

    public class BakaBTRequestGenerator : IIndexerRequestGenerator
    {
        public BakaBTSettings Settings { get; set; }
        public IndexerCapabilities Capabilities { get; set; }

        public BakaBTRequestGenerator()
        {
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term, int[] categories)
        {
            var searchString = term;
            var searchUrl = Settings.BaseUrl + "browse.php?only=0&hentai=1&incomplete=1&lossless=1&hd=1&multiaudio=1&bonus=1&reorder=1&q=";

            var match = Regex.Match(term, @".*(?=\s(?:[Ee]\d+|\d+)$)");
            if (match.Success)
            {
                searchString = match.Value;
            }

            var episodeSearchUrl = searchUrl + WebUtility.UrlEncode(searchString);

            var request = new IndexerRequest(episodeSearchUrl, null);

            yield return request;
        }

        public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(MusicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(TvSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BasicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public Func<IDictionary<string, string>> GetCookies { get; set; }
        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class BakaBTParser : IParseIndexerResponse
    {
        private readonly BakaBTSettings _settings;
        private readonly IndexerCapabilitiesCategories _categories;
        private readonly List<IndexerCategory> _defaultCategories = new List<IndexerCategory> { NewznabStandardCategory.TVAnime };

        public BakaBTParser(BakaBTSettings settings, IndexerCapabilitiesCategories categories)
        {
            _settings = settings;
            _categories = categories;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<TorrentInfo>();

            var parser = new HtmlParser();
            var dom = parser.ParseDocument(indexerResponse.Content);
            var rows = dom.QuerySelectorAll(".torrents tr.torrent, .torrents tr.torrent_alt");
            var currentCategories = new List<IndexerCategory> { NewznabStandardCategory.TVAnime };

            foreach (var row in rows)
            {
                var qTitleLink = row.QuerySelector("a.title, a.alt_title");
                if (qTitleLink == null)
                {
                    continue;
                }

                var title = qTitleLink.TextContent.Trim();

                // Insert before the release info
                var taidx = title.IndexOf('(');
                var tbidx = title.IndexOf('[');

                if (taidx == -1)
                {
                    taidx = title.Length;
                }

                if (tbidx == -1)
                {
                    tbidx = title.Length;
                }

                var titleSplit = Math.Min(taidx, tbidx);
                var titleSeries = title.Substring(0, titleSplit);
                var releaseInfo = title.Substring(titleSplit);

                currentCategories = GetNextCategory(row, currentCategories).ToList();

                var stringSeparator = new[] { " | " };
                var titles = titleSeries.Split(stringSeparator, StringSplitOptions.RemoveEmptyEntries);

                if (titles.Count() > 1 && !_settings.AddRomajiTitle)
                {
                    titles = titles.Skip(1).ToArray();
                }

                foreach (var name in titles)
                {
                    var release = new TorrentInfo();

                    release.Title = (name + releaseInfo).Trim();

                    // Ensure the season is defined as this tracker only deals with full seasons
                    if (release.Title.IndexOf("Season") == -1 && _settings.AppendSeason)
                    {
                        // Insert before the release info
                        var aidx = release.Title.IndexOf('(');
                        var bidx = release.Title.IndexOf('[');

                        if (aidx == -1)
                        {
                            aidx = release.Title.Length;
                        }

                        if (bidx == -1)
                        {
                            bidx = release.Title.Length;
                        }

                        var insertPoint = Math.Min(aidx, bidx);
                        release.Title = release.Title.Substring(0, insertPoint) + " Season 1 " + release.Title.Substring(insertPoint);
                    }

                    release.Categories = currentCategories;

                    //release.Description = row.QuerySelector("span.tags")?.TextContent;
                    release.Guid = _settings.BaseUrl + qTitleLink.GetAttribute("href");
                    release.InfoUrl = release.Guid;

                    release.DownloadUrl = _settings.BaseUrl + row.QuerySelector(".peers a").GetAttribute("href");

                    var grabs = row.QuerySelectorAll(".peers")[0].FirstChild.NodeValue.TrimEnd().TrimEnd('/').TrimEnd();
                    grabs = grabs.Replace("k", "000");
                    release.Grabs = int.Parse(grabs);
                    release.Seeders = int.Parse(row.QuerySelectorAll(".peers a")[0].TextContent);
                    release.Peers = release.Seeders + int.Parse(row.QuerySelectorAll(".peers a")[1].TextContent);

                    release.MinimumRatio = 1;
                    release.MinimumSeedTime = 172800; // 48 hours

                    var size = row.QuerySelector(".size").TextContent;
                    release.Size = ReleaseInfo.GetBytes(size);

                    //22 Jul 15
                    var dateStr = row.QuerySelector(".added").TextContent.Replace("'", string.Empty);
                    if (dateStr.Split(' ')[0].Length == 1)
                    {
                        dateStr = "0" + dateStr;
                    }

                    if (string.Equals(dateStr, "yesterday", StringComparison.InvariantCultureIgnoreCase))
                    {
                        release.PublishDate = DateTime.Now.AddDays(-1);
                    }
                    else if (string.Equals(dateStr, "today", StringComparison.InvariantCultureIgnoreCase))
                    {
                        release.PublishDate = DateTime.Now;
                    }
                    else
                    {
                        release.PublishDate = DateTime.ParseExact(dateStr, "dd MMM yy", CultureInfo.InvariantCulture);
                    }

                    release.DownloadVolumeFactor = row.QuerySelector("span.freeleech") != null ? 0 : 1;
                    release.UploadVolumeFactor = 1;

                    torrentInfos.Add(release);
                }
            }

            return torrentInfos.ToArray();
        }

        private ICollection<IndexerCategory> GetNextCategory(IElement row, ICollection<IndexerCategory> currentCategories)
        {
            var nextCategoryName = GetCategoryName(row);
            if (nextCategoryName != null)
            {
                currentCategories = _categories.MapTrackerCatDescToNewznab(nextCategoryName);
                if (currentCategories.Count == 0)
                {
                    return _defaultCategories;
                }
            }

            return currentCategories;
        }

        private string GetCategoryName(IElement row)
        {
            var categoryElement = row.QuerySelector("td.category span");
            if (categoryElement == null)
            {
                return null;
            }

            var categoryName = categoryElement.GetAttribute("title");

            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                return categoryName;
            }

            return null;
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class BakaBTSettingsValidator : AbstractValidator<BakaBTSettings>
    {
        public BakaBTSettingsValidator()
        {
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.Password).NotEmpty();
        }
    }

    public class BakaBTSettings : IIndexerSettings
    {
        private static readonly BakaBTSettingsValidator Validator = new BakaBTSettingsValidator();

        public BakaBTSettings()
        {
            Username = "";
            Password = "";
        }

        [FieldDefinition(1, Label = "Base Url", Type = FieldType.Select, SelectOptionsProviderAction = "getUrls", HelpText = "Select which baseurl Prowlarr will use for requests to the site")]
        public string BaseUrl { get; set; }

        [FieldDefinition(2, Label = "Username", HelpText = "Site Username", Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(3, Label = "Password", Type = FieldType.Password, HelpText = "Site Password", Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(4, Label = "Add Romaji Title", Type = FieldType.Checkbox, HelpText = "Add releases for Romaji Title")]
        public bool AddRomajiTitle { get; set; }

        [FieldDefinition(5, Label = "Append Season", Type = FieldType.Checkbox, HelpText = "Append Season for Sonarr Compatibility")]
        public bool AppendSeason { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
