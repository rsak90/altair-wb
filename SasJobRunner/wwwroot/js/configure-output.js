// configure-output.js — SAS Job Runner Configure Output screen

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------
const state = {
    jobId: null,           // string | null — set after Create succeeds
    pollingInterval: null, // setInterval handle | null
    lastLog: '',           // last successfully retrieved log text
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function getLogContent() {
    return document.getElementById('logContent');
}

function setLogText(text) {
    var el = getLogContent();
    if (!el) return;
    el.textContent = text;
    el.scrollTop = el.scrollHeight;
}

function getAntiForgeryToken() {
    var meta = document.querySelector('meta[name="RequestVerificationToken"]');
    return meta ? meta.getAttribute('content') : '';
}

function btn(id) {
    return DevExpress.ui.dxButton.getInstance(document.getElementById(id));
}

// ---------------------------------------------------------------------------
// Button state machine
//
//  'idle'      — Create enabled, Run disabled, Cancel disabled   (page load / job done)
//  'created'   — Create disabled, Run enabled, Cancel enabled    (job created, not yet running)
//  'running'   — Create disabled, Run disabled, Cancel enabled   (job committed / executing)
//  'cancelling'— Create disabled, Run disabled, Cancel disabled  (cancel in-flight)
// ---------------------------------------------------------------------------
function setState(newState) {
    var create = btn('btnCreate');
    var run    = btn('btnRun');
    var cancel = btn('btnCancel');

    switch (newState) {
        case 'idle':
            if (create) create.option('disabled', false);
            if (run)    run.option('disabled', true);
            if (cancel) cancel.option('disabled', true);
            break;
        case 'created':
            if (create) create.option('disabled', true);
            if (run)    run.option('disabled', false);
            if (cancel) cancel.option('disabled', false);
            break;
        case 'running':
            if (create) create.option('disabled', true);
            if (run)    run.option('disabled', true);
            if (cancel) cancel.option('disabled', false);
            break;
        case 'cancelling':
            if (create) create.option('disabled', true);
            if (run)    run.option('disabled', true);
            if (cancel) cancel.option('disabled', true);
            break;
        default:
            console.warn('setState: unknown state "' + newState + '"');
    }
}

// ---------------------------------------------------------------------------
// Create button — POST /api/jobs/create
// ---------------------------------------------------------------------------
async function onCreateClick() {
    var editorContent = window.monacoEditor ? window.monacoEditor.getValue() : '';
    if (!editorContent || !editorContent.trim()) {
        setLogText('Please enter a SAS program before clicking Create.');
        return;
    }

    setLogText('Creating job...');

    try {
        var response = await fetch('/api/jobs/create', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify({ sasCode: editorContent })
        });

        if (response.ok) {
            var data = await response.json();
            state.jobId = data.jobId;
            state.lastLog = '';
            setLogText('Job created (ID: ' + state.jobId + '). Click Run to submit for execution.');
            setState('created');
        } else {
            var errorData = null;
            try { errorData = await response.json(); } catch (_) {}
            if (response.status === 401) { window.location.href = '/account/login?expired=true'; return; }
            setLogText(errorData && errorData.message
                ? errorData.message
                : 'Failed to create job (HTTP ' + response.status + ').');
        }
    } catch (e) {
        setLogText('Unable to reach the server. Please check your connection.');
    }
}

// ---------------------------------------------------------------------------
// Run button — POST /api/jobs/{jobId}/commit
// ---------------------------------------------------------------------------
async function onRunClick() {
    if (!state.jobId) {
        setLogText('No job created yet. Click Create first.');
        return;
    }

    setLogText('Submitting job for execution...');
    setState('running');

    try {
        var response = await fetch('/api/jobs/' + encodeURIComponent(state.jobId) + '/commit', {
            method: 'POST',
            headers: {
                'RequestVerificationToken': getAntiForgeryToken()
            }
        });

        if (response.ok) {
            setLogText('');       // clear log — polling will fill it
            startPolling();
        } else {
            var errorData = null;
            try { errorData = await response.json(); } catch (_) {}
            if (response.status === 401) { window.location.href = '/account/login?expired=true'; return; }
            setLogText(errorData && errorData.message
                ? errorData.message
                : 'Failed to commit job (HTTP ' + response.status + ').');
            setState('created'); // revert — job still exists, user can retry Run
        }
    } catch (e) {
        setLogText('Unable to reach the server. Please check your connection.');
        setState('created');
    }
}

// ---------------------------------------------------------------------------
// Cancel button — DELETE /api/jobs/{jobId}/cancel
// ---------------------------------------------------------------------------
async function onCancelClick() {
    if (!state.jobId) return;

    setState('cancelling');

    try {
        var response = await fetch('/api/jobs/' + encodeURIComponent(state.jobId) + '/cancel', {
            method: 'DELETE',
            headers: { 'RequestVerificationToken': getAntiForgeryToken() }
        });

        if (response.ok) {
            stopPolling();
            state.jobId = null;
            state.lastLog = '';
            setLogText('Job cancelled.');
            setState('idle');
        } else {
            var errorData = null;
            try { errorData = await response.json(); } catch (_) {}
            if (response.status === 401) { window.location.href = '/account/login?expired=true'; return; }
            setLogText(errorData && errorData.message
                ? errorData.message
                : 'Cancel failed (HTTP ' + response.status + ').');
            setState('running');
        }
    } catch (e) {
        setLogText('Unable to reach the server to cancel. Polling will continue.');
        setState('running');
    }
}

