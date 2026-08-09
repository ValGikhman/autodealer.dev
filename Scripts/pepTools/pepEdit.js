/*!
 * Dependencies: jQuery 3+, Bootstrap Icons (bi-*)
 *
 * Usage:
 *   $('#Body').pepEdit({ tools: ['bold', 'italic', '|', 'createLink'] });
 *   $('#Body').data('pepEdit').value('<p>Hello</p>');
 *   var html = $('#Body').data('pepEdit').value();
 *
 * Lightweight, self-contained rich text editor:
 *   - Attaches to a <textarea>, keeps it in sync for normal form postback.
 *   - `tools` accepts an array of tool-name strings (unsupported names are
 *     skipped with a console warning instead of throwing).
 *   - `.value()` getter/setter and `.body` (raw contenteditable DOM element)
 *     provide a simple API for reading/writing editor content.
 *
 * See README-PepEdit.md in this folder for full documentation and the tool
 * registry / plugin-extension API.
 */
;(function ($) {
    'use strict';

    const DATA_KEY = 'pep-edit';

    function debounce(fn, wait) {
        let timer;
        return function () {
            const ctx = this, args = arguments;
            clearTimeout(timer);
            timer = setTimeout(function () { fn.apply(ctx, args); }, wait);
        };
    }

    function isSeparator(item) {
        return item === '|' || (item && typeof item === 'object' && item.type === 'separator');
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Tool registry — extensible plugin API
    // ════════════════════════════════════════════════════════════════════════
    // Each tool descriptor:
    //   { type: 'button'|'dropdown'|'color', icon, title, command,
    //     items (dropdown), exec(editor, value), isActive(editor) }
    const toolRegistry = {};

    /**
     * Register a custom toolbar tool so it can be referenced by name in the
     * `tools` option.
     * @param {string} name      Tool name used in the `tools` array
     * @param {object} def       Tool descriptor (see registry comment above)
     */
    function registerTool(name, def) {
        toolRegistry[name] = $.extend({ type: 'button' }, def);
    }

    const BLOCK_FORMATS = [
        { text: 'Paragraph', value: 'p' },
        { text: 'Heading 1', value: 'h1' },
        { text: 'Heading 2', value: 'h2' },
        { text: 'Heading 3', value: 'h3' },
        { text: 'Heading 4', value: 'h4' },
        { text: 'Quotation', value: 'blockquote' },
        { text: 'Formatted', value: 'pre' }
    ];

    const FONT_NAMES = ['Arial', 'Courier New', 'Georgia', 'Tahoma', 'Times New Roman', 'Verdana'];
    const FONT_SIZES = ['1', '2', '3', '4', '5', '6', '7'];
    const FONT_SIZE_LABELS = { '1': '8pt', '2': '10pt', '3': '12pt', '4': '14pt', '5': '18pt', '6': '24pt', '7': '36pt' };

    // ── Built-in tools ───────────────────────────────────────────────────────
    registerTool('bold',            { icon: 'bi-type-bold', title: 'Bold', command: 'bold' });
    registerTool('italic',          { icon: 'bi-type-italic', title: 'Italic', command: 'italic' });
    registerTool('underline',       { icon: 'bi-type-underline', title: 'Underline', command: 'underline' });
    registerTool('strikethrough',   { icon: 'bi-type-strikethrough', title: 'Strikethrough', command: 'strikeThrough' });
    registerTool('subscript',       { icon: 'bi-subscript', title: 'Subscript', command: 'subscript' });
    registerTool('superscript',     { icon: 'bi-superscript', title: 'Superscript', command: 'superscript' });

    registerTool('justifyLeft',     { icon: 'bi-text-left', title: 'Align left', command: 'justifyLeft' });
    registerTool('justifyCenter',   { icon: 'bi-text-center', title: 'Align center', command: 'justifyCenter' });
    registerTool('justifyRight',    { icon: 'bi-text-right', title: 'Align right', command: 'justifyRight' });
    registerTool('justifyFull',     { icon: 'bi-justify', title: 'Justify', command: 'justifyFull' });

    registerTool('insertOrderedList',   { icon: 'bi-list-ol', title: 'Ordered list', command: 'insertOrderedList' });
    registerTool('insertUnorderedList', { icon: 'bi-list-ul', title: 'Unordered list', command: 'insertUnorderedList' });
    registerTool('indent',           { icon: 'bi-text-indent-right', title: 'Indent', command: 'indent' });
    registerTool('outdent',          { icon: 'bi-text-indent-left', title: 'Outdent', command: 'outdent' });

    registerTool('foreColor', {
        type: 'color', icon: 'bi-palette', title: 'Text color', command: 'foreColor'
    });
    registerTool('backColor', {
        type: 'color', icon: 'bi-paint-bucket', title: 'Background color', command: 'hiliteColor'
    });

    registerTool('formatting', {
        type: 'dropdown', title: 'Paragraph format', command: 'formatBlock',
        items: BLOCK_FORMATS
    });
    registerTool('fontName', {
        type: 'dropdown', title: 'Font', command: 'fontName',
        items: FONT_NAMES.map(function (f) { return { text: f, value: f }; }),
        placeholder: 'Font'
    });
    registerTool('fontSize', {
        type: 'dropdown', title: 'Font size', command: 'fontSize',
        items: FONT_SIZES.map(function (s) { return { text: FONT_SIZE_LABELS[s], value: s }; }),
        placeholder: 'Size'
    });

    registerTool('createLink', {
        icon: 'bi-link-45deg', title: 'Insert link',
        exec: function (editor) {
            const existing = document.queryCommandValue('createLink');
            const url = window.prompt('Enter a URL:', existing && existing !== 'false' ? existing : 'https://');
            if (url) { document.execCommand('createLink', false, url); }
        }
    });
    registerTool('unlink', { icon: 'bi-link-45deg', title: 'Remove link', command: 'unlink' });

    registerTool('insertImage', {
        icon: 'bi-image', title: 'Insert image',
        exec: function (editor) {
            const insert = function (url) { if (url) { document.execCommand('insertImage', false, url); } };
            if (typeof editor._opts.onImageSelect === 'function') {
                editor._opts.onImageSelect.call(editor._el, insert);
            } else {
                insert(window.prompt('Enter an image URL:', 'https://'));
            }
        }
    });

    registerTool('createTable', {
        icon: 'bi-table', title: 'Insert table',
        exec: function (editor) {
            const rows = 2, cols = 2;
            let html = '<table class="pe-table"><tbody>';
            for (let r = 0; r < rows; r++) {
                html += '<tr>';
                for (let c = 0; c < cols; c++) { html += '<td>&nbsp;</td>'; }
                html += '</tr>';
            }
            html += '</tbody></table><p></p>';
            document.execCommand('insertHTML', false, html);
        }
    });

    registerTool('cleanFormatting', {
        icon: 'bi-eraser', title: 'Clean formatting',
        exec: function (editor) {
            document.execCommand('removeFormat');
            $(editor.body).find('span[style], font').each(function () {
                $(this).contents().unwrap();
            });
            editor._syncField();
            editor._triggerChange();
        }
    });

    registerTool('viewHtml', {
        icon: 'bi-code-slash', title: 'View HTML source',
        exec: function (editor) { editor._toggleSourceView(); }
    });

    registerTool('undo', { icon: 'bi-arrow-counterclockwise', title: 'Undo', command: 'undo' });
    registerTool('redo', { icon: 'bi-arrow-clockwise', title: 'Redo', command: 'redo' });

    const DEFAULT_TOOLS = [
        'bold', 'italic', 'underline', 'strikethrough', 'subscript', 'superscript', '|',
        'foreColor', 'backColor', '|',
        'justifyLeft', 'justifyCenter', 'justifyRight', 'justifyFull', '|',
        'insertOrderedList', 'insertUnorderedList', 'indent', 'outdent', '|',
        'createLink', 'unlink', 'insertImage', '|',
        'formatting', 'fontName', 'fontSize', '|',
        'cleanFormatting', 'viewHtml', '|',
        'undo', 'redo'
    ];

    // ════════════════════════════════════════════════════════════════════════
    //  Default options
    // ════════════════════════════════════════════════════════════════════════
    const defaults = {
        /** Toolbar tool names, in order. Accepts '|' or { type: 'separator' } as dividers. */
        tools: DEFAULT_TOOLS,

        /** CSS height of the editable content area. */
        height: 300,

        /** Placeholder text shown when the editor is empty. */
        placeholder: '',

        /** Start the editor in read-only mode. */
        readonly: false,

        /**
         * Show a settings (gear) button at the end of the toolbar that opens a
         * checklist letting the user dynamically show/hide toolbar tools
         * (including tools registered but not present in `tools`).
         * Default true.
         */
        showToolSettings: true,

        /**
         * Show a custom right-click menu, listing the currently visible
         * toolbar tools, when the user right-clicks over a text selection.
         * Default true.
         */
        showContextMenu: true,

        /**
         * Zero-argument change callback, for compatibility with call sites
         * that only need a notification rather than the changed value:
         *   pepEdit({ change: fn })
         */
        change: null,

        /** pepEdit-style change callback. ({ value }) */
        onChange: null,

        /** Fires when the editable area gains focus. () */
        onFocus: null,

        /** Fires when the editable area loses focus. () */
        onBlur: null,

        /**
         * Hook for custom image upload flows. Receives an `insert(url)` callback;
         * call it (sync or async) with the final image URL. When omitted, a
         * plain window.prompt() is used.
         * @example onImageSelect: function (insert) { openUploadDialog(insert); }
         */
        onImageSelect: null
    };

    // ════════════════════════════════════════════════════════════════════════
    //  PepEdit constructor
    // ════════════════════════════════════════════════════════════════════════
    function PepEdit($el, options) {
        this.$el   = $el;
        this._el   = $el[0];
        this._opts = $.extend({}, defaults, options);
        this._sourceViewOn = false;

        this._buildUi();
        this._wireEvents();

        // Seed the editable area from the original element's current value.
        this.value(this._el.value || '');

        if (this._opts.readonly || this._el.disabled || this._el.readOnly) {
            this.readonly(true);
        }
    }

    PepEdit.prototype = {

        // ── UI construction ──────────────────────────────────────────────────

        _buildUi: function () {
            this.$wrapper    = $('<div class="pe-editor"></div>');
            this.$toolbar    = $('<div class="pe-toolbar" role="toolbar" aria-label="Formatting"></div>');
            this.$toolsArea  = $('<div class="pe-tools-area"></div>');
            this.$editable   = $('<div class="pe-body" contenteditable="true" spellcheck="true"></div>');
            this.$sourceArea = $('<textarea class="pe-source"></textarea>');

            if (this._opts.placeholder) {
                this.$editable.attr('data-placeholder', this._opts.placeholder);
            }
            if (this._opts.height) {
                const h = typeof this._opts.height === 'number' ? this._opts.height + 'px' : this._opts.height;
                this.$editable.css('height', h);
            }

            // The configured tool list becomes a stable "spec": layout order
            // and separators are preserved, and it drives both the initial
            // render and the settings checklist below.
            this._toolSpec    = this._normalizeToolSpec(this._opts.tools || []);
            this._activeTools = new Set(this._specToolNames());
            this._extraTools  = []; // tools enabled later via the checklist that
                                     // weren't part of the original `tools` option

            this.$toolbar.append(this.$toolsArea);
            this._renderToolGroups();

            if (this._opts.showToolSettings) {
                this.$toolbar.append(this._buildSettingsControl());
            }

            this.$wrapper
                .append(this.$toolbar)
                .append(this.$editable)
                .append(this.$sourceArea);

            this.$el.hide().after(this.$wrapper);
            this.body = this.$editable[0];

            if (this._opts.showContextMenu) {
                this._buildContextMenu();
            }
        },

        /** Resolve tool-array items to names, dropping unknown ones (warns once). */
        _normalizeToolSpec: function (tools) {
            const spec = [];
            tools.forEach(function (item) {
                if (isSeparator(item)) { spec.push('|'); return; }
                const name = typeof item === 'string' ? item : item.name;
                if (!toolRegistry[name]) {
                    console.warn('pepEdit: skipping tool "' + name + '" (unknown tool).');
                    return;
                }
                spec.push(name);
            });
            return spec;
        },

        /** Unique tool names from `_toolSpec`, in layout order (separators excluded). */
        _specToolNames: function () {
            const seen = {}, names = [];
            (this._toolSpec || []).forEach(function (name) {
                if (name === '|' || seen[name]) { return; }
                seen[name] = true;
                names.push(name);
            });
            return names;
        },

        /** Re-render the toolbar's tool groups from `_toolSpec` + `_activeTools`/`_extraTools`. */
        _renderToolGroups: function () {
            const self = this;
            this.$toolsArea.empty();

            let $group = $('<div class="pe-tool-group"></div>');
            function flushGroup() {
                if ($group.children().length) { self.$toolsArea.append($group); }
                $group = $('<div class="pe-tool-group"></div>');
            }

            (this._toolSpec || []).forEach(function (name) {
                if (name === '|') { flushGroup(); return; }
                if (!self._activeTools.has(name)) { return; }
                $group.append(self._buildToolEl(name, toolRegistry[name]));
            });
            flushGroup();

            // Tools enabled at runtime via the settings checklist that weren't
            // part of the original layout render together in a trailing group.
            if (this._extraTools.length) {
                const $extra = $('<div class="pe-tool-group"></div>');
                this._extraTools.forEach(function (name) {
                    if (self._activeTools.has(name)) { $extra.append(self._buildToolEl(name, toolRegistry[name])); }
                });
                if ($extra.children().length) { this.$toolsArea.append($extra); }
            }

            this._updateToolbarState();
        },

        // ── Toolbar settings (show/hide tools) ───────────────────────────────

        _buildSettingsControl: function () {
            const self = this;

            this.$settingsPanel = $('<div class="pe-settings-panel"></div>').hide();
            this._settingsBtn = $('<button type="button" class="pe-btn pe-settings-btn" title="Toolbar settings" aria-haspopup="true" aria-expanded="false"><i class="bi bi-gear"></i></button>');

            this._settingsBtn.on('click', function (e) {
                e.stopPropagation();
                if (self.$settingsPanel.is(':visible')) { self._closeSettingsPanel(); } else { self._openSettingsPanel(); }
            });

            this._onDocClickSettings = function (e) {
                if (self.$settingsPanel.is(':visible')
                    && !self.$settingsPanel[0].contains(e.target)
                    && !self._settingsBtn[0].contains(e.target)) {
                    self._closeSettingsPanel();
                }
            };
            $(document).on('click.' + DATA_KEY + this._instanceId(), this._onDocClickSettings);

            return $('<div class="pe-settings-wrap"></div>').append(this._settingsBtn).append(this.$settingsPanel);
        },

        _openSettingsPanel: function () {
            this._renderSettingsPanel();
            this.$settingsPanel.show();
            this._settingsBtn.attr('aria-expanded', 'true').addClass('pe-active');
        },

        _closeSettingsPanel: function () {
            if (!this.$settingsPanel) { return; }
            this.$settingsPanel.hide();
            this._settingsBtn.attr('aria-expanded', 'false').removeClass('pe-active');
        },

        _renderSettingsPanel: function () {
            const self = this;
            const specNames = this._specToolNames();
            const otherNames = Object.keys(toolRegistry)
                .filter(function (n) { return specNames.indexOf(n) === -1; })
                .sort(function (a, b) {
                    return (toolRegistry[a].title || a).localeCompare(toolRegistry[b].title || b);
                });

            const $list = $('<div class="pe-settings-list"></div>');

            function addRow(name) {
                const def = toolRegistry[name];
                const id  = 'pe-set-' + self._instanceId() + '-' + name;
                const $cb = $('<input type="checkbox">').attr('id', id)
                    .prop('checked', self._activeTools.has(name))
                    .on('change', function () { self._toggleTool(name, this.checked); });

                $list.append(
                    $('<label class="pe-settings-item"></label>').attr('for', id)
                        .append($cb)
                        .append('<i class="bi ' + (def.icon || 'bi-sliders') + ' pe-settings-icon"></i>')
                        .append('<span>' + (def.title || name) + '</span>')
                );
            }

            specNames.forEach(addRow);
            if (otherNames.length) {
                $list.append('<div class="pe-settings-subtitle">More tools</div>');
                otherNames.forEach(addRow);
            }

            this.$settingsPanel.empty()
                .append('<div class="pe-settings-title">Toolbar tools</div>')
                .append($list);
        },

        _toggleTool: function (name, isActive) {
            if (isActive) {
                this._activeTools.add(name);
                if (this._specToolNames().indexOf(name) === -1 && this._extraTools.indexOf(name) === -1) {
                    this._extraTools.push(name);
                }
            } else {
                this._activeTools.delete(name);
            }
            this._renderToolGroups();
        },

        // ── Selection context menu (right-click on selected text) ────────────

        _buildContextMenu: function () {
            const self = this;
            const ns = DATA_KEY + this._instanceId() + '-ctx';
            this._ctxNs = ns;

            this.$contextMenu = $('<div class="pe-context-menu" role="menu"></div>').hide();
            $(document.body).append(this.$contextMenu);

            this.$editable.on('contextmenu', function (e) {
                const sel = window.getSelection();
                const hasSelection = sel && sel.rangeCount && !sel.isCollapsed && self.body.contains(sel.anchorNode);
                if (!hasSelection) { return; } // let the native menu show when nothing is selected

                e.preventDefault();
                self._saveSelection();
                self._openContextMenu(e.clientX, e.clientY);
            });

            $(document).on('click.' + ns, function (e) {
                if (self.$contextMenu.is(':visible') && !self.$contextMenu[0].contains(e.target)) {
                    self._closeContextMenu();
                }
            });
            $(document).on('keydown.' + ns, function (e) {
                if (e.key === 'Escape') { self._closeContextMenu(); }
            });
            $(window).on('scroll.' + ns + ' resize.' + ns, function () { self._closeContextMenu(); });
        },

        _openContextMenu: function (x, y) {
            this._renderContextMenu();
            this.$contextMenu.css({ visibility: 'hidden', display: 'block', left: 0, top: 0 });

            const menuW = this.$contextMenu.outerWidth();
            const menuH = this.$contextMenu.outerHeight();
            const maxX = Math.max(8, window.innerWidth - menuW - 8);
            const maxY = Math.max(8, window.innerHeight - menuH - 8);

            this.$contextMenu.css({
                left: Math.min(x, maxX) + 'px',
                top: Math.min(y, maxY) + 'px',
                visibility: 'visible'
            });
        },

        _closeContextMenu: function () {
            if (this.$contextMenu) { this.$contextMenu.hide(); }
        },

        _renderContextMenu: function () {
            const self = this;
            const names = this.getActiveTools();

            this.$contextMenu.empty();
            if (!names.length) {
                this.$contextMenu.append('<div class="pe-ctx-empty">No tools enabled</div>');
                return;
            }
            names.forEach(function (name) {
                const def = toolRegistry[name];
                if (def) { self.$contextMenu.append(self._buildContextMenuRow(name, def)); }
            });
        },

        _buildContextMenuRow: function (name, def) {
            const self = this;

            if (def.type === 'dropdown') {
                const $select = this._buildToolEl(name, def).on('change', function () { self._closeContextMenu(); });
                return $('<div class="pe-ctx-row pe-ctx-row-select"></div>')
                    .append('<span class="pe-ctx-label">' + def.title + '</span>')
                    .append($select);
            }

            if (def.type === 'color') {
                const $group = this._buildToolEl(name, def);
                $group.find('.pe-color-input').on('change', function () { self._closeContextMenu(); });
                return $('<div class="pe-ctx-row"></div>')
                    .append($group)
                    .append('<span class="pe-ctx-label">' + def.title + '</span>');
            }

            const $row = $('<button type="button" class="pe-ctx-row pe-ctx-btn"></button>')
                .attr('data-tool', name)
                .append('<i class="bi ' + def.icon + ' pe-ctx-icon"></i>')
                .append('<span class="pe-ctx-label">' + def.title + '</span>');

            $row.on('mousedown', function (e) { e.preventDefault(); });
            $row.on('click', function () {
                self._restoreSelection();
                self._exec(name, def);
                self._closeContextMenu();
                self.$editable.trigger('focus');
            });

            return $row;
        },

        _buildToolEl: function (name, def) {
            const self = this;

            if (def.type === 'dropdown') {
                const $select = $('<select class="pe-select"></select>').attr('data-tool', name).attr('title', def.title);
                if (def.placeholder) { $select.append('<option value="">' + def.placeholder + '</option>'); }
                (def.items || []).forEach(function (opt) {
                    $select.append($('<option></option>').attr('value', opt.value).text(opt.text));
                });
                $select.on('mousedown', function () { self._saveSelection(); });
                $select.on('change', function () {
                    self._focusAndRestoreSelection();
                    self._exec(name, def, $select.val());
                });
                return $select;
            }

            if (def.type === 'color') {
                const $group = $('<span class="pe-color-group"></span>');
                const $btn = $('<button type="button" class="pe-btn pe-color-btn"></button>')
                    .attr('title', def.title).attr('data-tool', name)
                    .append('<i class="bi ' + def.icon + '"></i>')
                    .append('<span class="pe-color-swatch"></span>');
                const $input = $('<input type="color" class="pe-color-input" tabindex="-1">');

                $btn.on('mousedown', function (e) { e.preventDefault(); self._saveSelection(); });
                $btn.on('click', function () { $input.trigger('click'); });
                // 'input' fires repeatedly while the native color popup is still
                // open (live preview as the user drags); only touch the swatch
                // preview here. Applying the command / refocusing the editor
                // mid-interaction would steal focus from that still-open native
                // popup and force it to close prematurely.
                $input.on('input', function () {
                    $btn.find('.pe-color-swatch').css('background-color', $input.val());
                });
                // 'change' fires once, after the popup closes with a final value.
                $input.on('change', function () {
                    self._focusAndRestoreSelection();
                    self._exec(name, def, $input.val());
                    $btn.find('.pe-color-swatch').css('background-color', $input.val());
                });

                return $group.append($btn).append($input);
            }

            // Plain toggle/action button
            const $button = $('<button type="button" class="pe-btn"></button>')
                .attr('title', def.title).attr('data-tool', name)
                .append('<i class="bi ' + def.icon + '"></i>');

            $button.on('mousedown', function (e) { e.preventDefault(); self._saveSelection(); });
            $button.on('click', function () {
                self._restoreSelection();
                self._exec(name, def);
                self.$editable.trigger('focus');
                self._updateToolbarState();
            });

            return $button;
        },

        /**
         * Return browser focus to the editable area and re-apply the saved
         * selection range. Must run BEFORE any execCommand call triggered from
         * a control that steals focus (e.g. a <select> or a native
         * <input type="color">/its OS color-picker dialog) — otherwise
         * document.activeElement is no longer the editor and the command is
         * silently dropped or applied to the wrong place, which looks like
         * "losing the selection".
         */
        _focusAndRestoreSelection: function () {
            this.body.focus();
            this._restoreSelection();
        },

        _exec: function (name, def, value) {
            if (typeof def.exec === 'function') {
                def.exec(this, value);
            } else if (def.command) {
                document.execCommand(def.command, false, value != null ? value : null);
            }
            this._syncField();
            this._triggerChange();
        },

        // ── Selection handling ───────────────────────────────────────────────

        _saveSelection: function () {
            const sel = window.getSelection();
            if (sel && sel.rangeCount && this.body.contains(sel.anchorNode)) {
                this._savedRange = sel.getRangeAt(0).cloneRange();
            }
        },

        _restoreSelection: function () {
            if (!this._savedRange) { return; }
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(this._savedRange);
        },

        _updateToolbarState: function () {
            this.$toolbar.find('.pe-btn[data-tool]').each(function () {
                const name = $(this).data('tool');
                const def = toolRegistry[name];
                if (!def || !def.command || def.type === 'color') { return; }
                let active = false;
                try { active = document.queryCommandState(def.command); } catch (e) { /* ignore */ }
                $(this).toggleClass('pe-active', !!active);
            });
        },

        // ── Wiring ────────────────────────────────────────────────────────────

        _wireEvents: function () {
            const self = this;

            this._onInput = debounce(function () {
                self._syncField();
                self._triggerChange();
            }, 150);
            this.$editable.on('input', this._onInput);

            this.$editable.on('focus', function () { self._trigger('focus'); });
            this.$editable.on('blur', function () { self._syncField(); self._trigger('blur'); });

            this._onSelectionChange = debounce(function () {
                const sel = window.getSelection();
                if (sel && sel.rangeCount && self.body.contains(sel.anchorNode)) {
                    self._updateToolbarState();
                }
            }, 100);
            $(document).on('selectionchange.' + DATA_KEY + this._instanceId(), this._onSelectionChange);

            this._onSubmit = function () { self._syncField(); };
            this.$form = this.$el.closest('form');
            if (this.$form.length) { this.$form.on('submit.' + DATA_KEY, this._onSubmit); }
        },

        _instanceId: function () {
            if (!this._id) { this._id = 'pe' + Math.random().toString(36).slice(2); }
            return this._id;
        },

        _trigger: function (name, detail) {
            const cbKey = 'on' + name.charAt(0).toUpperCase() + name.slice(1);
            const data = detail || {};
            this.$el.trigger('pepedit:' + name.toLowerCase(), [data]);
            if (typeof this._opts[cbKey] === 'function') { this._opts[cbKey].call(this._el, data); }
        },

        _syncField: function () {
            this._el.value = this.$editable.html();
        },

        _triggerChange: function () {
            this._trigger('change', { value: this._el.value });
            // Zero-arg callback for consumers that only need a notification.
            if (typeof this._opts.change === 'function') { this._opts.change.call(this._el); }
        },

        _toggleSourceView: function () {
            this._sourceViewOn = !this._sourceViewOn;
            if (this._sourceViewOn) {
                this.$sourceArea.val(this.$editable.html());
                this.$editable.hide();
                this.$sourceArea.show().trigger('focus');
            } else {
                this.$editable.html(this.$sourceArea.val()).show();
                this.$sourceArea.hide();
                this._syncField();
                this._triggerChange();
            }
        },

        // ── Public API ────────────────────────────────────────────────────────

        /**
         * Get or set the editor's HTML content.
         * @param {string} [html]  When provided, replaces the content.
         * @returns {string|undefined}
         */
        value: function (html) {
            if (arguments.length === 0) {
                return this._sourceViewOn ? this._el.value : this.$editable.html();
            }
            this.$editable.html(html || '');
            this._syncField();
            return undefined;
        },

        /** Give keyboard focus to the editable area. */
        focus: function () {
            this.$editable.trigger('focus');
            return this.$el;
        },

        /**
         * Enable or disable editing.
         * @param {boolean} isReadonly
         */
        readonly: function (isReadonly) {
            this.$editable.attr('contenteditable', !isReadonly);
            this.$toolbar.find('button, select, input').prop('disabled', !!isReadonly);
            this.$wrapper.toggleClass('pe-readonly', !!isReadonly);
            return this.$el;
        },

        /** Run any execCommand-backed or custom-registered tool programmatically. */
        execTool: function (name, value) {
            const def = toolRegistry[name];
            if (!def) { $.error('pepEdit: unknown tool "' + name + '".'); return this.$el; }
            this.focus();
            this._exec(name, def, value);
            return this.$el;
        },

        /** Names of currently visible toolbar tools, in rendered order. */
        getActiveTools: function () {
            const self = this;
            return this._specToolNames().concat(this._extraTools)
                .filter(function (n) { return self._activeTools.has(n); });
        },

        /**
         * Replace the set of visible toolbar tools and re-render.
         * Accepts any registered tool name, including ones not in the
         * original `tools` option.
         * @param {Array<string>} names
         */
        setActiveTools: function (names) {
            const self = this;
            const valid = (names || []).filter(function (n) { return !!toolRegistry[n]; });
            this._activeTools = new Set(valid);
            const spec = this._specToolNames();
            this._extraTools = valid.filter(function (n) { return spec.indexOf(n) === -1; });
            this._renderToolGroups();
            if (this.$settingsPanel && this.$settingsPanel.is(':visible')) { this._renderSettingsPanel(); }
            return this.$el;
        },

        /** Destroy the editor: restore the original element and remove plugin data. */
        destroy: function () {
            $(document).off('selectionchange.' + DATA_KEY + this._instanceId());
            $(document).off('click.' + DATA_KEY + this._instanceId());
            if (this._ctxNs) {
                $(document).off('click.' + this._ctxNs).off('keydown.' + this._ctxNs);
                $(window).off('scroll.' + this._ctxNs + ' resize.' + this._ctxNs);
            }
            if (this.$contextMenu) { this.$contextMenu.remove(); }
            if (this.$form && this.$form.length) { this.$form.off('submit.' + DATA_KEY, this._onSubmit); }
            this._syncField();
            this.$wrapper.remove();
            this.$el.show();
            this.$el.removeData(DATA_KEY);
        }
    };

    // ════════════════════════════════════════════════════════════════════════
    //  jQuery plugin bridge
    // ════════════════════════════════════════════════════════════════════════
    $.fn.pepEdit = function (optionsOrMethod) {
        const args = Array.prototype.slice.call(arguments, 1);
        let returnVal = this;

        this.each(function () {
            const $el = $(this);
            let instance = $el.data(DATA_KEY);

            if (typeof optionsOrMethod === 'string') {
                if (!instance) {
                    $.error('pepEdit has not been initialized on this element.');
                    return;
                }
                if (typeof instance[optionsOrMethod] !== 'function') {
                    $.error('pepEdit: method "' + optionsOrMethod + '" does not exist.');
                    return;
                }
                const result = instance[optionsOrMethod].apply(instance, args);
                if (result !== undefined) { returnVal = result; return false; }
            } else {
                if (!instance) {
                    instance = new PepEdit($el, optionsOrMethod || {});
                    $el.data(DATA_KEY, instance);
                }
            }
        });

        return returnVal;
    };

    /** Register a custom tool, extending the toolbar registry (see file header). */
    $.fn.pepEdit.registerTool = registerTool;
    /** Read-only introspection of the current tool registry. */
    $.fn.pepEdit.tools = toolRegistry;

}(jQuery));

