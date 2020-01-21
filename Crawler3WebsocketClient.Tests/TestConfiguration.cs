using Microsoft.Extensions.Configuration;

namespace Crawler3WebsocketClient.Tests {
    public class TestConfiguration {
        private readonly IConfiguration _config;
        public TestConfiguration() {
            _config = new ConfigurationBuilder()
                     .AddUserSecrets<TestConfiguration>()
                     .AddEnvironmentVariables()
                     .Build();
        }

        public string this[string key] => _config[key];
        public string CrawlerWebsocketUrl => _config["CrawlerWebsocketUrl"];
    }
}