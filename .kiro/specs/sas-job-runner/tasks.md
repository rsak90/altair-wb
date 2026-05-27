# Implementation Plan: SAS Job Runner

## Overview

Build an ASP.NET Core MVC .NET 10 web application that proxies SAS job execution through the Altair SLC Hub. The implementation is organized into three feature-based releases:

- **Release 1 — Login & Job Submission**: Authentication flow end-to-end, Configure Output screen with Monaco editor, Run button posts a job and receives a Job ID.
- **Release 2 — Job Output & Log Viewer**: Status polling loop, program log fetching, Log Tab display with auto-scroll, final log fetch on terminal status.
- **Release 3 — Cancel & Session Management**: Cancel button, logout, session expiry redirect.

No unit or property-based tests are included in this MVP scope.

---

## Tasks

## Release 1 — Login & Job Submission

> Goal: A user can log in with username/password and submit a SAS program. The app receives a Job ID and the Run button is disabled while the job is active.

- [x] 1. Scaffold the ASP.NET Core MVC project and configure dependencies
  - [x] 1.1 Create the ASP.NET Core MVC .NET 10 project with the `SasJobRunner` solution and project structure
    - Run `dotnet new mvc -n SasJobRunner --framework net10.0` and verify the project builds
    - Create the directory layout: `Controllers/`, `Controllers/Api/`, `Models/`, `Services/`, `Views/Account/`, `Views/Home/`, `wwwroot/js/`, `wwwroot/css/`
    - _Requirements: 7.1_

  - [x] 1.2 Add required NuGet packages
    - Add `DevExtreme.AspNet.Core` (or `DevExtreme.AspNet.Data`) for DevExtreme tag helpers
    - Add `Microsoft.AspNetCore.Session` (included in ASP.NET Core; confirm `AddSession` is available)
    - Add `Microsoft.AspNetCore.Antiforgery` (built-in; confirm `AddAntiforgery` is available)
    - _Requirements: 2.2, 7.6_

  - [x] 1.3 Configure `appsettings.json` with required settings
    - Add `"SlcHub": { "BaseUrl": "https://placeholder.slchub.example.com" }` section
    - Add `"Session": { "TimeoutMinutes": 60 }` section
    - _Requirements: 1.3, 7.2_

- [x] 2. Configure `Program.cs` — service registration, middleware, and routing
  - [x] 2.1 Register services and configure the HTTP pipeline
    - `builder.Services.AddControllersWithViews()` with anti-forgery
    - `builder.Services.AddHttpClient<SlcHubClient>()` (typed client)
    - `builder.Services.AddDistributedMemoryCache()` and `builder.Services.AddSession(opts => { opts.IdleTimeout = TimeSpan.FromMinutes(config["Session:TimeoutMinutes"]); opts.Cookie.HttpOnly = true; opts.Cookie.IsEssential = true; })`
    - `builder.Services.AddAntiforgery(opts => opts.HeaderName = "RequestVerificationToken")`
    - Set `builder.WebHost.ConfigureKestrel(opts => opts.Limits.MaxRequestBodySize = 1_048_576)` (1 MB global limit)
    - In the middleware pipeline: `app.UseSession()` before `app.UseRouting()` / `app.MapControllerRoute(...)`
    - Add default MVC route: `"{controller=Home}/{action=Index}/{id?}"`
    - _Requirements: 1.3, 7.6, 7.7_

- [x] 3. Define C# data models needed for Release 1
  - [x] 3.1 Create `Models/LoginViewModel.cs`
    - Implement `LoginViewModel` with `[Required] string Username`, `[Required] string Password`, and `string? ErrorMessage` properties
    - _Requirements: 1.1_

  - [x] 3.2 Create `Models/ApiErrorResponse.cs`
    - Implement `ApiErrorResponse` with `int StatusCode` and `string Message` properties
    - _Requirements: 7.3, 7.4, 7.5_

  - [x] 3.3 Create `Models/JobSubmitRequest.cs` and `Models/JobSubmitResponse.cs`
    - `JobSubmitRequest`: `[Required] string SasCode`
    - `JobSubmitResponse`: `string JobId`
    - _Requirements: 3.1, 3.3, 3.4_

