(function (window, document, $) {
    'use strict';

    function setState(element, valid, active) {
        if (!element) return;
        element.classList.toggle('is-valid', active && valid);
        element.classList.toggle('is-invalid', active && !valid);
    }

    function attachPasswordPolicy(form) {
        var password = form.querySelector('[data-password-primary]');
        var confirmation = form.querySelector('[data-password-confirmation]');
        var policy = form.querySelector('[data-password-policy]');
        var policyPanel = form.querySelector('[data-password-policy-panel]');
        var policyCount = form.querySelector('[data-password-policy-count]');
        var confirmationStatus = form.querySelector('[data-password-confirmation-status]');
        if (!password || !confirmation || !policy) return;

        var rules = {
            length: function (value) { return value.length >= 12 && value.length <= 100; },
            lower: function (value) { return /[a-z]/.test(value); },
            upper: function (value) { return /[A-Z]/.test(value); },
            number: function (value) { return /[0-9]/.test(value); },
            symbol: function (value) { return /[^A-Za-z0-9\s]/.test(value); },
            spaces: function (value) { return !/\s/.test(value); }
        };

        function refresh() {
            var value = password.value || '';
            var allValid = value.length > 0;
            var validCount = 0;
            Array.prototype.forEach.call(policy.querySelectorAll('[data-password-rule]'), function (item) {
                var valid = rules[item.getAttribute('data-password-rule')](value);
                setState(item, valid, value.length > 0);
                if (valid && value.length > 0) validCount += 1;
                allValid = allValid && valid;
            });
            if (policyCount) policyCount.textContent = validCount + ' of 6 rules met';
            if (policyPanel) {
                policyPanel.classList.toggle('is-active', value.length > 0);
                policyPanel.classList.toggle('is-complete', allValid);
            }
            password.setCustomValidity(value.length > 0 && !allValid
                ? 'Use 12–100 characters with uppercase, lowercase, a number, and a symbol, without spaces.'
                : '');

            var confirmationActive = confirmation.value.length > 0;
            var matches = confirmationActive && confirmation.value === value;
            confirmation.setCustomValidity(confirmationActive && !matches ? 'The password confirmation does not match.' : '');
            setState(confirmationStatus, matches, confirmationActive);
            if (confirmationStatus) confirmationStatus.textContent = confirmationActive
                ? matches ? 'Passwords match.' : 'Passwords do not match.'
                : 'Re-enter the password to confirm it.';
        }

        password.addEventListener('input', refresh);
        confirmation.addEventListener('input', refresh);
        refresh();
    }

    function attachEmailAvailability(form) {
        var input = form.querySelector('[data-email-availability]');
        var status = form.querySelector('[data-email-availability-status]');
        var url = form.getAttribute('data-email-check-url');
        if (!input || !status || !url || !$) return;

        var timer = null;
        var sequence = 0;
        var lastAvailableEmail = '';

        function normalizedValue() { return (input.value || '').trim().toLowerCase(); }
        function validEmail(value) { return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value); }
        function show(message, state) {
            status.textContent = message;
            input.classList.toggle('availability-valid', state === 'valid');
            input.classList.toggle('availability-invalid', state === 'invalid');
            status.classList.toggle('is-valid', state === 'valid');
            status.classList.toggle('is-invalid', state === 'invalid');
            status.classList.toggle('is-checking', state === 'checking');
        }
        function check() {
            window.clearTimeout(timer);
            var value = normalizedValue();
            if (!validEmail(value)) {
                input.setCustomValidity('');
                show(value ? 'Enter a valid email address.' : 'Email availability will be checked here.', value ? 'invalid' : '');
                return;
            }
            if (value === lastAvailableEmail) {
                input.setCustomValidity('');
                show('Email is available.', 'valid');
                return;
            }

            var requestSequence = ++sequence;
            input.setCustomValidity('Checking email availability.');
            show('Checking email availability…', 'checking');
            var token = document.querySelector('input[name="__RequestVerificationToken"]');
            var excludeSource = form.getAttribute('data-email-exclude-source');
            var excludeInput = excludeSource ? document.querySelector(excludeSource) : null;
            $.ajax({
                url: url,
                method: 'POST',
                dataType: 'json',
                data: {
                    __RequestVerificationToken: token ? token.value : '',
                    email: value,
                    clientId: excludeInput && excludeInput.value ? excludeInput.value : null
                }
            }).done(function (response) {
                if (requestSequence !== sequence || value !== normalizedValue()) return;
                if (response.Available) {
                    lastAvailableEmail = value;
                    input.setCustomValidity('');
                    show(response.Message || 'Email is available.', 'valid');
                } else {
                    input.setCustomValidity(response.Message || 'This email address is already in use.');
                    show(response.Message || 'This email address is already in use.', 'invalid');
                }
            }).fail(function (xhr) {
                if (requestSequence !== sequence || value !== normalizedValue()) return;
                input.setCustomValidity('');
                var response = xhr.responseJSON || {};
                show(response.Message || 'Availability could not be checked; it will be verified when submitted.', '');
            });
        }
        function schedule() {
            window.clearTimeout(timer);
            lastAvailableEmail = '';
            var value = normalizedValue();
            if (!value || !validEmail(value)) {
                input.setCustomValidity('');
                show(value ? 'Enter a valid email address.' : 'Email availability will be checked here.', value ? 'invalid' : '');
                return;
            }
            input.setCustomValidity('Checking email availability.');
            show('Waiting to check…', 'checking');
            timer = window.setTimeout(check, 550);
        }

        input.addEventListener('input', schedule);
        input.addEventListener('blur', check);
        schedule();
    }

    function attach(form) {
        if (!form || form.getAttribute('data-account-validation-ready') === 'true') return;
        form.setAttribute('data-account-validation-ready', 'true');
        attachPasswordPolicy(form);
        attachEmailAvailability(form);
    }

    function initialize() {
        Array.prototype.forEach.call(document.querySelectorAll('[data-account-validation]'), attach);
    }

    window.AutoDealerAccountValidation = { initialize: initialize };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize);
    else initialize();
}(window, document, window.jQuery));
