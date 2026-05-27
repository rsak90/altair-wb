# Requirements Document

## Introduction

The SAS Job Runner is an ASP.NET Core MVC .NET 10 web application that allows users to write, submit, and monitor SAS programs against an Altair SLC Hub. The application provides a Monaco code editor for authoring SAS code, authentication via the Altair SLC Hub API, job execution controls (Run and Cancel), and a live log viewer that polls job status and program output during execution. The UI is built with DevExtreme components throughout.

## Glossary

- **SAS_Job_Runner**: The ASP.NET Core MVC .NET 10 web application described in this document.
- **Altair_SLC_Hub**: The external backend service that authenticates users, accepts SAS job submissions, cancels jobs, and provides job status and log output via its API.
- **Bearer_Token**: The authentication token returned by the Altair_SLC_Hub login API, used to authorize all subsequent API calls.
- **Job**: A SAS program submitted to the Altair_SLC_Hub for execution.
- **Job_ID**: A unique identifier returned by the Altair_SLC_Hub when a Job is submitted, used to poll status and retrieve logs.
- **Job_Status**: The execution state of a Job as reported by the Altair_SLC_Hub. Valid values are: `Submitted` (accepted but not yet executing), `Running` (actively executing), `Completed` (finished successfully), `Failed` (finished with an error), and `Cancelled` (stopped by user request).
- **Program_Log**: The textual execution log produced by the Altair_SLC_Hub for a running or completed Job.
- **Configure_Output_Screen**: The main screen of the SAS_Job_Runner containing the Monaco editor, Run/Cancel controls, and the Log tab.
- **Log_Tab**: The panel below the Monaco editor that displays the Program_Log for the active Job.
- **Monaco_Editor**: The embedded code editor component used for authoring SAS programs.
- **API_Controller**: An ASP.NET Core controller located under the `/api` route prefix within the same project, acting as a proxy/facade to the Altair_SLC_Hub.
- **Session**: The server-side storage that holds the Bearer_Token for the duration of a user's authenticated session.

---

## Requirements

### Requirement 1: User Authentication

**User Story:** As a user, I want to log in with my username and password, so that I can obtain a Bearer Token and access the SAS Job Runner features.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL display a login screen with a username field, a password field, and a Login button before granting access to the Configure_Output_Screen.
2. WHEN the user submits the login form, THE SAS_Job_Runner SHALL send the credentials to the Altair_SLC_Hub authentication API.
3. WHEN the Altair_SLC_Hub returns a valid Bearer_Token (a non-null, non-empty string), THE SAS_Job_Runner SHALL store the Bearer_Token in the server-side Session and redirect the user to the Configure_Output_Screen.
4. IF the Altair_SLC_Hub returns an authentication error (HTTP 4xx response), THEN THE SAS_Job_Runner SHALL display the error message from the response body on the login screen and prevent access to the Configure_Output_Screen.
5. IF the Altair_SLC_Hub is unreachable (no response within 30 seconds or a network-level failure), THEN THE SAS_Job_Runner SHALL display a generic connectivity error message on the login screen and prevent access to the Configure_Output_Screen.
6. WHILE the user is not authenticated (no valid Bearer_Token in Session), THE SAS_Job_Runner SHALL redirect any request to the Configure_Output_Screen to the login screen and ensure the user lands on the login screen.
7. THE SAS_Job_Runner SHALL include the Bearer_Token as a `Bearer` Authorization header in all subsequent API requests to the Altair_SLC_Hub.
8. IF the Session no longer contains a valid Bearer_Token during an active session, THEN THE SAS_Job_Runner SHALL redirect the user to the login screen and display a session-expired message.
9. WHEN the user clicks the Logout button on the Configure_Output_Screen, THE SAS_Job_Runner SHALL remove the Bearer_Token from the Session and redirect the user to the login screen.

---

### Requirement 2: Configure Output Screen Layout