- [x] 4. Implement `SlcHubClient` — login and job submission methods
  - [x] 4.1 Create `Services/SlcHubClient.cs` with constructor and base URL injection
    - Accept `HttpClient _http` via constructor (registered as typed client)
    - Read `SlcHub:BaseUrl` from `IConfiguration` and set `_http.BaseAddress`
    - _Requirements: 7.2_

  - [x] 4.2 Implement `LoginAsync(string username, string password, CancellationToken ct)`
    - POST `{ username, password }` JSON to `/auth/login`
    - On success: deserialize `{ "token": "..." }` and return the token string
    - On 4xx: throw a typed exception carrying the Hub error body
    - On timeout/network failure: throw a typed connectivity exception
    - Use a 30-second `CancellationTokenSource` linked to `ct`
    - _Requirements: 1.2, 1.4, 1.5_

  - [x] 4.3 Implement `SubmitJobAsync(string bearerToken, string sasCode, CancellationToken ct)`
    - POST `{ sasCode }` JSON to `/jobs` with `Authorization: Bearer <token>` set on the `HttpRequestMessage` (not `DefaultRequestHeaders`)
    - On success: deserialize `{ "jobId": "..." }` and return the job ID string
    - On error: throw a typed exception carrying the Hub error body
    - Use a 30-second `CancellationTokenSource` linked to `ct`
    - _Requirements: 3.3, 7.2_

- [x] 5. Implement `AuthApiController` — POST /api/auth/login
  - [x] 5.1 Create `Controllers/Api/AuthApiController.cs`
    - Decorate with `[ApiController]` and `[Route("api/auth")]`
    - Inject `SlcHubClient`
    - _Requirements: 7.1_

  - [x] 5.2 Implement `POST /api/auth/login` action
    - Accept `{ username, password }` JSON body
    - Call `SlcHubClient.LoginAsync`; on success store the token in `HttpContext.Session` under key `"BearerToken"` and return `200 { success: true }`
    - On Hub 4xx: return `400 ApiErrorResponse` with the Hub error message
    - On timeout/network failure: return `503 ApiErrorResponse` with a generic connectivity message
    - _Requirements: 1.2, 1.3, 1.4, 1.5_

- [x] 6. Implement `JobApiController` — submit endpoint only
  - [x] 6.1 Create `Controllers/Api/JobApiController.cs` with session auth helper
    - Decorate with `[ApiController]` and `[Route("api/jobs")]`
    - Inject `SlcHubClient`
    - Add a private helper `GetBearerToken()` that reads `Session["BearerToken"]`; if null/empty, returns `null` (callers return `401 ApiErrorResponse`)
    - _Requirements: 7.1, 7.5_

  - [x] 6.2 Implement `POST /api/jobs/submit` action
    - Decorate with `[ValidateAntiForgeryToken]`
    - Check bearer token via helper; return `401` if absent
    - Check `Session["ActiveJobStatus"]`; if `Submitted` or `Running`, return `409 ApiErrorResponse`
    - Check `Request.ContentLength`; if > 1,048,576 bytes, return `413 ApiErrorResponse`
    - Call `SlcHubClient.SubmitJobAsync`; on success store `jobId` in `Session["ActiveJobId"]` and `"Submitted"` in `Session["ActiveJobStatus"]`; return `200 JobSubmitResponse`
    - On Hub error: return the Hub status code with `ApiErrorResponse`
    - On timeout: return `503 ApiErrorResponse`
    - _Requirements: 3.3, 3.6, 7.5, 7.6, 7.7_

