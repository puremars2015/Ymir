"""Fill the platform announcement template without rebuilding its package/layout."""

import argparse
from copy import deepcopy
import json
from pathlib import Path
from zipfile import ZipFile

from lxml import etree

NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
W = "{" + NS["w"] + "}"
TEMPLATE = Path(__file__).with_name("template.docx")


def set_text(paragraph, value):
    """Keep the reference run properties; add real line breaks for multiline text."""
    nodes = paragraph.findall(".//" + W + "t")
    first = next(node for node in nodes if node.text)
    for node in nodes:
        node.text = ""
    lines = value.split("\n")
    first.text = lines[0]
    first.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
    anchor = first
    for line in lines[1:]:
        br = etree.Element(W + "br")
        anchor.addnext(br)
        text = etree.Element(W + "t")
        text.text = line
        text.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
        br.addnext(text)
        anchor = text


def validate(data):
    if not isinstance(data, dict):
        raise ValueError("Announcement must be a JSON object")
    allowed = {"department", "date", "title", "sections", "closing", "contact"}
    if set(data) - allowed:
        raise ValueError("Unknown announcement fields: " + ", ".join(sorted(set(data) - allowed)))
    for key in allowed - {"sections"}:
        if not isinstance(data.get(key, ""), str):
            raise ValueError(key + " must be text")
    for key in ("department", "title"):
        if not data.get(key, "").strip():
            raise ValueError(key + " is required")
    sections = data.get("sections")
    if not isinstance(sections, list) or not sections:
        raise ValueError("At least one section is required")
    for section in sections:
        if not isinstance(section, dict) or set(section) - {"heading", "body", "items"}:
            raise ValueError("Sections accept heading, body and items only")
        for key in ("heading", "body"):
            if not isinstance(section.get(key, ""), str):
                raise ValueError("Section " + key + " must be text")
        items = section.get("items", [])
        if not isinstance(items, list) or any(not isinstance(item, str) for item in items):
            raise ValueError("Section items must be a list of text")
        if not any(section.get(key, "").strip() for key in ("heading", "body")) and not any(item.strip() for item in items):
            raise ValueError("Empty sections are not allowed")


def build(data, output):
    validate(data)
    output = Path(output)
    if output.resolve() == TEMPLATE.resolve():
        raise ValueError("The shared template cannot be overwritten")
    if output.suffix.lower() != ".docx":
        raise ValueError("Output must end in .docx")
    with ZipFile(TEMPLATE) as source:
        root = etree.fromstring(source.read("word/document.xml"), etree.XMLParser(resolve_entities=False, no_network=True))
        slots = {}
        for p in root.findall(".//" + W + "p"):
            # Slots are deliberately single text nodes in the sanitized template.
            for node in p.findall(".//" + W + "t"):
                if node.text and node.text.startswith("{{"):
                    key = node.text.split("}}", 1)[0][2:]
                    slots["department" if key == "dept" else key] = p
        expected = {"department", "date", "title", "section_heading", "section_body", "section_item", "closing", "contact"}
        if set(slots) != expected:
            raise ValueError("Announcement template slot contract is invalid")

        prototypes = [slots[key] for key in ("section_heading", "section_body", "section_item")]
        parent = prototypes[0].getparent()
        if any(p.getparent() is not parent for p in prototypes):
            raise ValueError("Section prototypes must share a parent")
        position = parent.index(prototypes[0])
        for p in prototypes:
            parent.remove(p)
        for section in data["sections"]:
            values = [(prototypes[0], section.get("heading", "")), (prototypes[1], section.get("body", ""))]
            values.extend((prototypes[2], item) for item in section.get("items", []))
            for prototype, value in values:
                if not value.strip():
                    continue
                p = deepcopy(prototype)
                # Word uses these IDs for revision tracking; clones must not reuse them.
                for attribute in list(p.attrib):
                    if etree.QName(attribute).localname in ("paraId", "textId"):
                        del p.attrib[attribute]
                set_text(p, value)
                parent.insert(position, p)
                position += 1
        for key in ("department", "date", "title", "closing", "contact"):
            value = data.get(key, "")
            if key == "department":
                value += "公告"
            # Keep the footer cell's required paragraph even when optional text is absent.
            set_text(slots[key], value)

        document = etree.tostring(root, encoding="UTF-8", xml_declaration=True, standalone=True)
        output.parent.mkdir(parents=True, exist_ok=True)
        with ZipFile(output, "w") as destination:
            for info in source.infolist():
                destination.writestr(info, document if info.filename == "word/document.xml" else source.read(info.filename))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("content", type=Path, help="UTF-8 announcement JSON")
    parser.add_argument("output", type=Path, help="Final .docx in the execution deliverables directory")
    args = parser.parse_args()
    build(json.loads(args.content.read_text(encoding="utf-8")), args.output)
