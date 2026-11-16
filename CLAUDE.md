# CLAUDE.md

Ce fichier contient des règles générales pour ce dépôt, à respecter par toute session Claude (Claude Code, y compris les workflows GitHub Actions générés ou modifiés).

## Règle : exécution des GitHub Actions selon l'OS

Par défaut, pour tout workflow GitHub Actions ajouté ou modifié dans ce dépôt :

- **Jobs Windows** : doivent s'exécuter sur le runner self-hosted local `SURFACE-BOOK-3`.
  ```yaml
  runs-on: [self-hosted, Windows, SURFACE-BOOK-3]
  ```

- **Jobs macOS** : doivent être déclenchés **uniquement manuellement** (`workflow_dispatch`), jamais automatiquement sur `push`, `pull_request`, ou un autre événement, afin de limiter la consommation de minutes GitHub Actions (macOS coûte ~10x plus cher en minutes facturées que Linux).
  ```yaml
  on:
    workflow_dispatch:
  jobs:
    build-macos:
      runs-on: macos-latest
  ```
  Si un même workflow contient aussi des jobs Linux/Windows déclenchés automatiquement, séparer le job macOS avec une condition `if: github.event_name == 'workflow_dispatch'` pour qu'il ne se déclenche jamais via les autres triggers du workflow.

Cette règle s'applique à tout nouveau workflow créé dans `.github/workflows/`, ainsi qu'à toute modification d'un workflow existant.

## Règle : tests validés avant fusion d'une pull request

- **Aucune pull request n'est fusionnée** dans `dev` ou `main` tant que le check **« LicenceManager - Tests PR »** (workflow `.github/workflows/pr-tests.yml`) n'est pas vert.
- `pr-tests.yml` ne doit **jamais** recevoir de filtre `paths` (ni de condition qui ferait sauter son job) : un check requis absent bloque la PR, et un job sauté compte comme réussi.
- Toute session lance localement les tests concernés par ses changements et les voit passer **avant** d'ouvrir ou de mettre à jour une PR.
- Ne jamais désactiver, ignorer (`Skip`, `[Fact(Skip=…)]`…) ni supprimer un test pour faire passer une PR : corriger le code ou signaler le problème.
- Plus de push direct sur les branches protégées (`dev`, `main`) : toute modification passe par une pull request.
- Tout nouveau projet ou nouvelle suite de tests doit être ajouté à `pr-tests.yml` (une étape `dotnet test` par projet).
- Le ruleset correspondant est versionné dans `.github/rulesets/tests-obligatoires.json` (import via Settings → Rules → Rulesets → Import a ruleset).

## Règle : aucun paquet vulnérable avant de passer sur main

- Aucune modification n'arrive sur `main` ou `dev` avec un paquet présentant une vulnérabilité **haute ou critique**, directe ou transitive.
- Le contrôle est l'étape « Paquets vulnérables (haute / critique) » du job requis « LicenceManager - Tests PR » (`.github/workflows/pr-tests.yml`), via `.github/scripts/check-vulnerable-packages.ps1` (`dotnet list package --vulnerable --include-transitive`) : il fait partie du check requis, pas d'un job séparé. Les vulnérabilités faibles/modérées sont signalées en avertissement.
- Toute session lance ce contrôle localement (`pwsh .github/scripts/check-vulnerable-packages.ps1 LicenceManager.sln LicenceManagerCpp/tools/vectorgen/vectorgen.csproj` après restauration) avant d'ouvrir ou de mettre à jour une PR.
- Correction : mise à jour de la dépendance, ou épinglage d'une version corrigée pour une dépendance transitive, avec un commentaire citant l'avis GHSA ; puis relancer build et tests.
- Ne jamais retirer, ignorer ou contourner ce contrôle, ni baisser son seuil.
- Tout nouveau projet doit être couvert par le contrôle (inclus dans `LicenceManager.sln` ou ajouté à la liste de l'étape).

## Règle : suivi des actions en cours

- Jérôme suit les actions en attente de tous ses projets dans le tableau « Suivi des actions » (https://claude.ai/artifact/41ayfFGBTCRjpUJH4pgf1P).
- Toute session utilise le skill `suivi-actions` pour tenir ce tableau à jour à la fin de chaque chantier et chaque fois qu'elle rend la main avec une action qui dépend de lui (décision, validation, étape manuelle, secret à créer, PR à fusionner, ruleset à importer, réponse externe).
- Lire l'existant avant d'ajouter (pas de doublon) ; passer à `doing`/`done` ce qui avance.
- Jamais de secret dans le tableau ; ne jamais supprimer une action.

## Règle : couverture de code

- Chaque pull request doit couvrir par des tests **au moins 90 % des lignes C# qu'elle ajoute ou modifie** (hors projets de tests, `Migrations/`, `obj/`), et la **couverture globale des lignes ne doit jamais baisser** sous le seuil de `.github/seuil-couverture.json`.
- Le contrôle est l'étape « Couverture (90 % du code modifié, global au moins au seuil) » du job requis « LicenceManager - Tests PR » : coverlet (`coverage.runsettings`, format Cobertura) sur toutes les suites .NET, puis `.github/scripts/Test-Coverage.ps1`. Le validateur C++ n'est pas mesuré (pas d'outil fiable pour MSVC sur le runner) : la règle porte sur le code .NET.
- Le seuil global ne se relève qu'à la hausse (valeur entière = mesure arrondie à l'inférieur), quand le script l'indique par un `::notice::`. Ne jamais l'abaisser.
- Ne jamais exclure du code de la mesure (attribut, filtre, runsettings), abaisser un seuil ni ajouter de `Skip` pour faire passer une PR.
- Toute session lance la mesure et le script avant d'ouvrir ou de mettre à jour une PR, et ajoute les tests manquants :
  ```
  dotnet test <projet de tests> --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/<projet>
  pwsh .github/scripts/Test-Coverage.ps1 -ReportsDir TestResults -BaseRef origin/dev
  ```
- Tout nouveau projet de tests est lancé avec la collecte de couverture dans `pr-tests.yml`.

## Règle : tout développement part de `dev`, jamais de `main`

- Le dépôt LicenceManager a deux branches permanentes : `dev` (intégration) et `main` (production). `dev` est la branche par défaut du dépôt.
- Toute branche de travail (`feature/…`, `fix/…`, `claude/…`) est créée depuis `dev` à jour (`git fetch origin dev && git checkout -b <branche> origin/dev`), jamais depuis `main`, et sa PR cible `dev`.
- `main` ne reçoit que des PR `dev` → `main`, sur validation explicite de Jérôme. Aucun commit ni aucune branche de travail ne part de `main` (seule exception : un correctif urgent de production, sur demande explicite, ensuite reporté dans `dev`).
- Si une session démarre sur `main` ou sur une branche qui n'est pas basée sur `dev`, elle se rebase/recrée sur `origin/dev` avant le premier commit.