- [x] 7. Implement MVC controllers for Login and Configure Output
  - [x] 7.1 Create `Controllers/AccountController.cs`
    - `GET /account/login`: render `Views/Account/Login.cshtml` with a fresh `LoginViewModel`
    - `POST /account/login`: call `SlcHubClient.LoginAsync` directly; on success store token in Session and redirect to `/`; on failure re-render login view with `ErrorMessage` populated
    - _Requirements: 1.1, 1.3, 1.4, 1.5_

  - [x] 7.2 Create `Controllers/HomeController.cs`
    - `GET /`: check `Session["BearerToken"]`; if null/empty redirect to `/account/login`; otherwise render `Views/Home/Index.cshtml`
    - _Requirements: 1.6, 2.1_

- [ ] 8. Build the Login Razor view
  - [-] 8.1 Create `Views/Account/Login.cshtml`
    - Render a DevExtreme form (`dx-form`) with `username` and `password` text/password items and a Login submit button
    - Display `Model.ErrorMessage` in a visible error area when non-null/non-empty
    - Include a `<script src="~/js/login.js">` reference
    - _Requirements: 1.1, 1.4, 1.5_

- [ ] 9. Build the Configure Output Razor view (shell)
  - [-] 9.1 Create `Views/Home/Index.cshtml`
    - Add `<meta name="RequestVerificationToken" content="@antiforgery.GetAndStoreTokens(Context).RequestToken" />` in the `<head>` (inject `IAntiforgery`)
    - Render a DevExtreme toolbar row with Run button (`id="btnRun"`, enabled) and Cancel button (`id="btnCancel"`, disabled) using `dx-button` tag helpers
    - Render a `<div id="monacoContainer">` occupying at least 50% of the viewport height via CSS
    - Render a DevExtreme tab panel (`dx-tab-panel`) below the editor with a single "Log" tab containing a `<pre id="logContent">` element (empty on load)
    - Include CDN references for Monaco Editor (`vs/loader.js`) and a `<script src="~/js/configure-output.js">` reference
    - _Requirements: 2.1, 2.2, 2.3, 2.5, 2.6, 2.7, 2.8, 2.9, 2.10_

- [ ] 10. Implement `wwwroot/js/login.js`
  - [-] 10.1 Write the DevExtreme form submit handler
    - On Login button click, read username and password values from the DevExtreme form instance
    - `fetch('POST /api/auth/login', { body: JSON.stringify({ username, password }) })`
    - On `200`: redirect `window.location.href = '/'`
    - On error: parse `ApiErrorResponse.message` and display it in the error area
    - _Requirements: 1.2, 1.4, 1.5_

- [ ] 11. Implement `wwwroot/js/configure-output.js` — Monaco init, button state, Run handler
  - [-] 11.1 Initialize Monaco Editor with SAS syntax highlighting
    - Use `require.config({ paths: { vs: '<cdn>/vs' } })` and `require(['vs/editor/editor.main'], ...)`
    - Register a Monarch tokenizer for language `"sas"` that classifies `DATA`, `PROC`, `RUN`, `END`, and other SAS keywords as `keyword` token type; non-keyword identifiers must NOT be classified as `keyword`
    - Create the editor instance in `#monacoContainer` with `language: 'sas'`
    - _Requirements: 2.4, 2.9_

  - [-] 11.2 Implement the button state machine
    - Define `setState(state)` where `state` is `'idle' | 'running' | 'cancelling'`
    - `idle`: Run enabled, Cancel disabled
    - `running`: Run disabled, Cancel enabled
    - `cancelling`: Run disabled, Cancel disabled
    - Call `setState('idle')` on page load
    - _Requirements: 2.6, 2.7, 3.5, 3.7, 4.5, 5.3_

  - [ ] 11.3 Implement the Run button click handler
    - Read editor content; if empty or whitespace-only, display a validation message in `#logContent` and return without fetching
    - Read anti-forgery token from `<meta name="RequestVerificationToken">`
    - `fetch('POST /api/jobs/submit', ...)` with `RequestVerificationToken` header
    - On success: store `jobId` in `state.jobId`, clear `#logContent`, call `setState('running')`
    - On error: display `ApiErrorResponse.message` in `#logContent`; handle `409` (duplicate job) and `413` (too large) with specific messages
    - _Requirements: 3.1, 3.2, 3.4, 3.5, 3.7, 3.8, 3.9_

