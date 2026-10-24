using System.IO;
using System.Windows.Data;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// The shared result is published on whatever thread finishes the analysis; a page built on another (UI) thread must not touch its bound collections from
/// the publishing thread (CollectionView: "does not support changes to its SourceCollection from a thread different from the Dispatcher thread").
/// Root cause of the CollectAllTests flake seen on 2026-10-23: a MediaCenterViewModel left by a real-view test answered the publish of CollectAllTests.
/// </summary>
[Collection("CollectAllState")]
public sealed class PublishThreadAffinityTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("la-publish-").FullName;
    public void Dispose() { CollectAllState.Instance.Clear(); _k.Dispose(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void A_publish_from_another_thread_does_not_touch_the_media_page_collections_off_its_dispatcher()
    {
        MediaCenterViewModel? vm = null;
        var ui = new Thread(() =>
        {
            vm = new MediaCenterViewModel(new MediaRegisterViewModel(Path.Combine(_dir, "media.json")));
            CollectionViewSource.GetDefaultView(vm.SelectedDetails);   // a bound view on this thread, as the page's DataGrid makes
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();

        var result = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        var ex = Record.Exception(() => CollectAllState.Instance.Publish(result));
        Assert.Null(ex);
        GC.KeepAlive(vm);
    }
}
