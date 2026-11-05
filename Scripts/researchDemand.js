(function () {
    'use strict';
    var root = document.getElementById('price-research');
    if (!root) return;

    var count = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
    var days = new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 });

    function text(id, value) { document.getElementById(id).textContent = value; }
    function valid(value) { return typeof value === 'number' && Number.isFinite(value) && value >= 0; }

    root.addEventListener('pr:research-result', function (event) {
        var demand = (event.detail && event.detail.demand) || {};
        text('pr-demand-days', demand.status === 'ready' && valid(demand.daysSupply) ? days.format(demand.daysSupply) + ' days' : 'Unavailable');
        text('pr-demand-active', valid(demand.activeCount) ? count.format(demand.activeCount) : '—');
        text('pr-demand-sales', valid(demand.sales45Days) ? count.format(demand.sales45Days) : '—');
        text('pr-demand-message', demand.message || 'Local demand data is unavailable. Pricing remains available.');
        var timestamp = demand.checkedAtUtc ? new Date(demand.checkedAtUtc) : null;
        text('pr-demand-time', timestamp && !isNaN(timestamp.getTime()) ? 'Checked ' + timestamp.toLocaleString() : '');
    });
})();
