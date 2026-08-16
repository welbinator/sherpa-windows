# Sherpa Mac → Windows reverse-engineering map

Source: compiled `Sherpa.app` (bundle `app.shepherd.macos`) strings + Swift metadata.
Not Jack’s original source — reconstructed surface area for clone fidelity.

## Shell
- SidebarSection: Sites | Hosts | Settings
- SitesListView + SiteDetailView (tabs)
- Empty: "Create your first site" / "Start blank or pick a Marketplace starter kit."
- Sheets: NewSiteWizard, ImportSiteSheet, ComposerInstallSheet, CommandsSheet, CreateUserSheet, UpdateLogSheet, AddonBrowserSheet, ConnectHostSheet
- Wizards: DeployWizard, CloudDeployWizard, StaticPublishWizard

## SiteDetail tabs (from symbols)
- overviewTab, packagesTab, deployTab (+ forgeDeployTab, cloudDeployTab, staticDeployTab)
- Git is first-class (Save changes / Sync / Pull / Push)
- Commands sheet (please/artisan catalog)
- Create User (`please make:user`)
- Remove from Sherpa vs Delete site files (Trash)

## NewSiteWizard steps (symbols + strings)
1. **startingPointStep** — Blank site; Marketplace starter kits; "Fresh Statamic, no starter kit"
2. **identityStep** — Site name, Folder, URL preview, Will create
3. **optionsStep** — Park in Herd and open as `{name}.test`; Secure with HTTPS (Herd)
4. storage / license panels for paid kits

## AppServices members (binary)
store, runner, runtime, herd, git, composer, github, forge, netlify, cloudflarePages, laravelCloud, packagist, statamicAPI, appearance, installs, packageUpdates, staticPublishes, preferences, lastAdvice, busyMessage

## Settings
- Default sites folder
- Prefer Herd for new sites
- Secure new Herd sites with HTTPS
- GitHub token / Packagist auth → Keychain (Windows: DPAPI secret store)
- Composer update/rollback
- Appearance preference

## Git tab copy (exact energy)
- Save changes commits the checked files; nothing is sent to GitHub
- Sync: commit selection → pull --rebase --autostash → push
- Back up to GitHub (private repo); token stays in Keychain
- Git needs name and email before it can save changes

## Hosts
- Dynamic: Laravel Forge, Laravel Cloud
- Static: Cloudflare Pages, Netlify
- Connect a New Host; tokens never written into sites
