using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Crawler3WebsocketClient.Tests {
    public class JsonTests {

        private class Json1Data {
            public int X { get; set; }
            public int Y { get; set; }  
        }

        [Test]
        public void Json1() {
            var o = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true,
            };
            var d = JsonSerializer.Deserialize<Json1Data>("{\"x\":1, \"y\":2}", o);
            TestContext.WriteLine(d.X);
            Assert.AreEqual(1, d.X);
        }

    }
}
