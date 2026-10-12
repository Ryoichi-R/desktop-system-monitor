using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

[Collection("WpfWindow")]
public sealed class VersionInformationWindowTests
{
    [Fact]
    public void Version_Tab_Uses_Shared_Ordered_Information_Without_Changing_Draft()
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var window = new SettingsWindow(settings);
                try
                {
                    var tabs = (TabControl)window.FindName("CategoryTabs");
                    var last = (TabItem)tabs.Items[tabs.Items.Count - 1];
                    Assert.Equal("バージョン情報", last.Header);
                    var fields = (ItemsControl)window.FindName("VersionFields");
                    Assert.Equal(BuildInformation.Current.Fields, Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, string>>>(fields.ItemsSource));
                    Assert.Equal("情報をコピー", ((Button)window.FindName("CopyVersionInformationButton")).Content);
                    Assert.Equal(settings.SamplingIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), ((TextBox)window.FindName("IntervalBox")).Text);
                }
                finally { window.Close(); }
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
