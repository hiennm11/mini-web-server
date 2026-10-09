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

# Files that legitimately contain wrong citations: the ADRs that record having
# fixed them. Their examples ARE the defects - a checker that passed them would
# be unable to describe the thing it exists to catch. Both are exempt wholesale.
# ADR 0031 documents this: writing that ADR made the checker fail on it eleven
# times, which is the correct behaviour and the reason for the exemption.
$exempt = @(
    'docs/adr/0030-citations-verified-against-pdfs.md',
    'docs/adr/0031-citation-title-check.md'
)

# A citation is §N.M, optionally with a closing range: §17.2-§17.4.
# The section sign is U+00A7.
$pattern = [char]0x00A7 + '\s*(\d{1,2})\.(\d{1,2})(?:\s*-\s*' + [char]0x00A7 + '\s*(\d{1,2})\.(\d{1,2}))?'

# A citation carrying a quoted title: §9.6 "Stride Scheduling".
$titlePattern = [char]0x00A7 + '\s*(\d{1,2}\.\d{1,2})\s*["' + [char]0x201C + ']([^"' + [char]0x201D + ']{3,70}?)["' + [char]0x201D + ']'

function Normalize-Heading([string]$s) {
    $s = $s.ToLowerInvariant().Replace([char]0x2019, "'")
    $s = $s -replace '[^a-z0-9 #]', ' '
    return ($s -replace '\s+', ' ').Trim()
}

# Reverse maps: normalized title -> section number, and normalized sub-heading
# -> its owning section. A quoted title that resolves to a DIFFERENT real
# section is an unambiguous error: the number and the title disagree, and one
# of them is wrong.
$titleToSection = @{}
foreach ($p in $known.Keys) {
    $t = Normalize-Heading $known[$p]
    if (-not $titleToSection.ContainsKey($t)) { $titleToSection[$t] = $p }
}
$subheadings = @{}
if ($index.PSObject.Properties.Name -contains 'subheadings') {
    $index.subheadings.PSObject.Properties | ForEach-Object {
        $owner = $_.Name
        foreach ($sh in $_.Value) { $subheadings[(Normalize-Heading $sh)] = $owner }
    }
}

$files = Get-ChildItem -Path $Path -Recurse -Include *.md, *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj)\\' -and
                   $_.FullName -notmatch 'ostep-sections\.json$' }

$bad = New-Object System.Collections.Generic.List[object]
$checked = 0
$titlesChecked = 0
$titlesOk = 0
$skipped = 0

