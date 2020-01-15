using System;

namespace Crawler3WebsocketClient {
    public interface IWebsocketLogger {
        void LogInfo(string message, Exception exception = null);
        void LogWarn(string message, Exception exception = null);
        void LogError(string message, Exception exception = null);
    }
}
