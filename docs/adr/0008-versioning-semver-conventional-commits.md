# 0008 — Versioning SemVer automatique via Conventional Commits

## Statut
Acceptée.

## Contexte
Le repo n'avait aucun versioning : pas de tag git, pas de `<Version>`, pas de release notes. La PR #2 a posé un premier jalon partiel (`<Version>0.1.0</Version>` dans `Directory.Build.props`, tag Docker figé sur cette valeur), mais sans logique de bump, de tag git ni de génération de notes — une version statique qui ne bouge jamais toute seule.

Le besoin : un flux reproductible où créer une branche, ouvrir une PR titrée selon le type de changement (SemVer via [Conventional Commits](https://www.conventionalcommits.org/fr/v1.0.0/)), et la merger sur `main`, suffit à faire calculer automatiquement la nouvelle version, créer le tag git et publier une release GitHub avec ses notes — sans étape manuelle.

## Décision
- **Un seul point de vérité de version** : `<Version>` dans `Directory.Build.props`, partagé par les 3 projets `src/` (Worker/Infrastructure/Core ne sont jamais versionnés indépendamment — ce sont un seul déployable). Aucun `.csproj` individuel ne porte de `<Version>`.
- **[Versionize](https://github.com/versionize/versionize)** (dotnet tool, exécuté dans le job `release` de `cd.yml`) calcule le bump depuis les commits Conventional Commits accumulés depuis le dernier tag, met à jour `Directory.Build.props`, committe et tag (`v{version}`), puis le commit+tag est poussé directement sur `main`. `--exit-insignificant-commits` fait que le job ne produit rien (et les jobs `build`/`docker`/`github-release` sont sautés) quand aucun commit `fix:`/`feat:`/`BREAKING CHANGE` n'a été mergé depuis la dernière release.
- **`--skip-changelog`** : pas de `CHANGELOG.md` committé — les notes de version vivent uniquement dans les GitHub Releases, générées nativement par `gh release create --generate-notes` à partir des PR mergées depuis le tag précédent.
- **Fiabilité du message de commit sur `main`** : le repo est configuré en squash-merge exclusif (`allow_merge_commit`/`allow_rebase_merge` désactivés), avec `squash_merge_commit_title=PR_TITLE` et `squash_merge_commit_message=PR_BODY` — le commit qui atterrit sur `main` est donc toujours le titre de la PR (+ corps, qui peut porter un footer `BREAKING CHANGE:`), jamais l'historique brut de commits WIP de la branche.
- **Validation du titre de PR** : l'app GitHub [Semantic Pull Requests](https://github.com/apps/semantic-pull-requests) (déjà installée sur le compte), configurée via `.github/semantic.yml` (`titleOnly: true`, types restreints à ceux que Versionize sait interpréter). Une règle de branch protection sur `main` rend son check obligatoire avant de pouvoir merger une PR.
- **Portée de la version** : tag git + release GitHub + tag Docker semver (`docker/metadata-action`, `type=semver,pattern={{version}}`) + `AssemblyVersion`/`FileVersion`/`InformationalVersion` des binaires — ce dernier point est obtenu sans aucune configuration supplémentaire, puisque `Directory.Build.props` est committé avant tout `dotnet build`/`publish` du job `docker` et de son `Dockerfile`.

## Alternatives écartées
- **semantic-release** : aurait rempli le même rôle (calcul de version + tag + release), mais ajoute une dépendance Node à un repo 100% .NET pour un bénéfice nul par rapport à Versionize.
- **GitVersion** : outil .NET natif, mais orienté stratégies de branches (GitFlow) plutôt que trunk-based squash-merge, et ne génère ni release GitHub ni notes — aurait fallu le combiner avec un step `gh release create` de toute façon, pour un calcul de version moins direct que Versionize sur ce cas d'usage simple (un seul `<Version>` partagé, pas de monorepo).
- **`amannn/action-semantic-pull-request`** : action GitHub redondante avec l'app Semantic Pull Requests déjà installée sur le compte — inutile de dupliquer le même contrôle.

## Conséquences
- L'historique actuel (avant cette PR) n'est pas en Conventional Commits ; le premier passage du job `release` après ce changement ne trouve donc rien de significatif et ne produit qu'un tag `v0.1.0` de référence sans commit ni release (comportement de bootstrap de Versionize en l'absence de tag préexistant). La première vraie release arrive au premier `feat:`/`fix:` mergé après coup.
- Toute PR dont le titre ne respecte pas Conventional Commits est bloquée au merge (check requis) ; toute PR de type `docs`/`chore`/`ci`/etc. est mergeable mais ne déclenche aucune release.
- Le workflow `cd.yml` pousse un commit directement sur `main` (le commit de release Versionize) avec le `GITHUB_TOKEN` par défaut — nécessite que la branch protection sur `main` n'exige pas de pull request pour les pushes directs (seul le required status check sur les *merges* de PR est actif).
