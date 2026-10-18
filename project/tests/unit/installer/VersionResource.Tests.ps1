BeforeAll {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
    . (Join-Path $projectRoot 'scripts/resolve-desktop-system-monitor-version-resource.ps1')

    function New-VersionBlock {
        param([string]$Key, [int]$Type, [byte[]]$Value = @(), [int]$ValueLength = 0, [object[]]$Children = @())

        $stream = [IO.MemoryStream]::new()
        $writer = [IO.BinaryWriter]::new($stream)
        $writer.Write([uint16]0)
        $writer.Write([uint16]$ValueLength)
        $writer.Write([uint16]$Type)
        $writer.Write([Text.Encoding]::Unicode.GetBytes($Key + [char]0))
        while ($stream.Length % 4) { $writer.Write([byte]0) }
        if ($Value.Length -gt 0) { $writer.Write($Value) }
        foreach ($child in $Children) {
            while ($stream.Length % 4) { $writer.Write([byte]0) }
            $writer.Write([byte[]]$child)
        }
        $bytes = $stream.ToArray()
        [BitConverter]::GetBytes([uint16]$bytes.Length).CopyTo($bytes, 0)
        return , $bytes
    }

    function New-StringEntry {
        param([string]$Key, [string]$Text)
        return , (New-VersionBlock -Key $Key -Type 1 -Value ([Text.Encoding]::Unicode.GetBytes($Text + [char]0)) -ValueLength ($Text.Length + 1))
    }

    function New-VersionInfo {
        param([string]$ProductVersion, [string]$FileVersion, [int[]]$Fixed)

        $fixedInfo = [byte[]]::new(52)
        [BitConverter]::GetBytes([uint32]4277077181).CopyTo($fixedInfo, 0)
        [BitConverter]::GetBytes([uint32]65536).CopyTo($fixedInfo, 4)
        foreach ($at in @(8, 16)) {
            [BitConverter]::GetBytes([uint32](($Fixed[0] -shl 16) -bor $Fixed[1])).CopyTo($fixedInfo, $at)
            [BitConverter]::GetBytes([uint32](($Fixed[2] -shl 16) -bor $Fixed[3])).CopyTo($fixedInfo, $at + 4)
        }
        $strings = @()
        if ($null -ne $ProductVersion) { $strings += , (New-StringEntry -Key 'ProductVersion' -Text $ProductVersion) }
        $strings += , (New-StringEntry -Key 'FileVersion' -Text $FileVersion)
        $table = New-VersionBlock -Key '000004b0' -Type 1 -Children $strings
        $stringFileInfo = New-VersionBlock -Key 'StringFileInfo' -Type 1 -Children @(, $table)
        return , (New-VersionBlock -Key 'VS_VERSION_INFO' -Type 0 -Value $fixedInfo -ValueLength 52 -Children @(, $stringFileInfo))
    }

    # Minimal PE32+ image: one .rsrc section containing RT_VERSION/1/0x409.
    function New-PeWithVersionInfo {
        param([string]$Path, [byte[]]$VersionInfo, [uint16]$Machine = 0x8664)

        $resource = [byte[]]::new(0x58 + $VersionInfo.Length)
        $directory = {
            param([int]$At, [uint32]$Id, [uint32]$Target)
            [BitConverter]::GetBytes([uint16]1).CopyTo($resource, $At + 14)
            [BitConverter]::GetBytes($Id).CopyTo($resource, $At + 16)
            [BitConverter]::GetBytes($Target).CopyTo($resource, $At + 20)
        }
        & $directory 0x00 16 ([uint32]2147483648 + 0x18)
        & $directory 0x18 1 ([uint32]2147483648 + 0x30)
        & $directory 0x30 0x409 0x48
        [BitConverter]::GetBytes([uint32](0x1000 + 0x58)).CopyTo($resource, 0x48)
        [BitConverter]::GetBytes([uint32]$VersionInfo.Length).CopyTo($resource, 0x4C)
        $VersionInfo.CopyTo($resource, 0x58)

        $image = [byte[]]::new(0x200 + $resource.Length)
        $image[0] = 0x4D; $image[1] = 0x5A
        [BitConverter]::GetBytes([uint32]0x40).CopyTo($image, 0x3C)
        [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($image, 0x40)
        [BitConverter]::GetBytes($Machine).CopyTo($image, 0x44)
        [BitConverter]::GetBytes([uint16]1).CopyTo($image, 0x46)
        [BitConverter]::GetBytes([uint16]0xF0).CopyTo($image, 0x54)
        $optional = 0x58
        [BitConverter]::GetBytes([uint16]0x20B).CopyTo($image, $optional)
        [BitConverter]::GetBytes([uint32]16).CopyTo($image, $optional + 108)
        [BitConverter]::GetBytes([uint32]0x1000).CopyTo($image, $optional + 112 + 16)
        [BitConverter]::GetBytes([uint32]$resource.Length).CopyTo($image, $optional + 112 + 20)
        $section = $optional + 0xF0
        [Text.Encoding]::ASCII.GetBytes('.rsrc').CopyTo($image, $section)
        [BitConverter]::GetBytes([uint32]$resource.Length).CopyTo($image, $section + 8)
        [BitConverter]::GetBytes([uint32]0x1000).CopyTo($image, $section + 12)
        [BitConverter]::GetBytes([uint32]$resource.Length).CopyTo($image, $section + 16)
        [BitConverter]::GetBytes([uint32]0x200).CopyTo($image, $section + 20)
        $resource.CopyTo($image, 0x200)
        [IO.File]::WriteAllBytes($Path, $image)
    }
}

Describe 'Windows executable version resource' {
    BeforeEach {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ('dsm-version-resource-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temp | Out-Null
        $exe = Join-Path $temp 'DesktopSystemMonitor.exe'
    }

    AfterEach {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }

    It 'reads ProductVersion, FileVersion and fixed versions from the RT_VERSION resource' {
        New-PeWithVersionInfo -Path $exe -VersionInfo (New-VersionInfo -ProductVersion '0.2.0' -FileVersion '0.2.0.0' -Fixed @(0, 2, 0, 0)) -Machine 0xAA64
        $information = Assert-DesktopSystemMonitorExecutableVersion -Path $exe -ProductVersion '0.2.0'
        $information.ProductVersion | Should -BeExactly '0.2.0'
        $information.FileVersion | Should -BeExactly '0.2.0.0'
        $information.FixedProductVersion | Should -BeExactly '0.2.0.0'
        $information.FixedFileVersion | Should -BeExactly '0.2.0.0'
        $information.Machine | Should -Be 0xAA64
    }

    It 'rejects an executable whose product version differs from Directory.Build.props' {
        New-PeWithVersionInfo -Path $exe -VersionInfo (New-VersionInfo -ProductVersion '0.1.0' -FileVersion '0.2.0.0' -Fixed @(0, 2, 0, 0))
        { Assert-DesktopSystemMonitorExecutableVersion -Path $exe -ProductVersion '0.2.0' } |
            Should -Throw '*ProductVersion=0.1.0 (expected 0.2.0)*'
    }

    It 'rejects a fixed file version that differs from the string version' {
        New-PeWithVersionInfo -Path $exe -VersionInfo (New-VersionInfo -ProductVersion '0.2.0' -FileVersion '0.2.0.0' -Fixed @(0, 1, 0, 0))
        { Assert-DesktopSystemMonitorExecutableVersion -Path $exe -ProductVersion '0.2.0' } |
            Should -Throw '*FixedFileVersion=0.1.0.0 (expected 0.2.0.0)*'
    }

    It 'rejects a resource without ProductVersion instead of guessing it' {
        New-PeWithVersionInfo -Path $exe -VersionInfo (New-VersionInfo -ProductVersion $null -FileVersion '0.2.0.0' -Fixed @(0, 2, 0, 0))
        { Assert-DesktopSystemMonitorExecutableVersion -Path $exe -ProductVersion '0.2.0' } |
            Should -Throw '*ProductVersion= (expected 0.2.0)*'
    }

    It 'rejects a non-PE file' {
        [IO.File]::WriteAllBytes($exe, [Text.Encoding]::ASCII.GetBytes('not an executable'))
        { Get-DesktopSystemMonitorExecutableVersionInfo -Path $exe } | Should -Throw '*Not a PE executable*'
    }

    It 'reads the product version only from a numeric Directory.Build.props Version' {
        Set-Content -LiteralPath (Join-Path $temp 'Directory.Build.props') -Value '<Project><PropertyGroup><Version>0.2.0</Version></PropertyGroup></Project>'
        Get-DesktopSystemMonitorProductVersion -ProjectRoot $temp | Should -BeExactly '0.2.0'
        Set-Content -LiteralPath (Join-Path $temp 'Directory.Build.props') -Value '<Project><PropertyGroup><Version>0.2.0-local</Version></PropertyGroup></Project>'
        { Get-DesktopSystemMonitorProductVersion -ProjectRoot $temp } | Should -Throw '*numeric major.minor.patch*'
    }

    It 'matches the Windows FileVersionInfo reader on the running PowerShell host' -Tag 'WindowsOnly' {
        $hostPath = (Get-Process -Id $PID).Path
        $expected = [Diagnostics.FileVersionInfo]::GetVersionInfo($hostPath)
        $actual = Get-DesktopSystemMonitorExecutableVersionInfo -Path $hostPath
        $actual.ProductVersion | Should -BeExactly $expected.ProductVersion
        $actual.FileVersion | Should -BeExactly $expected.FileVersion
        $actual.FixedFileVersion | Should -BeExactly ('{0}.{1}.{2}.{3}' -f $expected.FileMajorPart, $expected.FileMinorPart, $expected.FileBuildPart, $expected.FilePrivatePart)
    }
}
