(function ($, window, document) {
    'use strict';

    $(function () {
        var modalElement = document.getElementById('dashboard-record-modal');
        if (!modalElement || !window.bootstrap || !$.fn.pepGrid) return;

        var recordModal = window.bootstrap.Modal.getOrCreateInstance(modalElement);
        var sectionsElement = document.getElementById('dashboard-record-sections');
        var actionsElement = document.getElementById('dashboard-record-actions');
        var profileElement = document.getElementById('dashboard-record-profile');
        var emailPreviewElement = document.getElementById('dashboard-email-preview');
        var emailPreviewFrame = document.getElementById('dashboard-email-preview-frame');
        var subgridEditorElement = document.getElementById('dashboard-subgrid-editor');
        var subgridEditForm = document.getElementById('dashboard-subgrid-edit-form');
        var subgridEditError = document.getElementById('dashboard-subgrid-edit-error');
        var subgridEditSave = document.getElementById('dashboard-subgrid-edit-save');
        var subgridEditSaveIcon = document.getElementById('dashboard-subgrid-edit-save-icon');
        var subgridEditNote = document.getElementById('dashboard-subgrid-edit-note');
        var subgridEditCancel = document.getElementById('dashboard-subgrid-edit-cancel');
        var deleteClientOpen = document.getElementById('dashboard-client-delete-open');
        var clientEditFields = document.getElementById('client-edit-fields');
        var clientCreateFields = document.getElementById('client-create-fields');
        var clientCreateResult = document.getElementById('dashboard-client-create-result');
        var apiKeyEditFields = document.getElementById('api-key-edit-fields');
        var subscriptionEditFields = document.getElementById('subscription-edit-fields');
        var keyIssueModalElement = document.getElementById('issue-api-key-modal');
        var keyIssueModal = window.bootstrap.Modal.getOrCreateInstance(keyIssueModalElement);
        var keyIssueForm = document.getElementById('issue-api-key-form');
        var keyIssueName = document.getElementById('issue-api-key-name');
        var keyIssueSubmit = document.getElementById('issue-api-key-submit');
        var keyIssueError = document.getElementById('issue-api-key-error');
        var deleteClientModalElement = document.getElementById('delete-client-modal');
        var deleteClientModal = window.bootstrap.Modal.getOrCreateInstance(deleteClientModalElement);
        var deleteClientForm = document.getElementById('delete-client-form');
        var deleteClientConfirm = document.getElementById('delete-client-confirm');
        var deleteClientSubmit = document.getElementById('delete-client-submit');
        var deleteClientError = document.getElementById('delete-client-error');
        var deleteOpportunityModalElement = document.getElementById('delete-opportunity-modal');
        var deleteOpportunityModal = window.bootstrap.Modal.getOrCreateInstance(deleteOpportunityModalElement);
        var deleteOpportunityForm = document.getElementById('delete-opportunity-form');
        var deleteOpportunityConfirm = document.getElementById('delete-opportunity-confirm');
        var deleteOpportunitySubmit = document.getElementById('delete-opportunity-submit');
        var deleteOpportunityError = document.getElementById('delete-opportunity-error');
        var opportunityEditModalElement = document.getElementById('opportunity-edit-modal');
        var opportunityEditModal = window.bootstrap.Modal.getOrCreateInstance(opportunityEditModalElement);
        var opportunityEditForm = document.getElementById('opportunity-edit-form');
        var opportunityEditSave = document.getElementById('opportunity-edit-save');
        var opportunityEditError = document.getElementById('opportunity-edit-error');
        var opportunityRequestId = document.getElementById('opportunity-request-id');
        var opportunityDeleteOpen = document.getElementById('opportunity-delete-open');
        var pendingKeyIssue = null;
        var pendingSubgridEdit = null;
        var pendingClientDelete = null;
        var pendingOpportunityDelete = null;
        var opportunityEditIsNew = false;
        var subgridEditSequence = 0;
        var expandedCustomerDetail = null;
        var clientDeleteParentModal = null;
        var opportunityDeleteParentModal = null;

        function responseData(response) { return response && response.Data ? response.Data : []; }
        function clearEmailPreview() {
            emailPreviewFrame.removeAttribute('srcdoc');
            emailPreviewFrame.setAttribute('src', 'about:blank');
        }
        function templateHtml(id) {
            var template = document.getElementById(id);
            return template ? template.innerHTML.trim() : '';
        }
        function setSelectOptions(select, options, selectedValue) {
            select.innerHTML = '';
            (options || []).forEach(function (option) {
                var element = document.createElement('option');
                element.value = option.Value;
                element.textContent = option.Text;
                element.selected = String(option.Value) === String(selectedValue);
                select.appendChild(element);
            });
        }
        function setButtonLabel(button, value) {
            var label = button.querySelector('span');
            if (label) label.textContent = value;
            else button.textContent = value;
        }
        function subgridSubmitLabel(kind, submitting) {
            if (kind === 'client-new') {
                return submitting ? 'Creating account...' : 'Create account and issue key';
            }
            if (kind === 'subscription-new') {
                return submitting ? 'Creating...' : 'Create subscription';
            }
            return submitting ? 'Saving...' : 'Save changes';
        }
        function setSubgridSubmitIcon(kind) {
            var iconId = kind === 'client-new'
                ? '#icon-user-add'
                : kind === 'subscription-new'
                    ? '#icon-plan'
                    : '#icon-save';
            subgridEditSaveIcon.setAttribute('href', iconId);
        }
        function setIconButtonLabel(button, value) {
            var label = button.querySelector('span');
            if (label) label.textContent = value;
            button.setAttribute('aria-label', value);
            button.title = value;
        }
        function showDeleteConfirmation(modal, parentModal) {
            document.body.classList.add('admin-delete-confirmation-open');
            if (parentModal) parentModal.setAttribute('inert', '');
            modal.show();
        }
        function restoreParentModal(parentModal, focusTarget) {
            document.body.classList.remove('admin-delete-confirmation-open');
            if (!parentModal) return;
            parentModal.removeAttribute('inert');
            if (!parentModal.classList.contains('show')) return;
            document.body.classList.add('modal-open');
            window.setTimeout(function () { if (focusTarget) focusTarget.focus(); }, 0);
        }
        function notifyInput(element) {
            if (element) element.dispatchEvent(new window.Event('input', { bubbles: true }));
        }
        function setEditFieldsActive(container, active) {
            container.hidden = !active;
            Array.prototype.forEach.call(container.querySelectorAll('input, select, textarea'), function (field) {
                field.disabled = !active;
            });
        }
        function setClientEditOnlyActive(active) {
            Array.prototype.forEach.call(clientEditFields.querySelectorAll('.client-edit-only'), function (container) {
                container.hidden = !active;
                Array.prototype.forEach.call(container.querySelectorAll('input, select, textarea'), function (field) {
                    field.disabled = !active;
                });
            });
        }
        function resetRecordModalPanels() {
            modalElement.classList.remove('dashboard-email-modal', 'dashboard-subgrid-edit-modal', 'dashboard-new-client-modal', 'dashboard-confirmation-modal');
            modalElement.setAttribute('aria-labelledby', 'dashboard-record-title');
            subgridEditorElement.classList.remove('account-content-section');
            subgridEditForm.classList.remove('account-form', 'dashboard-style-form', 'dashboard-style-form-body');
            profileElement.hidden = true;
            emailPreviewElement.hidden = true;
            subgridEditorElement.hidden = true;
            deleteClientOpen.hidden = true;
            clearEmailPreview();
        }
        function showSubgridEditError(message) {
            subgridEditError.textContent = message || 'The record could not be loaded.';
            subgridEditError.hidden = false;
        }
        function openSubgridEditor(kind, item, gridElement, clientId, detailUrl, separator) {
            var isNewClient = kind === 'client-new';
            var isNewSubscription = kind === 'subscription-new';
            var isClient = kind === 'client' || isNewClient;
            var isApiKey = kind === 'api';
            var editUrl = $('#customer-grid').data(isNewClient ? 'new-client-url' : isClient ? 'edit-client-url' : isApiKey ? 'edit-api-key-url' : isNewSubscription ? 'new-subscription-url' : 'edit-subscription-url');
            var recordId = isNewClient || isNewSubscription ? null : isClient ? item.ClientId : isApiKey ? item.ApiKeyId : item.SubscriptionId;
            var requestSequence = ++subgridEditSequence;

            pendingSubgridEdit = null;
            resetRecordModalPanels();
            modalElement.classList.add('dashboard-subgrid-edit-modal');
            if (isNewClient) modalElement.classList.add('dashboard-new-client-modal');
            subgridEditorElement.classList.toggle('account-content-section', isNewClient);
            subgridEditForm.classList.toggle('account-form', isNewClient);
            subgridEditForm.classList.toggle('dashboard-style-form', isNewClient);
            subgridEditForm.classList.toggle('dashboard-style-form-body', isNewClient);
            text('dashboard-record-kicker', isNewClient ? 'NEW DEALER ACCOUNT' : isClient ? 'DEALER ACCOUNT' : isApiKey ? 'API KEY RECORD' : isNewSubscription ? 'NEW SUBSCRIPTION' : 'SUBSCRIPTION RECORD');
            text('dashboard-record-title', isNewClient ? 'Create a new customer' : isClient ? 'Edit dealer account' : isApiKey ? 'Edit API key' : isNewSubscription ? 'Create a new subscription' : 'Edit subscription');
            text('dashboard-record-subtitle', isNewClient ? 'Generating a fresh client number...' : isNewSubscription ? 'Preparing subscription defaults...' : 'Loading record #' + recordId + '...');
            subgridEditNote.textContent = isClient
                ? isNewClient
                    ? 'A 14-day trial will begin after the customer confirms their email. ' +
                        'Their primary API key will then be issued securely.'
                    : 'Changing account status affects customer access.'
                : isApiKey ? 'Changing key status affects API access immediately.'
                    : isNewSubscription ? 'Review the plan, status, and billing period before creating the subscription.' : 'Double-check status and billing dates before saving.';
            subgridEditError.hidden = true;
            clientCreateResult.hidden = true;
            subgridEditSave.disabled = true;
            subgridEditSave.hidden = false;
            setButtonLabel(subgridEditSave, subgridSubmitLabel(kind, false));
            setSubgridSubmitIcon(kind);
            deleteClientOpen.hidden = true;
            setIconButtonLabel(subgridEditCancel, 'Cancel');
            subgridEditorElement.hidden = false;
            setEditFieldsActive(clientEditFields, false);
            setEditFieldsActive(clientCreateFields, false);
            setEditFieldsActive(apiKeyEditFields, false);
            setEditFieldsActive(subscriptionEditFields, false);
            recordModal.show();

            $.getJSON(editUrl, isNewClient ? {} : isNewSubscription ? { clientId: clientId } : { id: recordId }).done(function (response) {
                if (requestSequence !== subgridEditSequence) return;
                pendingSubgridEdit = {
                    kind: kind,
                    id: isNewSubscription ? response.SubscriptionId : recordId,
                    editUrl: editUrl,
                    gridElement: gridElement,
                    clientId: clientId,
                    detailUrl: detailUrl,
                    separator: separator
                };
                document.getElementById('subgrid-edit-id').value = recordId;
                text('dashboard-record-subtitle', isClient
                    ? isNewClient ? 'Create the account, trial subscription, login, and primary credential.' : 'Update the customer identity, contact information, and account access.'
                    : isApiKey ? 'Update the credential lifecycle and access settings.'
                        : isNewSubscription ? 'Create a billing record for this dealer. All dates are UTC.' : 'Update billing state, plan, and service period. All dates are UTC.');
                setEditFieldsActive(clientEditFields, isClient && !isNewClient);
                setClientEditOnlyActive(isClient && !isNewClient);
                setEditFieldsActive(clientCreateFields, isNewClient);
                setEditFieldsActive(apiKeyEditFields, isApiKey);
                setEditFieldsActive(subscriptionEditFields, !isClient && !isApiKey);
                deleteClientOpen.hidden = !(isClient && !isNewClient);

                if (isNewClient) {
                    document.getElementById('create-client-number').value = response.ClientNumber || '';
                    document.getElementById('create-client-business').value = '';
                    document.getElementById('create-client-first-name').value = '';
                    document.getElementById('create-client-last-name').value = '';
                    document.getElementById('create-client-email').value = '';
                    document.getElementById('create-client-phone').value = '';
                    document.getElementById('create-client-password').value = response.TemporaryPassword || '';
                    document.getElementById('create-client-confirm-password').value = response.TemporaryPassword || '';
                    setSelectOptions(document.getElementById('create-client-plan'), response.PlanOptions, response.PlanCode);
                    notifyInput(document.getElementById('create-client-email'));
                    notifyInput(document.getElementById('create-client-password'));
                    notifyInput(document.getElementById('create-client-confirm-password'));
                    document.getElementById('create-client-business').focus();
                } else if (isClient) {
                    document.getElementById('edit-client-number').value = response.ClientNumber || '';
                    document.getElementById('edit-client-created').value = response.CreatedUtc || '';
                    document.getElementById('edit-client-business').value = response.BusinessName || '';
                    document.getElementById('edit-client-first-name').value = response.FirstName || '';
                    document.getElementById('edit-client-last-name').value = response.LastName || '';
                    document.getElementById('edit-client-email').value = response.Email || '';
                    document.getElementById('edit-client-phone').value = response.Phone || '';
                    document.getElementById('edit-client-email-verified').value = response.EmailVerifiedUtc || '';
                    setSelectOptions(document.getElementById('edit-client-status'), response.StatusOptions, response.Status);
                    notifyInput(document.getElementById('edit-client-email'));
                    document.getElementById('edit-client-business').focus();
                } else if (isApiKey) {
                    document.getElementById('edit-api-key-prefix').value = response.KeyPrefix || '';
                    document.getElementById('edit-api-key-name').value = response.Name || '';
                    document.getElementById('edit-api-key-expires').value = response.ExpiresUtc || '';
                    setSelectOptions(document.getElementById('edit-api-key-status'), response.StatusOptions, response.Status);
                    setSelectOptions(document.getElementById('edit-api-key-scope'), response.ScopeOptions, response.Scopes);
                    setSelectOptions(document.getElementById('edit-api-key-subscription'), response.SubscriptionOptions, response.SubscriptionId);
                    document.getElementById('edit-api-key-name').focus();
                } else {
                    document.getElementById('edit-subscription-start').value = response.CurrentPeriodStartUtc || '';
                    document.getElementById('edit-subscription-end').value = response.CurrentPeriodEndUtc || '';
                    document.getElementById('edit-subscription-cancel').value = response.CancelAtPeriodEnd ? 'true' : 'false';
                    document.getElementById('edit-subscription-provider').value = response.ProviderSubscriptionId || '';
                    setSelectOptions(document.getElementById('edit-subscription-plan'), response.PlanOptions, response.PlanId);
                    setSelectOptions(document.getElementById('edit-subscription-status'), response.StatusOptions, response.Status);
                    document.getElementById('edit-subscription-plan').focus();
                }
                subgridEditSave.disabled = false;
            }).fail(function (xhr) {
                if (requestSequence !== subgridEditSequence) return;
                var response = xhr.responseJSON || {};
                showSubgridEditError(response.Message || 'The record could not be loaded.');
            });
        }

        subgridEditForm.addEventListener('submit', function (event) {
            event.preventDefault();
            if (!pendingSubgridEdit || !subgridEditForm.checkValidity()) {
                subgridEditForm.reportValidity();
                return;
            }

            var context = pendingSubgridEdit;
            var data = $(subgridEditForm).serializeArray();
            if (context.kind !== 'client-new' && context.kind !== 'subscription-new')
                data.push({ name: context.kind === 'client' ? 'ClientId' : context.kind === 'api' ? 'ApiKeyId' : 'SubscriptionId', value: context.id });
            if (context.kind === 'client-new')
                data.push({ name: 'ClientNumber', value: document.getElementById('create-client-number').value });
            if (context.kind === 'subscription-new')
                data.push({ name: 'ClientId', value: context.clientId });
            data.push({
                name: '__RequestVerificationToken',
                value: document.querySelector('.dashboard-antiforgery input[name="__RequestVerificationToken"]').value
            });
            subgridEditSave.disabled = true;
            setButtonLabel(subgridEditSave, subgridSubmitLabel(context.kind, true));
            subgridEditError.hidden = true;

            $.ajax({ url: context.editUrl, method: 'POST', dataType: 'json', data: data }).done(function (response) {
                if (context.kind === 'client-new') {
                    setEditFieldsActive(clientEditFields, false);
                    setEditFieldsActive(clientCreateFields, false);
                    text('dashboard-record-title', 'Customer created');
                    text('dashboard-record-subtitle', response.Message || 'The workspace is waiting for email confirmation.');
                    updateWorkspaceConfirmation(clientCreateResult, response.Email, response.VerificationEmailSent);
                    modalElement.classList.add('dashboard-confirmation-modal');
                    modalElement.setAttribute('aria-labelledby', 'workspace-confirmation-heading');
                    clientCreateResult.hidden = false;
                    subgridEditSave.hidden = true;
                    setIconButtonLabel(subgridEditCancel, 'Done');
                    subgridEditNote.textContent = response.VerificationEmailSent
                        ? 'No API key is issued until the customer confirms their email.'
                        : 'The account is inactive. The confirmation email must be resent before activation.';
                    $('#customer-grid').pepGrid('refresh');
                    return;
                }
                recordModal.hide();
                if (context.kind === 'client' || context.kind === 'subscription' || context.kind === 'subscription-new') {
                    $('#customer-grid').pepGrid('refresh');
                    return;
                }
                $.getJSON(context.detailUrl + context.separator + $.param({ clientId: context.clientId })).done(function (updated) {
                    if (context.gridElement.parentNode) $(context.gridElement).pepGrid('setData', updated.ApiKeys || []);
                });
            }).fail(function (xhr) {
                var response = xhr.responseJSON || {};
                showSubgridEditError(response.Message || 'The record could not be saved.');
            }).always(function () {
                subgridEditSave.disabled = false;
                setButtonLabel(subgridEditSave, subgridSubmitLabel(context.kind, false));
            });
        });

        function updateWorkspaceConfirmation(container, email, delivered) {
            var panel = container.querySelector('[data-workspace-confirmation]');
            panel.setAttribute('data-verification-sent', delivered ? 'true' : 'false');
            panel.querySelector('[data-confirmation-eyebrow]').textContent = delivered ? 'CONFIRMATION SENT' : 'DELIVERY NEEDS ATTENTION';
            panel.querySelector('[data-confirmation-heading]').textContent = delivered ? 'Check your email to activate the workspace' : 'Your workspace was saved';
            panel.querySelector('[data-confirmation-prefix]').textContent = delivered ? 'We sent a secure confirmation message to' : 'We could not deliver the confirmation message to';
            panel.querySelector('[data-confirmation-email]').textContent = email || 'the customer email address';
            panel.querySelector('[data-confirmation-suffix]').textContent = delivered
                ? '. Open it and click the confirmation button within 24 hours.'
                : '. The workspace remains safely inactive and no API key has been issued.';
            panel.querySelector('[data-confirmation-next]').hidden = !delivered;
            panel.querySelector('[data-confirmation-note]').textContent = delivered
                ? 'The API key has not been created yet and will never be displayed in this window. If the message is not visible, check the spam or promotions folder.'
                : 'Please verify the email address before arranging a new confirmation message.';
        }

        function prepareClientDelete(client) {
            pendingClientDelete = {
                clientId: client.ClientId,
                businessName: client.BusinessName,
                clientNumber: client.ClientNumber
            };
            deleteClientForm.reset();
            deleteClientSubmit.disabled = true;
            deleteClientError.hidden = true;
            deleteClientError.textContent = '';
            text('delete-client-name', pendingClientDelete.businessName || 'Selected customer');
            text('delete-client-detail', pendingClientDelete.clientNumber || '');
        }

        deleteClientOpen.addEventListener('click', function () {
            if (!pendingSubgridEdit || pendingSubgridEdit.kind !== 'client') return;
            prepareClientDelete({
                ClientId: pendingSubgridEdit.id,
                BusinessName: document.getElementById('edit-client-business').value,
                ClientNumber: document.getElementById('edit-client-number').value
            });
            clientDeleteParentModal = modalElement;
            showDeleteConfirmation(deleteClientModal, clientDeleteParentModal);
        });

        deleteClientConfirm.addEventListener('change', function () {
            deleteClientSubmit.disabled = !deleteClientConfirm.checked;
        });

        deleteClientForm.addEventListener('submit', function (event) {
            event.preventDefault();
            if (!pendingClientDelete || !deleteClientConfirm.checked || !deleteClientForm.checkValidity()) {
                deleteClientForm.reportValidity();
                return;
            }

            var clientId = pendingClientDelete.clientId;
            deleteClientConfirm.disabled = true;
            deleteClientSubmit.disabled = true;
            deleteClientSubmit.textContent = 'Deleting...';
            deleteClientError.hidden = true;
            $.ajax({
                url: $('#customer-grid').data('delete-client-url'),
                method: 'POST',
                dataType: 'json',
                data: {
                    __RequestVerificationToken: document.querySelector('.dashboard-antiforgery input[name="__RequestVerificationToken"]').value,
                    clientId: clientId,
                    confirmDelete: true
                }
            }).done(function () {
                deleteClientModal.hide();
                if (clientDeleteParentModal) recordModal.hide();
                closeCustomerDetail();
                $('#customer-grid').pepGrid('refresh');
            }).fail(function (xhr) {
                var response = xhr.responseJSON || {};
                deleteClientError.textContent = response.Message || 'The customer could not be deleted. No records were removed.';
                deleteClientError.hidden = false;
            }).always(function () {
                deleteClientConfirm.disabled = false;
                deleteClientSubmit.textContent = 'Yes, delete everything';
                deleteClientSubmit.disabled = !deleteClientConfirm.checked;
            });
        });

        $(deleteClientModalElement).on('hidden.bs.modal', function () {
            restoreParentModal(clientDeleteParentModal, deleteClientOpen);
            clientDeleteParentModal = null;
            pendingClientDelete = null;
            deleteClientForm.reset();
            deleteClientConfirm.disabled = false;
            deleteClientSubmit.disabled = true;
            deleteClientSubmit.textContent = 'Yes, delete everything';
            deleteClientError.hidden = true;
            deleteClientError.textContent = '';
        });

        function prepareOpportunityDelete(opportunity) {
            pendingOpportunityDelete = {
                requestId: opportunity.RequestId,
                businessName: opportunity.BusinessName,
                contactName: opportunity.ContactName,
                email: opportunity.Email
            };
            deleteOpportunityForm.reset();
            deleteOpportunitySubmit.disabled = true;
            deleteOpportunityError.hidden = true;
            deleteOpportunityError.textContent = '';
            text('delete-opportunity-name', pendingOpportunityDelete.businessName || 'Selected opportunity');
            text('delete-opportunity-detail', [pendingOpportunityDelete.contactName, pendingOpportunityDelete.email].filter(Boolean).join(' — '));
        }

        deleteOpportunityConfirm.addEventListener('change', function () {
            deleteOpportunitySubmit.disabled = !deleteOpportunityConfirm.checked;
        });

        deleteOpportunityForm.addEventListener('submit', function (event) {
            event.preventDefault();
            if (!pendingOpportunityDelete || !deleteOpportunityConfirm.checked || !deleteOpportunityForm.checkValidity()) {
                deleteOpportunityForm.reportValidity();
                return;
            }

            deleteOpportunityConfirm.disabled = true;
            deleteOpportunitySubmit.disabled = true;
            deleteOpportunitySubmit.textContent = 'Deleting...';
            deleteOpportunityError.hidden = true;
            $.ajax({
                url: $('#demo-request-grid').data('delete-url'),
                method: 'POST',
                dataType: 'json',
                data: {
                    __RequestVerificationToken: document.querySelector('.dashboard-antiforgery input[name="__RequestVerificationToken"]').value,
                    requestId: pendingOpportunityDelete.requestId,
                    confirmDelete: true
                }
            }).done(function () {
                deleteOpportunityModal.hide();
                if (opportunityDeleteParentModal) opportunityEditModal.hide();
                $('#demo-request-grid').pepGrid('refresh');
            }).fail(function (xhr) {
                var response = xhr.responseJSON || {};
                deleteOpportunityError.textContent = response.Message || 'The opportunity could not be deleted. No records were removed.';
                deleteOpportunityError.hidden = false;
            }).always(function () {
                deleteOpportunityConfirm.disabled = false;
                deleteOpportunitySubmit.textContent = 'Yes, delete opportunity';
                deleteOpportunitySubmit.disabled = !deleteOpportunityConfirm.checked;
            });
        });

        $(deleteOpportunityModalElement).on('hidden.bs.modal', function () {
            restoreParentModal(opportunityDeleteParentModal, opportunityDeleteOpen);
            opportunityDeleteParentModal = null;
            pendingOpportunityDelete = null;
            deleteOpportunityForm.reset();
            deleteOpportunityConfirm.disabled = false;
            deleteOpportunitySubmit.disabled = true;
            deleteOpportunitySubmit.textContent = 'Yes, delete opportunity';
            deleteOpportunityError.hidden = true;
            deleteOpportunityError.textContent = '';
        });

        function opportunityValue(id, value) {
            document.getElementById(id).value = value == null ? '' : value;
        }

        function openOpportunityEditor(item) {
            opportunityEditIsNew = !item || !item.RequestId;
            opportunityEditForm.reset();
            opportunityRequestId.disabled = opportunityEditIsNew;
            opportunityRequestId.value = opportunityEditIsNew ? '' : item.RequestId;
            opportunityDeleteOpen.hidden = opportunityEditIsNew;
            opportunityEditError.hidden = true;
            opportunityEditError.textContent = '';
            opportunityEditSave.disabled = true;
            setButtonLabel(opportunityEditSave, opportunityEditIsNew ? 'Create opportunity' : 'Save opportunity');
            text('opportunity-edit-kicker', opportunityEditIsNew ? 'NEW OPPORTUNITY' : 'OPPORTUNITY');
            text('opportunity-edit-title', opportunityEditIsNew ? 'Add a new opportunity' : 'Edit opportunity');
            text('opportunity-edit-subtitle', opportunityEditIsNew ? 'Create a prospective dealer record for follow-up.' : 'Update the prospective dealer and follow-up status.');
            text('opportunity-edit-created', opportunityEditIsNew ? 'Created when saved' : 'Loading...');
            opportunityEditModal.show();

            $.getJSON($demoGrid.data(opportunityEditIsNew ? 'new-url' : 'edit-url'), opportunityEditIsNew ? {} : { id: item.RequestId })
                .done(function (response) {
                    opportunityRequestId.value = response.RequestId || '';
                    opportunityValue('opportunity-business', response.BusinessName);
                    opportunityValue('opportunity-contact', response.ContactName);
                    opportunityValue('opportunity-email', response.Email);
                    opportunityValue('opportunity-phone', response.Phone);
                    opportunityValue('opportunity-website', response.CurrentWebsite);
                    opportunityValue('opportunity-locations', response.LocationCount);
                    opportunityValue('opportunity-inventory', response.InventorySize);
                    opportunityValue('opportunity-goal', response.PrimaryGoal);
                    opportunityValue('opportunity-message', response.Message);
                    setSelectOptions(document.getElementById('opportunity-contact-method'), response.ContactOptions, response.PreferredContact);
                    setSelectOptions(document.getElementById('opportunity-status'), response.StatusOptions, response.Status);
                    text('opportunity-edit-created', opportunityEditIsNew ? 'Created when saved' : 'Received ' + (response.CreatedUtc || ''));
                    opportunityEditSave.disabled = false;
                    opportunityDeleteOpen.hidden = opportunityEditIsNew;
                    document.getElementById('opportunity-business').focus();
                })
                .fail(function (xhr) {
                    var response = xhr.responseJSON || {};
                    opportunityEditError.textContent = response.Message || 'The opportunity could not be loaded.';
                    opportunityEditError.hidden = false;
                });
        }

        opportunityEditForm.addEventListener('submit', function (event) {
            event.preventDefault();
            if (!opportunityEditForm.checkValidity()) {
                opportunityEditForm.reportValidity();
                return;
            }

            opportunityEditSave.disabled = true;
            setButtonLabel(opportunityEditSave, opportunityEditIsNew ? 'Creating...' : 'Saving...');
            opportunityEditError.hidden = true;
            var data = $(opportunityEditForm).serializeArray();
            data.push({
                name: '__RequestVerificationToken',
                value: document.querySelector('.dashboard-antiforgery input[name="__RequestVerificationToken"]').value
            });
            $.ajax({
                url: $demoGrid.data(opportunityEditIsNew ? 'new-url' : 'edit-url'),
                method: 'POST',
                dataType: 'json',
                data: data
            }).done(function () {
                opportunityEditModal.hide();
                $demoGrid.pepGrid('refresh');
            }).fail(function (xhr) {
                var response = xhr.responseJSON || {};
                opportunityEditError.textContent = response.Message || 'The opportunity could not be saved.';
                opportunityEditError.hidden = false;
            }).always(function () {
                opportunityEditSave.disabled = false;
                setButtonLabel(opportunityEditSave, opportunityEditIsNew ? 'Create opportunity' : 'Save opportunity');
            });
        });

        opportunityDeleteOpen.addEventListener('click', function () {
            if (opportunityEditIsNew || !opportunityRequestId.value) return;
            prepareOpportunityDelete({
                RequestId: opportunityRequestId.value,
                BusinessName: document.getElementById('opportunity-business').value,
                ContactName: document.getElementById('opportunity-contact').value,
                Email: document.getElementById('opportunity-email').value
            });
            opportunityDeleteParentModal = opportunityEditModalElement;
            showDeleteConfirmation(deleteOpportunityModal, opportunityDeleteParentModal);
        });

        $(opportunityEditModalElement).on('hidden.bs.modal', function () {
            opportunityEditForm.reset();
            opportunityRequestId.disabled = false;
            opportunityDeleteOpen.hidden = true;
            opportunityEditError.hidden = true;
            opportunityEditError.textContent = '';
        });

        function openKeyIssueModal(context) {
            pendingKeyIssue = context;
            keyIssueForm.reset();
            keyIssueError.hidden = true;
            keyIssueError.textContent = '';
            keyIssueSubmit.disabled = false;
            keyIssueSubmit.textContent = 'Issue API key';
            keyIssueModal.show();
            window.setTimeout(function () { keyIssueName.focus(); keyIssueName.select(); }, 180);
        }

        keyIssueForm.addEventListener('submit', function (event) {
            event.preventDefault();
            if (!pendingKeyIssue || !keyIssueForm.checkValidity()) {
                keyIssueForm.reportValidity();
                return;
            }

            var context = pendingKeyIssue;
            var keyName = keyIssueName.value.trim() || 'Additional key';
            keyIssueSubmit.disabled = true;
            keyIssueSubmit.textContent = 'Issuing...';
            keyIssueError.hidden = true;

            $.ajax({
                url: $('#customer-grid').data('issue-key-url'),
                method: 'POST',
                dataType: 'json',
                data: {
                    __RequestVerificationToken: document.querySelector('.dashboard-antiforgery input[name="__RequestVerificationToken"]').value,
                    clientId: context.detail.dataItem.ClientId,
                    name: keyName
                }
            }).done(function (response) {
                keyIssueModal.hide();
                context.issueResult.classList.remove('is-error');
                context.issueResult.querySelector('span').textContent = response.Message + ' This full key is shown once:';
                context.issueResult.querySelector('code').textContent = response.ApiKey;
                context.issueResult.hidden = false;
                context.detail.dataItem.ApiKeyCount = Number(context.detail.dataItem.ApiKeyCount || 0) + 1;
                context.toggle.querySelector('span').textContent = context.detail.dataItem.ApiKeyCount;
                $.getJSON(context.detailUrl + context.separator + $.param({ clientId: context.detail.dataItem.ClientId })).done(function (updated) {
                    if (context.detailRow.parentNode) $(context.detailRow).find('.customer-api-grid').pepGrid('setData', updated.ApiKeys || []);
                });
                pendingKeyIssue = null;
            }).fail(function (xhr) {
                var response = xhr.responseJSON || {};
                keyIssueError.textContent = response.Message || 'The API key could not be issued.';
                keyIssueError.hidden = false;
            }).always(function () {
                keyIssueSubmit.disabled = false;
                keyIssueSubmit.textContent = 'Issue API key';
            });
        });

        $(keyIssueModalElement).on('hidden.bs.modal', function () {
            pendingKeyIssue = null;
            keyIssueError.hidden = true;
            keyIssueError.textContent = '';
        });

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
            resetRecordModalPanels();
            profileElement.hidden = false;
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
                    { label: 'API keys', value: item.ApiKeyCount },
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

        function closeCustomerDetail() {
            if (!expandedCustomerDetail) return;
            if (expandedCustomerDetail.toggle) {
                expandedCustomerDetail.toggle.setAttribute('aria-expanded', 'false');
                expandedCustomerDetail.toggle.classList.remove('is-open');
            }
            if (expandedCustomerDetail.row && expandedCustomerDetail.row.parentNode)
                expandedCustomerDetail.row.parentNode.removeChild(expandedCustomerDetail.row);
            expandedCustomerDetail = null;
        }

        function showEmailPreview(item) {
            resetRecordModalPanels();
            modalElement.classList.add('dashboard-email-modal');
            emailPreviewElement.hidden = false;
            text('dashboard-record-kicker', 'MESSAGE PREVIEW');
            text('dashboard-record-title', item.Subject || 'Email message');
            text('dashboard-record-subtitle', 'Sent ' + valueOrEmpty(item.Sent) + ' to ' + valueOrEmpty(item.ToEmail));
            clearEmailPreview();
            emailPreviewFrame.removeAttribute('src');
            emailPreviewFrame.srcdoc = item.HtmlBody || '';
            recordModal.show();
        }

        $(modalElement).on('hidden.bs.modal', function () {
            subgridEditSequence += 1;
            pendingSubgridEdit = null;
            clearEmailPreview();
            modalElement.classList.remove('dashboard-email-modal', 'dashboard-subgrid-edit-modal', 'dashboard-new-client-modal', 'dashboard-confirmation-modal');
            modalElement.setAttribute('aria-labelledby', 'dashboard-record-title');
            emailPreviewElement.hidden = true;
            subgridEditorElement.hidden = true;
            subgridEditError.hidden = true;
            clientCreateResult.hidden = true;
            subgridEditSave.hidden = false;
            setIconButtonLabel(subgridEditCancel, 'Cancel');
            profileElement.hidden = false;
        });

        function expandCustomerEmails(detail) {
            var toggle = detail.event.target.closest('.customer-email-toggle');
            if (!toggle || !detail.dataItem.EmailCount) return;
            detail.event.preventDefault();
            detail.event.stopPropagation();

            if (expandedCustomerDetail && expandedCustomerDetail.ownerRow === detail.rowElement && expandedCustomerDetail.kind === 'email') {
                closeCustomerDetail();
                return;
            }
            closeCustomerDetail();

            var detailRow = document.createElement('tr');
            detailRow.className = 'customer-email-detail-row';
            var detailCell = document.createElement('td');
            detailCell.colSpan = detail.rowElement.children.length;
            detailCell.innerHTML = templateHtml('customer-email-detail-template');
            detailRow.appendChild(detailCell);
            detail.rowElement.parentNode.insertBefore(detailRow, detail.rowElement.nextSibling);
            toggle.setAttribute('aria-expanded', 'true');
            toggle.classList.add('is-open');
            expandedCustomerDetail = { ownerRow: detail.rowElement, row: detailRow, toggle: toggle, kind: 'email' };

            detailRow.querySelector('.customer-email-collapse').addEventListener('click', closeCustomerDetail);

            var emailUrl = $('#customer-grid').data('email-url');
            var separator = String(emailUrl).indexOf('?') >= 0 ? '&' : '?';
            $(detailRow).find('.customer-email-grid').pepGrid({
                url: emailUrl + separator + $.param({ clientId: detail.dataItem.ClientId }),
                schema: { data: responseData }, height: null, pageable: false, pageSize: 20,
                resizable: true, autozoomable: true, showSearch: false, exportToExcel: false, exportToPdf: false,
                defaultSort: [{ field: 'SentSort', dir: 'desc' }],
                onCellClick: function (emailDetail) {
                    var action = emailDetail.event.target.closest('[data-email-action]');
                    if (!action) return;
                    emailDetail.event.preventDefault();
                    emailDetail.event.stopPropagation();
                    showEmailPreview(emailDetail.dataItem);
                },
                onCellDblClick: function (emailDetail) { showEmailPreview(emailDetail.dataItem); },
                onRowDblClick: function (emailDetail) { showEmailPreview(emailDetail.dataItem); },
                columns: [
                    { field: 'Sent', title: 'Sent', width: '24%' },
                    { field: 'ToEmail', title: 'To', width: '26%' },
                    { field: 'Subject', title: 'Subject', width: '38%' },
                    { field: 'View', title: 'Message', width: '12%', sortable: false, filterable: false, template: '#customer-email-actions-template' }
                ]
            });
        }

        function expandCustomerAccount(detail, kind) {
            var isApiKeys = kind === 'api';
            var toggle = detail.event.target.closest(isApiKeys ? '.customer-api-toggle' : '.customer-subscription-toggle');
            if (!toggle) return;
            detail.event.preventDefault();
            detail.event.stopPropagation();

            if (expandedCustomerDetail && expandedCustomerDetail.ownerRow === detail.rowElement && expandedCustomerDetail.kind === kind) {
                closeCustomerDetail();
                return;
            }
            closeCustomerDetail();

            var detailRow = document.createElement('tr');
            detailRow.className = 'customer-email-detail-row customer-account-detail-row';
            var detailCell = document.createElement('td');
            detailCell.colSpan = detail.rowElement.children.length;
            detailCell.innerHTML = templateHtml(isApiKeys ? 'customer-api-detail-template' : 'customer-subscription-detail-template');
            detailRow.appendChild(detailCell);
            detail.rowElement.parentNode.insertBefore(detailRow, detail.rowElement.nextSibling);
            toggle.setAttribute('aria-expanded', 'true');
            toggle.classList.add('is-open');
            expandedCustomerDetail = { ownerRow: detail.rowElement, row: detailRow, toggle: toggle, kind: kind };
            detailRow.querySelector('.customer-email-collapse').addEventListener('click', closeCustomerDetail);

            var detailUrl = $('#customer-grid').data('account-detail-url');
            var separator = String(detailUrl).indexOf('?') >= 0 ? '&' : '?';
            if (isApiKeys) {
                var issueButton = detailRow.querySelector('.customer-api-issue');
                var issueResult = detailRow.querySelector('.customer-api-issue-result');
                var copyButton = detailRow.querySelector('.customer-api-copy');
                issueButton.addEventListener('click', function () {
                    issueResult.hidden = true;
                    openKeyIssueModal({
                        detail: detail,
                        detailRow: detailRow,
                        toggle: toggle,
                        issueResult: issueResult,
                        detailUrl: detailUrl,
                        separator: separator
                    });
                });
                copyButton.addEventListener('click', function () {
                    var value = issueResult.querySelector('code').textContent;
                    if (!value) return;
                    if (navigator.clipboard && navigator.clipboard.writeText) {
                        navigator.clipboard.writeText(value).then(function () { copyButton.textContent = 'Copied'; });
                    } else {
                        var selection = window.getSelection();
                        var range = document.createRange();
                        range.selectNodeContents(issueResult.querySelector('code'));
                        selection.removeAllRanges();
                        selection.addRange(range);
                        document.execCommand('copy');
                        selection.removeAllRanges();
                        copyButton.textContent = 'Copied';
                    }
                });
            } else {
                detailRow.querySelector('.customer-subscription-new').addEventListener('click', function () {
                    openSubgridEditor(
                        'subscription-new',
                        {},
                        detailRow.querySelector('.customer-subscription-grid'),
                        detail.dataItem.ClientId,
                        detailUrl,
                        separator
                    );
                });
            }
            $.getJSON(detailUrl + separator + $.param({ clientId: detail.dataItem.ClientId }))
                .done(function (response) {
                    if (!detailRow.parentNode) return;
                    if (isApiKeys) {
                        var apiGridElement = detailRow.querySelector('.customer-api-grid');
                        $(apiGridElement).pepGrid({
                            data: response.ApiKeys || [], height: null, pageable: false, pageSize: 100,
                            resizable: true, autozoomable: true, showSearch: false, exportToExcel: false, exportToPdf: false,
                            defaultSort: [{ field: 'CreatedSort', dir: 'desc' }],
                            onCellDblClick: function (apiDetail) {
                                openSubgridEditor('api', apiDetail.dataItem, apiGridElement, detail.dataItem.ClientId, detailUrl, separator);
                            },
                            onRowDblClick: function (apiDetail) {
                                openSubgridEditor('api', apiDetail.dataItem, apiGridElement, detail.dataItem.ClientId, detailUrl, separator);
                            },
                            columns: [
                                { field: 'ApiKeyId', title: 'ID', width: '7%' },
                                { field: 'Name', title: 'Name', width: '16%' },
                                { field: 'KeyPrefix', title: 'Key prefix', width: '15%' },
                                { field: 'Scopes', title: 'Scopes', width: '14%' },
                                { field: 'Status', title: 'Status', width: '11%', template: '#customer-key-status-template' },
                                { field: 'Created', title: 'Created', width: '18%' },
                                { field: 'LastUsed', title: 'Last used', width: '19%' }
                            ]
                        });
                    } else {
                        var subscriptionGridElement = detailRow.querySelector('.customer-subscription-grid');
                        $(subscriptionGridElement).pepGrid({
                            data: response.Subscriptions || [], height: null, pageable: false, pageSize: 100,
                            resizable: true, autozoomable: true, showSearch: false, exportToExcel: false, exportToPdf: false,
                            defaultSort: [{ field: 'PeriodEndSort', dir: 'desc' }],
                            onCellDblClick: function (subscriptionDetail) {
                                openSubgridEditor('subscription', subscriptionDetail.dataItem, subscriptionGridElement, detail.dataItem.ClientId, detailUrl, separator);
                            },
                            onRowDblClick: function (subscriptionDetail) {
                                openSubgridEditor('subscription', subscriptionDetail.dataItem, subscriptionGridElement, detail.dataItem.ClientId, detailUrl, separator);
                            },
                            columns: [
                                { field: 'SubscriptionId', title: 'ID', width: '7%' },
                                { field: 'PlanName', title: 'Plan', width: '15%' },
                                { field: 'Status', title: 'Status', width: '12%', template: '#customer-subscription-status-template' },
                                { field: 'Quota', title: 'Quota', width: '10%' },
                                { field: 'PeriodStart', title: 'Period starts', width: '19%' },
                                { field: 'PeriodEnd', title: 'Period ends', width: '19%' },
                                { field: 'CancelAtPeriodEnd', title: 'Cancel', width: '8%' },
                                { field: 'ProviderSubscription', title: 'Provider ID', width: '15%' }
                            ]
                        });
                    }
                })
                .fail(function () {
                    if (!detailRow.parentNode) return;
                    detailRow.querySelector('.customer-account-detail').insertAdjacentHTML('beforeend', '<p class="customer-account-error">Account details could not be loaded.</p>');
                });
        }

        var $customerGrid = $('#customer-grid');
        if ($customerGrid.length) {
            function ensureNewCustomerToolbarButton() {
                var customerSearchBar = $customerGrid[0].querySelector('.pg-search-bar');
                if (!customerSearchBar || customerSearchBar.querySelector('.admin-new-customer')) {
                    return;
                }

                var newCustomerButton = document.createElement('button');
                newCustomerButton.type = 'button';
                newCustomerButton.className = 'admin-new-customer';
                newCustomerButton.textContent = 'New customer';
                newCustomerButton.addEventListener('click', function () {
                    openSubgridEditor('client-new', {}, document.getElementById('customer-grid'), null, null, null);
                });

                var searchInputGroup = customerSearchBar.querySelector('.pg-search-input-group');
                customerSearchBar.insertBefore(newCustomerButton, searchInputGroup || customerSearchBar.firstChild);
            }

            $customerGrid.pepGrid({
                url: $customerGrid.data('url'), schema: { data: responseData }, height: null, pageable: false, pageSize: 100,
                resizable: true, autozoomable: true, exportToExcel: false, exportToPdf: false,
                defaultSort: [{ field: 'CreatedSort', dir: 'desc' }],
                onDataBound: function () {
                    expandedCustomerDetail = null;
                    ensureNewCustomerToolbarButton();
                },
                onCellClick: function (detail) {
                    if (detail.field === 'Delete') {
                        var deleteButton = detail.event.target.closest('.customer-delete-action');
                        if (!deleteButton) return;
                        detail.event.preventDefault();
                        detail.event.stopPropagation();
                        prepareClientDelete(detail.dataItem);
                        deleteClientModal.show();
                        return;
                    }
                    if (detail.field === 'EmailCount') expandCustomerEmails(detail);
                    if (detail.field === 'ApiKeyCount') expandCustomerAccount(detail, 'api');
                    if (detail.field === 'SubscriptionCount') expandCustomerAccount(detail, 'subscription');
                },
                onCellDblClick: function (detail) {
                    if (detail.field !== 'EmailCount' && detail.field !== 'ApiKeyCount' && detail.field !== 'SubscriptionCount')
                        openSubgridEditor('client', detail.dataItem, document.getElementById('customer-grid'), detail.dataItem.ClientId, null, null);
                },
                onRowDblClick: function (detail) {
                    openSubgridEditor('client', detail.dataItem, document.getElementById('customer-grid'), detail.dataItem.ClientId, null, null);
                },
                columns: [
                    { field: 'BusinessName', title: 'Customer', width: '15%' },
                    { field: 'ClientNumber', title: 'Client number', width: '13%' },
                    { field: 'ContactName', title: 'Contact', width: '14%' },
                    { field: 'Email', title: 'Email', width: '18%' },
                    { field: 'ApiKeyCount', title: 'API keys', width: '10%', sortable: false, filterable: false, template: '#customer-api-toggle-template' },
                    { field: 'SubscriptionCount', title: 'Subscription', width: '13%', sortable: false, filterable: false, template: '#customer-subscription-toggle-template' },
                    { field: 'EmailCount', title: 'Mail', width: '8%', sortable: false, filterable: false, template: '#customer-email-toggle-template' },
                    { field: 'Delete', title: '', width: '9%', sortable: false, filterable: false, template: '#customer-delete-action-template' }
                ]
            });
        }

        var $demoGrid = $('#demo-request-grid');
        if ($demoGrid.length) {
            function ensureNewOpportunityToolbarButton() {
                var searchBar = $demoGrid[0].querySelector('.pg-search-bar');
                if (!searchBar || searchBar.querySelector('.admin-new-opportunity')) return;
                var button = document.createElement('button');
                button.type = 'button';
                button.className = 'admin-new-opportunity';
                button.textContent = 'New opportunity';
                button.addEventListener('click', function () { openOpportunityEditor(null); });
                var searchInputGroup = searchBar.querySelector('.pg-search-input-group');
                searchBar.insertBefore(button, searchInputGroup || searchBar.firstChild);
            }

            $demoGrid.pepGrid({
                url: $demoGrid.data('url'), schema: { data: responseData }, height: null, pageable: false, pageSize: 100,
                resizable: true, autozoomable: true, exportToExcel: false, exportToPdf: false,
                defaultSort: [{ field: 'CreatedSort', dir: 'desc' }],
                onDataBound: ensureNewOpportunityToolbarButton,
                onCellClick: function (detail) {
                    if (detail.field !== 'Delete') return;
                    var deleteButton = detail.event.target.closest('.opportunity-delete-action');
                    if (!deleteButton) return;
                    detail.event.preventDefault();
                    detail.event.stopPropagation();
                    prepareOpportunityDelete(detail.dataItem);
                    deleteOpportunityModal.show();
                },
                onCellDblClick: function (detail) {
                    if (detail.field !== 'Delete') openOpportunityEditor(detail.dataItem);
                },
                onRowDblClick: function (detail) {
                    if (!detail.event.target.closest('.opportunity-delete-action')) openOpportunityEditor(detail.dataItem);
                },
                columns: [
                    { field: 'BusinessName', title: 'Dealership', width: '15%' },
                    { field: 'ContactName', title: 'Contact', width: '15%' },
                    { field: 'Email', title: 'Email', width: '15%' },
                    { field: 'Phone', title: 'Phone', width: '15%' },
                    { field: 'Inventory', title: 'Inventory', width: '15%' },
                    { field: 'Status', title: 'Status', width: '15%', template: '#demo-status-template' },
                    { field: 'Delete', title: '', width: '10%', sortable: false, filterable: false, template: '#opportunity-delete-action-template' }
                ]
            });
        }
    });
})(jQuery, window, document);
