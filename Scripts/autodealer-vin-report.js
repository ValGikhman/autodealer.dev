(function (window, document) {
    'use strict';

    var selector = '[data-autodealer-vin-report]';
    var defaultApiUrl = '/api/service/vin/{vin}/html';
    var vinPattern = /^[A-HJ-NPR-Z0-9]{17}$/;

    function messageFromResponse(response, body) {
        try {
            var payload = JSON.parse(body);
            return payload.message || (payload.error && payload.error.message) || body;
        } catch (ignore) {
            return body || ('The VIN report request failed with status ' + response.status + '.');
        }
    }

    function reportUrl(element, vin) {
        var template = (element.getAttribute('data-api-url') || defaultApiUrl).trim();
        if (template.indexOf('{vin}') === -1) throw new Error('data-api-url must contain the {vin} placeholder.');
        return template.replace(/\{vin\}/g, encodeURIComponent(vin));
    }

    function setState(element, state, message) {
        element.setAttribute('data-state', state);
        element.setAttribute('aria-busy', state === 'loading' ? 'true' : 'false');
        if (state !== 'ready') {
            element.textContent = message;
            element.setAttribute('role', state === 'error' ? 'alert' : 'status');
        } else {
            element.removeAttribute('role');
        }
    }

    function load(element, vinOverride) {
        var vin = (vinOverride || element.getAttribute('data-vin') || '').trim().toUpperCase();
        element.setAttribute('data-vin', vin);
        if (!vinPattern.test(vin)) {
            setState(element, 'error', 'Enter a valid 17-character VIN. VINs cannot contain I, O, or Q.');
            return Promise.reject(new Error('Invalid VIN.'));
        }

        var url;
        try { url = reportUrl(element, vin); }
        catch (error) {
            setState(element, 'error', error.message);
            return Promise.reject(error);
        }

        if (element._autodealerRequest && element._autodealerRequest.abort) element._autodealerRequest.abort();
        var controller = window.AbortController ? new AbortController() : null;
        element._autodealerRequest = controller;
        var headers = { Accept: 'text/html' };
        var apiKey = (element.getAttribute('data-api-key') || '').trim();
        if (apiKey) headers.Authorization = 'Bearer ' + apiKey;

        setState(element, 'loading', element.getAttribute('data-loading-text') || 'Loading vehicle report...');
        element.dispatchEvent(new CustomEvent('autodealer:loading', { detail: { vin: vin, url: url } }));

        return window.fetch(url, {
            method: 'GET',
            headers: headers,
            credentials: 'same-origin',
            cache: 'no-store',
            signal: controller ? controller.signal : undefined
        }).then(function (response) {
            return response.text().then(function (body) {
                if (!response.ok) throw new Error(messageFromResponse(response, body));
                return { response: response, body: body };
            });
        }).then(function (result) {
            element.innerHTML = result.body;
            setState(element, 'ready', '');
            element.dispatchEvent(new CustomEvent('autodealer:loaded', {
                detail: { vin: vin, url: url, response: result.response }
            }));
            return result;
        }).catch(function (error) {
            if (error.name === 'AbortError') return;
            setState(element, 'error', error.message || 'The vehicle report could not be loaded.');
            element.dispatchEvent(new CustomEvent('autodealer:error', { detail: { vin: vin, url: url, error: error } }));
            throw error;
        });
    }

    function init(root) {
        var scope = root || document;
        var elements = scope.matches && scope.matches(selector) ? [scope] : scope.querySelectorAll(selector);
        Array.prototype.forEach.call(elements, function (element) {
            if (element.getAttribute('data-autodealer-ready') === 'true') return;
            element.setAttribute('data-autodealer-ready', 'true');
            if (element.getAttribute('data-auto-load') !== 'false') load(element).catch(function () { });
        });
    }

    window.AutoDealerVinReport = { init: init, load: load };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { init(document); });
    else init(document);
}(window, document));
