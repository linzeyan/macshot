<#
.SYNOPSIS
    Packs a published macshot into an MSIX and signs it.

.DESCRIPTION
    Windows only: makeappx.exe and signtool.exe both come from the Windows SDK.

    The input is a folder dotnet publish produced with -p:WindowsPackageType=MSIX. That
    property is the whole difference between this and the zip: an unpackaged build has the
    Windows App SDK bootstrapper wired into its startup path, and the bootstrapper refuses
    to run inside a package. Publishing twice is cheaper than finding that out at launch.

    Signing is not optional for an MSIX — an unsigned one cannot be installed at all — but
    a certificate is not always available, so this packs either way and says which it did.
    An unsigned package is still worth producing: it proves the packaging works, and it is
    the thing a developer signs with their own test certificate to try an install.

.PARAMETER PublishDirectory
    The published app. The manifest and the package logos are written into it, so give it
    a folder published for this purpose rather than the one the zip is made from.

.PARAMETER PackageVersion
    Four numbers. An MSIX version has no room for "-beta.1", so this is the same
    Major.Minor.Patch.Build the assembly carries and the tag's pre-release word is dropped.

.PARAMETER CertificateBase64
    The signing certificate as a base64 .pfx, which is how a repository secret can hold
    one. Absent means the package is packed and left unsigned.

.PARAMETER CertificatePath
    A .pfx on disk, for signing by hand with a self-signed certificate. There is no way to
    install an MSIX without one, so this is what makes the packaged build testable before
    a real certificate exists.

.EXAMPLE
    .\pack-msix.ps1 -PublishDirectory ..\dist\Release-msix -PackageVersion 1.0.0.0
    Packs an unsigned MSIX for local inspection.

.EXAMPLE
    New-SelfSignedCertificate -Type Custom -Subject 'CN=macshot' -KeyUsage DigitalSignature `
        -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
    The certificate a local install needs. Export it as a .pfx, import the .cer into
    Trusted People, then pass the .pfx here — the subject must match what this script
    writes as Publisher, which for an unsigned build is CN=macshot.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$PackageVersion,

    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [ValidateSet('normal', 'offline')]
    [string]$Variant = 'normal',

    [string]$FileName,
    [string]$SignedDirectory = 'msix-signed',
    [string]$UnsignedDirectory = 'msix-unsigned',

    [string]$CertificateBase64,
    [string]$CertificatePath,
    [string]$CertificatePassword,

    # A signature with no timestamp expires with the certificate, which would turn every
    # release already downloaded into one Windows refuses the day the certificate lapses.
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
    throw 'Packing an MSIX needs makeappx.exe and signtool.exe, which are Windows SDK tools.'
}

# The variant decides three names at once, and they must agree: the executable the
# manifest points at is the one the offline build actually produces, and the identity has
# to differ or installing one variant would upgrade over the other.
$offline = $Variant -eq 'offline'
$identityName = if ($offline) { 'Macshot.Windows.Offline' } else { 'Macshot.Windows' }
$displayName = if ($offline) { 'macshot Offline' } else { 'macshot' }
$executable = if ($offline) { 'Macshot.Windows.Offline.exe' } else { 'Macshot.Windows.exe' }

if (-not $FileName) {
    $suffix = if ($offline) { '-Offline' } else { '' }
    $FileName = "macshot$suffix-$PackageVersion-win-$Architecture.msix"
}

function Resolve-SdkTool {
    <#
        The Windows SDK is not on PATH. Newest version first: an MSIX cannot be signed by
        a signtool that predates the format, and the runner carries several.
    #>
    param([Parameter(Mandatory)][string]$Name)

    # The x86 Program Files first, which is where the Windows SDK installs on every
    # 64-bit machine. Filtered before Join-Path, because a missing variable would throw
    # there rather than simply not matching.
    $roots = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) |
        Where-Object { $_ } |
        ForEach-Object { Join-Path $_ 'Windows Kits\10\bin' } |
        Where-Object { Test-Path $_ }

    foreach ($root in $roots) {
        $versioned = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+(\.\d+)+$' } |
            Sort-Object { [version]$_.Name } -Descending

        # Filtered, because @($null) is a one-element array holding nothing: an SDK laid
        # out without version folders would otherwise fault on the first iteration and
        # never reach the unversioned fallback that case exists for.
        foreach ($directory in @($versioned | Where-Object { $_ }) + @(Get-Item $root)) {
            $candidate = Join-Path $directory.FullName "x64\$Name"
            if (Test-Path $candidate) { return $candidate }
        }
    }

    throw "$Name was not found under any Windows Kits\10\bin. Install the Windows SDK."
}

function Invoke-Tool {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string[]]$Arguments)

    & $Path @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path -Leaf $Path) failed with exit code $LASTEXITCODE."
    }
}

$layout = (Resolve-Path $PublishDirectory).Path
if (-not (Test-Path (Join-Path $layout $executable))) {
    throw "$PublishDirectory holds no $executable — it is not a published $Variant build."
}

# Read the certificate before writing the manifest, not after packing it. An MSIX only
# installs when its Publisher is character-for-character the certificate's subject, and
# the Publisher is inside the package that gets signed: getting it wrong means repacking,
# not re-signing.
$publisher = 'CN=macshot'
$certificateBytes = $null
$certificateFile = $null
if ($CertificateBase64 -and $CertificatePath) {
    throw 'Give either -CertificateBase64 or -CertificatePath, not both.'
}

if ($CertificateBase64) {
    # Held in memory, not written out here. signtool wants a path, so the secret does
    # reach the disk eventually — but only inside the try/finally that signs with it, so
    # that a failure between now and then cannot leave a certificate lying in TEMP.
    $certificateBytes = [Convert]::FromBase64String(($CertificateBase64 -replace '\s', ''))
}
elseif ($CertificatePath) {
    $certificateFile = (Resolve-Path $CertificatePath).Path
}

$signing = [bool]($certificateBytes -or $certificateFile)
if ($signing) {
    # The comma is what keeps the byte array intact: a bare array leaving a script block
    # is enumerated into the output stream and comes back as object[], which matches no
    # X509Certificate2 constructor. The wrapper is unrolled instead, and byte[] survives.
    $material = if ($certificateBytes) { (, $certificateBytes) } else { $certificateFile }
    $signer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 `
        -ArgumentList $material, $CertificatePassword
    $publisher = $signer.Subject
    $signer.Dispose()
}

