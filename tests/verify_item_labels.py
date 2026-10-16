"""Decode PDFs from ItemLabelTests independently. Requires pypdfium2, numpy, zxing-cpp.

Run integration label tests first, then: python3 tests/verify_item_labels.py
Use a disposable Python virtual environment; no application/runtime dependency.
"""
from pathlib import Path
import pypdfium2
import zxingcpp

artifacts = Path(__file__).resolve().parent.parent / 'artifacts'
expected = (artifacts / 'item-label-code.txt').read_text()
for name in ('item-label-50x30.pdf', 'item-label-60x40.pdf', 'item-label-70x50.pdf', 'phone-label-50x30.pdf'):
    with pypdfium2.PdfDocument(artifacts / name) as document:
        for page_number in range(len(document)):
            page = document[page_number]
            for dpi in (203, 300):
                bitmap = page.render(scale=dpi / 72)
                code = zxingcpp.read_barcode(bitmap.to_numpy())
                assert code and code.text.startswith('INVORA:') and len(code.text) == 41, (name, page_number, dpi)
                if name.startswith('item-label-'):
                    assert code.text == expected, (name, page_number, dpi, 'Wrong product')
                bitmap.close()
            page.close()
        print(f'{name}: {len(document)} pages decoded at 203 and 300 dpi')
