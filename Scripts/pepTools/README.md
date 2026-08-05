# PepTools

A collection of lightweight, dependency-free (beyond jQuery and Bootstrap
Icons) jQuery UI components for PepConnect:

- **[PepGrid](#pepgrid)** — a data grid with sorting, filtering, paging, grouping, and a clean public API.
- **[PepEdit](#pepedit)** — a WYSIWYG rich-text editor with an extensible toolbar.

Both plugins share the same conventions: an IIFE module, a `pep-`-style class
prefix (`pg-` for PepGrid, `pe-` for PepEdit), a jQuery plugin bridge
(`$(el).pepGrid(...)` / `$(el).pepEdit(...)`), and events that fire both as
jQuery events and as option callbacks.

```
wwwroot/
  css/pepTools/pepGrid.css  – Grid styles (pg-* prefix)
  css/pepTools/pepEdit.css  – Editor styles (pe-* prefix)
  js/pepTools/pepGrid.js    – PepGrid jQuery plugin
  js/pepTools/pepEdit.js    – PepEdit jQuery plugin
  js/pepTools/README.md     – This file
```

---

## PepGrid

A lightweight jQuery data grid for PepConnect. Supports client-side sorting, column filtering with value dropdowns, pagination, row/cell events, and a clean public API — zero external dependencies beyond jQuery and Bootstrap Icons.

### Quick Start

#### 1. Add the assets

Include both files **after** jQuery and Bootstrap Icons in your view:

```html
@section Styles {
    <link rel="stylesheet" href="~/css/pepTools/pepGrid.css?version=@Configuration["AppSettings:PepConnectVersion"]" />
}

@section Scripts {
    <script src="~/js/pepTools/pepGrid.js?version=@Configuration["AppSettings:PepConnectVersion"]"></script>
    <script src="~/js/MyPage/MyPage.js?version=@Configuration["AppSettings:PepConnectVersion"]"></script>
}
```

#### 2. Add a container element

```html
<div id="myGrid"></div>
```

#### 3. Initialize in JavaScript

```javascript
$('#myGrid').pepGrid({
    url: '/api/my-data',
    pageSize: 50,
    columns: [
        { field: 'Id',   hidden: true },
        { field: 'Name', title: 'Full Name' },
        { field: 'Status', title: 'Status', width: 160 }
    ]
});
```

### Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `url` | `string` | `null` | AJAX URL for data. Mutually exclusive with `data`. |
| `data` | `Array` | `null` | Static data array. When provided, `url` is ignored. |
| `schema` | `object` | `null` | `{ data: fn(response) }` — extracts the records array from the AJAX response. |
| `pageSize` | `number` | `50` | Rows per page. |
| `defaultSort` | `Array` | `[]` | Initial sort: `[{ field: 'Name', dir: 'asc' }]`. |
| `columns` | `Array` | `[]` | Column definitions (see below). |
| `multiSelect` | `boolean` | `false` | Allow multiple rows selected simultaneously (click to toggle). |
| `height` | `string\|null` | `'85vh'` | CSS height of the grid wrapper. `null` for auto height (no internal scroll). |
| `autozoomable` | `boolean` | `false` | Show a full-value popup when hovering a cell whose content is currently ellipsized. Can be overridden per column. |
| `resizable` | `boolean` | `false` | Show header resize handles. Dragging resizes only the active column and clamps it to a minimum width of `50px`. |
| `showSearch` | `boolean` | `true` | Show the quick-search bar above the table. Set `false` to hide it. |
| `groupable` | `boolean` | `false` | Enable grouping by drag. Shows a group bar above the toolbar; drag any column header label into it to group rows by that column's values. |
| `defaultGroups` | `Array` | `[]` | Initial group fields: `['Status', 'Region']`. Requires `groupable: true`. |

#### Column definition

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `field` | `string` | — | **Required.** Property name on the data object. |
| `title` | `string` | `field` | Column header label. |
| `width` | `number\|string` | — | Column width. Numbers are treated as pixels. |
| `hidden` | `boolean` | `false` | Exclude column from rendering entirely. Hidden fields still exist in `dataItem`. |
| `sortable` | `boolean` | `true` | Show sort icon and allow clicking to sort. |
| `filterable` | `boolean` | `true` | Show filter button and allow value-checkbox filtering. |
| `searchable` | `boolean` | `true` (`false` for template columns) | Include in quick-search filtering and highlight matches. Template columns default to `false`; set `true` to search on the underlying field value. |
| `encoded` | `boolean` | `true` | `false` = render cell value as raw HTML. Prefer `template` for action columns. |
| `autozoomable` | `boolean` | inherits grid `autozoomable` | Show the full-value popup for this column when the rendered cell is ellipsized. Set `false` to opt a column out. |
| `resizable` | `boolean` | inherits grid `resizable` | Set `false` on a column to keep its header fixed when grid resizing is enabled. |
| `template` | `string` | — | CSS selector for a `<script type="text/x-pepgrid-template">` element in the page. HTML is stamped per row; use `{{FieldName}}` tokens to interpolate data values. Implies raw HTML rendering. |

**Special field name:** `'Selection'` renders a select-all checkbox (`pg-select-all-cb` class) in the header. Use `template` for the row checkboxes.

### Cell templates

Define reusable HTML in `<script type="text/x-pepgrid-template">` blocks anywhere in the page body (they are inert — browsers do not execute them). Reference the block by `id` in the column definition.

```html
<!-- In your Razor view / HTML -->
<script type="text/x-pepgrid-template" id="actionTemplate">
    <button class="btn btn-sm btn-outline-secondary view-btn" data-id="{{Id}}">
        <i class="bi bi-eye"></i> View
    </button>
    <button class="btn btn-sm btn-outline-danger delete-btn" data-id="{{Id}}" data-name="{{Name}}">
        <i class="bi bi-trash"></i>
    </button>
</script>

<script type="text/x-pepgrid-template" id="selectionTemplate">
    <input type="checkbox" class="row-cb" title="Select row">
</script>
```

```javascript
$('#myGrid').pepGrid({
    url: '/api/data',
    columns: [
        { field: 'Selection', template: '#selectionTemplate', sortable: false, filterable: false },
        { field: 'Name',    title: 'Name' },
        { field: 'Actions', title: 'Actions', width: 140, sortable: false, filterable: false,
          template: '#actionTemplate' }
    ]
});
```

`{{FieldName}}` tokens are replaced with the row's data value for that field. Any field in the `dataItem` can be referenced — it does not have to be the column's own `field`.

### Methods

Call methods via the plugin bridge:

```javascript
$('#myGrid').pepGrid('methodName', arg1, arg2);
```

| Method | Arguments | Returns | Description |
|--------|-----------|---------|-------------|
| `refresh()` | — | `jQuery` | Reload data from the server (url mode) or re-render (static mode). |
| `setData(arr)` | `arr: Array` | `jQuery` | Replace data, reset page/sort/filters, and re-render. |
| `getDataItem(trEl)` | `trEl: HTMLElement` | `object\|null` | Return the data object bound to a `<tr>` element. |
| `getSelectedItems()` | — | `Array` | Return data items for all currently selected rows. |
| `clearSelection()` | — | `jQuery` | Deselect all rows. |
| `clearFilters()` | — | `jQuery` | Remove all column filters and re-render. |
| `clearSort()` | — | `jQuery` | Remove active sort and re-render. |
| `getSortState()` | — | `Array` | Copy of the current sort: `[{ field, dir }]`. |
| `getFilterState()` | — | `object` | Current filters: `{ fieldName: ['val1', 'val2'] }`. |
| `getGroupState()` | — | `Array` | Copy of the current group fields: `['Status', 'Region']`. |
| `setGroups(fields)` | `fields: Array` | `jQuery` | Set group fields programmatically and re-render. Resets collapse state. |
| `clearGroups()` | — | `jQuery` | Remove all groups and re-render. |
| `destroy()` | — | `jQuery` | Clear DOM content and remove plugin data. |

#### Example

```javascript
// Reload data
$('#myGrid').pepGrid('refresh');

// Get data for the row that was clicked
$(document).on('click', '.my-action-btn', function () {
    var item = $('#myGrid').pepGrid('getDataItem', $(this).closest('tr')[0]);
    console.log(item);
});

// Replace with new data
$.get('/api/filtered-data', function (resp) {
    $('#myGrid').pepGrid('setData', resp.Data);
});
```

### Events

Events fire in two ways simultaneously:

1. **jQuery event** on the container element — subscribe with `.on()`:
   ```javascript
   $('#myGrid').on('pepgrid:rowclick', function (e, data) {
       console.log(data.dataItem);
   });
   ```

2. **Option callback** — pass as an option during init:
   ```javascript
   $('#myGrid').pepGrid({
       url: '/api/data',
       columns: [...],
       onRowClick: function (data) {
           console.log(data.dataItem);
       }
   });
   ```

#### Event reference

| jQuery event | Callback option | Payload | Fires when |
|---|---|---|---|
| `pepgrid:beforeload` | `onBeforeLoad` | `{}` | Before the AJAX request is sent |
| `pepgrid:databound` | `onDataBound` | `{ data, total }` | After data is fetched and rendered |
| `pepgrid:rowclick` | `onRowClick` | `{ dataItem, rowElement, event }` | A row is clicked |
| `pepgrid:rowdblclick` | `onRowDblClick` | `{ dataItem, rowElement, event }` | A row is double-clicked |
| `pepgrid:rowcontextmenu` | `onRowContextMenu` | `{ dataItem, rowElement, event }` | Right-click on a row |
| `pepgrid:cellclick` | `onCellClick` | `{ dataItem, field, value, cellElement, rowElement, columnIndex, event }` | A cell is clicked |
| `pepgrid:celldblclick` | `onCellDblClick` | `{ dataItem, field, value, cellElement, rowElement, columnIndex, event }` | A cell is double-clicked |
| `pepgrid:rowselect` | `onRowSelect` | `{ dataItem, rowElement }` | A row transitions to selected state |
| `pepgrid:rowdeselect` | `onRowDeselect` | `{ dataItem, rowElement }` | A row transitions to deselected state |
| `pepgrid:selectionchange` | `onSelectionChange` | `{ selected: [...dataItems] }` | The selection set changes |
| `pepgrid:sortchange` | `onSortChange` | `{ sort: [{ field, dir }] }` | Sort state changes |
| `pepgrid:filterchange` | `onFilterChange` | `{ filters: { field: [...values] } }` | A filter is applied or cleared |
| `pepgrid:searchchange` | `onSearchChange` | `{ term, matchCount }` | Search term changes (debounced 200ms) |
| `pepgrid:pagechange` | `onPageChange` | `{ page, pageSize, total }` | User navigates to a different page |
| `pepgrid:groupchange` | `onGroupChange` | `{ groups: ['field', …] }` | Group fields change (drag, remove, or programmatic) |

### Full example

#### Grouping by drag

```javascript
$('#myGrid').pepGrid({
    url:      '/api/items',
    groupable: true,                         // show group bar
    defaultGroups: ['Status'],               // start grouped by Status
    columns: [
        { field: 'Id',     hidden: true },
        { field: 'Name',   title: 'Name' },
        { field: 'Status', title: 'Status', width: 160 },
        { field: 'Region', title: 'Region', width: 140 }
    ],
    onGroupChange: function (data) {
        console.log('Groups:', data.groups);  // e.g. ['Status', 'Region']
    }
});

// Programmatic grouping
$('#myGrid').pepGrid('setGroups', ['Region', 'Status']);

// Remove all groups
$('#myGrid').pepGrid('clearGroups');

// Read current groups
var groups = $('#myGrid').pepGrid('getGroupState');
```

**How it works:**
1. A group bar appears above the toolbar with placeholder text.
2. Drag any column header label (the column name text) and drop it onto the group bar to group by that column.
3. Groups are shown as collapsible chips in the group bar. Click a chip's **×** to remove it, or drag chips to reorder them.
4. Click a group header row in the grid to collapse/expand that group.
5. Filters, search, and sort all work inside grouped mode. While grouped, pagination is suspended and all rows are shown.

#### AJAX data with events

```javascript
$('#promotionGrid').pepGrid({
    url:         '/PromotionRating/Read',
    schema:      { data: function (resp) { return resp.Data || resp; } },
    pageSize:    50,
    defaultSort: [{ field: 'Name', dir: 'asc' }],
    columns: [
        { field: 'Id',     hidden: true },
        { field: 'Name',   title: 'Promotion Name' },
        { field: 'Status', title: 'Status', width: 160 },
        { field: 'Actions', title: 'Actions', width: 120, sortable: false, filterable: false, encoded: false }
    ],
    onRowClick: function (data) {
        console.log('Clicked:', data.dataItem.Name);
    },
    onDataBound: function (data) {
        console.log('Loaded ' + data.total + ' records.');
    }
});

// Subscribe to events after init
$('#promotionGrid').on('pepgrid:sortchange', function (e, data) {
    console.log('Sort:', data.sort);
});

// Reload button
$('#reloadBtn').on('click', function () {
    $('#promotionGrid').pepGrid('refresh');
});
```

#### Static data

```javascript
$('#simpleGrid').pepGrid({
    data: [
        { id: 1, name: 'Alice', role: 'Admin' },
        { id: 2, name: 'Bob',   role: 'User' }
    ],
    height: null,  // auto height, no internal scroll
    columns: [
        { field: 'id',   title: 'ID',   width: 60 },
        { field: 'name', title: 'Name' },
        { field: 'role', title: 'Role', width: 120 }
    ]
});

// Inject fresh data later
$.get('/api/users', function (users) {
    $('#simpleGrid').pepGrid('setData', users);
});
```

#### Selection column with checkboxes

The `Selection` field is a special sentinel that renders a select-all checkbox in the header. Use a `template` for the per-row checkboxes:

```html
<!-- In the view -->
<script type="text/x-pepgrid-template" id="rowCbTemplate">
    <input type="checkbox" class="row-cb" title="Select row">
</script>
```

```javascript
$('#myGrid').pepGrid({
    url: '/api/data',
    columns: [
        { field: 'Selection', template: '#rowCbTemplate', width: 40, sortable: false, filterable: false },
        { field: 'Name', title: 'Name' }
    ]
});

// Wire up select-all
$(document).on('click', '.pg-select-all-cb', function () {
    $('.row-cb').prop('checked', this.checked);
});
```

#### Context menu

```javascript
$('#myGrid').on('pepgrid:rowcontextmenu', function (e, data) {
    e.event.preventDefault();
    showContextMenu(e.event.clientX, e.event.clientY, data.dataItem);
});
```

#### Multi-select mode

```javascript
$('#myGrid').pepGrid({
    url: '/api/data',
    multiSelect: true,
    columns: [...],
    onSelectionChange: function (data) {
        console.log(data.selected.length + ' rows selected');
    }
});

$('#deleteSelectedBtn').on('click', function () {
    var items = $('#myGrid').pepGrid('getSelectedItems');
    // items is an array of data objects
});
```

### CSS customisation

All styles use the `pg-` prefix and are defined in `pepGrid.css`. Override any variable in your page stylesheet:

```css
/* Wider filter dropdown */
.pg-filter-dropdown { width: 300px; }

/* Custom row hover colour */
.pg-grid-table tbody tr:hover td { background: #d6e4f7; }

/* Custom selected row colour */
.pg-row-selected td { background: #cfe2ff !important; }

/* Taller rows */
.pg-grid-table td { padding: 0.5rem 0.65rem; }
```

### Browser support

IE 11 is **not** supported. PepGrid requires:
- `Map`, `Set`, `Array.from`, `Set.forEach`
- `Element.closest`
- CSS `position: sticky`

All modern browsers (Chrome, Edge, Firefox, Safari) are fully supported.

---

## PepEdit

A lightweight, jQuery WYSIWYG rich-text editor for PepConnect. Attaches
to a `<textarea>`, keeps it in sync for normal form postback, and exposes an
extensible tool registry so new toolbar buttons can be added without touching
the plugin core — zero external dependencies beyond jQuery and Bootstrap Icons.

### Quick Start

#### 1. Add the assets

Include both files **after** jQuery and Bootstrap Icons in your view:

```html
@section Styles {
    <link rel="stylesheet" href="~/css/pepTools/pepEdit.css?version=@Configuration["AppSettings:PepConnectVersion"]" />
}

@section Scripts {
    <script src="~/js/pepTools/pepEdit.js?version=@Configuration["AppSettings:PepConnectVersion"]"></script>
    <script src="~/js/MyPage/MyPage.js?version=@Configuration["AppSettings:PepConnectVersion"]"></script>
}
```

#### 2. Add a `<textarea>`

pepEdit attaches to a textarea and hides it, keeping its `value` in sync so the
containing `<form>` posts the HTML.

```html
<textarea name="Body" id="Body">@Model.Body</textarea>
```

#### 3. Initialize in JavaScript

```javascript
$('#Body').pepEdit({
    height: 300,
    placeholder: 'Type a message…'
});
```

### Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `tools` | `Array` | full default set | Toolbar tool names, in order. Use `'\|'` or `{ type: 'separator' }` as a divider. |
| `height` | `number\|string` | `300` | Height of the editable area. Numbers are treated as pixels. |
| `placeholder` | `string` | `''` | Text shown (via CSS) when the editor is empty. |
| `readonly` | `boolean` | `false` | Start the editor read-only. Also honors the textarea's `disabled`/`readonly` attribute. |
| `showToolSettings` | `boolean` | `true` | Show a gear button at the end of the toolbar that opens a checklist to dynamically show/hide tools — including tools registered but not present in `tools`. |
| `showContextMenu` | `boolean` | `true` | Show a custom right-click menu, listing the currently visible toolbar tools, when the user right-clicks over a text selection. |
| `change` | `function()` | `null` | Zero-argument change callback (alias for `onChange`). |
| `onChange` | `function({value})` | `null` | Change callback with payload. |
| `onFocus` / `onBlur` | `function()` | `null` | Fire when the editable area gains/loses focus. |
| `onImageSelect` | `function(insert)` | `null` | Hook for a custom image-upload dialog. Call `insert(url)` (sync or async) with the final URL. Falls back to `window.prompt()` when omitted. |

#### Default tools
```
bold, italic, underline, strikethrough, subscript, superscript, | foreColor, backColor, | justifyLeft, justifyCenter, justifyRight, justifyFull, |
insertOrderedList, insertUnorderedList, indent, outdent, | createLink, unlink, insertImage, | formatting, fontName, fontSize, | cleanFormatting, viewHtml, | undo, redo
```

### Toolbar settings (show/hide tools)

With `showToolSettings: true` (the default), a gear icon renders at the far
right of the toolbar. Clicking it opens a checklist of every tool — the ones
currently in the toolbar (in their configured order) plus every other tool
registered in `$.fn.pepEdit.tools` under a **More tools** section — so end
users (or you, while testing) can toggle tools on/off live, without
reinitializing the editor:

- Unchecking a tool removes it from the toolbar immediately.
- Checking a tool registered but not part of the original `tools` array adds
  it to a small trailing group at the end of the toolbar.
- The change is runtime-only (per page load); persist it yourself via
  `getActiveTools()`/`setActiveTools()` if you need it to stick.

```javascript
var editor = $('#Body').data('pepEdit');
editor.getActiveTools();               // ['bold', 'italic', ...] currently visible
editor.setActiveTools(['bold', 'italic', 'createLink', 'undo', 'redo']);
```

Set `showToolSettings: false` to hide the gear button entirely for a fixed toolbar.

### Selection context menu

When `showContextMenu: true` (the default) and the user right-clicks while
some text is selected, pepEdit shows its own popup menu — listing every tool
currently visible in the toolbar (in the same order) — instead of the native
browser context menu:

- Clicking a formatting tool applies it to the selection immediately, same as
  clicking its toolbar button.
- The list always mirrors `getActiveTools()`, so hiding/showing tools via the
  settings checklist (or `setActiveTools()`) automatically updates what shows
  up in the right-click menu too.
- Right-clicking with **no** selection falls through to the browser's native
  context menu, so cut/copy/paste/spell-check suggestions are unaffected.

Set `showContextMenu: false` to always use the native browser menu instead.

### Public API

```javascript
var editor = $('#Body').data('pepEdit');
```

| Method | Arguments | Returns | Description |
|--------|-----------|---------|-------------|
| `value()` | — | `string` | Get the current HTML content. |
| `value(html)` | `html: string` | — | Replace the content and sync the underlying textarea. |
| `focus()` | — | `jQuery` | Focus the editable area. |
| `readonly(state)` | `state: boolean` | `jQuery` | Toggle read-only mode. |
| `execTool(name, value)` | `name: string, value?` | `jQuery` | Run any registered tool programmatically. |
| `getActiveTools()` | — | `Array<string>` | Names of tools currently visible in the toolbar, in order. |
| `setActiveTools(names)` | `names: Array<string>` | `jQuery` | Show exactly these tools (any registered name); re-renders the toolbar. |
| `destroy()` | — | — | Restore the original `<textarea>` and remove plugin data. |

Methods can also be called through the plugin bridge:

```javascript
$('#Body').pepEdit('value', '<p>Hello</p>');
var html = $('#Body').pepEdit('value');
```

`editor.body` is the **raw** `contenteditable` DOM element (not jQuery-wrapped)
— useful for direct DOM access, e.g. `editor.body.onfocus = fn;`.

### Events

Events fire in two ways simultaneously, same as PepGrid:

```javascript
$('#Body').on('pepedit:change', function (e, data) {
    console.log(data.value);
});

$('#Body').pepEdit({
    onChange: function (data) { console.log(data.value); }
});
```

| jQuery event | Callback option | Payload | Fires when |
|---|---|---|---|
| `pepedit:change` | `onChange` (+ `change`) | `{ value }` | Content changes (debounced 150ms), a tool executes, or on blur |
| `pepedit:focus` | `onFocus` | `{}` | The editable area gains focus |
| `pepedit:blur` | `onBlur` | `{}` | The editable area loses focus |

### Extending: adding a custom tool

pepEdit's toolbar is backed by a registry, so new tools don't require editing
`pepEdit.js`:

```javascript
$.fn.pepEdit.registerTool('highlight', {
    icon: 'bi-highlighter',
    title: 'Highlight',
    command: 'hiliteColor',
    // exec(editor, value) overrides `command` entirely for custom behavior:
    // exec: function (editor, value) { ... }
});

$('#Body').pepEdit({
    tools: ['bold', 'italic', '|', 'highlight', '|', 'undo', 'redo']
});
```

A tool descriptor supports:

| Property | Description |
|---|---|
| `type` | `'button'` (default), `'dropdown'`, or `'color'` |
| `icon` | Bootstrap Icons class (e.g. `'bi-type-bold'`) |
| `title` | Tooltip / accessible label |
| `command` | `document.execCommand` name, run automatically on click |
| `items` | `[{ text, value }]` — dropdown options (`type: 'dropdown'` only) |
| `exec(editor, value)` | Custom handler; overrides the default `command` execution |

Custom tools registered this way automatically show up in the toolbar
settings checklist (see above) under **More tools**, so users can opt in to
them without you adding them to the default `tools` array.

### CSS customisation

All styles use the `pe-` prefix and are defined in `pepEdit.css`:

```css
/* Taller default editor */
.pe-body { min-height: 400px; }

/* Custom toolbar background */
.pe-toolbar { background: #fff; }
```

### Browser support

Uses `contenteditable` and `document.execCommand` (still supported for these
common commands in all evergreen browsers). IE 11 is not supported.
