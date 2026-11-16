<#
.SYNOPSIS
  Contrôle des paquets NuGet vulnérables (directs et transitifs) : échoue sur toute vulnérabilité
  HAUTE ou CRITIQUE, signale les vulnérabilités faibles/modérées en avertissement.

.DESCRIPTION
  Lancé par l'étape « Paquets vulnérables (haute / critique) » du job requis
  « LicenceManager - Tests PR » (.github/workflows/pr-tests.yml). Tous les projets du dépôt sont de
  style SDK (y compris les cibles net48), donc `dotnet list package --vulnerable` fonctionne pour
  chacun : pas besoin de NuGetAudit/packages.config. Les projets doivent être restaurés avant.

  Ne jamais baisser le seuil ni retirer un projet de la liste (voir CLAUDE.md).

.EXAMPLE
  pwsh .github/scripts/check-vulnerable-packages.ps1 LicenceManager.sln LicenceManagerCpp/tools/vectorgen/vectorgen.csproj
#>
param(
  [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
  [string[]] $Targets
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_NOLOGO = '1'
$blocking = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$checked = 0

foreach ($target in $Targets) {
  Write-Host "== $target"
  $raw = & dotnet list $target package --vulnerable --include-transitive --format json 2>&1 | Out-String
  if ($LASTEXITCODE -ne 0) {
    Write-Host $raw
    throw "dotnet list package --vulnerable a échoué pour $target (exit $LASTEXITCODE) : le contrôle ne peut pas conclure."
  }
  $report = $raw | ConvertFrom-Json

  foreach ($problem in @($report.problems)) {
    if ($null -eq $problem) { continue }
    # Un projet non restauré ou une source injoignable rendrait le contrôle aveugle : on échoue.
    if ($problem.level -eq 'error') { throw "$target : $($problem.text)" }
    Write-Host "::warning::$target : $($problem.text)"
  }

  foreach ($project in @($report.projects)) {
    $checked++
    foreach ($framework in @($project.frameworks)) {
      if ($null -eq $framework) { continue }
      $packages = @($framework.topLevelPackages) + @($framework.transitivePackages)
      foreach ($package in $packages) {
        if ($null -eq $package) { continue }
        $kind = if (@($framework.topLevelPackages) -contains $package) { 'direct' } else { 'transitif' }
        foreach ($vuln in @($package.vulnerabilities)) {
          if ($null -eq $vuln) { continue }
          $line = "$($project.path) [$($framework.framework)] $($package.id) $($package.resolvedVersion) ($kind) : $($vuln.severity) - $($vuln.advisoryurl)"
          if ($vuln.severity -in @('High', 'Critical')) { $blocking.Add($line) } else { $warnings.Add($line) }
        }
      }
    }
  }
}

foreach ($w in $warnings) { Write-Host "::warning::Paquet vulnérable (faible/modérée) : $w" }
foreach ($b in $blocking) { Write-Host "::error::Paquet vulnérable (haute/critique) : $b" }

if ($checked -eq 0) { throw "Aucun projet analysé : le contrôle ne peut pas conclure." }
if ($blocking.Count -gt 0) {
  throw "$($blocking.Count) vulnérabilité(s) haute/critique : mettre à jour la dépendance ou épingler une version corrigée (commentaire citant l'avis GHSA)."
}
Write-Host "OK : $checked projet(s) analysé(s), aucune vulnérabilité haute/critique ($($warnings.Count) avertissement(s))."
