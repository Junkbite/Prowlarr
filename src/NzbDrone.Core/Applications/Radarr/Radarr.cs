using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentValidation.Results;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Applications.Radarr
{
    public class Radarr : ApplicationBase<RadarrSettings>
    {
        public override string Name => "Radarr";

        private readonly IRadarrV3Proxy _radarrV3Proxy;
        private readonly ICached<List<RadarrIndexer>> _schemaCache;
        private readonly IConfigFileProvider _configFileProvider;

        public Radarr(ICacheManager cacheManager, IRadarrV3Proxy radarrV3Proxy, IConfigFileProvider configFileProvider, IAppIndexerMapService appIndexerMapService, Logger logger)
            : base(appIndexerMapService, logger)
        {
            _schemaCache = cacheManager.GetCache<List<RadarrIndexer>>(GetType());
            _radarrV3Proxy = radarrV3Proxy;
            _configFileProvider = configFileProvider;
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            var testIndexer = new IndexerDefinition
            {
                Id = 0,
                Name = "Test",
                Protocol = DownloadProtocol.Usenet,
                Capabilities = new IndexerCapabilities()
            };

            testIndexer.Capabilities.Categories.AddCategoryMapping(1, NewznabStandardCategory.Movies);

            try
            {
                failures.AddIfNotNull(_radarrV3Proxy.TestConnection(BuildRadarrIndexer(testIndexer, DownloadProtocol.Usenet), Settings));
            }
            catch (WebException ex)
            {
                _logger.Error(ex, "Unable to send test message");
                failures.AddIfNotNull(new ValidationFailure("BaseUrl", "Unable to complete application test, cannot connect to Radarr"));
            }

            return new ValidationResult(failures);
        }

        public override Dictionary<int, int> GetIndexerMappings()
        {
            var indexers = _radarrV3Proxy.GetIndexers(Settings)
                                         .Where(i => i.Implementation == "Newznab" || i.Implementation == "Torznab");

            var mappings = new Dictionary<int, int>();

            foreach (var indexer in indexers)
            {
                if ((string)indexer.Fields.FirstOrDefault(x => x.Name == "apiKey")?.Value == _configFileProvider.ApiKey)
                {
                    var match = AppIndexerRegex.Match((string)indexer.Fields.FirstOrDefault(x => x.Name == "baseUrl").Value);

                    if (match.Groups["indexer"].Success && int.TryParse(match.Groups["indexer"].Value, out var indexerId))
                    {
                        //Add parsed mapping if it's mapped to a Indexer in this Prowlarr instance
                        mappings.Add(indexer.Id, indexerId);
                    }
                }
            }

            return mappings;
        }

        public override void AddIndexer(IndexerDefinition indexer)
        {
            if (indexer.Capabilities.Categories.SupportedCategories(Settings.SyncCategories.ToArray()).Any())
            {
                var radarrIndexer = BuildRadarrIndexer(indexer, indexer.Protocol);

                var remoteIndexer = _radarrV3Proxy.AddIndexer(radarrIndexer, Settings);
                _appIndexerMapService.Insert(new AppIndexerMap { AppId = Definition.Id, IndexerId = indexer.Id, RemoteIndexerId = remoteIndexer.Id });
            }
        }

        public override void RemoveIndexer(int indexerId)
        {
            var appMappings = _appIndexerMapService.GetMappingsForApp(Definition.Id);

            var indexerMapping = appMappings.FirstOrDefault(m => m.IndexerId == indexerId);

            if (indexerMapping != null)
            {
                //Remove Indexer remotely and then remove the mapping
                _radarrV3Proxy.RemoveIndexer(indexerMapping.RemoteIndexerId, Settings);
                _appIndexerMapService.Delete(indexerMapping.Id);
            }
        }

        public override void UpdateIndexer(IndexerDefinition indexer)
        {
            _logger.Debug("Updating indexer {0} [{1}]", indexer.Name, indexer.Id);

            var appIndexerProfiles = indexer.AppProfile.FindAll(x => x.Value.ApplicationIDs.Contains(Definition.Id));

            if (appIndexerProfiles.Count >= 1)
            {
                var appMappings = _appIndexerMapService.GetMappingsForApp(Definition.Id);
                var indexerMapping = appMappings.FirstOrDefault(m => m.IndexerId == indexer.Id);

                var radarrIndexer = BuildRadarrIndexer(indexer, indexer.Protocol, indexerMapping?.RemoteIndexerId ?? 0);

                var remoteIndexer = _radarrV3Proxy.GetIndexer(indexerMapping.RemoteIndexerId, Settings);

                if (remoteIndexer != null)
                {
                    _logger.Debug("Remote indexer found, syncing with current settings");

                    if (!radarrIndexer.Equals(remoteIndexer))
                    {
                        _radarrV3Proxy.UpdateIndexer(radarrIndexer, Settings);
                    }
                }
                else
                {
                    _appIndexerMapService.Delete(indexerMapping.Id);

                    if (indexer.Capabilities.Categories.SupportedCategories(Settings.SyncCategories.ToArray()).Any())
                    {
                        _logger.Debug("Remote indexer not found, re-adding {0} to Radarr", indexer.Name);
                        radarrIndexer.Id = 0;
                        var newRemoteIndexer = _radarrV3Proxy.AddIndexer(radarrIndexer, Settings);
                        _appIndexerMapService.Insert(new AppIndexerMap
                        { AppId = Definition.Id, IndexerId = indexer.Id, RemoteIndexerId = newRemoteIndexer.Id });
                    }
                    else
                    {
                        _logger.Debug(
                            "Remote indexer not found for {0}, skipping re-add to Radarr due to indexer capabilities",
                            indexer.Name);
                    }
                }
            }
        }

        private RadarrIndexer BuildRadarrIndexer(IndexerDefinition indexer, DownloadProtocol protocol, int id = 0)
        {
            var cacheKey = $"{Settings.BaseUrl}";
            var schemas = _schemaCache.Get(cacheKey, () => _radarrV3Proxy.GetIndexerSchema(Settings), TimeSpan.FromDays(7));

            var newznab = schemas.Where(i => i.Implementation == "Newznab").First();
            var torznab = schemas.Where(i => i.Implementation == "Torznab").First();

            var schema = protocol == DownloadProtocol.Usenet ? newznab : torznab;

            var enableRss = true;
            var enableAutoSearch = true;
            var enableInteractiveSearch = true;

            var enableRssEnabled = indexer.AppProfile.Any(x => x.Value.EnableRss);
            var enableRssDisabled = indexer.AppProfile.Any(x => !x.Value.EnableRss);
            var enableAutoSearchEnabled = indexer.AppProfile.Any(x => x.Value.EnableAutomaticSearch);
            var enableAutoSearchDisabled = indexer.AppProfile.Any(x => !x.Value.EnableAutomaticSearch);
            var enableInteractiveSearchEnabled = indexer.AppProfile.Any(x => x.Value.EnableInteractiveSearch);
            var enableInteractiveSearchDisabled = indexer.AppProfile.Any(x => !x.Value.EnableInteractiveSearch);

            if (!enableRssEnabled && enableRssDisabled)
            {
                enableRss = false;
            }

            if (!enableAutoSearchEnabled && enableAutoSearchDisabled)
            {
                enableAutoSearch = false;
            }

            if (!enableInteractiveSearchEnabled && enableInteractiveSearchDisabled)
            {
                enableInteractiveSearch = false;
            }

            var radarrIndexer = new RadarrIndexer
            {
                Id = id,
                Name = $"{indexer.Name} (Prowlarr)",
                EnableRss = indexer.Enable && enableRss,
                EnableAutomaticSearch = indexer.Enable && enableAutoSearch,
                EnableInteractiveSearch = indexer.Enable && enableInteractiveSearch,
                Priority = indexer.Priority,
                Implementation = indexer.Protocol == DownloadProtocol.Usenet ? "Newznab" : "Torznab",
                ConfigContract = schema.ConfigContract,
                Fields = schema.Fields,
            };

            radarrIndexer.Fields.FirstOrDefault(x => x.Name == "baseUrl").Value = $"{Settings.ProwlarrUrl.TrimEnd('/')}/{indexer.Id}/";
            radarrIndexer.Fields.FirstOrDefault(x => x.Name == "apiPath").Value = "/api";
            radarrIndexer.Fields.FirstOrDefault(x => x.Name == "apiKey").Value = _configFileProvider.ApiKey;
            radarrIndexer.Fields.FirstOrDefault(x => x.Name == "categories").Value = JArray.FromObject(indexer.Capabilities.Categories.SupportedCategories(Settings.SyncCategories.ToArray()));

            return radarrIndexer;
        }
    }
}
