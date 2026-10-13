param([Parameter(Mandatory)][string] $PackageDirectory)

$ErrorActionPreference = 'Stop'
$expectedIds = @('RichEdit.Maui', 'CodeEdit.Maui')
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg' -File)
if ($packages.Count -ne $expectedIds.Count) { throw 'Expected only the two MAUI packages.' }

foreach ($id in $expectedIds) {
    $matchingPackages = @($packages | Where-Object Name -Like "$id.*.nupkg")
    if ($matchingPackages.Count -ne 1) { throw "Expected exactly one $id package." }
    $archive = [IO.Compression.ZipFile]::OpenRead($matchingPackages[0].FullName)
    try {
        $reader = [IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
        try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($dependency in $manifest.SelectNodes("//*[local-name()='dependency']")) {
            if ($dependency.id -in @('TextEdit.Core', 'RichEdit.Core')) {
                throw "$id depends on an unpublished core package: $($dependency.id)."
            }
        }
        $assemblies = @($archive.Entries | Where-Object FullName -Like "lib/*/$id.dll")
        if ($assemblies.Count -eq 0) { throw "$id has no platform assemblies." }
        foreach ($assembly in $assemblies) {
            $directory = $assembly.FullName.Substring(0, $assembly.FullName.LastIndexOf('/') + 1)
            if ($id -eq 'RichEdit.Maui') {
                foreach ($file in @('RichEdit.Core.dll', 'RichEdit.Core.xml', 'TextEdit.Core.dll', 'TextEdit.Core.xml')) {
                    if ($null -eq $archive.GetEntry($directory + $file)) { throw "$id is missing $directory$file." }
                }
            }
            else {
                $framework = $directory.Split('/')[1]
                $dependency = $manifest.SelectSingleNode("//*[local-name()='group' and @targetFramework='$framework']/*[local-name()='dependency' and @id='RichEdit.Maui']")
                if ($null -eq $dependency) { throw "$id must depend on RichEdit.Maui for $framework." }
                foreach ($file in @('RichEdit.Maui.dll', 'RichEdit.Core.dll', 'TextEdit.Core.dll')) {
                    if ($null -ne $archive.GetEntry($directory + $file)) { throw "$id duplicates $file from RichEdit.Maui." }
                }
            }
        }
        Write-Output "Verified $id ($($assemblies.Count) platform assets)."
    }
    finally { $archive.Dispose() }
}
