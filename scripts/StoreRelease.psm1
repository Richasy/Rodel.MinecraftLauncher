#requires -Version 7.5
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:ApplicationUri = 'https://manage.devcenter.microsoft.com/v1.0/my/applications/9MW9ZMS2K47C'

function Invoke-StoreHttp {
    param([hashtable]$Request)
    $arguments = @{
        Uri = $Request.Uri
        Method = $Request.Method
        MaximumRedirection = 0
        TimeoutSec = 60
        ErrorAction = 'Stop'
        SkipHttpErrorCheck = $true
        ContentType = 'application/json; charset=utf-8'
    }
    if ($Request.ContainsKey('Token')) { $arguments.Headers = @{ Authorization = 'Bearer ' + $Request.Token } }
    if ($Request.ContainsKey('Form')) {
        $arguments.Body = $Request.Form
        $arguments.ContentType = 'application/x-www-form-urlencoded'
    }
    if ($Request.ContainsKey('Body')) {
        $arguments.Body = [Text.Encoding]::UTF8.GetBytes(($Request.Body | ConvertTo-Json -Depth 100 -Compress))
        $arguments.ContentType = 'application/json; charset=utf-8'
    }
    if ($Request.ContainsKey('File')) {
        $arguments.InFile = $Request.File
        $arguments.Headers = @{ 'x-ms-blob-type' = 'BlockBlob'; 'x-ms-version' = '2021-12-02' }
        $arguments.ContentType = 'application/zip'
        $arguments.TimeoutSec = 1800
    }
    $code = 'transport'
    $detail = ''
    try {
        $response = Invoke-WebRequest @arguments
        $code = [int]$response.StatusCode
        if ($response.StatusCode -eq 404 -and $Request.ContainsKey('AllowNotFound') -and $Request.AllowNotFound) { return $null }
        if ($response.StatusCode -ge 400) {
            try {
                $errorBody = $response.Content | ConvertFrom-Json -AsHashtable
                if ($errorBody -is [Collections.IDictionary]) {
                    if ($errorBody['error'] -is [Collections.IDictionary]) { $errorBody = $errorBody['error'] }
                    if ($errorBody['message'] -is [string]) { $detail = $errorBody['message'] }
                    elseif ($errorBody['code'] -is [string]) { $detail = $errorBody['code'] }
                }
            }
            catch { }
            $secrets = @($Request['Token'], $env:STORE_CLIENT_SECRET, $env:STORE_CLIENT_ID, $env:STORE_TENANT_ID)
            if ($Request.ContainsKey('Form')) { $secrets += @($Request.Form.Values) }
            foreach ($secret in $secrets) {
                if (-not [string]::IsNullOrWhiteSpace($secret)) { $detail = $detail.Replace([string]$secret, '[redacted]') }
            }
            $detail = [regex]::Replace($detail, 'https?://[^\s''"<>]+', '[redacted-url]', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
            $detail = [regex]::Replace($detail, '[\p{Cc}\p{Cf}]', ' ')
            if ($detail.Length -gt 600) { $detail = $detail.Substring(0, 600) }
            if ($detail) { $detail = " Service diagnostic: $detail." }
            throw "HTTP $($response.StatusCode)"
        }
        if ($response.Content) { return $response.Content | ConvertFrom-Json -AsHashtable -Depth 100 -DateKind String }
    }
    catch {
        if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        throw "Store operation '$($Request.Operation)' failed (HTTP $code).$detail Do not replay a write; inspect its receipt and draft."
    }
}

function Send-StoreRequest {
    param([hashtable]$Request, [scriptblock]$Transport)
    if ($Transport) { return & $Transport $Request }
    Invoke-StoreHttp $Request
}

function Get-StoreToken {
    param([scriptblock]$Transport)
    foreach ($name in @('STORE_TENANT_ID', 'STORE_CLIENT_ID', 'STORE_CLIENT_SECRET')) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            throw "Configure the $name Actions secret."
        }
    }
    $tenant = [guid]::Empty
    $client = [guid]::Empty
    if (-not [guid]::TryParse($env:STORE_TENANT_ID, [ref]$tenant) -or
        -not [guid]::TryParse($env:STORE_CLIENT_ID, [ref]$client)) { throw 'Invalid Store application credentials.' }
    $result = Send-StoreRequest @{
        Operation = 'Get token'; Method = 'POST'
        Uri = "https://login.microsoftonline.com/$tenant/oauth2/token"
        Form = @{
            grant_type = 'client_credentials'; client_id = $env:STORE_CLIENT_ID
            client_secret = $env:STORE_CLIENT_SECRET; resource = 'https://manage.devcenter.microsoft.com'
        }
    } $Transport
    if (-not $result.access_token) { throw 'The Store token response was empty.' }
    $result.access_token
}

