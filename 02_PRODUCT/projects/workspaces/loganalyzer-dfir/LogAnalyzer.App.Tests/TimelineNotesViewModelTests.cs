using System;
using System.Linq;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.Services;
using Xunit;
namespace LogAnalyzer.App.Tests;

/// <summary>Master plan 6.9 on the „Cronologie” tab: source filter, bookmark, note, saved view, applying it again and comparing two views.</summary>
[Collection("CollectAllState")]
public sealed class TimelineNotesViewModelTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { CollectAllState.Instance.Clear(); _k.Dispose(); }

    [Fact]
    public void An_operator_filters_marks_annotates_saves_and_compares_views()
    {
        var loaded = CaseLoader.OpenAsync(_k.Case(_k.File("a.bin", "alpha")).Root).GetAwaiter().GetResult().Case!;
        var r = loaded.Result;
        r.Timeline.Clear();
        r.Timeline.AddRange([GapsTestKit.Ev("EventLog:Security", 1, "4625", user: "ana"), GapsTestKit.Ev("EventLog:Security", 2, "4624", user: "ana"), GapsTestKit.UsbRow(3)]);
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel();
        vm.ShowLoaded(loaded);
        Assert.Equal(3, vm.Timeline.Count);
        Assert.True(vm.TimelineSources.Count >= 3);

        vm.SelectedTimelineSource = r.Timeline[2].Source;
        Assert.Single(vm.Timeline);
        vm.SelectedTimelineSource = LogAnalyzer.UI.ViewModels.InvestigationViewModel.AllSources;

        vm.SelectedTimelineEvent = vm.Timeline.First(e => e.EventId == "4625");
        vm.AnnotationText = "prima încercare eșuată";
        vm.ToggleBookmarkCommand.Execute(null);
        Assert.Equal("Evenimentul a fost marcat.", vm.TimelineNotesLine);
        vm.AnnotationText = "Corelat cu 4624 la un minut distanță.";
        vm.AddAnnotationCommand.Execute(null);
        Assert.Contains(vm.SelectedEventNotes, n => n.Contains("Corelat cu 4624"));
        Assert.Equal("Scoate marcajul", vm.BookmarkButtonText);

        vm.TimelineOnlyBookmarked = true;
        Assert.Equal("4625", Assert.Single(vm.Timeline).EventId);
        vm.NewViewName = "Marcate";
        vm.SaveTimelineViewCommand.Execute(null);
        vm.TimelineOnlyBookmarked = false;
        vm.NewViewName = "Tot";
        vm.SaveTimelineViewCommand.Execute(null);
        Assert.Equal(["Marcate", "Tot"], vm.SavedViewNames.ToArray());

        vm.SelectedSavedView = "Marcate";
        vm.ApplySavedViewCommand.Execute(null);
        Assert.True(vm.TimelineOnlyBookmarked);
        Assert.Single(vm.Timeline);
        vm.CompareViewName = "Tot";
        vm.CompareViewsCommand.Execute(null);
        Assert.Contains("comune 1", vm.TimelineNotesLine);
        Assert.Contains("doar în „Tot” 2", vm.TimelineNotesLine);

        vm.TimelineFromText = "nu este dată";                 // „doar marcate” is still on from the applied view
        vm.ApplyTimelineFilterCommand.Execute(null);
        Assert.StartsWith("Data se scrie", vm.TimelineNotesLine);
        Assert.Contains("Intervalul NU este aplicat", vm.TimelineNotesLine);
        Assert.Single(vm.Timeline);                              // the bookmark filter is not silently dropped with the bad date

        vm.TimelineFromText = ""; vm.TimelineOnlyBookmarked = false;
        vm.TimelineToText = r.Timeline[0].Time.Utc!.Value.ToString("yyyy-MM-dd HH:mm");
        vm.ApplyTimelineFilterCommand.Execute(null);
        Assert.Single(vm.Timeline);                              // „până la hh:mm” includes that whole minute
    }
}