**User Story:** As a user, I want a single-screen interface with a code editor and controls, so that I can write SAS programs and manage job execution without navigating away.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL render the Configure_Output_Screen as the default authenticated view, containing the Monaco_Editor, a Run button, a Cancel button, a Logout button, and the Log_Tab.
2. THE SAS_Job_Runner SHALL render all UI controls (Run button, Cancel button, Logout button, Log_Tab) using DevExtreme components.
3. THE Monaco_Editor SHALL occupy at least 50% of the vertical viewport height of the Configure_Output_Screen.
4. THE Monaco_Editor SHALL apply SAS keyword syntax highlighting such that keywords including `DATA`, `PROC`, `RUN`, and `END` are visually distinguished from plain text.
5. THE SAS_Job_Runner SHALL position the Run button to the left of the Cancel button, with both buttons appearing above the Monaco_Editor.
6. THE Run button SHALL be rendered in an enabled state on initial page load.
7. THE Cancel button SHALL be rendered in a disabled state on initial page load.
8. THE Log_Tab SHALL be displayed directly below the Monaco_Editor on the Configure_Output_Screen.
9. WHEN the Configure_Output_Screen first loads, THE Monaco_Editor SHALL contain no content.
10. WHEN the Configure_Output_Screen first loads, THE Log_Tab SHALL display no content.

---

### Requirement 3: SAS Program Submission (Run)

**User Story:** As a user, I want to click Run to submit my SAS program, so that the job is executed on the Altair SLC Hub.

#### Acceptance Criteria

1. WHEN the user clicks the Run button, THE SAS_Job_Runner SHALL read the current content of the Monaco_Editor and POST it to the internal API_Controller.
2. IF the Monaco_Editor contains no content or contains only whitespace characters when the user clicks Run, THEN THE SAS_Job_Runner SHALL display a validation message in the Log_Tab and SHALL NOT submit any request to the API_Controller.
3. WHEN the Run request is received, THE API_Controller SHALL forward the SAS program to the Altair_SLC_Hub job submission API, including the Bearer_Token in the Authorization header.
4. WHEN the Altair_SLC_Hub returns a Job_ID, THE SAS_Job_Runner SHALL store the Job_ID in client state and begin polling for Job_Status at a 5-second interval.
5. WHILE a Job has Job_Status of `Submitted` or `Running`, THE SAS_Job_Runner SHALL disable the Run button.
6. WHILE a Job has Job_Status of `Submitted` or `Running`, THE API_Controller SHALL return HTTP 409 with a JSON error body if a new job submission request is received for the same Session.
7. WHILE a Job has Job_Status of `Submitted` or `Running`, THE SAS_Job_Runner SHALL enable the Cancel button.
8. IF the Altair_SLC_Hub returns an error on job submission, THEN THE SAS_Job_Runner SHALL display the error message from the response body in the Log_Tab, or a generic error message if the response body contains no detail.
9. WHEN the Altair_SLC_Hub returns a Job_ID, THE Log_Tab SHALL immediately clear any previously displayed content before the first polling cycle completes.

---

### Requirement 4: Job Cancellation (Cancel)

**User Story:** As a user, I want to click Cancel to stop a running job, so that I can abort execution when needed.

#### Acceptance Criteria

1. WHEN the user clicks the Cancel button, THE SAS_Job_Runner SHALL send a cancellation request to the internal API_Controller with the active Job_ID.
2. WHEN the cancellation request is received, THE API_Controller SHALL forward the cancellation to the Altair_SLC_Hub cancel API, including the Bearer_Token in the Authorization header.
3. WHEN the Altair_SLC_Hub returns a non-error response to the cancellation request, THE SAS_Job_Runner SHALL stop polling for Job_Status, re-enable the Run button, disable the Cancel button, and update the Log_Tab to indicate the job was cancelled.
4. IF the Altair_SLC_Hub returns an error on cancellation, THEN THE SAS_Job_Runner SHALL display the error message in the Log_Tab and SHALL continue polling for Job_Status.
5. WHILE the Job_Status is `Completed`, `Failed`, or `Cancelled`, THE SAS_Job_Runner SHALL disable the Cancel button.
6. IF the cancellation request receives no response within 30 seconds, THEN THE SAS_Job_Runner SHALL display a timeout error message in the Log_Tab and SHALL continue polling for Job_Status.

---

### Requirement 5: Job Status Polling

**User Story:** As a user, I want the application to automatically track my job's progress, so that I receive timely updates without manually refreshing.

#### Acceptance Criteria

