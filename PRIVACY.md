# Privacy Notice

Last updated: April 26, 2026

## 1. Scope

This Privacy Notice describes how Vidvix, a local-first Windows desktop
application, handles information when you install, launch, and use the
software.

Vidvix is designed primarily for on-device media processing. Under the current
implementation, media conversion, preview, trimming, merging, AI enhancement,
AI interpolation, source separation, and terminal-based media commands are
performed locally on the user's device.

## 2. Short Summary

- Vidvix does not require an account to use the desktop application.
- Vidvix does not include built-in advertising, cloud syncing, or analytics
  telemetry in the current codebase.
- Vidvix does not intentionally upload your media files to a Vidvix-operated
  server for routine processing.
- Vidvix stores certain settings, logs, caches, temporary working files, and
  extracted runtime assets locally on your device.
- Vidvix may make limited outbound network requests to obtain FFmpeg checksum
  metadata and download a fallback FFmpeg runtime from GitHub if a required
  local runtime is missing.

## 3. Information Vidvix May Process

### 3.1 Media files you choose

Vidvix may access media files, subtitles, output folders, and other content
that you explicitly select, import, preview, trim, merge, enhance, separate,
or process through the application.

### 3.2 Application settings

Vidvix stores user preference data locally. Depending on the features you use,
this may include items such as:

- preferred workspace and processing mode
- preferred output formats and output directories
- theme and language preferences
- GPU or acceleration preferences
- merge, trim, preview, and export behavior preferences
- main window placement preferences

### 3.3 Logs and diagnostic data

Vidvix writes local log data for operational and diagnostic purposes. Local
logs may contain timestamps, status messages, exception messages, runtime
paths, and, depending on the operation, file paths or command-related context.

### 3.4 Generated caches and temporary data

Vidvix may create local caches or temporary artifacts, including but not
limited to:

- media thumbnails
- waveform preview images
- extracted or prepared runtime assets
- AI probe files and model-preparation caches
- temporary concat lists, temporary media intermediates, and other working
  files required for processing

### 3.5 Terminal workspace content

If you use the Terminal workspace, the commands you compose and the outputs
returned by the underlying local tools may be displayed in the application and
may also be reflected in local logs or temporary processing context.

## 4. How Vidvix Uses Information

Vidvix uses local data only to operate the desktop application, including to:

- remember your settings and preferences
- resolve output paths and workspace defaults
- inspect media files and show metadata
- generate previews, waveforms, and thumbnails
- run local processing engines such as FFmpeg, mpv/libmpv, Demucs, RIFE, and
  Real-ESRGAN
- diagnose failures and improve local recoverability
- prepare or extract runtime assets required for local processing

## 5. Local Storage Locations

Under the current implementation, Vidvix may store data under locations such
as the following:

- `%LOCALAPPDATA%\Vidvix\user-preferences.json`
- `%LOCALAPPDATA%\Vidvix\Logs\latest.log`
- `%LOCALAPPDATA%\Vidvix\...` for extracted runtimes, caches, thumbnails,
  waveform data, AI preparation data, or similar operational files
- user-selected output directories
- application runtime directories when the installation location is writable

The exact local files created may vary by feature usage, release build, and
runtime availability.

## 6. Network Activity

Vidvix is intended to be offline-oriented, but the current codebase includes a
limited fallback network flow for FFmpeg runtime resolution.

If the required FFmpeg runtime is not available locally, Vidvix may:

- request checksum metadata from the FFmpeg build source configured in the
  application
- download a fallback FFmpeg archive from GitHub-hosted release assets

Under the current implementation, these requests are directed to GitHub-hosted
URLs associated with the configured FFmpeg build source. When this happens,
GitHub and its infrastructure providers may receive standard request metadata,
such as your IP address, request headers, and a user agent string identifying
Vidvix.

No other routine outbound network flow was identified in the current
application code reviewed for this notice. However, third-party components,
operating system services, or future versions of the application may behave
differently, and you should review release notes and source changes if you need
strict network controls.

## 7. Third-Party Runtime Components

Vidvix relies on third-party local runtimes and libraries for media playback,
inspection, processing, and AI features. These components may read the files
you instruct Vidvix to process because that is necessary for local operation.

Examples include:

- FFmpeg / FFprobe / FFplay
- mpv / libmpv
- Demucs runtimes and model assets
- RIFE NCNN Vulkan
- Real-ESRGAN and Real-ESRGAN NCNN Vulkan

Vidvix itself does not claim ownership of these third-party components, and
their use remains subject to their own license and documentation terms.

## 8. Sharing and Disclosure

Vidvix does not sell your personal data.

Vidvix does not include a built-in cloud account system or a Vidvix-operated
media upload service in the current implementation.

Information may still be disclosed to third parties in limited situations that
are outside the normal local-first processing flow, such as:

- when you choose to open external links, email links, or websites
- when a fallback FFmpeg download is triggered from GitHub-hosted resources
- when your operating system, security software, or network infrastructure logs
  application behavior independently of Vidvix

## 9. Data Retention

Local settings, logs, caches, extracted runtimes, and generated outputs remain
on your device until they are deleted by you, overwritten by the application,
or removed during uninstall, cleanup, or upgrade activity.

Because Vidvix is a desktop application with local-first behavior, retention is
primarily controlled by the user and the local environment.

## 10. Security

Vidvix is designed to reduce privacy exposure by performing core media work on
device. However, no software can guarantee absolute security.

You are responsible for:

- securing the device on which Vidvix runs
- protecting any sensitive media files you choose to process
- reviewing the third-party runtimes you bundle or distribute
- restricting outbound network access if your environment requires strict
  offline operation

## 11. Your Choices

You can generally limit or control data handling by:

- choosing which files or folders to import
- choosing where outputs are written
- clearing local logs, caches, and extracted runtimes
- disabling or restricting network connectivity if you do not want fallback
  downloads to occur
- reviewing the source code and third-party runtime contents before deployment

## 12. Changes to This Notice

This Privacy Notice may be updated as the application changes. If you publish
new releases that alter network behavior, local storage behavior, telemetry, or
third-party integrations, this notice should be updated accordingly.

## 13. Contact

Repository: https://github.com/LPHYSQS/Vidvix

Project contact and bug reports: 3261296352@qq.com
