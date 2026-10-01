# Architecture

## Shape

One WPF window (`Views/RecorderWindow`) drives one `RecordingSession` at a time.

```
RecorderWindow ── Record ──> RecordingSession.StartAsync
                                  │ Playwright, Channel "chrome", persistent profile
                                  ├─ recorder.js (init script, every frame) ──> binding ──> StepLog
                                  ├─ context Request/RequestFinished/RequestFailed ──> NetworkRecorder
                                  ├─ context Console/WebError ──> ConsoleLog
                                  └─ context Close ──> FinishAsync ──> RecordingArchive (zip in Downloads)
```

## Decisions and their proof

| Decision | Why | Proof |
|---|---|---|
| Playwright over a Chrome extension | It records cross-site iframes and new tabs from their first request. | Headless test, 2026-09-30: requests, bodies and console of a cross-site iframe (127.0.0.1 page, localhost iframe) all reached context events. |
| Own HAR instead of `RecordHarPath` | Playwright writes its HAR only on an API close. | Headless test, 2026-09-30: after `Browser.close` sent over CDP (what a user closing the window does), the Close event fired and the HAR file did not exist. |
| Separate Chrome profile | Chrome 136+ refuses remote debugging on the default profile. | Chrome release notes; not re-tested here. |
| Close event is the only save path | Stop and "user closed Chrome" must both save, exactly once. | `FinishAsync` is guarded by `Interlocked.Exchange`. |
| Hand-written DOM serialiser in recorder.js | `outerHTML` drops open shadow roots and typed values. | End-to-end run below. |

**End-to-end run, 2026-09-30.** This was the real recording code, headless, in a scratch harness. It filled a form, including a password field and a select, submitted it, clicked a shadow-DOM button, and typed into a cross-site iframe. Then it closed Chrome over CDP. The zip was written, with these results:

- 9 steps with the right selectors.
- The POST body and its response in the HAR, with `_afterStep` pointing at the submit.
- The iframe's console line.
- The shadow root written as `<template shadowrootmode="open">`.
- The select state and the typed name kept.
- The password masked everywhere outside the HAR.
- The screenshots were valid JPEGs.
- 0 warnings.

## Threading

Playwright raises events on thread-pool threads. Each log (`StepLog`, `NetworkRecorder`,
`ConsoleLog`, `WarningLog`, `TabRegistry`) guards its state with its own `Lock`. Async
follow-ups (reading bodies, screenshots) are registered in `TrackedWork`. Stop and
FinishAsync drain it, with a 15 s cap, before the archive is written. Failures and
leftovers become warnings in `session.json`.

## Privacy

`AppLog` never receives page content, URLs, headers or bodies. Those go into the zip only.
