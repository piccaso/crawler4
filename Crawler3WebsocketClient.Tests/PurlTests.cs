using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Crawler3WebsocketClient.Tests {
    public class PurlTests {
        [Test]
        [TestCase("https://💩.la/index.html", true)]
        [TestCase("https://www.💩.la/oops", true)]
        [TestCase("https://sub.sub.sub.domains.💩.la/oops", true)]
        [TestCase("https://w.💩.la.com/fake", false)]
        public void PoopLa(string url, bool expected) {
            var purl = "http[s?]://[([\\w-]+\\.){0,}]💩.la/[.*]";
            var sut = new PseudoUrl(purl);
            var match = sut.Match(url);
            var success = match == expected;
            TestContext.WriteLine($"{url} -- exp:{expected}, match:{match}");
            Assert.IsTrue(success);
        }

        [Test]
        [TestCase("https://www.example.com/[.*]", "https://www.example.com/", true)]
        [TestCase("https://www.example.com/[.*]", "https://www.example.com/main", true)]
        [TestCase("https://www.example.com/[.*]", "https://www2.example.com/", false)]
        public void Generic(string purl, string url, bool expected) {
            var sut = new PseudoUrl(purl);
            var match = sut.Match(url);
            var success = match == expected;
            TestContext.WriteLine($"{url} -- exp:{expected}, match:{match}");
            Assert.IsTrue(success);
        }

        [Test]
        [TestCase(".*$^()", "^\\.\\*\\$\\^\\(\\)$")]
        [TestCase("http://orf.at/", "^http://orf\\.at/$")]
        public void Escape(string purl, string expected) {
            var actual = PseudoUrl.ParsePurl(purl);
            TestContext.WriteLine(actual);
            Assert.AreEqual(expected, actual);
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void Throws(string purl) {
            Assert.Throws<ArgumentException>(() => { _ = new PseudoUrl(purl); });
        }

        [Test]
        public async Task ChannelTestAsync() {
            var c = Channel.CreateUnbounded<string>();
            c.Writer.TryComplete();
            c.Writer.TryComplete();
            await Task.Delay(1);
            c.Writer.TryWrite("lala");
        }
    }
}