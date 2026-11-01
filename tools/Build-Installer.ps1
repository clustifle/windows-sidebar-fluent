param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '..\artifacts\FluentSidebar'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\installer'),
    [string]$Version = '1.3.0',
    [string]$WixPath = 'wix'
)
$ErrorActionPreference = 'Stop'
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'FluentSidebar.exe'))) { throw 'Publish the app before building its installer.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$wxsPath = Join-Path $outputRoot 'WindowsSidebarFluent.wxs'
$msiPath = Join-Path $outputRoot "FluentSidebar-$Version-x64.msi"

function StableId([string]$prefix, [string]$path) {
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($path.ToLowerInvariant()))
    return $prefix + [Convert]::ToHexString($hash).Substring(0,24)
}
function StableGuid([string]$path) {
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes('WindowsSidebarFluent|' + $path.ToLowerInvariant()))
    return [guid]::new([byte[]]$hash[0..15]).ToString()
}
$namespace = 'http://wixtoolset.org/schemas/v4/wxs'
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create($wxsPath,$settings)
function Write-WixStart([string]$name, [hashtable]$attributes) {
    $writer.WriteStartElement($name,$namespace)
    foreach ($key in $attributes.Keys) { $writer.WriteAttributeString($key,[string]$attributes[$key]) }
}
function Element([string]$name, [hashtable]$attributes) { Write-WixStart $name $attributes; $writer.WriteEndElement() }
$directories = @{''='INSTALLFOLDER'}
function WriteDirectories([string]$folder) {
    foreach ($directory in Get-ChildItem -LiteralPath $folder -Directory | Sort-Object Name) {
        $relative = [System.IO.Path]::GetRelativePath($publishRoot,$directory.FullName)
        $id = StableId 'D_' $relative
        $directories[$relative] = $id
        Write-WixStart 'Directory' @{Id=$id;Name=$directory.Name}
        WriteDirectories $directory.FullName
        $writer.WriteEndElement()
    }
}
try {
    Write-WixStart 'Wix' @{}
    Write-WixStart 'Package' @{Name='FluentSidebar';Manufacturer='Clustifle';Version=$Version;UpgradeCode='01B63E60-D4A6-4CF9-A078-283CA64D96BA';Scope='perUser';Language='1033';InstallerVersion='500'}
    Element 'MajorUpgrade' @{DowngradeErrorMessage='A newer version of FluentSidebar is already installed.'}
    Element 'MediaTemplate' @{EmbedCab='yes';CompressionLevel='high'}
    Element 'Icon' @{Id='SidebarIcon';SourceFile=(Join-Path $publishRoot 'Assets\Sidebar.ico')}
    Element 'Property' @{Id='ARPPRODUCTICON';Value='SidebarIcon'}
    Element 'Property' @{Id='ARPHELPLINK';Value='https://github.com/clustifle/windows-sidebar-fluent'}
    Element 'Property' @{Id='ARPNOMODIFY';Value='1'}
    Write-WixStart 'StandardDirectory' @{Id='LocalAppDataFolder'}
    Write-WixStart 'Directory' @{Id='UserProgramsFolder';Name='Programs'}
    Write-WixStart 'Directory' @{Id='INSTALLFOLDER';Name='FluentSidebar'}
    WriteDirectories $publishRoot
    $writer.WriteEndElement(); $writer.WriteEndElement(); $writer.WriteEndElement()
    Element 'StandardDirectory' @{Id='DesktopFolder'}
    Element 'StandardDirectory' @{Id='ProgramMenuFolder'}
    Write-WixStart 'Feature' @{Id='MainFeature';Title='FluentSidebar';Level='1'}
    Element 'ComponentGroupRef' @{Id='ApplicationFiles'}
    Element 'ComponentRef' @{Id='ApplicationShortcuts'}
    $writer.WriteEndElement(); $writer.WriteEndElement()
    Write-WixStart 'Fragment' @{}
    Write-WixStart 'ComponentGroup' @{Id='ApplicationFiles'}
    foreach ($file in Get-ChildItem -LiteralPath $publishRoot -Recurse -File | Sort-Object FullName) {
        if ($file.Extension -in '.pdb','.wixpdb') { continue }
        $relative = [System.IO.Path]::GetRelativePath($publishRoot,$file.FullName)
        $parent = [System.IO.Path]::GetDirectoryName($relative)
        if (-not $parent) { $parent='' }
        $componentId = StableId 'C_' $relative
        Write-WixStart 'Component' @{Id=$componentId;Directory=$directories[$parent];Guid=(StableGuid $relative);Bitness='always64'}
        Element 'File' @{Id=(StableId 'F_' $relative);Source=$file.FullName;KeyPath='no'}
        Element 'RegistryValue' @{Root='HKCU';Key='Software\Clustifle\Windows Sidebar Fluent\Installer';Name=$componentId;Type='integer';Value='1';KeyPath='yes'}
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
    Write-WixStart 'Component' @{Id='ApplicationShortcuts';Directory='INSTALLFOLDER';Guid=(StableGuid 'ApplicationShortcuts');Bitness='always64'}
    Element 'Shortcut' @{Id='DesktopShortcut';Directory='DesktopFolder';Name='FluentSidebar';Target='[INSTALLFOLDER]FluentSidebar.exe';WorkingDirectory='INSTALLFOLDER';Icon='SidebarIcon';Advertise='no'}
    Element 'Shortcut' @{Id='StartMenuShortcut';Directory='ProgramMenuFolder';Name='FluentSidebar';Target='[INSTALLFOLDER]FluentSidebar.exe';WorkingDirectory='INSTALLFOLDER';Icon='SidebarIcon';Advertise='no'}
    Element 'RegistryValue' @{Root='HKCU';Key='Software\Clustifle\Windows Sidebar Fluent\Installer';Name='Shortcuts';Type='integer';Value='1';KeyPath='yes'}
    foreach ($directoryId in @($directories.Values) + @('UserProgramsFolder')) { Element 'RemoveFolder' @{Id=('Remove_'+$directoryId);Directory=$directoryId;On='uninstall'} }
    $writer.WriteEndElement(); $writer.WriteEndElement(); $writer.WriteEndElement()
}
finally { $writer.Dispose() }
& $WixPath build $wxsPath -arch x64 -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "WiX failed with exit code $LASTEXITCODE" }
Get-Item -LiteralPath $msiPath