function Write-StoreJson {
    param([string]$Path, $Value)
    $Path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    $temporary = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 100) + "`n")
        $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
        finally { $stream.Dispose() }
        for ($attempt = 0; ; $attempt++) {
            try {
                # Match Core JsonFiles: ReplaceFile retains shared-delete readers; MoveFileEx overwrite can deny them.
                if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
                else { [IO.File]::Move($temporary, $Path) }
                break
            }
            catch {
                $errorCode = $_.Exception.GetBaseException().HResult -band 0xffff
                $retry = $errorCode -in @(32, 33)
                if ($errorCode -in @(5, 1175)) {
                    try { $retry = [IO.File]::Exists($Path) -and ([IO.File]::GetAttributes($Path) -band [IO.FileAttributes]::ReadOnly) -eq 0 }
                    catch { $retry = $false }
                }
                if (-not $retry -or $attempt -ge 10 -or -not [IO.File]::Exists($temporary)) { throw }
                # Only retry this atomic file replacement, never the Store request that follows its durable intent.
                Start-Sleep -Milliseconds 100
            }
        }
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

function Get-StoreReleasePlan {
    param([Parameter(Mandatory)][string]$SourceRoot, [string]$Version, [scriptblock]$Transport)
    $token = Get-StoreToken $Transport
    $app = Send-StoreRequest @{ Operation = 'Get application'; Method = 'GET'; Uri = $script:ApplicationUri; Token = $token } $Transport
    if ($app['pendingApplicationSubmission']) { throw 'An existing Store draft was preserved. Resolve or inspect it before a new release.' }
    $publishedId = [string]$app.lastPublishedApplicationSubmission.id
    if ($publishedId -cnotmatch '^\d+$') { throw 'The Store application has no published baseline.' }
    $published = Send-StoreRequest @{
        Operation = 'Get published baseline'; Method = 'GET'
        Uri = "$script:ApplicationUri/submissions/$publishedId"; Token = $token
    } $Transport
    if ($published.status -cne 'Published') { throw 'The release baseline is not published.' }
    $previous = @($published.applicationPackages | ForEach-Object { [version]$_.version } | Sort-Object -Descending)[0]
    if (-not $Version) {
        if ($previous.Build -ge 65535) { throw 'Supply a new version after the patch component reaches UInt16 maximum.' }
        $Version = "$($previous.Major).$($previous.Minor).$($previous.Build + 1).0"
    }
    if ($Version -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.0$' -or
        @($Version.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count -or
        [version]$Version -le $previous) { throw 'The release version must be canonical, end in .0, and exceed every published package.' }
    $sourceCommit = (& git -C $SourceRoot rev-parse --verify HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot freeze the source commit.' }
    $tag = "v$previous"
    $PSNativeCommandUseErrorActionPreference = $false
    $baselineCommit = & git -C $SourceRoot rev-parse --verify --quiet "refs/tags/$tag`^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        $baseline = Get-Content -LiteralPath (Join-Path $SourceRoot '.github\store-release-baseline.json') -Raw | ConvertFrom-Json
        if ($baseline.version -cne $previous.ToString() -or $baseline.publishedSubmissionId -cne $publishedId) {
            throw "No source binding exists for published Store version $previous."
        }
        $baselineCommit = $baseline.sourceCommit
    }
    & git -C $SourceRoot merge-base --is-ancestor $baselineCommit $sourceCommit
    if ($LASTEXITCODE -ne 0) { throw 'The published source is not an ancestor of this release.' }
    [ordered]@{
        version = $Version; sourceCommit = $sourceCommit; baselineCommit = [string]$baselineCommit
        publishedVersion = $previous.ToString(); publishedSubmissionId = $publishedId
        languages = @($published.listings.Keys | Sort-Object)
    }
}

function New-StoreReleasePayload {
    param([Collections.IDictionary]$Draft, [string[]]$PackageNames, [Collections.IDictionary]$Notes, [Parameter(Mandatory)][string]$DisplayName)
    $payload = $Draft | ConvertTo-Json -Depth 100 -Compress | ConvertFrom-Json -AsHashtable -Depth 100 -DateKind String
    $payload.targetPublishMode = 'Immediate'
    $packages = @($payload.applicationPackages)
    foreach ($package in $packages) { $package.fileStatus = 'PendingDelete' }
    foreach ($name in $PackageNames) {
        $packages += @{ fileName = $name; fileStatus = 'PendingUpload'; minimumDirectXVersion = 'None'; minimumSystemRam = 'None' }
    }
    $payload.applicationPackages = $packages
    foreach ($listing in $payload.listings.Values) {
        foreach ($field in @('shortTitle', 'voiceTitle', 'sortTitle')) { $listing.baseListing[$field] = $DisplayName }
    }
    if ($Notes) {
        $locales = @($payload.listings.Keys)
        if (@(Compare-Object @($Notes.Keys | Sort-Object) @($locales | Sort-Object)).Count) {
            throw 'Release notes must cover exactly the current Store listing languages.'
        }
        foreach ($locale in $locales) {
            $note = $Notes[$locale]
            if ($note -isnot [string] -or [string]::IsNullOrWhiteSpace($note) -or $note.Length -gt 1500) {
                throw "Invalid release notes for $locale; the limit is 1,500 UTF-16 characters."
            }
            $payload.listings[$locale].baseListing.releaseNotes = $note.Trim()
        }
    }
    Remove-StoreReadOnlyMetadata $payload
    $payload
}

function Remove-StoreReadOnlyMetadata {
    param([Collections.IDictionary]$Submission)
    foreach ($key in @('status', 'statusDetails', 'fileUploadUrl', 'friendlyName')) { $Submission.Remove($key) }
    if ($Submission['pricing'] -is [Collections.IDictionary]) { $Submission.pricing.Remove('isAdvancedPricingModel') }
    foreach ($package in @($Submission['applicationPackages'])) {
        if ($package -is [Collections.IDictionary]) { $package.Remove('targetPlatform') }
    }
    # All listing languages are covered by the audited packages. Store derives these
    # names from AppxManifest DisplayName; writing title here can cause a name conflict.
    if ($Submission['listings'] -is [Collections.IDictionary]) {
        foreach ($listing in $Submission.listings.Values) {
            if ($listing['baseListing'] -is [Collections.IDictionary]) { $listing.baseListing.Remove('title') }
        }
    }
}

function Get-StoreReleaseStatus {
    param([Parameter(Mandatory)][string]$SubmissionId, [scriptblock]$Transport)
    if ($SubmissionId -cnotmatch '^\d+$') { throw 'Supply an exact numeric submission ID.' }
    $token = Get-StoreToken $Transport
    $status = Send-StoreRequest @{
        Operation = 'Get submission status'; Method = 'GET'
        Uri = "$script:ApplicationUri/submissions/$SubmissionId/status"; Token = $token
    } $Transport
    $current = [string]$status.status
    $failed = $current -cin @('Canceled', 'CommitFailed', 'PreProcessingFailed', 'CertificationFailed', 'ReleaseFailed', 'PublishFailed')
    $accepted = $current -cin @('PreProcessing', 'Certification', 'Release', 'PendingPublication', 'Publishing', 'Published')
    $pending = $current -cin @('None', 'PendingCommit', 'CommitStarted')
    if (-not ($failed -or $accepted -or $pending)) { throw 'Unknown Store status; inspect the exact draft without resubmitting.' }
    $errors = @(
        if ($status['statusDetails'] -and $status['statusDetails']['errors']) { $status['statusDetails']['errors'] }
    )
    [ordered]@{ submissionId = $SubmissionId; status = $current; accepted = $accepted -and -not $errors.Count; published = $current -ceq 'Published' -and -not $errors.Count; failed = $failed -or [bool]$errors.Count }
}

function Test-StoreReturnedValue {
    param($Expected, $Actual)
    if ($Expected -is [Collections.IDictionary]) {
        if ($Actual -isnot [Collections.IDictionary]) { return $false }
        foreach ($key in $Expected.Keys) {
            if (-not $Actual.Contains($key) -or -not (Test-StoreReturnedValue $Expected[$key] $Actual[$key])) { return $false }
        }
        return $true
    }
    if ($Expected -is [Collections.IList]) {
        if ($Actual -isnot [Collections.IList] -or $Expected.Count -ne $Actual.Count) { return $false }
        for ($index = 0; $index -lt $Expected.Count; $index++) {
            if (-not (Test-StoreReturnedValue $Expected[$index] $Actual[$index])) { return $false }
        }
        return $true
    }
    [Text.Json.Nodes.JsonNode]::DeepEquals(
        [Text.Json.Nodes.JsonNode]::Parse((ConvertTo-Json -InputObject $Expected -Depth 100 -Compress)),
        [Text.Json.Nodes.JsonNode]::Parse((ConvertTo-Json -InputObject $Actual -Depth 100 -Compress)))
}

function Get-StoreReadbackMismatches {
    param([Collections.IDictionary]$Expected, [Collections.IDictionary]$Actual)
    $Expected = $Expected | ConvertTo-Json -Depth 100 -Compress | ConvertFrom-Json -AsHashtable -Depth 100 -DateKind String
    Remove-StoreReadOnlyMetadata $Expected
    if ($Actual['id'] -cne $Expected.id) { 'id' }
    # Store enriches authored data with generated metadata. Additions are not data loss,
    # but every field we sent (including unknown fields) must still retain its value.
    foreach ($key in $Expected.Keys | Where-Object { $_ -notin @('id', 'applicationPackages') }) {
        if (-not $Actual.Contains($key) -or -not (Test-StoreReturnedValue $Expected[$key] $Actual[$key])) { [string]$key }
    }
    if ($Actual['applicationPackages'] -isnot [Collections.IList]) { 'applicationPackages'; return }
    $packages = @($Actual['applicationPackages'])
    if ($packages.Count -ne $Expected.applicationPackages.Count) { 'applicationPackages.count'; return }
    foreach ($package in $Expected.applicationPackages) {
        $matches = @($packages | Where-Object { $_['fileName'] -ceq $package.fileName })
        if ($matches.Count -ne 1 -or -not (Test-StoreReturnedValue $package $matches[0])) { 'applicationPackages'; return }
    }
}

function Commit-StoreDraft {
    param([Collections.IDictionary]$Receipt, [Collections.IDictionary[]]$Packages, [string]$ReceiptPath, [string]$Token, [scriptblock]$Transport)
    if ($Receipt.commitRequested -or $Receipt.committed) { throw 'A Store commit was already requested; do not replay it.' }
    foreach ($package in $Packages) {
        if ((Get-FileHash -LiteralPath $package.path).Hash -ine $package.sha256) { throw 'A package changed before commit.' }
    }
    $Receipt.phase = 'committingDraft'
    $Receipt.commitRequested = $true
    Write-StoreJson $ReceiptPath $Receipt
    $null = Send-StoreRequest @{
        Operation = 'Commit draft'; Method = 'POST'
        Uri = "$script:ApplicationUri/submissions/$($Receipt.submissionId)/commit"; Token = $Token
    } $Transport
    $Receipt.committed = $true
    $Receipt.phase = 'commitAcknowledged'
    Write-StoreJson $ReceiptPath $Receipt
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(15)
    do {
        $status = Get-StoreReleaseStatus $Receipt.submissionId $Transport
        $Receipt.status = $status.status
        $Receipt.accepted = $status.accepted
        $Receipt.published = $status.published
        if ($status.accepted -and $status.status -cne 'PreProcessing') {
            $null = Confirm-StoreProcessedSubmission $Receipt $ReceiptPath $Token $Transport
        }
        if ($status.accepted -and $Receipt.packagesVerified) { $Receipt.acceptedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
        $Receipt.phase = if ($status.failed) { 'rejected' } elseif ($Receipt.packagesVerified) {
            if ($Receipt.published) { 'published' } else { 'accepted' }
        } elseif ($status.accepted) { 'processingPackages' } else { 'pendingAcceptance' }
        Write-StoreJson $ReceiptPath $Receipt
        if ($status.failed) { throw "Store submission rejected at $($status.status); inspect the existing draft." }
        if ($status.accepted -and $Receipt.packagesVerified) { break }
        Start-Sleep -Seconds 5
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    $Receipt
}

function Confirm-StoreProcessedSubmission {
    param([Collections.IDictionary]$Receipt, [string]$ReceiptPath, [string]$Token, [scriptblock]$Transport)
    if (-not $Receipt['displayName'] -or -not $Receipt['languages']) { throw 'The receipt has no audited display name or listing-language evidence.' }
    $submission = Send-StoreRequest @{
        Operation = 'Read processed submission'; Method = 'GET'
        Uri = "$script:ApplicationUri/submissions/$($Receipt.submissionId)"; Token = $Token
    } $Transport
    if ($submission.status -cnotin @('Certification', 'Release', 'PendingPublication', 'Publishing', 'Published')) { return $false }
    $listings = $submission['listings']
    $mismatches = @(
        if ($submission.id -cne $Receipt.submissionId) { 'submissionId' }
        $active = @($submission.applicationPackages | Where-Object { $_['fileStatus'] -cne 'PendingDelete' })
        if ($active.Count -ne $Receipt.packages.Count) { 'applicationPackages.count' }
        foreach ($package in $Receipt.packages) {
            $matches = @($active | Where-Object { $_['fileName'] -ceq $package.name })
            if ($matches.Count -ne 1 -or $matches[0]['fileStatus'] -cne 'Uploaded' -or
                $matches[0]['version'] -cne $Receipt.version -or $matches[0]['architecture'] -ine $package.architecture -or
                $matches[0]['languages'] -isnot [Collections.IList] -or
                @($Receipt.languages | Where-Object { $_ -notin $matches[0]['languages'] }).Count) {
                "applicationPackages.$($package.architecture)"
            }
        }
        foreach ($locale in $Receipt.languages) {
            $listing = if ($listings -is [Collections.IDictionary]) { $listings[$locale] } else { $null }
            if ($listing -isnot [Collections.IDictionary] -or $listing['baseListing'] -isnot [Collections.IDictionary]) {
                "listings.$locale"
            }
            # Package-supported names come from the audited DisplayName; the API may
            # omit that derived field. An explicitly returned title must still match.
            elseif ($listing.baseListing.Contains('title') -and $listing.baseListing['title'] -cne $Receipt.displayName) {
                "listings.$locale.title"
            }
        }
        if ($submission['statusDetails'] -and $submission.statusDetails['errors']) { 'statusDetails.errors' }
    )
    $Receipt['processingMismatches'] = $mismatches
    $Receipt['packagesVerified'] = $mismatches.Count -eq 0
    if ($mismatches.Count) {
        $Receipt.phase = 'processedDataMismatch'
        Write-StoreJson $ReceiptPath $Receipt
        throw "Processed Store data differs from the audited packages ($($mismatches -join ', ')). No GitHub Release was created."
    }
    $Receipt.status = $submission.status
    $Receipt.published = $submission.status -ceq 'Published'
    $Receipt['processedPackages'] = @($active | ForEach-Object {
        @{ name = $_.fileName; version = $_.version; architecture = $_.architecture; languages = @($_['languages']) }
    })
    $Receipt['processedNames'] = @($Receipt.languages | ForEach-Object {
        @{ language = $_; displayName = $Receipt.displayName
            source = if ($listings[$_].baseListing.Contains('title')) { 'listing' } else { 'auditedPackage' } }
    })
    return $true
}

function Invoke-StoreUpdate {
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Plan,
        [Parameter(Mandatory)][Collections.IDictionary[]]$Packages,
        [Collections.IDictionary]$Notes,
        [string]$NotesSha256,
        [Parameter(Mandatory)][string]$ReceiptPath,
        [scriptblock]$Transport
    )
    if (Test-Path -LiteralPath $ReceiptPath) { throw 'A receipt already exists. Inspect that submission; do not replay this release.' }
    if ($Packages.Count -ne 2 -or @(Compare-Object @('arm64', 'x64') @($Packages.architecture | Sort-Object)).Count) {
        throw 'Store releases require exactly one x64 and one ARM64 upload.'
    }
    foreach ($package in $Packages) {
        if ($package.version -cne $Plan.version -or $package.sourceCommit -cne $Plan.sourceCommit -or
            (Get-FileHash -LiteralPath $package.path).Hash -ine $package.sha256) { throw 'A package does not match the frozen release.' }
        if (-not $package['displayName'] -or $package.displayName -cne $Packages[0].displayName -or
            @($Plan.languages | Where-Object { $_ -notin $package['languages'] }).Count) {
            throw 'Both audited packages must declare the same display name and cover every Store listing language.'
        }
    }
    $token = Get-StoreToken $Transport
    $app = Send-StoreRequest @{ Operation = 'Get application'; Method = 'GET'; Uri = $script:ApplicationUri; Token = $token } $Transport
    if ($app['pendingApplicationSubmission'] -or $app.lastPublishedApplicationSubmission.id -cne $Plan.publishedSubmissionId) {
        throw 'The Store baseline or draft changed after preflight. Existing Store data was preserved.'
    }
    $receipt = [ordered]@{
        version = $Plan.version; sourceCommit = $Plan.sourceCommit; phase = 'creatingDraft'
        submissionId = $null; commitRequested = $false; committed = $false
        status = $null; accepted = $false; published = $false
        notesSha256 = $NotesSha256; acceptedAtUtc = $null; commitConfirmedByStatus = $false
        displayName = $Packages[0].displayName; languages = @($Plan.languages); packagesVerified = $false
        packages = @($Packages | ForEach-Object { @{ name = [IO.Path]::GetFileName($_.path); sha256 = $_.sha256; architecture = $_.architecture } })
    }
    $zipPath = "$ReceiptPath.upload.zip"
    if (Test-Path -LiteralPath $zipPath) { throw 'Unexpected existing upload ZIP; preserve and inspect it.' }
    Write-StoreJson $ReceiptPath $receipt
    try {
        $draft = Send-StoreRequest @{ Operation = 'Create draft'; Method = 'POST'; Uri = "$script:ApplicationUri/submissions"; Token = $token } $Transport
        if ([string]$draft.id -cnotmatch '^\d+$') { throw 'The Store returned no usable draft ID.' }
        $receipt.submissionId = [string]$draft.id
        $receipt.phase = 'draftCreated'
        Write-StoreJson $ReceiptPath $receipt
        if (@(Compare-Object @($Plan.languages | Sort-Object) @($draft.listings.Keys | Sort-Object)).Count) {
            throw 'Store listing languages changed after preflight; the existing draft was not uploaded or committed.'
        }
        $sas = [uri]$draft.fileUploadUrl
        if ($sas.Scheme -cne 'https' -or $sas.Host -notmatch '^[a-z0-9-]+\.blob\.core\.windows\.net$' -or
            $sas.Query -notmatch '(?:\?|&)sig=' -or $sas.Query -match '(?:\?|&)(comp|blockid)=' -or $sas.UserInfo -or $sas.Fragment) {
            throw 'The Store returned an invalid Azure Blob upload URL.'
        }
        $names = @($Packages | ForEach-Object { [IO.Path]::GetFileName($_.path) })
        $payload = New-StoreReleasePayload $draft $names $Notes -DisplayName $Packages[0].displayName
        $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($package in $Packages) {
                $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $package.path, [IO.Path]::GetFileName($package.path), [IO.Compression.CompressionLevel]::NoCompression)
            }
        }
        finally { $zip.Dispose() }
        $receipt.phase = 'uploading'
        Write-StoreJson $ReceiptPath $receipt
        $null = Send-StoreRequest @{ Operation = 'Upload ZIP'; Method = 'PUT'; Uri = $sas.AbsoluteUri; File = $zipPath } $Transport
        $receipt.phase = 'updatingDraft'
        Write-StoreJson $ReceiptPath $receipt
        $draftUri = "$script:ApplicationUri/submissions/$($draft.id)"
        $null = Send-StoreRequest @{ Operation = 'Update draft'; Method = 'PUT'; Uri = $draftUri; Token = $token; Body = $payload } $Transport
        $updated = Send-StoreRequest @{ Operation = 'Read updated draft'; Method = 'GET'; Uri = $draftUri; Token = $token } $Transport
        $mismatches = @(if ($updated -is [Collections.IDictionary]) { Get-StoreReadbackMismatches $payload $updated } else { 'response' })
        if ($mismatches.Count) {
            $receipt['readbackMismatches'] = $mismatches
            Write-StoreJson $ReceiptPath $receipt
            throw "The Store did not retain authored submission data ($($mismatches -join ', ')). The draft was not committed."
        }
        Commit-StoreDraft $receipt $Packages $ReceiptPath $token $Transport
    }
    finally { if ([IO.File]::Exists($zipPath)) { [IO.File]::Delete($zipPath) } }
}

