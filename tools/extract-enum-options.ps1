# Extracts Swift enum case names/raw values from the original Atoll source
# so the Windows settings window can populate enum editors faithfully.
$root = "C:\Users\Anidin\projects\atoll-windows\original\DynamicIsland"
$out = "C:\Users\Anidin\projects\atoll-windows\docs\notes\enum-options.json"

$files = Get-ChildItem -Path $root -Recurse -Filter *.swift
$enums = @{}

foreach ($file in $files) {
    $text = Get-Content $file.FullName -Raw
    $declRegex = [regex]'enum\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::[^\{]+)?\{'
    foreach ($m in $declRegex.Matches($text)) {
        $name = $m.Groups[1].Value
        $start = $m.Index + $m.Length
        $depth = 1
        $i = $start
        $inString = $false
        $inLineComment = $false
        $inBlockComment = $false
        while ($i -lt $text.Length -and $depth -gt 0) {
            $c = $text[$i]
            $next = if ($i + 1 -lt $text.Length) { $text[$i + 1] } else { [char]0 }

            if ($inLineComment) {
                if ($c -eq "`n") { $inLineComment = $false }
                $i++; continue
            }
            if ($inBlockComment) {
                if ($c -eq '*' -and $next -eq '/') { $inBlockComment = $false; $i += 2; continue }
                $i++; continue
            }
            if ($inString) {
                if ($c -eq '\') { $i += 2; continue }
                if ($c -eq '"') { $inString = $false }
                $i++; continue
            }
            if ($c -eq '/' -and $next -eq '/') { $inLineComment = $true; $i += 2; continue }
            if ($c -eq '/' -and $next -eq '*') { $inBlockComment = $true; $i += 2; continue }
            if ($c -eq '"') { $inString = $true; $i++; continue }
            if ($c -eq '{') { $depth++ }
            elseif ($c -eq '}') { $depth-- }
            $i++
        }

        $body = $text.Substring($start, [Math]::Max(0, $i - $start - 1))
        $cases = @()
        $bodyDepth = 0
        foreach ($line in ($body -split "`n")) {
            $trim = $line.Trim()
            $isTopLevel = $bodyDepth -eq 0
            $bodyDepth += ([regex]::Matches($trim, '\{')).Count
            $bodyDepth -= ([regex]::Matches($trim, '\}')).Count
            if (-not $isTopLevel) { continue }
            if ($trim -match '^case\s+(.+?)\s*$') {
                $list = $Matches[1]
                if ($list.StartsWith('indirect')) { $list = $list.Substring(8).Trim() }
                foreach ($part in ($list -split ',')) {
                    $p = $part.Trim()
                    if ($p -match '^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*"([^"]*)"') {
                        $cases += $Matches[2]
                    } elseif ($p -match '^([A-Za-z_][A-Za-z0-9_]*)') {
                        $cases += $Matches[1]
                    }
                }
            }
        }
        if ($cases.Count -gt 0 -and -not $enums.ContainsKey($name)) {
            $enums[$name] = $cases
        }
    }
}

$enums.GetEnumerator() | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{ name = $_.Key; values = $_.Value }
} | ConvertTo-Json -Depth 4 | Set-Content -Path $out -Encoding UTF8

Write-Output "Wrote $($enums.Count) enums to $out"