1. WHILE a Job has Job_Status of `Submitted` or `Running`, THE SAS_Job_Runner SHALL poll the internal API_Controller for Job_Status at a fixed interval of 5 seconds.
2. WHEN the API_Controller receives a status poll request, THE API_Controller SHALL query the Altair_SLC_Hub job status API using the Job_ID and Bearer_Token.
3. WHEN the Altair_SLC_Hub reports the Job_Status as `Completed` or `Failed`, THE SAS_Job_Runner SHALL stop polling, re-enable the Run button, and disable the Cancel button. WHEN the Job_Status transitions to `Completed` or `Failed` and both statuses are reported simultaneously or in conflicting responses, THE SAS_Job_Runner SHALL treat either status as a terminal state and stop polling immediately.
4. IF a status poll request to the Altair_SLC_Hub receives no response within 10 seconds, THEN THE SAS_Job_Runner SHALL stop polling, display a timeout error message in the Log_Tab, re-enable the Run button, and disable the Cancel button.
5. IF a status poll request to the Altair_SLC_Hub returns an error response, THEN THE SAS_Job_Runner SHALL stop polling, display the error message from the response body in the Log_Tab, re-enable the Run button, and disable the Cancel button.

---

### Requirement 6: Program Log Display

**User Story:** As a user, I want to see the Program Log in the Log Tab as my job runs, so that I can monitor execution progress and diagnose issues.

#### Acceptance Criteria

1. WHILE a Job has Job_Status of `Submitted` or `Running`, THE SAS_Job_Runner SHALL fetch the Program_Log from the internal API_Controller on each 5-second polling cycle.
2. WHEN the API_Controller receives a log fetch request, THE API_Controller SHALL retrieve the Program_Log from the Altair_SLC_Hub log API using the Job_ID and Bearer_Token.
3. WHEN new Program_Log content is received, THE Log_Tab SHALL replace its entire displayed content with the latest complete log output.
4. WHEN the Job_Status transitions to `Completed` or `Failed`, THE SAS_Job_Runner SHALL perform one final log fetch and display the result in the Log_Tab.
5. IF the log fetch request to the Altair_SLC_Hub fails, THEN THE SAS_Job_Runner SHALL display an error message in the Log_Tab while preserving any previously displayed log content, and SHALL continue status polling.
6. WHEN the next log fetch after a failure succeeds, THE Log_Tab SHALL replace the error message with the successfully retrieved log content.
7. WHEN new Program_Log content is appended to the Log_Tab, THE Log_Tab SHALL automatically scroll to display the most recently added content.

---

### Requirement 7: Internal API Controllers

**User Story:** As a developer, I want all Altair SLC Hub communication to go through internal API controllers, so that the Bearer Token is never exposed to the browser and the frontend has a consistent interface.

#### Acceptance Criteria

1. THE SAS_Job_Runner SHALL expose API endpoints under the `/api` route prefix within the same ASP.NET Core project for login, job submission, job cancellation, job status, and log retrieval.
2. THE API_Controller SHALL read the Bearer_Token from the server-side Session when forwarding requests to the Altair_SLC_Hub.
3. IF the Altair_SLC_Hub returns an error response, THEN THE API_Controller SHALL return a structured JSON error response containing at minimum `statusCode` and `message` fields from the corresponding endpoint.
4. IF the Altair_SLC_Hub is unreachable (no response within 30 seconds), THEN THE API_Controller SHALL return a structured JSON error response containing at minimum `statusCode` and `message` fields from the corresponding endpoint.
5. THE API_Controller SHALL return HTTP 401 with a JSON body containing `statusCode` and `message` fields from any endpoint when the Session does not contain a valid Bearer_Token (non-null, non-empty string), regardless of the Altair_SLC_Hub response. Authentication validation SHALL take precedence over hub reachability checks, such that HTTP 401 is returned even when the Altair_SLC_Hub is unreachable.
6. THE API_Controller SHALL enforce anti-forgery token validation on all state-changing endpoints (job submission and job cancellation).
7. THE SAS_Job_Runner SHALL reject job submission requests where the SAS program content exceeds 1 MB in size, returning HTTP 413 with a JSON error body containing `statusCode` and `message` fields.
