"""Template contract tests; run with the bundled Python or the Agent image Python."""

from copy import deepcopy
import importlib.util
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from zipfile import ZipFile

from lxml import etree

ROOT = Path(__file__).parent / "templates" / "announcement"
spec = importlib.util.spec_from_file_location("announcement", ROOT / "build.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class AnnouncementTests(unittest.TestCase):
    def setUp(self):
        self.content = json.loads((ROOT / "example.json").read_text(encoding="utf-8"))

    def test_preserves_package_and_branding_while_filling_all_slots(self):
        with TemporaryDirectory() as folder:
            output = Path(folder) / "announcement.docx"
            module.build(self.content, output)
            with ZipFile(ROOT / "template.docx") as template, ZipFile(output) as result:
                self.assertEqual(template.namelist(), result.namelist())
                for part in template.namelist():
                    if part != "word/document.xml":
                        self.assertEqual(template.read(part), result.read(part), part)
                text = result.read("word/document.xml").decode("utf-8")
                self.assertNotIn("{{", text)
                self.assertIn(self.content["title"], text)

    def test_variable_sections_items_and_multiline_without_sample_data(self):
        self.content["sections"] = [{"heading": f"段落 {i}", "body": "第一行\n第二行", "items": [f"項目 {j}" for j in range(7)]} for i in range(6)]
        self.content.pop("date")
        self.content.pop("closing")
        self.content.pop("contact")
        with TemporaryDirectory() as folder:
            output = Path(folder) / "announcement.docx"
            module.build(self.content, output)
            with ZipFile(output) as result:
                root = etree.fromstring(result.read("word/document.xml"))
                ns = module.NS
                texts = root.xpath("//w:t/text()", namespaces=ns)
                self.assertEqual(6, texts.count("項目 6"))
                self.assertEqual(6, len(root.xpath("//w:br", namespaces=ns)))
                self.assertNotIn("{{", "".join(texts))
                self.assertNotIn("問卷抽獎", "".join(texts))
                self.assertNotIn("@web-pro.com.tw", "".join(texts))

    def test_xml_special_characters_are_preserved_as_text(self):
        self.content["title"] = "研發 <測試> & 驗證"
        with TemporaryDirectory() as folder:
            output = Path(folder) / "announcement.docx"
            module.build(self.content, output)
            with ZipFile(output) as result:
                root = etree.fromstring(result.read("word/document.xml"))
                self.assertIn(self.content["title"], root.xpath("//w:t/text()", namespaces=module.NS))

    def test_invalid_data_cannot_silently_discard_content(self):
        for change in ({"title": ""}, {"sections": []}, {"sections": [{"items": "wrong"}]}, {"attachments": ["missing.pdf"]}):
            data = deepcopy(self.content)
            data.update(change)
            with self.assertRaises(ValueError):
                module.validate(data)

    def test_template_cannot_be_overwritten(self):
        with self.assertRaises(ValueError):
            module.build(self.content, ROOT / "template.docx")


if __name__ == "__main__":
    unittest.main()