- [~] 12. Release 1 checkpoint — login and job submission smoke test
  - Run `dotnet build` and confirm zero errors
  - Start the app with `dotnet run` and verify:
    - Navigating to `/` redirects to `/account/login`
    - Login page renders with DevExtreme form
    - Successful login redirects to Configure Output screen
    - Monaco editor loads with SAS syntax highlighting
    - Clicking Run with empty editor shows validation message in Log Tab
    - Clicking Run with SAS code posts to `/api/jobs/submit` and Run button becomes disabled on success

---

## Release 2 — Job Output & Log Viewer

> Goal: After a job is submitted, the app polls for status and displays the live program log in the Log Tab. Polling stops automatically when the job reaches a terminal state.

- [ ] 13. Add remaining C# data models
  - [~] 13.1 Create `Models/JobStatusResponse.cs` and `Models/JobLogResponse.cs`
    - `JobStatusResponse`: `string Status` (values: `Submitted`, `Running`, `Completed`, `Failed`, `Cancelled`)
    - `JobLogResponse`: `string Log`
    - _Requirements: 5.2, 6.2_

- [ ] 14. Implement `SlcHubClient` — status and log methods
  - [~] 14.1 Implement `GetJobStatusAsync(string bearerToken, string jobId, CancellationToken ct)`
    - GET `/jobs/{jobId}/status` with `Authorization: Bearer <token>` on the `HttpRequestMessage`
    - Return the raw `status` string
    - Use a 10-second `CancellationTokenSource` linked to `ct`
    - _Requirements: 5.2_

  - [~] 14.2 Implement `GetProgramLogAsync(string bearerToken, string jobId, CancellationToken ct)`
    - GET `/jobs/{jobId}/log` with `Authorization: Bearer <token>` on the `HttpRequestMessage`
    - Return the raw `log` string
    - Use a 10-second `CancellationTokenSource` linked to `ct`
    - _Requirements: 6.2_

- [ ] 15. Implement `JobApiController` — status and log endpoints
  - [~] 15.1 Implement `GET /api/jobs/{jobId}/status` action
    - Check bearer token; return `401` if absent
    - Call `SlcHubClient.GetJobStatusAsync`; on success update `Session["ActiveJobStatus"]` and return `200 JobStatusResponse`
    - On Hub error: return the Hub status code with `ApiErrorResponse`
    - On timeout: return `504 ApiErrorResponse`
    - _Requirements: 5.2, 7.5_

  - [~] 15.2 Implement `GET /api/jobs/{jobId}/log` action
    - Check bearer token; return `401` if absent
    - Call `SlcHubClient.GetProgramLogAsync`; on success return `200 JobLogResponse`
    - On Hub error: return the Hub status code with `ApiErrorResponse`
    - On timeout: return `504 ApiErrorResponse`
    - _Requirements: 6.2, 7.5_

