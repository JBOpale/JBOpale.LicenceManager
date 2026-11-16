<#
.SYNOPSIS
  Règle de couverture de code (voir CLAUDE.md) : 90 % des lignes C# ajoutées/modifiées par la PR,
  et couverture globale des lignes jamais sous le seuil de .github/seuil-couverture.json.

.DESCRIPTION
  Lancé par l'étape « Couverture (90 % du code modifié, global au moins au seuil) » du job requis
  « LicenceManager - Tests PR ». Compatible Windows PowerShell 5.1 et PowerShell 7.

  - Fusionne tous les rapports Cobertura (coverlet) trouvés sous -ReportsDir : une ligne est couverte
    si hits > 0 dans au moins un rapport. Les chemins sont ramenés au chemin relatif au dépôt via
    <sources>/<source> + filename.
  - Code modifié (si -BaseRef est fourni) : git diff --unified=0 --diff-filter=AMR <BaseRef>...HEAD
    sur les *.cs, hors tests/, dossiers *Tests*, Migrations/, obj/. Seules les lignes couvrables
    (présentes dans les rapports) comptent. Un fichier modifié absent des rapports est ignoré si
    d'autres fichiers de son projet y figurent, sinon ses lignes modifiées comptent comme non couvertes.
  - Global : couverture des lignes >= lineCoverage du fichier de référence ; ::notice:: si la mesure
    le dépasse d'au moins 1 point (relever le seuil, jamais le baisser).
  - Échoue si aucun rapport n'est trouvé alors que le dépôt contient des projets de tests.

.EXAMPLE
  pwsh .github/scripts/Test-Coverage.ps1 -ReportsDir TestResults -BaseRef origin/dev
#>
[CmdletBinding()]
param(
  [string] $ReportsDir = 'TestResults',
  [string] $BaseRef = '',
  [string] $BaselineFile = '.github/seuil-couverture.json',
  [double] $DiffThreshold = 90
)

$ErrorActionPreference = 'Stop'

function Write-Summary([string] $text) {
  Write-Host $text
  if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $text -Encoding UTF8 }
}

function ConvertTo-RepoPath([string] $path, [string] $root) {
  $p = $path -replace '\\', '/'
  $r = ($root -replace '\\', '/').TrimEnd('/') + '/'
  if ($p.StartsWith($r, [System.StringComparison]::OrdinalIgnoreCase)) { return $p.Substring($r.Length) }
  return $null
}

function Test-ExcludedPath([string] $relPath) {
  $p = '/' + $relPath
  return ($p -match '/tests?/' -or $p -match '/[^/]*Tests[^/]*/' -or $p -match '/Migrations/' -or $p -match '/obj/')
}

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw 'git rev-parse a échoué : lancer le script depuis le dépôt.' }

# --- 1. Fusion des rapports Cobertura ------------------------------------------------------------
$coverage = @{}   # chemin relatif -> @{ numéro de ligne -> $true si couverte }
$reports = @()
if (Test-Path $ReportsDir) {
  $reports = @(Get-ChildItem -Path $ReportsDir -Recurse -Filter '*.cobertura.xml' -File)
}
$testProjects = @(& git -C $repoRoot ls-files '*Tests*.csproj')
if ($reports.Count -eq 0) {
  if ($testProjects.Count -gt 0) {
    Write-Host "::error::Aucun rapport de couverture (*.cobertura.xml) sous '$ReportsDir' alors que le dépôt contient $($testProjects.Count) projet(s) de tests."
    exit 1
  }
  Write-Summary 'Aucun projet de tests ni rapport de couverture : contrôle sans objet.'
  exit 0
}

foreach ($report in $reports) {
  [xml] $xml = Get-Content -Path $report.FullName -Raw -Encoding UTF8
  $sources = @()
  foreach ($s in @($xml.coverage.sources.source)) { if ($s) { $sources += [string] $s } }
  foreach ($class in $xml.SelectNodes('//class')) {
    $fileName = [string] $class.filename
    $candidates = @()
    if ([System.IO.Path]::IsPathRooted($fileName) -or $fileName -match '^[A-Za-z]:[\\/]') { $candidates += $fileName }
    else { foreach ($s in $sources) { $candidates += (($s -replace '\\', '/').TrimEnd('/') + '/' + ($fileName -replace '\\', '/')) } }
    $rel = $null
    foreach ($c in $candidates) {
      $r = ConvertTo-RepoPath $c $repoRoot
      if ($r -and (Test-Path (Join-Path $repoRoot $r))) { $rel = $r; break }
    }
    if (-not $rel -and $candidates.Count -gt 0) { $rel = ConvertTo-RepoPath $candidates[0] $repoRoot }
    if (-not $rel) { continue }   # fichier hors dépôt (paquet NuGet, SDK...)
    if (-not $coverage.ContainsKey($rel)) { $coverage[$rel] = @{} }
    $lines = $coverage[$rel]
    foreach ($line in $class.SelectNodes('lines/line')) {
      $n = [int] $line.number
      $hit = ([long] $line.hits) -gt 0
      if ($lines.ContainsKey($n)) { $lines[$n] = ($lines[$n] -or $hit) } else { $lines[$n] = $hit }
    }
  }
}

$total = 0; $covered = 0
foreach ($file in $coverage.Keys) {
  foreach ($v in $coverage[$file].Values) { $total++; if ($v) { $covered++ } }
}
if ($total -eq 0) {
  Write-Host "::error::Les $($reports.Count) rapport(s) de couverture ne contiennent aucune ligne du dépôt."
  exit 1
}
$globalPct = [math]::Round(100.0 * $covered / $total, 2)
$failed = $false

