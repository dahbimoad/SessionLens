# Session recording

This zip is one browsing session recorded by SessionLens (a Windows app that drives
Google Chrome through Playwright). It holds everything the user did and everything the
pages did in response.

## Files

| File | What it holds |
|---|---|
| session.json | Start/stop time, Chrome version, counts, and **warnings**. Read the warnings first: they list anything that could not be captured. |
| steps.json | Every user action, in order. This is the main timeline. |
| dom/step-NNNN-before.html | The full page HTML at the moment of the action, before the page reacted. |
| dom/step-NNNN-after.html | The full page HTML about 1 second after the action. |
| dom/step-NNNN-changes.json | Every DOM change between this step and the next one in the same frame: nodes added/removed (with their HTML), attribute and text changes, each with a time. Short-lived messages (toasts, confirmation panels) show up here even when the "after" snapshot missed them. |
| screenshots/step-NNNN.jpg | What the tab looked like at the "after" moment. |
| network.har | Every HTTP request and response of every tab and iframe, bodies included (HAR 1.2). |
| console.json | Console messages and uncaught page errors. |

## steps.json

Each step has these fields:

- **index**: the step number. It matches NNNN in the file names.
- **tab**: which tab it happened in. Tabs are numbered 1, 2, 3... in the order they appeared.
- **kind**: one of the values below.
  - click
  - change: a form field got a new value
  - edit: a rich-text area lost focus
  - key: Enter, Tab or Escape
  - submit
  - page-load: a page finished loading, in the tab itself or in any frame inside it (inIframe = true)
- **target**: the element that was acted on. It gives:
  - a CSS **selector**, built from stable attributes where possible
  - an **xpath**
  - tag, id, name, type and role
  - the visible **label** and **text**
  - whether it sits in a shadow DOM
- **value**: only on change/edit steps, the new value.
  - Selects give value and label.
  - Checkboxes give true/false.
  - File inputs give the file names.
- **key**: only on key steps.
- **url**, **title**, **frameUrl**, **inIframe**, **at** (ISO time).
- **files**: the paths of this step's snapshots, change log and screenshot.
- **changeCount** / **droppedChanges**: how many DOM changes followed the step, and how many were left out past the caps (2000 node/text changes and 1000 attribute changes per step). Attribute changes on SVG elements (chart animations) and changes to elements not yet in the page are not logged.
- **note**: present when the "after" snapshot is missing, because the page navigated first.

The HTML snapshots keep what the user typed. Open shadow roots are written as
`<template shadowrootmode="open">` inside their host element. An iframe's content is not
inside its parent's snapshot. It has its own steps, with inIframe = true.

## Linking the files

- A request in network.har has **_afterStep = N**. That means it started after step N and before step N+1.
- Step 0 means before the first action.
- So the requests a click caused are the ones carrying that click's index.
- Console entries carry the same **afterStep** field.
- **_tab** / **tab** tell tabs apart when several were used.

## Secrets are removed

- Password fields, and fields whose name says they hold a secret (password, secret, token, api key, credential, session id, otp, sysparm_ck), show ******** in steps and snapshots.
- In network.har:
  - These header values are replaced by [removed]: Cookie, Set-Cookie, Authorization, Proxy-Authorization, and any header whose name says it holds a secret (for example X-UserToken).
  - Secret-named fields in form and JSON bodies (both requests and responses) show ********.
  - Secret-named URL query parameters show ********. This covers request URLs, Referer and Location headers, page URLs in steps.json, and href/src/action addresses in the snapshots.
  - An entry where something was removed carries **_redacted: true**.
- Names are always kept. So a login is still visible as a login, but it cannot be replayed with the recorded session.

## Not in the recording

- Secrets that are not under a secret-looking name are NOT removed. For example, a token written inside an HTML page or a script.
- Bodies that are neither form nor JSON (multipart uploads, XML, plain text) are not scanned.
- Closed shadow roots (mode "closed") cannot be read, so they are not in the snapshots.
- Changes inside shadow roots are not in the change logs (they are in the snapshots).
- Added HTML in a change log is cut at 4000 characters per node.
- Service-worker requests have no tab number.
- Response bodies larger than 20 MB are left out. The entry says so in **_bodyError**.
