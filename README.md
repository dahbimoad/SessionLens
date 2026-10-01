# Session Recorder

A small Windows app. Press **Record**, and Google Chrome opens. Use any website normally,
then press **Stop and save**, or just close Chrome. One zip lands in your Downloads folder
with everything that happened:

- every click, form fill, key and page load, with stable selectors
- the full page HTML before and after each action, including typed values and shadow DOM
- a screenshot after each action
- every network request and response of every tab and iframe, bodies included (HAR)
- console messages and page errors

Hand the zip to Claude (or a developer) as the complete context of a workflow. Nobody has
to drive the browser live to understand it. Every zip carries its own `README.md` that
explains its files.

## Requirements

- Windows 10 or 11, x64
- Google Chrome installed

## Install

Run `SessionRecorder-Setup-<version>.exe`. There is no admin prompt, because it installs
for the current user only.

## First use of a site

The recording Chrome uses its **own profile**, not your everyday one. Chrome 136+ blocks
automation of the everyday profile. So the first time you record a site that needs a
login, log in inside the recording window. It remembers the login for later recordings.

While recording, Chrome shows "Chrome is being controlled by automated test software".
That is expected.

## Build

```powershell
.\scripts\publish.ps1            # self-contained build into publish\
.\scripts\build-installer.ps1    # publish + Inno Setup installer into dist\
```

Needs the .NET 10 SDK and Inno Setup 6.

## Where things live

| What | Where |
|---|---|
| Recordings | Downloads, `session-recording_<date>_<time>.zip` |
| Recording browser profile (logins) | `%LocalAppData%\SessionRecorder\ChromeProfile` |
| Log | `%LocalAppData%\SessionRecorder\session-recorder.log` |

Uninstalling removes the app, the log and the recording profile. Saved recordings are kept.

## Known limits

- Secrets are removed by name: password and secret-named fields, cookies, authorization and token headers,
  secret-named fields in form/JSON bodies and query strings. A token that sits inside an HTML page or a
  script, or inside a multipart/XML/plain-text body, is NOT removed. Treat every zip as private.
- Closed shadow roots cannot be read.
- Response bodies over 20 MB are left out (the HAR entry says so).
- Some sign-in pages (Google accounts, for example) refuse automated browsers.
