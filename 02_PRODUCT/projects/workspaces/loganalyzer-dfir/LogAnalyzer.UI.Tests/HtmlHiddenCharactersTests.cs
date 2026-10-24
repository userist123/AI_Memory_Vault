using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>Research backlog #3: HTML reports print evidence-derived text without bidi / zero-width / control characters, with a visible marker.</summary>
    public class HtmlHiddenCharactersTests
    {
        [Fact]
        public void Html_encoding_removes_and_names_hidden_characters()
        {
            var html = HtmlText.Encode("user‮gnp.exe​<b>");
            Assert.DoesNotContain('‮', html);
            Assert.DoesNotContain('​', html);
            Assert.Contains("[caractere ascunse eliminate: U+200B, U+202E]", html);
            Assert.Contains("&lt;b&gt;", html);
        }

        [Fact]
        public void Ordinary_text_is_encoded_as_before()
        {
            Assert.Equal("a &amp; b", HtmlText.Encode("a & b"));
        }
    }
}