- [ ] 16. Implement polling loop and log display in `configure-output.js`
  - [~] 16.1 Implement `startPolling` / `stopPolling` and the poll cycle
    - `startPolling()`: call `setInterval(pollCycle, 5000)` and store handle in `state.pollingInterval`; invoke immediately on first call
    - `stopPolling()`: call `clearInterval(state.pollingInterval)`; set `state.pollingInterval = null`
    - `pollCycle()`: concurrently fetch status (`GET /api/jobs/{jobId}/status`) and log (`GET /api/jobs/{jobId}/log`)
    - On status `Completed` or `Failed`: call `stopPolling()`, perform one final log fetch, update `#logContent`, call `setState('idle')`
    - On status `Cancelled`: call `stopPolling()`, call `setState('idle')`
    - On status poll error or `504`: call `stopPolling()`, display error in `#logContent`, call `setState('idle')`
    - _Requirements: 5.1, 5.3, 5.4, 5.5, 6.1, 6.4_

  - [~] 16.2 Implement log display and auto-scroll in the poll cycle
    - On log fetch success: replace `#logContent` text entirely with the returned log string; auto-scroll the `#logContent` container to the bottom
    - On log fetch failure: display an error message in `#logContent` while preserving the previous log in `state.lastLog`; do NOT stop polling
    - On next successful log fetch after a failure: replace the error message with the retrieved log content
    - _Requirements: 6.3, 6.5, 6.6, 6.7_

  - [~] 16.3 Wire polling start into the Run button handler
    - After a successful job submission (Job ID received), call `startPolling()` from the Run handler
    - _Requirements: 3.4_

- [~] 17. Release 2 checkpoint — polling and log viewer smoke test
  - Run `dotnet build` and confirm zero errors
  - Start the app and verify end-to-end:
    - Submit a SAS job → Log Tab clears immediately
    - Log Tab updates every 5 seconds with program log content
    - Log Tab auto-scrolls to the bottom on each update
    - When job reaches `Completed` or `Failed`: polling stops, Run button re-enables, Cancel button disables
    - On status poll failure: error shown in Log Tab, buttons reset to idle state

---

## Release 3 — Cancel & Session Management

> Goal: Users can cancel a running job. Session expiry and logout are handled gracefully.

- [ ] 18. Implement `SlcHubClient` — cancel method
  - [~] 18.1 Implement `CancelJobAsync(string bearerToken, string jobId, CancellationToken ct)`
    - DELETE `/jobs/{jobId}` with `Authorization: Bearer <token>` on the `HttpRequestMessage`
    - Accept `204 No Content` or `200 OK` as success
    - On error: throw a typed exception carrying the Hub error body
    - Use a 30-second `CancellationTokenSource` linked to `ct`
    - _Requirements: 4.2_

- [ ] 19. Implement `JobApiController` — cancel endpoint
  - [~] 19.1 Implement `DELETE /api/jobs/{jobId}/cancel` action
    - Decorate with `[ValidateAntiForgeryToken]`
    - Check bearer token; return `401` if absent
    - Call `SlcHubClient.CancelJobAsync`; on success clear `Session["ActiveJobId"]` and `Session["ActiveJobStatus"]`; return `200 OK`
    - On Hub error: return the Hub status code with `ApiErrorResponse`
    - On timeout: return `503 ApiErrorResponse`
    - _Requirements: 4.2, 7.5, 7.6_

- [ ] 20. Implement Cancel button handler in `configure-output.js`
  - [~] 20.1 Implement the Cancel button click handler
    - Read anti-forgery token from `<meta name="RequestVerificationToken">`
    - Call `setState('cancelling')`
    - `fetch('DELETE /api/jobs/{jobId}/cancel', ...)` with `RequestVerificationToken` header
    - On success: call `stopPolling()`, call `setState('idle')`, update `#logContent` with a cancellation message
    - On error: display error in `#logContent`; call `setState('running')` to re-enable Cancel and continue polling
    - On timeout (`504`): display timeout message in `#logContent`; call `setState('running')` to continue polling
    - _Requirements: 4.1, 4.3, 4.4, 4.6_

