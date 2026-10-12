using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using DesktopSystemMonitor.App;
using Xunit;

namespace DesktopSystemMonitor.Build.Tests;

public sealed class BuildInformationTests
{
    [Theory]
    [InlineData("Windows", Architecture.X64, "Windows / x64")]
    [InlineData("Windows", Architecture.Arm64, "Windows / ARM64")]
    [InlineData("macOS", Architecture.X64, "macOS / x64")]
    [InlineData("macOS", Architecture.Arm64, "macOS / ARM64")]
    public void Execution_Uses_App_Architecture(string os, Architecture appArchitecture, string expected)
    {
        Assert.Equal(expected, BuildInformation.Read(Fixture("false"), os, appArchitecture).Execution);
    }

    [Theory]
    [InlineData("true", "あり")]
    [InlineData("false", "なし")]
    [InlineData("unknown", "不明")]
    public void Copy_Preserves_Labels_Order_And_Embedded_Utc(string dirty, string expected)
    {
        var info = BuildInformation.Read(Fixture(dirty), "Windows", Architecture.X64);
        Assert.Equal(new[] { "アプリ名", "バージョン", "ソース識別子", "未コミット変更の有無", "ビルド日時", "実行版" }, info.Fields.Select(field => field.Key));
        Assert.Equal("0.2.0", info.Version);
        Assert.Equal("abcdef012345", info.SourceId);
        Assert.Equal(expected, info.Dirty);
        Assert.Equal("2026-01-02 03:04:05 UTC (+00:00)", info.BuiltUtc);
        Assert.Equal(string.Join(Environment.NewLine, info.Fields.Select(field => $"{field.Key}: {field.Value}")), info.CopyText);
    }

    [Fact]
    public void Missing_Metadata_Is_Unknown_Without_File_Or_Git_Fallback()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.Run);
        var info = BuildInformation.Read(assembly, "macOS", Architecture.Arm64);
        Assert.Equal("不明", info.Version);
        Assert.Equal("不明", info.SourceId);
        Assert.Equal("不明", info.Dirty);
        Assert.Equal("不明", info.BuiltUtc);
    }

    [Fact]
    public void Real_Assembly_Contains_Exact_Product_Version_And_Utc_Metadata()
    {
        var assembly = typeof(BuildInformation).Assembly;
        var info = BuildInformation.Current;
        Assert.Equal(assembly.GetName().Version!.ToString(3), info.Version);
        var file = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
        Assert.Equal(info.Version, file.ProductVersion);
        Assert.Equal(info.Version + ".0", file.FileVersion);
        Assert.EndsWith("UTC (+00:00)", info.BuiltUtc);
        Assert.Equal(RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "ARM64" : "x64", info.Execution.Split(" / ")[1]);
        Assert.DoesNotContain("/Users/", info.CopyText, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.CurrentDirectory, info.CopyText, StringComparison.OrdinalIgnoreCase);
    }

    private static Assembly Fixture(string dirty)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.Run);
        assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor(new[] { typeof(string) })!, new object[] { "0.2.0" }));
        foreach (var pair in new[] { ("DesktopSourceId", "abcdef012345"), ("DesktopDirty", dirty), ("DesktopBuiltUtc", "2026-01-02T03:04:05Z") })
            assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!, new object[] { pair.Item1, pair.Item2 }));
        return assembly;
    }
}
