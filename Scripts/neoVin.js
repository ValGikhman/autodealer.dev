(function () {
    'use strict';
    var root = document.getElementById('neo-vin');
    if (!root) return;
    var form = document.getElementById('neo-vin-form');
    var defaultApiKeyEnabled = root.getAttribute('data-default-api-key-enabled') === 'true';
    var clientResearch = root.getAttribute('data-client-research') === 'true';
    var clientKey = document.getElementById('client-research-key');
    var submit = document.getElementById('nv-submit');
    var status = document.getElementById('nv-status');
    var error = document.getElementById('nv-error');
    var results = document.getElementById('nv-results');
    var empty = document.getElementById('nv-empty');
    var sections = document.getElementById('nv-spec-sections');
    var rows = [];
    function setStatus(text, state) { status.className = 'pr-status' + (state ? ' is-' + state : ''); document.getElementById('nv-status-text').textContent = text; }
    function busy(value) { submit.disabled = value; submit.querySelector('span').textContent = value ? 'Decoding…' : 'Decode VIN'; root.querySelector('.pr-results-panel').setAttribute('aria-busy', value ? 'true' : 'false'); document.getElementById('nv-progress').hidden = !value; }
    function message(payload, fallback) { var nested = payload && (payload.error || payload.Error); return payload && (payload.message || payload.Message || (nested && (nested.message || nested.Message))) || fallback; }
    function accountKey(value) { return value.trim().replace(/^Bearer\s+/i, '').trim(); }
    function accountKeyError(value) {
        if (/^mc_(live|test)_/i.test(value)) return 'Use your AutoDealer.dev account API key here. The MarketCheck provider key is configured on the server.';
        var parts = value.split('.', 2);
        return parts.length !== 2 || parts[0].indexOf('ad_') !== 0 ? 'Enter the complete AutoDealer.dev account API key, including the secret after the dot (ad_keyId.secret).' : null;
    }
    function uniqueField(name) { var values = rows.filter(function (row) { return row.Field.toLowerCase() === name; }).map(function (row) { return row.Value; }).filter(function (value, i, all) { return value && value !== 'null' && all.indexOf(value) === i; }); return values.length === 1 ? values[0] : null; }
    function renderSpecifications() {
        var tree = { children: [], rows: [] };
        rows.forEach(function (row) {
            var node = tree;
            var path = row.Category.split(/\s*\/\s*/).filter(function (part) { return part.trim(); });
            if (!path.length) path = ['Specifications'];
            path.forEach(function (label) {
                label = label.trim();
                var child = node.children.filter(function (item) { return item.label === label; })[0];
                if (!child) { child = { label: label, children: [], rows: [] }; node.children.push(child); }
                node = child;
            });
            node.rows.push(row);
        });
        function renderSection(node, depth) {
            var details = document.createElement('details');
            details.className = 'nv-spec-section' + (depth ? ' is-nested' : '');
            details.open = depth === 0 && node.label.toLowerCase() === 'vehicle';
            var summary = document.createElement('summary');
            var label = document.createElement('span');
            label.className = 'nv-section-label'; label.textContent = node.label;
            summary.appendChild(label); details.appendChild(summary);
            var content = document.createElement('div'); content.className = 'nv-section-content';
            if (node.rows.length) {
                var list = document.createElement('dl'); list.className = 'nv-spec-list';
                node.rows.forEach(function (row) {
                    var pair = document.createElement('div'); pair.className = 'nv-spec-row';
                    var field = document.createElement('dt'); field.textContent = row.Field || 'Value';
                    var value = document.createElement('dd'); value.textContent = row.Value;
                    pair.appendChild(field); pair.appendChild(value); list.appendChild(pair);
                });
                content.appendChild(list);
            }
            node.children.forEach(function (child) { content.appendChild(renderSection(child, depth + 1)); });
            details.appendChild(content);
            return details;
        }
        sections.replaceChildren();
        tree.children.sort(function (a, b) { return (a.label.toLowerCase() === 'vehicle' ? -1 : 0) - (b.label.toLowerCase() === 'vehicle' ? -1 : 0); });
        tree.children.forEach(function (node) { sections.appendChild(renderSection(node, 0)); });
        if (!rows.length) { var note = document.createElement('p'); note.className = 'nv-no-specs'; note.textContent = 'No specification fields were supplied for this VIN.'; sections.appendChild(note); }
    }
    document.getElementById('nv-vin').addEventListener('input', function () { this.value = this.value.toUpperCase().replace(/\s/g, '').slice(0, 17); });
    form.addEventListener('submit', function (event) {
        event.preventDefault();
        if (submit.disabled || !form.reportValidity()) return;
        var vin = document.getElementById('nv-vin').value.trim().toUpperCase();
        var suppliedKey = clientResearch ? '' : document.getElementById('nv-api-key').value;
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
        var abort = typeof AbortController === 'function' ? new AbortController() : null;
        var timer = abort ? window.setTimeout(function () { abort.abort(); }, 75000) : null;
        error.hidden = true; results.hidden = true; empty.hidden = false;
        document.getElementById('nv-vehicle-title').textContent = 'Vehicle details';
        setStatus('Decoding vehicle specifications…', 'loading'); busy(true);
        fetch(root.getAttribute('data-api-url'), { method: 'POST', headers: headers, body: JSON.stringify({ Vin: vin }), credentials: 'same-origin', cache: 'no-store', signal: abort ? abort.signal : undefined })
            .then(function (response) { return response.text().then(function (body) {
                var payload; try { payload = JSON.parse(body); } catch (ignore) { payload = null; }
                if (!response.ok) throw new Error(message(payload, 'VIN decoding could not be completed (HTTP ' + response.status + ').'));
                var specs = payload && (payload.specifications || payload.Specifications);
                if (!Array.isArray(specs)) throw new Error('The API did not return a specification record.');
                var data = payload.data || payload.Data || {};
                rows = specs.map(function (row) { return { Category: String(row.Category == null ? row.category == null ? 'Specifications' : row.category : row.Category), Field: String(row.Field == null ? row.field == null ? '' : row.field : row.Field), Value: String(row.Value == null ? row.value == null ? 'Not supplied' : row.value : row.Value) }; });
                // Identity comes from NeoVIN's top-level vehicle record. Other
                // categories can also contain fields named Model (for example
                // an engine or equipment model), so do not mix those values.
                var identity = ['year', 'make', 'model', 'trim'].map(function (name) {
                    var value = data[name];
                    return typeof value === 'string' || typeof value === 'number' ? String(value).trim() : uniqueField(name);
                }).filter(Boolean).join(' ');
                document.getElementById('nv-vehicle-title').textContent = identity || 'Vehicle details';
                renderSpecifications();
                results.hidden = false; empty.hidden = true;
                setStatus('Decoded · ' + vin, 'success');
            }); })
            .catch(function (failure) { error.textContent = failure.name === 'AbortError' ? 'VIN decoding timed out. Please try again.' : failure.message || 'VIN decoding could not be completed.'; error.hidden = false; setStatus('VIN decoding could not be completed', 'error'); })
            .then(function () { if (timer !== null) window.clearTimeout(timer); busy(false); });
    });
}());