function Publish-StoreGitHubRelease {
    param(
        [Parameter(Mandatory)][string]$ReceiptPath,
        [Parameter(Mandatory)][string]$NotesPath,
        [scriptblock]$Transport
    )
    $receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json -AsHashtable -DateKind String
    $notes = Get-Content -LiteralPath $NotesPath -Raw | ConvertFrom-Json -AsHashtable -DateKind String
    $notesHash = (Get-FileHash -LiteralPath $NotesPath).Hash.ToLowerInvariant()
    if (-not ($receipt.committed -or $receipt.commitConfirmedByStatus) -or -not $receipt.accepted -or -not $receipt['packagesVerified'] -or
        $receipt.phase -cnotin @('accepted', 'published') -or
        $receipt.status -cnotin @('PreProcessing', 'Certification', 'Release', 'PendingPublication', 'Publishing', 'Published') -or
        $receipt.sourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
        $receipt.version -cnotmatch '^\d+\.\d+\.\d+\.0$' -or
        $receipt.version -cne $notes.version -or $receipt.sourceCommit -cne $notes.sourceCommit -or
        $receipt.notesSha256 -cne $notesHash -or -not $receipt.acceptedAtUtc) {
        throw 'GitHub Release requires the exact Store-accepted source and unchanged localized notes.'
    }
    if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'GitHub publication requires a contents:write job token.' }
    $tag = "v$($receipt.version)"
    $api = 'https://api.github.com/repos/Richasy/Rodel.MinecraftLauncher'
    $tagRequest = @{
        Operation = 'Get GitHub tag'; Method = 'GET'; Uri = "$api/git/ref/tags/$tag"
        Token = $env:GH_TOKEN; AllowNotFound = $true
    }
    $existingTag = Send-StoreRequest $tagRequest $Transport
    if ($existingTag -and ($existingTag.object.type -cne 'commit' -or $existingTag.object.sha -cne $receipt.sourceCommit)) {
        throw 'An existing release tag identifies different source; it was not overwritten.'
    }
    $body = [Collections.Generic.List[string]]::new()
    $body.Add('> Microsoft Store submission accepted. Certification and publication may still be pending.')
    $body.Add('')
    $body.Add('[Microsoft Store](https://apps.microsoft.com/detail/9MW9ZMS2K47C)')
    if ($notes.notesMode -ceq 'generated') {
        foreach ($entry in @(@('zh-cn', '简体中文'), @('zh-tw', '繁體中文'), @('en', 'English'))) {
            $note = $notes.notes[$entry[0]]
            if ($note -isnot [string] -or [string]::IsNullOrWhiteSpace($note) -or $note.Length -gt 1500) { throw 'GitHub Release has incomplete localized notes.' }
            $body.Add("`n## $($entry[1])`n`n$note")
        }
    }
    elseif ($notes.notesMode -cne 'preserve-existing') { throw 'Unknown GitHub release-note policy.' }
    $body.Add("`n<!-- rodel-release sourceCommit=$($receipt.sourceCommit) notesSha256=$notesHash submittedAtUtc=$($receipt.acceptedAtUtc) -->")
    $text = $body -join "`n"
    $release = Send-StoreRequest @{
        Operation = 'Get GitHub release'; Method = 'GET'; Uri = "$api/releases/tags/$tag"
        Token = $env:GH_TOKEN; AllowNotFound = $true
    } $Transport
    if (-not $release) {
        $release = Send-StoreRequest @{
            Operation = 'Create GitHub release'; Method = 'POST'; Uri = "$api/releases"; Token = $env:GH_TOKEN
            Body = @{
                tag_name = $tag; target_commitish = $receipt.sourceCommit; name = "$($receipt.displayName) $tag"
                body = $text; draft = $false; prerelease = $false; generate_release_notes = $false
            }
        } $Transport
    }
    $confirmedTag = Send-StoreRequest $tagRequest $Transport
    if (-not $confirmedTag -or $confirmedTag.object.type -cne 'commit' -or $confirmedTag.object.sha -cne $receipt.sourceCommit -or
        $release.tag_name -cne $tag -or $release.body -cne $text -or $release.draft -or $release.prerelease -or
        $release.html_url -cne "https://github.com/Richasy/Rodel.MinecraftLauncher/releases/tag/$tag") {
        throw 'The GitHub Release does not match the Store source and notes. Do not repeat the Store submission.'
    }
    $record = @{ tag = $tag; sourceCommit = $receipt.sourceCommit; notesSha256 = $notesHash; releaseId = $release.id; url = $release.html_url }
    Write-StoreJson (Join-Path (Split-Path -Parent $ReceiptPath) 'github-release.json') $record
    $record
}

