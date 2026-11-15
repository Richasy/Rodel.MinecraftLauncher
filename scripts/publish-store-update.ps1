#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PlanPath,
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$NotesPath,
    [Parameter(Mandatory)][string]$ReceiptPath,
    [switch]$Submit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StoreRelease.psm1') -Force
$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json -AsHashtable
$notes = Get-Content -LiteralPath $NotesPath -Raw | ConvertFrom-Json -AsHashtable
$source = (& git -C (Split-Path -Parent $PSScriptRoot) rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $source -cne $plan.sourceCommit -or
    $notes.sourceCommit -cne $source -or $notes.version -cne $plan.version -or $notes.baselineCommit -cne $plan.baselineCommit) {
    throw 'The release source, plan and notes must match exactly.'
}
if ($notes.notesMode -cnotin @('generated', 'preserve-existing')) { throw 'Unknown release-note policy.' }
$localized = if ($notes.notesMode -ceq 'generated') { $notes.notes } else { $null }
$packages = @(foreach ($architecture in @('x64', 'arm64')) {
    $directory = Join-Path $PackageRoot "$($plan.version)\win-$architecture\store"
    $files = @(Get-ChildItem -LiteralPath $directory -Filter '*.msix' -File)
    if ($files.Count -ne 1) { throw "Expected one exact $architecture MSIX." }
    & (Join-Path $PSScriptRoot 'verify-release-package.ps1') -Package $files[0].FullName `
        -Version $plan.version -Architecture $architecture -Store -MicrosoftClientId $env:RODEL_MICROSOFT_CLIENT_ID | Write-Host
    $audit = Get-Content -LiteralPath ($files[0].FullName + '.audit.json') -Raw | ConvertFrom-Json
    if (-not $audit.microsoftLoginConfigured) { throw 'Store updates require built-in Microsoft login configuration.' }
    if (@($plan.languages | Where-Object { $_ -notin $audit.languages }).Count) {
        throw 'Every Store listing language must be declared by both audited packages before deriving its display name.'
    }
    $upload = [IO.Path]::ChangeExtension($files[0].FullName, '.msixupload')
    @{ path = $upload; architecture = $architecture; version = $plan.version; sourceCommit = $source
        sha256 = (Get-FileHash -LiteralPath $upload).Hash.ToLowerInvariant(); displayName = $audit.displayName; languages = @($audit.languages) }
})
$manifestPath = "$ReceiptPath.packages.json"
if (Test-Path -LiteralPath $manifestPath) { throw 'A release manifest already exists. Inspect the previous attempt.' }
[IO.File]::WriteAllText($manifestPath, ($packages | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
if (-not $Submit) {
    Write-Output 'Validated both exact Store uploads; no draft was created, uploaded or submitted.'
    return
}
Invoke-StoreUpdate -Plan $plan -Packages $packages -Notes $localized -NotesSha256 (Get-FileHash -LiteralPath $NotesPath).Hash.ToLowerInvariant() -ReceiptPath $ReceiptPath
