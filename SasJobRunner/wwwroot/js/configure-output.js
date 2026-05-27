// configure-output.js
// SAS Job Runner — Configure Output screen client-side logic
// Tasks 11.1, 11.2, 11.3, 16.1, 16.2, 16.3

// ---------------------------------------------------------------------------
// Module-level state
// ---------------------------------------------------------------------------
const state = {
    jobId: null,           // string | null
    pollingInterval: null, // setInterval handle | null
    lastLog: "",           // last successfully retrieved log text
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Returns the #logContent element. The element lives inside a DevExtreme
 * dxTabPanel template, so it may not exist until the panel renders.
 * @returns {HTMLElement|null}
 */
function getLogContent() {
    return document.getElementById('logContent');
}

/**
 * Writes text to the Log Tab and auto-scrolls to the bottom (Req 6.7).
 * @param {string} text
 */
function setLogText(text) {
    var el = getLogContent();
    if (!el) return;
    el.textContent = text;
    el.scrollTop = el.scrollHeight;
}

/**
 * Reads the anti-forgery token from the <meta> tag injected by the Razor view.
 * @returns {string}
 */
function getAntiForgeryToken() {
    var meta = document.querySelector('meta[name="RequestVerificationToken"]');
    return meta ? meta.getAttribute('content') : '';
}

// ---------------------------------------------------------------------------
// Task 11.2 — Button State Machine
// ---------------------------------------------------------------------------

/**
 * Sets the enabled/disabled state of the Run and Cancel buttons.
 *
 * States:
 *   'idle'       – Run enabled,  Cancel disabled  (page load / job finished)
 *   'running'    – Run disabled, Cancel enabled   (job submitted or running)
 *   'cancelling' – Run disabled, Cancel disabled  (cancel request in-flight)
 *
 * Requirements: 2.6, 2.7, 3.5, 3.7, 4.5, 5.3
 *
 * @param {'idle'|'running'|'cancelling'} newState
 */
function setState(newState) {
    var btnRun    = DevExpress.ui.dxButton.getInstance(document.getElementById('btnRun'));
    var btnCancel = DevExpress.ui.dxButton.getInstance(document.getElementById('btnCancel'));

    switch (newState) {
        case 'idle':
            if (btnRun)    btnRun.option('disabled', false);
            if (btnCancel) btnCancel.option('disabled', true);
            break;

        case 'running':
            if (btnRun)    btnRun.option('disabled', true);
            if (btnCancel) btnCancel.option('disabled', false);
            break;

        case 'cancelling':
            if (btnRun)    btnRun.option('disabled', true);
            if (btnCancel) btnCancel.option('disabled', true);
            break;

        default:
            console.warn('setState: unknown state "' + newState + '"');
            break;
    }
}

// ---------------------------------------------------------------------------
// Task 16.1 — Polling loop: startPolling / stopPolling / pollCycle
// ---------------------------------------------------------------------------

/**
 * Starts the 5-second polling loop.
 * Invokes the first poll cycle immediately, then every 5 seconds.
 * Requirements: 5.1, 6.1
 */
function startPolling() {
    if (state.pollingInterval !== null) {
        // Already polling — do not start a second interval.
        return;
    }
    // Fire immediately, then on interval.
    pollCycle();
    state.pollingInterval = setInterval(pollCycle, 5000);
}

/**
 * Stops the polling loop.
 * Requirements: 5.3, 5.4, 5.5, 6.4
 */
function stopPolling() {
    if (state.pollingInterval !== null) {
        clearInterval(state.pollingInterval);
        state.pollingInterval = null;
    }
}

/**
 * One poll cycle: concurrently fetches job status and program log.
 *
 * Status handling:
 *   - Completed / Failed  → stop polling, final log fetch, setState('idle')
 *   - Cancelled           → stop polling, setState('idle')
 *   - poll error / 504    → stop polling, show error, setState('idle')
 *
 * Log handling:
 *   - Success             → replace #logContent, update state.lastLog, auto-scroll
 *   - Failure             → show error in #logContent, preserve state.lastLog,
 *                           do NOT stop polling (Req 6.5)
 *
 * Requirements: 5.1–5.5, 6.1–6.7
 */
async function pollCycle() {
    if (!state.jobId) return;

    var jobId = state.jobId;

    // Fire both requests concurrently (Req 5.1, 6.1).
    var [statusResult, logResult] = await Promise.allSettled([
        fetchJobStatus(jobId),
        fetchJobLog(jobId)
    ]);

    // ── Handle log result first so the display is updated before we potentially
    //    stop polling and do a final fetch below. ──────────────────────────────
    if (logResult.status === 'fulfilled') {
        // Task 16.2 — success: replace content and auto-scroll (Req 6.3, 6.7)
        state.lastLog = logResult.value;
        setLogText(state.lastLog);
    } else {
        // Task 16.2 — failure: show error, preserve previous log (Req 6.5)
        var logError = logResult.reason && logResult.reason.message
            ? logResult.reason.message
            : 'Failed to retrieve the program log.';
        setLogText('[Log fetch error: ' + logError + ']\n\n' + state.lastLog);
        // Do NOT stop polling on log failure.
    }

    // ── Handle status result ─────────────────────────────────────────────────
    if (statusResult.status === 'rejected') {
        // Req 5.4, 5.5 — poll error or timeout: stop polling, reset UI
        var statusError = statusResult.reason && statusResult.reason.message
            ? statusResult.reason.message
            : 'Failed to retrieve job status.';
        stopPolling();
        setLogText('Status poll error: ' + statusError);
        setState('idle');
        return;
    }

    var status = statusResult.value;

    if (status === 'Completed' || status === 'Failed') {
        // Req 5.3, 6.4 — terminal state: stop polling, do one final log fetch
        stopPolling();
        try {
            var finalLog = await fetchJobLog(jobId);
            state.lastLog = finalLog;
            setLogText(finalLog);
        } catch (e) {
            // Final log fetch failed — keep whatever we last displayed.
        }
        setState('idle');

    } else if (status === 'Cancelled') {
        // Req 5.3 — cancelled: stop polling, reset UI
        stopPolling();
        setState('idle');
    }
    // Submitted / Running → continue polling (interval keeps running).
}

/**
 * Fetches the job status from the internal API.
 * @param {string} jobId
 * @returns {Promise<string>} Resolves with the status string.
 * @throws On non-OK response or network error.
 */
async function fetchJobStatus(jobId) {
    var response = await fetch('/api/jobs/' + encodeURIComponent(jobId) + '/status');

    if (response.status === 401) {
        window.location.href = '/account/login?expired=true';
        throw new Error('Session expired.');
    }

    if (!response.ok) {
        var errorData = null;
        try { errorData = await response.json(); } catch (_) {}
        var msg = errorData && errorData.message
            ? errorData.message
            : 'HTTP ' + response.status;
        throw new Error(msg);
    }

    var data = await response.json();
    return data.status;
}

/**
 * Fetches the program log from the internal API.
 * @param {string} jobId
 * @returns {Promise<string>} Resolves with the log text.
 * @throws On non-OK response or network error.
 */
async function fetchJobLog(jobId) {
    var response = await fetch('/api/jobs/' + encodeURIComponent(jobId) + '/log');

    if (response.status === 401) {
        window.location.href = '/account/login?expired=true';
        throw new Error('Session expired.');
    }

    if (!response.ok) {
        var errorData = null;
        try { errorData = await response.json(); } catch (_) {}
        var msg = errorData && errorData.message
            ? errorData.message
            : 'HTTP ' + response.status;
        throw new Error(msg);
    }

    var data = await response.json();
    return data.log || '';
}

// ---------------------------------------------------------------------------
// Task 11.1 — Monaco Editor initialisation with SAS syntax highlighting
// ---------------------------------------------------------------------------
(function initMonaco() {
    const CDN_BASE = "https://cdn.jsdelivr.net/npm/monaco-editor@0.52.0/min";

    require.config({ paths: { vs: CDN_BASE + "/vs" } });

    require(["vs/editor/editor.main"], function () {

        // Register the SAS language
        monaco.languages.register({ id: "sas" });

        // Monarch tokenizer for SAS (Req 2.4, Property 7)
        monaco.languages.setMonarchTokensProvider("sas", {
            keywords: [
                "DATA", "PROC", "RUN", "END",
                "SET", "MERGE", "BY", "IF", "THEN", "ELSE",
                "DO", "OUTPUT", "KEEP", "DROP", "WHERE",
                "INPUT", "CARDS", "DATALINES",
                "TITLE", "OPTIONS", "LIBNAME", "FILENAME"
            ],

            // Case-insensitive matching
            ignoreCase: true,

            tokenizer: {
                root: [
                    [/\s+/, "white"],
                    [/\*[^;]*;/, "comment"],
                    [/\/\*/, "comment", "@blockComment"],
                    [/'[^']*'/, "string"],
                    [/"[^"]*"/, "string"],
                    [/\d+(\.\d+)?([eE][+-]?\d+)?/, "number"],
                    [
                        /[A-Za-z_][A-Za-z0-9_]*/,
                        {
                            cases: {
                                "@keywords": "keyword",
                                "@default": "identifier"
                            }
                        }
                    ],
                    [/[;,.()\[\]{}]/, "delimiter"],
                    [/[=<>!+\-*\/&|^~%]/, "operator"]
                ],

                blockComment: [
                    [/[^/*]+/, "comment"],
                    [/\*\//, "comment", "@pop"],
                    [/[/*]/, "comment"]
                ]
            }
        });

        // Create the Monaco editor instance
        window.monacoEditor = monaco.editor.create(
            document.getElementById("monacoContainer"),
            {
                value: "",
                language: "sas",
                theme: "vs",
                automaticLayout: true
            }
        );

        // Set initial button state once editor and DevExtreme widgets are ready.
        setState('idle');
    });
}());

// ---------------------------------------------------------------------------
// Task 11.3 — Run button click handler
// ---------------------------------------------------------------------------

/**
 * Handles the Run button click.
 *
 * 1. Validates that the editor contains non-whitespace content (Req 3.2).
 * 2. POSTs to /api/jobs/submit with anti-forgery token (Req 3.1, 7.6).
 * 3. On success: stores jobId, clears log, transitions to 'running',
 *    starts polling (Req 3.4, 3.5, 3.7, 3.9, 16.3).
 * 4. On error: displays message in Log Tab (Req 3.8).
 */
async function onRunClick() {
    // --- Validation (Req 3.2) ---
    var editorContent = window.monacoEditor ? window.monacoEditor.getValue() : '';
    if (!editorContent || !editorContent.trim()) {
        setLogText('Please enter a SAS program before clicking Run.');
        return;
    }

    // --- Submit (Req 3.1) ---
    try {
        var response = await fetch('/api/jobs/submit', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify({ sasCode: editorContent })
        });

        if (response.ok) {
            // Success path (Req 3.4, 3.5, 3.7, 3.9)
            var data = await response.json();
            state.jobId = data.jobId;
            state.lastLog = '';
            setLogText('');                // clear log immediately (Req 3.9)
            setState('running');           // disable Run, enable Cancel
            startPolling();               // Task 16.3 — begin polling (Req 3.4)
        } else {
            var errorData = null;
            try { errorData = await response.json(); } catch (_) {}

            if (response.status === 401) {
                window.location.href = '/account/login?expired=true';
                return;
            }

            if (response.status === 409) {
                setLogText(errorData && errorData.message
                    ? errorData.message
                    : 'A job is already active. Wait for it to complete or cancel it before submitting a new one.');
            } else if (response.status === 413) {
                setLogText(errorData && errorData.message
                    ? errorData.message
                    : 'The SAS program exceeds the maximum allowed size of 1 MB.');
            } else {
                setLogText(errorData && errorData.message
                    ? errorData.message
                    : 'An error occurred while submitting the job (HTTP ' + response.status + ').');
            }
        }
    } catch (networkError) {
        setLogText('Unable to reach the server. Please check your connection and try again.');
    }
}

// ---------------------------------------------------------------------------
// Initialisation — wire up button handlers on DOMContentLoaded
// ---------------------------------------------------------------------------
document.addEventListener('DOMContentLoaded', function () {
    var runEl = document.getElementById('btnRun');
    if (runEl) {
        var btnRunInstance = DevExpress.ui.dxButton.getInstance(runEl);
        if (btnRunInstance) {
            btnRunInstance.option('onClick', onRunClick);
        } else {
            runEl.addEventListener('click', onRunClick);
        }
    }
});
