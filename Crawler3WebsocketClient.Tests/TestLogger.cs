using System;
using System.Text;

namespace Crawler3WebsocketClient.Tests {
    public class TestLogger : IWebsocketLogger {
        private readonly Action<string> _writeLogLine;
        public TestLogger(Action<string> writeLogLine) {
            _writeLogLine = writeLogLine;
        }

        private void Log(string prefix, string message, Exception exception) {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(prefix)) sb.Append(prefix);
            if (!string.IsNullOrWhiteSpace(message)) sb.Append(message);
            if (exception != null) {
                sb.AppendLine();
                sb.Append(exception);
            }
            _writeLogLine?.Invoke(sb.ToString());
        }

        public void LogInfo(string message, Exception exception = null) => Log("I>", message, exception);
        public void LogWarn(string message, Exception exception = null) => Log("W>", message, exception);
        public void LogError(string message, Exception exception = null) => Log("E>", message, exception);
    }
}