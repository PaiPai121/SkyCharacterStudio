"""Shared, sourced character metadata; independent of mesh/bone detection."""
import json
import re
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[1] / 'assets'
CATALOGS = {
    'first': json.loads((ASSETS / 'character-ages.json').read_text(encoding='utf-8')),
    'second': json.loads((ASSETS / 'character-ages-2nd.json').read_text(encoding='utf-8')),
}

def age_info(model_id, is_base_game_character=False, edition='first', definition_label=None):
    catalog = CATALOGS[edition]
    if model_id in catalog:
        return catalog[model_id]
    # The caller supplies a definition row read from the selected game's name
    # table. A matching ID prefix or a scene alias alone does not establish
    # costume identity, especially for resources assembled from reused parts.
    if is_base_game_character and re.fullmatch(r'chr\d{4}_c\d{2}', model_id, re.I):
        person = catalog.get(model_id[:7])
        if person and person.get('name') and definition_label and any(
                definition_label.startswith(person['name'] + separator) for separator in ('：', ':')):
            return {**person, 'basis': f'游戏名称表将该服装定义为「{definition_label}」；{person["basis"]}'}
    return {'status': 'unknown', 'age': None,
        'basis': '该模型尚无经核对的年龄记录；胸部调整仅向已确认成年的模型开放。', 'source': ''}

def adult_eligible(model_id, is_base_game_character=False, edition='first', definition_label=None):
    info = age_info(model_id, is_base_game_character, edition, definition_label)
    return info['status'] == 'adult' and (info.get('age') is None or info['age'] >= 18)
