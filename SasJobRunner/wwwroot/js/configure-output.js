// configure-output.js
// SAS Job Runner — Configure Output screen client-side logic
// Tasks 11.1, 11.2, 11.3

// ---------------------------------------------------------------------------
// Module-level state
// ---------------------------------------------------------------------------
const state = {
    jobId: null,           // string | null
    pollingInterval: null, // setInterval handle | null
    lastLog: "",           // last successfully retrieved log text
};

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
 * Uses the DevExtreme dxButton API so that the visual state is managed by the
 * component rather than raw DOM attribute manipulation.
 *
 * Requirements: 2.6, 2.7, 3.5, 3.7, 4.5, 5.3
 *
 * @param {'idle'|'running'|'cancelling'} state
 */
function setState(state) {
    var btnRun    = DevExpress.ui.dxButton.getInstance(document.getElementById('btnRun'));
    var btnCancel = DevExpress.ui.dxButton.getInstance(document.getElementById('btnCancel'));

    switch (state) {
        case 'idle':
            // Run enabled, Cancel disabled (Requirements 2.6, 2.7)
            btnRun.option('disabled', false);
            btnCancel.option('disabled', true);
            break;

        case 'running':
            // Run disabled, Cancel enabled (Requirements 3.5, 3.7)
            btnRun.option('disabled', true);
            btnCancel.option('disabled', false);
            break;

        case 'cancelling':
            // Run disabled, Cancel disabled (Requirements 4.5, 5.3)
            btnRun.option('disabled', true);
            btnCancel.option('disabled', true);
            break;

        default:
            console.warn('setState: unknown state "' + state + '"');
            break;
    }
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

        // Monarch tokenizer for SAS
        // Keywords are matched case-insensitively; all other identifiers are
        // classified as "identifier" (not "keyword").
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
                    // Whitespace
                    [/\s+/, "white"],

                    // Single-line comment: * ... ;
                    [/\*[^;]*;/, "comment"],

                    // Block comment: /* ... */
                    [/\/\*/, "comment", "@blockComment"],

                    // String literals (single-quoted)
                    [/'[^']*'/, "string"],

                    // String literals (double-quoted)
                    [/"[^"]*"/, "string"],

                    // Numbers
                    [/\d+(\.\d+)?([eE][+-]?\d+)?/, "number"],

                    // Identifiers and keywords
                    // The @keywords check uses the ignoreCase flag above.
                    [
                        /[A-Za-z_][A-Za-z0-9_]*/,
                        {
                            cases: {
                                "@keywords": "keyword",
                                "@default": "identifier"
                            }
                        }
                    ],

                    // Operators and punctuation
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

        // Set initial button state once the editor (and DevExtreme widgets) are ready.
        // The DevExtreme buttons are rendered on DOMContentLoaded, which has already
        // fired by the time this require callback runs, so calling setState here is safe.
        setState('idle');
    });
}());

// ---------------------------------------------------------------------------
// Task 11.3 — Run button click handler
// ---------------------------------------------------------------------------

/**
 * Handles the Run button click.
 *
 * 1. Validates that the editor contains non-whitespace content.
 * 2. Reads the anti-forgery token from the <meta> tag.
 * 3. POSTs to /api/jobs/submit.
 * 4. On success: stores jobId, clears log, transitions to 'running'.
 * 5. On error: displays a message in #logContent with specific handling for
 *    409 (duplicate job) and 413 (payload too large).
 *
 * Requirements: 3.1, 3.2, 3.4, 3.5, 3.7, 3.8, 3.9
 */
async function onRunClick() {
    var logContent = document.getElementById('logContent');

    // --- Validation (Req 3.2) ---
    var editorContent = window.monacoEditor ? window.monacoEditor.getValue() : '';
    if (!editorContent || !editorContent.trim()) {
        logContent.textContent = 'Please enter a SAS program before clicking Run.';
        return;
    }

    // --- Anti-forgery token (Req 7.6) ---
    var tokenMeta = document.querySelector('meta[name="RequestVerificationToken"]');
    var antiForgeryToken = tokenMeta ? tokenMeta.getAttribute('content') : '';

    // --- Submit (Req 3.1) ---
    try {
        var response = await fetch('/api/jobs/submit', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': antiForgeryToken
            },
            body: JSON.stringify({ sasCode: editorContent })
        });

        if (response.ok) {
            // Success path (Req 3.4, 3.5, 3.7, 3.9)
            var data = await response.json();
            state.jobId = data.jobId;
            logContent.textContent = '';       // clear log immediately (Req 3.9)
            setState('running');               // disable Run, enable Cancel (Req 3.5, 3.7)
        } else {
            // Error path (Req 3.8)
            var errorData = null;
            try {
                errorData = await response.json();
            } catch (_) {
                // response body was not JSON
            }

            if (response.status === 401) {
                // Session expired — redirect to login (Req 1.8)
                window.location.href = '/account/login?expired=true';
                return;
            }

            if (response.status === 409) {
                logContent.textContent = errorData && errorData.message
                    ? errorData.message
                    : 'A job is already active. Wait for it to complete or cancel it before submitting a new one.';
            } else if (response.status === 413) {
                logContent.textContent = errorData && errorData.message
                    ? errorData.message
                    : 'The SAS program exceeds the maximum allowed size of 1 MB.';
            } else {
                logContent.textContent = errorData && errorData.message
                    ? errorData.message
                    : 'An error occurred while submitting the job (HTTP ' + response.status + ').';
            }
        }
    } catch (networkError) {
        // Network-level failure
        logContent.textContent = 'Unable to reach the server. Please check your connection and try again.';
    }
}

// ---------------------------------------------------------------------------
// Initialisation — button state on page load
// ---------------------------------------------------------------------------
// DOMContentLoaded fires before the Monaco require callback, but the
// DevExtreme dxButton instances are created during DOMContentLoaded by the
// Razor view's inline scripts.  We therefore call setState('idle') inside the
// Monaco callback above (after the editor is ready) rather than here.
// This listener is kept as a safety net for environments where Monaco is
// already cached and the require callback fires synchronously.
document.addEventListener('DOMContentLoaded', function () {
    // Guard: only call setState if the DevExtreme button instances exist.
    var runEl = document.getElementById('btnRun');
    if (runEl && DevExpress.ui.dxButton.getInstance(runEl)) {
        setState('idle');
    }

    // Wire up the Run button click handler (Task 11.3).
    // The DevExtreme dxButton onClick option is set in the Razor view; this
    // listener is a fallback for plain <button> or direct DOM binding.
    if (runEl) {
        var btnRunInstance = DevExpress.ui.dxButton.getInstance(runEl);
        if (btnRunInstance) {
            btnRunInstance.option('onClick', onRunClick);
        } else {
            runEl.addEventListener('click', onRunClick);
        }
    }
});
