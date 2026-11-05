(function () {
    'use strict';
    var root = document.getElementById('price-research'), panel = document.getElementById('pr-auction');
    if (!root || !panel) return;
    var form = document.getElementById('pr-auction-form');
    var resale = document.getElementById('pr-bid-resale'), current = document.getElementById('pr-bid-current');
    var status = document.getElementById('pr-bid-status'), error = document.getElementById('pr-bid-error');
    var revision = 0, delay = null, deadline = null, controller = null, active = false;
    var money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 0, maximumFractionDigits: 2 });
    function element(id) { return document.getElementById(id); }
    function text(id, value) { element(id).textContent = value; }
    function number(value) { return typeof value === 'number' && Number.isFinite(value) ? value : null; }
    function amount(id, value) { text(id, number(value) === null ? '—' : money.format(value)); }
    function busy(value) { panel.setAttribute('aria-busy', value ? 'true' : 'false'); }
    function cancel() {
        revision++;
        if (delay !== null) window.clearTimeout(delay);
        if (deadline !== null) window.clearTimeout(deadline);
        delay = deadline = null;
        if (controller) controller.abort();
        controller = null;
        busy(false);
    }
    function clearOutput() {
        ['pr-bid-maximum', 'pr-bid-fee', 'pr-bid-transport', 'pr-bid-target', 'pr-bid-max-investment', 'pr-bid-max-profit', 'pr-bid-break-even', 'pr-bid-current-investment', 'pr-bid-current-profit'].forEach(function (id) { text(id, '—'); });
        ['pr-bid-costs', 'pr-bid-at-maximum', 'pr-bid-at-current'].forEach(function (id) { element(id).hidden = true; });
        error.hidden = true; error.textContent = ''; status.textContent = '';
    }
    function reset() { cancel(); clearOutput(); active = false; resale.value = ''; current.value = ''; panel.hidden = true; }
    function fail(message) { clearOutput(); error.textContent = message; error.hidden = false; }
    function readInputs() {
        var sellingText = resale.value.trim(), currentText = current.value.trim();
        var sellingPrice = sellingText ? Number(sellingText) : NaN, currentBid = currentText ? Number(currentText) : null;
        if (!Number.isFinite(sellingPrice) || sellingPrice <= 0 || sellingPrice > 2000000) return { error: 'Enter an expected resale price greater than $0 and no more than $2,000,000.' };
        if (currentBid !== null && (!Number.isFinite(currentBid) || currentBid < 0 || currentBid > 2000000)) return { error: 'Enter a current hammer bid between $0 and $2,000,000, or leave it blank.' };
        return { sellingPrice: sellingPrice, currentBid: currentBid };
    }
    function render(data) {
        if (!data || (data.status !== 'ready' && data.status !== 'no_bid') || number(data.auctionFeeUsd) === null || number(data.transportUsd) === null || number(data.minimumProfitUsd) === null || number(data.roundingIncrementUsd) === null || data.roundingIncrementUsd <= 0) throw new Error('The bid calculation returned an incomplete result. Please try again.');
        amount('pr-bid-fee', data.auctionFeeUsd); amount('pr-bid-transport', data.transportUsd); amount('pr-bid-target', data.minimumProfitUsd);
        element('pr-bid-costs').hidden = false;
        text('pr-bid-scope', data.costScope || 'Auction fee and transport only; profit before other expenses.');
        if (data.status === 'no_bid') {
            text('pr-bid-maximum', 'No bid room');
            status.textContent = 'No positive hammer bid in ' + money.format(data.roundingIncrementUsd) + ' increments meets this profit target.';
        } else {
            if (number(data.maximumHammerBidUsd) === null || number(data.maximumTotalInvestmentUsd) === null || number(data.profitAtMaximumUsd) === null || number(data.breakEvenHammerBidUsd) === null) throw new Error('The bid calculation returned an incomplete result. Please try again.');
            amount('pr-bid-maximum', data.maximumHammerBidUsd); amount('pr-bid-max-investment', data.maximumTotalInvestmentUsd); amount('pr-bid-max-profit', data.profitAtMaximumUsd); amount('pr-bid-break-even', data.breakEvenHammerBidUsd);
            element('pr-bid-at-maximum').hidden = false;
            status.textContent = 'Maximum hammer price that meets your ' + money.format(data.minimumProfitUsd) + ' profit target before other expenses.';
        }
        if (number(data.currentHammerBidUsd) !== null) {
            if (number(data.currentTotalInvestmentUsd) === null || number(data.currentProfitUsd) === null || typeof data.currentBidWithinLimit !== 'boolean') throw new Error('The current-bid calculation returned an incomplete result. Please try again.');
            amount('pr-bid-current-investment', data.currentTotalInvestmentUsd); amount('pr-bid-current-profit', data.currentProfitUsd);
            var verdict = element('pr-bid-current-verdict');
            if (data.currentBidWithinLimit) verdict.textContent = 'At a ' + money.format(data.currentHammerBidUsd) + ' hammer bid, estimated profit meets your minimum target before other expenses.';
            else if (data.currentProfitUsd < data.minimumProfitUsd) verdict.textContent = 'At a ' + money.format(data.currentHammerBidUsd) + ' hammer bid, estimated profit is ' + money.format(data.minimumProfitUsd - data.currentProfitUsd) + ' below your minimum target.';
            else verdict.textContent = data.status === 'no_bid' ? 'No positive hammer bid in ' + money.format(data.roundingIncrementUsd) + ' increments is available under this budget.' : 'Your ' + money.format(data.currentHammerBidUsd) + ' hammer bid exceeds the maximum after rounding down to ' + money.format(data.roundingIncrementUsd) + ' increments.';
            verdict.classList.toggle('is-below-target', !data.currentBidWithinLimit);
            element('pr-bid-at-current').hidden = false;
        }
    }
    function calculate(values, requestRevision) {
        if (!active || requestRevision !== revision) return;
        delay = null;
        controller = typeof AbortController === 'function' ? new AbortController() : null;
        var requestController = controller;
        var token = form.querySelector('input[name="__RequestVerificationToken"]');
        if (!token || !token.value) { fail('Refresh the page and sign in to Majordome again before calculating a bid.'); return; }
        var body = '__RequestVerificationToken=' + encodeURIComponent(token.value) + '&ExpectedSellingPriceUsd=' + encodeURIComponent(values.sellingPrice);
        if (values.currentBid !== null) body += '&CurrentHammerBidUsd=' + encodeURIComponent(values.currentBid);
        busy(true); status.textContent = 'Calculating auction bid…';
        var requestDeadline = window.setTimeout(function () {
            if (requestRevision !== revision) return;
            revision++; if (requestController) requestController.abort();
            controller = null; deadline = null; busy(false); fail('The bid calculation timed out. Edit an amount to try again.');
        }, 20000);
        deadline = requestDeadline;
        fetch(panel.getAttribute('data-bid-url'), { method: 'POST', headers: { 'Accept': 'application/json', 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8', 'X-Requested-With': 'XMLHttpRequest' }, body: body, credentials: 'same-origin', cache: 'no-store', signal: requestController ? requestController.signal : undefined })
            .then(function (response) { return response.text().then(function (bodyText) {
                if (requestRevision !== revision) return;
                if (response.status === 401 || response.status === 403 || response.redirected) throw new Error('Sign in again to calculate a bid.');
                var data; try { data = JSON.parse(bodyText); } catch (ignore) { throw new Error('The bid calculation could not be read. Sign in to Majordome again and retry.'); }
                if (!response.ok) throw new Error(data.error || 'The bid calculation could not be completed.');
                render(data);
            }); })
            .catch(function (failure) { if (requestRevision === revision) fail(failure.message || 'The bid calculation could not be completed.'); })
            .then(function () { window.clearTimeout(requestDeadline); if (requestRevision === revision) { deadline = null; controller = null; busy(false); } });
    }
    function update(immediate) {
        if (!active) return;
        cancel(); clearOutput();
        var values = readInputs();
        if (values.error) { status.textContent = values.error; return; }
        var requestRevision = revision;
        if (immediate) calculate(values, requestRevision);
        else { status.textContent = 'Updating bid calculation…'; delay = window.setTimeout(function () { calculate(values, requestRevision); }, 300); }
    }
    resale.addEventListener('input', function () { update(false); });
    current.addEventListener('input', function () { update(false); });
    form.addEventListener('submit', function (event) { event.preventDefault(); update(true); });
    ['pr-vin', 'pr-miles', 'pr-zip', 'pr-radius'].forEach(function (id) { element(id).addEventListener('input', reset); });
    root.addEventListener('pr:research-start', reset);
    root.addEventListener('pr:research-error', reset);
    root.addEventListener('pr:research-result', function (event) {
        reset(); active = true; panel.hidden = false;
        var estimate = number(event.detail && event.detail.expectedSellingPriceUsd);
        resale.value = estimate !== null && estimate > 0 ? String(estimate) : '';
        update(true);
    });
    reset();
}());
