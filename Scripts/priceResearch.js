(function ($) {
    'use strict';
    var root = document.getElementById('price-research');
    if (!root) return;
    var form = document.getElementById('price-research-form');
    var defaultApiKeyEnabled = root.getAttribute('data-default-api-key-enabled') === 'true';
    var clientResearch = root.getAttribute('data-client-research') === 'true';
    var clientKey = document.getElementById('client-research-key');
    var submit = document.getElementById('pr-submit');
    var error = document.getElementById('pr-error');
    var results = document.getElementById('pr-results');
    var empty = document.getElementById('pr-empty');
    var progress = document.getElementById('pr-progress');
    var currentType = document.getElementById('pr-comparables-current'), recentType = document.getElementById('pr-comparables-recent');
    var currentRows = [], recentRows = [], comparablesRadius = 0, researchRevision = 0, researchController = null;
    var money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
    var integer = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
    var distanceFormat = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
    function numeric(value) { if (value === null || value === undefined || (typeof value === 'string' && value.trim() === '')) return null; var n = Number(value); return Number.isFinite(n) ? n : null; }
    function formatNumber(value) { return value === null ? '—' : integer.format(value); }
    function formatPrice(value) { return value === null ? '—' : money.format(value); }
    function escape(value) { return String(value === null || value === undefined ? '' : value).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
    function safeUrl(value) { try { if (!/^https?:\/\//i.test(String(value || ''))) return null; var u = new URL(value); return !u.username && !u.password && (u.protocol === 'http:' || u.protocol === 'https:') ? u.href : null; } catch (ignore) { return null; } }
    function accountKey(value) { return value.trim().replace(/^Bearer\s+/i, '').trim(); }
    function accountKeyError(value) {
        if (/^mc_(live|test)_/i.test(value)) return 'Use your AutoDealer.dev account API key here. The MarketCheck provider key is configured on the server.';
        var parts = value.split('.', 2);
        return parts.length !== 2 || parts[0].indexOf('ad_') !== 0 ? 'Enter the complete AutoDealer.dev account API key, including the secret after the dot (ad_keyId.secret).' : null;
    }
    function errorMessage(payload, fallback) {
        if (!payload || typeof payload !== 'object') return fallback;
        var nested = payload.error || payload.Error, message = payload.message || payload.Message || (nested && (nested.message || nested.Message));
        if (message) return message;
        var state = payload.ModelState || payload.modelState;
        if (state) { var errors = []; Object.keys(state).forEach(function (key) { if (Array.isArray(state[key])) errors = errors.concat(state[key]); }); if (errors.length) return errors.join(' '); }
        return fallback;
    }
    function setBusy(busy) { submit.disabled = busy || (clientResearch && !clientKey); submit.querySelector('span').textContent = busy ? 'Researching…' : 'Research price'; root.querySelector('.pr-results-panel').setAttribute('aria-busy', busy ? 'true' : 'false'); progress.hidden = !busy; }
    function makeRows(listings, request) {
        return (Array.isArray(listings) ? listings : []).map(function (row) {
            var miles = numeric(row.miles), price = numeric(row.price), dist = (typeof row.dist === 'number' || typeof row.dist === 'string') ? numeric(row.dist) : null, days = numeric(row.dom);
            if (dist !== null && dist < 0) dist = null;
            var own = String(row.vin || '').toUpperCase() === request.Vin, local = !own && dist !== null && dist <= request.Radius;
            var url = safeUrl(row.vdp_url), source = url ? '<a href="' + escape(url) + '" target="_blank" rel="noopener noreferrer">View</a>' : 'No listing URL';
            return { Vehicle: [row.year, row.make, row.model, row.trim].filter(function (v) { return v !== null && v !== undefined && v !== ''; }).join(' '), Vin: String(row.vin || ''), Dealer: String(row.dealer_name || ''), Price: price, PriceDisplay: formatPrice(price), Mileage: miles, MileageDisplay: formatNumber(miles), Distance: dist, DistanceDisplay: dist === null ? 'Unknown' : distanceFormat.format(dist) + ' mi', Days: days, DaysDisplay: formatNumber(days), Source: source, IsSubject: own, IsLocal: local };
        });
    }
    function gridColumns() {
        return [
            { field: 'Vin', title: 'VIN', width: '19%', searchable: false, filterable: false },
            { field: 'Dealer', title: 'Dealer', width: '24%', searchable: false, filterable: false },
            { field: 'Price', title: 'Asking price', width: '12%', filterable: false, template: '#pr-price-template', searchable: false },
            { field: 'Mileage', title: 'Mileage', width: '12%', filterable: false, template: '#pr-mileage-template', searchable: false },
            { field: 'Distance', title: 'Distance', width: '12%', filterable: false, template: '#pr-distance-template', searchable: false },
            { field: 'Days', title: 'Days', width: '13%', filterable: false, template: '#pr-dom-template', searchable: false },
            { field: 'Source', title: 'Source', width: '8%', encoded: false, sortable: false, filterable: false, searchable: false }
        ];
    }
    function updateGrid() {
        var recent = recentType.checked, rows = recent ? recentRows : currentRows;
        var grid = $('#pr-comparables-grid');
        // Start each collection with its default sort and scroll position.
        if (grid.data('pep-grid')) grid.pepGrid('destroy');
        grid.pepGrid({ data: rows, columns: gridColumns(), pageable: false, height: '510px', showSearch: false, resizable: false, autozoomable: false, exportToExcel: false, exportToPdf: false, defaultSort: [{ field: 'Distance', dir: 'asc' }] });
        document.getElementById('pr-comparables-grid').setAttribute('aria-label', (recent ? 'Recent' : 'Current') + ' comparable vehicle listings');
        document.getElementById('pr-comparables-caption').textContent = (recent ? 'Recent' : 'Current') + ' · ' + integer.format(rows.length) + (rows.length === 1 ? ' competitor within ' : ' competitors within ') + comparablesRadius + ' miles';
        var explanation = recent
            ? 'Recent shows listing history for similar cars and can include earlier advertisements; it does not confirm a sale. Current shows cars advertised for sale now.'
            : 'Current shows similar cars advertised for sale now, so you can compare your price with today\'s asking prices. Recent shows listing history that can include earlier advertisements.';
        document.getElementById('pr-comparables-description').textContent = explanation + ' For example, imagine the same car advertised at $18,000 earlier and $20,000 now. Those are two asking prices for one car; they do not prove it sold for $18,000.';
    }
    function showResult(payload, request) {
        var data = payload.data || payload.Data;
        if (!data || typeof data !== 'object') throw new Error('The API did not return price research data.');
        // Find the subject's identity in the original response before removing it
        // from the comparable lists. The complete provider payload stays in the API response.
        var returnedCurrent = makeRows(data.comparables && data.comparables.listings, request);
        var returnedRecent = makeRows(data.recent_comparables && data.recent_comparables.listings, request);
        var subject = returnedCurrent.concat(returnedRecent).filter(function (r) { return r.IsSubject; })[0];
        currentRows = returnedCurrent.filter(function (r) { return r.IsLocal; });
        recentRows = returnedRecent.filter(function (r) { return r.IsLocal; });
        var priced = currentRows.filter(function (r) { return r.Price !== null && r.Price > 0; });
        document.getElementById('pr-vehicle-title').textContent = subject && subject.Vehicle ? subject.Vehicle : 'Price research · ' + request.Vin;
        document.getElementById('pr-request-summary').textContent = request.Vin + ' · ' + integer.format(request.Miles) + ' miles · ZIP ' + request.Zip + ' · ' + request.Radius + '-mile requested radius · Independent dealer';
        document.getElementById('pr-prediction').textContent = formatPrice(numeric(data.marketcheck_price));
        document.getElementById('pr-current-count').textContent = integer.format(currentRows.length);
        document.getElementById('pr-local-mean').textContent = priced.length ? formatPrice(priced.reduce(function (sum, row) { return sum + row.Price; }, 0) / priced.length) : '—';
        document.getElementById('pr-recent-count').textContent = integer.format(recentRows.length);
        document.getElementById('pr-radius-note').textContent = 'Showing competitors within ' + request.Radius + ' miles of ZIP ' + request.Zip + '. The subject VIN, listings outside the radius and unknown distances are excluded from the comparable lists. ' + (currentRows.length === 0 ? 'No current comparables match this radius. ' : '') + 'The current asking-price mean uses ' + priced.length + ' priced current comparables without mileage or condition adjustments. The MarketCheck estimate is the provider prediction.';
        comparablesRadius = request.Radius; currentType.checked = true; recentType.checked = false;
        results.hidden = false; empty.hidden = true; updateGrid();
        root.dispatchEvent(new CustomEvent('pr:research-result', { detail: { expectedSellingPriceUsd: numeric(data.marketcheck_price), demand: payload.demand } }));
    }
    [currentType, recentType].forEach(function (option) { option.addEventListener('change', function () { if (option.checked && !results.hidden) updateGrid(); }); });
    document.getElementById('pr-vin').addEventListener('input', function () { this.value = this.value.toUpperCase().replace(/\s/g, '').slice(0, 17); });
    form.addEventListener('submit', function (event) {
        event.preventDefault(); if (submit.disabled) return;
        root.dispatchEvent(new CustomEvent('pr:research-start'));
        if (!form.reportValidity()) return;
        if (!$ || !$.fn.pepGrid) { error.textContent = 'The grid could not be loaded. Refresh the page and try again.'; error.hidden = false; return; }
        var vin = document.getElementById('pr-vin').value.trim().toUpperCase(), suppliedKey = clientResearch ? '' : document.getElementById('pr-api-key').value;
        var apiKey = accountKey(suppliedKey), useDefault = defaultApiKeyEnabled && suppliedKey.trim() === '';
        if (!/^[A-HJ-NPR-Z0-9]{17}$/.test(vin)) { error.textContent = 'Enter a valid 17-character VIN without I, O, or Q.'; error.hidden = false; return; }
        if (!apiKey && !useDefault) { error.textContent = 'Enter your AutoDealer.dev account API key.'; error.hidden = false; return; }
        var keyError = apiKey ? accountKeyError(apiKey) : null;
        if (keyError) { error.textContent = keyError; error.hidden = false; return; }
        var headers = { 'Accept': 'application/json', 'Content-Type': 'application/json' };
        if (useDefault) {
            var verificationToken = form.querySelector('input[name="__RequestVerificationToken"]');
            if (!verificationToken || !verificationToken.value) { error.textContent = 'Refresh the page and sign in again to use your account.'; error.hidden = false; return; }
            headers['X-Research-Verification-Token'] = verificationToken.value;
            if (clientResearch) {
                if (!clientKey || !clientKey.value) return;
                headers['X-Research-Workspace'] = 'account';
                headers['X-Research-Key-Id'] = clientKey.value;
            }
        } else headers.Authorization = 'Bearer ' + apiKey;
        var request = { Vin: vin, Miles: Number(document.getElementById('pr-miles').value), Zip: document.getElementById('pr-zip').value.trim(), Radius: Number(document.getElementById('pr-radius').value) };
        var requestRevision = ++researchRevision;
        var controller = typeof AbortController === 'function' ? new AbortController() : null, timeout = controller ? window.setTimeout(function () { controller.abort(); }, 75000) : null;
        researchController = controller;
        error.hidden = true; results.hidden = true; empty.hidden = false; setBusy(true);
        fetch(root.getAttribute('data-api-url'), { method: 'POST', headers: headers, body: JSON.stringify(request), credentials: 'same-origin', cache: 'no-store', signal: controller ? controller.signal : undefined })
            .then(function (response) { return response.text().then(function (body) { if (requestRevision !== researchRevision) return; var payload; try { payload = JSON.parse(body); } catch (ignore) { payload = null; } if (!response.ok) throw new Error(errorMessage(payload, 'The request could not be completed (HTTP ' + response.status + ').')); showResult(payload, request); }); })
            .catch(function (failure) { if (requestRevision !== researchRevision) return; root.dispatchEvent(new CustomEvent('pr:research-error')); error.textContent = failure.name === 'AbortError' ? 'The request timed out. Please try again.' : failure.message || 'The request could not be completed.'; error.hidden = false; })
            .then(function () { if (timeout !== null) window.clearTimeout(timeout); if (requestRevision === researchRevision) { researchController = null; setBusy(false); } });
    });
    ['pr-vin', 'pr-miles', 'pr-zip', 'pr-radius'].forEach(function (id) {
        document.getElementById(id).addEventListener('input', function () {
            var hadResults = !results.hidden || submit.disabled;
            researchRevision++; if (researchController) researchController.abort(); researchController = null;
            results.hidden = true; empty.hidden = false; setBusy(false);
            if (hadResults) { error.textContent = 'Vehicle details changed. Run price research again to update the results.'; error.hidden = false; }
        });
    });
}(window.jQuery));
