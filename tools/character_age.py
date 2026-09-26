"""Shared, sourced character metadata; independent of mesh/bone detection."""
import json
from pathlib import Path

CATALOG_PATH = Path(__file__).resolve().parents[1] / 'assets' / 'character-ages.json'
CATALOG = json.loads(CATALOG_PATH.read_text(encoding='utf-8'))

def age_info(model_id, is_base_game_character=False):
    if model_id in CATALOG:
        return CATALOG[model_id]
    return {'status': 'unknown', 'age': None,
        'basis': '尚无可核对的本作年龄资料', 'source': ''}

def adult_eligible(model_id, is_base_game_character=False):
    info = age_info(model_id, is_base_game_character)
    return info['status'] == 'adult' and (info.get('age') is None or info['age'] >= 18)
