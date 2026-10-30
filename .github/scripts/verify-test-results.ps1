[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [Parameter(Mandatory)][string]$ReportName
)

$ErrorActionPreference = 'Stop'
$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter $ReportName -Recurse -File)
if ($reports.Count -eq 0) { throw "No $ReportName under $ResultsDirectory." }
foreach ($report in $reports) {
    [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
    $summary = $document.SelectSingleNode("//*[local-name()='ResultSummary']")
    if ($null -eq $summary) { throw "No test summary in $($report.FullName)." }
    $counters = $summary.SelectSingleNode("*[local-name()='Counters']")
    if ($null -eq $counters) { throw "No test counters in $($report.FullName)." }
    foreach ($name in @('total', 'executed', 'passed', 'failed')) {
        $number = 0L
        if (-not [long]::TryParse($counters.GetAttribute($name), [ref]$number) -or $number -lt 0) {
            throw "Invalid $name counter in $($report.FullName)."
        }
    }
    $executed = [long]$counters.executed
    if ($executed -le 0 -or [long]$counters.total -lt $executed -or [long]$counters.passed -ne $executed -or
        [long]$counters.failed -ne 0 -or $summary.GetAttribute('outcome') -ne 'Completed') {
        throw "Tests did not complete successfully in $($report.FullName)."
    }
    Write-Host "$($report.Name): $executed executed, $($counters.passed) passed, 0 failed."
}
