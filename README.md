# GuideCapture prototype

A local Windows application for engineering leads creating screenshot-based technical procedures. Requires Windows with .NET Framework 4.8. No server, account, API key, or installation is required. Run `GuideCapture.exe` from this folder. The executable is unsigned.

## First workflow

1. Enter a guide title. **Autocrop** is selected by default and captures a 900 by 600 pixel area around each click. Adjust Width and Height if needed, or choose **Full monitor**. Click **Start recording**. The app minimizes.
2. Perform the procedure in your browser or desktop application. Each left or right click creates a screenshot with a red click marker and an editable draft instruction.
3. Press **Ctrl+Shift+F10** to stop and return to the editor. You can also restore the app from the taskbar or right-click its notification-area icon and choose **Stop and review**.
4. Select each step. Edit its instruction above the screenshot. Add follow-up directions in **Instructions after screenshot** below the preview; line breaks are preserved in Word. Move steps up or down, or delete them.
5. To hide sensitive details, enable **Redact screenshot** and drag over the screenshot. This paints a permanent black rectangle into the image. Review before saving; there is no undo in this prototype.
6. Click **Save project** to keep an editable `.guide` file. Click **Export Word** to create the `.docx` for your SharePoint document library. Upload the Word file through your usual SharePoint workflow.

Open `Example.guide` to try the editor without recording. It contains fictional demonstration screens.

## Recording controls

| Shortcut | Action |
| --- | --- |
| Ctrl+Shift+F8 | Pause or resume an active recording |
| Ctrl+Shift+F9 | Add a screenshot at the mouse pointer while recording |
| Ctrl+Shift+F10 | Stop and review |

Shortcuts can conflict with other apps. The taskbar window and notification-area menu remain available. Starting again appends steps to the current guide. Use **New** for a separate procedure.

## Capture behavior and limits

- Autocrop captures only a local rectangle around the clicked point, immediately after the click. The default 900 by 600 pixels gives space for nearby labels, fields, and buttons. Near monitor edges, the rectangle shifts inward; on smaller monitors it shrinks to fit. This is a fixed-size contextual crop, not automatic control or dialog detection. Width and Height can be adjusted before recording and apply to new captures, including the manual capture shortcut. Settings last for the current app session. Choose Full monitor when a procedure needs more context. Other windows visible inside the capture area appear in the image. A fast-closing menu may disappear before capture.
- Draft labels use Windows accessibility information when available, falling back to a highlighted-location instruction and window title. Labels are best-effort, not AI-generated procedure writing.
- Records left and right clicks; clicks within 350 milliseconds are combined. Dragging, scrolling, and typing do not create steps automatically. Use the manual capture shortcut and edit the instruction for those actions.
- App controls are excluded from click capture. Recordings only run after Start. No keyboard text is logged. Visible sensitive content may still appear in screenshots and window titles; use pause and review/redaction as appropriate.
- Screenshots remain in memory until saved or exported. Projects and Word documents remain local. Nothing uploads automatically. Projects are ZIP packages containing XML and PNG files; they are not encrypted.
- Save project before closing. Exporting Word does not save the editable project. Redacting an image does not modify earlier exports or project copies.
- Designed for ordinary desktop windows. UAC secure desktop, protected video, elevated apps, and remote-session control names may not be captured correctly.
- Large monitors use more memory. The prototype limits guides to 500 steps; short procedures are the intended first test.
- Word exports flow continuously without forced page breaks between steps. Screenshots preserve their proportions and fit within 4.8 inches wide by 2.4 inches high. Each step contains its heading, initial instruction, screenshot, then any follow-up instructions. Actual page breaks depend on content length. Text remains editable in Word.

## Build and validation

Source and `build.ps1` are included. Rebuild using Windows PowerShell from this directory: `powershell -File .\build.ps1`.

The application includes a synthetic smoke test: `GuideCapture.exe --self-test C:\path\to\test-folder`. It generates a demonstration document, round-trips a project, and renders the editor to an image. Use a disposable test folder.

Verified during development: centered and edge crops, negative monitor coordinates, crops larger than a monitor, full monitor mode, compilation, synthetic project save/reopen, instruction edits, reordering, deletion, pixel redaction, embedded Word images and document XML, and editor layout. Live click capture and global shortcuts still need an interactive Windows pilot. The bundled Word renderer was unavailable, so final Word pagination has not been visually verified. No production deployment or SharePoint integration is included.



## Updated project format
The new version opens older version 1 projects. Saving uses version 2 to preserve follow-up instructions. Open these updated projects with this new app; the older app cannot read version 2. Existing Word documents must be re-exported to use the compact layout.

