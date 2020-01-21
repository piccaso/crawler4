using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crawler3WebsocketClient;
using SQLite;

namespace TestCli {

    public abstract class SqliteBase {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }
    }

    public abstract class CrawlRelated : SqliteBase {
        [Indexed]
        public long CrawlId { get; set; }
    }

    public class Crawl : SqliteBase {
        public string BaseUrl { get; set; }
    }

    public class Node : CrawlRelated {
        public int Status { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public bool External { get; set; }
        public string Errors { get; set; }
        public string HtmlSource { get; set; }
        public string Text { get; set; }
        public byte[] Screenshot { get; set; }
        public double LoadTime { get; set; }
    }

    public class Edge : CrawlRelated {
        public string Parent { get; set; }
        public string Child { get; set; }
        public string Relation { get; set; }
    }
    
    public class Db : IDisposable {
        private readonly SQLiteConnection _db;
        public Db(string databasePath) {
            _db = new SQLiteConnection(databasePath);
            _db.CreateTables<Crawl, Node, Edge>();
        }

        public long NewCrawl(string baseUrl) {
            var c = new Crawl {BaseUrl = baseUrl};
            _db.Insert(c);
            return c.Id;
        }

        public void StoreNode(long crawlId, CrawlerResponseNode node) {
            var n = new Node {
                CrawlId = crawlId,
                HtmlSource = node.HtmlSource,
                External = node.External,
                LoadTime = node.LoadTime,
                Screenshot = node.Screenshot,
                Status = node.Status,
                Text = node.Text,
                Title = node.Title,
                Url = node.Url,
            };

            if (node.Errors != null && node.Errors.Any()) {
                n.Errors = string.Join("\n", node.Errors);
            }

            _db.Insert(n);
        }

        public void StoreEdge(long crawlId, CrawlerResponseEdge edge) {
            var e = new Edge {
                CrawlId = crawlId,
                Child = edge.Child,
                Parent = edge.Parent,
                Relation = edge.Relation,
            };
            _db.Insert(e);
        }

        public void Dispose() {
            _db.Dispose();
        }
    }
}
