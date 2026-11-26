using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

[CollectionDefinition("PlainStory.CurrentPersonOf", DisableParallelization = true)]
public sealed class PlainStoryCurrentCollection;

/// <summary>A person added to the users register after the analysis is named on the card and in the report (the stored sentence is told again).</summary>
[Collection("PlainStory.CurrentPersonOf")]
public sealed class PlainStoryCurrentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 13, 15, 0, TimeSpan.Zero);

    [Fact]
    public void The_sentence_is_told_again_with_the_register_the_application_holds_now_unless_the_account_is_a_sid()
    {
        var f = new Finding
        {
            FindingId = "F-1", RuleId = "AIRGAP-PRINT-JOB", Title = "t", Description = "d", Severity = Severity.Low, User = "labadmin", FirstSeenUtc = T0,
            PlainWhat = "a tipărit documentul „Plan.docx”", Plain = "stored: utilizatorul cu contul „labadmin” a tipărit",
        };
        var bySid = new Finding
        {
            FindingId = "F-2", RuleId = "PS-SUSPICIOUS", Title = "t", Description = "d", Severity = Severity.Low, User = "S-1-5-21-1-2-3-1001", FirstSeenUtc = T0,
            PlainWhat = "a rulat un script", Plain = "stored: Popescu Ion (contul ion) a rulat un script",
        };
        var before = PlainStory.CurrentPersonOf;
        try
        {
            PlainStory.CurrentPersonOf = null;
            Assert.Equal(f.Plain, PlainStory.Current(f));
            PlainStory.CurrentPersonOf = new UsersRegister { Rows = [new UserRow { Person = "Ionescu Maria", Accounts = "labadmin" }] }.PersonOf;
            Assert.Contains("Ionescu Maria (contul labadmin) a tipărit documentul „Plan.docx”", PlainStory.Current(f));
            Assert.Equal(bySid.Plain, PlainStory.Current(bySid));   // resolved at analysis from the case's events, kept
        }
        finally { PlainStory.CurrentPersonOf = before; }
    }
}
