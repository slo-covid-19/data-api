﻿using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Prometheus;
using SloCovidServer.Models;
using SloCovidServer.Services.Abstract;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SloCovidServer.Services.Implemented
{
    public class Communicator : ICommunicator
    {
        const string root = "https://raw.githubusercontent.com/slo-covid-19/data/master/csv";
        readonly HttpClient client;
        readonly ILogger<Communicator> logger;
        readonly Mapper mapper;
        protected static readonly Histogram RequestDuration = Metrics.CreateHistogram("source_request_duration_milliseconds",
            "Request duration to CSV sources in milliseconds",
            new HistogramConfiguration
            {
                Buckets = Histogram.ExponentialBuckets(start: 20, factor: 2, count: 10),
                LabelNames = new[] { "endpoint", "is_exception" }
            });
        protected static readonly Counter RequestCount = Metrics.CreateCounter("source_request_total", "Total number of requests to source",
            new CounterConfiguration
            {
                LabelNames = new[] { "endpoint" }
            });
        protected static readonly Counter RequestMissedCache = Metrics.CreateCounter("source_request_missed_cache_total", 
            "Total number of missed cache when fetching from source",
            new CounterConfiguration
            {
                LabelNames = new[] { "endpoint" }
            });
        protected static readonly Counter RequestExceptions = Metrics.CreateCounter("source_request_exceptions_total", 
            "Total number of exceptions when fetching data from source",
            new CounterConfiguration
            {
                LabelNames = new[] { "endpoint" }
            });
        readonly ArrayEndpointCache<StatsDaily> statsCache;
        readonly ArrayEndpointCache<RegionsDay> regionCache;
        readonly ArrayEndpointCache<PatientsDay> patientsCache;
        readonly ArrayEndpointCache<HospitalsDay> hospitalsCache;
        readonly ArrayEndpointCache<Hospital> hospitalsListCache;
        readonly ArrayEndpointCache<Municipality> municipalitiesListCache;
        readonly ArrayEndpointCache<RetirementHome> retirementHomesListCache;
        public Communicator(ILogger<Communicator> logger, Mapper mapper)
        {
            client = new HttpClient();
            this.logger = logger;
            this.mapper = mapper;
            statsCache = new ArrayEndpointCache<StatsDaily>();
            regionCache = new ArrayEndpointCache<RegionsDay>();
            patientsCache = new ArrayEndpointCache<PatientsDay>();
            hospitalsCache = new ArrayEndpointCache<HospitalsDay>();
            hospitalsListCache = new ArrayEndpointCache<Hospital>();
            municipalitiesListCache = new ArrayEndpointCache<Municipality>();
            retirementHomesListCache = new ArrayEndpointCache<RetirementHome>();
        }

        public async Task<(ImmutableArray<StatsDaily>? Data, string ETag)> GetStatsAsync(string callerEtag, CancellationToken ct)
        {
            var result = await GetAsync(callerEtag, $"{root}/stats.csv", statsCache, mapFromString: mapper.GetStatsFromRaw, ct);
            return result;
        }

        public async Task<(ImmutableArray<RegionsDay>? Data, string ETag)> GetRegionsAsync(string callerEtag, CancellationToken ct)
        {
            var result = await GetAsync(callerEtag, $"{root}/regions.csv",regionCache, mapFromString: mapper.GetRegionsFromRaw, ct);
            return result;
        }

        public async Task<(ImmutableArray<PatientsDay>? Data, string ETag)> GetPatientsAsync(string callerEtag, CancellationToken ct)
        {
            var result = await GetAsync(callerEtag, $"{root}/patients.csv", patientsCache, mapFromString: mapper.GetPatientsFromRaw, ct);
            return result;
        }

        public async Task<(ImmutableArray<HospitalsDay>? Data, string ETag)> GetHospitalsAsync(string callerEtag, CancellationToken ct)
        {
            var result = await GetAsync(callerEtag, $"{root}/hospitals.csv", hospitalsCache, mapFromString: mapper.GetHospitalsFromRaw, ct);
            return result;
        }

        public async Task<(ImmutableArray<Hospital>? Data, string ETag)> GetHospitalsListAsync(string callerEtag, CancellationToken ct)
        {
            var result = await GetAsync(callerEtag, $"{root}/dict-hospitals.csv",hospitalsListCache, mapFromString: mapper.GetHospitalsListFromRaw, ct);
            return result;
        }

        public async Task<(ImmutableArray<Municipality>? Data, string ETag)> GetMunicipalitiesListAsync(string callerEtag, CancellationToken ct)
        {
            var result = await GetAsync(callerEtag, $"{root}/dict-municipality.csv",municipalitiesListCache, mapFromString: mapper.GetMunicipalitiesListFromRaw, ct);
            return result;
        }

        public async Task<(ImmutableArray<RetirementHome>? Data, string ETag)> GetRetirementHomesListAsync(string callerEtag, CancellationToken ct)
        {
            var result = await GetAsync(callerEtag, $"{root}/dict-retirement_homes.csv", retirementHomesListCache, 
                mapFromString: mapper.GetRetirementHomesListFromRaw, ct);
            return result;
        }

        public class RegionsPivotCacheData
        {
            public ETagCacheItem<ImmutableArray<Municipality>> Municipalities { get; }
            public ETagCacheItem<ImmutableArray<RegionsDay>> Regions { get; }
            public ImmutableArray<ImmutableArray<object>> Data { get;}
            public RegionsPivotCacheData(ETagCacheItem<ImmutableArray<Municipality>> municipalities, ETagCacheItem<ImmutableArray<RegionsDay>> regions,
                ImmutableArray<ImmutableArray<object>> data)
            {
                Municipalities = municipalities;
                Regions = regions;
                Data = data;
            }
        }
        RegionsPivotCacheData regionsPivotCacheData = new RegionsPivotCacheData(
            new ETagCacheItem<ImmutableArray<Municipality>>(null, ImmutableArray<Municipality>.Empty),
            new ETagCacheItem<ImmutableArray<RegionsDay>>(null, ImmutableArray<RegionsDay>.Empty),
            data: ImmutableArray<ImmutableArray<object>>.Empty
        );
        readonly object syncRegionsPivot = new object();
        public async Task<(ImmutableArray<ImmutableArray<object>>? Data, string ETag)>  GetRegionsPivotAsync(string callerEtag, CancellationToken ct)
        {
            string[] callerETags = !string.IsNullOrEmpty(callerEtag) ? callerEtag.Split(',') : new string[2];
            if (callerETags.Length != 2)
            {
                callerETags = new string[2];
            }
            RegionsPivotCacheData localCache;
            lock(syncRegionsPivot)
            {
                localCache = regionsPivotCacheData;
            }
            var muncipalityTask = GetMunicipalitiesListAsync(localCache.Municipalities.ETag, ct);
            var regions = await GetRegionsAsync(localCache.Regions.ETag, ct);
            var municipalities = await muncipalityTask;
            if (regions.Data.HasValue || municipalities.Data.HasValue)
            {
                var data = mapper.MapRegionsPivot(municipalities.Data ?? localCache.Municipalities.Data, regions.Data ?? localCache.Regions.Data);
                localCache = new RegionsPivotCacheData(
                    municipalities.Data.HasValue ? 
                        new ETagCacheItem<ImmutableArray<Municipality>>(municipalities.ETag, municipalities.Data ?? ImmutableArray<Municipality>.Empty)
                        : localCache.Municipalities,
                    regions.Data.HasValue ? 
                        new ETagCacheItem<ImmutableArray<RegionsDay>>(regions.ETag, regions.Data ?? ImmutableArray<RegionsDay>.Empty)
                        : localCache.Regions,
                    data
                );
                lock(syncRegionsPivot)
                {
                    regionsPivotCacheData = localCache;
                }
                return (data, $"{municipalities.ETag},{regions.ETag}");
            }
            else
            {
                string resultTag = $"{municipalities.ETag},{regions.ETag}";
                if (string.Equals(callerETags[0], localCache.Municipalities.ETag, StringComparison.Ordinal)
                    && string.Equals(callerETags[1], localCache.Regions.ETag, StringComparison.Ordinal))
                {
                    return (null, resultTag);
                }
                else
                {
                    return (localCache.Data, resultTag);
                }
            }
        }

        async Task<(TData? Data, string ETag)> GetAsync<TData>(string callerEtag, string url, EndpointCache<TData> sync,
            Func<string, TData> mapFromString, CancellationToken ct)
            where TData: struct
        {
            RequestCount.WithLabels(url).Inc();
            var stopwatch = Stopwatch.StartNew();
            var policy = HttpPolicyExtensions
              .HandleTransientHttpError()
              .RetryAsync(3);

            ETagCacheItem<TData> current = sync.Cache;

            bool isException = false;
            try
            {
                var response = await policy.ExecuteAsync(() =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (!string.IsNullOrEmpty(current.ETag))
                    {
                        request.Headers.Add("If-None-Match", current.ETag);
                    }
                    return client.SendAsync(request, ct);
                });

                string etagInfo = $"ETag {(string.IsNullOrEmpty(callerEtag) ? "none" : "present")}";
                if (response.IsSuccessStatusCode)
                {
                    RequestMissedCache.WithLabels(url).Inc();
                    string newETag = response.Headers.GetValues("ETag").SingleOrDefault();
                    string content = await response.Content.ReadAsStringAsync();
                    var newData = mapFromString(content);
                    current = new ETagCacheItem<TData>(newETag, newData);
                    lock (sync)
                    {
                        sync.Cache = current;
                    }
                    if (string.Equals(current.ETag, callerEtag, StringComparison.Ordinal))
                    {
                        logger.LogInformation($"Cache refreshed, client cache hit, {etagInfo}");
                        return (null, current.ETag);
                    }
                    logger.LogInformation($"Cache refreshed, client refreshed, {etagInfo}");
                    return (current.Data, current.ETag);
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    if (string.Equals(current.ETag, callerEtag, StringComparison.Ordinal))
                    {
                        logger.LogInformation($"Cache hit, client cache hit, {etagInfo}");
                        return (null, current.ETag);
                    }
                    else
                    {
                        logger.LogInformation($"Cache hit, client cache refreshed, {etagInfo}");
                        return (current.Data, current.ETag);
                    }
                }
                throw new Exception($"Failed fetching data: {response.ReasonPhrase}");
            }
            catch
            {
                isException = true;
                RequestExceptions.WithLabels(url).Inc();
                throw;
            }
            finally
            {
                RequestDuration.WithLabels(url, isException.ToString()).Observe(stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
