#requires -Version 7.5
param([string]$Repository, [string]$Directory, [string]$Scenario)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Scenario.StartsWith('audit-', [StringComparison]::Ordinal)) {
    & (Join-Path $PSScriptRoot 'package-audit.ps1') -Repository $Repository -Directory $Directory -Scenario $Scenario
    return
}
if ($Scenario.StartsWith('http-', [StringComparison]::Ordinal)) {
    & (Join-Path $PSScriptRoot 'store-http.ps1') -Repository $Repository -Directory $Directory -Scenario $Scenario
    return
}
Import-Module (Join-Path $Repository 'scripts\StoreRelease.psm1') -Force
if ($Scenario -eq 'receipt-reader') {
    $path = Join-Path $Directory 'snapshot.json'
    $module = Get-Module StoreRelease
    & $module { param($path) Write-StoreJson $path @{ state = 'before' } } $path
    $stream = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read -bor [IO.FileShare]::Delete)
    $reader = [IO.StreamReader]::new($stream)
    $caught = $false
    $nativeError = $null
    try {
        try { & $module { param($path) Write-StoreJson $path @{ state = 'after' } } $path }
        catch { $caught = $true; $nativeError = $_.Exception.GetBaseException().HResult -band 0xffff }
        @{ caught = $caught; nativeError = $nativeError
            retainedState = ($reader.ReadToEnd() | ConvertFrom-Json).state
            currentState = ([IO.File]::ReadAllText($path) | ConvertFrom-Json).state
            temporaryFiles = @(Get-ChildItem -LiteralPath $Directory -Filter '*.tmp').Count } | ConvertTo-Json -Compress
    }
    finally { $reader.Dispose() }
    return
}
$env:STORE_TENANT_ID = '11111111-1111-4111-8111-111111111111'
$env:STORE_CLIENT_ID = '22222222-2222-4222-8222-222222222222'
$env:STORE_CLIENT_SECRET = 'synthetic-store-secret'
$env:RELEASE_AI_ENABLED = 'false'
$env:GH_TOKEN = 'synthetic-github-token'
$baseline = Get-Content (Join-Path $Repository '.github\store-release-baseline.json') -Raw | ConvertFrom-Json
$source = (& git -C $Repository rev-parse HEAD).Trim()
$state = @{
    calls = [Collections.Generic.List[string]]::new()
    payload = $null; zipNames = @(); zipBytesCorrect = $false
}
$transport = {
    param($request)
    $state.calls.Add($request.Operation)
    switch ($request.Operation) {
        'Get token' { return @{ access_token = 'synthetic-token' } }
        'Get application' {
            # Missing optional pendingApplicationSubmission is the real published API shape, not an explicit null.
            $app = @{ lastPublishedApplicationSubmission = @{ id = $baseline.publishedSubmissionId } }
            if ($Scenario -eq 'existing-draft') { $app.pendingApplicationSubmission = @{ id = '999' } }
            return $app
        }
        'Get published baseline' {
            return @{ status = 'Published'; applicationPackages = @(@{ version = $baseline.version }); listings = @{ en = @{}; 'zh-cn' = @{} } }
        }
        'Create draft' {
            $created = @{
                id = '123'; fileUploadUrl = 'https://synthetic.blob.core.windows.net/upload/test.zip?sig=synthetic'
                friendlyName = 'Submission 2'
                targetPublishMode = 'Manual'
                applicationPackages = @(@{ fileName = 'old.msixupload'; fileStatus = 'Uploaded'; targetPlatform = 'Universal'; unknownPackageField = 'keep' })
                listings = @{
                    en = @{ baseListing = @{ title = 'RMCL'; shortTitle = 'Old short label'; voiceTitle = 'Old spoken name'; sortTitle = 'Old sorting name'; description = 'Existing English description'; releaseNotes = 'Old English notes'; images = @(@{ fileName = 'https://example.invalid/already-uploaded.png'; imageType = 'Screenshot' }) }; platformOverrides = @{ Desktop = @{ unchanged = $true } } }
                    'zh-cn' = @{ baseListing = @{ description = 'Existing Chinese description'; releaseNotes = 'Old Chinese notes'; unknownListingField = 42 } }
                }
                pricing = @{ priceId = 'Free'; isAdvancedPricingModel = $true; futureField = @{ keep = $true } }
            }
            return $created
        }
        'Upload ZIP' {
            if ($request.ContainsKey('Token')) { throw 'A Store bearer token leaked to Blob storage.' }
            $zip = [IO.Compression.ZipFile]::OpenRead($request.File)
            try {
                $state.zipNames = @($zip.Entries.FullName | Sort-Object)
                $state.zipBytesCorrect = $true
                foreach ($entry in $zip.Entries) {
                    $reader = [IO.StreamReader]::new($entry.Open())
                    try {
                        if ($reader.ReadToEnd() -cne "synthetic-$($entry.Name.Split('.')[0])") { $state.zipBytesCorrect = $false }
                    }
                    finally { $reader.Dispose() }
                }
            }
            finally { $zip.Dispose() }
            return
        }
        'Update draft' {
            $state.payload = $request.Body
            $updated = $request.Body | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable
            if ($Scenario -eq 'server-normalized') {
                # Store adds package metadata to the authored fields; collection order is not package identity.
                foreach ($package in $updated.applicationPackages) {
                    $package.id = [guid]::NewGuid().ToString('N')
                    $package.architecture = 'Neutral'
                    $package.targetDeviceFamilies = @()
                }
                [array]::Reverse($updated.applicationPackages)
                $updated.listings.en.baseListing.serverMetadata = @{ retained = $true }
                $updated.friendlyName = $null
                $updated.pricing.isAdvancedPricingModel = $false
                foreach ($package in $updated.applicationPackages) { $package.Remove('targetPlatform') }
            }
            if ($Scenario -eq 'server-changed-package') { $updated.applicationPackages[1].fileName = 'unexpected.msixupload' }
            if ($Scenario -eq 'server-changed-listing') { $updated.listings.en.baseListing.releaseNotes = 'Unexpected notes' }
            if ($Scenario -eq 'server-changed-pricing') { $updated.pricing.futureField.keep = $false }
            # Before package ingestion, the API can still expose the previous package's derived name.
            foreach ($listing in $updated.listings.Values) { $listing.baseListing.title = 'Rodel Minecraft Launcher' }
            $updated.status = 'PendingCommit'
            $state.updated = $updated
            return $request.Body
        }
        'Read updated draft' { return $state.updated }
        'Commit draft' {
            if ($Scenario -in @('commit-response-lost', 'github-resume-commit')) { throw [TimeoutException]::new('Synthetic lost commit acknowledgement.') }
            return
        }
        'Get submission status' {
            $state.statusReads = if ($state.ContainsKey('statusReads')) { $state.statusReads + 1 } else { 1 }
            return @{ status = if ($Scenario -eq 'processing-delayed' -and $state.statusReads -eq 1) { 'PreProcessing' } else { 'Certification' } }
        }
        'Read processed submission' {
            $processed = $state.updated | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable
            $processed.status = 'Certification'
            $processed.applicationPackages = @($processed.applicationPackages | Where-Object fileStatus -eq 'PendingUpload')
            foreach ($package in $processed.applicationPackages) {
                $package.fileStatus = 'Uploaded'
                $package.version = if ($Scenario -eq 'processed-version-mismatch') { '0.2611.0.0' } else { $state.version }
                $package.architecture = $package.fileName.Split('.')[0]
                $package.languages = @('en', 'zh-CN')
            }
            foreach ($listing in $processed.listings.Values) { $listing.baseListing.title = 'RMCL' }
            if ($Scenario -eq 'processed-title-mismatch') { $processed.listings.en.baseListing.title = 'Rodel Minecraft Launcher' }
            if ($Scenario -eq 'processed-title-null') { $processed.listings.en.baseListing.title = $null }
            if ($Scenario -in @('processed-title-omitted', 'processed-language-missing', 'github-resume-commit')) {
                $processed.listings.en.baseListing.Remove('title')
            }
            if ($Scenario -eq 'processed-language-missing') { $processed.applicationPackages[0].languages = @('zh-CN') }
            if ($Scenario -eq 'processed-listing-missing') { $processed.listings.Remove('en') }
            return $processed
        }
        'Get GitHub tag' {
            return @{ object = @{ type = 'commit'; sha = if ($Scenario -eq 'github-source-mismatch') { 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' } else { $source } } }
        }
        'Get GitHub release' { return $state['release'] }
        'Create GitHub release' {
            $release = $request.Body | ConvertTo-Json | ConvertFrom-Json -AsHashtable
            $release.id = 1
            $release.html_url = "https://github.com/Richasy/Rodel.MinecraftLauncher/releases/tag/$($request.Body.tag_name)"
            $state.release = $release
            return $release
        }
        default { throw "Unexpected operation: $($request.Operation)" }
    }
}.GetNewClosure()
$receiptPath = Join-Path $Directory 'receipt.json'
$plan = @{
    sourceCommit = $source; baselineCommit = $baseline.sourceCommit
    version = '0.2611.1.0'; publishedSubmissionId = $baseline.publishedSubmissionId
    languages = @('en', 'zh-cn')
}
$state.version = $plan.version
$packages = @(foreach ($architecture in @('x64', 'arm64')) {
    $path = Join-Path $Directory "$architecture.msixupload"
    [IO.File]::WriteAllText($path, "synthetic-$architecture", [Text.UTF8Encoding]::new($false))
    @{ path = $path; architecture = $architecture; version = $plan.version; sourceCommit = $source
        sha256 = (Get-FileHash $path).Hash; displayName = 'RMCL'; languages = @('en', 'zh-cn') }
})
$notes = if ($Scenario -in @('preserve-notes', 'github-preserve', 'server-normalized')) { $null } else { @{ en = 'New English notes'; 'zh-cn' = 'New Chinese notes' } }
$notesPath = Join-Path $Directory 'github-notes.json'
@{
    version = $plan.version; sourceCommit = $source; baselineCommit = $plan.baselineCommit
    notesMode = if ($notes) { 'generated' } else { 'preserve-existing' }
    notes = @{ en = 'New English notes'; 'zh-cn' = 'New Chinese notes'; 'zh-tw' = 'New Traditional notes' }
} | ConvertTo-Json | Set-Content $notesPath -Encoding utf8NoBOM
$notesHash = (Get-FileHash $notesPath).Hash.ToLowerInvariant()
if ($Scenario -eq 'tampered-package') { [IO.File]::AppendAllText($packages[0].path, 'changed') }
$caught = $false
$retryCaught = $false
$githubRejected = $false
$failure = $null
$preflight = $null
try {
    if ($Scenario -eq 'preflight') {
        $preflight = Get-StoreReleasePlan -SourceRoot $Repository -Transport $transport
    }
    elseif ($Scenario -eq 'ai-disabled') {
        $planPath = Join-Path $Directory 'plan.json'
        $plan | ConvertTo-Json | Set-Content $planPath
        & (Join-Path $Repository 'scripts\generate-store-notes.ps1') -PlanPath $planPath -OutputPath (Join-Path $Directory 'notes.json')
    }
    else {
        $null = Invoke-StoreUpdate -Plan $plan -Packages $packages -Notes $notes -NotesSha256 $notesHash -ReceiptPath $receiptPath -Transport $transport
        if ($Scenario.StartsWith('github-', [StringComparison]::Ordinal)) {
            if ($Scenario -eq 'github-notes-changed') { [IO.File]::AppendAllText($notesPath, ' ') }
            if ($Scenario -eq 'github-pending') {
                $pending = Get-Content $receiptPath -Raw | ConvertFrom-Json -AsHashtable
                $pending.accepted = $false
                $pending.phase = 'pendingAcceptance'
                $pending | ConvertTo-Json -Depth 10 | Set-Content $receiptPath
            }
            $null = Publish-StoreGitHubRelease -ReceiptPath $receiptPath -NotesPath $notesPath -Transport $transport
            $null = Publish-StoreGitHubRelease -ReceiptPath $receiptPath -NotesPath $notesPath -Transport $transport
        }
    }
}
catch { $caught = $true; $failure = $_.Exception.Message }
if ($Scenario -in @('commit-response-lost', 'github-resume-commit')) {
    try { $null = Invoke-StoreUpdate -Plan $plan -Packages $packages -Notes $notes -ReceiptPath $receiptPath -Transport $transport }
    catch { $retryCaught = $true }
}
if ($Scenario -in @('processed-version-mismatch', 'processed-title-mismatch', 'processed-title-null', 'processed-language-missing', 'processed-listing-missing')) {
    try { $null = Publish-StoreGitHubRelease -ReceiptPath $receiptPath -NotesPath $notesPath -Transport $transport }
    catch { $githubRejected = $true }
}
if ($Scenario -eq 'github-resume-commit') {
    try {
        $null = Complete-StoreGitHubRelease -ReceiptPath $receiptPath -NotesPath $notesPath -Transport $transport
        $null = Complete-StoreGitHubRelease -ReceiptPath $receiptPath -NotesPath $notesPath -Transport $transport
    }
    catch { $failure = $_.Exception.Message }
}
$receipt = if (Test-Path $receiptPath) { Get-Content $receiptPath -Raw | ConvertFrom-Json -AsHashtable } else { $null }
@{
    caught = $caught; failure = $failure; retryCaught = $retryCaught; receipt = $receipt; calls = $state.calls.ToArray()
    githubRejected = $githubRejected
    payload = $state.payload; zipNames = $state.zipNames; zipBytesCorrect = $state.zipBytesCorrect; preflight = $preflight
    notesCreated = Test-Path (Join-Path $Directory 'notes.json')
    temporaryZipExists = Test-Path "$receiptPath.upload.zip"
    githubRelease = $state['release']
} | ConvertTo-Json -Depth 30 -Compress
