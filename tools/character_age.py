"""Shared, sourced character metadata; independent of mesh/bone detection."""
import json
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[1] / 'assets'
CATALOGS = {
    'first': json.loads((ASSETS / 'character-ages.json').read_text(encoding='utf-8')),
    'second': json.loads((ASSETS / 'character-ages-2nd.json').read_text(encoding='utf-8')),
}

def age_info(model_id, is_base_game_character=False, edition='first'):
    catalog = CATALOGS[edition]
    if model_id in catalog:
        return catalog[model_id]
    return {'status': 'unknown', 'age': None,
        'basis': '该模型尚无经核对的年龄记录；胸部调整仅向已确认成年的模型开放。', 'source': ''}

def adult_eligible(model_id, is_base_game_character=False, edition='first'):
    info = age_info(model_id, is_base_game_character, edition)
    return info['status'] == 'adult' and (info.get('age') is None or info['age'] >= 18)
