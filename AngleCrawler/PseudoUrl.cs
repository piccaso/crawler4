using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AngleCrawler {
    public class PseudoUrl {
        public PseudoUrl(string purl) {
            _regex = new Regex(ParsePurl(purl));
        }

        private readonly Regex _regex;

        public bool Match(string url) {
            return url != null && _regex.Match(url).Success;
        }

        /// <summary>
        ///     Parses PURL into Regex string.
        ///     Based on
        ///     https://github.com/apifytech/apify-js/blob/8336ea4ca6c2a5556500a2ec88f29bceef49a6ff/src/pseudo_url.js#L6-L50
        ///     (Apache 2.0 License)
        /// </summary>
        /// <param name="purl">PseudoURL</param>
        /// <returns>Regex string</returns>
        internal static string ParsePurl(string purl) {
            var trimmedPurl = purl?.Trim();
            if (string.IsNullOrEmpty(trimmedPurl))
                throw new ArgumentException($"Cannot parse PURL '{purl}': it must be an non-empty string.");

            var regex = new StringBuilder();
            regex.Append("^");

            var openBrackets = 0;
            foreach (var ch in trimmedPurl)
                if (ch == '[' && ++openBrackets == 1) {
                    // Beginning of '[regex]' section
                    // Enclose regex in () brackets to enforce operator priority
                    regex.Append('(');
                }
                else if (ch == ']' && openBrackets > 0 && --openBrackets == 0) {
                    // End of '[regex]' section
                    regex.Append(')');
                }
                else if (openBrackets > 0) {
                    // Inside '[regex]' section
                    regex.Append(ch);
                }
                else {
                    // Outside '[regex]' section, parsing the URL part
                    var escaped = Regex.Escape(ch.ToString());
                    regex.Append(escaped);
                }

            regex.Append("$");
            return regex.ToString();
        }
    }
}