- [ ] 21. Implement session expiry and logout
  - [~] 21.1 Update `AccountController` to handle session expiry
    - `GET /account/login`: if `?expired=true` query param is present, set `ErrorMessage` to a session-expired message on the `LoginViewModel`
    - _Requirements: 1.8_

  - [~] 21.2 Update `HomeController` and `JobApiController` for session expiry redirect
    - `HomeController.Index`: if `Session["BearerToken"]` is absent, redirect to `/account/login?expired=true`
    - All `JobApiController` endpoints: already return `401` when token is absent (from Release 1); confirm the client-side handler in `configure-output.js` redirects to `/account/login?expired=true` on receiving `401`
    - _Requirements: 1.6, 1.8_

  - [~] 21.3 Add Logout button to Configure Output view and implement handler
    - Add a Logout `dx-button` (`id="btnLogout"`) to the toolbar in `Views/Home/Index.cshtml`
    - Add `POST /account/logout` action to `AccountController`: call `HttpContext.Session.Clear()` and redirect to `/account/login`
    - In `configure-output.js`, implement the Logout button click handler: POST to `/account/logout` (with anti-forgery token), then `window.location.href = '/account/login'`
    - _Requirements: 1.9, 2.1_

- [~] 22. Release 3 checkpoint — cancel and session management smoke test
  - Run `dotnet build` and confirm zero errors
  - Start the app and verify:
    - Cancel button is enabled only while a job is `Submitted` or `Running`
    - Clicking Cancel sends DELETE request; on success polling stops and Log Tab shows cancellation message
    - On cancel error: Log Tab shows error, polling continues
    - Logout button clears session and redirects to login
    - Accessing `/` after session expires redirects to login with session-expired message
    - Any `/api/*` call with expired session returns `401` and client redirects to login

---

## Notes

- Task 1 is fully complete (project scaffolded, packages added, appsettings configured)
- Each release is independently deployable and testable
- The `SlcHubClient` sets `Authorization` headers per-`HttpRequestMessage`, never on `DefaultRequestHeaders`, to prevent race conditions under concurrent requests
- Anti-forgery token header name is `RequestVerificationToken` — must match between `Program.cs` (`AddAntiforgery`) and `configure-output.js`
- Monaco Editor is loaded from CDN; ensure the CDN URL is reachable in the target environment
- `Session["ActiveJobId"]` and `Session["ActiveJobStatus"]` are set in Release 1 (submit) and cleared in Release 3 (cancel/logout)

## Task Dependency Graph

```json
{
  "waves": [
    {
      "id": 0,
      "label": "Release 1 — Foundation",
      "tasks": ["1.1", "1.2", "1.3"]
    },
    {
      "id": 1,
      "label": "Release 1 — Program.cs & Models",
      "tasks": ["2.1", "3.1", "3.2", "3.3"]
    },
    {
      "id": 2,
      "label": "Release 1 — SlcHubClient (login + submit)",
      "tasks": ["4.1", "4.2", "4.3"]
    },
    {
      "id": 3,
      "label": "Release 1 — API Controllers",
      "tasks": ["5.1", "5.2", "6.1", "6.2"]
    },
    {
      "id": 4,
      "label": "Release 1 — MVC Controllers",
      "tasks": ["7.1", "7.2"]
    },
    {
      "id": 5,
      "label": "Release 1 — Views & JS",
      "tasks": ["8.1", "9.1", "10.1", "11.1", "11.2", "11.3"]
    },
    {
      "id": 6,
      "label": "Release 1 — Checkpoint",
      "tasks": ["12"]
    },
    {
      "id": 7,
      "label": "Release 2 — Models & SlcHubClient (status + log)",
      "tasks": ["13.1", "14.1", "14.2"]
    },
    {
      "id": 8,
      "label": "Release 2 — API Endpoints",
      "tasks": ["15.1", "15.2"]
    },
    {
      "id": 9,
      "label": "Release 2 — Polling & Log Display",
      "tasks": ["16.1", "16.2", "16.3"]
    },
    {
      "id": 10,
      "label": "Release 2 — Checkpoint",
      "tasks": ["17"]
    },
    {
      "id": 11,
      "label": "Release 3 — Cancel",
      "tasks": ["18.1", "19.1", "20.1"]
    },
    {
      "id": 12,
      "label": "Release 3 — Session Management",
      "tasks": ["21.1", "21.2", "21.3"]
    },
    {
      "id": 13,
      "label": "Release 3 — Checkpoint",
      "tasks": ["22"]
    }
  ]
}
```
