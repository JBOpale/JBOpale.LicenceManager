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