Write-Summary '## Couverture de code'
Write-Summary ''
Write-Summary "- Rapports fusionnés : $($reports.Count) ; fichiers : $($coverage.Count)"
Write-Summary "- Couverture globale des lignes : **$globalPct %** ($covered / $total)"

# --- 2. Règle globale -----------------------------------------------------------------------------
$baselinePath = Join-Path $repoRoot $BaselineFile
if (-not (Test-Path $baselinePath)) { throw "Fichier de référence introuvable : $BaselineFile" }
$baseline = [double] ((Get-Content -Path $baselinePath -Raw -Encoding UTF8 | ConvertFrom-Json).lineCoverage)
Write-Summary "- Seuil global ($BaselineFile) : $baseline %"
if ($globalPct -lt $baseline) {
  Write-Host "::error::Couverture globale $globalPct % sous le seuil $baseline % ($BaselineFile) : ajouter des tests, ne jamais baisser le seuil."
  $failed = $true
} elseif ($globalPct -ge ($baseline + 1)) {
  Write-Host "::notice::Couverture globale $globalPct % : relever lineCoverage de $BaselineFile à $([math]::Floor($globalPct))."
}

# --- 3. Règle sur le code modifié -----------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($BaseRef)) {
  Write-Summary '- Code modifié : pas de branche de base (lancement manuel), seule la règle globale s''applique.'
} else {
  $diff = & git -C $repoRoot diff --unified=0 --diff-filter=AMR "$BaseRef...HEAD" -- '*.cs'
  if ($LASTEXITCODE -ne 0) { throw "git diff contre $BaseRef a échoué (checkout avec fetch-depth: 0 ?)." }

  $changed = @{}   # chemin relatif -> liste des lignes ajoutées/modifiées
  $current = $null
  foreach ($l in @($diff)) {
    if ($l -match '^\+\+\+ (?:b/)?(.+)$') {
      $path = $Matches[1].Trim()
      if ($path -eq '/dev/null' -or (Test-ExcludedPath $path)) { $current = $null }
      else { $current = $path; if (-not $changed.ContainsKey($path)) { $changed[$path] = New-Object System.Collections.Generic.List[int] } }
    } elseif ($current -and $l -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@') {
      $start = [int] $Matches[1]
      $count = 1; if ($Matches[2]) { $count = [int] $Matches[2] }
      for ($i = 0; $i -lt $count; $i++) { $changed[$current].Add($start + $i) }
    }
  }

  $coveredFiles = @($coverage.Keys)
  $diffTotal = 0; $diffCovered = 0
  $uncovered = New-Object System.Collections.Generic.List[string]
  foreach ($file in ($changed.Keys | Sort-Object)) {
    $lineNumbers = $changed[$file]
    if ($coverage.ContainsKey($file)) {
      $map = $coverage[$file]
      foreach ($n in $lineNumbers) {
        if ($map.ContainsKey($n)) {
          $diffTotal++
          if ($map[$n]) { $diffCovered++ } else { $uncovered.Add("${file}:$n") }
        }
      }
      continue
    }
    # Fichier absent des rapports : ignoré si son projet est mesuré, sinon non couvert.
    $dir = Split-Path -Path (Join-Path $repoRoot $file) -Parent
    $projectDir = $null
    while ($dir -and $dir.Length -ge $repoRoot.Length) {
      if (@(Get-ChildItem -Path $dir -Filter '*.csproj' -File -ErrorAction SilentlyContinue).Count -gt 0) { $projectDir = $dir; break }
      $dir = Split-Path -Path $dir -Parent
    }
    $projectRel = $null
    if ($projectDir) { $projectRel = ConvertTo-RepoPath $projectDir $repoRoot }
    $projectMeasured = $false
    if ($projectRel) {
      $prefix = $projectRel.TrimEnd('/') + '/'
      foreach ($cf in $coveredFiles) { if ($cf.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { $projectMeasured = $true; break } }
    }
    if ($projectMeasured) {
      Write-Host "Ignoré (aucune ligne couvrable, projet mesuré) : $file"
      continue
    }
    $content = @(Get-Content -Path (Join-Path $repoRoot $file) -Encoding UTF8)
    foreach ($n in $lineNumbers) {
      if ($n -ge 1 -and $n -le $content.Count -and $content[$n - 1].Trim()) {
        $diffTotal++
        $uncovered.Add("${file}:$n (projet non mesuré)")
      }
    }
  }

  if ($diffTotal -eq 0) {
    Write-Summary "- Code modifié (contre $BaseRef) : aucune ligne couvrable modifiée, règle satisfaite."
  } else {
    $diffPct = [math]::Round(100.0 * $diffCovered / $diffTotal, 2)
    Write-Summary "- Code modifié (contre $BaseRef) : **$diffPct %** ($diffCovered / $diffTotal lignes couvrables), seuil $DiffThreshold %"
    if ($uncovered.Count -gt 0) {
      Write-Summary ''
      Write-Summary '<details><summary>Lignes modifiées non couvertes</summary>'
      Write-Summary ''
      foreach ($u in $uncovered) { Write-Summary "- $u" }
      Write-Summary ''
      Write-Summary '</details>'
    }
    if ($diffPct -lt $DiffThreshold) {
      Write-Host "::error::Couverture du code modifié $diffPct % < $DiffThreshold % : ajouter les tests manquants (lignes listées ci-dessus)."
      $failed = $true
    }
  }
}

if ($failed) { exit 1 }
Write-Host 'Couverture : OK.'
exit 0