// ---------------------------------------------------------------------------
// Logout button
// ---------------------------------------------------------------------------
async function onLogoutClick() {
    try {
        await fetch('/account/logout', {
            method: 'POST',
            headers: { 'RequestVerificationToken': getAntiForgeryToken() }
        });
    } catch (_) {}
    window.location.href = '/account/login';
}

// ---------------------------------------------------------------------------
// Polling
// ---------------------------------------------------------------------------
function startPolling() {
    if (state.pollingInterval !== null) return;
    pollCycle();
    state.pollingInterval = setInterval(pollCycle, 5000);
}

function stopPolling() {
    if (state.pollingInterval !== null) {
        clearInterval(state.pollingInterval);
        state.pollingInterval = null;
    }
}

async function pollCycle() {
    if (!state.jobId) return;
    var jobId = state.jobId;

    var [statusResult, logResult] = await Promise.allSettled([
        fetchJobStatus(jobId),
        fetchJobLog(jobId)
    ]);

    // Update log display
    if (logResult.status === 'fulfilled') {
        state.lastLog = logResult.value;
        setLogText(state.lastLog);
    } else {
        var logErr = logResult.reason && logResult.reason.message
            ? logResult.reason.message : 'Failed to retrieve log.';
        setLogText('[Log error: ' + logErr + ']\n\n' + state.lastLog);
    }

    // Handle status
    if (statusResult.status === 'rejected') {
        var statusErr = statusResult.reason && statusResult.reason.message
            ? statusResult.reason.message : 'Failed to retrieve status.';
        stopPolling();
        setLogText('Status poll error: ' + statusErr);
        setState('idle');
        return;
    }

    var status = statusResult.value;

    if (status === 'Completed' || status === 'Failed') {
        stopPolling();
        try {
            var finalLog = await fetchJobLog(jobId);
            state.lastLog = finalLog;
            setLogText(finalLog);
        } catch (_) {}
        state.jobId = null;
        setState('idle');
    } else if (status === 'Cancelled') {
        stopPolling();
        state.jobId = null;
        setState('idle');
    }
    // Submitted / Running → keep polling
}

async function fetchJobStatus(jobId) {
    var response = await fetch('/api/jobs/' + encodeURIComponent(jobId) + '/status');
    if (response.status === 401) { window.location.href = '/account/login?expired=true'; throw new Error('Session expired.'); }
    if (!response.ok) {
        var err = null; try { err = await response.json(); } catch (_) {}
        throw new Error(err && err.message ? err.message : 'HTTP ' + response.status);
    }
    var data = await response.json();
    return data.status;
}

async function fetchJobLog(jobId) {
    var response = await fetch('/api/jobs/' + encodeURIComponent(jobId) + '/log');
    if (response.status === 401) { window.location.href = '/account/login?expired=true'; throw new Error('Session expired.'); }
    if (!response.ok) {
        var err = null; try { err = await response.json(); } catch (_) {}
        throw new Error(err && err.message ? err.message : 'HTTP ' + response.status);
    }
    var data = await response.json();
    return data.log || '';
}

// ---------------------------------------------------------------------------
// Monaco Editor init
// ---------------------------------------------------------------------------
(function initMonaco() {
    require.config({ paths: { vs: 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.0/min/vs' } });
    require(['vs/editor/editor.main'], function () {
        monaco.languages.register({ id: 'sas' });
        monaco.languages.setMonarchTokensProvider('sas', {
            keywords: ['DATA','PROC','RUN','END','SET','MERGE','BY','IF','THEN','ELSE',
                       'DO','OUTPUT','KEEP','DROP','WHERE','INPUT','CARDS','DATALINES',
                       'TITLE','OPTIONS','LIBNAME','FILENAME'],
            ignoreCase: true,
            tokenizer: {
                root: [
                    [/\s+/, 'white'],
                    [/\*[^;]*;/, 'comment'],
                    [/\/\*/, 'comment', '@blockComment'],
                    [/'[^']*'/, 'string'],
                    [/"[^"]*"/, 'string'],
                    [/\d+(\.\d+)?([eE][+-]?\d+)?/, 'number'],
                    [/[A-Za-z_][A-Za-z0-9_]*/, { cases: { '@keywords': 'keyword', '@default': 'identifier' } }],
                    [/[;,.()\[\]{}]/, 'delimiter'],
                    [/[=<>!+\-*\/&|^~%]/, 'operator']
                ],
                blockComment: [
                    [/[^/*]+/, 'comment'],
                    [/\*\//, 'comment', '@pop'],
                    [/[/*]/, 'comment']
                ]
            }
        });

        window.monacoEditor = monaco.editor.create(
            document.getElementById('monacoContainer'),
            { value: '', language: 'sas', theme: 'vs', automaticLayout: true }
        );

        setState('idle');
    });
}());
