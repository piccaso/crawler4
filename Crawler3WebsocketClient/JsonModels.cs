using System;
using System.Collections.Generic;

namespace Crawler3WebsocketClient {

    public class CrawlerConfig
    {
        public string UrlFilter { get; set; }
        public List<string> RequestQueue { get; set; }
        public bool CheckExternalLinks { get; set; }
        public bool FollowInternalLinks { get; set; }
        public long MaxRequestsPerCrawl { get; set; }
        public long MaxConcurrency { get; set; }
    }

    public class CrawlerResponseBase {
        public string Type { get; set; }
    }

    public class CrawlerResponseNode : CrawlerResponseBase {
        public int Status { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public bool External { get; set; }
        public object Errors { get; set; } //TODO!
        public string HtmlSource { get; set; }
        public string Text { get; set; }
        public byte[] Screenshot { get; set; }
        public double LoadTime { get; set; }
    }

    public class CrawlerResponseEdge : CrawlerResponseBase {
        public string Parent { get; set; }
        public string Child { get; set; }
        public string Relation { get; set; }
    }
}