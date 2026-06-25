param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$patchFile = Join-Path $RepoRoot 'Source\WorkbenchConnect\Patches\BillStack_Patches.cs'
$source = Get-Content -LiteralPath $patchFile -Raw
$failures = @()

if ($source -notmatch 'ShouldSyncFromBillInterface') {
    $failures += 'Bill UI synchronization must be guarded by ShouldSyncFromBillInterface.'
}

if ($source -notmatch 'EventType\.Repaint') {
    $failures += 'Bill UI synchronization must skip EventType.Repaint.'
}

if ($source -notmatch 'EventType\.Layout') {
    $failures += 'Bill UI synchronization must skip EventType.Layout.'
}

if ($source -notmatch 'public static void BillInterface_Postfix\(Bill __instance\)\s*\{\s*if \(!ShouldSyncFromBillInterface\(\)\)\s*return;\s*SyncFromBillStack') {
    $failures += 'BillInterface_Postfix must return before synchronization when ShouldSyncFromBillInterface is false.'
}

if ($source -notmatch 'public static void DialogBillConfig_Postfix\(Bill_Production ___bill\)\s*\{\s*if \(!ShouldSyncFromBillInterface\(\)\)\s*return;\s*SyncFromBillStack') {
    $failures += 'DialogBillConfig_Postfix must return before synchronization when ShouldSyncFromBillInterface is false.'
}

if ($failures.Count -gt 0) {
    Write-Error (($failures -join [Environment]::NewLine))
    exit 1
}

Write-Host 'Bill UI synchronization skips repaint and layout events.'