foreach ($file in $files) {
    $rel = $file.FullName.Substring($Path.Length).TrimStart('\').Replace('\', '/')
    if ($exempt -contains $rel) { $skipped++; continue }

    $text = Get-Content $file.FullName -Raw
    $lines = $text -split "`r?`n"

    # Pass 1: does every §N.M name a section that exists?
    foreach ($m in [regex]::Matches($text, $pattern)) {
        # RFC 2100 / 4226 / 6238 and NIST SP citations use the same section
        # sign and are NOT OSTEP.
        $lead = [Math]::Max(0, $m.Index - 60)
        $context = $text.Substring($lead, $m.Index - $lead)
        if ($context -match '(RFC\s*\d{3,4}|\bNIST\b|SP\s*800)') { $skipped++; continue }
        $checked++
        $num = "$($m.Groups[1].Value).$($m.Groups[2].Value)"
        $endNum = $null
        if ($m.Groups[3].Success) { $endNum = "$($m.Groups[3].Value).$($m.Groups[4].Value)" }
        foreach ($target in @($num, $endNum)) {
            if (-not $target) { continue }
            if (-not $known.ContainsKey($target)) {
                $lineNo = ($text.Substring(0, $m.Index) -split "`n").Count
                $bad.Add([pscustomobject]@{
                    File = $rel; Line = $lineNo; Section = $target
                    Problem = 'section does not exist'
                })
            }
            elseif ($List) {
                Write-Host ("  ok  {0}:{1} §{2} = {3}" -f $rel, $lineNo, $target, $known[$target])
            }
        }
    }

    # Pass 2: where a citation quotes a title, does that title belong to the
    # section cited? Three outcomes are legal: the title IS this section's
    # title; the title is a sub-heading inside this section; or the quoted
    # text is prose from the section rather than its title. Only a title that
    # belongs to a DIFFERENT section is an error, because then the number and
    # the title cannot both be right.
    foreach ($m in [regex]::Matches($text, $titlePattern)) {
        $num = $m.Groups[1].Value
        if (-not $known.ContainsKey($num)) { continue }   # pass 1 already flagged it
        $lead = [Math]::Max(0, $m.Index - 60)
        if ($text.Substring($lead, $m.Index - $lead) -match '(RFC\s*\d{3,4}|\bNIST\b|SP\s*800)') { continue }

        $titlesChecked++
        $lineNo = ($text.Substring(0, $m.Index) -split "`n").Count
        $rawQuoted = $m.Groups[2].Value
        $quoted = Normalize-Heading $rawQuoted
        $own = Normalize-Heading $known[$num]

        # OSTEP titles are Title Case. A lower-case quotation is prose someone
        # put in quotes, not a heading, and must not be read as one - otherwise
        # §22.5 "implementation" gets flagged against §9.3 "Implementation".
        $words = [regex]::Matches($rawQuoted, "[A-Za-z][a-z']*") |
                 ForEach-Object { $_.Value } | Where-Object { $_.Length -gt 2 }
        if ($words.Count -lt 2) { $titlesOk++; continue }
        $capitalised = ($words | Where-Object { $_[0] -is [char] -and [char]::IsUpper($_[0]) }).Count
        if ($capitalised -lt [Math]::Max(2, [int]($words.Count * 0.6))) { $titlesOk++; continue }

        if ($quoted -eq $own) { $titlesOk++; continue }
        if ($subheadings.ContainsKey($quoted)) {
            if ($subheadings[$quoted] -eq $num) { $titlesOk++ }
            else {
                $bad.Add([pscustomobject]@{
                    File = $rel; Line = $lineNo; Section = $num
                    Problem = ("cites §{0} but quotes the sub-heading of §{1}" -f $num, $subheadings[$quoted])
                })
            }
            continue
        }
        if ($titleToSection.ContainsKey($quoted)) {
            $real = $titleToSection[$quoted]
            $bad.Add([pscustomobject]@{
                File = $rel; Line = $lineNo; Section = $num
                Problem = ("number and title disagree: §{0} is titled '{1}', but the quoted title is §{2}" -f $num, $known[$num], $real)
            })
            continue
        }
        # Prose quotation from the section - not a title, so not checkable.
        $titlesOk++
    }
}

Write-Host ''
Write-Host ("OSEP citation check: {0} section references across {1} files ({2} skipped as external/historical)" -f $checked, ($files.Count - $skipped), $skipped)
Write-Host ("Section index: {0} sections across {1} chapters, {2} verified sub-headings" -f $known.Count, (($known.Keys | ForEach-Object { [int]$_.Split('.')[0] } | Sort-Object -Unique).Count), $subheadings.Count)
Write-Host ("Title check: {0} citations quote a title; {1} agree with the section cited" -f $titlesChecked, $titlesOk)
Write-Host ''

if ($bad.Count -eq 0) {
    Write-Host 'PASS - every §N.M resolves, and every quoted title matches its section.' -ForegroundColor Green
    Write-Host ''
    Write-Host 'Note: this proves the section EXISTS and that a quoted TITLE belongs to' -ForegroundColor DarkGray
    Write-Host 'it. It does not check prose quotations, and it cannot tell whether the' -ForegroundColor DarkGray
    Write-Host 'section holds the specific content attributed to it. A correct title on' -ForegroundColor DarkGray
    Write-Host 'the wrong section still passes. See ADR 0030 and ADR 0031.' -ForegroundColor DarkGray
    exit 0
}

Write-Host ("FAIL - {0} citation problem(s):" -f $bad.Count) -ForegroundColor Red
Write-Host ''
$bad | Sort-Object File, Line | Format-Table -AutoSize |
    Out-String -Width 220 | Write-Host
foreach ($b in $bad) {
    Write-Host ("  {0}:{1}  §{2}  - {3}" -f $b.File, $b.Line, $b.Section, $b.Problem) -ForegroundColor Red
}
Write-Host ''
Write-Host "'section does not exist' means the number is past the end of the chapter." -ForegroundColor DarkGray
Write-Host "'number and title disagree' means the citation quotes the title of a" -ForegroundColor DarkGray
Write-Host "different section: exactly one of the two is right. This repo's history is" -ForegroundColor DarkGray
Write-Host "entirely off-by-one errors, where the title was right and the number wrong." -ForegroundColor DarkGray
exit 1