function Complete-StoreGitHubRelease {
    param([Parameter(Mandatory)][string]$ReceiptPath, [Parameter(Mandatory)][string]$NotesPath, [scriptblock]$Transport)
    $receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json -AsHashtable -DateKind String
    if (-not $receipt.commitRequested) { throw 'This release never requested a Store commit; it cannot be finalized.' }
    $status = Get-StoreReleaseStatus -SubmissionId $receipt.submissionId -Transport $Transport
    if (-not $status.accepted -or $status.failed) { throw "Store acceptance is not confirmed ($($status.status)); no GitHub Release was created." }
    $receipt.status = $status.status
    $receipt.accepted = $status.accepted
    $receipt.published = $status.published
    if ($status.status -ceq 'PreProcessing' -or -not (Confirm-StoreProcessedSubmission $receipt $ReceiptPath (Get-StoreToken $Transport) $Transport)) {
        throw 'Store package processing has not completed; no GitHub Release was created.'
    }
    $receipt.commitConfirmedByStatus = $true
    $receipt.phase = if ($receipt.published) { 'published' } else { 'accepted' }
    if (-not $receipt.acceptedAtUtc) { $receipt.acceptedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
    Write-StoreJson $ReceiptPath $receipt
    Publish-StoreGitHubRelease -ReceiptPath $ReceiptPath -NotesPath $NotesPath -Transport $Transport
}

Export-ModuleMember -Function Get-StoreReleasePlan, New-StoreReleasePayload, Get-StoreReleaseStatus, Invoke-StoreUpdate, Publish-StoreGitHubRelease, Complete-StoreGitHubRelease