$templates = (Resolve-Path (Join-Path $PSScriptRoot '..\packaging\msix')).Path
$manifestPath = Join-Path $layout 'AppxManifest.xml'

[xml]$manifest = Get-Content (Join-Path $templates 'AppxManifest.xml') -Raw

# The template's comments explain the template, not the package. They would otherwise ship
# inside every MSIX describing placeholders that are no longer there.
foreach ($comment in @($manifest.SelectNodes('//comment()'))) {
    $comment.ParentNode.RemoveChild($comment) | Out-Null
}

$namespaces = New-Object System.Xml.XmlNamespaceManager $manifest.NameTable
$namespaces.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespaces.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')

$identity = $manifest.SelectSingleNode('/m:Package/m:Identity', $namespaces)
$identity.SetAttribute('Name', $identityName)
$identity.SetAttribute('Publisher', $publisher)
$identity.SetAttribute('Version', $PackageVersion)
$identity.SetAttribute('ProcessorArchitecture', $Architecture)

$manifest.SelectSingleNode('/m:Package/m:Properties/m:DisplayName', $namespaces).InnerText = $displayName

$application = $manifest.SelectSingleNode('/m:Package/m:Applications/m:Application', $namespaces)
$application.SetAttribute('Executable', $executable)
$application.SelectSingleNode('uap:VisualElements', $namespaces).SetAttribute('DisplayName', $displayName)

$manifest.Save($manifestPath)

# The contents rather than the folder: Copy-Item onto a destination that already exists
# nests it, and a second run would produce Assets\Assets and a package whose logos are
# all missing.
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Copy-Item (Join-Path $templates 'Assets\*') $assets -Force

# Two output folders rather than one, so that a caller can tell the two apart by where
# the file is instead of by reading the log. The release workflow collects only one of
# them.
$outputDirectory = if ($signing) { $SignedDirectory } else { $UnsignedDirectory }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$package = Join-Path (Resolve-Path $outputDirectory).Path $FileName

Write-Host "==> Packing $FileName" -ForegroundColor Cyan
Invoke-Tool (Resolve-SdkTool 'makeappx.exe') @('pack', '/o', '/d', $layout, '/p', $package)

if ($signing) {
    $signtool = Resolve-SdkTool 'signtool.exe'
    $pfx = $certificateFile
    try {
        if ($certificateBytes) {
            $pfx = Join-Path ([System.IO.Path]::GetTempPath()) "macshot-signing-$([guid]::NewGuid()).pfx"
            [System.IO.File]::WriteAllBytes($pfx, $certificateBytes)
        }

        Write-Host '==> Signing' -ForegroundColor Cyan
        Invoke-Tool $signtool @(
            'sign', '/fd', 'SHA256', '/f', $pfx, '/p', $CertificatePassword,
            '/tr', $TimestampUrl, '/td', 'SHA256', $package)

        # Asserted rather than assumed: signtool sign reports success for a signature the
        # machine will still refuse, and an unverifiable package is worse than none —
        # it looks installable right up to the moment somebody tries.
        Invoke-Tool $signtool @('verify', '/pa', $package)
    }
    finally {
        # Only the copy this script made. A -CertificatePath the caller owns is theirs.
        if ($certificateBytes -and $pfx) {
            Remove-Item $pfx -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host "Signed: $package" -ForegroundColor Green
}
else {
    Write-Warning "NOT SIGNED: $package"
    Write-Warning 'No certificate was supplied, so this package cannot be installed. Windows'
    Write-Warning 'refuses an unsigned MSIX outright — this one is for inspection, or to be'
    Write-Warning 'signed locally with a test certificate whose subject is exactly CN=macshot.'
}

$package
