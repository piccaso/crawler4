using System;
using System.Collections.Generic;

namespace Crawler3WebsocketClient {

    public class CrawlerConfig
    {
        public string UrlFilter { get; set; }
        public ICollection<string> RequestQueue { get; set; } = new List<string>();
        public bool CheckExternalLinks { get; set; }
        public bool FollowInternalLinks { get; set; }
        public bool TakeScreenShots { get; set; }
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
        public string[] Errors { get; set; }
        public string HtmlSource { get; set; }
        public string Text { get; set; }
        public byte[] ScreenShot { get; set; }
        public double LoadTime { get; set; }
    }

    public class CrawlerResponseEdge {
        public string Parent { get; set; }
        public string Child { get; set; }
        public string Relation { get; set; }
    }

    public class CrawlerResponseEdges : CrawlerResponseBase {
        public ICollection<CrawlerResponseEdge> Edges { get; set; }
    }

    public class CrawlerResponseStatus : CrawlerResponseBase {
        public ulong TotalRequestCount { get; set; }
        public ulong HandledRequestCount { get; set; }
        public ulong PendingRequestCount { get; set; }
    }
}