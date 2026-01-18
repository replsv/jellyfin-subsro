# Jellyfin Subs.ro Plugin

A Jellyfin plugin for downloading subtitles from [Subs.ro](https://subs.ro).

## Features

- Search subtitles by IMDb ID, TMDb ID, or title
- Support for both movies and TV series
- Automatic extraction from ZIP and RAR archives
- Support for multiple subtitle formats (SRT, SUB, SSA, ASS, VTT)
- Support for multiple languages (Romanian, English, Italian, French, German, Hungarian, Greek, Portuguese, Spanish)

## Requirements

- Jellyfin 10.11.5 or later
- .NET 9.0 SDK (for building)
- a valid API key


## Installation

### Method 1: From Plugin Repository (SOON)

1. Open Jellyfin dashboard
2. Navigate to **Plugins** → **Catalog**
3. Add to the list this repository:
```
https://raw.githubusercontent.com/replsv/jellyfin-subsro/master/manifest.json (for jellyfin 10.11)
```

### Method 2: Manual Installation

1. Download the latest release DLLs from the releases page
2. Create a folder named `Subs.ro` in your Jellyfin plugins directory:
   - Linux: `/var/lib/jellyfin/plugins/Subs.ro/`
   - Windows: `%AppData%\Jellyfin\Server\plugins\Subs.ro\`
   - macOS: `~/.local/share/jellyfin/plugins/Subs.ro/`
3. Copy the following DLL files into this folder:
   - `JellyfinSubsPlugin.dll` (main plugin)
   - `SharpCompress.dll` (for extracting ZIP/RAR archives)
   - `ZstdSharp.dll` (dependency of SharpCompress)
4. Restart Jellyfin

**Note:** The plugin folder must be named exactly `Subs.ro` to match the plugin name. All three DLL files should be placed directly in this folder.

Also, you can decide to build them your own. Details below.

## Configuration

1. Open Jellyfin dashboard
2. Navigate to **Plugins** → **Subs.ro**
3. Enter your Subs.ro API key
4. Click **Save**

## Usage

### Automatic Subtitle Download

1. Open a movie or episode in Jellyfin
2. Click the **...** menu button
3. Select **Download Subtitles**
4. Choose your preferred language
5. Select a subtitle from the Subs.ro provider
6. Click **Download**

### Library Scan

Jellyfin can automatically search for subtitles during library scans:

1. Navigate to **Dashboard** → **Libraries**
2. Select a library and click **Manage Library**
3. Go to the **Subtitles** tab
4. Enable **Download subtitles from the internet**
5. Select **Subs.ro** as a subtitle downloader
6. Choose your preferred languages
7. Save changes

## Building from Source

### Prerequisites

- .NET 9.0 SDK or later
- Git

### Build Steps

```bash
# Build the plugin
dotnet build --configuration Release

# The output will be in JellyfinSubsPlugin/bin/Release/net9.0/
```

### Manual Deployment After Building

After building, deploy the plugin to your Jellyfin server:

```bash
# 1. Create the plugin directory (if it doesn't exist)
sudo mkdir -p /var/lib/jellyfin/plugins/Subs.ro

# 2. Copy all required DLLs to the plugin directory
sudo cp JellyfinSubsPlugin/bin/Release/net9.0/JellyfinSubsPlugin.dll /var/lib/jellyfin/plugins/Subs.ro/
sudo cp JellyfinSubsPlugin/bin/Release/net9.0/SharpCompress.dll /var/lib/jellyfin/plugins/Subs.ro/
sudo cp JellyfinSubsPlugin/bin/Release/net9.0/ZstdSharp.dll /var/lib/jellyfin/plugins/Subs.ro/

# 3. Set appropriate permissions
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/Subs.ro

# 4. Restart Jellyfin
sudo systemctl restart jellyfin
```

**Important Notes:**
- The plugin directory must be named exactly `Subs.ro` (matching the plugin Name property)
- You must copy all three DLL files:
  - `JellyfinSubsPlugin.dll` - Main plugin
  - `SharpCompress.dll` - Required for extracting ZIP/RAR archives
  - `ZstdSharp.dll` - Dependency of SharpCompress
- Do not copy `Jellyfin.*.dll` or `MediaBrowser.*.dll` files as these are already in Jellyfin
- Restart Jellyfin after deploying the plugin

## Supported Languages

The plugin supports the following languages from Subs.ro:

- Romanian (ro)
- English (en)
- Italian (ita)
- French (fra)
- German (ger)
- Hungarian (ung)
- Greek (gre)
- Portuguese (por)
- Spanish (spa)

## Archive Support

Subs.ro provides subtitles in ZIP or RAR archives. The plugin automatically:

1. Downloads the archive
2. Detects the archive type (ZIP or RAR)
3. Intelligently selects the best matching subtitle file
4. Returns it to Jellyfin

### How Subtitle Matching Works

The plugin uses intelligent matching to select the best subtitle file from archives that contain multiple files:

#### For TV Series Episodes

When searching for series episodes, the plugin:

1. **Season Matching**: Matches season numbers from subtitle titles (e.g., "Sezonul 1" matches S01)
   - Only returns subtitles for the correct season
   - Romanian format: "Sezonul X" is automatically matched to season X

2. **Episode Matching**: For archives containing multiple episode files
   - First tries exact pattern matching (E02, e02, .02., etc.)
   - Extracts episode numbers from filenames using regex
   - Uses Levenshtein distance algorithm for fuzzy matching as fallback
   - Example: For `Show.S01E02.mkv`, it will find and extract the E02 file from an archive containing E01-E09

**Example workflow:**
```
Request: Pluribus.S01E02.HDRip.mkv
API returns: "Pluribus - Sezonul 1" (archive with episodes 1-9)
Plugin extracts: Episode 2 subtitle file from the archive
```

#### For Movies

When searching for movies, the plugin:

1. **Type Flexibility**: Accepts both movie and series results from Subs.ro
   - Some content may be classified as "series" even when searching for movies
   - Multi-season archives are supported (e.g., "Sezonele 1-2" containing all episodes)
   - Series archives are labeled with `[Series Archive]` prefix for easy identification

2. **Filename Matching**: Compares archive filenames with the current media filename
   - Uses Levenshtein distance to find the closest match
   - Prioritizes files with similar release names

3. **Release Format Priority**: If no close filename match is found, prioritizes by release format
   - Detects common formats: HDRip, DVDRip, BRRip, BluRay, WEB-DL, CAM, TS, SCREENER, etc.
   - Matches the format from your media filename (if detected)
   - Falls back to the first available subtitle if no format match

**Example workflow:**
```
Request: Movie.2024.HDRip.x264.mkv
Archive contains:
  - Movie.2024.HDRip.srt (✓ exact format match)
  - Movie.2024.DVDRip.srt
  - Movie.2024.CAM.srt
Plugin selects: Movie.2024.HDRip.srt
```

**Multi-season archive example:**
```
Request: Landman (movie search)
API returns: "Landman - Sezonele 1-2" (series archive with all episodes)
Plugin shows: [Series Archive] NOU! Episodul 6 din S02...
User can download and extract the appropriate subtitle from the archive
```

### Supported Archive Formats

- **ZIP** (.zip) - Fully supported
- **RAR** (.rar) - Fully supported via SharpCompress library

### Supported Subtitle Formats

The plugin can extract and use the following subtitle formats:
- SRT (.srt)
- SUB (.sub)

## Troubleshooting

### Configuration page error (Failed to get resource)

If you see an error like "Failed to get resource Jellyfin.Plugin.SubsRo.Configuration.configPage.html":

- Ensure the plugin folder is named exactly `Subs.ro` (not `SubsRo` or `Subs.ro Plugin`)
- Verify you copied the correct DLL file (`JellyfinSubsPlugin.dll`)
- Check that the file permissions are correct (owned by the jellyfin user on Linux)
- Try rebuilding from source if you made local modifications
- Restart Jellyfin after deployment

### No subtitles found

- Verify your API key is correct
- Check that the media has IMDb ID or TMDb ID metadata
- Try searching manually with the media title
- Check Jellyfin logs for error messages

## License

This project is licensed under the GPLv3 License - see the LICENSE file for details.

## Credits

- Developed for Jellyfin
- Based on the Jellyfin plugin template
- Inspired by the OpenSubtitles plugin
