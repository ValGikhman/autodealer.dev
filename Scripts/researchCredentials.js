(function () {
    'use strict';
    document.querySelectorAll('form[data-research-credentials]').forEach(function (form) {
        form.querySelectorAll('[data-research-credential]').forEach(function (field) {
            var interacted = false;
            function clear() {
                field.value = '';
                field.defaultValue = '';
                field.readOnly = document.activeElement !== field;
            }
            clear();
            field.addEventListener('focus', function () {
                // Ignore values injected by autofill before the user edits this field.
                if (field.readOnly) field.value = '';
                field.readOnly = false;
                interacted = true;
            });
            field.addEventListener('blur', function () {
                if (!field.value) field.readOnly = true;
            });
            field.addEventListener('input', function () {
                if (document.activeElement === field && !field.readOnly) interacted = true;
            });
            form.addEventListener('submit', function () {
                if (!interacted) clear();
            }, true);
            window.addEventListener('pageshow', function (event) {
                if (event.persisted) interacted = false;
                if (event.persisted || !interacted) clear();
            });
        });
    });
}());
