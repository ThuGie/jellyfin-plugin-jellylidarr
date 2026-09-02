# JellyLidarr

JellyLidarr is a single Jellyfin plugin for discovering music, requesting artists or albums through Lidarr, approving requests, and following them until they appear in Jellyfin.

## Requirements

- Jellyfin Server 10.11.11
- An existing Lidarr instance with its indexers and download client configured
- .NET 9 SDK only when building from source; it is not required to install the release archive

## Build

Run `./build.ps1`. The tested plugin archive is created at `artifacts/jellylidarr_1.0.0.zip`.

## Install

### Plugin catalog (recommended)

1. In Jellyfin, open Dashboard → Plugins → Repositories.
2. Add `https://raw.githubusercontent.com/ThuGie/jellyfin-plugin-jellylidarr/main/manifest.json`.
3. Open Catalog, install JellyLidarr, and restart Jellyfin when prompted.
4. Open Dashboard → Plugins → JellyLidarr, enter the Lidarr URL and API key, load the Lidarr options, choose the profiles and root folder, and assign user roles.
5. Restart Jellyfin once more after initial configuration so the selected reconciliation interval is applied.

For a manual installation, extract the release archive into a folder named `JellyLidarr` under Jellyfin's plugin directory and restart Jellyfin.

Common plugin directories:

- Docker: `/config/plugins/JellyLidarr` inside the container; place the extracted files in the host directory mounted as `/config`.
- Linux packages: `/var/lib/jellyfin/plugins/JellyLidarr`.
- Windows tray install: `%ProgramData%\Jellyfin\Server\plugins\JellyLidarr`.
- Windows direct install: `%LocalAppData%\jellyfin\plugins\JellyLidarr`.

### Synology Package Center installation

The catalog method above also works with the native SynoCommunity Jellyfin package and requires no SSH access. For a manual DSM 7 installation, stop Jellyfin in Package Center and extract the archive into:

`/var/packages/jellyfin/var/data/plugins/JellyLidarr`

Then make sure the extracted folder is readable by the Jellyfin package account and start Jellyfin again in Package Center. The path is Synology's stable package-data link; its underlying volume is normally `/volumeN/@appdata/jellyfin/data/plugins/JellyLidarr`. Do not install the files under `/var/packages/jellyfin/target`, because package upgrades can replace that directory.

The plugin adds **Music Requests** to Jellyfin Web. Native television and mobile clients do not render server-plugin pages; open the same Jellyfin server in a browser to request music.

## Upgrade and rollback

Stop Jellyfin, back up the JellyLidarr plugin folder and `jellylidarr/requests.db` from Jellyfin's data directory, replace the plugin files, and restart. To roll back, stop Jellyfin and restore both backups. Configuration is retained in Jellyfin's plugin configuration directory.

## Security

The Lidarr key is stored only in Jellyfin's server-side plugin configuration. Search and request endpoints require a Jellyfin session. New non-admin users receive Viewer access, and administrators explicitly assign Requester, Trusted requester, or Approver access.

JellyLidarr only asks Lidarr to acquire requested items. It does not configure indexers, download clients, or sources; server owners are responsible for using lawful sources.

## License

JellyLidarr is available under the MIT License. It is an independent community project and is not affiliated with Jellyfin or Lidarr.
