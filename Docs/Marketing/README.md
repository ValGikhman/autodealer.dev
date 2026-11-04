# AutoDealer.dev print brochure

`AutoDealer.dev-Trifold-Brochure.pdf` is the approved two-page, six-panel brochure. It uses charcoal and light-gray backgrounds, current product screenshots, and light dealership humor. The PDF contains no interactive annotations, section numbering, or decorative arrows.

## Printing and folding

- US Letter paper, landscape (11 by 8.5 inches).
- Print both pages on one sheet at 100% actual size.
- For duplex printing, flip on the short edge.
- With the inside facing up, fold the narrower right panel inward, then fold the left panel over it.
- Outside panel widths, left to right: 3.625, 3.6875, and 3.6875 inches. The inside reverses this order.
- Essential content is inset at least a quarter inch. Backgrounds extend to the page edge; non-borderless printers may leave an unprinted outer margin.
- Print one test sheet to confirm the printer's duplex orientation before printing a batch.

## Regeneration

Run `build_brochure.py` with Python 3 and the `reportlab`, `Pillow`, `pypdf`, and `pypdfium2` packages installed. The script uses Windows Segoe UI font files from `C:/Windows/Fonts` and the existing images in `Content/images`. It writes the brochure alongside this README and renders both sides to a temporary directory for visual review.

Review both rendered sides after every text or layout change. Confirm the PDF still has two landscape Letter pages, no interactive annotations, and adequate clearance around folds and the footer.
