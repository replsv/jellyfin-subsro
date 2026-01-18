#!/usr/bin/env python3
import json
import sys

def update_manifest(version, checksum, timestamp, changelog, tag_name):
    """Update manifest.json with new release information"""
    
    with open('manifest.json', 'r') as f:
        manifest = json.load(f)
    
    # Create new version entry
    new_version = {
        "version": version,
        "changelog": changelog,
        "targetAbi": "10.11.5.0",
        "sourceUrl": f"https://github.com/replsv/jellyfin-subsro/releases/download/{tag_name}/jellyfin-subsro-{version}.zip",
        "checksum": checksum,
        "timestamp": timestamp
    }
    
    # Check if version already exists
    versions = manifest[0]["versions"]
    existing_index = None
    for i, v in enumerate(versions):
        if v["version"] == version:
            existing_index = i
            break
    
    if existing_index is not None:
        # Update existing version
        versions[existing_index] = new_version
    else:
        # Add new version at the beginning
        versions.insert(0, new_version)
    
    manifest[0]["versions"] = versions
    
    with open('manifest.json', 'w') as f:
        json.dump(manifest, f, indent=4)
        f.write('\n')
    
    print(f"Updated manifest.json with version {version}")

if __name__ == "__main__":
    if len(sys.argv) != 6:
        print("Usage: update_manifest.py <version> <checksum> <timestamp> <changelog> <tag_name>")
        sys.exit(1)
    
    version = sys.argv[1]
    checksum = sys.argv[2]
    timestamp = sys.argv[3]
    changelog = sys.argv[4]
    tag_name = sys.argv[5]
    
    update_manifest(version, checksum, timestamp, changelog, tag_name)
