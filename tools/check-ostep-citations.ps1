<#
.SYNOPSIS
    Verify every OSTEP §N.M citation in the repo against the chapter index.

.DESCRIPTION
    Scans .md and .cs files for §N.M tokens and checks each against
    tools/ostep-sections.json, which is transcribed from the OSTEP chapter PDFs.

    This catches the failure class that code review cannot: a citation that names
    a section which does not exist, or which exists but is a Summary page. Roughly
    55 of the ~180 claims corrected in ADR 0030 were of this shape, and every one
    of them had survived a careful read - a plausible number in a plausible
    chapter is exactly what a human eye does not catch.

    Two things it does NOT do, by design:
      - It cannot tell whether §N.M contains the content attributed to it. A
        number that exists but is the wrong section passes. That needs the
        chapter PDF.
      - It does not check quoted wording. Citation accuracy and quotation
        accuracy are separate axes and both failed in ADR 0030.

.PARAMETER Path
    Repository root. Defaults to the parent of this script.

.PARAMETER Json
    Path to the section index. Defaults to tools/ostep-sections.json next to
    this script.

.PARAMETER List
    Print every citation that resolves, not just the failures.

.EXAMPLE
    pwsh tools/check-ostep-citations.ps1

.EXAMPLE
    pwsh tools/check-ostep-citations.ps1 -List     # list every citation
#>
[CmdletBinding()]
param(
    [string]$Path,
    [string]$Json,
    [switch]$List
)

$ErrorActionPreference = 'Stop'
if (-not $Path) { $Path = Split-Path -Parent $PSScriptRoot }
if (-not $Json) { $Json = Join-Path $PSScriptRoot 'ostep-sections.json' }

$index = Get-Content $Json -Raw | ConvertFrom-Json
$sections = $index.sections
$known = @{}
$sections.PSObject.Properties | ForEach-Object { $known[$_.Name] = $_.Value }

# Files that legitimately mention wrong section numbers: the ADRs that record
# having fixed them. Their findings are history, not defects.
$exempt = @(
    'docs/adr/0030-citations-verified-against-pdfs.md'
)

# A citation is §N.M, optionally with a closing range: §17.2-§17.4.
# The section sign is U+00A7.
$pattern = [char]0x00A7 + '\s*(\d{1,2})\.(\d{1,2})(?:\s*-\s*' + [char]0x00A7 + '\s*(\d{1,2})\.(\d{1,2}))?'

$files = Get-ChildItem -Path $Path -Recurse -Include *.md, *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj)\\' -and
                   $_.FullName -notmatch 'ostep-sections\.json$' }

$bad = New-Object System.Collections.Generic.List[object]
$checked = 0
$skipped = 0

foreach ($file in $files) {
    $rel = $file.FullName.Substring($Path.Length).TrimStart('\').Replace('\', '/')
    if ($exempt -contains $rel) { $skipped++; continue }

    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($m in [regex]::Matches($lines[$i], $pattern)) {
            # RFC 2100 / 4226 / 6238 and NIST SP citations use the same section
            # sign and are NOT OSTEP. Check the text immediately preceding this
            # match on this line, and the whole previous line if the match is
            # near the start (a citation can wrap).
            $lead = [Math]::Max(0, $m.Index - 60)
            $context = $lines[$i].Substring($lead, $m.Index - $lead)
            if ($context -match '(RFC\s*\d{3,4}|\bNIST\b|SP\s*800)') {
                $skipped++
                continue
            }
            $checked++
            $num = "$($m.Groups[1].Value).$($m.Groups[2].Value)"
            $endNum = $null
            if ($m.Groups[3].Success) {
                $endNum = "$($m.Groups[3].Value).$($m.Groups[4].Value)"
            }

            foreach ($target in @($num, $endNum)) {
                if (-not $target) { continue }
                if (-not $known.ContainsKey($target)) {
                    $bad.Add([pscustomobject]@{
                        File = $rel; Line = $i + 1; Section = $target
                        Problem = 'does not exist'
                    })
                }
                elseif ($List) {
                    Write-Host ("  ok  {0}:{1} §{2} = {3}" -f $rel, ($i + 1), $target, $known[$target])
                }
            }
        }
    }
}

Write-Host ''
Write-Host ("OSEP citation check: {0} references across {1} files ({2} skipped as historical records)" -f $checked, ($files.Count - $skipped), $skipped)
Write-Host ("Section index: {0} sections across {1} chapters" -f $known.Count, (($known.Keys | ForEach-Object { [int]$_.Split('.')[0] } | Sort-Object -Unique).Count))
Write-Host ''

if ($bad.Count -eq 0) {
    Write-Host 'PASS - every §N.M resolves to a real section.' -ForegroundColor Green
    Write-Host ''
    Write-Host 'Note: this proves the section EXISTS, not that it contains what the text' -ForegroundColor DarkGray
    Write-Host 'attributes to it. A wrong-but-real citation still passes; check the chapter' -ForegroundColor DarkGray
    Write-Host 'PDF for those. See ADR 0030.' -ForegroundColor DarkGray
    exit 0
}

Write-Host ("FAIL - {0} citation(s) name a section that does not exist:" -f $bad.Count) -ForegroundColor Red
Write-Host ''
$bad | Sort-Object File, Line | Format-Table -AutoSize |
    Out-String -Width 200 | Write-Host
foreach ($b in $bad) {
    Write-Host ("  {0}:{1}  §{2}  - {3}" -f $b.File, $b.Line, $b.Section, $b.Problem) -ForegroundColor Red
}
Write-Host ''
Write-Host "The chapter is: a bare §N.M where N.M is past the chapters last section." -ForegroundColor DarkGray
Write-Host 'The section index does not include Ch. 52 (Dialogue on Security), which' -ForegroundColor DarkGray
Write-Host 'exists but has no numbered sections.' -ForegroundColor DarkGray
exit 1