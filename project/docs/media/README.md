# Media capture contract

Only real output from the release candidate may be presented as product UI.
Capture in a dedicated Windows profile with a neutral wallpaper and no personal
notifications, device names, network identifiers, paths, or unrelated process
names.

Required scenes are the normal transparent overlay, click-through interaction,
tray Settings, optional metrics, and full-screen enter/exit. Store PNG source
captures and a 15–30 second MP4. Derivatives must remove EXIF/location metadata.
The social preview must be 1280×640 and remain legible after GitHub cropping.

## README preview exception

Redacted screenshots from a real evaluation build may appear in the README
before the final hardware gate when they are explicitly labeled as UI examples,
not acceptance evidence or production-release media. They must not show personal
notifications, device names, network identifiers, paths, unrelated process
names, or claims of metric accuracy.

`readme-default-metrics-view.png` and `readme-optional-metrics-view.png` are such evaluation UI
examples. Manual visual review found only application metrics and partial
application or Windows chrome; PNG chunk inspection found no text or location
metadata. The README states that displayed values are illustrative and that
unavailable sensors show `N/A`.

Final release-candidate media is still not checked in because the final
candidate has not passed the hardware gate.
`release/github-settings-change-set.json` deliberately remains blocked until
real release capture, redaction review, dimensions, and candidate lineage are
recorded.
