using System;
using System.Globalization;
using System.Net;

namespace LogAnalyzer.Core.Services
{
    /// <summary>
    /// The one helper every HTML report uses for interpolated values. Report text is evidence-derived
    /// (usernames, event messages, rule output) and therefore attacker-controlled: every value goes
    /// through <see cref="Encode(object?)"/>, which escapes &lt; &gt; &amp; " and ' so it is safe both as
    /// element text and inside a quoted attribute.
    /// </summary>
    public static class HtmlText
    {
        /// <summary>
        /// CSP for generated reports: no script, no network, no frames; inline styles only (the reports
        /// carry their stylesheet inline).
        /// </summary>
        public const string ContentSecurityPolicy =
            "default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'";

        /// <summary>The CSP as a &lt;meta&gt; element, to be placed first in &lt;head&gt;.</summary>
        public static string CspMetaTag =>
            "<meta http-equiv=\"Content-Security-Policy\" content=\"" + ContentSecurityPolicy + "\">";

        public static string Encode(object? value)
        {
            if (value is null) return string.Empty;
            var text = value is IFormattable f ? f.ToString(null, CultureInfo.CurrentCulture) : value.ToString();
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }
    }
}
