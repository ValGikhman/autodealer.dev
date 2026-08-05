(function ($, window, document) {
    'use strict';

    $(function () {
        var modalElement = document.getElementById('dashboard-record-modal');
        if (!modalElement || !window.bootstrap || !$.fn.pepGrid) return;

        var recordModal = window.bootstrap.Modal.getOrCreateInstance(modalElement);
        var sectionsElement = document.getElementById('dashboard-record-sections');
        var actionsElement = document.getElementById('dashboard-record-actions');

        function responseData(response) { return response && response.Data ? response.Data : []; }
        function text(id, value) { document.getElementById(id).textContent = value == null ? '' : value; }
        function valueOrEmpty(value) { return value == null || String(value).trim() === '' ? 'Not provided' : String(value); }
        function initials(value) {
            var words = valueOrEmpty(value).split(/\s+/).filter(Boolean);
            if (!words.length) return 'AD';
            return (words[0].charAt(0) + (words.length > 1 ? words[words.length - 1].charAt(0) : '')).toUpperCase();
        }
        function phoneHref(value) {
            var normalized = String(value || '').replace(/[^\d+]/g, '');
            if (normalized.charAt(0) === '+') return 'tel:+' + normalized.substring(1).replace(/\+/g, '');
            return 'tel:' + normalized.replace(/\+/g, '');
        }
        function fieldValue(field) {
            var value = valueOrEmpty(field.value);
            if (value === 'Not provided') return document.createTextNode(value);
            if (field.type === 'email') {
                var email = document.createElement('a');
                email.href = 'mailto:' + value;
                email.className = 'dashboard-detail-link';
                email.textContent = value;
                return email;
            }
            if (field.type === 'phone') {
                var phone = document.createElement('a');
                phone.href = phoneHref(value);
                phone.className = 'dashboard-detail-link';
                phone.textContent = value;
                return phone;
            }
            if (field.type === 'url' && /^https?:\/\//i.test(field.href || '')) {
                var website = document.createElement('a');
                website.href = field.href;
                website.target = '_blank';
                website.rel = 'noopener noreferrer';
                website.className = 'dashboard-detail-link';
                website.textContent = value;
                return website;
            }
            return document.createTextNode(value);
        }
        function addSection(title, fields, wide) {
            var section = document.createElement('section');
            section.className = 'dashboard-detail-section' + (wide ? ' dashboard-detail-section-wide' : '');
            var heading = document.createElement('h3');
            heading.textContent = title;
            section.appendChild(heading);
            var list = document.createElement('dl');
            fields.forEach(function (field) {
                var row = document.createElement('div');
                if (field.multiline) row.className = 'dashboard-detail-long';
                var term = document.createElement('dt');
                var description = document.createElement('dd');
                term.textContent = field.label;
                description.appendChild(fieldValue(field));
                row.appendChild(term);
                row.appendChild(description);
                list.appendChild(row);
            });
            section.appendChild(list);
            sectionsElement.appendChild(section);
        }
        function addAction(label, href, kind) {
            if (!href || href === 'tel:') return;
            var action = document.createElement('a');
            action.className = 'dashboard-record-action dashboard-record-action-' + kind;
            action.href = href;
            action.innerHTML = '<span aria-hidden="true">' + (kind === 'phone' ? '&#9742;' : '&#9993;') + '</span>';
            action.appendChild(document.createTextNode(label));
            actionsElement.appendChild(action);
        }
        function prepareModal(item, type) {
            sectionsElement.innerHTML = '';
            actionsElement.innerHTML = '';
            var isCustomer = type === 'customer';
            var status = isCustomer ? item.SubscriptionStatus : item.Status;
            text('dashboard-record-kicker', isCustomer ? 'DEALER ACCOUNT' : 'NEW OPPORTUNITY');
            text('dashboard-record-title', isCustomer ? 'Dealer account profile' : 'Demo request profile');
            text('dashboard-record-subtitle', isCustomer ? 'A complete view of this customer relationship.' : 'Every detail shared by this prospective dealer.');
            text('dashboard-record-name', valueOrEmpty(item.BusinessName));
            text('dashboard-record-caption', isCustomer ? valueOrEmpty(item.ClientNumber) : 'Received ' + valueOrEmpty(item.Received));
            text('dashboard-record-monogram', initials(item.BusinessName));
            text('dashboard-record-status', valueOrEmpty(status));
            addAction('Email ' + valueOrEmpty(item.ContactName), 'mailto:' + valueOrEmpty(item.Email), 'email');
            if (!isCustomer && item.Phone) addAction('Call ' + valueOrEmpty(item.ContactName), phoneHref(item.Phone), 'phone');
            if (isCustomer) {
                addSection('Dealer identity', [
                    { label: 'Business', value: item.BusinessName },
                    { label: 'Client number', value: item.ClientNumber },
                    { label: 'Internal client ID', value: item.ClientId },
                    { label: 'Account created', value: item.Created }
                ]);
                addSection('Primary contact', [
                    { label: 'Name', value: item.ContactName },
                    { label: 'Email', value: item.Email, type: 'email' }
                ]);
                addSection('Subscription', [
                    { label: 'Plan', value: item.PlanName },
                    { label: 'Status', value: item.SubscriptionStatus },
                    { label: 'Period ends', value: item.PeriodEnd },
                    { label: 'Active API keys', value: item.ActiveApiKeyCount }
                ]);
            } else {
                addSection('Primary contact', [
                    { label: 'Name', value: item.ContactName },
                    { label: 'Email', value: item.Email, type: 'email' },
                    { label: 'Phone', value: item.Phone, type: 'phone' },
                    { label: 'Preferred contact', value: item.PreferredContact }
                ]);
                addSection('Dealership profile', [
                    { label: 'Business', value: item.BusinessName },
                    { label: 'Current website', value: item.CurrentWebsite, type: 'url', href: item.WebsiteHref },
                    { label: 'Locations', value: item.LocationCount },
                    { label: 'Inventory size', value: item.InventorySize }
                ]);
                addSection('Request record', [
                    { label: 'Status', value: item.Status },
                    { label: 'Received', value: item.Received },
                    { label: 'Request ID', value: item.RequestId }
                ]);
                addSection('The opportunity', [
                    { label: 'Primary goal', value: item.PrimaryGoal },
                    { label: 'Request', value: item.Message, multiline: true }
                ], true);
            }
            recordModal.show();
        }

        var $customerGrid = $('#customer-grid');
        if ($customerGrid.length) {
            $customerGrid.pepGrid({
                url: $customerGrid.data('url'), schema: { data: responseData }, height: null, pageSize: 100,
                resizable: false, autozoomable: true, exportToExcel: false, exportToPdf: false,
                defaultSort: [{ field: 'CreatedSort', dir: 'desc' }],
                onCellDblClick: function (detail) { prepareModal(detail.dataItem, 'customer'); },
                onRowDblClick: function (detail) { prepareModal(detail.dataItem, 'customer'); },
                columns: [
                    { field: 'BusinessName', title: 'Customer', width: '20%' },
                    { field: 'ClientNumber', title: 'Client number', width: '20%' },
                    { field: 'ContactName', title: 'Contact', width: '20%' },
                    { field: 'Email', title: 'Email', width: '20%' },
                    { field: 'PlanName', title: 'Plan', width: '20%' }
                ]
            });
        }

        var $demoGrid = $('#demo-request-grid');
        if ($demoGrid.length) {
            $demoGrid.pepGrid({
                url: $demoGrid.data('url'), schema: { data: responseData }, height: null, pageSize: 100,
                resizable: false, autozoomable: true, exportToExcel: false, exportToPdf: false,
                defaultSort: [{ field: 'CreatedSort', dir: 'desc' }],
                onCellDblClick: function (detail) { prepareModal(detail.dataItem, 'demo'); },
                onRowDblClick: function (detail) { prepareModal(detail.dataItem, 'demo'); },
                columns: [
                    { field: 'BusinessName', title: 'Dealership', width: '15%' },
                    { field: 'ContactName', title: 'Contact', width: '15%' },
                    { field: 'Email', title: 'Email', width: '15%' },
                    { field: 'Phone', title: 'Phone', width: '15%' },
                    { field: 'Inventory', title: 'Inventory', width: '15%' },
                    { field: 'Status', title: 'Status', width: '15%', template: '#demo-status-template' }
                ]
            });
        }
    });
})(jQuery, window, document);
