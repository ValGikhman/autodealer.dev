;(function ($) {
    'use strict';

    $(function () {
        var navBadge = document.getElementById('admin-mail-nav-badge');

        function setCount(element, count, suffix) {
            if (!element) return;
            var value = Number(count || 0);
            element.textContent = suffix ? value + ' ' + suffix : String(value);
            element.setAttribute('aria-label', value + ' unread inbox ' + (value === 1 ? 'message' : 'messages'));
            element.hidden = false;
        }

        function updateUnreadCount(count) {
            setCount(navBadge, count, '');
            var pageCount = document.getElementById('admin-inbox-page-count');
            if (pageCount) setCount(pageCount, count, count === 1 ? 'unread' : 'unread');
        }

        if (navBadge && navBadge.dataset.url) {
            $.getJSON(navBadge.dataset.url).done(function (response) {
                setCount(navBadge, response.Count, '');
            });
        }

        var gridElement = document.getElementById('admin-mail-grid');
        if (!gridElement || !window.bootstrap || !$.fn.pepGrid) return;

        var $grid = $(gridElement);
        var modalElement = document.getElementById('admin-mail-preview-modal');
        var previewModal = window.bootstrap.Modal.getOrCreateInstance(modalElement);
        var previewFrame = document.getElementById('admin-mail-preview-frame');
        var previewTitle = document.getElementById('admin-mail-preview-title');
        var previewSubtitle = document.getElementById('admin-mail-preview-subtitle');
        var previewError = document.getElementById('admin-inbox-preview-error');
        var deleteOpen = document.getElementById('admin-mail-delete-open');
        var deleteModalElement = document.getElementById('admin-mail-delete-modal');
        var deleteModal = window.bootstrap.Modal.getOrCreateInstance(deleteModalElement);
        var deleteForm = document.getElementById('admin-mail-delete-form');
        var deleteSubmit = document.getElementById('admin-mail-delete-submit');
        var deleteError = document.getElementById('admin-mail-delete-error');
        var deleteSubject = document.getElementById('admin-mail-delete-subject');
        var deleteDetail = document.getElementById('admin-mail-delete-detail');
        var newEmailOpen = document.getElementById('admin-inbox-new-email');
        var composeModalElement = document.getElementById('admin-inbox-compose-modal');
        var composeModal = window.bootstrap.Modal.getOrCreateInstance(composeModalElement);
        var composeForm = document.getElementById('admin-inbox-compose-form');
        var composeTo = document.getElementById('admin-inbox-compose-to');
        var composeSubject = document.getElementById('admin-inbox-compose-subject');
        var composeBody = document.getElementById('admin-inbox-compose-body');
        var composeSubmit = document.getElementById('admin-inbox-compose-submit');
        var composeError = document.getElementById('admin-inbox-compose-error');
        var pageError = document.getElementById('admin-inbox-error');
        var totalCount = document.getElementById('admin-inbox-total-count');
        var inboxData = [];
        var previewRequest = null;
        var previewSequence = 0;
        var activeMessage = null;
        var pendingDelete = null;
        var showDeleteAfterPreview = false;

        if ($.fn.pepEdit) {
            $(composeBody).pepEdit({
                height: 320,
                placeholder: 'Write the email message...'
            });
        }

        function setButtonLabel(button, label) {
            var text = button.querySelector('span');
            if (text) text.textContent = label;
            else button.textContent = label;
        }

        function replacePreviewFrame(html) {
            var replacement = document.createElement('iframe');
            replacement.id = 'admin-mail-preview-frame';
            replacement.setAttribute('sandbox', '');
            replacement.setAttribute('referrerpolicy', 'no-referrer');
            replacement.setAttribute('title', 'Inbox email preview');
            if (html == null) replacement.setAttribute('src', 'about:blank');
            else replacement.srcdoc = html;
            previewFrame.parentNode.replaceChild(replacement, previewFrame);
            previewFrame = replacement;
        }

        function markUnreadRows() {
            $grid.find('tbody tr').each(function () {
                var item = $grid.pepGrid('getDataItem', this);
                this.classList.toggle('admin-inbox-unread-row', !!(item && item.IsUnread));
            });
        }

        function initializeGrid(data) {
            inboxData = data || [];
            $grid.pepGrid({
                data: inboxData,
                height: null,
                pageable: false,
                pageSize: 100,
                resizable: true,
                autozoomable: true,
                showSearch: false,
                exportToExcel: false,
                exportToPdf: false,
                defaultSort: [{ field: 'ReceivedSort', dir: 'desc' }],
                onDataBound: markUnreadRows,
                onCellClick: function (detail) {
                    var action = detail.event.target.closest('[data-email-action]');
                    if (!action) return;
                    detail.event.preventDefault();
                    detail.event.stopPropagation();
                    if (action.dataset.emailAction === 'delete') prepareDelete(detail.dataItem, false);
                    else openMessage(detail.dataItem);
                },
                onCellDblClick: function (detail) { openMessage(detail.dataItem); },
                onRowDblClick: function (detail) { openMessage(detail.dataItem); },
                columns: [
                    { field: 'Received', title: 'Received', width: '24%' },
                    { field: 'From', title: 'From', width: '26%' },
                    { field: 'Subject', title: 'Subject', width: '34%' },
                    { field: 'View', title: 'Message', width: '16%', sortable: false, filterable: false, template: '#admin-inbox-actions-template' }
                ]
            });
        }

        function openMessage(item) {
            var requestSequence = ++previewSequence;
            if (previewRequest) previewRequest.abort();
            activeMessage = item;
            deleteOpen.disabled = true;
            previewTitle.textContent = item.Subject || 'Email message';
            previewSubtitle.textContent = 'Loading message from ' + (item.From || 'unknown sender');
            previewError.hidden = true;
            replacePreviewFrame(null);
            previewModal.show();

            previewRequest = $.getJSON($grid.data('message-url'), { uid: item.Uid })
                .done(function (response) {
                    if (requestSequence !== previewSequence) return;
                    previewTitle.textContent = response.Subject || 'Email message';
                    previewSubtitle.textContent = 'Received ' + (response.Received || '') + ' from ' + (response.From || 'unknown sender');
                    replacePreviewFrame(response.HtmlBody || '');
                    item.IsUnread = false;
                    $grid.pepGrid('setData', inboxData);
                    updateUnreadCount(response.UnreadCount);
                    deleteOpen.disabled = false;
                })
                .fail(function (xhr, status) {
                    if (status === 'abort' || requestSequence !== previewSequence) return;
                    var response = xhr.responseJSON || {};
                    previewError.textContent = response.Message || 'The message could not be loaded.';
                    previewError.hidden = false;
                })
                .always(function () {
                    if (requestSequence === previewSequence) previewRequest = null;
                });
        }

        function prepareDelete(item, fromPreview) {
            if (!item) return;
            pendingDelete = item;
            deleteSubject.textContent = pendingDelete.Subject || '(No subject)';
            deleteDetail.textContent = 'From ' + (pendingDelete.From || 'unknown sender') + ' — ' + (pendingDelete.Received || 'date unavailable');
            deleteError.hidden = true;
            deleteError.textContent = '';
            if (fromPreview) {
                showDeleteAfterPreview = true;
                previewModal.hide();
            } else {
                deleteModal.show();
            }
        }

        deleteOpen.addEventListener('click', function () {
            if (!activeMessage || deleteOpen.disabled) return;
            prepareDelete(activeMessage, true);
        });

        newEmailOpen.addEventListener('click', function () {
            composeForm.reset();
            composeError.hidden = true;
            composeError.textContent = '';
            composeSubmit.disabled = false;
            setButtonLabel(composeSubmit, 'Send email');
            var editorTemplate = $('<div>')
                .append($('<p>').append($('<strong>').text('Dear Customer,')))
                .append($('<p>').append('<br>'))
                .html();
            if ($.fn.pepEdit) $(composeBody).pepEdit('value', editorTemplate);
            else composeBody.value = editorTemplate;
            composeModal.show();
            window.setTimeout(function () { composeTo.focus(); }, 180);
        });

        composeForm.addEventListener('submit', function (event) {
            event.preventDefault();
            if (!composeForm.checkValidity()) {
                composeForm.reportValidity();
                return;
            }

            var body = $.fn.pepEdit ? $(composeBody).pepEdit('value') : composeBody.value;
            composeBody.value = body || '';
            var data = $(composeForm).serializeArray();
            var token = document.querySelector('.dashboard-antiforgery input[name="__RequestVerificationToken"]');
            data.push({ name: '__RequestVerificationToken', value: token ? token.value : '' });
            composeSubmit.disabled = true;
            setButtonLabel(composeSubmit, 'Sending...');
            composeError.hidden = true;

            $.ajax({
                url: $grid.data('send-url'),
                method: 'POST',
                dataType: 'json',
                data: data
            }).done(function () {
                composeModal.hide();
            }).fail(function (xhr) {
                var response = xhr.responseJSON || {};
                composeError.textContent = response.Message || 'The email could not be sent.';
                composeError.hidden = false;
            }).always(function () {
                composeSubmit.disabled = false;
                setButtonLabel(composeSubmit, 'Send email');
            });
        });

        deleteForm.addEventListener('submit', function (event) {
            event.preventDefault();
            if (!pendingDelete) return;

            var token = document.querySelector('.dashboard-antiforgery input[name="__RequestVerificationToken"]');
            deleteSubmit.disabled = true;
            deleteSubmit.textContent = 'Moving...';
            deleteError.hidden = true;

            $.ajax({
                url: $grid.data('delete-url'),
                method: 'POST',
                dataType: 'json',
                data: {
                    uid: pendingDelete.Uid,
                    __RequestVerificationToken: token ? token.value : ''
                }
            }).done(function (response) {
                var deletedUid = String(pendingDelete.Uid);
                inboxData = inboxData.filter(function (message) {
                    return String(message.Uid) !== deletedUid;
                });
                $grid.pepGrid('setData', inboxData);
                totalCount.textContent = inboxData.length + (inboxData.length === 1 ? ' message' : ' messages');
                updateUnreadCount(response.UnreadCount);
                pendingDelete = null;
                deleteModal.hide();
            }).fail(function (xhr) {
                var response = xhr.responseJSON || {};
                deleteError.textContent = response.Message || 'The message could not be moved to Trash.';
                deleteError.hidden = false;
            }).always(function () {
                deleteSubmit.disabled = false;
                deleteSubmit.textContent = 'Move to Trash';
            });
        });

        $(modalElement).on('hidden.bs.modal', function () {
            previewSequence += 1;
            if (previewRequest) {
                previewRequest.abort();
                previewRequest = null;
            }
            replacePreviewFrame(null);
            previewError.hidden = true;
            activeMessage = null;
            deleteOpen.disabled = true;
            if (showDeleteAfterPreview) {
                showDeleteAfterPreview = false;
                window.setTimeout(function () { deleteModal.show(); }, 0);
            }
        });

        $(deleteModalElement).on('hidden.bs.modal', function () {
            pendingDelete = null;
            deleteError.hidden = true;
            deleteError.textContent = '';
        });

        $(composeModalElement).on('hidden.bs.modal', function () {
            composeForm.reset();
            if ($.fn.pepEdit) $(composeBody).pepEdit('value', '');
            else composeBody.value = '';
            composeError.hidden = true;
            composeError.textContent = '';
            composeSubject.value = '';
        });

        $.getJSON($grid.data('url'))
            .done(function (response) {
                var messages = response.Data || [];
                initializeGrid(messages);
                updateUnreadCount(response.UnreadCount);
                totalCount.textContent = response.TotalCount + (response.TotalCount === 1 ? ' message' : ' messages');
            })
            .fail(function (xhr) {
                var response = xhr.responseJSON || {};
                pageError.textContent = response.Message || 'The inbox could not be loaded.';
                pageError.hidden = false;
                totalCount.textContent = 'Inbox unavailable';
                initializeGrid([]);
            });
    });
})(jQuery);
