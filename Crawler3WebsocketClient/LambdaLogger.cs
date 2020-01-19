using System;
using System.Text;

namespace Crawler3WebsocketClient {
    public class LambdaLogger : IWebsocketLogger {
        private readonly Action<string> _info;
        private readonly Action<string> _warning;
        private readonly Action<string> _error;
        public LambdaLogger(Action<string> writeLogLine) {
            _info = (s) => writeLogLine("I>" + s);
            _warning = (s) => writeLogLine("W>" + s);
            _error = (s) => writeLogLine("E>" + s);
        }

        public LambdaLogger(Action<string> info, Action<string> warning, Action<string> error) {
            _info = info;
            _warning = warning;
            _error = error;
        }

        private static string Log(string message, Exception exception) {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(message)) sb.Append(message);
            if (exception != null) {
                sb.AppendLine();
                sb.Append(exception);
            }
            return sb.ToString();
        }

        public void LogInfo(string message, Exception exception = null) => _info?.Invoke(Log(message, exception));
        public void LogWarn(string message, Exception exception = null) => _warning?.Invoke(Log(message, exception));
        public void LogError(string message, Exception exception = null) => _error?.Invoke(Log(message, exception));
    }
}