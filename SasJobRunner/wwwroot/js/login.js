/**
 * login.js — DevExtreme login form submit handler
 * Requirements: 1.2, 1.4, 1.5
 */

(function () {
    'use strict';

    /**
     * Displays an error message in the #errorMessage element.
     * @param {string} message
     */
    function showError(message) {
        var errorEl = document.getElementById('errorMessage');
        if (errorEl) {
            errorEl.textContent = message;
            errorEl.style.display = 'block';
        }
    }

    /**
     * Clears any previously displayed error message.
     */
    function clearError() {
        var errorEl = document.getElementById('errorMessage');
        if (errorEl) {
            errorEl.textContent = '';
            errorEl.style.display = 'none';
        }
    }

    /**
     * Handles the Login button click.
     * Reads credentials from the DevExtreme form, POSTs to /api/auth/login,
     * and either redirects on success or displays the error message on failure.
     */
    function handleLoginSubmit() {
        clearError();

        // Retrieve the DevExtreme dxForm instance by its container element id.
        var formElement = document.getElementById('loginForm');
        if (!formElement) {
            showError('Login form not found.');
            return;
        }

        var formInstance = DevExpress.ui.dxForm.getInstance(formElement);
        if (!formInstance) {
            showError('Login form is not initialized.');
            return;
        }

        // Validate the form before submitting.
        var validationResult = formInstance.validate();
        if (!validationResult.isValid) {
            return;
        }

        // Read field values from the form editors.
        var usernameEditor = formInstance.getEditor('username');
        var passwordEditor = formInstance.getEditor('password');

        var username = usernameEditor ? usernameEditor.option('value') : '';
        var password = passwordEditor ? passwordEditor.option('value') : '';

        // POST credentials to the internal auth API.
        fetch('/api/auth/login', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ username: username, password: password })
        })
        .then(function (response) {
            if (response.ok) {
                // Requirement 1.3: redirect to Configure Output screen on success.
                window.location.href = '/';
                return;
            }

            // Attempt to parse the ApiErrorResponse body for a meaningful message.
            return response.json().then(function (errorBody) {
                // Requirement 1.4: display Hub authentication error on the login screen.
                var message = (errorBody && errorBody.message)
                    ? errorBody.message
                    : 'Login failed. Please check your credentials and try again.';
                showError(message);
            }).catch(function () {
                showError('Login failed. Please check your credentials and try again.');
            });
        })
        .catch(function () {
            // Requirement 1.5: display a generic connectivity error when the server is unreachable.
            showError('Unable to connect to the server. Please check your network connection and try again.');
        });
    }

    // Expose the submit handler so the DevExtreme form's Login button can call it.
    window.loginSubmitHandler = handleLoginSubmit;

}());
