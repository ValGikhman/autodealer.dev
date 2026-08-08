(function () {
    'use strict';

    var eyeIcon = '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M2.5 12s3.4-5.5 9.5-5.5 9.5 5.5 9.5 5.5-3.4 5.5-9.5 5.5S2.5 12 2.5 12Z"></path><circle cx="12" cy="12" r="2.75"></circle></svg>';
    var eyeOffIcon = '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M3 3l18 18"></path><path d="M10.6 6.7c.5-.1.9-.2 1.4-.2 6.1 0 9.5 5.5 9.5 5.5a16.2 16.2 0 0 1-3.1 3.5M6.2 6.2A16.4 16.4 0 0 0 2.5 12s3.4 5.5 9.5 5.5c1.4 0 2.6-.3 3.7-.7M9.9 9.9a3 3 0 0 0 4.2 4.2"></path></svg>';

    function updateToggle(input, button, visible) {
        var label = visible ? 'Hide password' : 'Show password';
        input.type = visible ? 'text' : 'password';
        button.classList.toggle('is-visible', visible);
        button.setAttribute('aria-label', label);
        button.setAttribute('title', label);
        button.setAttribute('aria-pressed', visible ? 'true' : 'false');
        button.innerHTML = visible ? eyeOffIcon : eyeIcon;
    }

    function enhance(input) {
        if (!input || input.dataset.passwordVisibilityReady === 'true') return;

        var wrapper = document.createElement('div');
        var button = document.createElement('button');
        wrapper.className = 'password-input-shell';
        button.type = 'button';
        button.className = 'password-visibility-toggle';

        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);
        wrapper.appendChild(button);
        input.dataset.passwordVisibilityReady = 'true';
        updateToggle(input, button, false);

        button.addEventListener('click', function () {
            var selectionStart = input.selectionStart;
            var selectionEnd = input.selectionEnd;
            updateToggle(input, button, input.type === 'password');
            input.focus({ preventScroll: true });
            if (selectionStart !== null && selectionEnd !== null) input.setSelectionRange(selectionStart, selectionEnd);
        });
    }

    function enhanceWithin(root) {
        if (!root || root.nodeType !== 1 && root.nodeType !== 9) return;
        if (root.matches && root.matches('input[type="password"]')) enhance(root);
        var fields = root.querySelectorAll ? root.querySelectorAll('input[type="password"]') : [];
        for (var i = 0; i < fields.length; i += 1) enhance(fields[i]);
    }

    function initialize() {
        enhanceWithin(document);

        if (window.MutationObserver) {
            new MutationObserver(function (mutations) {
                for (var i = 0; i < mutations.length; i += 1) {
                    for (var j = 0; j < mutations[i].addedNodes.length; j += 1) enhanceWithin(mutations[i].addedNodes[j]);
                }
            }).observe(document.body, { childList: true, subtree: true });
        }

        document.addEventListener('reset', function (event) {
            window.setTimeout(function () {
                var fields = event.target.querySelectorAll('.password-input-shell input');
                for (var i = 0; i < fields.length; i += 1) {
                    var button = fields[i].parentNode.querySelector('.password-visibility-toggle');
                    if (button) updateToggle(fields[i], button, false);
                }
            }, 0);
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize);
    else initialize();
}());
